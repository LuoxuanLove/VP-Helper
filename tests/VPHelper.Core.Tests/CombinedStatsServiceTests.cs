using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Combined multi-account VP stats: each gym counted once, VP and practice kept separate, and
/// multi-account VP treated as a conflict the user resolves. Uses a real EF+SQLite round-trip so
/// the composite (Handle, GymId, Kind) key and the selection upsert are exercised for real.
/// </summary>
public class CombinedStatsServiceTests
{
    private static VpDbContext NewInMemory()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        var ctx = new VpDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static VpResult Vp(string handle, long gym, ParticipationKind kind, int solved, long penalty)
        => new() { Handle = handle, GymId = gym, Kind = kind, SolvedCount = solved, Penalty = penalty };

    [Fact]
    public async Task SingleAccountPerGym_AutoCounts_AndSumsSolved()
    {
        await using var db = NewInMemory();
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Virtual, 4, 0));
        db.VpResults.Add(Vp("alice", 200, ParticipationKind.Virtual, 3, 0));
        // A practice-only gym must never contribute to the VP total.
        db.VpResults.Add(Vp("alice", 300, ParticipationKind.Practice, 5, 0));
        await db.SaveChangesAsync();

        var svc = new CombinedStatsService(db);
        Assert.Equal(7, await svc.GetCombinedSolvedAsync());
        Assert.False(await svc.HasUnresolvedConflictsAsync());
    }

    [Fact]
    public async Task PracticeAndVirtual_SameGymSameAccount_OnlyVirtualCounts()
    {
        await using var db = NewInMemory();
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Virtual, 4, 0));
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Practice, 6, 0)); // upsolve, excluded
        await db.SaveChangesAsync();

        var svc = new CombinedStatsService(db);
        Assert.Equal(4, await svc.GetCombinedSolvedAsync());
    }

    [Fact]
    public async Task SameHandleAcrossPlatforms_ProducesDistinctChoices()
    {
        await using var db = NewInMemory();
        db.VpResults.Add(new VpResult
        {
            Platform = Platform.Codeforces, Handle = "same", GymId = 100,
            Kind = ParticipationKind.Virtual, SolvedCount = 7,
        });
        db.VpResults.Add(new VpResult
        {
            Platform = Platform.Qoj, Handle = "same", GymId = 100,
            Kind = ParticipationKind.Virtual, SolvedCount = 3,
        });
        await db.SaveChangesAsync();

        var choices = await new CombinedStatsService(db).GetChosenPerGymAsync();

        Assert.Equal(2, choices.Count);
        Assert.Contains(choices, c => c.Platform == Platform.Codeforces && c.SolvedCount == 7);
        Assert.Contains(choices, c => c.Platform == Platform.Qoj && c.SolvedCount == 3);

        var byAccount = await new CombinedStatsService(db).GetSolvedByAccountAsync();
        Assert.Equal(7, byAccount[(Platform.Codeforces, "same")]);
        Assert.Equal(3, byAccount[(Platform.Qoj, "same")]);
    }

    [Fact]
    public async Task SubmissionCounts_UseRawRows_AndKeepPlatformsDistinct()
    {
        await using var db = NewInMemory();
        db.Accounts.AddRange(
            new Account { Platform = Platform.Codeforces, Handle = "same", TotalSubmissions = 99 },
            new Account { Platform = Platform.Qoj, Handle = "same", TotalSubmissions = 0 });
        db.Submissions.AddRange(
            new Submission { Platform = Platform.Codeforces, Id = 1, Handle = "same", GymId = 100, ProblemIndex = "A", Verdict = "OK" },
            new Submission { Platform = Platform.Codeforces, Id = 2, Handle = "same", GymId = 100, ProblemIndex = "B", Verdict = "WRONG_ANSWER" },
            new Submission { Platform = Platform.Qoj, Id = 1, Handle = "same", GymId = 200, ProblemIndex = "A", Verdict = "Accepted" });
        await db.SaveChangesAsync();

        var counts = await new CombinedStatsService(db).GetSubmissionCountByAccountAsync();

        Assert.Equal(2, counts[(Platform.Codeforces, "same")]);
        Assert.Equal(1, counts[(Platform.Qoj, "same")]);
    }

    [Fact]
    public async Task MultiAccountVp_SameGym_IsUnresolvedConflict_ExcludedUntilChosen()
    {
        await using var db = NewInMemory();
        db.GymContests.Add(new GymContest { GymId = 100, Title = "Gym 100" });
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Virtual, 4, 1000));
        db.VpResults.Add(Vp("bob", 100, ParticipationKind.Virtual, 5, 2000));
        await db.SaveChangesAsync();

        var svc = new CombinedStatsService(db);

        // Unresolved: the conflicted gym is excluded from the total.
        Assert.True(await svc.HasUnresolvedConflictsAsync());
        Assert.Equal(0, await svc.GetCombinedSolvedAsync());

        var conflicts = await svc.GetConflictsAsync();
        var c = Assert.Single(conflicts);
        Assert.Equal(100, c.GymId);
        Assert.Equal("Gym 100", c.GymTitle);
        Assert.True(c.IsUnresolved);
        // Candidates ordered best-first: bob (5 solved) before alice (4 solved).
        Assert.Equal("bob", c.Candidates[0].Handle);
        Assert.Equal("alice", c.Candidates[1].Handle);

        // Resolve to alice: now counted, and no longer unresolved.
        await svc.SelectAsync(Platform.Codeforces, 100, "alice");
        Assert.False(await svc.HasUnresolvedConflictsAsync());
        Assert.Equal(4, await svc.GetCombinedSolvedAsync());

        // Re-select bob (replace old choice): total switches.
        await svc.SelectAsync(Platform.Codeforces, 100, "bob");
        Assert.Equal(5, await svc.GetCombinedSolvedAsync());
    }

    [Fact]
    public async Task MixedGyms_ConflictExcluded_UnconflictedStillCount()
    {
        await using var db = NewInMemory();
        // Conflicted gym.
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Virtual, 4, 0));
        db.VpResults.Add(Vp("bob", 100, ParticipationKind.Virtual, 5, 0));
        // Non-conflicted gyms.
        db.VpResults.Add(Vp("alice", 200, ParticipationKind.Virtual, 2, 0));
        db.VpResults.Add(Vp("bob", 300, ParticipationKind.Virtual, 3, 0));
        await db.SaveChangesAsync();

        var svc = new CombinedStatsService(db);
        // Only the two unconflicted gyms count (2 + 3); gym 100 excluded until chosen.
        Assert.Equal(5, await svc.GetCombinedSolvedAsync());

        await svc.SelectAsync(Platform.Codeforces, 100, "bob");
        Assert.Equal(10, await svc.GetCombinedSolvedAsync()); // + 5
    }

    [Fact]
    public async Task RemoveAccount_DeletesItsDataAndDissolvesConflict()
    {
        await using var db = NewInMemory();
        db.Accounts.Add(new Account { Handle = "alice" });
        db.Accounts.Add(new Account { Handle = "bob" });
        db.Submissions.Add(new Submission { Id = 1, Handle = "bob", GymId = 100, ProblemIndex = "A", Verdict = "OK", SubmitTime = 10, ParticipantType = "VIRTUAL" });
        db.VpResults.Add(Vp("alice", 100, ParticipationKind.Virtual, 4, 0));
        db.VpResults.Add(Vp("bob", 100, ParticipationKind.Virtual, 5, 0));
        await db.SaveChangesAsync();

        var svc = new CombinedStatsService(db);
        await svc.SelectAsync(Platform.Codeforces, 100, "bob");
        Assert.Equal(5, await svc.GetCombinedSolvedAsync());

        // Removing bob deletes his submissions/results/selection; gym 100 falls back to alice alone.
        await svc.RemoveAccountAsync(Platform.Codeforces, "bob");
        Assert.Empty(await db.Submissions.Where(s => s.Handle == "bob").ToListAsync());
        Assert.Empty(await db.VpResults.Where(v => v.Handle == "bob").ToListAsync());
        Assert.Empty(await db.GymVpSelections.ToListAsync());
        Assert.Null(await db.Accounts.FindAsync(Platform.Codeforces, "bob"));

        Assert.False(await svc.HasUnresolvedConflictsAsync());
        Assert.Equal(4, await svc.GetCombinedSolvedAsync()); // alice's run now sole candidate
    }
}
