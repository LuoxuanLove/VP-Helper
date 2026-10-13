using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VPHelper.Core.Rankland;

/// <summary>
/// Fetches raw text (config.yaml / srk json) from the algoux/srk-collection repo.
///
/// <para><b>Why this is more than a bare HttpClient.</b> The canonical host
/// <c>raw.githubusercontent.com</c> is frequently unreachable from mainland-China networks: a
/// common proxy/split-tunnel setup resolves it to a fake-IP (e.g. 198.18.0.0/16) whose TLS
/// handshake is then dropped, so every request fails with a schannel "failed to receive
/// handshake" error. Observed on the target machine: raw.githubusercontent fails 100%, while the
/// public CDN mirrors that map the same GitHub repo (jsDelivr, fastly, gcore) succeed — but even
/// they drop roughly one handshake in three under the same interference.</para>
///
/// <para>So this source does two things a plain client does not:
///  • <b>Mirror fallback</b> — a raw.githubusercontent URL is rewritten to a list of equivalent
///    mirror URLs (see <see cref="MirrorsFor"/>); they are tried in order until one yields a
///    definitive result (2xx / 304 / 404).
///  • <b>Retry with backoff</b> — each mirror is retried a few times, since the interference is
///    intermittent rather than total; this is the dominant lever for getting through.</para>
///
/// <para>The first mirror host that returns a definitive result is remembered
/// (<see cref="_preferredHost"/>) and tried first on subsequent calls, so a run doesn't keep
/// paying the timeout on a dead host once a working one is found.</para>
///
/// Supports ETag revalidation (304 short-circuits the body read).
/// </summary>
public sealed class HttpRawContentSource : IRawContentSource, IDisposable
{
    private const string RawHost = "raw.githubusercontent.com";
    private const string RawPrefix = "https://" + RawHost + "/";

    // Mirror URL templates. {0} is the repo-relative path after ".../{owner}/{repo}/{ref}/".
    // jsDelivr uses gh/{owner}/{repo}@{ref}/{path}; the proxies prepend the full raw URL.
    private static readonly string[] MirrorTemplates =
    {
        "https://raw.githubusercontent.com/{0}",   // canonical (works off China networks)
        "https://cdn.jsdelivr.net/gh/{1}",          // jsDelivr (owner/repo@ref/path)
        "https://fastly.jsdelivr.net/gh/{1}",
        "https://gcore.jsdelivr.net/gh/{1}",
        "https://ghproxy.net/https://raw.githubusercontent.com/{0}",
    };

    private const int PerMirrorAttempts = 3;

    private readonly HttpClient _http;
    // The scheme+host of the mirror that last succeeded, tried first next time. Null until one works.
    private volatile string? _preferredHost;

    public HttpRawContentSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("VPHelper/1.0"))
            _http.DefaultRequestHeaders.Add("User-Agent", "VPHelper/1.0");
    }

    public async Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await GetWithMirrorsAsync(url, etag, budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("官方榜单请求超时（45 秒），请稍后重试。");
        }
    }

    private async Task<RawContent?> GetWithMirrorsAsync(string url, string? etag, CancellationToken ct)
    {
        var mirrors = MirrorsFor(url);
        Exception? lastError = null;

        foreach (var mirror in mirrors)
        {
            for (var attempt = 0; attempt < PerMirrorAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await FetchOnceAsync(mirror, url, etag, ct).ConfigureAwait(false);
                    // A definitive result (found / 304 / 404). Remember the working host and return.
                    RememberHost(mirror);
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // caller-requested cancellation is not a transient error
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    // Transient: TLS handshake drop, timeout, or connection reset. Back off and retry.
                    lastError = ex;
                    if (attempt < PerMirrorAttempts - 1)
                        await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct).ConfigureAwait(false);
                }
            }
        }

        throw new HttpRequestException(
            $"All {mirrors.Count} mirror(s) failed for {url}. The network may be blocking GitHub raw " +
            "content and its CDN mirrors (TLS handshake drops).", lastError);
    }

    /// <summary>Fetch a single mirror URL once. Returns null on 404; throws on transient failures.</summary>
    private async Task<RawContent?> FetchOnceAsync(string mirrorUrl, string canonicalUrl, string? etag, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, mirrorUrl);
        if (!string.IsNullOrEmpty(etag))
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotModified)
            return new RawContent(canonicalUrl, null, etag, NotModified: true);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // Only the canonical host emits usable ETags; mirrors' ETags don't revalidate against raw,
        // so keep the etag only when we actually hit raw.githubusercontent.
        var newEtag = mirrorUrl.StartsWith(RawPrefix, StringComparison.Ordinal) ? resp.Headers.ETag?.Tag : null;
        return new RawContent(canonicalUrl, body, newEtag, NotModified: false);
    }

    /// <summary>
    /// Expand a canonical raw.githubusercontent URL into the ordered list of mirror URLs to try.
    /// A previously-successful host is moved to the front. Non-raw URLs are returned as-is (single).
    /// </summary>
    internal IReadOnlyList<string> MirrorsFor(string url)
    {
        if (!url.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase))
            return new[] { url };

        // path = "{owner}/{repo}/{ref}/{rest...}"
        var path = url.Substring(RawPrefix.Length);
        var jsDelivrPath = ToJsDelivrPath(path);

        var urls = new List<string>(MirrorTemplates.Length);
        foreach (var template in MirrorTemplates)
            urls.Add(string.Format(template, path, jsDelivrPath));

        var preferred = _preferredHost;
        if (preferred != null)
        {
            // Stable move-to-front: the mirror whose scheme+host matches the last success goes first.
            var idx = urls.FindIndex(u => HostOf(u) == preferred);
            if (idx > 0)
            {
                var hit = urls[idx];
                urls.RemoveAt(idx);
                urls.Insert(0, hit);
            }
        }
        return urls;
    }

    /// <summary>Turn "owner/repo/ref/rest..." into jsDelivr's "owner/repo@ref/rest...".</summary>
    private static string ToJsDelivrPath(string ownerRepoRefRest)
    {
        var parts = ownerRepoRefRest.Split('/');
        if (parts.Length < 4) return ownerRepoRefRest; // unexpected shape; leave as-is
        var owner = parts[0];
        var repo = parts[1];
        var gitRef = parts[2];
        var rest = string.Join('/', parts, 3, parts.Length - 3);
        return $"{owner}/{repo}@{gitRef}/{rest}";
    }

    private void RememberHost(string mirrorUrl) => _preferredHost = HostOf(mirrorUrl);

    /// <summary>Scheme+host prefix used to identify a mirror (e.g. "https://cdn.jsdelivr.net").</summary>
    private static string HostOf(string url)
    {
        // Find the end of "scheme://host".
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return url;
        var hostStart = schemeEnd + 3;
        var slash = url.IndexOf('/', hostStart);
        return slash < 0 ? url : url.Substring(0, slash);
    }

    public void Dispose() => _http.Dispose();
}
