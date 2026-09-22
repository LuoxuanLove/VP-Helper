using System.Linq;
using System.Text.RegularExpressions;
using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Parser tests grounded in the three real QOJ fixtures under <c>tests/fixtures/</c>:
/// a contest submissions page, and two global submissions pages (9-column layout).
/// Synthetic HTML is avoided on purpose — real QOJ markup is the regression source.
/// </summary>
public class QojSubmissionsParserTests
{
    private const string ContestPage =
        "第十一届中国大学生程序设计竞赛 哈尔滨站（CCPC 2025 Harbin Site） - Submissions - Contest - QOJ.ac.html";
    private const string GlobalPage = "Submissions - QOJ.ac.html";
    private const string GlobalPage2 = "Submissions2 - QOJ.ac.html";
    private const string ProfilePage = "LuoxuanLove - User profile - QOJ.ac.html";

    [Fact]
    public void ContestPage_ParsesAllSubmissionRows()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(ContestPage));

        // The fixture has 10 submission rows, all by LuoxuanLove on contest 2575.
        Assert.Equal(10, page.Submissions.Count);
        Assert.All(page.Submissions, s => Assert.Equal("LuoxuanLove", s.Submitter));
        Assert.All(page.Submissions, s => Assert.Equal(2575, s.ContestId));
        Assert.All(page.Submissions, s => Assert.True(s.Id > 0));
    }

    [Fact]
    public void CurrentSpanUsernameLayout_ParsesSubmitter()
    {
        // Current authenticated pages render usernames as a span with data-nickname. Keep the
        // complete real contest fixture around that changed node so the regression stays realistic.
        var html = Regex.Replace(
            Fixtures.Read(ContestPage),
            @"<a class=""uoj-username""[^>]*>LuoxuanLove</a>",
            @"<span class=""uoj-username"" data-nickname=""LuoxuanLove"">LuoxuanLove</span>");

        var page = QojSubmissionsParser.Parse(html);

        Assert.Equal(10, page.Submissions.Count);
        Assert.All(page.Submissions, s => Assert.Equal("LuoxuanLove", s.Submitter));
    }

    [Fact]
    public void ContestPage_InContestFlagMatchesRelativeTime()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(ContestPage));

        // On the contest page the <sup> contest marker is perfectly correlated with a
        // relative HH:MM:SS submit time (in-contest / virtual). 6 in-contest, 4 practice.
        var inContest = page.Submissions.Where(s => s.InContest).ToList();
        var practice = page.Submissions.Where(s => !s.InContest).ToList();

        Assert.Equal(6, inContest.Count);
        Assert.Equal(4, practice.Count);

        // In-contest rows carry a relative offset (and no absolute unix); practice rows the reverse.
        Assert.All(inContest, s => Assert.NotNull(s.RelativeSeconds));
        Assert.All(inContest, s => Assert.Equal(0, s.SubmitTimeUnix));
        Assert.All(practice, s => Assert.Null(s.RelativeSeconds));
        Assert.All(practice, s => Assert.True(s.SubmitTimeUnix > 0));
    }

    [Fact]
    public void ContestPage_ExtractsVerdictsAndProblems()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(ContestPage));

        // Verdicts seen in the fixture: AC (with ✓), WA, TL, and a Compile Error.
        var verdicts = page.Submissions.Select(s => s.Verdict).ToHashSet();
        Assert.Contains("AC", verdicts);
        Assert.Contains("WA", verdicts);
        Assert.Contains("TL", verdicts);
        Assert.Contains("COMPILATION_ERROR", verdicts);

        // Contest-page problem labels use a letter index: "J. Judge of Gensokyo".
        var j = page.Submissions.First(s => s.ProblemIndex == "J");
        Assert.Equal("Judge of Gensokyo", j.ProblemName);
        Assert.False(string.IsNullOrEmpty(j.ProblemId)); // numeric problem id from the href
    }

    [Fact]
    public void ContestDashboard_MapsNumericProblemIdsToContestLetters()
    {
        // A real dashboard table is the authority for QOJ's two identities of the same problem:
        // submissions show numeric #14823, while the contest/Rankland column is J.
        const string dashboard = """
            <table><thead><tr><th>#</th><th>Problem</th></tr></thead><tbody>
              <tr><td>A</td><td><a href="/contest/2575/problem/14814/statement/en">Another GCD Problem</a></td></tr>
              <tr><td>J</td><td><a href="/contest/2575/problem/14823/statement/en">Judge of Gensokyo</a></td></tr>
              <tr><td>M</td><td><a href="/contest/2575/problem/14826">连通的正三角形</a></td></tr>
            </tbody></table>
            """;

        var problems = QojSubmissionsParser.ParseContestProblems(dashboard, 2575);

        Assert.Equal(3, problems.Count);
        var judge = Assert.Single(problems.Where(p => p.ProblemId == "14823"));
        Assert.Equal("J", judge.ProblemIndex);
        Assert.Equal("Judge of Gensokyo", judge.Title);
        Assert.Equal("https://qoj.ac/contest/2575/problem/14823", judge.ProblemUrl);
    }

    [Fact]
    public void ContestPage_RelativeSecondsAreParsedCorrectly()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(ContestPage));

        // 03:12:17 = 11537s is the earliest in-contest submission in the fixture.
        var min = page.Submissions.Where(s => s.RelativeSeconds is not null)
            .Min(s => s.RelativeSeconds!.Value);
        Assert.Equal(3 * 3600 + 12 * 60 + 17, min);
    }

    [Fact]
    public void GlobalPage_ParsesMultipleSubmittersAndDetectsInContest()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(GlobalPage));

        // Global page: 10 rows across many submitters; times always absolute.
        Assert.Equal(10, page.Submissions.Count);
        Assert.True(page.Submissions.Select(s => s.Submitter).Distinct().Count() > 1);
        Assert.All(page.Submissions, s => Assert.Null(s.RelativeSeconds));

        // The <sup> marker is the only in-contest signal here; the fixture has 5 such rows.
        Assert.Equal(5, page.Submissions.Count(s => s.InContest));
    }

    [Fact]
    public void GlobalPage_UsesProblemNumberIndex()
    {
        var page = QojSubmissionsParser.Parse(Fixtures.Read(GlobalPage));

        // Global labels look like "#18433. Lottery" — the "#" is stripped, number kept as index.
        var lottery = page.Submissions.FirstOrDefault(s => s.ProblemName == "Lottery");
        Assert.NotNull(lottery);
        Assert.Equal("18433", lottery!.ProblemIndex);
        Assert.Equal(3799, lottery.ContestId);
    }

    [Fact]
    public void GlobalPage2_ParsesWithoutError()
    {
        // Second global fixture (different snapshot) must parse to a non-empty multi-submitter set.
        var page = QojSubmissionsParser.Parse(Fixtures.Read(GlobalPage2));

        Assert.NotEmpty(page.Submissions);
        Assert.All(page.Submissions, s => Assert.True(s.Id > 0));
        Assert.All(page.Submissions, s => Assert.Null(s.RelativeSeconds));
    }

    [Theory]
    [InlineData("https://qoj.ac/contest/2575/problem/14823", 2575L, "14823")]
    [InlineData("https://qoj.ac/contest/3799/problem/18433", 3799L, "18433")]
    [InlineData("/contest/987/problem/4564", 987L, "4564")]
    [InlineData("https://qoj.ac/submission/1931370", 0L, "")]
    public void ParseProblemHref_Works(string href, long expectedContest, string expectedProblem)
    {
        var (cid, pid) = QojSubmissionsParser.ParseProblemHref(href);
        Assert.Equal(expectedContest, cid);
        Assert.Equal(expectedProblem, pid);
    }

    [Theory]
    [InlineData("J. Judge of Gensokyo", "J", "Judge of Gensokyo")]
    [InlineData("#18433. Lottery", "18433", "Lottery")]
    [InlineData("#898. 二分图最大匹配", "898", "二分图最大匹配")]
    [InlineData("NoDotLabel", "NoDotLabel", "")]
    public void SplitProblemLabel_Works(string label, string index, string name)
    {
        var (i, n) = QojSubmissionsParser.SplitProblemLabel(label);
        Assert.Equal(index, i);
        Assert.Equal(name, n);
    }

    [Theory]
    [InlineData("AC ✓", "AC")]
    [InlineData("WA ", "WA")]
    [InlineData("TL ", "TL")]
    [InlineData("Compile Error", "COMPILATION_ERROR")]
    [InlineData("100", "AC")]
    [InlineData("60", "60")]
    [InlineData("", "UNKNOWN")]
    public void NormalizeVerdict_Works(string raw, string expected)
    {
        Assert.Equal(expected, QojSubmissionsParser.NormalizeVerdict(raw));
    }

    [Fact]
    public void ParseQojTime_ParsesAbsoluteFormat()
    {
        // 2026-01-19 01:29:41 UTC → unix seconds.
        long unix = QojSubmissionsParser.ParseQojTime("2026-01-19 01:29:41");
        Assert.Equal(1768786181, unix);
    }

    [Fact]
    public void Profile_DiscoversVirtualParticipations()
    {
        var contests = QojProfileParser.ParseVirtualParticipations(Fixtures.Read(ProfilePage));

        // The profile's "Virtual Participations — 13 contests" table.
        Assert.Equal(13, contests.Count);
        Assert.All(contests, c => Assert.True(c.ContestId > 0));
        Assert.All(contests, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));

        // At least one row carries a parseable start time.
        Assert.Contains(contests, c => c.StartTimeUnix is > 0);
    }
}
