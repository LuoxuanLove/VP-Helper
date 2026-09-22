using System.Linq;
using VPHelper.Core.Models;
using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Parsing the CF <c>contest.status</c> API JSON — the only source that separates VP from practice
/// via <c>author.participantType</c>. Covers the participant-type surface plus non-OK / malformed
/// envelopes the sync must report rather than treat as an empty gym.
/// </summary>
public class ContestStatusParserTests
{
    private static string Row(long id, long contestId, string index, string participantType,
        string verdict, long creationTime, long relative) => $@"
    {{
      ""id"": {id},
      ""contestId"": {contestId},
      ""creationTimeSeconds"": {creationTime},
      ""relativeTimeSeconds"": {relative},
      ""problem"": {{ ""contestId"": {contestId}, ""index"": ""{index}"", ""name"": ""Prob {index}"" }},
      ""author"": {{ ""participantType"": ""{participantType}"" }},
      ""verdict"": ""{verdict}""
    }}";

    private static string Envelope(params string[] rows)
        => $@"{{ ""status"": ""OK"", ""result"": [ {string.Join(",", rows)} ] }}";

    [Fact]
    public void Parse_ExtractsParticipantTypeAndRelativeTime()
    {
        var json = Envelope(
            Row(101, 100500, "A", "VIRTUAL", "OK", 1_700_000_000, 3600),
            Row(102, 100500, "B", "PRACTICE", "WRONG_ANSWER", 1_700_100_000, 2147483647));

        var result = ContestStatusParser.Parse(json);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Submissions.Count);

        var vp = result.Submissions.Single(s => s.Id == 101);
        Assert.Equal("VIRTUAL", vp.ParticipantType);
        Assert.Equal(100500, vp.GymId);
        Assert.Equal("A", vp.ProblemIndex);
        Assert.Equal(3600, vp.RelativeTime);
        Assert.Equal(ParticipationKind.Virtual, ParticipationKindClassifier.Classify(vp.ParticipantType));

        var practice = result.Submissions.Single(s => s.Id == 102);
        Assert.Equal("PRACTICE", practice.ParticipantType);
        // int-max relativeTimeSeconds sentinel (practice) normalizes to null.
        Assert.Null(practice.RelativeTime);
        Assert.Equal(ParticipationKind.Practice, ParticipationKindClassifier.Classify(practice.ParticipantType));
    }

    [Fact]
    public void Classify_ContestantCountsAsVirtual_UnknownIsPractice()
    {
        Assert.Equal(ParticipationKind.Virtual, ParticipationKindClassifier.Classify("VIRTUAL"));
        Assert.Equal(ParticipationKind.Virtual, ParticipationKindClassifier.Classify("CONTESTANT"));
        Assert.Equal(ParticipationKind.Practice, ParticipationKindClassifier.Classify("PRACTICE"));
        Assert.Equal(ParticipationKind.Practice, ParticipationKindClassifier.Classify("OUT_OF_COMPETITION"));
        Assert.Equal(ParticipationKind.Practice, ParticipationKindClassifier.Classify(null));
        Assert.Equal(ParticipationKind.Practice, ParticipationKindClassifier.Classify(""));
    }

    [Fact]
    public void Parse_NonOkEnvelope_ReportsCommentNotEmptyGym()
    {
        var json = @"{ ""status"": ""FAILED"", ""comment"": ""contestId: You are not allowed to..."" }";
        var result = ContestStatusParser.Parse(json);

        Assert.False(result.Ok);
        Assert.Contains("not allowed", result.Comment);
        Assert.Empty(result.Submissions);
    }

    [Fact]
    public void Parse_MalformedJson_IsNotOk()
    {
        var result = ContestStatusParser.Parse("<html>not json</html>");
        Assert.False(result.Ok);
        Assert.Empty(result.Submissions);
    }

    [Fact]
    public void BuildUrl_EncodesHandleAndGym()
    {
        var url = ContestStatusParser.BuildUrl(100500, "user+name");
        Assert.Contains("contestId=100500", url);
        Assert.Contains("handle=user%2Bname", url);
    }
}
