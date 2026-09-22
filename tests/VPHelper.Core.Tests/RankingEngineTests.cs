using System.Linq;
using VPHelper.Core.Analysis;
using VPHelper.Core.Srk;
using Xunit;

namespace VPHelper.Core.Tests;

public class RankingEngineTests
{
    private static SrkRow Team(int solved, long penalty, bool official = true)
        => new()
        {
            User = new SrkUser { Official = official, Name = $"t{solved}-{penalty}" },
            SolvedValue = solved,
            PenaltySeconds = penalty,
            Statuses = new() { new SrkProblemStatus { Result = solved > 0 ? "AC" : null } },
        };

    [Fact]
    public void RankStandings_MoreSolvedFirstThenLessPenalty()
    {
        var rl = new SrkRanklist
        {
            Rows =
            {
                Team(5, 500),
                Team(6, 900),
                Team(6, 800),
                Team(5, 400),
            }
        };
        var ranked = RankingEngine.RankStandings(rl);
        Assert.Equal(6, ranked[0].Solved);
        Assert.Equal(800, ranked[0].PenaltySeconds); // 6/800 beats 6/900
        Assert.Equal(6, ranked[1].Solved);
        Assert.Equal(900, ranked[1].PenaltySeconds);
        Assert.Equal(5, ranked[2].Solved);
        Assert.Equal(400, ranked[2].PenaltySeconds); // 5/400 beats 5/500
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(4, ranked[3].Rank);
    }

    [Fact]
    public void RankStandings_TiesShareRankThenSkip()
    {
        var rl = new SrkRanklist { Rows = { Team(5, 500), Team(5, 500), Team(4, 100) } };
        var ranked = RankingEngine.RankStandings(rl);
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(1, ranked[1].Rank);
        Assert.Equal(3, ranked[2].Rank); // skips 2
    }

    [Fact]
    public void InsertVp_ComputesRankAsStrictlyAheadPlusOne()
    {
        var rl = new SrkRanklist
        {
            Rows = { Team(7, 100), Team(6, 200), Team(5, 300), Team(4, 400) }
        };
        // VP solved 6, penalty 150 -> beats 5/300 and 4/400, loses to 7/100 and 6/200?
        // 6/150 vs 6/200: same solved, 150<200 so VP is ahead of 6/200.
        var res = RankingEngine.InsertVp(rl, new ScoreEntry(6, 150), new MedalRuleProvider());
        // strictly ahead: only 7/100. rank = 2.
        Assert.Equal(2, res.Rank);
    }

    [Fact]
    public void InsertVp_MedalFromDefaultRule()
    {
        var rl = new SrkRanklist();
        for (int i = 0; i < 10; i++) rl.Rows.Add(Team(10 - (i / 3), i * 100));
        // 10 official teams; default gold=1 silver=3 bronze=6
        var res = RankingEngine.InsertVp(rl, new ScoreEntry(100, 0), new MedalRuleProvider());
        Assert.Equal(1, res.Rank);           // beats everyone
        Assert.Equal(Medal.Gold, res.Medal);
    }

    [Fact]
    public void EndToEnd_MacauFixture_TopVpRanksReasonably()
    {
        var rl = SrkParser.Parse(Fixtures.ReadSrk("icpc2023macau.srk.json"));
        var ranked = RankingEngine.RankStandings(rl);
        var top = ranked[0];

        // A VP matching the champion's exact score should tie/insert at rank 1.
        var res = RankingEngine.InsertVp(rl, new ScoreEntry(top.Solved, top.PenaltySeconds),
            new MedalRuleProvider(), officialMedalCounts: new[] { 8, 16, 24 });
        Assert.Equal(1, res.Rank);
        Assert.Equal(Medal.Gold, res.Medal);

        // A weak VP (solve 1, huge penalty) should land near the back and earn no medal.
        var weak = RankingEngine.InsertVp(rl, new ScoreEntry(1, 999999),
            new MedalRuleProvider(), officialMedalCounts: new[] { 8, 16, 24 });
        Assert.True(weak.Rank > res.Rank);
        Assert.Equal(Medal.None, weak.Medal);
    }

    [Fact]
    public void EndToEnd_MacauFixture_MidVpEarnsExpectedBand()
    {
        var rl = SrkParser.Parse(Fixtures.ReadSrk("icpc2023macau.srk.json"));
        var ranked = RankingEngine.RankStandings(rl).Where(r => r.Official).ToList();
        // Take the team currently sitting at official rank ~10 (silver band, line 8/24/48).
        var target = ranked.First(r => r.Rank >= 10);
        var res = RankingEngine.InsertVp(rl, new ScoreEntry(target.Solved, target.PenaltySeconds),
            new MedalRuleProvider(), officialMedalCounts: new[] { 8, 16, 24 });
        Assert.InRange(res.Rank, 9, 12);
        Assert.Equal(Medal.Silver, res.Medal);
    }
}
