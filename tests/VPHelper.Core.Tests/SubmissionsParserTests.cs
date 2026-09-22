using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

public class SubmissionsParserTests
{
    // A minimal reproduction of the Codeforces submissions table markup.
    private const string SampleHtml = @"
<html><body>
<div class='datatable'>
<table>
<tr><th>#</th><th>When</th><th>Problem</th><th>Verdict</th></tr>
<tr data-submission-id='300001'>
  <td class='id-cell'>300001</td>
  <td class='format-time'>Oct/19/2025 13:05</td>
  <td><a href='/gym/104520/problem/A'>A - Alpha Problem</a></td>
  <td><span submissionverdict='OK' class='verdict-accepted'>Accepted</span></td>
</tr>
<tr data-submission-id='300002'>
  <td class='id-cell'>300002</td>
  <td class='format-time'>Oct/19/2025 13:20</td>
  <td><a href='/gym/104520/problem/B'>B - Beta Problem</a></td>
  <td><span submissionverdict='WRONG_ANSWER' class='verdict-rejected'>Wrong answer on test 3</span></td>
</tr>
<tr data-submission-id='300003'>
  <td class='id-cell'>300003</td>
  <td class='format-time'>Oct/20/2025 09:00</td>
  <td><a href='/contest/1500/problem/C'>C - Rated Problem</a></td>
  <td><span submissionverdict='OK' class='verdict-accepted'>Accepted</span></td>
</tr>
</table>
</div>
<div class='pagination'><ul>
  <li><a href='/submissions/tourist/page/1'>1</a></li>
  <li><a href='/submissions/tourist/page/2'>2</a></li>
  <li><a href='/submissions/tourist/page/3'>3</a></li>
</ul></div>
</body></html>";

    [Fact]
    public void Parse_ExtractsOnlyGymSubmissions()
    {
        var page = SubmissionsParser.Parse(SampleHtml);
        // The rated /contest/ row must be excluded; only 2 gym rows remain.
        Assert.Equal(2, page.Submissions.Count);
        Assert.All(page.Submissions, s => Assert.Equal(104520, s.GymId));
    }

    [Fact]
    public void Parse_ExtractsFields()
    {
        var page = SubmissionsParser.Parse(SampleHtml);
        var a = page.Submissions.Single(s => s.Id == 300001);
        Assert.Equal("A", a.ProblemIndex);
        Assert.Equal("Alpha Problem", a.ProblemName);
        Assert.Equal("OK", a.Verdict);
        Assert.True(a.SubmitTimeUnix > 0);

        var b = page.Submissions.Single(s => s.Id == 300002);
        Assert.Equal("B", b.ProblemIndex);
        Assert.Equal("WRONG_ANSWER", b.Verdict);
    }

    [Fact]
    public void Parse_FindsMaxPage()
    {
        var page = SubmissionsParser.Parse(SampleHtml);
        Assert.Equal(3, page.MaxPageIndex);
    }

    [Theory]
    [InlineData("/gym/104520/problem/A", 104520, "A")]
    [InlineData("/gym/98765/problem/D", 98765, "D")]
    [InlineData("/contest/1500/problem/C", 0, "")]
    public void ParseProblemHref_Works(string href, long expectedGym, string expectedIndex)
    {
        var (gym, idx) = SubmissionsParser.ParseProblemHref(href);
        Assert.Equal(expectedGym, gym);
        Assert.Equal(expectedIndex, idx);
    }

    [Fact]
    public void ParseCfTime_ParsesKnownFormats()
    {
        Assert.True(SubmissionsParser.ParseCfTime("Oct/19/2025 13:05") > 0);
        Assert.True(SubmissionsParser.ParseCfTime("2025-10-19 13:05") > 0);
        Assert.Equal(0, SubmissionsParser.ParseCfTime("not a date"));
    }
}

public class ScraperIncrementalTests
{
    // A fetcher that serves canned pages by URL.
    private sealed class MapFetcher : IBrowserFetcher
    {
        private readonly Dictionary<string, string> _pages;
        public int Calls { get; private set; }
        public MapFetcher(Dictionary<string, string> pages) => _pages = pages;
        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
        {
            Calls++;
            if (_pages.TryGetValue(url, out var html))
                return Task.FromResult(new FetchResult(url, html, FetchStatus.Ok));
            return Task.FromResult(new FetchResult(url, "<html><body></body></html>", FetchStatus.Ok));
        }
    }

    private static string PageWith(params (long id, string idx, string verdict)[] subs)
    {
        var rows = string.Join("", subs.Select(s => $@"
<tr data-submission-id='{s.id}'>
  <td class='format-time'>Oct/19/2025 13:05</td>
  <td><a href='/gym/1000/problem/{s.idx}'>{s.idx} - P</a></td>
  <td><span submissionverdict='{s.verdict}'>x</span></td>
</tr>"));
        return $"<html><body><table>{rows}</table></body></html>";
    }

    private static Throttle NoWaitThrottle() =>
        new(System.TimeSpan.Zero, delay: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Scrape_FullFirstRun_ReturnsAll()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://codeforces.com/submissions/me"] =
                PageWith((5, "A", "OK"), (4, "B", "WRONG_ANSWER")),
        };
        var scraper = new SubmissionScraper(new MapFetcher(pages), NoWaitThrottle(),
            delay: (_, _) => Task.CompletedTask);

        var result = await scraper.ScrapeAsync("me", _ => false);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Scrape_Incremental_StopsAtKnownId()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://codeforces.com/submissions/me"] =
                PageWith((5, "A", "OK"), (4, "B", "OK")),
        };
        var scraper = new SubmissionScraper(new MapFetcher(pages), NoWaitThrottle(),
            delay: (_, _) => Task.CompletedTask);

        // id 4 already known → only id 5 is new, and we stop.
        var result = await scraper.ScrapeAsync("me", id => id == 4);
        Assert.Single(result);
        Assert.Equal(5, result[0].Id);
    }

    [Fact]
    public async Task Scrape_RetriesOnErrorThenSucceeds()
    {
        int attempts = 0;
        var flaky = new DelegateFetcher(url =>
        {
            attempts++;
            if (attempts < 3) return new FetchResult(url, null, FetchStatus.Error, "boom");
            return new FetchResult(url, PageWith((9, "A", "OK")), FetchStatus.Ok);
        });
        var scraper = new SubmissionScraper(flaky, NoWaitThrottle(),
            new BackoffPolicy { MaxRetries = 5 }, delay: (_, _) => Task.CompletedTask);

        var result = await scraper.ScrapeAsync("me", _ => false);
        Assert.Single(result);
        // 2 failed attempts + 1 success on page 1; the scraper then probes page 2
        // (no pagination marker), gets a duplicate id, and stops. So >= 3 fetches.
        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task Scrape_RaisesCloudflareEvent()
    {
        bool raised = false;
        int attempts = 0;
        var fetcher = new DelegateFetcher(url =>
        {
            attempts++;
            if (attempts == 1) return new FetchResult(url, null, FetchStatus.CloudflareChallenge);
            return new FetchResult(url, PageWith((9, "A", "OK")), FetchStatus.Ok);
        });
        var scraper = new SubmissionScraper(fetcher, NoWaitThrottle(),
            delay: (_, _) => Task.CompletedTask);
        scraper.CloudflareChallengeDetected += _ => raised = true;

        await scraper.ScrapeAsync("me", _ => false);
        Assert.True(raised);
    }

    private sealed class DelegateFetcher : IBrowserFetcher
    {
        private readonly System.Func<string, FetchResult> _fn;
        public DelegateFetcher(System.Func<string, FetchResult> fn) => _fn = fn;
        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
            => Task.FromResult(_fn(url));
    }
}
