using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;
using VPHelper.WinUI.ViewModels;
using Xunit;

namespace VPHelper.Core.Tests;

public class GymDirectoryViewModelTests
{
    [Fact]
    public async Task CachedDirectory_FiltersAndReloadsParticipationWithoutNetwork()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(connection).Options;
        await using var db = new VpDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.OfficialContests.Add(new OfficialContest { Id = "icpc2023macau", Name = "2023 ICPC Macau", CategoryPath = "icpc" });
        db.ContestMappings.Add(new ContestMapping { GymId = 123, OfficialId = "icpc2023macau", Confirmed = MappingStatus.ManualConfirmed });
        db.VpResults.Add(new VpResult { GymId = 123, Handle = "alice", Kind = ParticipationKind.Virtual, SolvedCount = 0 });
        await db.SaveChangesAsync();
        var source = new OfflineSource();
        var vm = new GymDirectoryViewModel(new Factory(options), new RanklandClient(source));
        await vm.LoadAsync();
        Assert.Equal(0, source.Calls);
        vm.SearchText = "icpc2023macau";
        var row = Assert.Single(vm.Items);
        Assert.Contains("其中已 VP 1 场", vm.Summary);
        Assert.Equal("已 VP", row.ParticipationText);
        Assert.Equal("待加载榜单", row.RankText);
        vm.ParticipationFilter = 2;
        Assert.Empty(vm.Items);
        Assert.True(vm.ShowEmpty);
        Assert.Contains("其中已 VP 0 场", vm.Summary);
        vm.ParticipationFilter = 1;
        Assert.Single(vm.Items);
        db.OfficialStandings.Add(new OfficialStanding { OfficialId = "icpc2023macau", SolvedCount = 3, Official = true });
        await db.SaveChangesAsync();
        await vm.LoadAsync();
        Assert.Equal("#2 / 1", Assert.Single(vm.Items).RankText);
        Assert.Equal(0, source.Calls);
        await vm.LoadAsync(refreshCatalog: true);
        Assert.True(vm.HasWarning);
        Assert.Equal("#2 / 1", Assert.Single(vm.Items).RankText);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task MergedOfficialAliases_RetainParticipationFromBothStandings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(connection).Options;
        await using var db = new VpDbContext(options);
        await db.Database.EnsureCreatedAsync();
        foreach (var (id, gym) in new[] { ("hbcpc7th", 11L), ("icpc2026invitational-wuhan", 12L) })
        {
            db.OfficialContests.Add(new OfficialContest { Id = id, Name = id, CategoryPath = "Provincial" });
            db.ContestMappings.Add(new ContestMapping { GymId = gym, OfficialId = id, Confirmed = MappingStatus.ManualConfirmed });
            db.VpResults.Add(new VpResult { GymId = gym, Handle = "alice", Kind = ParticipationKind.Virtual, SolvedCount = 2 });
        }
        await db.SaveChangesAsync();
        var vm = new GymDirectoryViewModel(new Factory(options), new RanklandClient(new OfflineSource()));
        await vm.LoadAsync();
        var merged = Assert.Single(vm.Items.Where(i => i.OfficialIds.Contains("hbcpc7th")));
        Assert.Contains("icpc2026invitational-wuhan", merged.OfficialIds);
        Assert.Equal(2, merged.Runs.Count);
        Assert.True(merged.HasVp);
    }

    private sealed class Factory(DbContextOptions<VpDbContext> options) : IDbContextFactory<VpDbContext>
    {
        public VpDbContext CreateDbContext() => new(options);
    }
    private sealed class OfflineSource : IRawContentSource
    {
        public int Calls { get; private set; }
        public Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default)
        {
            Calls++;
            throw new HttpRequestException("Offline test");
        }
    }
}
