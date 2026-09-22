using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;

namespace VPHelper.WinUI.ViewModels;

/// <param name="GymId">Codeforces Gym ID, null if unconfirmed.</param>
/// <param name="DisplayName">Human-readable name shown in the directory.</param>
/// <param name="OfficialId">srk-collection official ID.</param>
/// <param name="DateText">Contest date shown in the directory.</param>
/// <param name="ProblemCount">Expected problem count.</param>
/// <param name="GymUrl">Codeforces Gym URL, null when GymId is unconfirmed.</param>
/// <param name="QojUrl">QOJ contest URL, null when QOJ contest ID is unconfirmed.</param>
/// <param name="IsSupplementaryLink">True when the entry only contributes an auxiliary directory link.</param>
public record GymDirectoryItem(
    long?   GymId,
    string  DisplayName,
    string  OfficialId,
    string  DateText,
    int     ProblemCount,
    string? GymUrl,
    string? QojUrl,
    bool    IsSupplementaryLink = false)
{
    public IReadOnlyList<string> OfficialIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OfficialParticipation> Runs { get; init; } = Array.Empty<OfficialParticipation>();
    public bool HasVp => Runs.Any(r => r.Kind == ParticipationKind.Virtual);
    public bool HasPractice => Runs.Any(r => r.Kind == ParticipationKind.Practice);
    public bool HasLocalResult => Runs.Count > 0;
    public bool ParticipationAvailable { get; init; } = true;
    public string ParticipationText => !ParticipationAvailable ? "状态未知" : HasVp ? "已 VP" : HasPractice ? "仅练习" : "未 VP";
    public string RankText => !HasVp ? "—" : Runs.Count(r => r.Kind == ParticipationKind.Virtual) > 1
        ? string.Join(" / ", Runs.Where(r => r.Kind == ParticipationKind.Virtual).Select(r => r.Rank.HasValue ? $"#{r.Rank}" : "待加载"))
        : Runs.First(r => r.Kind == ParticipationKind.Virtual) is { Rank: int rank, TotalTeams: int total }
            ? $"#{rank} / {total}" : Runs.First(r => r.Kind == ParticipationKind.Virtual).OfficialId.Length == 0 ? "待关联" : "待加载榜单";
    public string ResultHint => HasLocalResult ? "查看成绩" : "—";
    public string DateDisplay => string.IsNullOrWhiteSpace(DateText) ? "—" : DateText;
    public string ProblemCountText => ProblemCount > 0 ? ProblemCount.ToString() : "—";
    public string? RanklandUrl => RanklandPaths.BuildOfficialContestUrl(OfficialId);
    public bool HasRanklandUrl => !string.IsNullOrWhiteSpace(RanklandUrl);
    public bool HasGymUrl => !string.IsNullOrWhiteSpace(GymUrl);
    public bool HasQojUrl => !string.IsNullOrWhiteSpace(QojUrl);
    public string GymLinkText => GymId.HasValue ? $"Gym {GymId.Value}" : "未收录";
    public string QojLinkText => ExtractContestId(QojUrl) is { } id ? $"QOJ {id}" : "未收录";

    private static string? ExtractContestId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        return uri.Segments.LastOrDefault()?.Trim('/');
    }
}

public enum GymDirectoryCategory
{
    All,
    IcpcRegional,
    CcpcRegional,
    ProvincialInvitational,
    Final,
}

public class GymDirectoryViewModel : ObservableObject
{
    private readonly IDbContextFactory<VpDbContext> _dbFactory;
    private readonly RanklandClient _ranklandClient;
    private string _status = string.Empty;
    private string? _loadWarning;
    private bool _isLoading;
    private GymDirectoryCategory _selectedCategory = GymDirectoryCategory.All;
    private string _searchText = string.Empty;
    private int _participationFilter;
    private IReadOnlyList<GymDirectoryItem> _allItems = Array.Empty<GymDirectoryItem>();

    private static readonly IReadOnlyDictionary<string, string> OfficialGroupKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hbcpc7th"] = "2026-wuhan-hubei",
            ["icpc2026invitational-wuhan"] = "2026-wuhan-hubei",
        };

    public ObservableCollection<GymDirectoryItem> Items { get; } = new();
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilter(); } }
    public int ParticipationFilter { get => _participationFilter; set { if (SetProperty(ref _participationFilter, value)) ApplyFilter(); } }
    public bool IsReady => !IsLoading;
    public bool HasWarning => !string.IsNullOrEmpty(Status);
    public bool ShowEmpty => !IsLoading && Items.Count == 0;
    public string Summary => $"显示 {Items.Count} / {_allItems.Count} 场 · " +
        (_allItems.Any(i => !i.ParticipationAvailable) ? "VP 状态暂不可用" : $"已 VP {_allItems.Count(i => i.HasVp)} 场");

    public string Status
    {
        get => _status;
        private set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(HasWarning)); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (SetProperty(ref _isLoading, value)) { OnPropertyChanged(nameof(IsReady)); OnPropertyChanged(nameof(ShowEmpty)); } }
    }

    public GymDirectoryCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value))
                return;

            ApplyFilter();
        }
    }

    public GymDirectoryViewModel(IDbContextFactory<VpDbContext> dbFactory, RanklandClient ranklandClient)
    {
        _dbFactory = dbFactory;
        _ranklandClient = ranklandClient;
    }

    public async Task LoadAsync(bool refreshCatalog = false)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        _loadWarning = null;
        Status = string.Empty;
        try
        {
            var items = await BuildItemsAsync(refreshCatalog);
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var runs = await new OfficialParticipationService(db).LoadAsync();
                _allItems = items.Select(item => item with
                {
                    Runs = runs.Where(r =>
                        (!string.IsNullOrEmpty(r.OfficialId) && (r.OfficialId == item.OfficialId || item.OfficialIds.Contains(r.OfficialId))) ||
                        (string.IsNullOrEmpty(r.OfficialId) &&
                         (r.Platform == Platform.Codeforces && r.GymId == item.GymId ||
                          r.Platform == Platform.Qoj && item.QojUrl == $"https://qoj.ac/contest/{r.GymId}")))
                        .OrderBy(r => r.Kind).ThenByDescending(r => r.Solved).ThenBy(r => r.Penalty).ToList()
                }).ToList();
            }
            catch (Exception ex)
            {
                _loadWarning = $"本地参赛记录读取失败，VP 状态暂不可用：{ex.Message}";
                // Do not present unknown participation as a confirmed negative.
                _allItems = items.Select(i => i with { ParticipationAvailable = false }).ToList();
            }
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var items = _allItems
            .Where(i => SelectedCategory == GymDirectoryCategory.All || Classify(i) == SelectedCategory)
            .Where(i => string.IsNullOrWhiteSpace(SearchText) ||
                $"{i.DisplayName} {i.OfficialId} {i.DateText} {i.GymId} {i.QojUrl}".Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(i => ParticipationFilter switch { 1 => i.HasVp, 2 => i.ParticipationAvailable && !i.HasVp, 3 => i.HasPractice && !i.HasVp, _ => true })
            .ToList();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        Status = _loadWarning ?? string.Empty;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private async Task<IReadOnlyList<GymDirectoryItem>> BuildItemsAsync(bool refreshCatalog)
    {
        var curatedByOfficialId = CuratedContestMap.All
            .GroupBy(e => e.OfficialId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var cachedOfficials = SortOfficials(await db.OfficialContests
                .AsNoTracking()
                .ToListAsync());
            if (!refreshCatalog && cachedOfficials.Count > 0)
                return MergeOfficialAndCuratedItems(cachedOfficials, curatedByOfficialId);

            try
            {
                var officials = await RefreshOfficialCatalogAsync(db);
                if (officials.Count > 0)
                    return MergeOfficialAndCuratedItems(officials, curatedByOfficialId);
            }
            catch (Exception ex) when (cachedOfficials.Count > 0)
            {
                _loadWarning = $"官方目录更新失败，已显示上次同步结果：{ex.Message}";
                return MergeOfficialAndCuratedItems(cachedOfficials, curatedByOfficialId);
            }
        }
        catch (Exception ex)
        {
            // Fall back to the curated table if the local DB is temporarily unavailable.
            _loadWarning = $"官方目录加载失败，已显示内置预制表：{ex.Message}";
        }

        return CollapseAndSortItems(CuratedContestMap.All
            .Where(e => ContestDisplayNameBuilder.ShouldShowInDirectory(e.OfficialId, e.DisplayName, string.Empty))
            .Select(FromCurated)
            .Concat(CuratedContestLinkMap.All.Select(FromLinkOnly))
            .ToList());
    }

    private async Task<List<OfficialContest>> RefreshOfficialCatalogAsync(VpDbContext db)
    {
        var sync = new RanklandSyncService(db, _ranklandClient);
        var currentLeaves = await sync.GetCatalogLeavesAsync();
        await sync.SyncCatalogAsync();

        var currentIds = currentLeaves
            .Select(leaf => leaf.Path)
            .ToHashSet(StringComparer.Ordinal);

        return SortOfficials((await db.OfficialContests
            .AsNoTracking()
            .ToListAsync())
            .Where(official => currentIds.Contains(official.Id))
            .ToList());
    }

    private static List<OfficialContest> SortOfficials(List<OfficialContest> officials)
        => officials
            .OrderByDescending(SortTimestamp)
            .ThenBy(o => o.CategoryPath, StringComparer.Ordinal)
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

    private static long SortTimestamp(OfficialContest official)
    {
        if (official.StartAt.HasValue)
            return official.StartAt.Value;

        var year = ContestDisplayNameBuilder.ExtractYear(official.Id)
            ?? ContestDisplayNameBuilder.ExtractYear(official.Name);
        if (!year.HasValue)
            return 0;

        return new DateTimeOffset(year.Value, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeSeconds();
    }

    private static IReadOnlyList<GymDirectoryItem> MergeOfficialAndCuratedItems(
        IReadOnlyList<OfficialContest> officials,
        IReadOnlyDictionary<string, CuratedEntry> curatedByOfficialId)
    {
        var visibleOfficials = officials
            .Where(o => ContestDisplayNameBuilder.IsSupportedDirectoryCategory(o.CategoryPath))
            .Where(o => ContestDisplayNameBuilder.ShouldShowInDirectory(o.Id, o.Name, o.CategoryPath))
            .ToList();
        var officialIds = officials.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var items = visibleOfficials
            .Select(o =>
            {
                curatedByOfficialId.TryGetValue(o.Id, out var curated);
                return FromOfficial(o, curated);
            })
            .ToList();

        // Keep verified link entries visible even before their srk files have been synced locally.
        items.AddRange(CuratedContestMap.All
            .Where(e => !officialIds.Contains(e.OfficialId))
            .Where(e => ContestDisplayNameBuilder.ShouldShowInDirectory(e.OfficialId, e.DisplayName, string.Empty))
            .Select(FromCurated));

        items.AddRange(CuratedContestLinkMap.All
            .Where(e => ContestDisplayNameBuilder.ShouldShowInDirectory(e.OfficialId ?? string.Empty, e.DisplayName, string.Empty))
            .Select(FromLinkOnly));

        return CollapseAndSortItems(items);
    }

    private static GymDirectoryItem FromOfficial(OfficialContest official, CuratedEntry? curated)
    {
        var dateText = BuildDateText(official);

        return new GymDirectoryItem(
            curated?.GymId,
            ContestDisplayNameBuilder.BuildDisplayName(official.Id, official.Name, curated?.DisplayName),
            official.Id,
            dateText,
            official.ProblemCount > 0 ? official.ProblemCount : curated?.ExpectedProblemCount ?? 0,
            BuildGymUrl(curated?.GymId),
            BuildQojUrl(curated?.QojContestId));
    }

    private static GymDirectoryItem FromCurated(CuratedEntry entry)
        => new(
            entry.GymId,
            ContestDisplayNameBuilder.BuildDisplayName(entry.OfficialId, entry.DisplayName),
            entry.OfficialId,
            ContestDisplayNameBuilder.ExtractDateText(entry.DisplayName) ?? string.Empty,
            entry.ExpectedProblemCount,
            BuildGymUrl(entry.GymId),
            BuildQojUrl(entry.QojContestId));

    private static GymDirectoryItem FromLinkOnly(CuratedContestLink entry)
        => new(
            entry.GymId,
            entry.DisplayName,
            entry.OfficialId ?? string.Empty,
            entry.DateText,
            entry.ExpectedProblemCount,
            BuildGymUrl(entry.GymId),
            BuildQojUrl(entry.QojContestId),
            IsSupplementaryLink: !string.IsNullOrWhiteSpace(entry.OfficialId));

    private static IReadOnlyList<GymDirectoryItem> CollapseAndSortItems(IReadOnlyList<GymDirectoryItem> items)
        => items
            .GroupBy(BuildDirectoryKey, StringComparer.Ordinal)
            .Select(MergeEquivalentItems)
            .OrderByDescending(SortDateText)
            .ThenBy(i => i.DisplayName, StringComparer.Ordinal)
            .ToList();

    private static string BuildDirectoryKey(GymDirectoryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.OfficialId) &&
            OfficialGroupKeys.TryGetValue(item.OfficialId, out var group))
        {
            return $"official-group:{group}";
        }

        if (item.GymId.HasValue)
            return $"gym:{item.GymId.Value}";

        if (!string.IsNullOrWhiteSpace(item.OfficialId))
            return $"official:{item.OfficialId}";

        return $"display:{item.DateText}|{item.DisplayName}";
    }

    private static GymDirectoryItem MergeEquivalentItems(IGrouping<string, GymDirectoryItem> group)
    {
        var items = group.ToList();
        var primary = items
            .OrderBy(i => i.IsSupplementaryLink ? 1 : 0)
            .ThenByDescending(i => HasDirectoryLinks(i) ? 1 : 0)
            .ThenByDescending(i => i.ProblemCount)
            .ThenBy(i => i.OfficialId, StringComparer.Ordinal)
            .First();

        var gymId = items.FirstOrDefault(i => i.GymId.HasValue)?.GymId;
        var gymUrl = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.GymUrl))?.GymUrl;
        var qojUrl = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.QojUrl))?.QojUrl;
        var officialId = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.OfficialId))?.OfficialId ?? string.Empty;
        var dateText = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.DateText))?.DateText ?? primary.DateText;
        var problemCount = items.Max(i => i.ProblemCount);

        return primary with
        {
            GymId = gymId,
            OfficialId = officialId,
            DateText = dateText,
            ProblemCount = problemCount,
            GymUrl = gymUrl,
            QojUrl = qojUrl,
            OfficialIds = items.SelectMany(i => i.OfficialIds.Append(i.OfficialId)).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList(),
        };
    }

    private static bool HasDirectoryLinks(GymDirectoryItem item)
        => !string.IsNullOrWhiteSpace(item.GymUrl) || !string.IsNullOrWhiteSpace(item.QojUrl);

    private static DateTime SortDateText(GymDirectoryItem item)
        => DateTime.TryParse(item.DateText, out var date) ? date : DateTime.MinValue;

    private static GymDirectoryCategory Classify(GymDirectoryItem item)
    {
        var id = item.OfficialId.ToLowerInvariant();
        var name = item.DisplayName;
        var text = $"{id} {name}";

        if (ContainsAny(text, "final", "World Finals", "EC Final", "China Final"))
            return GymDirectoryCategory.Final;

        if (ContestDisplayNameBuilder.IsProvincialOrInvitational(item.OfficialId, item.DisplayName))
        {
            return GymDirectoryCategory.ProvincialInvitational;
        }

        if (id.StartsWith("ccpc", StringComparison.Ordinal) || name.Contains("CCPC", StringComparison.OrdinalIgnoreCase))
            return GymDirectoryCategory.CcpcRegional;

        return GymDirectoryCategory.IcpcRegional;
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string? BuildGymUrl(long? gymId)
        => gymId.HasValue ? $"https://codeforces.com/gym/{gymId.Value}" : null;

    private static string? BuildQojUrl(int? qojContestId)
        => qojContestId.HasValue ? $"https://qoj.ac/contest/{qojContestId.Value}" : null;

    private static string BuildDateText(OfficialContest official)
    {
        var fromName = ContestDisplayNameBuilder.ExtractDateText(official.Name);
        if (!string.IsNullOrWhiteSpace(fromName))
            return fromName;

        return official.StartAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(official.StartAt.Value).ToLocalTime().ToString("yyyy-MM-dd")
            : string.Empty;
    }
}
