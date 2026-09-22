using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VPHelper.Core.Scraping;

/// <summary>Progress callback payload during a sync.</summary>
public record ScrapeProgress(int Page, int NewSubmissions, string Message);

public record ScrapeOptions
{
    public int MaxPages { get; init; } = 200;
    /// <summary>Stop early when a submission id already present locally is seen (incremental).</summary>
    public bool Incremental { get; init; } = true;
}

/// <summary>
/// Orchestrates paginated scraping of <c>/submissions/{handle}</c> through an
/// <see cref="IBrowserFetcher"/> with throttling, exponential-backoff retry, Cloudflare
/// detection, and incremental stop (DESIGN 6.2 / 2.4). Pure orchestration — no DB or UI
/// dependency; callers supply the "already seen" predicate and persist results.
/// </summary>
public class SubmissionScraper
{
    private readonly IBrowserFetcher _fetcher;
    private readonly Throttle _throttle;
    private readonly BackoffPolicy _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SubmissionScraper(
        IBrowserFetcher fetcher,
        Throttle throttle,
        BackoffPolicy? backoff = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _fetcher = fetcher;
        _throttle = throttle;
        _backoff = backoff ?? new BackoffPolicy();
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    /// <summary>Raised when a Cloudflare challenge is detected; UI should surface the WebView.</summary>
    public event Action<string>? CloudflareChallengeDetected;

    /// <summary>
    /// Optional diagnostics sink. When set, receives per-step facts (page url, fetch status, body
    /// size, parsed/gym row counts, stop reason) plus the raw HTML of the first page. Lets a real
    /// run be diagnosed from a log file instead of screenshots when live DOM differs from fixtures.
    /// </summary>
    public IScrapeDiagnostics? Diagnostics { get; set; }

    /// <summary>
    /// Scrape all gym submissions for a handle. <paramref name="isKnown"/> returns true if a
    /// submission id already exists locally (used to stop incremental sync). Returns the list of
    /// newly discovered submissions across all pages, in page order.
    /// </summary>
    public async Task<IReadOnlyList<ParsedSubmission>> ScrapeAsync(
        string handle,
        Func<long, bool> isKnown,
        ScrapeOptions? options = null,
        IProgress<ScrapeProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ScrapeOptions();
        var all = new List<ParsedSubmission>();
        var seenThisRun = new HashSet<long>();
        int? maxPage = null;

        Diagnostics?.Log($"=== Scrape start: handle='{handle}', incremental={options.Incremental}, maxPages={options.MaxPages} ===");

        for (int page = 1; page <= options.MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (maxPage != null && page > maxPage) { Diagnostics?.Log($"Stop: page {page} > maxPage {maxPage}"); break; }

            var url = BuildPageUrl(handle, page);
            var result = await FetchWithRetryAsync(url, ct).ConfigureAwait(false);
            if (result == null) { Diagnostics?.Log($"Page {page}: fetch gave up after retries ({url})"); break; }

            var body = result.Body ?? string.Empty;
            Diagnostics?.Log($"Page {page}: {url} -> status={result.Status}, bodyLen={body.Length}");
            // Capture the first page's raw HTML so real-DOM mismatches can be diagnosed precisely.
            if (page == 1) Diagnostics?.CaptureRaw($"page1_{handle}.html", body);

            var parsed = SubmissionsParser.Parse(body);
            maxPage ??= parsed.MaxPageIndex;
            Diagnostics?.Log($"Page {page}: parsed rows={parsed.Submissions.Count}, gymRows={parsed.Submissions.Count(s => s.GymId > 0)}, maxPageIndex={parsed.MaxPageIndex?.ToString() ?? "null"}");

            int newCount = 0;
            bool hitKnown = false;
            int unseenOnPage = 0; // ids not already collected this run (detects page repeats)
            foreach (var s in parsed.Submissions)
            {
                if (!seenThisRun.Add(s.Id)) continue; // already collected this run: repeated page
                unseenOnPage++;
                if (options.Incremental && isKnown(s.Id))
                {
                    hitKnown = true;
                    continue;
                }
                all.Add(s);
                newCount++;
            }

            progress?.Report(new ScrapeProgress(page, newCount, $"Page {page}: {newCount} new"));

            // Empty page => end of data.
            if (parsed.Submissions.Count == 0) { Diagnostics?.Log($"Stop: page {page} empty"); break; }
            // No pagination info and this page introduced no new ids => same page repeating; stop.
            if (maxPage == null && unseenOnPage == 0) { Diagnostics?.Log($"Stop: page {page} no new ids and no pagination"); break; }
            // Incremental: once we hit a known id and the page yielded no new rows, stop.
            if (options.Incremental && hitKnown && newCount == 0) { Diagnostics?.Log($"Stop: incremental hit known id on page {page}, no new rows"); break; }
        }

        Diagnostics?.Log($"=== Scrape done: {all.Count} new submission(s) collected ===");
        return all;
    }

    /// <summary>Fetch a URL, retrying with backoff; surfaces Cloudflare via the event and keeps retrying.</summary>
    private async Task<FetchResult?> FetchWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= _backoff.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await _throttle.WaitAsync(ct).ConfigureAwait(false);

            FetchResult result;
            try
            {
                result = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = new FetchResult(url, null, FetchStatus.Error, ex.Message);
            }

            // Per-attempt fact: status + message + body size. This is the only window into WHY a
            // real run gives up — CF challenges/429/errors can't be reproduced against fixtures.
            Diagnostics?.Log($"  attempt {attempt}/{_backoff.MaxRetries}: status={result.Status}"
                + $", bodyLen={result.Body?.Length ?? 0}"
                + (string.IsNullOrEmpty(result.Message) ? string.Empty : $", msg={result.Message}"));

            switch (result.Status)
            {
                case FetchStatus.Ok:
                    return result;
                case FetchStatus.CloudflareChallenge:
                    CloudflareChallengeDetected?.Invoke(url);
                    // Wait then retry (user is expected to solve the challenge in the WebView).
                    if (attempt == _backoff.MaxRetries)
                    {
                        Diagnostics?.Log($"  give up: Cloudflare challenge unresolved after {_backoff.MaxRetries + 1} attempts");
                        return null;
                    }
                    await _delay(_backoff.DelayFor(attempt), ct).ConfigureAwait(false);
                    break;
                case FetchStatus.RateLimited:
                case FetchStatus.Error:
                    if (attempt == _backoff.MaxRetries) return null;
                    await _delay(_backoff.DelayFor(attempt), ct).ConfigureAwait(false);
                    break;
            }
        }
        return null;
    }

    internal static string BuildPageUrl(string handle, int page)
        => page <= 1
            ? $"https://codeforces.com/submissions/{handle}"
            : $"https://codeforces.com/submissions/{handle}/page/{page}";
}
