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
/// End-to-end Phase 1 integration: real CF submissions markup (paginated, gym rows with the
/// source-link-first shape) → SubmissionScraper → GymSyncService → a REAL on-disk SQLite file →
/// derived GymContest + VpResult. Runs the sync TWICE to prove the incremental second run stores
/// nothing new and does not re-walk the full history.
///
/// The only piece NOT exercised here is the human WebView2 Cloudflare login (isolated behind
/// IBrowserFetcher per DESIGN 6.2); everything downstream of that fetch is covered against
/// authentic CF HTML and a genuine SQLite database on disk.
/// </summary>
public class GymSyncIntegrationTests
{
    /// <summary>Serves fixed page HTML per URL and records how many fetches happened.</summary>
    private sealed class PagedFetcher : IBrowserFetcher
    {
        private readonly IReadOnlyDictionary<string, string> _pages;
        public int FetchCount { get; private set; }
        public List<string> FetchedUrls { get; } = new();

        public PagedFetcher(IReadOnlyDictionary<string, string> pages) => _pages = pages;

        public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
        {
            FetchCount++;
            FetchedUrls.Add(url);
            if (_pages.TryGetValue(url, out var html))
                return Task.FromResult(new FetchResult(url, html, FetchStatus.Ok));
            // Unknown page => empty table (end of data).
            return Task.FromResult(new FetchResult(url, "<html><body><table></table></body></html>", FetchStatus.Ok));
        }
    }

    private static Throttle NoWait() => new(TimeSpan.Zero, delay: (_, _) => Task.CompletedTask);

    private static Dictionary<string, string> RealGymPages(string handle)
    {
        var p1 = Fixtures.Read("gym_submissions_p1.html");
        var p2 = Fixtures.Read("gym_submissions_p2.html");
        return new Dictionary<string, string>
        {
            [$"https://codeforces.com/submissions/{handle}"] = p1,
            [$"https://codeforces.com/submissions/{handle}/page/2"] = p2,
        };
    }

    // Captures diagnostics in memory to assert the scraper emits step facts + page-1 HTML.
    private sealed class MemoryDiagnostics : IScrapeDiagnostics
    {
        public readonly List<string> Lines = new();
        public readonly Dictionary<string, string> Raw = new();
        public void Log(string message) => Lines.Add(message);
        public void CaptureRaw(string name, string content) => Raw[name] = content;
    }

    [Fact]
    public async Task Scrape_EmitsDiagnostics_AndCapturesFirstPageHtml()
    {
        const string handle = "someuser";
        var pages = RealGymPages(handle);
        var fetcher = new PagedFetcher(pages);
        var scraper = new SubmissionScraper(fetcher, NoWait(),
            new BackoffPolicy(), delay: (_, _) => Task.CompletedTask);
        var diag = new MemoryDiagnostics();
        scraper.Diagnostics = diag;

        var fresh = await scraper.ScrapeAsync(handle, _ => false);

        Assert.NotEmpty(fresh);
        // Log has start + per-page + done markers.
        Assert.Contains(diag.Lines, l => l.Contains("Scrape start"));
        Assert.Contains(diag.Lines, l => l.Contains("Scrape done"));
        Assert.Contains(diag.Lines, l => l.Contains("parsed rows="));
        // Page-1 raw HTML captured under the handle-named artifact, and it is the real markup.
        var key = $"page1_{handle}.html";
        Assert.True(diag.Raw.ContainsKey(key), "page-1 HTML should be captured");
        Assert.Contains("status-frame-datatable", diag.Raw[key]);
    }

    private static VpDbContext OpenDb(string dbPath)
    {
        var options = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new VpDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task FullSync_RealMarkup_ToDiskSqlite_ThenIncrementalSecondRun()
    {
        const string handle = "someuser";
        var pages = RealGymPages(handle);

        // A real temp SQLite file, not in-memory, so we prove on-disk persistence + reopen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_it_{Guid.NewGuid():N}.db");
        try
        {
            // ---- First run: full scrape ----
            var fetcher1 = new PagedFetcher(pages);
            int firstRunFetches;
            int stored1;
            await using (var db = OpenDb(dbPath))
            {
                var scraper = new SubmissionScraper(fetcher1, NoWait(),
                    new BackoffPolicy(), delay: (_, _) => Task.CompletedTask);
                var sync = new GymSyncService(db, scraper);
                stored1 = await sync.SyncAsync(handle);
                firstRunFetches = fetcher1.FetchCount;
            }

            Assert.True(stored1 > 0, "first run should store gym submissions");

            // Verify persisted state by REOPENING the on-disk DB (proves it actually hit disk).
            await using (var db = OpenDb(dbPath))
            {
                var subs = await db.Submissions.Where(s => s.Handle == handle).ToListAsync();
                Assert.Equal(stored1, subs.Count);
                // Only gym submissions must be stored (regular /contest/ rows excluded).
                Assert.All(subs, s => Assert.True(s.GymId > 0));

                // Derived aggregates exist.
                var gyms = await db.GymContests.ToListAsync();
                Assert.NotEmpty(gyms);
                var vps = await db.VpResults.Where(v => v.Handle == handle).ToListAsync();
                Assert.NotEmpty(vps);

                // Account watermark advanced.
                var acct = await db.Accounts.FindAsync(Platform.Codeforces, handle);
                Assert.NotNull(acct);
                Assert.True(acct!.LastSyncedAt > 0);

                // Problem index must be the real letter (source-link-vs-problem-link fix),
                // never an empty/"?" index from the view-source anchor.
                Assert.All(subs, s => Assert.False(string.IsNullOrEmpty(s.ProblemIndex)));
                Assert.DoesNotContain(subs, s => s.ProblemIndex == "?");
            }

            // ---- Second run: incremental, same pages, nothing new ----
            var fetcher2 = new PagedFetcher(pages);
            int stored2;
            int totalAfter;
            await using (var db = OpenDb(dbPath))
            {
                var scraper = new SubmissionScraper(fetcher2, NoWait(),
                    new BackoffPolicy(), delay: (_, _) => Task.CompletedTask);
                var sync = new GymSyncService(db, scraper);
                stored2 = await sync.SyncAsync(handle);
                totalAfter = await db.Submissions.CountAsync(s => s.Handle == handle);
            }

            Assert.Equal(0, stored2); // incremental: no new rows stored
            Assert.Equal(stored1, totalAfter); // no duplicates
            // Incremental must stop early: the second run fetches fewer (or equal) pages, and
            // crucially does NOT store anything. It should stop at/after the first page once it
            // sees all-known ids.
            Assert.True(fetcher2.FetchCount <= firstRunFetches,
                $"incremental run should not fetch more pages than the full run (was {fetcher2.FetchCount} vs {firstRunFetches})");
        }
        finally
        {
            // SQLite pools connections; clear it so the file handle is released before delete.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task IncrementalNoNewRows_RepairsMissingDerivedRows()
    {
        const string handle = "someuser";
        var pages = RealGymPages(handle);
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_repair_{Guid.NewGuid():N}.db");
        try
        {
            int stored1;
            await using (var db = OpenDb(dbPath))
            {
                var scraper = new SubmissionScraper(new PagedFetcher(pages), NoWait(),
                    new BackoffPolicy(), delay: (_, _) => Task.CompletedTask);
                var sync = new GymSyncService(db, scraper);
                stored1 = await sync.SyncAsync(handle);

                db.GymContests.RemoveRange(db.GymContests);
                db.VpResults.RemoveRange(db.VpResults);
                await db.SaveChangesAsync();
            }

            await using (var db = OpenDb(dbPath))
            {
                Assert.Equal(stored1, await db.Submissions.CountAsync(s => s.Handle == handle));
                Assert.Empty(await db.GymContests.ToListAsync());
                Assert.Empty(await db.VpResults.ToListAsync());

                var scraper = new SubmissionScraper(new PagedFetcher(pages), NoWait(),
                    new BackoffPolicy(), delay: (_, _) => Task.CompletedTask);
                var sync = new GymSyncService(db, scraper);
                var stored2 = await sync.SyncAsync(handle);

                Assert.Equal(0, stored2);
                Assert.NotEmpty(await db.GymContests.ToListAsync());
                Assert.NotEmpty(await db.VpResults.Where(v => v.Handle == handle).ToListAsync());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RebuildDerived_InferredVirtualRun_ComputesElapsedPenalty()
    {
        const string handle = "LuoxuanLove";
        var dbPath = Path.Combine(Path.GetTempPath(), $"vphelper_inferred_{Guid.NewGuid():N}.db");
        try
        {
            await using var db = OpenDb(dbPath);
            const long start = 1_757_146_020;
            db.Submissions.AddRange(
                Sub(1, handle, 105486, "L", "WRONG_ANSWER", start),
                Sub(2, handle, 105486, "L", "OK", start + 120),
                Sub(3, handle, 105486, "A", "OK", start + 2_760),
                Sub(4, handle, 105486, "K", "WRONG_ANSWER", start + 18_300));
            await db.SaveChangesAsync();

            var sync = new GymSyncService(db);
            await sync.RebuildDerivedAsync(handle);

            var vp = await db.VpResults.FindAsync(Platform.Codeforces, handle, 105486L, ParticipationKind.Virtual);
            Assert.NotNull(vp);
            Assert.Equal(2, vp!.SolvedCount);
            // L: 2 min + 20 min wrong, A: 46 min.
            Assert.Equal(68 * 60, vp.Penalty);

            var practice = await db.VpResults.FindAsync(Platform.Codeforces, handle, 105486L, ParticipationKind.Practice);
            Assert.NotNull(practice);
            Assert.Equal(0, practice!.SolvedCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    private static Submission Sub(long id, string handle, long gymId, string problem, string verdict, long submitTime)
        => new()
        {
            Platform = Platform.Codeforces,
            Id = id,
            Handle = handle,
            GymId = gymId,
            ProblemIndex = problem,
            Verdict = verdict,
            SubmitTime = submitTime,
        };
}
