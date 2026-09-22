using System.Linq;
using VPHelper.Core.Srk;
using Xunit;

namespace VPHelper.Core.Tests;

public class SrkParserTests
{
    [Fact]
    public void ParsesText_PlainAndI18n()
    {
        var json = """
        {"type":"general","version":"0.3.9",
         "contest":{"title":{"fallback":"Hello","zh-CN":"你好"},"startAt":"2025-01-02T09:00:00+08:00","duration":[5,"h"]},
         "problems":[{"alias":"A","title":"Plain"}],
         "rows":[]}
        """;
        var rl = SrkParser.Parse(json);
        Assert.Equal("Hello", rl.Contest.Title);
        Assert.Equal("A", rl.Problems[0].Alias);
        Assert.Equal("Plain", rl.Problems[0].Title);
    }

    [Fact]
    public void NormalizesDurationUnitsToSeconds()
    {
        var json = """
        {"contest":{"title":"x","duration":[5,"h"],"frozenDuration":[60,"min"]},"rows":[]}
        """;
        var rl = SrkParser.Parse(json);
        Assert.Equal(5 * 3600, rl.Contest.DurationSeconds);
        Assert.Equal(60 * 60, rl.Contest.FrozenDurationSeconds);
    }

    [Fact]
    public void ParsesIsoStartAtToUnix()
    {
        var json = """{"contest":{"title":"x","startAt":"2025-01-01T00:00:00+00:00"},"rows":[]}""";
        var rl = SrkParser.Parse(json);
        Assert.Equal(1735689600, rl.Contest.StartAt); // 2025-01-01T00:00Z
    }

    [Fact]
    public void ParsesRowScoreAndStatuses()
    {
        var json = """
        {"contest":{"title":"x"},
         "problems":[{"alias":"A"},{"alias":"B"}],
         "rows":[
           {"user":{"name":"T1","official":true,"organization":"Org"},
            "score":{"value":2,"time":[100,"min"]},
            "statuses":[{"result":"AC","time":[10,"min"],"tries":1},{"result":"RJ","tries":3}]}
         ]}
        """;
        var rl = SrkParser.Parse(json);
        var row = rl.Rows.Single();
        Assert.True(row.User.Official);
        Assert.Equal("Org", row.User.Organization);
        Assert.Equal(2, row.SolvedValue);
        Assert.Equal(100 * 60, row.PenaltySeconds);
        Assert.Equal("AC", row.Statuses[0].Result);
        Assert.Equal(10 * 60, row.Statuses[0].TimeSeconds);
        Assert.Equal(3, row.Statuses[1].Tries);
    }

    [Fact]
    public void ParsesProblemStatisticsAsAcceptedAndSubmittedAttempts()
    {
        var json = """
        {"contest":{"title":"x"},
         "problems":[{"alias":"A","statistics":{"accepted":127,"submitted":1063}}],
         "rows":[]}
        """;

        var rl = SrkParser.Parse(json);

        Assert.Equal(127, rl.Problems[0].Accepted);
        Assert.Equal(1063, rl.Problems[0].Submitted);
    }

    [Fact]
    public void ParsesSorterPenalty()
    {
        var json = """
        {"contest":{"title":"x"},"rows":[],
         "sorter":{"algorithm":"ICPC","config":{"penalty":[20,"min"],"timePrecision":"min","noPenaltyResults":["AC","FB",null]}}}
        """;
        var rl = SrkParser.Parse(json);
        Assert.Equal("ICPC", rl.Sorter!.Algorithm);
        Assert.Equal(20 * 60, rl.Sorter.PenaltyPerWrongSeconds);
        Assert.Contains(null, rl.Sorter.NoPenaltyResults);
        Assert.Contains("AC", rl.Sorter.NoPenaltyResults);
    }

    [Fact]
    public void ParsesSeriesMedalCounts()
    {
        var json = """
        {"contest":{"title":"x"},"rows":[],
         "series":[{"title":"#","rule":{"preset":"ICPC","options":{"count":{"value":[8,16,24]}}}}]}
        """;
        var rl = SrkParser.Parse(json);
        Assert.Equal(new[] { 8, 16, 24 }, rl.Series[0].MedalCounts);
    }

    [Fact]
    public void ToleratesMissingOptionalFields()
    {
        var json = """{"rows":[{"user":{"name":"T"}}]}""";
        var rl = SrkParser.Parse(json);
        Assert.Single(rl.Rows);
        Assert.Equal("T", rl.Rows[0].User.Name);
        Assert.Empty(rl.Problems);
        Assert.Null(rl.Sorter);
    }

    [Theory]
    [InlineData("gzcpc2024.srk.json", 49, 14)]
    [InlineData("icpc2023macau.srk.json", 92, 11)]
    public void ParsesRealFixtures(string file, int expectRows, int expectProblems)
    {
        var rl = SrkParser.Parse(Fixtures.ReadSrk(file));
        Assert.Equal(expectRows, rl.Rows.Count);
        Assert.Equal(expectProblems, rl.Problems.Count);
        Assert.False(string.IsNullOrEmpty(rl.Contest.Title));
        Assert.NotNull(rl.Contest.StartAt);
    }
}
