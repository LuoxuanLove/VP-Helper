using System;
using System.Collections.Generic;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace VPHelper.Core.Rankland;

/// <summary>A leaf contest expanded from official/config.yaml.</summary>
public class RanklandLeaf
{
    /// <summary>Leaf directory/file path segment (e.g. "icpc2024invitational-wuhan").</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Display name from config.yaml (e.g. "2024-05-02 武汉邀请赛").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>All ancestor path segments from root to leaf, inclusive.</summary>
    public List<string> PathSegments { get; set; } = new();
    /// <summary>Top-level category path (first segment, e.g. "icpc").</summary>
    public string CategoryPath => PathSegments.Count > 0 ? PathSegments[0] : string.Empty;
    /// <summary>Alias for the top-level category path segment.</summary>
    public string TopCategory => CategoryPath;
    /// <summary>format field, typically "srk.json".</summary>
    public string Format { get; set; } = "srk.json";

    /// <summary>
    /// Repo-relative path of the srk file (under repo root), e.g.
    /// <c>official/icpc/icpc2024/icpc2024invitational-wuhan.srk.json</c>.
    /// Real layout (2026-07-06 verified): flat file named after the leaf path segment,
    /// placed directly in its group dir; NO repeated leaf directory. Depth varies.
    /// </summary>
    public string RelativePath
    {
        get
        {
            var sb = new StringBuilder("official");
            foreach (var seg in PathSegments)
            {
                sb.Append('/');
                sb.Append(seg);
            }
            sb.Append(".srk.json");
            return sb.ToString();
        }
    }

    /// <summary>Build the full raw.githubusercontent URL for this leaf's srk file.</summary>
    public string BuildRawUrl(string baseRaw = RanklandPaths.RawBase)
        => baseRaw.TrimEnd('/') + "/" + RelativePath;
}

public static class RanklandPaths
{
    public const string RawBase = "https://raw.githubusercontent.com/algoux/srk-collection/master";
    public const string ConfigUrl = RawBase + "/official/config.yaml";
    public const string OfficialCollectionUrl = "https://rl.algoux.cn/collection/official";
    public const string Attribution = "algoux/srk-collection (AGPL-3.0)";

    /// <summary>Build the public Rankland page URL for an official contest ID.</summary>
    public static string? BuildOfficialContestUrl(string? officialId)
    {
        if (string.IsNullOrWhiteSpace(officialId))
            return null;

        return $"{OfficialCollectionUrl}?rankId={Uri.EscapeDataString(officialId)}";
    }
}

/// <summary>Parses the nested official/config.yaml tree into a flat list of leaves.</summary>
public static class RanklandConfigParser
{
    public static List<RanklandLeaf> Parse(string yaml)
    {
        var leaves = new List<RanklandLeaf>();
        var stream = new YamlStream();
        using var reader = new System.IO.StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0) return leaves;

        var root = stream.Documents[0].RootNode;
        if (root is not YamlMappingNode rootMap) return leaves;

        // root: { children: [...] }
        if (rootMap.Children.TryGetValue(new YamlScalarNode("root"), out var rootNode)
            && rootNode is YamlMappingNode rootObj)
        {
            Walk(rootObj, new List<string>(), leaves);
        }
        else
        {
            Walk(rootMap, new List<string>(), leaves);
        }
        return leaves;
    }

    private static void Walk(YamlMappingNode node, List<string> ancestors, List<RanklandLeaf> leaves)
    {
        string? path = GetScalar(node, "path");
        string? name = GetScalar(node, "name");
        string? format = GetScalar(node, "format");

        var segments = new List<string>(ancestors);
        if (path != null) segments.Add(path);

        var children = GetChildren(node);
        if (children != null && children.Children.Count > 0)
        {
            foreach (var child in children.Children)
                if (child is YamlMappingNode childMap)
                    Walk(childMap, segments, leaves);
        }
        else if (path != null && format != null)
        {
            // A leaf: has format, no children.
            leaves.Add(new RanklandLeaf
            {
                Path = path,
                Name = name ?? path,
                Format = format,
                PathSegments = segments,
            });
        }
    }

    private static YamlSequenceNode? GetChildren(YamlMappingNode node)
    {
        if (node.Children.TryGetValue(new YamlScalarNode("children"), out var c) && c is YamlSequenceNode seq)
            return seq;
        return null;
    }

    private static string? GetScalar(YamlMappingNode node, string key)
    {
        if (node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode scalar)
            return scalar.Value;
        return null;
    }
}
