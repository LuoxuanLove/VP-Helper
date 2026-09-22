using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Scraping;

namespace VPHelper.Core.Services;

/// <summary>
/// Fetches and parses the Codeforces <c>contest.status</c> API for one gym + handle through the
/// logged-in browser context (DESIGN 6.2). This is the only source of <c>participantType</c>, which
/// is what separates a virtual run from post-contest practice — the HTML submissions page does not
/// carry it. Fetching goes through <see cref="IBrowserFetcher"/> so the request reuses the session
/// cookies / TLS fingerprint / Cloudflare clearance, exactly like the HTML scrape.
/// </summary>
public class ContestStatusApi
{
    private readonly IBrowserFetcher _fetcher;
    private readonly Throttle _throttle;
    private readonly BackoffPolicy _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ContestStatusApi(
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

    public IScrapeDiagnostics? Diagnostics { get; set; }

    /// <summary>
    /// Fetch the participant-typed submissions for one gym. Returns the parse result: <c>Ok=true</c>
    /// with rows (possibly empty), or <c>Ok=false</c> with a comment (private gym needing auth, bad
    /// id, CF challenge unresolved, network give-up) so the caller can report it rather than treating
    /// the gym as truly empty.
    /// </summary>
    public async Task<ContestStatusResult> FetchGymAsync(long gymId, string handle,
        CancellationToken ct = default)
    {
        var url = ContestStatusParser.BuildUrl(gymId, handle);
        var fetch = await FetchWithRetryAsync(url, ct).ConfigureAwait(false);
        if (fetch == null)
        {
            Diagnostics?.Log($"contest.status gym {gymId}: fetch gave up after retries");
            return new ContestStatusResult(false, Array.Empty<ApiSubmission>(), "fetch failed after retries");
        }

        var parsed = ContestStatusParser.Parse(fetch.Body);
        Diagnostics?.Log($"contest.status gym {gymId}: ok={parsed.Ok}, rows={parsed.Submissions.Count}"
            + (parsed.Comment is { } c ? $", comment={c}" : string.Empty));
        return parsed;
    }

    /// <summary>Fetch a URL through the browser with throttle + backoff, surfacing CF challenges.</summary>
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

            Diagnostics?.Log($"  api attempt {attempt}/{_backoff.MaxRetries}: status={result.Status}"
                + $", bodyLen={result.Body?.Length ?? 0}"
                + (string.IsNullOrEmpty(result.Message) ? string.Empty : $", msg={result.Message}"));

            switch (result.Status)
            {
                case FetchStatus.Ok:
                    return result;
                case FetchStatus.CloudflareChallenge:
                    CloudflareChallengeDetected?.Invoke(url);
                    if (attempt == _backoff.MaxRetries) return null;
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
}
