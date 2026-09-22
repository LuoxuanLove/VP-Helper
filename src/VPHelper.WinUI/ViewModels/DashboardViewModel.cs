using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Services;

namespace VPHelper.WinUI.ViewModels;

/// <summary>One stat card shown at the top of the dashboard.</summary>
public sealed class StatCardItem
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
}

/// <summary>One recent-VP row shown in the dashboard list.</summary>
public sealed class RecentVpItem
{
    public Platform Platform { get; init; }
    public long GymId { get; init; }
    public string PlatformBadge => Platform == Platform.Qoj ? "QOJ" : "CF";
    public string Title { get; init; } = string.Empty;
    public string? OfficialName { get; init; }
    public string DisplayTitle => OfficialName ?? Title;
    public string Handle { get; init; } = string.Empty;
    public int SolvedCount { get; init; }
    public int ProblemCount { get; init; }
    public string StartTimeText { get; init; } = string.Empty;
    public string SolvedText => $"通过 {SolvedCount} 题";
}

/// <summary>
/// Backs the dashboard (statistics overview) page. Loads aggregated VP stats from the local SQLite
/// store — no network calls; data reflects the last successful sync across all accounts and platforms.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private const int PageSize = 10;

    private readonly IDbContextFactory<VpDbContext> _dbFactory;
    private readonly List<RecentVpItem> _allRecentVps = new();

    private bool _isEmpty = true;
    private string _lastRefreshedText = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private string _pageText = "1 / 1";
    private bool _canGoPrevious;
    private bool _canGoNext;

    public ObservableCollection<StatCardItem> StatCards { get; } = new();
    public ObservableCollection<RecentVpItem> RecentVps { get; } = new();

    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
                PageText = $"{_currentPage} / {_totalPages}";
        }
    }
    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, Math.Max(1, value)))
                PageText = $"{_currentPage} / {_totalPages}";
        }
    }
    public string PageText { get => _pageText; private set => SetProperty(ref _pageText, value); }
    public bool CanGoPrevious { get => _canGoPrevious; private set => SetProperty(ref _canGoPrevious, value); }
    public bool CanGoNext { get => _canGoNext; private set => SetProperty(ref _canGoNext, value); }
    public string LastRefreshedText
    {
        get => _lastRefreshedText;
        set => SetProperty(ref _lastRefreshedText, value);
    }

    public DashboardViewModel(IDbContextFactory<VpDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>Reload all stats from the local store (call on page Loaded and after any sync).</summary>
    public async Task RefreshAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // -- totals ----------------------------------------------------------
        var allVirtual = await db.VpResults
            .Where(v => v.Kind == ParticipationKind.Virtual && v.SolvedCount > 0)
            .ToListAsync();
        var allPractice = await db.VpResults
            .Where(v => v.Kind == ParticipationKind.Practice && v.SolvedCount > 0)
            .ToListAsync();

        // Choose one result per (Platform, GymId): the account with the most solved,
        // honoring manual conflict selections where present.
        var selections = await db.GymVpSelections
            .ToDictionaryAsync(s => (s.Platform, s.GymId), s => s.Handle);

        int totalContests = 0, totalSolved = 0;
        int cfContests = 0, qojContests = 0;
        var chosenVirtuals = new List<VpResult>();

        foreach (var group in allVirtual.GroupBy(v => (v.Platform, v.GymId)))
        {
            var chosen = PickChosenResult(group.ToList(), selections);
            if (chosen == null) continue; // unresolved conflict — exclude

            totalContests++;
            totalSolved += chosen.SolvedCount;
            chosenVirtuals.Add(chosen);
            if (chosen.Platform == Platform.Codeforces) cfContests++;
            else qojContests++;
        }

        // -- account count ---------------------------------------------------
        var accountCount = await db.Accounts.CountAsync();
        var submissionCount = await db.Submissions.CountAsync();
        var practiceContests = allPractice
            .Select(v => (v.Platform, v.GymId))
            .Distinct()
            .Count();

        // -- build stat cards ------------------------------------------------
        StatCards.Clear();
        StatCards.Add(new StatCardItem { Label = "VP 比赛总数", Value = totalContests.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "VP 总过题数", Value = totalSolved.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "Codeforces", Value = cfContests.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "QOJ", Value = qojContests.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "已跟踪账号", Value = accountCount.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "补题比赛", Value = practiceContests.ToString(), Glyph = "" });
        StatCards.Add(new StatCardItem { Label = "提交记录", Value = submissionCount.ToString(), Glyph = "" });

        // -- recent VP contests (newest first, up to 20) ----------------------
        var gyms = await db.GymContests.ToListAsync();
        var gymById = gyms.ToDictionary(g => (g.Platform, g.GymId));
        var officialNames = await LoadConfirmedOfficialNamesAsync(db);

        var recentVp = chosenVirtuals
            .OrderByDescending(v =>
            {
                gymById.TryGetValue((v.Platform, v.GymId), out var gym);
                return gym?.StartTime ?? 0;
            })
            .ToList();

        _allRecentVps.Clear();
        foreach (var v in recentVp)
        {
            gymById.TryGetValue((v!.Platform, v.GymId), out var gym);
            _allRecentVps.Add(new RecentVpItem
            {
                Platform = v.Platform,
                GymId = v.GymId,
                Title = gym?.Title ?? (v.Platform == Platform.Qoj ? $"QOJ {v.GymId}" : $"Gym {v.GymId}"),
                OfficialName = officialNames.TryGetValue((v.Platform, v.GymId), out var officialName)
                    ? officialName
                    : null,
                Handle = v.Handle,
                SolvedCount = v.SolvedCount,
                ProblemCount = gym?.ProblemCount ?? 0,
                StartTimeText = gym?.StartTime is { } t and > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("yyyy-MM-dd")
                    : "—",
            });
        }
        TotalPages = Math.Max(1, (int)Math.Ceiling(_allRecentVps.Count / (double)PageSize));
        CurrentPage = Math.Min(Math.Max(1, CurrentPage), TotalPages);
        ApplyPage();

        IsEmpty = totalContests == 0 && practiceContests == 0 && submissionCount == 0;
        LastRefreshedText = $"基于本地已同步数据 · 统计刷新于 {DateTime.Now:HH:mm}";
    }

    public void NextPage()
    {
        if (!CanGoNext) return;
        CurrentPage++;
        ApplyPage();
    }

    public void PreviousPage()
    {
        if (!CanGoPrevious) return;
        CurrentPage--;
        ApplyPage();
    }

    private void ApplyPage()
    {
        RecentVps.Clear();
        foreach (var item in _allRecentVps
                     .Skip((CurrentPage - 1) * PageSize)
                     .Take(PageSize))
            RecentVps.Add(item);

        CanGoPrevious = CurrentPage > 1;
        CanGoNext = CurrentPage < TotalPages;
        PageText = $"{CurrentPage} / {TotalPages}";
    }

    private static VpResult? PickChosenResult(
        IReadOnlyList<VpResult> results,
        IReadOnlyDictionary<(Platform Platform, long GymId), string> selections)
    {
        if (results.Count == 0) return null;
        if (results.Count == 1) return results[0];

        var key = (results[0].Platform, results[0].GymId);
        return selections.TryGetValue(key, out var selectedHandle)
            ? results.FirstOrDefault(v => v.Handle == selectedHandle) ?? results[0]
            : null;
    }

    private static async Task<Dictionary<(Platform Platform, long GymId), string>> LoadConfirmedOfficialNamesAsync(
        VpDbContext db)
    {
        var rows = await db.ContestMappings
            .Where(m => m.Confirmed == MappingStatus.ManualConfirmed ||
                        m.Confirmed == MappingStatus.AutoConfirmed)
            .ToListAsync();
        var names = await db.OfficialContests.ToDictionaryAsync(o => o.Id, o => o.Name);
        var curatedNames = CuratedContestMap.All
            .GroupBy(entry => entry.OfficialId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ContestDisplayNameBuilder.BuildDisplayName(group.Key, group.First().DisplayName),
                StringComparer.Ordinal);

        return rows
            .GroupBy(m => (m.Platform, m.GymId))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var pick = g.OrderByDescending(m => m.Confirmed == MappingStatus.ManualConfirmed ? 1 : 0)
                        .ThenByDescending(m => m.Confidence)
                        .First();
                    return names.TryGetValue(pick.OfficialId, out var syncedName)
                        ? ContestDisplayNameBuilder.BuildDisplayName(pick.OfficialId, syncedName)
                        : curatedNames.TryGetValue(pick.OfficialId, out var curatedName)
                            ? curatedName
                            : pick.OfficialId;
                });
    }
}
