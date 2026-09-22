using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

public class OfficialParticipationServiceTests
{
    [Fact]
    public async Task Projection_KeepsPlatformsAccountsAndPracticeSeparate_AndIncludesZeroSolveVp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.ContestMappings.AddRange(
            new ContestMapping { Platform = Platform.Codeforces, GymId = 42, OfficialId = "cf", Confirmed = MappingStatus.AutoConfirmed },
            new ContestMapping { Platform = Platform.Qoj, GymId = 42, OfficialId = "qoj", Confirmed = MappingStatus.ManualConfirmed },
            new ContestMapping { Platform = Platform.Codeforces, GymId = 99, OfficialId = "cf", Confirmed = MappingStatus.Candidate });
        db.VpResults.AddRange(
            Run(Platform.Codeforces, 42, "alice", ParticipationKind.Virtual, 3, 100),
            Run(Platform.Codeforces, 42, "bob", ParticipationKind.Virtual, 0, 0),
            Run(Platform.Codeforces, 42, "alice", ParticipationKind.Practice, 8, 50),
            Run(Platform.Qoj, 42, "alice", ParticipationKind.Virtual, 5, 200),
            Run(Platform.Codeforces, 99, "alice", ParticipationKind.Virtual, 9, 0));
        db.OfficialStandings.AddRange(
            Standing("cf", 4, 200), Standing("cf", 3, 100), Standing("cf", 3, 101),
            Standing("cf", 8, 0, official: false));
        await db.SaveChangesAsync();

        var rows = await new OfficialParticipationService(db).LoadAsync();
        Assert.Equal(5, rows.Count);
        var vp = Assert.Single(rows.Where(r => r.Platform == Platform.Codeforces && r.GymId == 42 && r.Handle == "alice" && r.Kind == ParticipationKind.Virtual));
        Assert.Equal(2, vp.Rank); // A tie shares the higher rank; unofficial teams are excluded.
        Assert.Equal(3, vp.TotalTeams);
        Assert.Equal(4, Assert.Single(rows.Where(r => r.Handle == "bob")).Rank);
        Assert.Null(Assert.Single(rows.Where(r => r.Kind == ParticipationKind.Practice)).Rank);
        var qoj = Assert.Single(rows.Where(r => r.Platform == Platform.Qoj));
        Assert.Equal("qoj", qoj.OfficialId);
        Assert.Null(qoj.Rank); // Missing standings must never turn into rank one.
        var candidate = Assert.Single(rows.Where(r => r.GymId == 99));
        Assert.Empty(candidate.OfficialId);
        Assert.Null(candidate.Rank);
    }

    [Fact]
    public async Task ManualMappingWins_AndNoOfficialFlagsFallsBackToAllTeams()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VpDbContext(new DbContextOptionsBuilder<VpDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.ContestMappings.AddRange(
            new ContestMapping { GymId = 7, OfficialId = "auto", Confirmed = MappingStatus.AutoConfirmed, Confidence = 1 },
            new ContestMapping { GymId = 7, OfficialId = "manual", Confirmed = MappingStatus.ManualConfirmed, Confidence = .5 });
        db.VpResults.Add(Run(Platform.Codeforces, 7, "alice", ParticipationKind.Virtual, 2, 200));
        db.OfficialStandings.AddRange(Standing("manual", 3, 100, false), Standing("manual", 2, 300, false));
        await db.SaveChangesAsync();
        var row = Assert.Single(await new OfficialParticipationService(db).LoadAsync());
        Assert.Equal("manual", row.OfficialId);
        Assert.Equal(2, row.Rank);
        Assert.Equal(2, row.TotalTeams);
    }

    private static VpResult Run(Platform platform, long gym, string handle, ParticipationKind kind, int solved, long penalty)
        => new() { Platform = platform, GymId = gym, Handle = handle, Kind = kind, SolvedCount = solved, Penalty = penalty };
    private static OfficialStanding Standing(string id, int solved, long penalty, bool official = true)
        => new() { OfficialId = id, SolvedCount = solved, Penalty = penalty, Official = official, HadValidSubmission = true };
}
