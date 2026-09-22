using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VPHelper.Core.Analysis;
using VPHelper.Core.Srk;

namespace VPHelper.Core.Rankland;

/// <summary>Abstraction over fetching raw text (config.yaml / srk json) from a source.</summary>
public interface IRawContentSource
{
    /// <summary>Fetch text at a raw URL. Returns null on 404 / missing.</summary>
    Task<RawContent?> GetAsync(string url, string? etag = null, CancellationToken ct = default);
}

/// <summary>Fetched raw content plus caching metadata.</summary>
public record RawContent(string Url, string? Body, string? ETag, bool NotModified);

/// <summary>A fully resolved official contest ready to persist.</summary>
public record ResolvedOfficial(
    string Id,
    string Name,
    string CategoryPath,
    ContestType Category,
    string SrkUrl,
    long? StartAt,
    long? DurationSeconds,
    int ProblemCount,
    int[]? MedalCounts,
    SrkRanklist Ranklist);

/// <summary>
/// Reads rankland official standings from the public <c>algoux/srk-collection</c> repo
/// (DESIGN 6.4): parse <c>official/config.yaml</c>, expand leaves, build raw URLs, fetch
/// and parse each srk. Fetching is delegated to <see cref="IRawContentSource"/> so this
/// class stays testable against local fixtures.
/// </summary>
public class RanklandClient
{
    public const string ConfigUrl = RanklandPaths.ConfigUrl;

    private readonly IRawContentSource _source;

    public RanklandClient(IRawContentSource source) => _source = source;

    /// <summary>Fetch and expand config.yaml into the full leaf list.</summary>
    public async Task<IReadOnlyList<RanklandLeaf>> GetLeavesAsync(CancellationToken ct = default)
    {
        var content = await _source.GetAsync(ConfigUrl, null, ct).ConfigureAwait(false);
        if (content?.Body == null)
            throw new InvalidOperationException("Failed to fetch official/config.yaml");
        return RanklandConfigParser.Parse(content.Body);
    }

    /// <summary>Build the raw srk URL for a leaf from its ancestor path segments.</summary>
    public static string BuildSrkUrl(RanklandLeaf leaf)
        => leaf.BuildRawUrl(RanklandPaths.RawBase);

    /// <summary>Fetch and parse the srk for one leaf into a <see cref="ResolvedOfficial"/>.</summary>
    public async Task<ResolvedOfficial?> ResolveAsync(RanklandLeaf leaf, string? etag = null,
        CancellationToken ct = default)
    {
        var url = BuildSrkUrl(leaf);
        var content = await _source.GetAsync(url, etag, ct).ConfigureAwait(false);
        if (content == null || content.Body == null) return null;

        var rl = SrkParser.Parse(content.Body);
        var medalProvider = new MedalRuleProvider();
        var line = medalProvider.FromSrkSeries(rl);
        int[]? counts = line != null
            ? new[] { line.Gold, line.Silver - line.Gold, line.Bronze - line.Silver }
            : null;

        var category = ContestClassifier.Classify(leaf.Name, string.Join('/', leaf.PathSegments), leaf.CategoryPath);

        return new ResolvedOfficial(
            Id: leaf.Path,
            Name: string.IsNullOrEmpty(leaf.Name) ? rl.Contest.Title : leaf.Name,
            CategoryPath: leaf.CategoryPath,
            Category: category,
            SrkUrl: url,
            StartAt: rl.Contest.StartAt,
            DurationSeconds: rl.Contest.DurationSeconds,
            ProblemCount: rl.Problems.Count,
            MedalCounts: counts,
            Ranklist: rl);
    }
}
