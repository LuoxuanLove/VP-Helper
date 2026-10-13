using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

public class RanklandSyncServiceTests
{
    private sealed class FakeRawContentSource : IRawContentSource
    {
        private const string Config = """
root:
  children:
    - path: icpc
      name: ICPC
      children:
        - path: icpc2024
          name: ICPC 2024
          children:
            - path: icpc2024nanjing
              format: srk.json
              name: ICPC 2024 Nanjing
            - path: icpc2024shanghai
              format: srk.json
              name: ICPC 2024 Shanghai
""";

        private const string Srk = """
{
  "contest": {
    "title": "ICPC 2024 Nanjing",
    "startAt": "2024-11-03T09:00:00+08:00",
    "duration": [5, "h"]
  },
  "problems": [
    { "alias": "A", "title": "Warmup", "statistics": { "accepted": 30, "submitted": 40 } },
    { "alias": "B", "title": "Hard", "statistics": { "accepted": 5, "submitted": 20 } }
  ],
  "rows": []
}
""";

        public Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default)
        {
            if (url == RanklandPaths.ConfigUrl)
                return Task.FromResult<RawContent?>(new RawContent(url, Config, null, false));

            if (url.EndsWith("/icpc2024nanjing.srk.json", System.StringComparison.Ordinal))
                return Task.FromResult<RawContent?>(new RawContent(url, Srk, null, false));

            return Task.FromResult<RawContent?>(null);
        }
    }

    [Fact]
    public async Task SyncAsync_BoundedRun_StillPersistsFullOfficialCatalog()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        await using var db = new VpDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = new RanklandSyncService(db, new RanklandClient(new FakeRawContentSource()));

        var detailed = await service.SyncAsync(maxContests: 1);

        Assert.Equal(1, detailed);
        Assert.Equal(2, await db.OfficialContests.CountAsync());

        var nanjing = await db.OfficialContests.FindAsync("icpc2024nanjing");
        var shanghai = await db.OfficialContests.FindAsync("icpc2024shanghai");

        Assert.NotNull(nanjing);
        Assert.Equal(2, nanjing!.ProblemCount);
        Assert.True(nanjing.FetchedAt > 0);

        var problemB = await db.OfficialProblems.FindAsync("icpc2024nanjing", "B");
        Assert.NotNull(problemB);
        Assert.Equal("Hard", problemB!.Title);
        Assert.Equal(1, problemB.SortOrder);
        Assert.Equal(5, problemB.AcceptedCount);
        Assert.Equal(20, problemB.SubmittedCount);

        Assert.NotNull(shanghai);
        Assert.Equal(0, shanghai!.ProblemCount);
        Assert.Equal("ICPC 2024 Shanghai", shanghai.Name);
        Assert.EndsWith("/official/icpc/icpc2024/icpc2024shanghai.srk.json", shanghai.SrkUrl);
    }

    [Fact]
    public async Task GetCatalogLeavesAsync_ReturnsCurrentConfigLeavesWithoutPersisting()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        await using var db = new VpDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = new RanklandSyncService(db, new RanklandClient(new FakeRawContentSource()));

        var leaves = await service.GetCatalogLeavesAsync();

        Assert.Equal(2, leaves.Count);
        Assert.Empty(await db.OfficialContests.ToListAsync());
    }
    [Fact]
    public async Task RefreshExistingStanding_ReplacesProblemsWithoutTrackingConflict()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new RanklandSyncService(db, new RanklandClient(new FakeRawContentSource()));
        Assert.True(await service.SyncOneAsync("icpc2024nanjing", forceRefresh: true));
        Assert.True(await service.SyncOneAsync("icpc2024nanjing", forceRefresh: true));
        Assert.Equal(2, await db.OfficialProblems.CountAsync());
        Assert.Equal(2, await db.OfficialContests.CountAsync());
    }

    [Fact]
    public async Task PostSync_AutomaticallyMapsKnownQojContestAndLoadsProblems()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        db.GymContests.Add(new VPHelper.Core.Models.GymContest { Platform = VPHelper.Core.Models.Platform.Qoj, GymId = 1828, ProblemCount = 13 });
        db.Submissions.Add(new VPHelper.Core.Models.Submission { Platform = VPHelper.Core.Models.Platform.Qoj, Id = 1, GymId = 1828, Handle = "alice" });
        await db.SaveChangesAsync();
        var result = await new PostSyncService(db, new RanklandClient(new FakeRawContentSource()))
            .RunAsync(VPHelper.Core.Models.Platform.Qoj, "alice");
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Updated);
        Assert.Equal("icpc2024nanjing", (await db.ContestMappings.SingleAsync()).OfficialId);
        Assert.Equal(2, await db.OfficialProblems.CountAsync());
    }

    [Fact]
    public async Task FailedDownload_KeepsExistingDataAndReportsFailure()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        db.OfficialStandings.Add(new VPHelper.Core.Models.OfficialStanding { OfficialId = "icpc2024shanghai", TeamName = "retained" });
        await db.SaveChangesAsync();
        var service = new RanklandSyncService(db, new RanklandClient(new FakeRawContentSource()));
        Assert.False(await service.SyncOneAsync("icpc2024shanghai", forceRefresh: true));
        Assert.Equal("retained", (await db.OfficialStandings.SingleAsync()).TeamName);
        Assert.Contains("icpc2024shanghai", service.FailedContests);
    }

}
