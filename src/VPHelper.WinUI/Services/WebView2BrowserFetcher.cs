using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using VPHelper.Core.Scraping;

namespace VPHelper.WinUI.Services;

/// <summary>
/// <see cref="IBrowserFetcher"/> backed by a shared, already-logged-in WebView2 (DESIGN 6.2).
/// Fetches happen inside the browser context (page-context <c>fetch</c>) so requests reuse the
/// session cookies, TLS/JA3 fingerprint, and Cloudflare clearance. Detects Cloudflare challenge
/// pages and surfaces them rather than hammering through.
///
/// The fetch result is returned asynchronously via <c>chrome.webview.postMessage</c> +
/// <see cref="CoreWebView2.WebMessageReceived"/>, NOT via <c>ExecuteScriptAsync</c>'s return value.
/// <c>ExecuteScriptAsync</c> does not await promises — an <c>async</c> IIFE evaluates to a pending
/// Promise that serializes to the literal string <c>"{}"</c>, which is why the earlier
/// return-value approach failed every fetch with a JSON conversion error at byte position 1.
/// Each fetch carries a unique token so its posted message can be matched back to the awaiting call.
/// </summary>
public sealed class WebView2BrowserFetcher : IBrowserFetcher
{
    private readonly CoreWebView2 _core;
    private readonly DispatcherQueue _dispatcher;

    // token -> awaiting fetch. ConcurrentDictionary: WebMessageReceived fires on the UI thread
    // while FetchAsync may be awaited from a background thread.
    private readonly ConcurrentDictionary<string, PendingFetch> _pending = new();

    public WebView2BrowserFetcher(CoreWebView2 core, DispatcherQueue dispatcher)
    {
        _core = core;
        _dispatcher = dispatcher;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    public async Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<FetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = new PendingFetch(url, tcs);

        try
        {
            // Marshal the script injection onto the UI thread that owns the WebView2. The fetch
            // result does NOT come back here — it arrives via OnWebMessageReceived keyed by token.
            bool enqueued = _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await _core.ExecuteScriptAsync(BuildScript(url, token));
                    // Script started; the posted message will resolve the TCS.
                }
                catch (Exception ex)
                {
                    if (_pending.TryRemove(token, out var p))
                        p.Tcs.TrySetResult(new FetchResult(url, null, FetchStatus.Error, ex.Message));
                }
            });
            if (!enqueued)
            {
                _pending.TryRemove(token, out _);
                return new FetchResult(url, null, FetchStatus.Error, "UI dispatcher unavailable");
            }

            using (ct.Register(() =>
            {
                if (_pending.TryRemove(token, out var p)) p.Tcs.TrySetCanceled();
            }))
                return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// Page-context fetch that posts its result back as a JSON envelope tagged with
    /// <paramref name="token"/>. Reuses cookies + challenge state via <c>credentials: 'include'</c>.
    /// </summary>
    private static string BuildScript(string url, string token) => $@"
(async () => {{
  const token = {JsSafe(token)};
  try {{
    const resp = await fetch({JsSafe(url)}, {{ credentials: 'include', redirect: 'follow' }});
    const text = await resp.text();
    const cfMitigated = resp.headers.get('cf-mitigated') || '';
    window.chrome.webview.postMessage(JSON.stringify({{ token, status: resp.status, cf: cfMitigated, body: text }}));
  }} catch (e) {{
    window.chrome.webview.postMessage(JSON.stringify({{ token, status: -1, cf: '', body: '', error: String(e) }}));
  }}
}})()";

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch { return; } // not one of our string messages (e.g. an object from another page)
        if (string.IsNullOrEmpty(json)) return;

        FetchEnvelope? envelope;
        try { envelope = System.Text.Json.JsonSerializer.Deserialize<FetchEnvelope>(json); }
        catch { return; } // not our envelope
        if (envelope?.Token == null || !_pending.TryRemove(envelope.Token, out var pending))
            return;

        pending.Tcs.TrySetResult(Interpret(pending.Url, envelope));
    }

    /// <summary>Map a returned envelope to a <see cref="FetchResult"/>, detecting CF/ratelimit.</summary>
    private FetchResult Interpret(string url, FetchEnvelope envelope)
    {
        // Cloudflare challenge detection: cf-mitigated: challenge, or a challenge interstitial body.
        if (string.Equals(envelope.Cf, "challenge", StringComparison.OrdinalIgnoreCase)
            || LooksLikeChallenge(envelope.Body, envelope.Status))
            return new FetchResult(url, null, FetchStatus.CloudflareChallenge, "Cloudflare challenge");

        if (envelope.Status == 429)
            return new FetchResult(url, null, FetchStatus.RateLimited, "HTTP 429");
        if (envelope.Status < 0 || envelope.Status >= 400)
            return new FetchResult(url, envelope.Body, FetchStatus.Error,
                string.IsNullOrEmpty(envelope.Error) ? $"HTTP {envelope.Status}" : envelope.Error);

        return new FetchResult(url, envelope.Body, FetchStatus.Ok);
    }

    /// <summary>Heuristic Cloudflare interstitial detection on the returned HTML.</summary>
    internal static bool LooksLikeChallenge(string? body, int status)
    {
        if (string.IsNullOrEmpty(body)) return false;
        if (status is 403 or 503)
        {
            if (body.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
                || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string JsSafe(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    private readonly record struct PendingFetch(string Url, TaskCompletionSource<FetchResult> Tcs);

    private sealed class FetchEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("token")] public string? Token { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("status")] public int Status { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("cf")] public string? Cf { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("body")] public string? Body { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("error")] public string? Error { get; set; }
    }
}
