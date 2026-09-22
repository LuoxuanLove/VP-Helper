using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Scraping;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// QOJ sync tests: profile discovery, contest submission scraping, SQLite persistence, and
/// derived VpResult/GymContest rows. Uses the real QOJ HTML fixtures in <c>tests/fixtures/</c>
/// so the parser's in-contest detection is exercised against authentic markup.
/// </summary>
public class QojSyncServiceTests
{
    private const string Handle = "LuoxuanLove";
    private const string ContestFixture =
        "第十一届中国大学生程序设计竞赛 哈尔滨站（CCPC 2025 Harbin Site） - Submissions - Contest - QOJ.ac.html";
    private const string ProfileFixture = "LuoxuanLove - User profile - QOJ.ac.html";
    private const string DashboardFixture = """
        <table><thead><tr><th>#</th><th>Problem</th></tr></thead><tbody>
          <tr><td>B</td><td><a href="/contest/2575/problem/14815/statement/en">Ball And Movable Walls</a></td></tr>
          <tr><td>J</td><td><a href="/contest/2575/problem/14823/statement/en">Judge of Gensokyo</a></td></tr>
        </tbody></table>
        """;

    // ---- helpers ----------------------------------------------------------------

    private static VpDbContext NewInMemory()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>().UseSqlite(conn).Options;
        var ctx = new VpDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static Throttle NoWait() => new(TimeSpan.Zero, delay: (_, _) => Task.CompletedTask);

    /// <summary>
    /// A fake fetcher that serves specific HTML bodies for known URLs, and an empty table for
    /// anything else (so the scraper terminates cleanly on unrecognised URLs).
    /// </summary>
    private sealed class MapFetcher : IBrowserFetcher
    {
        private readonly IReadOnlyDictionary<string, string> _map;

        public MapFetcher(IReadOnlyDictionary<string, string> map) => _map = map;

        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
        {
            if (_map.TryGetValue(url, out var html))
                return Task.FromResult(new FetchResult(url, html, FetchStatus.Ok));
            return Task.FromResult(new FetchResult(url,
                "<html><body><table><thead><tr><th>ID</th></tr></thead><tbody></tbody></table></body></html>",
                FetchStatus.Ok));
        }
    }

    private sealed class SelectiveFailureFetcher : IBrowserFetcher
    {
        private readonly IReadOnlyDictionary<string, string> _map;

        public SelectiveFailureFetcher(IReadOnlyDictionary<string, string> map) => _map = map;

        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
            => _map.TryGetValue(url, out var html)
                ? Task.FromResult(new FetchResult(url, html, FetchStatus.Ok))
                : Task.FromResult(new FetchResult(url, null, FetchStatus.Error, "network unavailable"));
    }

    // ---- URL builder ------------------------------------------------------------

    [Theory]
    [InlineData(2575L, 1, "https://qoj.ac/contest/2575/submissions")]
    [InlineData(2575L, 2, "https://qoj.ac/contest/2575/submissions?page=2")]
    [InlineData(3799L, 5, "https://qoj.ac/contest/3799/submissions?page=5")]
    public void BuildContestPageUrl_ReturnsExpectedUrl(long contestId, int page, string expected)
    {
        Assert.Equal(expected, QojSyncService.BuildContestPageUrl(contestId, page));
    }

    [Theory]
    [InlineData("LuoxuanLove", 1, "https://qoj.ac/submissions?submitter=LuoxuanLove")]
    [InlineData("LuoxuanLove", 3, "https://qoj.ac/submissions?submitter=LuoxuanLove&page=3")]
    [InlineData("a b", 1, "https://qoj.ac/submissions?submitter=a%20b")]
    public void BuildAccountPageUrl_ReturnsFilteredUrl(string handle, int page, string expected)
    {
        Assert.Equal(expected, QojSyncService.BuildAccountPageUrl(handle, page));
    }

    // ---- profile discovery -------------------------------------------------------

    [Fact]
    public void ParseProfile_FindsThirteenVpContests()
    {
        var html = Fixtures.Read(ProfileFixture);
        var contests = QojProfileParser.ParseVirtualParticipations(html);
        Assert.Equal(13, contests.Count);
        Assert.All(contests, c => Assert.True(c.ContestId > 0));
    }

    // ---- contest-level sync -----------------------------------------------------

    [Fact]
    public async Task Sync_ContestPage_StoresVpAndPracticeSubmissions()
    {
        // The CCPC Harbin fixture has 10 rows by LuoxuanLove:
        //   6 in-contest (VP, marked with <sup>)  + 4 practice.
        // Profile fixture has 13 VP contests, but we only wire up the CCPC one here.
        const long contestId = 2575;
        var profileHtml = Fixtures.Read(ProfileFixture);
        var contestHtml = Fixtures.Read(ContestFixture);

        // Map: profile URL + first contest page (no subsequent pages → scraper stops naturally).
        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = profileHtml,
            [QojSyncService.BuildContestDashboardUrl(contestId)] = DashboardFixture,
            [$"https://qoj.ac/contest/{contestId}/submissions"] = contestHtml,
            // Other contests from the profile return empty pages.
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());
        var stored = await svc.SyncAsync(Handle);

        // 10 submissions from the CCPC contest should have been stored.
        // The other 12 contests return empty pages (no rows by LuoxuanLove) → 0 rows.
        Assert.Equal(10, stored);

        var subs = await db.Submissions.Where(s => s.Platform == Platform.Qoj).ToListAsync();
        Assert.Equal(10, subs.Count);
        Assert.All(subs, s => Assert.Equal(Platform.Qoj, s.Platform));
        Assert.All(subs, s => Assert.Equal(Handle, s.Handle));
        Assert.All(subs, s => Assert.Equal(contestId, s.GymId));

        // 6 in-contest → VIRTUAL; 4 practice → PRACTICE.
        Assert.Equal(6, subs.Count(s => s.ParticipantType == "VIRTUAL"));
        Assert.Equal(4, subs.Count(s => s.ParticipantType == "PRACTICE"));
        Assert.All(subs, s => Assert.Contains(s.ProblemIndex, new[] { "B", "J" }));
        Assert.All(subs, s => Assert.Contains(s.ProblemId, new[] { "14815", "14823" }));
    }

    [Fact]
    public async Task Sync_AccountFeed_StoresRowsWithoutVisitingEveryContest()
    {
        const long contestId = 2575;
        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = Fixtures.Read(ProfileFixture),
            [QojSyncService.BuildContestDashboardUrl(contestId)] = DashboardFixture,
            [QojSyncService.BuildAccountPageUrl(Handle, 1)] = Fixtures.Read(ContestFixture),
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());

        var stored = await svc.SyncAsync(Handle);

        Assert.Equal(10, stored);
        Assert.Equal(10, await db.Submissions
            .CountAsync(s => s.Platform == Platform.Qoj && s.Handle == Handle && s.GymId == contestId));

        // The global account feed labels the same QOJ problems as #14815 / #14823, but persisted
        // data must use contest B / J so VP status and Rankland columns share one key.
        Assert.All(await db.Submissions.Where(s => s.Platform == Platform.Qoj).ToListAsync(), s =>
            Assert.Contains(s.ProblemIndex, new[] { "B", "J" }));
        Assert.All(await db.Submissions.Where(s => s.ProblemIndex == "J").ToListAsync(), s =>
            Assert.Equal("14823", s.ProblemId));
        Assert.Equal("https://qoj.ac/contest/2575/problem/14823",
            (await db.Submissions.FirstAsync(s => s.ProblemIndex == "J")).ProblemUrl);
    }

    [Fact]
    public async Task Sync_AllSubmissionPagesFail_ThrowsAndDoesNotAdvanceAccount()
    {
        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = Fixtures.Read(ProfileFixture),
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new SelectiveFailureFetcher(map), NoWait(),
            new BackoffPolicy { MaxRetries = 0 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SyncAsync(Handle));

        Assert.Contains("提交页均读取失败", ex.Message);
        Assert.Null(await db.Accounts.FindAsync(Platform.Qoj, Handle));
    }

    [Fact]
    public async Task Sync_SubmissionRowsWithoutRecognizableSubmitter_ThrowsAndDoesNotAdvanceAccount()
    {
        var unrecognizedLayout = Fixtures.Read(ContestFixture)
            .Replace("class=\"uoj-username\"", "class=\"unknown-username\"");
        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = Fixtures.Read(ProfileFixture),
            [QojSyncService.BuildAccountPageUrl(Handle, 1)] = unrecognizedLayout,
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SyncAsync(Handle));

        Assert.Contains("无法识别提交者", ex.Message);
        Assert.Null(await db.Accounts.FindAsync(Platform.Qoj, Handle));
        Assert.Empty(await db.Submissions.Where(s => s.Platform == Platform.Qoj).ToListAsync());
    }

    [Fact]
    public async Task Sync_ContestPage_ProducesVpResultAndGymContest()
    {
        const long contestId = 2575;
        var profileHtml = Fixtures.Read(ProfileFixture);
        var contestHtml = Fixtures.Read(ContestFixture);

        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = profileHtml,
            [$"https://qoj.ac/contest/{contestId}/submissions"] = contestHtml,
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());
        await svc.SyncAsync(Handle);

        // GymContest row must exist for the contest.
        var gym = await db.GymContests
            .Where(g => g.Platform == Platform.Qoj && g.GymId == contestId)
            .FirstOrDefaultAsync();
        Assert.NotNull(gym);

        // VpResult for Virtual must exist and have at least one solve (the fixture has 2 ACs).
        var vpResult = await db.VpResults
            .Where(v => v.Platform == Platform.Qoj && v.Handle == Handle
                     && v.GymId == contestId && v.Kind == ParticipationKind.Virtual)
            .FirstOrDefaultAsync();
        Assert.NotNull(vpResult);
        Assert.True(vpResult!.SolvedCount >= 1);

        // Practice result must also exist (4 practice rows, some of which may be ACs).
        var practiceResult = await db.VpResults
            .Where(v => v.Platform == Platform.Qoj && v.Handle == Handle
                     && v.GymId == contestId && v.Kind == ParticipationKind.Practice)
            .FirstOrDefaultAsync();
        Assert.NotNull(practiceResult);
    }

    [Fact]
    public async Task Sync_Incremental_SecondRunStoresNothing()
    {
        const long contestId = 2575;
        var profileHtml = Fixtures.Read(ProfileFixture);
        var contestHtml = Fixtures.Read(ContestFixture);

        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = profileHtml,
            [$"https://qoj.ac/contest/{contestId}/submissions"] = contestHtml,
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());

        int first = await svc.SyncAsync(Handle);
        int second = await svc.SyncAsync(Handle);

        Assert.Equal(10, first);  // 10 rows on the first run
        Assert.Equal(0, second);  // zero new rows on the second run (all known)
        Assert.Equal(10, await db.Submissions.Where(s => s.Platform == Platform.Qoj).CountAsync());
    }

    [Fact]
    public async Task Sync_Account_WatermarkAdvances()
    {
        const long contestId = 2575;
        var profileHtml = Fixtures.Read(ProfileFixture);
        var contestHtml = Fixtures.Read(ContestFixture);

        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = profileHtml,
            [$"https://qoj.ac/contest/{contestId}/submissions"] = contestHtml,
        };

        await using var db = NewInMemory();
        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());
        await svc.SyncAsync(Handle);

        var acct = await db.Accounts.FindAsync(Platform.Qoj, Handle);
        Assert.NotNull(acct);
        Assert.True(acct!.LastSyncedAt > 0);
        Assert.Equal(10, acct.TotalSubmissions);
        Assert.Equal(10, acct.LastSyncCount);
    }

    [Fact]
    public async Task Sync_CfAndQoj_SameHandle_RowsKeptSeparate()
    {
        // Ensure (Platform.Codeforces, "LuoxuanLove") and (Platform.Qoj, "LuoxuanLove") are
        // distinct keys and their submissions never mix. Seed a CF row first, then sync QOJ.
        const long qojContestId = 2575;

        await using var db = NewInMemory();

        // Seed a CF submission with the same id as the first QOJ submission in the fixture.
        // If Platform is not in the PK, the QOJ insert would collide and be silently ignored.
        var qojHtml = Fixtures.Read(ContestFixture);
        var firstQojId = QojSubmissionsParser.Parse(qojHtml).Submissions[0].Id;
        db.Submissions.Add(new Submission
        {
            Platform = Platform.Codeforces,
            Id = firstQojId,   // same numeric id, different platform
            Handle = Handle,
            GymId = 999999,
            ProblemIndex = "A",
            Verdict = "OK",
            SubmitTime = 1,
        });
        await db.SaveChangesAsync();

        var profileHtml = Fixtures.Read(ProfileFixture);
        var map = new Dictionary<string, string>
        {
            [$"https://qoj.ac/user/profile/{Handle}"] = profileHtml,
            [$"https://qoj.ac/contest/{qojContestId}/submissions"] = qojHtml,
        };

        var svc = new QojSyncService(db, new MapFetcher(map), NoWait());
        var stored = await svc.SyncAsync(Handle);

        // All 10 QOJ submissions must be stored despite the id collision with the CF row.
        Assert.Equal(10, stored);

        // CF row still there, QOJ rows all there, no mixing.
        Assert.Equal(1, await db.Submissions.Where(s => s.Platform == Platform.Codeforces).CountAsync());
        Assert.Equal(10, await db.Submissions.Where(s => s.Platform == Platform.Qoj).CountAsync());
    }
}
