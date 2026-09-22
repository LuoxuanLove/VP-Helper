using System.Collections.Generic;

using System;

namespace VPHelper.Core.Analysis;

/// <summary>
/// A single curated CF Gym ↔ official srk-collection mapping entry.
/// <see cref="GymId"/> is null when the Codeforces Gym ID has not been verified.
/// <see cref="QojContestId"/> is null when no confirmed QOJ contest exists for this entry.
/// Entries with a non-null GymId receive confidence 0.99 (0.75 on problem-count mismatch).
/// </summary>
/// <param name="GymId">Codeforces Gym ID, or null if unconfirmed.</param>
/// <param name="OfficialId">Leaf path from srk-collection config.yaml (= OfficialContest.Id).</param>
/// <param name="ExpectedProblemCount">Problem count for confidence adjustment. 0 = unknown.</param>
/// <param name="DisplayName">Human-readable contest name shown in the Gym directory.</param>
/// <param name="QojContestId">QOJ contest ID (numeric, for qoj.ac/contest/{id}), or null if unconfirmed.</param>
/// <param name="QojExpectedProblemCount">Confirmed QOJ problem count when it differs from the Gym replay. Null uses <see cref="ExpectedProblemCount"/>.</param>
public record CuratedEntry(
    long?  GymId,
    string OfficialId,
    int    ExpectedProblemCount,
    string DisplayName,
    int?   QojContestId = null,
    int?   QojExpectedProblemCount = null);

/// <summary>
/// Hand-curated table mapping Codeforces Gym IDs to algoux/srk-collection official contest IDs,
/// and optionally to QOJ contest IDs for supplementary problem links.
/// OfficialId values are verified against official/config.yaml tree leaf paths.
/// GymId / QojContestId are set only for entries personally confirmed; leave null when unsure.
/// Rule: if you cannot 100% confirm an ID, leave it null rather than guessing.
/// </summary>
public static class CuratedContestMap
{
    private static readonly CuratedEntry[] Entries =
    [
        // ====== 2026 (Gym ids verified from codeforces.com/gyms search) =======
        new(106567L, "jlcpc19th",                     13, "2026 吉林省赛", 3786),
        new(106570L, "sccpc18th",                     12, "2026 四川省赛", 3789),
        new(106565L, "ccpc2026invitational-fuzhou",   13, "CCPC 2026 福州邀请赛"),
        new(106554L, "ccpc2026invitational-nanchang", 13, "CCPC 2026 南昌邀请赛"),
        new(106534L, "hlcpc21st",                     13, "CCPC 2026 黑龙江省赛", 3768),
        new(null, "sdcpc16th",                         13, "2026 山东省大学生程序设计竞赛", 3767),
        new(null, "icpc2026invitational-wuhan",        13, "2026 ICPC武汉邀请赛 & 湖北省赛", 3799),
        // QOJ 3588 is a Universal Cup mirror, so it stays directory-only.
        new(null, "icpc2026invitational-shenzhen",     13, "2026 ICPC深圳邀请赛"),
        new(null, "icpc2026invitational-xi_an",        14, "2026 ICPC陕西邀请赛", 3729, 14),
        new(null, "fjcpc13th",                          13, "2026 福建省赛", 3784),
        // QOJ 3756 is published canonically as Jiangsu and aliases Guangdong; directory-only.
        new(null, "gdcpc23rd",                          12, "2026 广东省赛"),
        new(null, "hecpc10th",                          13, "2026 河北省赛"),
        // QOJ 3749 is a Universal Cup mirror of Zhejiang; directory-only.
        new(null, "zjcpc23rd",                          13, "2026 浙江省赛"),

        // ====== 2025 (QOJ ids verified from qoj.ac/category/576) =======
        // QOJ calls these "Asia East Continent Online Contest (I/II)"; the Rankland
        // leaves name the same two official events as 网络赛第一场/第二场.
        new(null, "icpc2025preliminary-1", 13, "ICPC 2025 区域网络赛第一场", 2513),
        new(null, "icpc2025preliminary-2", 12, "ICPC 2025 区域网络赛第二场", 2524),
        new(null, "icpc2025xi_an",        13, "ICPC 2025 西安站", 2562),
        new(106161L, "icpc2025chengdu",      13, "ICPC 2025 成都站", 2567),
        new(null, "icpc2025wuhan",        13, "ICPC 2025 武汉站", 2609),
        new(null, "icpc2025nanjing",      13, "ICPC 2025 南京站", 2581),
        new(106252L, "icpc2025shenyang",     13, "ICPC 2025 沈阳站", 2641),
        new(null, "icpc2025shanghai",     13, "ICPC 2025 上海站", 2908),
        new(null, "icpc2025hongkong",     12, "ICPC 2025 香港站", 3169),
        new(null, "icpc2025ecfinal",      12, "ICPC 2025 EC Final", 3295),
        new(null, "ccpc2025harbin",       13, "CCPC 2025 哈尔滨站", 2575),
        new(null, "ccpc2025jinan",        13, "CCPC 2025 济南站", 2693),
        new(null, "ccpc2025zhengzhou",    13, "CCPC 2025 郑州站", 2661),
        new(null, "ccpc2025chongqing",    13, "CCPC 2025 重庆站", 3387),
        new(null, "ccpc2025final",        13, "CCPC 2025 Final", 3727),
        new(null, "ccpc2025preliminary",  13, "CCPC 2025 网络预选赛", 2534),
        new(null, "ccpc2025ladies",       12, "CCPC 2025 女生赛", 2564),
        new(null, "ccpc2025hv",           12, "CCPC 2025 高职专场", 2565),
        // QOJ 2025 is a Universal Cup mirror, so it must not auto-bind VP results.
        new(105901L, "icpc2025invitational-wuhan", 13, "ICPC 2025 武汉邀请赛"),
        new(105941L, "ccpc2025invitational-zhengzhou", 13, "CCPC 2025 郑州邀请赛暨河南省赛"),
        new(106380L, "lncpc6th",                      8, "2025 辽宁省赛", 2642, 13),
        new(105977L, "ccpc2025invitational-fujian",   8, "CCPC 2025 福建邀请赛"),
        new(105851L, "bjcpc2025",                     5, "2025 北京市赛", 1986, 11),
        new(105949L, "sccpc17th",                     8, "2025 四川省赛", 2152, 12),
        new(105909L, "hecpc9th",                     13, "第九届河北省大学生程序设计竞赛", 2629),
        new(105945L, "gdcpc22nd",                    13, "2025 江苏省赛暨广东省赛"),
        new(105930L, "sdcpc15th",                    13, "第十五届山东省大学生程序设计竞赛", 2040),
        new(105922L, "jlcpc18th",                    13, "第十八届吉林省大学生程序设计竞赛", 2033, 12),
        new(105891L, "sncpc13th",                    13, "第十三届陕西省大学生程序设计竞赛"),
        // Source Gym ids verified through the user's local clone chains. Problem counts are
        // deliberately left unknown because the local training records are incomplete.
        new(105911L, "icpc2025invitational-nanchang",  0, "ICPC 2025 南昌邀请赛暨江西省赛"),
        new(105992L, "shcpc2025",                      0, "2025 上海市大学生程序设计竞赛", 2238, 13),
        new(null, "fjcpc12th",                          13, "2025 福建省赛", 2182),
        // QOJ 2058 aliases Guangdong/Jiangsu; QOJ 2521 aliases Jiangxi/Nanchang;
        // QOJ 2021 is a Universal Cup mirror. Keep them directory-only.
        new(null, "zjcpc22nd",                          13, "2025 浙江省赛"),

        // ====== 2024 (QOJ ids verified from qoj.ac/category/458) =======
        // GymId 105484 is user-confirmed (ICPC 2024 Asia Nanjing Regional)
        new(null,    "icpc2024preliminary-1", 13, "ICPC 2024 区域网络赛第一场", 1794),
        new(105484L, "icpc2024nanjing",      13, "ICPC 2024 南京站", 1828),
        new(105486L, "icpc2024chengdu",      13, "ICPC 2024 成都站", 1821),
        new(105657L, "icpc2024hangzhou",     13, "ICPC 2024 杭州站", 1893),
        new(null,    "icpc2024shanghai",     13, "ICPC 2024 上海站", 1913),
        new(105578L, "icpc2024shenyang",     13, "ICPC 2024 沈阳站", 1865),
        new(105588L, "icpc2024kunming",      13, "ICPC 2024 昆明站", 1871),
        new(null,    "icpc2024hongkong",     13, "ICPC 2024 香港站", 1885),
        new(null,    "icpc2024ecfinal",      12, "ICPC 2024 EC Final", 1894),
        new(105459L, "ccpc2024harbin",       13, "CCPC 2024 哈尔滨站", 1817),
        new(105540L, "ccpc2024jinan",        13, "CCPC 2024 济南站", 1843),
        new(106030L, "ccpc2024chongqing",    13, "CCPC 2024 重庆站", 1840),
        new(105632L, "ccpc2024zhengzhou",    13, "CCPC 2024 郑州站", 1873),
        new(null,    "ccpc2024final",        13, "CCPC 2024 Final", 2036),
        new(105143L, "icpc2024invitational-wuhan",   13, "ICPC 2024 中国武汉邀请赛"),
        new(105386L, "icpc2024invitational-kunming", 13, "ICPC 2024 中国昆明邀请赛", 1802),
        // Gym 105385 is the source of the user's Gym 632808 clone chain.
        new(105385L, "sdcpc14th",                      13, "2024 CCPC 山东邀请赛暨山东省赛", 1801),
        new(105358L, "icpc2024preliminary-2",          12, "ICPC 2024 区域网络赛第二场", 1799),
        new(105336L, "ccpc2024preliminary",            12, "CCPC 2024 网络预选赛", 2530),
        new(null,    "ccpc2024ladies",                 13, "CCPC 2024 女生赛", 1841),
        new(null,    "ccpc2024hv",                     12, "CCPC 2024 高职专场", 1842),
        new(105170L, "jlcpc17th",                      12, "2024 CCPC 长春邀请赛暨吉林省赛", 1996),
        new(null,    "icpc2024invitational-shaanxi", 13, "ICPC 2024 中国陕西邀请赛"),
        new(105231L, "jxcpc2024",                     11, "2024 江西省赛"),
        new(105222L, "sccpc16th",                      8, "2024 四川省赛", 1862, 12),
        new(null, "gdcpc21st",                         10, "2024 广东省赛", 1692),
        new(null, "lncpc5th",                          13, "2024 辽宁省赛", 1831),
        new(null, "sdcpc13th",                         13, "2023 山东省赛", 1280),
        // QOJ 1780 is a Universal Cup mirror of Zhejiang, so it stays directory-only.
        new(null, "zjcpc21st",                         13, "2024 浙江省赛"),

        // ====== Provincial / invitational backfill (QOJ /category/212) =======
        // Each entry below was checked against the QOJ category and the current Rankland
        // config tree. Entries with ambiguous QOJ aliases remain in CuratedContestLinkMap.
        new(null, "gdcpc20th", 13, "2023 广东省赛", 1247),
        new(null, "hecpc6th",  13, "2022 河北省赛", 2626),
        new(null, "hecpc7th",  13, "2023 河北省赛", 2627),
        new(null, "hecpc8th",  11, "2024 河北省赛", 2628),
        new(null, "jscpc6th",  12, "2021 江苏省赛", 2006),
        new(null, "jscpc7th",  12, "2022 江苏省赛", 2007),
        new(null, "jscpc9th",  11, "2024 江苏省赛", 2009),
        new(null, "sdcpc10th", 13, "2019 山东省赛", 1281),
        new(null, "sncpc7th",  12, "2019 陕西省赛", 1285),
        new(null, "sncpc14th", 13, "2026 陕西省赛", 3753),
        new(null, "hncpc15th", 11, "2019 湖南省赛", 907),
        new(null, "zjcpc17th", 12, "2020 浙江省赛", 2020),
        new(null, "zjcpc18th", 13, "2021 浙江省赛", 2014),
        new(null, "zjcpc19th", 13, "2022 浙江省赛", 2056),
        new(null, "northeastcpc20th", 12, "2026 东北地区赛", 3934),

        // ====== Browser-verified supplementary QOJ entries =======
        new(null, "bjcpc2024",                         11, "2024 北京市赛", 1668),

        // ====== 2023 (QOJ ids verified from qoj.ac/category/459) =======
        new(null, "icpc2023preliminary-1", 12, "ICPC 2023 区域网络赛第一场", 1485),
        new(105471L, "icpc2023xi_an",        14, "ICPC 2023 西安站", 1784),
        new(104821L, "icpc2023nanjing",      13, "ICPC 2023 南京站", 1435),
        new(104869L, "icpc2023shenyang",     13, "ICPC 2023 沈阳站", 1449),
        new(104891L, "icpc2023macau",        11, "ICPC 2023 澳门站", 1459),
        new(null, "icpc2023hefei",           12, "ICPC 2023 合肥站", 1440),
        new(104901L, "icpc2023jinan",        13, "ICPC 2023 济南站", 1472),
        new(104976L, "icpc2023hangzhou",     13, "ICPC 2023 杭州站", 1516),
        new(null, "icpc2023ecfinal",      13, "ICPC 2023 EC Final", 1522),
        new(null, "icpc2023invitational-xi_an", 13, "ICPC 2023 中国西安邀请赛", 1291),
        new(null, "ccpc2023preliminary",    12, "CCPC 2023 网络预选赛", 1358),
        new(104787L, "ccpc2023qinhuangdao",  13, "CCPC 2023 秦皇岛站", 1411),
        new(104768L, "ccpc2023guilin",       13, "CCPC 2023 桂林站", 1404),
        new(104813L, "ccpc2023harbin",       13, "CCPC 2023 哈尔滨站", 1412),
        new(null, "ccpc2023shenzhen",     13, "CCPC 2023 深圳站", 1540),
        new(null, "ccpc2023final",        13, "CCPC 2023 Final", 1596),
        new(103118L, "sdcpc11th",             8, "2021 山东省赛"),

        // ====== 2022 (QOJ ids verified from qoj.ac/category/460) =======
        new(104160L, "icpc2022shenyang", 13, "ICPC 2022 沈阳站", 1096),
        new(104077L, "icpc2022xi_an",    12, "ICPC 2022 西安站", 1051),
        new(null, "icpc2022jinan",    13, "ICPC 2022 济南站", 1053),
        new(104090L, "icpc2022hangzhou", 13, "ICPC 2022 杭州站", 1071),
        new(104128L, "icpc2022nanjing",  13, "ICPC 2022 南京站", 1093),
        new(104172L, "icpc2022hongkong", 12, "ICPC 2022 香港站", 1099),
        new(null, "icpc2022ecfinal",  13, "ICPC 2022 EC Final", 1197),
        new(null, "ccpc2022preliminary", 13, "CCPC 2022 网络预选赛", 2531),
        new(104008L, "ccpc2022guilin",   13, "CCPC 2022 桂林站", 1303),
        new(104023L, "ccpc2022weihai",   13, "CCPC 2022 威海站", 1833),
        new(104053L, "ccpc2022guangzhou",13, "CCPC 2022 广州站", 1493),
        new(104065L, "ccpc2022mianyang", 13, "CCPC 2022 绵阳站", 1480),
        new(null, "ccpc2022final",    13, "CCPC 2022 Final", 1244),

        // ====== 2021 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2021guilin",   12, "CCPC 2021 桂林站", 1305),
        new(null, "ccpc2021guangzhou",12, "CCPC 2021 广州站", 2700),
        new(null, "ccpc2021final",    12, "CCPC 2021 Final", 1869),

        // ====== 2020 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2020weihai",   12, "CCPC 2020 威海站", 1793),
        new(null, "ccpc2020mianyang", 12, "CCPC 2020 绵阳站", 1838),
        new(null, "ccpc2020final",    13, "CCPC 2020 Final", 845),

        // ====== 2019 (QOJ ids verified from qoj.ac/category/205) =======
        // Rankland's official page directly links Gym 102361 as this contest's problem replay.
        new(102361L, "ccpc2019qinhuangdao", 12, "CCPC 2019 秦皇岛站"),
        new(null, "ccpc2019harbin",        12, "CCPC 2019 哈尔滨站", 1797),
        new(null, "ccpc2019xiamen",        12, "CCPC 2019 厦门站", 1733),
        new(null, "ccpc2019final",         12, "CCPC 2019 Final", 2027),

        // ====== 2018 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2018guilin", 12, "CCPC 2018 桂林站", 1867),
        new(null, "ccpc2018final",  12, "CCPC 2018 Final", 2035),

        // ====== 2017 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2017qinhuangdao", 13, "CCPC 2017 秦皇岛站", 1807),
        new(null, "ccpc2017final",       11, "CCPC 2017 Final", 2037),

        // ====== 2016 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2016final", 12, "CCPC 2016 Final", 2038),

        // ====== 2015 (QOJ ids verified from qoj.ac/category/205) =======
        new(null, "ccpc2015nanyang", 12, "CCPC 2015 南阳站", 2039),

        // ====== 2021 (QOJ ids verified from qoj.ac/category/461) =======
        new(null, "icpc2021jinan",    13, "ICPC 2021 济南站", 1997),
        new(null, "icpc2021kunming",  13, "ICPC 2021 昆明站", 1870),
        new(null, "icpc2021macau",    11, "ICPC 2021 澳门站", 911),
        new(null, "icpc2021nanjing",  13, "ICPC 2021 南京站", 1236),
        new(null, "icpc2021shanghai", 13, "ICPC 2021 上海站", 2016),
        new(null, "icpc2021shenyang", 13, "ICPC 2021 沈阳站", 1252),
        new(null, "icpc2021ecfinal",  13, "ICPC 2021 EC Final", 1041),

        // ====== 2020 (QOJ ids verified from qoj.ac/category/462) =======
        new(null, "icpc2020jinan",    13, "ICPC 2020 济南站", 2181),
        new(null, "icpc2020nanjing",  13, "ICPC 2020 南京站", 1235),
        new(null, "icpc2020shanghai", 13, "ICPC 2020 上海站", 1477),
        new(null, "icpc2020shenyang", 13, "ICPC 2020 沈阳站", 1251),
        new(null, "icpc2020yinchuan", 13, "ICPC 2020 银川站", 1322),
        new(null, "icpc2020ecfinal",  13, "ICPC 2020 EC Final", 1040),

        // ====== 2019 (QOJ ids verified from qoj.ac/category/463) =======
        new(null, "icpc2019nanjing",  11, "ICPC 2019 南京站", 1321),
        new(null, "icpc2019shanghai", 13, "ICPC 2019 上海站", 1866),
        new(null, "icpc2019xuzhou",   13, "ICPC 2019 徐州站", 1834),
        new(null, "icpc2019yinchuan", 14, "ICPC 2019 银川站", 1320),
        new(null, "icpc2019ecfinal",  13, "ICPC 2019 EC Final", 1039),

        // ====== 2018 (QOJ ids verified from qoj.ac/category/464) =======
        new(null, "icpc2018jiaozuo",  12, "ICPC 2018 焦作站", 1319),
        new(null, "icpc2018nanjing",  13, "ICPC 2018 南京站", 1832),
        new(null, "icpc2018qingdao",  13, "ICPC 2018 青岛站", 1187),
        new(null, "icpc2018shenyang", 13, "ICPC 2018 沈阳站", 1318),
        new(null, "icpc2018xuzhou",   13, "ICPC 2018 徐州站", 1317),
        new(null, "icpc2018ecfinal",  12, "ICPC 2018 EC Final", 1938),

        // ====== 2017 (QOJ ids verified from qoj.ac/category/465) =======
        new(null, "icpc2017beijing",  10, "ICPC 2017 北京站", 1809),
        new(null, "icpc2017nanning",  13, "ICPC 2017 南宁站", 2114),
        new(null, "icpc2017qingdao",  11, "ICPC 2017 青岛站", 2108),
        new(null, "icpc2017shenyang", 13, "ICPC 2017 沈阳站", 2109),
        new(null, "icpc2017xi_an",    11, "ICPC 2017 西安站", 2112),
        new(null, "icpc2017urumchi",  11, "ICPC 2017 乌鲁木齐站", 2113),
        new(null, "icpc2017ecfinal",  13, "ICPC 2017 EC Final", 2026),

        // ====== 2016 (QOJ ids verified from qoj.ac/category/466) =======
        new(null, "icpc2016qingdao",  13, "ICPC 2016 青岛站", 2549),
        new(null, "icpc2016shenyang", 13, "ICPC 2016 沈阳站", 2540),
        new(null, "icpc2016cnfinal",  12, "ICPC 2016 China Final", 2028),

        // ====== 2015 (QOJ ids verified from qoj.ac/category/467) =======
        new(null, "icpc2015changchun", 13, "ICPC 2015 长春站", 2116),
        new(null, "icpc2015shenyang",  13, "ICPC 2015 沈阳站", 2115),
        new(null, "icpc2015ecfinal",   13, "ICPC 2015 EC Final", 2029),
    ];

    // QOJ may publish an upsolving mirror in addition to the canonical contest page. Keep such
    // aliases outside Entries so the official directory remains one row per OfficialId.
    private static readonly CuratedEntry[] QojAliases =
    [
        new(null, "icpc2025shanghai", 13, "ICPC 2025 上海站（补题）", 2645),
    ];

    // Local training contests may be clones whose parent chain has not yet been refreshed in an
    // older database. These verified source IDs are only a fallback: a live clone chain always
    // takes priority when it is available.
    private static readonly (long SourceGymId, long CanonicalGymId)[] GymAliases =
    [
        (632808L, 105385L),
        (634205L, 105358L),
        (635617L, 105336L),
        (637222L, 106030L),
        (642844L, 105657L),
        (643698L, 105588L),
        (644777L, 105484L),
        (647317L, 105632L),
        (648287L, 105578L),
        (668092L, 105170L),
        (677629L, 105911L),
        (679438L, 105909L),
        (680980L, 105901L),
        (682064L, 105992L),
    ];

    /// <summary>Lookup table keyed by confirmed Codeforces Gym ID (null GymId entries excluded).</summary>
    private static readonly Dictionary<long, CuratedEntry> _byGymId;
    private static readonly Dictionary<long, CuratedEntry> _bySourceGymAlias;
    private static readonly IReadOnlyDictionary<long, CuratedEntry> _allGymMappings;
    private static readonly Dictionary<long, CuratedEntry> _byQojContestId;

    static CuratedContestMap()
    {
        _byGymId = new Dictionary<long, CuratedEntry>();
        _bySourceGymAlias = new Dictionary<long, CuratedEntry>();
        _byQojContestId = new Dictionary<long, CuratedEntry>();
        foreach (var e in Entries)
        {
            if (e.GymId.HasValue)
                _byGymId[e.GymId.Value] = e;
            if (e.QojContestId.HasValue)
                _byQojContestId[e.QojContestId.Value] = e;
        }

        foreach (var (sourceGymId, canonicalGymId) in GymAliases)
        {
            if (_byGymId.ContainsKey(sourceGymId))
                throw new InvalidOperationException($"Duplicate curated Gym ID {sourceGymId}.");
            if (!_byGymId.TryGetValue(canonicalGymId, out var canonical))
                throw new InvalidOperationException($"Missing canonical Gym entry {canonicalGymId} for source Gym {sourceGymId}.");
            _bySourceGymAlias[sourceGymId] = canonical;
        }

        var allGymMappings = new Dictionary<long, CuratedEntry>(_byGymId);
        foreach (var (sourceGymId, entry) in _bySourceGymAlias)
            allGymMappings[sourceGymId] = entry;
        _allGymMappings = allGymMappings;

        foreach (var e in QojAliases)
        {
            if (!e.QojContestId.HasValue)
                throw new InvalidOperationException("A QOJ alias must have a contest ID.");
            if (_byQojContestId.ContainsKey(e.QojContestId.Value))
                throw new InvalidOperationException($"Duplicate curated QOJ contest ID {e.QojContestId.Value}.");
            _byQojContestId[e.QojContestId.Value] = e;
        }
    }

    /// <summary>
    /// Try to find a curated entry for a Codeforces Gym ID.
    /// Returns null if no confirmed mapping exists for that ID.
    /// </summary>
    public static CuratedEntry? TryGet(long gymId)
        => _byGymId.TryGetValue(gymId, out var e) ? e : null;

    /// <summary>Try a verified source-Gym fallback when no clone-chain entry matched.</summary>
    internal static CuratedEntry? TryGetSourceGymAlias(long gymId)
        => _bySourceGymAlias.TryGetValue(gymId, out var e) ? e : null;

    /// <summary>All confirmed Gym IDs, including verified source aliases for startup reconciliation.</summary>
    internal static IReadOnlyDictionary<long, CuratedEntry> GymMappings => _allGymMappings;

    /// <summary>Try to find a curated entry for an exact QOJ contest ID, including verified mirrors.</summary>
    public static CuratedEntry? TryGetQoj(long contestId)
        => _byQojContestId.TryGetValue(contestId, out var e) ? e : null;

    /// <summary>Canonical curated entries, one row per official contest, in insertion order.</summary>
    public static IReadOnlyList<CuratedEntry> All => Entries;
}
