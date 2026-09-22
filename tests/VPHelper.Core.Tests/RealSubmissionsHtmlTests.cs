using System.Linq;
using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Tests the submissions parser against the EXACT real Codeforces markup shape (captured
/// 2026-06 from a live /submissions page): the first anchor in each row is the view-source
/// link (…/submission/{id}) which also matches /contest/ or /gym/, and the problem link
/// (…/problem/X) comes later. The parser must select the problem link, not the source link.
/// </summary>
public class RealSubmissionsHtmlTests
{
    // A gym row in the exact real CF shape: source link first (no /problem/), problem link later.
    private const string RealGymRow = @"
<tr data-submission-id=""380351477"">
    <td class=""id-cell"">
        <a class=""view-source"" title=""Source"" href=""/gym/106409/submission/380351477"" submissionId=""380351477"">380351477</a>
    </td>
    <td class=""status-small"">
        <span class=""format-time"" data-locale=""en"">Jun/27/2026 18:42</span>
    </td>
    <td class=""status-party-cell"">
        <a href=""/profile/tourist"" class=""rated-user"">tourist</a>
    </td>
    <td class=""status-small"" data-problemId=""4188087"">
        <a href=""/gym/106409/problem/J"">J - Some Gym Problem</a>
    </td>
    <td>C++23</td>
    <td class=""status-cell status-small status-verdict-cell"" submissionId=""380351477"" waiting=""false"">
        <span class=""submissionVerdictWrapper"" submissionId=""380351477"" submissionVerdict=""OK""><span class='verdict-accepted'>Accepted</span></span>
    </td>
    <td class=""time-consumed-cell"">671&nbsp;ms</td>
    <td class=""memory-consumed-cell"">50500&nbsp;KB</td>
</tr>";

    private static string Wrap(string rows) => $"<html><body><table>{rows}</table></body></html>";

    [Fact]
    public void RealGymRow_PicksProblemLink_NotSourceLink()
    {
        var page = SubmissionsParser.Parse(Wrap(RealGymRow));
        Assert.Single(page.Submissions);
        var s = page.Submissions[0];
        Assert.Equal(380351477, s.Id);
        Assert.Equal(106409, s.GymId);
        // The critical assertion: index is "J" from /problem/J, not "?" from the source link.
        Assert.Equal("J", s.ProblemIndex);
        Assert.Equal("OK", s.Verdict);
        Assert.Equal("Some Gym Problem", s.ProblemName);
        Assert.True(s.SubmitTimeUnix > 0);
    }

    [Fact]
    public void RealFixture_ParsesWithoutError_AndExcludesNonGymRows()
    {
        // The captured fixture is a real /submissions/tourist page: all rows are regular
        // rated-contest submissions (no gym). The parser must not throw and must return
        // zero gym rows (contest submissions are out of scope).
        if (!Fixtures.Exists("real_submissions.html")) return; // fixture optional
        var html = Fixtures.Read("real_submissions.html");
        var page = SubmissionsParser.Parse(html);
        Assert.Empty(page.Submissions); // no /gym/ problem links in this fixture
    }

    [Fact]
    public void MixedPage_KeepsOnlyGymRows()
    {
        // A page with one contest row and one gym row must yield exactly the gym row.
        const string contestRow = @"
<tr data-submission-id=""111"">
    <td class=""id-cell""><a class=""view-source"" href=""/contest/2239/submission/111"">111</a></td>
    <td class=""status-small""><span class=""format-time"">Jun/27/2026 18:42</span></td>
    <td class=""status-small""><a href=""/contest/2239/problem/D"">D - Beast</a></td>
    <td class=""status-verdict-cell""><span submissionVerdict=""OK"">Accepted</span></td>
</tr>";
        var page = SubmissionsParser.Parse(Wrap(contestRow + RealGymRow));
        Assert.Single(page.Submissions);
        Assert.Equal(106409, page.Submissions[0].GymId);
    }
}
