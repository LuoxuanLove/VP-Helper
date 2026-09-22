using System.Linq;
using VPHelper.Core.Analysis;
using VPHelper.Core.Srk;
using Xunit;

namespace VPHelper.Core.Tests;

public class MedalRuleTests
{
    private static SrkRow Row(bool official, bool hasSubmission)
        => new()
        {
            User = new SrkUser { Official = official, Name = "T" },
            Statuses = hasSubmission
                ? new() { new SrkProblemStatus { Result = "AC" } }
                : new() { new SrkProblemStatus { Result = null } },
        };

    [Fact]
    public void FromOfficialCounts_ConvertsPerBandToCumulative()
    {
        var line = MedalRuleProvider.FromOfficialCounts(new[] { 8, 16, 24 });
        Assert.NotNull(line);
        Assert.Equal(8, line!.Gold);
        Assert.Equal(24, line.Silver);   // 8 + 16
        Assert.Equal(48, line.Bronze);   // 8 + 16 + 24
    }

    [Fact]
    public void FromOfficialCounts_ZeroTriple_IsUnspecified()
    {
        Assert.Null(MedalRuleProvider.FromOfficialCounts(new[] { 0, 0, 0 }));
        Assert.Null(MedalRuleProvider.FromOfficialCounts(null));
        Assert.Null(MedalRuleProvider.FromOfficialCounts(new[] { 1 }));
    }

    [Theory]
    // N, gold(ceil .1), silver(ceil .3), bronze(ceil .6)
    [InlineData(100, 10, 30, 60)]
    [InlineData(48, 5, 15, 29)]   // ceil(4.8)=5, ceil(14.4)=15, ceil(28.8)=29
    [InlineData(1, 1, 1, 1)]
    [InlineData(11, 2, 4, 7)]     // ceil(1.1)=2, ceil(3.3)=4, ceil(6.6)=7
    [InlineData(0, 0, 0, 0)]
    public void DefaultLine_CeilRule(int n, int g, int s, int b)
    {
        var line = new MedalRuleProvider().DefaultLine(n);
        Assert.Equal(g, line.Gold);
        Assert.Equal(s, line.Silver);
        Assert.Equal(b, line.Bronze);
    }

    [Fact]
    public void EffectiveOfficialTeamCount_ExcludesNonOfficialAndZeroSubmission()
    {
        var rl = new SrkRanklist
        {
            Rows =
            {
                Row(official: true, hasSubmission: true),   // counts
                Row(official: true, hasSubmission: true),   // counts
                Row(official: true, hasSubmission: false),  // excluded: no submission
                Row(official: false, hasSubmission: true),  // excluded: not official
            }
        };
        Assert.Equal(2, MedalRuleProvider.EffectiveOfficialTeamCount(rl));
    }

    [Fact]
    public void Compute_PrefersOfficialCountsOverDefault()
    {
        var rl = new SrkRanklist { Rows = { Row(true, true), Row(true, true) } };
        var line = new MedalRuleProvider().Compute(rl, new[] { 1, 1, 1 });
        Assert.Equal(1, line.Gold);
        Assert.Equal(2, line.Silver);
        Assert.Equal(3, line.Bronze);
    }

    [Fact]
    public void Compute_FallsBackToDefaultWhenNoOfficialLine()
    {
        // 10 official teams w/ submissions -> gold ceil(1.0)=1, silver ceil(3.0)=3, bronze ceil(6.0)=6
        var rl = new SrkRanklist();
        for (int i = 0; i < 10; i++) rl.Rows.Add(Row(true, true));
        var line = new MedalRuleProvider().Compute(rl, new[] { 0, 0, 0 });
        Assert.Equal(1, line.Gold);
        Assert.Equal(3, line.Silver);
        Assert.Equal(6, line.Bronze);
    }

    [Fact]
    public void MedalFor_CumulativeBands()
    {
        var line = new MedalLine(3, 6, 9);
        Assert.Equal(Medal.Gold, line.MedalFor(1));
        Assert.Equal(Medal.Gold, line.MedalFor(3));
        Assert.Equal(Medal.Silver, line.MedalFor(4));
        Assert.Equal(Medal.Silver, line.MedalFor(6));
        Assert.Equal(Medal.Bronze, line.MedalFor(7));
        Assert.Equal(Medal.Bronze, line.MedalFor(9));
        Assert.Equal(Medal.None, line.MedalFor(10));
        Assert.Equal(Medal.None, line.MedalFor(0));
    }

    [Fact]
    public void RealFixture_Macau_UsesOfficialLine()
    {
        var rl = SrkParser.Parse(Fixtures.ReadSrk("icpc2023macau.srk.json"));
        var provider = new MedalRuleProvider();
        var line = provider.FromSrkSeries(rl);
        Assert.NotNull(line);
        // series count [8,16,24] -> cumulative 8/24/48
        Assert.Equal(8, line!.Gold);
        Assert.Equal(24, line.Silver);
        Assert.Equal(48, line.Bronze);
    }

    [Fact]
    public void RealFixture_Gzcpc_FallsBackToDefault()
    {
        var rl = SrkParser.Parse(Fixtures.ReadSrk("gzcpc2024.srk.json"));
        var provider = new MedalRuleProvider();
        Assert.Null(provider.FromSrkSeries(rl)); // [0,0,0] provincial line -> unspecified
        int n = MedalRuleProvider.EffectiveOfficialTeamCount(rl);
        Assert.True(n > 0);
        var line = provider.Compute(rl, provider.FromSrkSeries(rl) is { } l
            ? new[] { l.Gold, l.Silver - l.Gold, l.Bronze - l.Silver } : null);
        Assert.Equal(System.Math.Ceiling(n * 0.10), line.Gold);
        Assert.Equal(System.Math.Ceiling(n * 0.30), line.Silver);
        Assert.Equal(System.Math.Ceiling(n * 0.60), line.Bronze);
    }
}
