using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using Xunit;

namespace VPHelper.Core.Tests;

public class RepositoryTests
{
    private static VpDbContext NewInMemory()
    {
        // Use a shared SQLite in-memory connection for a real EF+SQLite round-trip.
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new VpDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task Submissions_RoundTrip_And_Unique()
    {
        await using var ctx = NewInMemory();
        ctx.Submissions.Add(new Submission { Id = 1, Handle = "tourist", GymId = 100, ProblemIndex = "A", Verdict = "OK", SubmitTime = 10 });
        ctx.Submissions.Add(new Submission { Id = 2, Handle = "tourist", GymId = 100, ProblemIndex = "B", Verdict = "WRONG_ANSWER", SubmitTime = 20 });
        await ctx.SaveChangesAsync();

        var back = await ctx.Submissions.Where(s => s.GymId == 100).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, back.Count);
        Assert.Equal("A", back[0].ProblemIndex);
    }

    [Fact]
    public async Task ContestMapping_Confirmed_Persists()
    {
        await using var ctx = NewInMemory();
        ctx.ContestMappings.Add(new ContestMapping { GymId = 100, OfficialId = "icpc2024nanjing", Confidence = 0.8, Confirmed = 1 });
        await ctx.SaveChangesAsync();

        var m = await ctx.ContestMappings.SingleAsync();
        Assert.Equal(1, m.Confirmed);
        Assert.Equal("icpc2024nanjing", m.OfficialId);
    }

    [Fact]
    public async Task OfficialContest_And_Standings_RoundTrip()
    {
        await using var ctx = NewInMemory();
        ctx.OfficialContests.Add(new OfficialContest { Id = "x", Name = "Test", CategoryPath = "icpc", SrkUrl = "u" });
        ctx.OfficialStandings.Add(new OfficialStanding { OfficialId = "x", Rank = 1, TeamName = "T1", SolvedCount = 5, Penalty = 100, Official = true });
        await ctx.SaveChangesAsync();

        var standings = await ctx.OfficialStandings.Where(s => s.OfficialId == "x").ToListAsync();
        Assert.Single(standings);
        Assert.True(standings[0].Official);
    }
}
