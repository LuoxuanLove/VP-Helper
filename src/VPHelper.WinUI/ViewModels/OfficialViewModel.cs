using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;

namespace VPHelper.WinUI.ViewModels;

public sealed class OfficialContestItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CategoryPath { get; init; } = string.Empty;
    public long? StartAt { get; init; }
    public int ProblemCount { get; init; }
}

public sealed class CandidateItem
{
    public string OfficialId { get; init; } = string.Empty;
    public string OfficialName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public bool HasDateText => !string.IsNullOrWhiteSpace(DateText);
    public string DetailText => OfficialId;

    public override string ToString() => OfficialName;
}

/// <summary>One row of the bulk auto-match overview table.</summary>
public sealed class MappingRowItem : ObservableObject
{
    private CandidateItem? _selectedCandidate;
    private string _officialSearchText = string.Empty;

    public long GymId { get; init; }
    public Platform Platform { get; init; }
    public string GymTitle { get; init; } = string.Empty;
    public string ContestUrl => Platform == Platform.Qoj
        ? $"https://qoj.ac/contest/{GymId}"
        : $"https://codeforces.com/gym/{GymId}";
    public string PlatformBadge => Platform == Platform.Qoj ? "QOJ" : "CF";
    public string ContestIdText => Platform == Platform.Qoj ? $"QOJ {GymId}" : $"Gym {GymId}";
    public string ContestTitleText => string.IsNullOrWhiteSpace(GymTitle) ? ContestIdText : GymTitle;
    public bool HasDistinctContestTitle =>
        !string.IsNullOrWhiteSpace(GymTitle)
        && !string.Equals(GymTitle.Trim(), ContestIdText, StringComparison.OrdinalIgnoreCase);
    public string OfficialId { get; init; } = string.Empty;
    public string OfficialName { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public int Status { get; init; }
    public long? StartTime { get; init; }
    public int ProblemCount { get; init; }
    public ObservableCollection<CandidateItem> OfficialSuggestions { get; } = new();

    public CandidateItem? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
                OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public string OfficialSearchText
    {
        get => _officialSearchText;
        set
        {
            if (SetProperty(ref _officialSearchText, value))
            {
                OnPropertyChanged(nameof(OfficialPickerText));
            }
        }
    }

    public bool IsConfirmed =>
        Status == MappingStatus.ManualConfirmed || Status == MappingStatus.AutoConfirmed;

    public bool CanConfirm => !IsConfirmed && SelectedCandidate != null;
    public bool CanClear => IsConfirmed;

    public string ConfidenceText => Status == MappingStatus.Unmapped ? "—" : $"{Confidence:P0}";

    public string MatchSourceText => Status switch
    {
        MappingStatus.ManualConfirmed => "手动",
        MappingStatus.AutoConfirmed => Platform == Platform.Qoj ? "QOJ 目录" : "赛事目录",
        MappingStatus.Unmapped => "-",
        _ => "-",
    };

    public string StatusText => Status switch
    {
        MappingStatus.ManualConfirmed => "手动确认",
        MappingStatus.AutoConfirmed => "自动确认",
        MappingStatus.Unmapped => "未匹配",
        _ => "候选",
    };

    public string OfficialDisplay => IsConfirmed ? OfficialName : "选择官方比赛";
    public string OfficialPickerText => string.IsNullOrWhiteSpace(OfficialSearchText)
        ? "搜索或选择官方比赛"
        : OfficialSearchText;
}

/// <summary>
/// Backs the rankland official contests view: sync from srk-collection, list officials, and the
/// gym↔official mapping surface (DESIGN 6.3 / 6.4).
/// </summary>
public sealed class OfficialViewModel : ObservableObject
{
    private readonly IDbContextFactory<VpDbContext> _dbFactory;
    private readonly RanklandClient _client;

    private bool _isBusy;
    private bool _isLoading;
    private string _status = string.Empty;
    private double _progress;
    private CancellationTokenSource? _cts;

    public ObservableCollection<OfficialContestItem> Officials { get; } = new();
    public ObservableCollection<MappingRowItem> Overview { get; } = new();
    public ObservableCollection<MappingRowItem> VisibleOverview { get; } = new();
    private string _searchText = string.Empty;
    private int _mappingFilter;
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyOverviewFilter(); } }
    public int MappingFilter { get => _mappingFilter; set { if (SetProperty(ref _mappingFilter, value)) ApplyOverviewFilter(); } }
    public bool ShowOverviewEmpty => !IsLoading && VisibleOverview.Count == 0;
    public string OverviewEmptyText => Overview.Count == 0 ? "暂无本地比赛，请先在账号管理中同步。" : "没有符合条件的比赛，请调整搜索或关联状态。";

    private void ApplyOverviewFilter()
    {
        VisibleOverview.Clear();
        foreach (var row in Overview.Where(r => MappingFilter switch { 1 => !r.IsConfirmed, 2 => r.IsConfirmed, _ => true })
            .Where(r => string.IsNullOrWhiteSpace(SearchText) ||
                $"{r.GymTitle} {r.GymId} {r.OfficialName} {r.PlatformBadge}".Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)))
            VisibleOverview.Add(row);
        OnPropertyChanged(nameof(ShowOverviewEmpty));
        OnPropertyChanged(nameof(OverviewEmptyText));
    }
    public int TotalContestCount => Overview.Count;
    public int CodeforcesContestCount => Overview.Count(x => x.Platform == Platform.Codeforces);
    public int QojContestCount => Overview.Count(x => x.Platform == Platform.Qoj);
    public int MappedContestCount => Overview.Count(x => x.IsConfirmed);
    public int UnmappedContestCount => Overview.Count(x => !x.IsConfirmed);
    public string OverviewSummary => $"全部 {TotalContestCount} · 待关联 {UnmappedContestCount} · 已关联 {MappedContestCount}";
    public string PlatformSummary => $"Codeforces {CodeforcesContestCount} · QOJ {QojContestCount}";

    public string Attribution => RanklandPaths.Attribution;

    public OfficialViewModel(IDbContextFactory<VpDbContext> dbFactory, RanklandClient client)
    {
        _dbFactory = dbFactory;
        _client = client;
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy && !IsLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsContentReady));
                OnPropertyChanged(nameof(ShowOverviewEmpty));
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsContentReady => !IsLoading;
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

    public async Task LoadAsync()
    {
        if (IsLoading || IsBusy) return;
        IsLoading = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await LoadOfficialsAsync(db);
            await LoadOverviewCoreAsync(db, reloadOfficials: false);
            Status = $"已加载 {Officials.Count} 场官方比赛和 {Overview.Count} 场本地参赛记录。";
        }
        catch (Exception ex) { Status = $"比赛关联加载失败：{ex.Message}"; }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Load the gym↔official mapping overview table (bulk auto-match result + manual picks).</summary>
    public async Task LoadOverviewAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await LoadOverviewCoreAsync(db, reloadOfficials: true);
    }

    private async Task LoadOverviewCoreAsync(VpDbContext db, bool reloadOfficials)
    {
        if (reloadOfficials)
            await LoadOfficialsAsync(db);

        var gymFacts = await db.GymContests
            .ToDictionaryAsync(g => (g.Platform, g.GymId));

        var mapping = new MappingService(db);
        var rows = await mapping.GetOverviewAsync();
        Overview.Clear();
        foreach (var r in rows)
        {
            gymFacts.TryGetValue((r.Platform, r.GymId), out var gym);
            var item = new MappingRowItem
            {
                Platform = r.Platform,
                GymId = r.GymId,
                GymTitle = r.GymTitle,
                OfficialId = r.OfficialId ?? string.Empty,
                OfficialName = BuildOfficialDisplayName(r.OfficialId, r.OfficialName, null),
                Confidence = r.Confidence,
                Status = r.Status,
                StartTime = gym?.StartTime,
                ProblemCount = gym?.ProblemCount ?? 0,
            };
            ApplyInitialCandidate(item);
            Overview.Add(item);
        }
        OnPropertyChanged(nameof(TotalContestCount));
        OnPropertyChanged(nameof(CodeforcesContestCount));
        OnPropertyChanged(nameof(QojContestCount));
        OnPropertyChanged(nameof(MappedContestCount));
        OnPropertyChanged(nameof(UnmappedContestCount));
        OnPropertyChanged(nameof(OverviewSummary));
        OnPropertyChanged(nameof(PlatformSummary));
        ApplyOverviewFilter();
    }

    /// <summary>
    /// Bulk-match gyms using only the curated contest directory. Gyms outside the directory stay
    /// unmapped, but can still be handled with the row-level official picker.
    /// </summary>
    public async Task AutoMatchAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<MatchProgress>(p =>
            {
                Progress = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                Status = $"[{p.Done}/{p.Total}] 正在按赛事目录匹配 {p.Current}";
            });
            AutoMatchSummary summary;
            await using (var db = await _dbFactory.CreateDbContextAsync())
            {
                var mapping = new MappingService(db);
                summary = await mapping.AutoMatchAllAsync(progress: progress, ct: _cts.Token);
            }
            Status = $"自动匹配完成：新增或刷新 {summary.AutoConfirmed} 个目录关联，"
                   + $"保留 {summary.SkippedManual} 个手动关联，{summary.NoCandidate} 个目录外比赛保持未关联。";
            await LoadOverviewAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "自动匹配已取消。";
        }
        catch (Exception ex)
        {
            Status = $"自动匹配失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    public void RefreshRowOfficialSuggestions(MappingRowItem? row, string? query, bool preserveSelection = false)
    {
        if (row == null || row.IsConfirmed) return;
        ApplySuggestions(row, query, preserveSelection);
    }

    public void ClearAllRowSuggestions()
    {
        foreach (var row in Overview)
            row.OfficialSuggestions.Clear();
    }

    public void ChooseRowOfficialSuggestion(MappingRowItem? row, CandidateItem? candidate)
    {
        if (row == null || candidate == null || row.IsConfirmed) return;
        row.SelectedCandidate = candidate;
        row.OfficialSearchText = candidate.OfficialName;
    }

    public void SubmitRowOfficialQuery(MappingRowItem? row, CandidateItem? chosen, string? query)
    {
        if (row == null || row.IsConfirmed) return;
        if (chosen != null)
        {
            ChooseRowOfficialSuggestion(row, chosen);
            return;
        }

        ApplySuggestions(row, query, preserveSelection: false);
    }

    /// <summary>Manually confirm the selected overview row's currently chosen official.</summary>
    public async Task AcceptOverviewRowAsync(MappingRowItem? row)
    {
        if (row == null || row.IsConfirmed || row.SelectedCandidate == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var mapping = new MappingService(db);
        await mapping.ConfirmAsync(row.Platform, row.GymId, row.SelectedCandidate.OfficialId);
        Status = $"已确认 {row.ContestIdText} ↔ {row.SelectedCandidate.OfficialName}";
        await LoadOverviewAsync();
    }

    public async Task ClearOverviewRowAsync(MappingRowItem? row)
    {
        if (row == null || !row.IsConfirmed) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var mapping = new MappingService(db);
        await mapping.ClearAsync(row.Platform, row.GymId);
        Status = $"已解除 {row.ContestIdText} 的官方比赛关联";
        await LoadOverviewAsync();
    }

    /// <summary>Sync official contests from srk-collection. Bounded on first run for responsiveness.</summary>
    public async Task SyncAsync(int? max = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RanklandProgress>(p =>
            {
                Progress = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                Status = $"[{p.Done}/{p.Total}] {p.Message}";
            });
            await using var db = await _dbFactory.CreateDbContextAsync();
            var rankland = new RanklandSyncService(db, _client);
            int n = await rankland.SyncAsync(max, forceRefresh: false, progress, _cts.Token);
            Status = $"Synced {n} contests.";
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Sync cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    public void CancelSync() => _cts?.Cancel();

    private async Task LoadOfficialsAsync(VpDbContext db)
    {
        var officials = await db.OfficialContests
            .OrderBy(o => o.CategoryPath)
            .ThenBy(o => o.Name)
            .ToListAsync();

        Officials.Clear();
        foreach (var o in officials)
        {
            Officials.Add(new OfficialContestItem
            {
                Id = o.Id,
                Name = o.Name,
                DisplayName = BuildOfficialDisplayName(o.Id, o.Name, null),
                DateText = BuildOfficialDateText(o.Name, o.StartAt),
                Category = o.Category ?? string.Empty,
                CategoryPath = o.CategoryPath,
                StartAt = o.StartAt,
                ProblemCount = o.ProblemCount,
            });
        }
    }

    private void ApplySuggestions(MappingRowItem row, string? query, bool preserveSelection)
    {
        var previousId = row.SelectedCandidate?.OfficialId;
        var suggestions = BuildSuggestions(query).ToList();

        row.OfficialSuggestions.Clear();
        foreach (var item in suggestions)
            row.OfficialSuggestions.Add(item);

        // A picker reopen may preserve the explicitly selected item. A text-change refresh passes
        // false, invalidating the previous choice so an old result cannot be confirmed by mistake.
        row.SelectedCandidate = preserveSelection
            ? suggestions.FirstOrDefault(c => c.OfficialId == previousId)
            : null;
        if (preserveSelection)
            row.OfficialSearchText = row.SelectedCandidate?.OfficialName ?? string.Empty;
    }

    private void ApplyInitialCandidate(MappingRowItem row)
    {
        if (row.IsConfirmed)
        {
            row.OfficialSearchText = row.OfficialName;
            row.OfficialSuggestions.Clear();
            return;
        }

        // An unmapped row starts empty. Loading the page must never create an implicit selection.
        row.SelectedCandidate = null;
        row.OfficialSearchText = string.Empty;
        row.OfficialSuggestions.Clear();
    }

    private IReadOnlyList<CandidateItem> BuildSuggestions(string? query)
    {
        string q = query?.Trim() ?? string.Empty;
        if (q.Length == 0)
            return Array.Empty<CandidateItem>();

        // Literal search only. Gym/QOJ metadata must never select an official contest on the
        // user's behalf, and no confidence score is calculated for manual search results.
        return Officials
            .Where(o => Contains(o.Id, q)
                     || Contains(o.Name, q)
                     || Contains(o.DisplayName, q)
                     || Contains(o.DateText, q)
                     || Contains(o.Category, q)
                     || Contains(o.CategoryPath, q))
            .OrderByDescending(o => string.Equals(o.Id, q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(o => string.Equals(o.DisplayName, q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(o => o.DisplayName.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .ThenBy(o => o.DisplayName, StringComparer.CurrentCulture)
            .Take(8)
            .Select(o => new CandidateItem
            {
                OfficialId = o.Id,
                OfficialName = o.DisplayName,
                DateText = o.DateText,
            })
            .ToList();

        static bool Contains(string value, string query)
            => !string.IsNullOrWhiteSpace(value)
               && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOfficialDisplayName(string? officialId, string? sourceName, string? curatedName)
    {
        if (string.IsNullOrWhiteSpace(officialId))
            return sourceName ?? string.Empty;

        return ContestDisplayNameBuilder.BuildDisplayName(
            officialId,
            string.IsNullOrWhiteSpace(sourceName) ? curatedName ?? officialId : sourceName,
            curatedName);
    }

    private static string BuildOfficialDateText(string? sourceName, long? startAt)
    {
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            var fromName = ContestDisplayNameBuilder.ExtractDateText(sourceName);
            if (!string.IsNullOrWhiteSpace(fromName))
                return fromName;
        }

        return startAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(startAt.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : string.Empty;
    }
}
