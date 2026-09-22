using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Analysis;
using VPHelper.Core.Rankland;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Opt-in live integration against the real algoux/srk-collection repo over plain HTTPS
/// (raw.githubusercontent is NOT behind Cloudflare, so a bare HttpClient is legitimate here —
/// this is the DESIGN 6.2 "回退路径" for endpoints confirmed challenge-free).
///
/// Skipped by default so the normal suite stays offline/deterministic. Enable with:
///   set VPHELPER_LIVE=1  (Windows)  /  export VPHELPER_LIVE=1
/// then: dotnet test --filter Category=Live
/// </summary>
[Trait("Category", "Live")]
public class LiveRanklandTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("VPHELPER_LIVE") == "1";

    /// <summary>Minimal HttpClient-backed source; only used by the opt-in live tests.</summary>
    private sealed class HttpSource : IRawContentSource
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("VPHelper-test/1.0");
            return c;
        }

        public async Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(etag))
                req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                return new RawContent(url, null, etag, NotModified: true);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var tag = resp.Headers.ETag?.Tag;
            return new RawContent(url, body, tag, NotModified: false);
        }
    }

    [Fact]
    public async Task Live_Config_Expands_And_KnownLeaf_Resolves_And_Ranks()
    {
        if (!Enabled)
            return; // opt-in only

        var client = new RanklandClient(new HttpSource());

        // 1. config.yaml expands to the full leaf set.
        var leaves = await client.GetLeavesAsync();
        Assert.True(leaves.Count > 100, $"expected >100 leaves, got {leaves.Count}");

        // 2. Resolve a known, populated regional (macau 2023) end-to-end.
        var leaf = leaves.FirstOrDefault(l => l.Path == "icpc2023macau");
        Assert.NotNull(leaf);

        var resolved = await client.ResolveAsync(leaf!);
        Assert.NotNull(resolved);
        Assert.True(resolved!.Ranklist.Rows.Count > 50);
        Assert.True(resolved.ProblemCount > 0);

        // 3. Full pipeline: rank standings, insert a champion-equal VP → rank 1 / gold.
        var ranked = RankingEngine.RankStandings(resolved.Ranklist);
        var champ = ranked[0];
        var res = RankingEngine.InsertVp(resolved.Ranklist,
            new ScoreEntry(champ.Solved, champ.PenaltySeconds), new MedalRuleProvider());
        Assert.Equal(1, res.Rank);
        Assert.Equal(Medal.Gold, res.Medal);
    }

    [Fact]
    public async Task Live_Etag_SecondFetch_NotModified()
    {
        if (!Enabled)
            return;

        var source = new HttpSource();
        var first = await source.GetAsync(RanklandPaths.ConfigUrl);
        Assert.NotNull(first?.Body);
        if (string.IsNullOrEmpty(first!.ETag))
            return; // server didn't supply an ETag; nothing to assert

        var second = await source.GetAsync(RanklandPaths.ConfigUrl, first.ETag);
        Assert.NotNull(second);
        Assert.True(second!.NotModified, "expected 304 Not Modified on ETag revalidation");
    }

    /// <summary>
    /// The production <see cref="HttpRawContentSource"/> (mirror fallback + retry) must fetch
    /// config.yaml even when raw.githubusercontent is unreachable — exercises the China-network
    /// path where only the CDN mirrors get through. This is the source the app actually ships.
    /// </summary>
    [Fact]
    public async Task Live_ProductionSource_WithMirrorFallback_FetchesConfig()
    {
        if (!Enabled)
            return;

        using var source = new HttpRawContentSource();
        var content = await source.GetAsync(RanklandPaths.ConfigUrl);

        Assert.NotNull(content?.Body);
        // config.yaml is a sizeable YAML document; a truncated/error body would be far smaller.
        Assert.True(content!.Body!.Length > 1000, $"config body too short: {content.Body!.Length} bytes");

        // And it parses into the full leaf set through the real client.
        var leaves = await new RanklandClient(source).GetLeavesAsync();
        Assert.True(leaves.Count > 100, $"expected >100 leaves, got {leaves.Count}");
    }
}
