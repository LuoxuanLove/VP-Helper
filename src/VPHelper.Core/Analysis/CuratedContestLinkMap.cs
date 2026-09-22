using System.Collections.Generic;

namespace VPHelper.Core.Analysis;

/// <summary>
/// Verified contest links that are useful in the directory but cannot be mapped to an srk official
/// standing yet. These entries must not create ContestMappings.
/// </summary>
/// <param name="GymId">Codeforces Gym ID, or null if not verified.</param>
/// <param name="DisplayName">Normalized contest name.</param>
/// <param name="DateText">Contest date, yyyy-MM-dd when known.</param>
/// <param name="ExpectedProblemCount">Problem count when known. 0 = unknown.</param>
/// <param name="QojContestId">QOJ contest ID, or null if not verified.</param>
/// <param name="OfficialId">Optional Rankland leaf to which this page should be shown as a link. It never creates an automatic mapping.</param>
public record CuratedContestLink(
    long?  GymId,
    string DisplayName,
    string DateText,
    int    ExpectedProblemCount,
    int?   QojContestId = null,
    string? OfficialId = null);

/// <summary>
/// Link-only contest catalog. Use this for publicly verified Gym/QOJ contest pages whose official
/// rankland standing is missing from algoux/srk-collection.
/// </summary>
public static class CuratedContestLinkMap
{
    private static readonly CuratedContestLink[] Entries =
    [
        // Codeforces Gym page title: The 21st Hunan Provincial Collegiate Programming Contest.
        // srk-collection currently has no 2026 Hunan official standing, so this is link-only.
        new(106139L, "2025 湖南省赛", "2025-10-12", 11),

        // The 2023 Beijing collegiate contest is a city competition. It has no corresponding
        // srk leaf yet, but remains useful as a QOJ-only directory entry under provincial/city
        // competitions. Non-East-Continent regionals and university-only contests are excluded.
        new(null, "2023 北京市大学生程序设计竞赛", string.Empty, 0, 1464),

        // Every remaining QOJ category/21 contest was verified in the side browser. These five
        // have no one-to-one Rankland official leaf, so retain their QOJ pages without creating
        // an automatic ContestMapping.
        new(null, "ICPC 2016 平壤站", string.Empty, 12, 2180),
        new(null, "ICPC 2017 香港站", string.Empty, 11, 2111),
        new(null, "ICPC 2018 北京站", string.Empty, 10, 2004),
        new(null, "ICPC 2019 香港站", string.Empty, 11, 1537),
        new(null, "ICPC 2020 澳门站", string.Empty, 12, 1538),
        // QOJ /category/212: shared problem sets and Universal Cup mirrors are
        // directory links only. QOJ VP data cannot distinguish the aliases, so these
        // entries must never auto-bind an official standing.
        new(null, "2026 广东省赛（QOJ 题单）", string.Empty, 12, 3756, "gdcpc23rd"),
        new(null, "2026 江苏省赛（QOJ 题单）", string.Empty, 12, 3756, "jscpc11th"),
        new(null, "2025 广东省赛（QOJ 题单）", string.Empty, 12, 2058, "gdcpc22nd"),
        new(null, "2025 江苏省赛（QOJ 题单）", string.Empty, 12, 2058, "jscpc10th"),
        new(null, "2023 江苏省赛（QOJ 题单）", string.Empty, 12, 2008, "jscpc8th"),
        new(null, "2023 湖南省赛（QOJ 题单）", string.Empty, 12, 2008, "hncpc19th"),
        new(null, "2025 江西省赛（QOJ 题单）", string.Empty, 13, 2521, "jxcpc2025"),
        new(null, "ICPC 2025 南昌邀请赛（QOJ 题单）", string.Empty, 13, 2521, "icpc2025invitational-nanchang"),
        new(null, "2024 浙江省赛（Universal Cup 题单）", string.Empty, 13, 1780, "zjcpc21st"),
        new(null, "2025 浙江省赛（Universal Cup 题单）", string.Empty, 13, 2021, "zjcpc22nd"),
        new(null, "2026 浙江省赛（Universal Cup 题单）", string.Empty, 13, 3749, "zjcpc23rd"),
        new(null, "ICPC 2025 武汉邀请赛（Universal Cup 题单）", string.Empty, 13, 2025, "icpc2025invitational-wuhan"),
        new(null, "ICPC 2026 深圳邀请赛（Universal Cup 题单）", string.Empty, 13, 3588, "icpc2026invitational-shenzhen"),
        new(null, "ICPC 2026 陕西邀请赛（Universal Cup 题单）", string.Empty, 14, 3766, "icpc2026invitational-xi_an"),

        // Category 212 pages without an appropriate Rankland leaf remain link-only.
        new(null, "2020 河北省赛", string.Empty, 0, 2624),
        new(null, "2021 河北省赛", string.Empty, 12, 2625),
        new(null, "2022 香港赛", string.Empty, 7, 3918),
        new(null, "2023 香港赛", string.Empty, 7, 3871),
        new(null, "2026 香港赛", string.Empty, 9, 3869),
        new(null, "2013 湖南省赛", string.Empty, 11, 903),
        new(null, "2014 湖南省赛", string.Empty, 11, 904),
        new(null, "2016 湖南省赛", string.Empty, 11, 905),
        new(null, "2018 湖南省赛", string.Empty, 11, 906),
        new(null, "2020 湖南省赛", string.Empty, 11, 908),
        new(null, "2025 湖南省赛", string.Empty, 11, 2566),
        new(null, "2020 江苏省赛", string.Empty, 12, 2005),
        new(null, "2018 多省联赛", string.Empty, 13, 1316),
        new(null, "2023 陕西省赛", string.Empty, 11, 1290),
        new(null, "2022 上海市赛", string.Empty, 14, 1304),
        new(null, "2015 四川省赛", string.Empty, 10, 900),
        new(null, "2016 四川省赛", string.Empty, 10, 901),
        new(null, "2017 四川省赛", string.Empty, 12, 902),
        new(null, "2019 四川省赛", string.Empty, 11, 1861),
        new(null, "2021 四川省赛", string.Empty, 13, 1292),
        new(null, "2017 浙江省赛", string.Empty, 13, 1301),
        new(null, "2018 浙江省赛", string.Empty, 13, 1889),
        new(null, "2019 浙江省赛", string.Empty, 13, 1874),
    ];

    private static readonly Dictionary<long, CuratedContestLink> _byGymId;

    static CuratedContestLinkMap()
    {
        _byGymId = new Dictionary<long, CuratedContestLink>();
        foreach (var entry in Entries)
            if (entry.GymId.HasValue)
                _byGymId[entry.GymId.Value] = entry;
    }

    public static CuratedContestLink? TryGet(long gymId)
        => _byGymId.TryGetValue(gymId, out var entry) ? entry : null;

    public static IReadOnlyList<CuratedContestLink> All => Entries;
}
