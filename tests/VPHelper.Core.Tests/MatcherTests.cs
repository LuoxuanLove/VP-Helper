using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Analysis;
using Xunit;

namespace VPHelper.Core.Tests;

public class MatcherTests
{
    [Fact]
    public void TitleSimilarity_IdenticalIsOne()
    {
        Assert.Equal(1.0, ContestMatcher.TitleSimilarity("2024 ICPC Wuhan", "2024 ICPC Wuhan"), 3);
    }

    [Fact]
    public void TitleSimilarity_UnrelatedIsLow()
    {
        var s = ContestMatcher.TitleSimilarity("ICPC Wuhan Regional", "Educational Codeforces Round 100");
        Assert.True(s < 0.3, $"expected low, got {s}");
    }

    [Fact]
    public void TitleSimilarity_CjkOverlapScoresHigh()
    {
        var s = ContestMatcher.TitleSimilarity("2024 武汉邀请赛", "2024-05-02 武汉邀请赛");
        Assert.True(s > 0.5, $"expected high CJK overlap, got {s}");
    }

    [Fact]
    public void TimeScore_WithinDayIsOne()
    {
        Assert.Equal(1.0, ContestMatcher.TimeScore(1_000_000, 1_000_000 + 3600), 3);
    }

    [Fact]
    public void TimeScore_FarApartIsZero()
    {
        Assert.Equal(0.0, ContestMatcher.TimeScore(0, 40L * 86400), 3);
    }

    [Fact]
    public void ProblemScore_ExactIsOne_DiffDecays()
    {
        Assert.Equal(1.0, ContestMatcher.ProblemScore(13, 13), 3);
        Assert.True(ContestMatcher.ProblemScore(13, 12) > 0.7);
        Assert.Equal(0.0, ContestMatcher.ProblemScore(5, 13), 3);
    }

    [Fact]
    public void Match_RanksCorrectOfficialTop1()
    {
        var gym = new MatchGym(104520, "2024 ICPC 武汉邀请赛", 1714613400, 13);
        var officials = new List<MatchOfficial>
        {
            new("icpc2024invitational-wuhan", "2024-05-02 武汉邀请赛", 1714613400, 13),
            new("icpc2024invitational-xi_an", "2024-05-18 西安邀请赛", 1716000000, 12),
            new("ccpc2024final", "2024 CCPC 总决赛", 1730000000, 11),
        };
        var matcher = new ContestMatcher();
        var candidates = matcher.Match(gym, officials);
        Assert.Equal("icpc2024invitational-wuhan", candidates[0].OfficialId);
        Assert.True(candidates[0].Confidence > candidates[1].Confidence);
    }
}

public class ContestClassifierTests
{
    [Theory]
    [InlineData("2024-05-02 武汉邀请赛", "icpc/icpc2024/icpc2024invitational-wuhan", ContestType.Invitational)]
    [InlineData("2025-09-07 网络赛第一场", "icpc/icpc2025/icpc2025preliminary-1", ContestType.Preliminary)]
    [InlineData("2024 ICPC EC Final", "icpc/icpc2024/icpc2024ecfinal", ContestType.Final)]
    [InlineData("2024 ICPC World Finals", "icpc/icpc2024/icpc49thworldfinals", ContestType.Final)]
    [InlineData("2025-11-02 武汉站", "icpc/icpc2025/icpc2025wuhan", ContestType.Regional)]
    public void Classify_Samples(string name, string path, ContestType expected)
    {
        Assert.Equal(expected, ContestClassifier.Classify(name, path));
    }

    [Fact]
    public void Classify_ProvincialByTopCategory()
    {
        Assert.Equal(ContestType.Provincial,
            ContestClassifier.Classify("第9届山东省赛", "provincial/sd/sdcpc9th", "provincial"));
    }
}
