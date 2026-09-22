using System;
using System.Collections.Generic;
using System.Linq;

namespace VPHelper.Core.Analysis;

/// <summary>
/// Builds stable Chinese display names for official srk-collection contests whose leaf names can be terse.
/// </summary>
public static class ContestDisplayNameBuilder
{
    private static readonly IReadOnlyDictionary<string, string> PlaceBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["beijing"] = "北京",
        ["changchun"] = "长春",
        ["changsha"] = "长沙",
        ["chengdu"] = "成都",
        ["chongqing"] = "重庆",
        ["dalian"] = "大连",
        ["fujian"] = "福建",
        ["fuzhou"] = "福州",
        ["guangdong"] = "广东",
        ["guangzhou"] = "广州",
        ["guilin"] = "桂林",
        ["guizhou"] = "贵州",
        ["hangzhou"] = "杭州",
        ["harbin"] = "哈尔滨",
        ["hefei"] = "合肥",
        ["hongkong"] = "香港",
        ["hong_kong"] = "香港",
        ["hunan"] = "湖南",
        ["jinan"] = "济南",
        ["jinhua"] = "金华",
        ["jilin"] = "吉林",
        ["kunming"] = "昆明",
        ["macau"] = "澳门",
        ["mianyang"] = "绵阳",
        ["mudanjiang"] = "牡丹江",
        ["nanchang"] = "南昌",
        ["nanyang"] = "南阳",
        ["nanjing"] = "南京",
        ["nanning"] = "南宁",
        ["northeast"] = "东北",
        ["ningbo"] = "宁波",
        ["qinhuangdao"] = "秦皇岛",
        ["qingdao"] = "青岛",
        ["shaanxi"] = "陕西",
        ["shanghai"] = "上海",
        ["shenyang"] = "沈阳",
        ["shenzhen"] = "深圳",
        ["tianjin"] = "天津",
        ["urumchi"] = "乌鲁木齐",
        ["weihai"] = "威海",
        ["wuhan"] = "武汉",
        ["xiamen"] = "厦门",
        ["xiangtan"] = "湘潭",
        ["xi_an"] = "西安",
        ["xian"] = "西安",
        ["xuzhou"] = "徐州",
        ["yinchuan"] = "银川",
        ["zhengzhou"] = "郑州",
    };

    private static readonly IReadOnlyDictionary<string, string> PreliminaryPlaceBySlug = new Dictionary<string, string>(PlaceBySlug, StringComparer.OrdinalIgnoreCase)
    {
        ["anshan"] = "鞍山",
        ["ccniit"] = "CCNIIT",
        ["cuhk"] = "CUHK",
    };

    private static readonly IReadOnlyDictionary<string, string> CoHostedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hbcpc7th"] = "ICPC武汉邀请赛 & 湖北省赛",
        ["icpc2026invitational-wuhan"] = "ICPC武汉邀请赛 & 湖北省赛",
    };

    private static readonly IReadOnlyDictionary<string, string> ContestFamilyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["northeastcpc"] = "东北地区赛",
        ["haccpc"] = "CCPC河南省赛",
        ["haicpc"] = "ICPC河南省赛",
        ["jxccpc"] = "CCPC江西省赛",
        ["ahcpc"] = "安徽省赛",
        ["bjcpc"] = "北京市赛",
        ["cqcpc"] = "重庆市赛",
        ["fjcpc"] = "福建省赛",
        ["gdcpc"] = "广东省赛",
        ["gxcpc"] = "广西区赛",
        ["gzcpc"] = "贵州省赛",
        ["hbcpc"] = "湖北省赛",
        ["hecpc"] = "河北省赛",
        ["hlcpc"] = "黑龙江省赛",
        ["hncpc"] = "湖南省赛",
        ["jlcpc"] = "吉林省赛",
        ["jscpc"] = "江苏省赛",
        ["jxcpc"] = "江西省赛",
        ["lncpc"] = "辽宁省赛",
        ["nmcpc"] = "内蒙古区赛",
        ["sccpc"] = "四川省赛",
        ["sdcpc"] = "山东省赛",
        ["shcpc"] = "上海市赛",
        ["sncpc"] = "陕西省赛",
        ["xjcpc"] = "新疆区赛",
        ["zjcpc"] = "浙江省赛",
    };

    private static readonly string[] WarmupMarkers =
    [
        "（热身赛）",
        "(热身赛)",
        "热身赛",
        "（Warmup）",
        "(Warmup)",
        "Warmup",
        "Warm-up",
        "warmup",
        "warm-up",
    ];

    private static readonly string[] IgnoredDirectoryMarkers =
    [
        .. WarmupMarkers,
        "初赛",
        "预赛",
        "预选赛",
        "网络赛",
        "preliminary",
    ];

    private static readonly string[] ProvincialInvitationalMarkers =
    [
        "省赛",
        "市赛",
        "区赛",
        "邀请赛",
        "invitational",
        "市大学生程序设计竞赛",
        "hbcpc",
        "hncpc",
        "jlcpc",
        "sccpc",
        "gzcpc",
        "gdcpc",
        "fjcpc",
        "bjcpc",
        "cqcpc",
        "jxcpc",
        "lncpc",
        "nmcpc",
        "northeastcpc",
        "haccpc",
        "haicpc",
        "hecpc",
        "hlcpc",
        "jscpc",
        "sdcpc",
        "sncpc",
        "xjcpc",
        "zjcpc",
    ];

    public static string BuildDisplayName(string officialId, string sourceName, string? curatedName = null)
    {
        var year = ExtractYear(officialId)
            ?? ExtractYear(sourceName)
            ?? (curatedName is null ? null : ExtractYear(curatedName));
        var prefix = year.HasValue ? $"{year.Value} " : string.Empty;
        var id = officialId.ToLowerInvariant();

        var standard = TryBuildCoHostedName(id)
            ?? TryBuildIcpcCcpcName(id)
            ?? TryBuildKnownContestFamilyName(id)
            ?? NormalizeFallbackName(sourceName, curatedName);

        return $"{prefix}{RemoveLeadingYear(standard)}".Trim();
    }

    public static bool ShouldShowInDirectory(string officialId, string sourceName, string categoryPath)
    {
        if (categoryPath.Equals("university-level", StringComparison.OrdinalIgnoreCase) ||
            categoryPath.Equals("ucup", StringComparison.OrdinalIgnoreCase))
            return false;

        var text = $"{officialId} {sourceName}";
        return !ContainsIgnoredDirectoryMarker(text);
    }

    /// <summary>Whether a directory entry belongs to the provincial/city/invitational section.</summary>
    public static bool IsProvincialOrInvitational(string officialId, string displayName)
    {
        var text = $"{officialId} {displayName}";
        return ProvincialInvitationalMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the leaf belongs to an official directory section supported by VP Helper.</summary>
    public static bool IsSupportedDirectoryCategory(string categoryPath)
        => categoryPath.Equals("icpc", StringComparison.OrdinalIgnoreCase) ||
           categoryPath.Equals("ccpc", StringComparison.OrdinalIgnoreCase) ||
           categoryPath.Equals("provincial", StringComparison.OrdinalIgnoreCase);

    public static int? ExtractYear(string text)
    {
        for (var i = 0; i <= text.Length - 4; i++)
        {
            if (!char.IsDigit(text[i]) ||
                !char.IsDigit(text[i + 1]) ||
                !char.IsDigit(text[i + 2]) ||
                !char.IsDigit(text[i + 3]))
                continue;

            var year = (text[i] - '0') * 1000
                + (text[i + 1] - '0') * 100
                + (text[i + 2] - '0') * 10
                + (text[i + 3] - '0');
            if (year is >= 1900 and <= 2099)
                return year;
        }

        return null;
    }

    public static string? ExtractDateText(string text)
    {
        for (var i = 0; i <= text.Length - 10; i++)
        {
            if (char.IsDigit(text[i]) &&
                char.IsDigit(text[i + 1]) &&
                char.IsDigit(text[i + 2]) &&
                char.IsDigit(text[i + 3]) &&
                text[i + 4] == '-' &&
                char.IsDigit(text[i + 5]) &&
                char.IsDigit(text[i + 6]) &&
                text[i + 7] == '-' &&
                char.IsDigit(text[i + 8]) &&
                char.IsDigit(text[i + 9]))
                return text.Substring(i, 10);
        }

        return null;
    }

    private static string? TryBuildIcpcCcpcName(string officialId)
    {
        if (TryGetSeriesSuffix(officialId, "icpc", out var icpcSuffix))
            return TryBuildSeriesName("ICPC", icpcSuffix);

        if (TryGetSeriesSuffix(officialId, "ccpc", out var ccpcSuffix))
            return TryBuildSeriesName("CCPC", ccpcSuffix);

        return null;
    }

    private static string? TryBuildCoHostedName(string officialId)
        => CoHostedNames.TryGetValue(officialId, out var name) ? name : null;

    private static bool TryGetSeriesSuffix(string officialId, string series, out string suffix)
    {
        suffix = string.Empty;
        if (!officialId.StartsWith(series, StringComparison.Ordinal))
            return false;

        var yearStart = series.Length;
        if (officialId.Length < yearStart + 4 || !officialId.Skip(yearStart).Take(4).All(char.IsDigit))
            return false;

        suffix = officialId[(yearStart + 4)..];
        return true;
    }

    private static string? TryBuildSeriesName(string series, string suffix)
    {
        if (suffix.StartsWith("invitational-", StringComparison.Ordinal))
        {
            var slug = suffix["invitational-".Length..];
            const string preliminarySuffix = "-preliminary";
            if (slug.EndsWith(preliminarySuffix, StringComparison.Ordinal))
            {
                var placeSlug = slug[..^preliminarySuffix.Length];
                return PlaceBySlug.TryGetValue(placeSlug, out var preliminaryPlace)
                    ? $"{series}{preliminaryPlace}邀请赛预赛"
                    : $"{series}{placeSlug}邀请赛预赛";
            }

            return PlaceBySlug.TryGetValue(slug, out var place)
                ? $"{series}{place}邀请赛"
                : $"{series}{slug}邀请赛";
        }

        if (suffix.StartsWith("preliminary", StringComparison.Ordinal))
        {
            var detail = suffix["preliminary".Length..].TrimStart('-', '_');
            return TryBuildPreliminaryName(series, detail);
        }

        return suffix switch
        {
            "ecfinal" => $"{series} EC Final",
            "cnfinal" => $"{series} China Final",
            "final" => $"{series} Final",
            "worldfinals" => $"{series} World Finals",
            "women" => $"{series}女生赛",
            "ladies" => $"{series}女生赛",
            "hv" => $"{series}高职专场",
            "vocational" => $"{series}高职专场",
            "srni" => $"{series}丝绸之路邀请赛",
            _ when PlaceBySlug.TryGetValue(suffix, out var place) => $"{series}{place}站",
            _ => null,
        };
    }

    private static string TryBuildPreliminaryName(string series, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return $"{series}网络赛";

        if (TryBuildOrdinalPreliminaryName(series, detail, out var ordinalName))
            return ordinalName;

        if (detail.StartsWith("xiaomi-", StringComparison.OrdinalIgnoreCase) &&
            TryBuildOrdinalPreliminaryName(series, detail["xiaomi-".Length..], out var xiaomiName))
        {
            return xiaomiName.Replace("网络赛", "小米网络赛", StringComparison.Ordinal);
        }

        return PreliminaryPlaceBySlug.TryGetValue(detail, out var place)
            ? $"{series}{FormatPlacePrefix(place)}网络赛"
            : $"{series}{detail.ToUpperInvariant()}网络赛";
    }

    private static string FormatPlacePrefix(string place)
        => place.All(c => c < 128) ? $" {place}" : place;

    private static bool TryBuildOrdinalPreliminaryName(string series, string detail, out string name)
    {
        name = string.Empty;
        var ordinal = detail switch
        {
            "1" => "第一场",
            "2" => "第二场",
            "3" => "第三场",
            "4" => "第四场",
            _ => string.Empty,
        };

        if (ordinal.Length == 0)
            return false;

        name = $"{series}网络赛{ordinal}";
        return true;
    }

    private static string? TryBuildKnownContestFamilyName(string officialId)
    {
        foreach (var (prefix, name) in ContestFamilyNames.OrderByDescending(kv => kv.Key.Length))
        {
            if (!officialId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            return officialId.Contains("preliminary", StringComparison.OrdinalIgnoreCase)
                ? $"{name}初赛"
                : name;
        }

        return null;
    }

    private static string NormalizeFallbackName(string sourceName, string? curatedName)
    {
        var text = StripIgnoredDirectoryMarkers(StripLeadingDate(sourceName));
        if (string.IsNullOrWhiteSpace(text) || IsYearOnly(text))
            text = curatedName ?? sourceName;

        text = StripIgnoredDirectoryMarkers(StripLeadingDate(text)).Trim();
        return string.IsNullOrWhiteSpace(text) ? sourceName : text;
    }

    private static string StripLeadingDate(string text)
    {
        text = text.Trim();
        if (text.Length >= 10 && ExtractDateText(text[..10]) == text[..10])
            return text[10..].Trim();

        return text;
    }

    private static string RemoveLeadingYear(string text)
    {
        text = text.Trim();
        if (text.Length >= 5 &&
            char.IsDigit(text[0]) &&
            char.IsDigit(text[1]) &&
            char.IsDigit(text[2]) &&
            char.IsDigit(text[3]) &&
            char.IsWhiteSpace(text[4]))
            return text[5..].Trim();

        return text;
    }

    private static bool IsYearOnly(string text)
    {
        text = text.Trim();
        return text.Length == 4 && text.All(char.IsDigit);
    }

    private static bool ContainsIgnoredDirectoryMarker(string text)
        => IgnoredDirectoryMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string StripIgnoredDirectoryMarkers(string text)
    {
        foreach (var marker in WarmupMarkers)
            text = text.Replace(marker, string.Empty, StringComparison.OrdinalIgnoreCase);

        return text.Trim();
    }
}
