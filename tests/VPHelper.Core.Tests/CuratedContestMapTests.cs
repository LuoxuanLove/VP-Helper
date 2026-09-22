using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;
using Xunit;

namespace VPHelper.Core.Tests;

// ─── CuratedContestMap unit tests ────────────────────────────────────────────

public class CuratedContestMapTests
{
    [Fact]
    public void TryGet_KnownGymId_ReturnsEntry()
    {
        // 105484 = ICPC 2024 Asia Nanjing Regional (user-confirmed)
        var entry = CuratedContestMap.TryGet(105484);
        Assert.NotNull(entry);
        Assert.Equal("icpc2024nanjing", entry!.OfficialId);
        Assert.Equal(105484L, entry.GymId);
        Assert.True(entry.ExpectedProblemCount > 0);
    }

    [Fact]
    public void TryGet_UnknownGymId_ReturnsNull()
    {
        var entry = CuratedContestMap.TryGet(999999999);
        Assert.Null(entry);
    }

    [Fact]
    public void TryGetQoj_KnownContestId_ReturnsEntry()
    {
        var entry = CuratedContestMap.TryGetQoj(3789);
        Assert.NotNull(entry);
        Assert.Equal("sccpc18th", entry!.OfficialId);
        Assert.Equal(3789, entry.QojContestId);
    }

    [Theory]
    [InlineData(1668, "bjcpc2024")]
    [InlineData(2645, "icpc2025shanghai")]
    [InlineData(3729, "icpc2026invitational-xi_an")]
    [InlineData(3767, "sdcpc16th")]
    public void TryGetQoj_BrowserVerifiedContestOrMirror_ReturnsOfficial(int qojContestId, string officialId)
    {
        var entry = CuratedContestMap.TryGetQoj(qojContestId);

        Assert.NotNull(entry);
        Assert.Equal(officialId, entry!.OfficialId);
        Assert.Equal(qojContestId, entry.QojContestId);
    }

    [Theory]
    [InlineData(3784, "fjcpc13th", 13)]
    [InlineData(2626, "hecpc6th", 13)]
    [InlineData(2627, "hecpc7th", 13)]
    [InlineData(2628, "hecpc8th", 11)]
    [InlineData(2629, "hecpc9th", 13)]
    [InlineData(2006, "jscpc6th", 12)]
    [InlineData(2007, "jscpc7th", 12)]
    [InlineData(2009, "jscpc9th", 11)]
    [InlineData(1281, "sdcpc10th", 13)]
    [InlineData(1285, "sncpc7th", 12)]
    [InlineData(3753, "sncpc14th", 13)]
    [InlineData(907, "hncpc15th", 11)]
    [InlineData(2020, "zjcpc17th", 12)]
    [InlineData(2014, "zjcpc18th", 13)]
    [InlineData(2056, "zjcpc19th", 13)]
    [InlineData(3934, "northeastcpc20th", 12)]
    public void TryGetQoj_Category212UnambiguousContest_ReturnsExactOfficialAndProblemCount(
        int qojContestId, string officialId, int expectedProblemCount)
    {
        var entry = CuratedContestMap.TryGetQoj(qojContestId);

        Assert.NotNull(entry);
        Assert.Equal(officialId, entry!.OfficialId);
        Assert.Equal(expectedProblemCount, entry.QojExpectedProblemCount ?? entry.ExpectedProblemCount);
    }

    [Theory]
    [InlineData(2116, "icpc2015changchun", 13)]
    [InlineData(2028, "icpc2016cnfinal", 12)]
    [InlineData(1809, "icpc2017beijing", 10)]
    [InlineData(1938, "icpc2018ecfinal", 12)]
    [InlineData(1320, "icpc2019yinchuan", 14)]
    [InlineData(2181, "icpc2020jinan", 13)]
    [InlineData(911, "icpc2021macau", 11)]
    [InlineData(1051, "icpc2022xi_an", 12)]
    [InlineData(1485, "icpc2023preliminary-1", 12)]
    [InlineData(1784, "icpc2023xi_an", 14)]
    [InlineData(1459, "icpc2023macau", 11)]
    [InlineData(1794, "icpc2024preliminary-1", 13)]
    [InlineData(1799, "icpc2024preliminary-2", 12)]
    [InlineData(1894, "icpc2024ecfinal", 12)]
    [InlineData(2513, "icpc2025preliminary-1", 13)]
    [InlineData(2524, "icpc2025preliminary-2", 12)]
    [InlineData(3169, "icpc2025hongkong", 12)]
    [InlineData(3295, "icpc2025ecfinal", 12)]
    public void TryGetQoj_Category21VerifiedContest_ReturnsExactOfficialAndProblemCount(
        int qojContestId, string officialId, int expectedProblemCount)
    {
        var entry = CuratedContestMap.TryGetQoj(qojContestId);

        Assert.NotNull(entry);
        Assert.Equal(officialId, entry!.OfficialId);
        Assert.Equal(expectedProblemCount, entry.ExpectedProblemCount);
    }

    [Theory]
    [InlineData(105385L, "sdcpc14th")]
    [InlineData(105358L, "icpc2024preliminary-2")]
    [InlineData(105336L, "ccpc2024preliminary")]
    [InlineData(105170L, "jlcpc17th")]
    [InlineData(105911L, "icpc2025invitational-nanchang")]
    [InlineData(105992L, "shcpc2025")]
    [InlineData(105909L, "hecpc9th")]
    [InlineData(105941L, "ccpc2025invitational-zhengzhou")]
    [InlineData(105945L, "gdcpc22nd")]
    [InlineData(105930L, "sdcpc15th")]
    [InlineData(105922L, "jlcpc18th")]
    [InlineData(105891L, "sncpc13th")]
    public void TryGet_BrowserVerifiedCloneRoot_ReturnsOfficial(long gymId, string officialId)
    {
        var entry = CuratedContestMap.TryGet(gymId);

        Assert.NotNull(entry);
        Assert.Equal(officialId, entry!.OfficialId);
    }

    [Fact]
    public void All_HasEntries_NoDuplicateGymIds()
    {
        var entries = CuratedContestMap.All;
        Assert.True(entries.Count > 0);
        // GymId is nullable; most entries are unconfirmed (null). Only check non-null ids.
        var gymIds = entries.Where(e => e.GymId.HasValue).Select(e => e.GymId!.Value).ToList();
        Assert.True(gymIds.Count == gymIds.Distinct().Count(), "Duplicate confirmed GymIds found");
    }

    [Fact]
    public void All_HasEntries_NoDuplicateOfficialIds()
    {
        var entries = CuratedContestMap.All;
        var officialIds = entries.Select(e => e.OfficialId).ToList();
        Assert.Equal(officialIds.Count, officialIds.Distinct(System.StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_HasEntries_NoDuplicateQojContestIds()
    {
        var entries = CuratedContestMap.All;
        var qojIds = entries.Where(e => e.QojContestId.HasValue)
            .Select(e => e.QojContestId!.Value)
            .ToList();
        Assert.Equal(qojIds.Count, qojIds.Distinct().Count());
    }

    [Fact]
    public void All_AllEntriesHaveNonEmptyOfficialId()
    {
        foreach (var e in CuratedContestMap.All)
            Assert.False(string.IsNullOrWhiteSpace(e.OfficialId),
                $"GymId {e.GymId} has blank OfficialId");
    }

    [Theory]
    [InlineData("ccpc2025harbin", 2575)]
    [InlineData("ccpc2025jinan", 2693)]
    [InlineData("ccpc2025zhengzhou", 2661)]
    [InlineData("jlcpc19th", 3786)]
    [InlineData("sccpc18th", 3789)]
    [InlineData("hlcpc21st", 3768)]
    [InlineData("sdcpc16th", 3767)]
    [InlineData("icpc2026invitational-wuhan", 3799)]
    [InlineData("icpc2025xi_an", 2562)]
    [InlineData("icpc2025nanjing", 2581)]
    [InlineData("icpc2025shanghai", 2908)]
    [InlineData("icpc2025shenyang", 2641)]
    [InlineData("bjcpc2024", 1668)]
    [InlineData("icpc2026invitational-xi_an", 3729)]
    [InlineData("icpc2024nanjing", 1828)]
    [InlineData("icpc2024shanghai", 1913)]
    [InlineData("icpc2024ecfinal", 1894)]
    [InlineData("icpc2023nanjing", 1435)]
    [InlineData("icpc2023hefei", 1440)]
    [InlineData("icpc2023ecfinal", 1522)]
    [InlineData("ccpc2023shenzhen", 1540)]
    [InlineData("icpc2022xi_an", 1051)]
    [InlineData("icpc2022nanjing", 1093)]
    [InlineData("ccpc2022mianyang", 1480)]
    public void All_KnownQojContestIds_ArePresent(string officialId, int qojContestId)
    {
        var entry = Assert.Single(CuratedContestMap.All.Where(e => e.OfficialId == officialId));
        Assert.Equal(qojContestId, entry.QojContestId);
    }

    [Fact]
    public void LinkOnlyMap_KnownGymWithoutOfficialStanding_IsPresent()
    {
        var entry = CuratedContestLinkMap.TryGet(106139);

        Assert.NotNull(entry);
        Assert.Equal("2025 湖南省赛", entry!.DisplayName);
        Assert.Equal("2025-10-12", entry.DateText);
        Assert.Equal(11, entry.ExpectedProblemCount);
    }

    [Fact]
    public void LinkOnlyMap_DoesNotPolluteOfficialMappings()
    {
        Assert.Null(CuratedContestMap.TryGet(106139));
    }

    [Fact]
    public void LinkOnlyMap_ContainsVerifiedQojContestWithoutOfficialStanding()
    {
        var linkOnlyQojIds = CuratedContestLinkMap.All
            .Where(entry => entry.QojContestId.HasValue)
            .Select(entry => entry.QojContestId!.Value)
            .ToHashSet();

        Assert.Contains(1464, linkOnlyQojIds);
        Assert.Contains(2180, linkOnlyQojIds);
        Assert.Contains(2111, linkOnlyQojIds);
        Assert.Contains(2004, linkOnlyQojIds);
        Assert.Contains(1537, linkOnlyQojIds);
        Assert.Contains(1538, linkOnlyQojIds);

        Assert.Null(CuratedContestMap.TryGetQoj(1464));
        Assert.Null(CuratedContestMap.TryGetQoj(2180));
    }

    [Theory]
    [InlineData(3756, "gdcpc23rd")]
    [InlineData(3756, "jscpc11th")]
    [InlineData(2058, "gdcpc22nd")]
    [InlineData(2058, "jscpc10th")]
    [InlineData(2008, "jscpc8th")]
    [InlineData(2008, "hncpc19th")]
    [InlineData(2521, "jxcpc2025")]
    [InlineData(2521, "icpc2025invitational-nanchang")]
    [InlineData(1780, "zjcpc21st")]
    [InlineData(2021, "zjcpc22nd")]
    [InlineData(3749, "zjcpc23rd")]
    [InlineData(2025, "icpc2025invitational-wuhan")]
    [InlineData(3588, "icpc2026invitational-shenzhen")]
    [InlineData(3766, "icpc2026invitational-xi_an")]
    public void LinkOnlyMap_Category212AmbiguousOrMirrorContest_IsShownButNotAutoMapped(
        int qojContestId, string officialId)
    {
        Assert.Null(CuratedContestMap.TryGetQoj(qojContestId));
        Assert.Contains(CuratedContestLinkMap.All, entry =>
            entry.QojContestId == qojContestId && entry.OfficialId == officialId);
    }

    [Fact]
    public void LinkOnlyMap_Category212NoRanklandContest_IsPresent()
    {
        var ids = CuratedContestLinkMap.All
            .Where(entry => entry.QojContestId.HasValue)
            .Select(entry => entry.QojContestId!.Value)
            .ToHashSet();

        foreach (var qojId in new[] { 2624, 2625, 3918, 3871, 3869, 903, 904, 905, 906, 908, 2566, 2005, 1316, 1290, 1304, 900, 901, 902, 1861, 1292, 1301, 1889, 1874 })
            Assert.Contains(qojId, ids);
    }

    [Fact]
    public void Category212_EveryVerifiedContest_IsMappedOrLinkOnly()
    {
        // QOJ /category/212 was checked group by group in the in-app browser. These are its
        // 72 distinct contests after duplicate invitation/provincial aliases are collapsed.
        int[] category212ContestIds =
        [
            1464, 1668, 1986, 2182, 3784, 1247, 1692, 2058, 3756,
            2624, 2625, 2626, 2627, 2628, 2629, 3768, 3918, 3871, 3869,
            903, 904, 905, 906, 907, 908, 2008, 2566,
            2005, 2006, 2007, 2009, 2521, 1996, 2033, 3786, 1831, 2642,
            1316, 1285, 1290, 3753, 1281, 1280, 1801, 2040, 3767, 1304,
            2238, 900, 901, 902, 1861, 1292, 1862, 2152, 3789,
            1301, 1889, 1874, 2020, 2014, 2056, 1780, 2021, 3749,
            1802, 3934, 3588, 2025, 3799, 1291, 3766,
        ];

        var linkOnlyIds = CuratedContestLinkMap.All
            .Where(entry => entry.QojContestId.HasValue)
            .Select(entry => entry.QojContestId!.Value)
            .ToHashSet();

        Assert.Equal(category212ContestIds.Length, category212ContestIds.Distinct().Count());
        Assert.Equal(72, category212ContestIds.Length);
        Assert.All(category212ContestIds, contestId => Assert.True(
            CuratedContestMap.TryGetQoj(contestId) is not null || linkOnlyIds.Contains(contestId),
            $"QOJ {contestId} is absent from both the curated map and the link-only catalog."));
    }

    [Fact]
    public void LinkOnlyMap_ExcludesEcFinalWarmups()
    {
        var linkOnlyQojIds = CuratedContestLinkMap.All
            .Where(entry => entry.QojContestId.HasValue)
            .Select(entry => entry.QojContestId!.Value)
            .ToHashSet();

        Assert.DoesNotContain(1481, linkOnlyQojIds);
        Assert.DoesNotContain(1888, linkOnlyQojIds);
        Assert.DoesNotContain(3294, linkOnlyQojIds);
    }

    [Fact]
    public void LinkOnlyMap_ExcludesNonEastRegionalAndUniversityOnlyContests()
    {
        var entries = CuratedContestLinkMap.All;

        Assert.DoesNotContain(entries, entry => entry.GymId == 105910);
        Assert.DoesNotContain(entries, entry => entry.GymId == 106130);
        Assert.DoesNotContain(entries, entry => entry.GymId == 666794);
        Assert.DoesNotContain(entries, entry => entry.GymId == 667372);
        Assert.Contains(entries, entry =>
            entry.DisplayName == "2023 北京市大学生程序设计竞赛" && entry.QojContestId == 1464);
    }

    [Fact]
    public void Category205_AllBrowserVerifiedCcpcContests_AreMappedExceptWarmup()
    {
        // QOJ /category/205 was checked year by year in the side browser. QOJ 3656 is the
        // 2025 CCPC Final warmup and is intentionally excluded from both mappings and directory.
        (int QojId, string OfficialId)[] expected =
        [
            (2039, "ccpc2015nanyang"),
            (2038, "ccpc2016final"),
            (1807, "ccpc2017qinhuangdao"),
            (2037, "ccpc2017final"),
            (1867, "ccpc2018guilin"),
            (2035, "ccpc2018final"),
            (1733, "ccpc2019xiamen"),
            (1797, "ccpc2019harbin"),
            (2027, "ccpc2019final"),
            (1793, "ccpc2020weihai"),
            (1838, "ccpc2020mianyang"),
            (845, "ccpc2020final"),
            (2700, "ccpc2021guangzhou"),
            (1305, "ccpc2021guilin"),
            (1869, "ccpc2021final"),
            (2531, "ccpc2022preliminary"),
            (1833, "ccpc2022weihai"),
            (1493, "ccpc2022guangzhou"),
            (1303, "ccpc2022guilin"),
            (1480, "ccpc2022mianyang"),
            (1244, "ccpc2022final"),
            (1358, "ccpc2023preliminary"),
            (1412, "ccpc2023harbin"),
            (1404, "ccpc2023guilin"),
            (1540, "ccpc2023shenzhen"),
            (1411, "ccpc2023qinhuangdao"),
            (1596, "ccpc2023final"),
            (2530, "ccpc2024preliminary"),
            (1817, "ccpc2024harbin"),
            (1843, "ccpc2024jinan"),
            (1873, "ccpc2024zhengzhou"),
            (1840, "ccpc2024chongqing"),
            (1841, "ccpc2024ladies"),
            (1842, "ccpc2024hv"),
            (2036, "ccpc2024final"),
            (2534, "ccpc2025preliminary"),
            (2575, "ccpc2025harbin"),
            (2693, "ccpc2025jinan"),
            (2661, "ccpc2025zhengzhou"),
            (3387, "ccpc2025chongqing"),
            (2564, "ccpc2025ladies"),
            (2565, "ccpc2025hv"),
            (3727, "ccpc2025final"),
        ];

        Assert.Equal(43, expected.Length);
        foreach (var (qojId, officialId) in expected)
        {
            var entry = CuratedContestMap.TryGetQoj(qojId);
            Assert.NotNull(entry);
            Assert.Equal(officialId, entry!.OfficialId);
        }

        Assert.Null(CuratedContestMap.TryGetQoj(3656));
    }

    [Fact]
    public void Category21_AllBrowserVerifiedContests_AreMappedOrLinkOnly()
    {
        // QOJ /category/21: 2015--2025 annual subcategories, 81 distinct contests in total.
        int[] category21ContestIds =
        [
            2116, 2115, 2029,
            2180, 2549, 2540, 2028,
            1809, 2111, 2114, 2108, 2109, 2112, 2113, 2026,
            2004, 1319, 1832, 1187, 1318, 1317, 1938,
            1537, 1321, 1866, 1834, 1320, 1039,
            2181, 1538, 1235, 1477, 1251, 1322, 1040,
            1997, 1870, 911, 1236, 2016, 1252, 1041,
            1071, 1099, 1053, 1093, 1096, 1051, 1197,
            1485, 1516, 1440, 1472, 1459, 1435, 1449, 1784, 1481, 1522,
            1794, 1799, 1821, 1893, 1885, 1871, 1828, 1913, 1865, 1888, 1894,
            2513, 2524, 2567, 3169, 2581, 2908, 2641, 2609, 2562, 3294, 3295,
        ];
        var linkOnlyIds = CuratedContestLinkMap.All
            .Where(entry => entry.QojContestId.HasValue)
            .Select(entry => entry.QojContestId!.Value)
            .ToHashSet();

        Assert.Equal(81, category21ContestIds.Length);
        int[] excludedWarmupIds = [1481, 1888, 3294];
        var directoryContestIds = category21ContestIds.Except(excludedWarmupIds).ToArray();

        Assert.All(directoryContestIds, contestId => Assert.True(
            CuratedContestMap.TryGetQoj(contestId) is not null || linkOnlyIds.Contains(contestId),
            $"QOJ {contestId} is absent from both the curated map and the link-only catalog."));
        Assert.All(excludedWarmupIds, contestId =>
        {
            Assert.Null(CuratedContestMap.TryGetQoj(contestId));
            Assert.DoesNotContain(contestId, linkOnlyIds);
        });
        Assert.Equal(73, category21ContestIds.Count(contestId => CuratedContestMap.TryGetQoj(contestId) is not null));
        Assert.Equal(5, directoryContestIds.Count(linkOnlyIds.Contains));
    }
}

// ─── MappingService curated-priority integration tests ───────────────────────

public class CuratedMappingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly VpDbContext _db;
    private readonly MappingService _svc;

    public CuratedMappingServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<VpDbContext>()
            .UseSqlite(_conn).Options;
        _db = new VpDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new MappingService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // A gym whose ID appears in the curated table (105484 → icpc2024nanjing).
    private const long CuratedGymId = 105484;
    private const string CuratedOfficialId = "icpc2024nanjing";

    private void SeedCuratedGym(int problemCount = 13)
    {
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = CuratedGymId,
            Title = "ICPC 2024 China Wuhan Invitational",
            StartTime = 1714613400,
            ProblemCount = problemCount,
        });
    }

    private void SeedOfficial(string id, string name, long startAt = 1714613400, int problems = 13)
    {
        _db.OfficialContests.Add(new OfficialContest
        {
            Id = id,
            Name = name,
            StartAt = startAt,
            ProblemCount = problems,
        });
    }

    // ── GenerateCandidatesAsync: curated entry is always first ──────────────

    [Fact]
    public async Task GenerateCandidates_CuratedGym_CuratedIsFirst()
    {
        SeedCuratedGym(13);
        SeedOfficial(CuratedOfficialId, "2024-05-02 武汉邀请赛", 1714613400, 13);
        SeedOfficial("some-other-official", "2024 CCPC Guangzhou", 1730000000, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(CuratedGymId, topN: 5);

        Assert.True(candidates.Count > 0);
        Assert.Equal(CuratedOfficialId, candidates[0].OfficialId);
        Assert.Equal(0.99, candidates[0].Confidence, 2);
        Assert.Contains("curated=true", candidates[0].Reason);
    }

    [Fact]
    public async Task GenerateCandidates_CuratedGym_ProblemMismatch_Confidence075()
    {
        SeedCuratedGym(problemCount: 10); // mismatched; expected 13
        SeedOfficial(CuratedOfficialId, "武汉邀请赛", 1714613400, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(CuratedGymId, topN: 5);

        Assert.True(candidates.Count > 0);
        Assert.Equal(CuratedOfficialId, candidates[0].OfficialId);
        Assert.Equal(0.75, candidates[0].Confidence, 2);
        Assert.Contains("prob_mismatch", candidates[0].Reason);
    }

    [Fact]
    public async Task GenerateCandidates_CuratedGym_NoCuratedOfficialInDb_CuratedStillFirst()
    {
        // Official not yet synced to DB, but curated table entry exists.
        SeedCuratedGym(13);
        SeedOfficial("icpc2024nanjing", "ICPC 2024 Nanjing", 1730000000, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(CuratedGymId, topN: 5);

        // Curated entry is prepended even though its official isn't in DB yet.
        Assert.Equal(CuratedOfficialId, candidates[0].OfficialId);
        Assert.Equal(0.99, candidates[0].Confidence, 2);
    }

    [Fact]
    public async Task GenerateCandidates_NonCuratedGym_DoesNotCreateFuzzyCandidates()
    {
        long nonCuratedId = 999888; // not in curated table
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = nonCuratedId,
            Title = "2024 ICPC Nanjing Regional",
            StartTime = 1730000000,
            ProblemCount = 13,
        });
        SeedOfficial("icpc2024nanjing", "ICPC 2024 Nanjing Regional", 1730000000, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(nonCuratedId, topN: 5);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GenerateCandidates_CloneGym_UsesCuratedRoot()
    {
        const long cloneGymId = 632808;
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = cloneGymId,
            Title = "2025 秋季组队训练赛-1",
            StartTime = 1_756_000_000,
            ProblemCount = 13,
            CloneParentGymId = 105486,
            CloneParentTitle = "ICPC 2024 Chengdu",
            CloneRootGymId = 105486,
            CloneCheckedAt = 1_756_000_000,
        });
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = 105486,
            Title = "ICPC 2024 Chengdu",
            StartTime = 1_731_000_000,
            ProblemCount = 13,
        });
        SeedOfficial("icpc2024chengdu", "ICPC 2024 成都站", 1_731_000_000, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(cloneGymId, topN: 5);

        Assert.NotEmpty(candidates);
        Assert.Equal("icpc2024chengdu", candidates[0].OfficialId);
        Assert.Contains("clone_chain_match=105486", candidates[0].Reason);
    }

    // ── AutoMatchAllAsync: curated entry wins over fuzzy ────────────────────

    [Fact]
    public async Task AutoMatchAll_CuratedGym_AutoConfirmedWithCuratedId()
    {
        SeedCuratedGym(13);
        SeedOfficial(CuratedOfficialId, "武汉邀请赛", 1714613400, 13);
        // Add a different official that fuzzy might prefer based on pure title similarity.
        SeedOfficial("ccpc2024guangzhou", "CCPC 2024 Guangzhou Site", 1735000000, 13);
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.AutoConfirmed);

        var mapping = await _db.ContestMappings
            .FirstOrDefaultAsync(m => m.Platform == Platform.Codeforces && m.GymId == CuratedGymId);
        Assert.NotNull(mapping);
        Assert.Equal(CuratedOfficialId, mapping!.OfficialId);
        Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
        Assert.Equal(0.99, mapping.Confidence, 2);
    }

    [Fact]
    public async Task AutoMatchAll_CuratedGym_ProblemMismatch_StillAutoConfirmed()
    {
        // 0.75 > default threshold of 0.72 → still AutoConfirmed.
        SeedCuratedGym(problemCount: 10);
        SeedOfficial(CuratedOfficialId, "武汉邀请赛", 1714613400, 13);
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync(autoConfirmThreshold: 0.72);

        Assert.Equal(1, summary.AutoConfirmed);
        var mapping = await _db.ContestMappings
            .FirstOrDefaultAsync(m => m.Platform == Platform.Codeforces && m.GymId == CuratedGymId);
        Assert.Equal(0.75, mapping!.Confidence, 2);
        Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
    }

    [Fact]
    public async Task AutoMatchAll_CuratedGym_ManualConfirmedIsNotOverwritten()
    {
        SeedCuratedGym(13);
        SeedOfficial(CuratedOfficialId, "武汉邀请赛", 1714613400, 13);
        SeedOfficial("manually-picked", "手动选择的比赛", 1714613400, 13);

        // Pre-seed a manual confirmation for a different official.
        _db.ContestMappings.Add(new ContestMapping
        {
            Platform = Platform.Codeforces,
            GymId = CuratedGymId,
            OfficialId = "manually-picked",
            Confidence = 1.0,
            Confirmed = MappingStatus.ManualConfirmed,
        });
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.SkippedManual);
        // Manual mapping must survive untouched.
        var mapping = await _db.ContestMappings
            .FirstOrDefaultAsync(m => m.Platform == Platform.Codeforces && m.GymId == CuratedGymId
                                   && m.Confirmed == MappingStatus.ManualConfirmed);
        Assert.NotNull(mapping);
        Assert.Equal("manually-picked", mapping!.OfficialId);
    }

    [Fact]
    public async Task AutoMatchAll_CuratedGym_NoOfficialsInDb_StillAutoConfirmed()
    {
        // Curated match works even without officials loaded — bypasses fuzzy path.
        SeedCuratedGym(13);
        // No OfficialContests seeded.
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.AutoConfirmed);
        var mapping = await _db.ContestMappings
            .FirstOrDefaultAsync(m => m.Platform == Platform.Codeforces && m.GymId == CuratedGymId);
        Assert.NotNull(mapping);
        Assert.Equal(CuratedOfficialId, mapping!.OfficialId);
    }

    [Fact]
    public async Task AutoMatchAll_CloneGym_PersistsMappingOnSourceGym()
    {
        const long cloneGymId = 632808;
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = cloneGymId,
            Title = "2025 秋季组队训练赛-1",
            ProblemCount = 13,
            CloneParentGymId = 105486,
            CloneRootGymId = 105486,
        });
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = 105486,
            Title = "ICPC 2024 Chengdu",
        });
        SeedOfficial("icpc2024chengdu", "ICPC 2024 成都站", 1_731_000_000, 13);
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.AutoConfirmed);
        var mapping = await _db.ContestMappings
            .SingleOrDefaultAsync(m => m.Platform == Platform.Codeforces && m.GymId == cloneGymId);
        Assert.NotNull(mapping);
        Assert.Equal("icpc2024chengdu", mapping!.OfficialId);
        Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
    }

    [Fact]
    public async Task AutoMatchAll_VerifiedSourceGymAlias_AutoConfirmsWithoutCloneMetadata()
    {
        const long sourceGymId = 632808;
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = sourceGymId,
            Title = "秋季组队训练赛-1",
            ProblemCount = 9,
        });
        SeedOfficial("sdcpc14th", "2024 山东省赛", 1_716_000_000, 13);
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.AutoConfirmed);
        var mapping = await _db.ContestMappings.SingleAsync(m =>
            m.Platform == Platform.Codeforces && m.GymId == sourceGymId);
        Assert.Equal("sdcpc14th", mapping.OfficialId);
        Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
    }

    [Fact]
    public async Task AutoMatchAll_HebeiTrainingClone_AutoConfirmsWithoutCloneMetadata()
    {
        const long sourceGymId = 679438;
        _db.GymContests.Add(new GymContest
        {
            Platform = Platform.Codeforces,
            GymId = sourceGymId,
            Title = "25级个人训练赛-2",
            ProblemCount = 13,
        });
        SeedOfficial("hecpc9th", "第九届河北省大学生程序设计竞赛", 1_747_532_800, 13);
        await _db.SaveChangesAsync();

        var summary = await _svc.AutoMatchAllAsync();

        Assert.Equal(1, summary.AutoConfirmed);
        var mapping = await _db.ContestMappings.SingleAsync(m =>
            m.Platform == Platform.Codeforces && m.GymId == sourceGymId);
        Assert.Equal("hecpc9th", mapping.OfficialId);
        Assert.Equal(MappingStatus.AutoConfirmed, mapping.Confirmed);
    }

    [Fact]
    public async Task GenerateCandidates_VerifiedSourceGymAlias_DoesNotOverrideLiveCloneChain()
    {
        const long sourceGymId = 632808;
        _db.GymContests.AddRange(
            new GymContest
            {
                Platform = Platform.Codeforces,
                GymId = sourceGymId,
                Title = "秋季组队训练赛-1",
                ProblemCount = 13,
                CloneParentGymId = 105486,
                CloneRootGymId = 105486,
            },
            new GymContest
            {
                Platform = Platform.Codeforces,
                GymId = 105486,
                Title = "ICPC 2024 Chengdu",
                ProblemCount = 13,
            });
        SeedOfficial("icpc2024chengdu", "ICPC 2024 成都站", 1_731_000_000, 13);
        await _db.SaveChangesAsync();

        var candidates = await _svc.GenerateCandidatesAsync(sourceGymId);

        Assert.Equal("icpc2024chengdu", candidates[0].OfficialId);
        Assert.Contains("clone_chain_match=105486", candidates[0].Reason);
        Assert.DoesNotContain("source_alias=true", candidates[0].Reason);
    }
}
