using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;
using VPHelper.Core.Srk;
using Xunit;

namespace VPHelper.Core.Tests;

public class ServiceTests
{
    private static VpDbContext NewInMemory()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        var ctx = new VpDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public void RankRows_AlignsToOriginalOrder_AndTiesShareRank()
    {
        var rl = new SrkRanklist();
        // rows in arbitrary order; ranks must be by (solved desc, penalty asc)
        rl.Rows.Add(new SrkRow { SolvedValue = 3, PenaltySeconds = 300 }); // idx0 -> rank 2
        rl.Rows.Add(new SrkRow { SolvedValue = 5, PenaltySeconds = 100 }); // idx1 -> rank 1
        rl.Rows.Add(new SrkRow { SolvedValue = 3, PenaltySeconds = 300 }); // idx2 -> tie rank 2
        rl.Rows.Add(new SrkRow { SolvedValue = 1, PenaltySeconds = 999 }); // idx3 -> rank 4

        var ranks = RankingEngine.RankRows(rl);
        Assert.Equal(1, ranks[1]);
        Assert.Equal(2, ranks[0]);
        Assert.Equal(2, ranks[2]);
        Assert.Equal(4, ranks[3]); // tie skips rank 3
    }

    [Fact]
    public void EstimateFromStandings_DefaultLine_UsesValidSubmissionFlag()
    {
        var svc = new MappingService(NewInMemory());
        // 10 official teams: 8 with valid submissions, 2 pure-zero.
        var standings = new List<OfficialStanding>();
        for (int i = 0; i < 8; i++)
            standings.Add(new OfficialStanding { Official = true, SolvedCount = 5 - i / 2, Penalty = 100 * i, HadValidSubmission = true });
        for (int i = 0; i < 2; i++)
            standings.Add(new OfficialStanding { Official = true, SolvedCount = 0, Penalty = 0, HadValidSubmission = false });

        // VP with 6 solved beats everyone -> rank 1. N=8 -> gold ceil(0.8)=1.
        var est = svc.EstimateFromStandings(6, 0, standings, null);
        Assert.Equal(1, est.Rank);
        Assert.Equal(1, est.MedalLine.Gold);       // ceil(8*0.1)=1
        Assert.Equal(3, est.MedalLine.Silver);      // ceil(8*0.3)=3
        Assert.Equal(5, est.MedalLine.Bronze);      // ceil(8*0.6)=5
        Assert.Equal(Medal.Gold, est.Medal);
    }

    [Fact]
    public void EstimateFromStandings_OfficialCounts_TakePriority()
    {
        var svc = new MappingService(NewInMemory());
        var standings = Enumerable.Range(0, 20)
            .Select(i => new OfficialStanding { Official = true, SolvedCount = 10 - i / 3, Penalty = 50 * i, HadValidSubmission = true })
            .ToList();
        // official counts [2,4,6] -> cumulative gold 2 / silver 6 / bronze 12
        var est = svc.EstimateFromStandings(11, 0, standings, new[] { 2, 4, 6 });
        Assert.Equal(2, est.MedalLine.Gold);
        Assert.Equal(6, est.MedalLine.Silver);
        Assert.Equal(12, est.MedalLine.Bronze);
        Assert.Equal(1, est.Rank);
        Assert.Equal(Medal.Gold, est.Medal);
    }

    [Fact]
    public void RankPersistedStandings_UsesSameOfficialTeamSetAsVpEstimate()
    {
        var rows = new List<OfficialStanding>
        {
            new() { Rank = 1, TeamName = "Champion", Official = true, SolvedCount = 8, Penalty = 100 },
            new() { Rank = 2, TeamName = "Unofficial Guest", Official = false, SolvedCount = 7, Penalty = 90 },
            new() { Rank = 3, TeamName = "Silver", Official = true, SolvedCount = 7, Penalty = 120 },
            new() { Rank = 4, TeamName = "Bronze", Official = true, SolvedCount = 6, Penalty = 200 },
        };

        var ranked = RankingEngine.RankPersistedStandings(rows);

        Assert.Equal(new[] { "Champion", "Silver", "Bronze" }, ranked.Select(r => r.Standing.TeamName));
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Rank));

        var svc = new MappingService(NewInMemory());
        var est = svc.EstimateFromStandings(7, 110, rows, null);
        Assert.Equal(2, est.Rank);
        Assert.Equal(3, est.TotalTeams);
    }

    [Fact]
    public async Task MappingService_ConfirmThenReuse_Persists()
    {
        await using var ctx = NewInMemory();
        ctx.GymContests.Add(new GymContest { GymId = 500, Title = "Gym 500", ProblemCount = 10 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "off1", Name = "Some Regional", CategoryPath = "icpc", ProblemCount = 10, SrkUrl = "u" });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        await svc.ConfirmAsync(500, "off1");
        var confirmed = await svc.GetConfirmedOfficialAsync(500);
        Assert.Equal("off1", confirmed);

        // Only one mapping remains, confirmed.
        Assert.Single(ctx.ContestMappings);
        Assert.Equal(1, ctx.ContestMappings.Single().Confirmed);
    }

    [Fact]
    public async Task MappingService_Clear_RemovesAllRowsForGym()
    {
        await using var ctx = NewInMemory();
        ctx.ContestMappings.Add(new ContestMapping { GymId = 500, OfficialId = "off1", Confidence = 1, Confirmed = MappingStatus.ManualConfirmed });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 500, OfficialId = "off2", Confidence = 0.4, Confirmed = MappingStatus.Candidate });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 501, OfficialId = "off3", Confidence = 1, Confirmed = MappingStatus.ManualConfirmed });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        await svc.ClearAsync(500);

        Assert.Null(await svc.GetConfirmedOfficialAsync(500));
        Assert.Single(ctx.ContestMappings);
        Assert.Equal(501, ctx.ContestMappings.Single().GymId);
    }

    [Fact]
    public async Task MappingService_PlatformKeys_DoNotCrossBindSameNumericContestId()
    {
        await using var ctx = NewInMemory();
        ctx.GymContests.Add(new GymContest { Platform = Platform.Codeforces, GymId = 3789, Title = "CF 3789", ProblemCount = 3 });
        ctx.GymContests.Add(new GymContest { Platform = Platform.Qoj, GymId = 3789, Title = "QOJ 3789", ProblemCount = 12 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "cf-off", Name = "CF Official", CategoryPath = "icpc", SrkUrl = "u" });
        ctx.OfficialContests.Add(new OfficialContest { Id = "sccpc18th", Name = "2026 四川省赛", CategoryPath = "provincial", SrkUrl = "u" });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        await svc.ConfirmAsync(Platform.Codeforces, 3789, "cf-off");
        await svc.ConfirmAsync(Platform.Qoj, 3789, "sccpc18th");

        Assert.Equal("cf-off", await svc.GetConfirmedOfficialAsync(Platform.Codeforces, 3789));
        Assert.Equal("sccpc18th", await svc.GetConfirmedOfficialAsync(Platform.Qoj, 3789));

        await svc.ClearAsync(Platform.Qoj, 3789);
        Assert.Equal("cf-off", await svc.GetConfirmedOfficialAsync(Platform.Codeforces, 3789));
        Assert.Null(await svc.GetConfirmedOfficialAsync(Platform.Qoj, 3789));
    }

    [Fact]
    public async Task AutoMatchAll_QojCuratedContest_IsAutoConfirmed()
    {
        await using var ctx = NewInMemory();
        ctx.GymContests.Add(new GymContest
        {
            Platform = Platform.Qoj,
            GymId = 3789,
            Title = "The 18th Sichuan Provincial Collegiate Programming Contest",
            ProblemCount = 12,
            StartTime = 1,
        });
        ctx.OfficialContests.Add(new OfficialContest
        {
            Id = "sccpc18th",
            Name = "2026 四川省赛",
            CategoryPath = "provincial",
            ProblemCount = 12,
            SrkUrl = "u",
        });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        var summary = await svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.AutoConfirmed);
        Assert.Equal("sccpc18th", await svc.GetConfirmedOfficialAsync(Platform.Qoj, 3789));
        var overview = await svc.GetOverviewAsync();
        Assert.Single(overview);
        Assert.Equal(Platform.Qoj, overview[0].Platform);
        Assert.Equal(MappingStatus.AutoConfirmed, overview[0].Status);
    }

    [Fact]
    public async Task EstimateAsync_Qoj_UsesQojVirtualResult()
    {
        await using var ctx = NewInMemory();
        ctx.OfficialContests.Add(new OfficialContest { Id = "off", Name = "Official", CategoryPath = "provincial", SrkUrl = "u" });
        ctx.OfficialStandings.Add(new OfficialStanding { OfficialId = "off", Rank = 1, TeamName = "A", SolvedCount = 8, Penalty = 100, Official = true, HadValidSubmission = true });
        ctx.OfficialStandings.Add(new OfficialStanding { OfficialId = "off", Rank = 2, TeamName = "B", SolvedCount = 5, Penalty = 500, Official = true, HadValidSubmission = true });
        ctx.VpResults.Add(new VpResult { Platform = Platform.Qoj, Handle = "qoj-user", GymId = 99, Kind = ParticipationKind.Virtual, SolvedCount = 6, Penalty = 300 });
        ctx.ContestMappings.Add(new ContestMapping { Platform = Platform.Qoj, GymId = 99, OfficialId = "off", Confidence = 1, Confirmed = MappingStatus.ManualConfirmed });
        await ctx.SaveChangesAsync();

        var estimate = await new MappingService(ctx).EstimateAsync(Platform.Qoj, "qoj-user", 99);

        Assert.NotNull(estimate);
        Assert.Equal(2, estimate!.Rank);
        Assert.Equal(6, estimate.Solved);
    }

    [Fact]
    public async Task AutoMatchAll_NonCuratedGymsStayUnmapped_AndClearOldFuzzyRows()
    {
        await using var ctx = NewInMemory();
        // Gym A has an exact-name, same-time, same-problem-count official → high confidence.
        ctx.GymContests.Add(new GymContest { GymId = 1, Title = "2023 ICPC Asia Hangzhou Regional", StartTime = 1_700_000_000, ProblemCount = 13 });
        // Gym B matches nothing well → low confidence.
        ctx.GymContests.Add(new GymContest { GymId = 2, Title = "Random Practice Set XYZ", StartTime = 900_000_000, ProblemCount = 5 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "hz2023", Name = "2023 ICPC Asia Hangzhou Regional", CategoryPath = "icpc", StartAt = 1_700_000_050, ProblemCount = 13, SrkUrl = "u" });
        ctx.OfficialContests.Add(new OfficialContest { Id = "other", Name = "2019 CCPC Final", CategoryPath = "ccpc", StartAt = 1_560_000_000, ProblemCount = 12, SrkUrl = "u" });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 1, OfficialId = "hz2023", Confidence = 0.97, Confirmed = MappingStatus.AutoConfirmed });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 2, OfficialId = "other", Confidence = 0.27, Confirmed = MappingStatus.Pending });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        var summary = await svc.AutoMatchAllAsync();

        Assert.Equal(2, summary.Total);
        Assert.Equal(0, summary.AutoConfirmed);
        Assert.Equal(0, summary.Pending);
        Assert.Equal(0, summary.SkippedManual);
        Assert.Equal(2, summary.NoCandidate);

        Assert.Empty(ctx.ContestMappings);
        Assert.Null(await svc.GetConfirmedOfficialAsync(1));
        Assert.Null(await svc.GetConfirmedOfficialAsync(2));
    }

    [Fact]
    public async Task AutoMatchAll_SkipsManuallyConfirmedGyms()
    {
        await using var ctx = NewInMemory();
        ctx.GymContests.Add(new GymContest { GymId = 1, Title = "2023 ICPC Asia Hangzhou Regional", StartTime = 1_700_000_000, ProblemCount = 13 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "hz2023", Name = "2023 ICPC Asia Hangzhou Regional", CategoryPath = "icpc", StartAt = 1_700_000_050, ProblemCount = 13, SrkUrl = "u" });
        ctx.OfficialContests.Add(new OfficialContest { Id = "manual", Name = "Totally Different", CategoryPath = "icpc", StartAt = 1, ProblemCount = 1, SrkUrl = "u" });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        // User pinned gym 1 to the deliberately-wrong "manual" official.
        await svc.ConfirmAsync(1, "manual");

        var summary = await svc.AutoMatchAllAsync();
        Assert.Equal(1, summary.SkippedManual);
        Assert.Equal(0, summary.AutoConfirmed);

        // The manual choice survives; auto-match did not override it.
        Assert.Equal("manual", await svc.GetConfirmedOfficialAsync(1));
        Assert.Single(ctx.ContestMappings);
        Assert.Equal(MappingStatus.ManualConfirmed, ctx.ContestMappings.Single().Confirmed);
    }

    [Fact]
    public async Task AutoMatchAll_IsIdempotent_RerunKeepsNonCuratedUnmapped()
    {
        await using var ctx = NewInMemory();
        ctx.GymContests.Add(new GymContest { GymId = 1, Title = "2023 ICPC Asia Hangzhou Regional", StartTime = 1_700_000_000, ProblemCount = 13 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "hz2023", Name = "2023 ICPC Asia Hangzhou Regional", CategoryPath = "icpc", StartAt = 1_700_000_050, ProblemCount = 13, SrkUrl = "u" });
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        await svc.AutoMatchAllAsync();
        await svc.AutoMatchAllAsync(); // second pass must not throw on the same (gym, official) key

        Assert.Empty(ctx.ContestMappings);
    }

    [Fact]
    public async Task GetOverview_IgnoresLegacyPendingRows_AndReportsUnmapped()
    {
        await using var ctx = NewInMemory();
        // Seed mappings directly so ordering is deterministic and not tied to matcher boundary scores.
        ctx.GymContests.Add(new GymContest { GymId = 1, Title = "Auto Gym", StartTime = 300, ProblemCount = 13 });
        ctx.GymContests.Add(new GymContest { GymId = 2, Title = "Pending Gym", StartTime = 200, ProblemCount = 5 });
        ctx.GymContests.Add(new GymContest { GymId = 3, Title = "Unmapped Gym", StartTime = 100, ProblemCount = 7 });
        ctx.OfficialContests.Add(new OfficialContest { Id = "auto-off", Name = "Auto Official", CategoryPath = "icpc", ProblemCount = 13, SrkUrl = "u" });
        ctx.OfficialContests.Add(new OfficialContest { Id = "pend-off", Name = "Pending Official", CategoryPath = "icpc", ProblemCount = 5, SrkUrl = "u" });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 1, OfficialId = "auto-off", Confidence = 0.9, Confirmed = MappingStatus.AutoConfirmed });
        ctx.ContestMappings.Add(new ContestMapping { GymId = 2, OfficialId = "pend-off", Confidence = 0.4, Confirmed = MappingStatus.Pending });
        // Gym 3 has no mapping row → reported as unmapped.
        await ctx.SaveChangesAsync();

        var svc = new MappingService(ctx);
        var overview = await svc.GetOverviewAsync();

        Assert.Equal(3, overview.Count);
        Assert.Equal(MappingStatus.Unmapped, overview[0].Status);
        Assert.Equal(2, overview[0].GymId);
        Assert.Equal(MappingStatus.Unmapped, overview[1].Status);
        Assert.Equal(3, overview[1].GymId);
        Assert.Equal(MappingStatus.AutoConfirmed, overview[2].Status);
        // Every gym appears exactly once.
        Assert.Equal(new[] { 1L, 2L, 3L }, overview.Select(r => r.GymId).OrderBy(x => x).ToArray());
        // Mapped rows carry the official name, not just the id.
        Assert.Equal("Auto Official", overview[2].OfficialName);
        Assert.Null(overview[0].OfficialId);
        Assert.Null(overview[1].OfficialId);
    }

    [Fact]
    public async Task GymSync_And_EstimateEndToEnd_DuplicateTeamNamesSurvive()
    {
        await using var ctx = NewInMemory();
        // Two official rows with the SAME team name must both persist (surrogate PK).
        ctx.OfficialContests.Add(new OfficialContest { Id = "off1", Name = "Dup Test", CategoryPath = "icpc", ProblemCount = 3, SrkUrl = "u" });
        ctx.OfficialStandings.Add(new OfficialStanding { OfficialId = "off1", Rank = 1, TeamName = "SameName", SolvedCount = 3, Penalty = 100, Official = true, HadValidSubmission = true });
        ctx.OfficialStandings.Add(new OfficialStanding { OfficialId = "off1", Rank = 2, TeamName = "SameName", SolvedCount = 2, Penalty = 200, Official = true, HadValidSubmission = true });
        await ctx.SaveChangesAsync();

        var rows = await ctx.OfficialStandings.Where(s => s.OfficialId == "off1").ToListAsync();
        Assert.Equal(2, rows.Count);
    }
}
