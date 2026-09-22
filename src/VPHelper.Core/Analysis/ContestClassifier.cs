using System;

namespace VPHelper.Core.Analysis;

public enum ContestType
{
    Unknown,
    Regional,     // 区域赛 (regional / 站)
    Invitational, // 邀请赛
    Provincial,   // 省赛
    Preliminary,  // 网络赛 / 预选赛
    Final,        // 总决赛 / World Finals / EC Final
}

/// <summary>
/// Classifies a contest into a coarse type from its srk name + collection path keywords
/// (DESIGN 6.4 / Phase 2 auto-classification).
/// </summary>
public static class ContestClassifier
{
    public static ContestType Classify(string? name, string? path)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        string p = (path ?? string.Empty).ToLowerInvariant();
        string hay = n + " " + p;

        // Order matters: finals and invitationals are more specific than "regional".
        if (Contains(hay, "worldfinals", "world finals", "ecfinal", "ec final", "总决赛", "final"))
            return ContestType.Final;
        if (Contains(hay, "invitational", "邀请赛", "srni", "silk road"))
            return ContestType.Invitational;
        if (Contains(hay, "preliminary", "网络赛", "预选赛", "network"))
            return ContestType.Preliminary;
        if (Contains(hay, "provincial", "省赛", "省级", "省份"))
            return ContestType.Provincial;
        if (Contains(hay, "regional", "区域赛", "站", "site"))
            return ContestType.Regional;
        return ContestType.Unknown;
    }

    /// <summary>Classify using the top-level collection category (icpc/ccpc/provincial/...) as a strong hint.</summary>
    public static ContestType Classify(string? name, string? path, string? topCategory)
    {
        var byText = Classify(name, path);
        if (byText != ContestType.Unknown) return byText;
        if (string.Equals(topCategory, "provincial", StringComparison.OrdinalIgnoreCase))
            return ContestType.Provincial;
        return byText;
    }

    private static bool Contains(string hay, params string[] needles)
    {
        foreach (var s in needles)
            if (hay.Contains(s, StringComparison.Ordinal)) return true;
        return false;
    }
}
