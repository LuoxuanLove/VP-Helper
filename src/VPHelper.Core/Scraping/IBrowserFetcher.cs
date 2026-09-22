using System.Threading;
using System.Threading.Tasks;

namespace VPHelper.Core.Scraping;

/// <summary>
/// Abstraction over a logged-in, Cloudflare-cleared browser context (DESIGN 6.2).
/// The WinUI layer implements this with WebView2; Core stays platform-neutral and
/// testable via mocks. All fetching for Codeforces goes through this so requests
/// reuse the browser's cookies / TLS fingerprint / challenge state.
/// </summary>
public interface IBrowserFetcher
{
    /// <summary>
    /// Fetch a URL within the browser context and return the response body (HTML or JSON text).
    /// Implementations should navigate (or page-context fetch) and return document text.
    /// </summary>
    Task<FetchResult> FetchAsync(string url, CancellationToken ct = default);
}

/// <summary>Outcome of a single browser fetch.</summary>
public record FetchResult(
    string Url,
    string? Body,
    FetchStatus Status,
    string? Message = null)
{
    public bool IsSuccess => Status == FetchStatus.Ok;
}

public enum FetchStatus
{
    Ok,
    /// <summary>Response looks like a Cloudflare challenge page; user must complete verification.</summary>
    CloudflareChallenge,
    /// <summary>Rate limited (HTTP 429 or CF rate page). Caller should back off.</summary>
    RateLimited,
    /// <summary>Network / navigation error.</summary>
    Error,
}
