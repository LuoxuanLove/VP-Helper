using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;
using VPHelper.Core.Services;
using Windows.UI;

namespace VPHelper.WinUI.ViewModels;

public enum ProblemSortMode
{
    Difficulty,
    ContestOrder,
}

/// <summary>A gym contest row for the list.</summary>
public sealed record GymContestItem
{
    public Platform Platform { get; init; }
    public long GymId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public long? StartTime { get; init; }
    public int SolvedCount { get; init; }
    public int ProblemCount { get; init; }
    public string StartTimeText { get; init; } = string.Empty;
    public string? OfficialId { get; init; }
    public string? MappedOfficialName { get; set; }
    public bool HasResult { get; init; }
    public bool HasVirtualRun { get; init; }
    public string PlatformBadge => Platform == Platform.Qoj ? "QOJ" : "CF";
    public bool IsMapped => !string.IsNullOrWhiteSpace(MappedOfficialName);
    public string DisplayTitle => MappedOfficialName ?? Title;
    public string? RanklandUrl => RanklandPaths.BuildOfficialContestUrl(OfficialId);
    public bool HasRanklandUrl => !string.IsNullOrWhiteSpace(RanklandUrl);
    public string SolvedText
    {
        get
        {
            if (!HasResult) return "无本地成绩";
            var handlePrefix = string.IsNullOrWhiteSpace(Handle) ? string.Empty : $"{Handle} · ";
            return HasVirtualRun
                ? $"{handlePrefix}VP · 通过 {SolvedCount} 题"
                : $"{handlePrefix}练习 · 通过 {SolvedCount} 题";
        }
    }
}

/// <summary>A per-problem detail row in the single-contest VP view.</summary>
public sealed class ProblemDetailItem
{
    public string Index { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool Solved { get; init; }
    public string StatusText { get; init; } = "—";
    public string PenaltyText { get; init; } = "—";
    public string FirstAcText { get; init; } = "—";
    public int? OfficialAcceptedCount { get; init; }
    public int? OfficialSubmittedCount { get; init; }
    public int ContestSortOrder { get; init; } = int.MaxValue;
    public string ThroughText => OfficialAcceptedCount is int accepted
        ? $"{accepted}/{(OfficialSubmittedCount is int submitted ? submitted.ToString() : "—")}"
        : "—";
    public string ProblemUrl { get; init; } = string.Empty;
    public bool HasProblemUrl => !string.IsNullOrWhiteSpace(ProblemUrl);
}

/// <summary>
/// One row in the official standings side-panel.
/// When <see cref="IsVpRow"/> is true this is the simulated insertion point for the VP result.
/// </summary>
public sealed class OfficialStandingItem
{
    public int Rank { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public string? Organization { get; init; }
    public int SolvedCount { get; init; }
    public long Penalty { get; init; }
    /// <summary>True when this row represents the user's own VP result (not an official team).</summary>
    public bool IsVpRow { get; init; }
    public bool ShowGoldBoundary { get; set; }
    public bool ShowSilverBoundary { get; set; }
    public bool ShowBronzeBoundary { get; set; }
    public string GoldBoundaryText { get; set; } = string.Empty;
    public string SilverBoundaryText { get; set; } = string.Empty;
    public string BronzeBoundaryText { get; set; } = string.Empty;
    public string PenaltyText => $"{Penalty / 60} min";
    public string RankText => Rank == 0 ? "—" : Rank.ToString();
}

/// <summary>
/// Backs the contest list + single-contest VP detail. Loads gym contests, their VP results,
/// mapping status, and (when mapped) the official standings with the VP result simulated inline.
/// </summary>
public sealed class ContestsViewModel : ObservableObject
{
    private const long DefaultContestDurationSeconds = 5 * 60 * 60;
    private static readonly Brush AwardGoldBrush = CreateBrush(0xD6, 0xA7, 0x2D);
    private static readonly Brush AwardGoldRibbonBrush = CreateBrush(0x8E, 0x6A, 0x15);
    private static readonly Brush AwardSilverBrush = CreateBrush(0x98, 0xA3, 0xAF);
    private static readonly Brush AwardSilverRibbonBrush = CreateBrush(0x62, 0x6D, 0x78);
    private static readonly Brush AwardBronzeBrush = CreateBrush(0xB8, 0x73, 0x45);
    private static readonly Brush AwardBronzeRibbonBrush = CreateBrush(0x78, 0x43, 0x27);
    private static readonly Brush AwardNeutralBrush = CreateBrush(0x5E, 0x68, 0x75);
    private static readonly Brush AwardNeutralRibbonBrush = CreateBrush(0x3E, 0x47, 0x52);
    private static readonly Brush AwardDarkTextBrush = CreateBrush(0x1A, 0x1A, 0x1A);
    private static readonly Brush AwardLightTextBrush = CreateBrush(0xFA, 0xFA, 0xFA);

    private readonly IDbContextFactory<VpDbContext> _dbFactory;
    private readonly RanklandClient _ranklandClient;

    private string _handle = string.Empty;
    private GymContestItem? _selected;
    private string _detailTitle = "请选择一场比赛";
    private string _vpSummary = string.Empty;
    private string _estimateRankText = string.Empty;
    private string _estimateTotalText = string.Empty;
    private string _goldMedalLineText = string.Empty;
    private string _silverMedalLineText = string.Empty;
    private string _bronzeMedalLineText = string.Empty;
    private string _estimatedAwardTitle = string.Empty;
    private string _estimatedAwardSymbol = string.Empty;
    private string _estimatedAwardToolTip = string.Empty;
    private Brush _estimatedAwardBrush = AwardNeutralBrush;
    private Brush _estimatedAwardRibbonBrush = AwardNeutralRibbonBrush;
    private Brush _estimatedAwardForegroundBrush = AwardLightTextBrush;
    private string _medalLineSourceText = string.Empty;
    private string _medalLineSourceDetail = string.Empty;
    private string _officialPlaceholderTitle = "暂无官方榜单";
    private string _officialPlaceholderMessage = "请选择一场比赛。";
    private bool _hasEstimate;
    private bool _hasMedalLines;
    private bool _hasOfficialStandings;
    private bool _isEmpty = true;
    private bool _isDetailLoading;
    private ProblemSortMode _problemSortMode = ProblemSortMode.Difficulty;
    private int _detailLoadVersion;
    private string _contestSearchText = string.Empty;
    private readonly List<GymContestItem> _allContests = new();

    public ObservableCollection<GymContestItem> Contests { get; } = new();
    public ObservableCollection<ProblemDetailItem> Problems { get; } = new();
    /// <summary>Official standings rows with the VP result injected at its estimated position.</summary>
    public ObservableCollection<OfficialStandingItem> OfficialStandings { get; } = new();

    public ContestsViewModel(IDbContextFactory<VpDbContext> dbFactory, RanklandClient ranklandClient)
    {
        _dbFactory = dbFactory;
        _ranklandClient = ranklandClient;
    }

    public string Handle { get => _handle; set => SetProperty(ref _handle, value); }
    public string ContestSearchText
    {
        get => _contestSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _contestSearchText, normalized)) return;
            ApplyContestFilter();
        }
    }
    public bool HasContestSearch => !string.IsNullOrWhiteSpace(ContestSearchText);
    public bool ShowContestSearchEmpty => _allContests.Count > 0 && Contests.Count == 0;
    public string ContestSearchSummary => HasContestSearch
        ? $"匹配 {Contests.Count} / {_allContests.Count} 场比赛"
        : $"{Contests.Count} 场比赛";
    public ProblemSortMode ProblemSortMode => _problemSortMode;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }
    public string DetailTitle { get => _detailTitle; set => SetProperty(ref _detailTitle, value); }
    public string VpSummary { get => _vpSummary; set => SetProperty(ref _vpSummary, value); }
    public string EstimateRankText { get => _estimateRankText; set => SetProperty(ref _estimateRankText, value); }
    public string EstimateTotalText { get => _estimateTotalText; set => SetProperty(ref _estimateTotalText, value); }
    public string GoldMedalLineText { get => _goldMedalLineText; private set => SetProperty(ref _goldMedalLineText, value); }
    public string SilverMedalLineText { get => _silverMedalLineText; private set => SetProperty(ref _silverMedalLineText, value); }
    public string BronzeMedalLineText { get => _bronzeMedalLineText; private set => SetProperty(ref _bronzeMedalLineText, value); }
    public string EstimatedAwardTitle { get => _estimatedAwardTitle; private set => SetProperty(ref _estimatedAwardTitle, value); }
    public string EstimatedAwardSymbol { get => _estimatedAwardSymbol; private set => SetProperty(ref _estimatedAwardSymbol, value); }
    public string EstimatedAwardToolTip { get => _estimatedAwardToolTip; private set => SetProperty(ref _estimatedAwardToolTip, value); }
    public Brush EstimatedAwardBrush { get => _estimatedAwardBrush; private set => SetProperty(ref _estimatedAwardBrush, value); }
    public Brush EstimatedAwardRibbonBrush { get => _estimatedAwardRibbonBrush; private set => SetProperty(ref _estimatedAwardRibbonBrush, value); }
    public Brush EstimatedAwardForegroundBrush { get => _estimatedAwardForegroundBrush; private set => SetProperty(ref _estimatedAwardForegroundBrush, value); }
    public string MedalLineSourceText { get => _medalLineSourceText; set => SetProperty(ref _medalLineSourceText, value); }
    public string MedalLineSourceDetail { get => _medalLineSourceDetail; set => SetProperty(ref _medalLineSourceDetail, value); }
    public string OfficialPlaceholderTitle { get => _officialPlaceholderTitle; set => SetProperty(ref _officialPlaceholderTitle, value); }
    public string OfficialPlaceholderMessage { get => _officialPlaceholderMessage; set => SetProperty(ref _officialPlaceholderMessage, value); }
    public bool HasEstimate { get => _hasEstimate; set => SetProperty(ref _hasEstimate, value); }
    public bool HasMedalLines { get => _hasMedalLines; set => SetProperty(ref _hasMedalLines, value); }
    public bool HasOfficialStandings
    {
        get => _hasOfficialStandings;
        set
        {
            if (!SetProperty(ref _hasOfficialStandings, value)) return;
            NotifyViewStates();
        }
    }
    public bool HasSelectedContest => Selected != null;
    public string? SelectedRanklandUrl => Selected?.RanklandUrl;
    public bool HasSelectedRanklandUrl => !string.IsNullOrWhiteSpace(SelectedRanklandUrl);
    public bool ShowDetailEmpty => !HasSelectedContest && !IsDetailLoading;
    public bool ShowDetailContent => HasSelectedContest && !IsDetailLoading;
    public bool ShowOfficialPlaceholder => !IsDetailLoading && !HasOfficialStandings;
    public bool ShowOfficialStandings => !IsDetailLoading && HasOfficialStandings;
    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        set
        {
            if (!SetProperty(ref _isDetailLoading, value)) return;
            NotifyViewStates();
        }
    }

    public GymContestItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelectedContest));
            OnPropertyChanged(nameof(SelectedRanklandUrl));
            OnPropertyChanged(nameof(HasSelectedRanklandUrl));
            NotifyViewStates();
            _ = LoadDetailAsync(value);
        }
    }

    public event Action? OfficialStandingsLoaded;

    public void SetProblemSortMode(ProblemSortMode sortMode)
    {
        if (_problemSortMode == sortMode)
        {
            ApplyProblemSort();
            return;
        }

        _problemSortMode = sortMode;
        OnPropertyChanged(nameof(ProblemSortMode));
        ApplyProblemSort();
    }

    public async Task LoadContestsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        if (string.IsNullOrWhiteSpace(Handle))
        {
            var acct = await db.Accounts.FirstOrDefaultAsync();
            if (acct != null) Handle = acct.Handle;
        }

        var gyms = await db.GymContests.ToListAsync();
        var resultsByGym = (await db.VpResults.ToListAsync())
            .GroupBy(v => (v.Platform, v.GymId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var selections = await db.GymVpSelections
            .ToDictionaryAsync(s => (s.Platform, s.GymId), s => s.Handle);
        var confirmedRows = await db.ContestMappings
            .Where(m => m.Confirmed == MappingStatus.ManualConfirmed ||
                        m.Confirmed == MappingStatus.AutoConfirmed)
            .ToListAsync();
        var confirmed = confirmedRows
            .GroupBy(m => (m.Platform, m.GymId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.Confirmed == MappingStatus.ManualConfirmed ? 1 : 0)
                    .ThenByDescending(m => m.Confidence)
                    .First());
        var officialNames = await db.OfficialContests
            .ToDictionaryAsync(o => o.Id, o => ContestDisplayNameBuilder.BuildDisplayName(o.Id, o.Name));
        var curatedNames = CuratedContestMap.All
            .GroupBy(entry => entry.OfficialId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ContestDisplayNameBuilder.BuildDisplayName(group.Key, group.First().DisplayName),
                StringComparer.Ordinal);

        _allContests.Clear();
        foreach (var g in gyms.OrderByDescending(g => g.StartTime ?? 0))
        {
            var key = (g.Platform, g.GymId);
            resultsByGym.TryGetValue(key, out var contestResults);
            selections.TryGetValue(key, out var selectedHandle);
            var basis = PickDisplayResult(contestResults ?? new List<VpResult>(), selectedHandle);

            confirmed.TryGetValue(key, out var map);
            string? officialName = map == null
                ? null
                : officialNames.TryGetValue(map.OfficialId, out var syncedName)
                    ? syncedName
                    : curatedNames.TryGetValue(map.OfficialId, out var curatedName)
                        ? curatedName
                        : map.OfficialId;
            _allContests.Add(new GymContestItem
            {
                Platform = g.Platform,
                GymId = g.GymId,
                Handle = basis?.Handle ?? string.Empty,
                Title = g.Title ?? (g.Platform == Platform.Qoj ? $"QOJ {g.GymId}" : $"Gym {g.GymId}"),
                StartTime = g.StartTime,
                SolvedCount = basis?.SolvedCount ?? 0,
                ProblemCount = g.ProblemCount,
                StartTimeText = FormatTime(g.StartTime),
                OfficialId = map?.OfficialId,
                MappedOfficialName = officialName,
                HasResult = basis != null,
                HasVirtualRun = basis?.Kind == ParticipationKind.Virtual,
            });
        }
        IsEmpty = _allContests.Count == 0;
        ApplyContestFilter();
    }

    public async Task SelectContestAsync(Platform platform, long gymId, string? handle = null)
    {
        if (_allContests.Count == 0)
            await LoadContestsAsync();

        var item = _allContests.FirstOrDefault(c => c.Platform == platform && c.GymId == gymId);
        if (item != null)
        {
            if (!string.IsNullOrWhiteSpace(handle) && item.Handle != handle)
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var result = PickDisplayResult(await db.VpResults.AsNoTracking()
                    .Where(v => v.Platform == platform && v.GymId == gymId && v.Handle == handle).ToListAsync(), handle);
                if (result != null)
                {
                    var replacement = item with { Handle = handle, SolvedCount = result.SolvedCount, HasVirtualRun = result.Kind == ParticipationKind.Virtual };
                    _allContests[_allContests.IndexOf(item)] = replacement;
                    if (Contests.Contains(item)) Contests[Contests.IndexOf(item)] = replacement;
                    item = replacement;
                }
            }
            if (!Contests.Contains(item))
                ContestSearchText = string.Empty;
            Selected = item;
        }
    }

    private void ApplyContestFilter()
    {
        var query = ContestSearchText.Trim();
        var matchingContests = string.IsNullOrWhiteSpace(query)
            ? _allContests
            : _allContests.Where(contest => MatchesContestSearch(contest, query)).ToList();

        Contests.Clear();
        foreach (var contest in matchingContests)
            Contests.Add(contest);

        OnPropertyChanged(nameof(HasContestSearch));
        OnPropertyChanged(nameof(ShowContestSearchEmpty));
        OnPropertyChanged(nameof(ContestSearchSummary));
    }

    private static bool MatchesContestSearch(GymContestItem contest, string query)
    {
        var tokens = query.Split(
            new[] { ' ', '\t', '\r', '\n', ',', '，', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.All(token => MatchesContestSearchToken(contest, token));
    }

    private static bool MatchesContestSearchToken(GymContestItem contest, string token)
    {
        var normalizedToken = token.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedToken)) return true;

        if (normalizedToken is "cf" or "codeforces")
            return contest.Platform == Platform.Codeforces;
        if (normalizedToken is "qoj")
            return contest.Platform == Platform.Qoj;
        if (normalizedToken is "已绑定" or "绑定" or "mapped")
            return contest.IsMapped;
        if (normalizedToken is "未绑定" or "unmapped")
            return !contest.IsMapped;
        if (normalizedToken is "vp" or "virtual" or "虚拟")
            return contest.HasVirtualRun;
        if (normalizedToken is "练习" or "practice")
            return contest.HasResult && !contest.HasVirtualRun;

        var sourceText = contest.Platform == Platform.Qoj
            ? $"QOJ {contest.GymId}"
            : $"Codeforces CF Gym {contest.GymId}";
        var corpus = string.Join(' ', new[]
        {
            contest.DisplayTitle,
            contest.Title,
            contest.MappedOfficialName ?? string.Empty,
            sourceText,
            contest.Handle,
            contest.StartTimeText,
            contest.SolvedText,
            $"{contest.SolvedCount}/{contest.ProblemCount}",
        });

        return ContainsSearchText(corpus, normalizedToken);
    }

    private static bool ContainsSearchText(string corpus, string token)
    {
        if (corpus.Contains(token, StringComparison.OrdinalIgnoreCase))
            return true;

        var compactCorpus = string.Concat(corpus.Where(character => !char.IsWhiteSpace(character)));
        var compactToken = string.Concat(token.Where(character => !char.IsWhiteSpace(character)));
        return compactCorpus.Contains(compactToken, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadDetailAsync(GymContestItem? item)
    {
        var version = ++_detailLoadVersion;
        IsDetailLoading = item != null;
        Problems.Clear();
        OfficialStandings.Clear();
        HasEstimate = false;
        HasMedalLines = false;
        HasOfficialStandings = false;
        EstimateRankText = string.Empty;
        EstimateTotalText = string.Empty;
        GoldMedalLineText = string.Empty;
        SilverMedalLineText = string.Empty;
        BronzeMedalLineText = string.Empty;
        ResetEstimatedAward();
        MedalLineSourceText = string.Empty;
        MedalLineSourceDetail = string.Empty;
        OfficialPlaceholderTitle = "暂无官方榜单";
        OfficialPlaceholderMessage = "请选择一场比赛。";

        if (item == null)
        {
            DetailTitle = "请选择一场比赛";
            VpSummary = string.Empty;
            IsDetailLoading = false;
            return;
        }

        try
        {
            DetailTitle = item.DisplayTitle;
            await using var db = await _dbFactory.CreateDbContextAsync();

        var submissions = string.IsNullOrWhiteSpace(item.Handle)
            ? new List<Submission>()
            : await db.Submissions
                .Where(s => s.Platform == item.Platform && s.Handle == item.Handle && s.GymId == item.GymId)
                .ToListAsync();
        if (version != _detailLoadVersion) return;

        VpResult? vp = null;
        VpResult? practice = null;
        if (!string.IsNullOrWhiteSpace(item.Handle))
        {
            vp = await db.VpResults.FindAsync(item.Platform, item.Handle, item.GymId, ParticipationKind.Virtual);
            practice = await db.VpResults.FindAsync(item.Platform, item.Handle, item.GymId, ParticipationKind.Practice);
        }
        if (version != _detailLoadVersion) return;
        if (vp != null && RefreshVirtualPenalty(vp, submissions))
            await db.SaveChangesAsync();

        var mapping = new MappingService(db);
        string? officialId = null;
        string? officialName = null;
        long? officialStartAt = null;
        long? officialDurationSeconds = null;
        var officialProblems = new List<OfficialProblem>();
        var officialRows = new List<OfficialStanding>();
        officialId = await mapping.GetConfirmedOfficialAsync(item.Platform, item.GymId);
        if (version != _detailLoadVersion) return;
        if (officialId != null)
        {
            var officialRow = await db.OfficialContests
                .Where(o => o.Id == officialId)
                .Select(o => new { o.Id, o.Name, o.StartAt, o.DurationSeconds })
                .FirstOrDefaultAsync();
            if (version != _detailLoadVersion) return;
            officialName = officialRow != null
                ? ContestDisplayNameBuilder.BuildDisplayName(officialRow.Id, officialRow.Name)
                : officialId;
            officialStartAt = officialRow?.StartAt;
            officialDurationSeconds = officialRow?.DurationSeconds;

            if (await EnsureOfficialStandingAsync(db, officialId))
            {
                if (version != _detailLoadVersion) return;
                if (officialRow == null)
                {
                    var refreshedOfficial = await db.OfficialContests
                        .Where(o => o.Id == officialId)
                        .Select(o => new { o.StartAt, o.DurationSeconds })
                        .FirstOrDefaultAsync();
                    if (version != _detailLoadVersion) return;
                    officialStartAt = refreshedOfficial?.StartAt;
                    officialDurationSeconds = refreshedOfficial?.DurationSeconds;
                }
                officialProblems = await db.OfficialProblems
                    .Where(p => p.OfficialId == officialId)
                    .OrderBy(p => p.SortOrder)
                    .ToListAsync();
                if (version != _detailLoadVersion) return;
                officialRows = await db.OfficialStandings
                    .Where(s => s.OfficialId == officialId)
                    .OrderBy(s => s.Rank)
                    .ToListAsync();
                if (version != _detailLoadVersion) return;
            }
        }

        var contestDuration = officialDurationSeconds ?? DefaultContestDurationSeconds;
        var inferredVp = vp == null && item.Platform == Platform.Codeforces
            ? BuildInferredVirtualResult(item, submissions, contestDuration)
            : null;
        var scoringResult = vp ?? inferredVp ?? practice;

        if (scoringResult == null && practice == null)
        {
            VpSummary = "本地没有可用的 VP 或练习成绩。";
        }
        else
        {
            var basis = scoringResult!;
            await LoadProblemDetailsAsync(db, item, basis, submissions, officialProblems, officialRows,
                officialStartAt, officialDurationSeconds);

            var vpBasis = vp ?? inferredVp;
            var vpLine = vpBasis != null
                ? $"VP（{item.Handle}）：{vpBasis.SolvedCount} 题 · 总罚时 {FormatPenalty(vpBasis.Penalty)}"
                : $"VP（{item.Handle}）：无正式虚拟参加记录";
            var practiceLine = practice != null
                ? $"补题（{item.Handle}）：{practice.SolvedCount} 题"
                : null;
            VpSummary = practiceLine != null ? $"{vpLine}\n{practiceLine}" : vpLine;
        }

        if (officialId == null)
        {
            OfficialPlaceholderTitle = "未关联官方榜";
            OfficialPlaceholderMessage = "在「官方榜单与关联」页面绑定后，此处将显示完整排名。";
            return;
        }

        if (officialRows.Count == 0)
        {
            OfficialPlaceholderTitle = "榜单尚未加载";
            OfficialPlaceholderMessage = $"已绑定 {officialName}，但未能获取完整官方榜单。请稍后重试或在「官方榜单与关联」页同步全部。";
            return;
        }

        VpEstimate? estimate = null;
        int[]? medalCounts = null;
        bool hasOfficialMedalLine = false;
        if (scoringResult != null && !string.IsNullOrWhiteSpace(item.Handle))
        {
            medalCounts = await LoadOfficialMedalCountsAsync(db, officialId);
            hasOfficialMedalLine = MedalRuleProvider.FromOfficialCounts(medalCounts) != null;
            if (version != _detailLoadVersion) return;
            var estimateOfficialName = string.IsNullOrWhiteSpace(officialName) ? officialId : officialName;
            estimate = mapping.EstimateFromStandings(
                scoringResult.SolvedCount,
                scoringResult.Penalty,
                officialRows,
                medalCounts) with { OfficialId = officialId, OfficialName = estimateOfficialName };
        }

        if (estimate != null)
        {
            HasEstimate = true;
            EstimateRankText = $"#{estimate.Rank}";
            EstimateTotalText = $"/ {estimate.TotalTeams} 支参赛队";
            HasMedalLines = estimate.MedalLine.Bronze > 0;
            GoldMedalLineText = FormatMedalLineText("金", estimate.MedalLine.Gold);
            SilverMedalLineText = FormatMedalLineText("银", estimate.MedalLine.Silver);
            BronzeMedalLineText = FormatMedalLineText("铜", estimate.MedalLine.Bronze);
            MedalLineSourceText = hasOfficialMedalLine ? "官方奖牌线" : "推算奖牌线";
            MedalLineSourceDetail = hasOfficialMedalLine
                ? "奖牌名额来自官方榜单配置"
                : "按正式参赛且有有效提交的队伍数，以 10% / 30% / 60% 规则推算";
            SetEstimatedAward(estimate);
        }

            LoadOfficialStandings(officialRows, estimate, scoringResult, item.Handle);
        }
        finally
        {
            if (version == _detailLoadVersion)
                IsDetailLoading = false;
        }
    }

    private void LoadOfficialStandings(IReadOnlyList<OfficialStanding> rows,
        VpEstimate? estimate, VpResult? result, string handle)
    {
        HasOfficialStandings = true;
        bool shouldInsertVp = estimate != null && result != null;
        bool vpInserted = false;
        var resultScore = result == null ? null : new ScoreEntry(result.SolvedCount, result.Penalty);
        var displayRows = new List<OfficialStandingItem>();
        var rankedRows = RankingEngine.RankPersistedStandings(rows);

        foreach (var rankedRow in rankedRows)
        {
            var row = rankedRow.Standing;
            var rowScore = new ScoreEntry(row.SolvedCount, row.Penalty);
            // Insert at the first row that is not strictly ahead, so tied ranks stay visible.
            if (shouldInsertVp && !vpInserted &&
                RankingEngine.Compare(rowScore, resultScore!) >= 0)
            {
                displayRows.Add(BuildVpStanding(estimate!, result!, handle));
                vpInserted = true;
            }
            displayRows.Add(new OfficialStandingItem
            {
                Rank = rankedRow.Rank,
                TeamName = row.TeamName,
                Organization = row.Organization,
                SolvedCount = row.SolvedCount,
                Penalty = row.Penalty,
                IsVpRow = false,
            });
        }
        if (shouldInsertVp && !vpInserted)
            displayRows.Add(BuildVpStanding(estimate!, result!, handle));

        if (estimate != null)
        {
            MarkMedalBoundary(displayRows, estimate.MedalLine.Gold, "金牌线", static row => row.ShowGoldBoundary = true,
                static (row, text) => row.GoldBoundaryText = text);
            MarkMedalBoundary(displayRows, estimate.MedalLine.Silver, "银牌线", static row => row.ShowSilverBoundary = true,
                static (row, text) => row.SilverBoundaryText = text);
            MarkMedalBoundary(displayRows, estimate.MedalLine.Bronze, "铜牌线", static row => row.ShowBronzeBoundary = true,
                static (row, text) => row.BronzeBoundaryText = text);
        }

        foreach (var row in displayRows)
            OfficialStandings.Add(row);
        OfficialStandingsLoaded?.Invoke();
    }

    private void ResetEstimatedAward()
    {
        EstimatedAwardTitle = string.Empty;
        EstimatedAwardSymbol = string.Empty;
        EstimatedAwardToolTip = string.Empty;
        EstimatedAwardBrush = AwardNeutralBrush;
        EstimatedAwardRibbonBrush = AwardNeutralRibbonBrush;
        EstimatedAwardForegroundBrush = AwardLightTextBrush;
    }

    private void SetEstimatedAward(VpEstimate estimate)
    {
        string title;
        string symbol;
        Brush medalBrush;
        Brush ribbonBrush;
        Brush foregroundBrush;

        if (estimate.Rank == 1)
        {
            title = "冠军";
            symbol = "冠";
            medalBrush = AwardGoldBrush;
            ribbonBrush = AwardGoldRibbonBrush;
            foregroundBrush = AwardDarkTextBrush;
        }
        else if (estimate.Rank == 2)
        {
            title = "亚军";
            symbol = "亚";
            medalBrush = AwardSilverBrush;
            ribbonBrush = AwardSilverRibbonBrush;
            foregroundBrush = AwardDarkTextBrush;
        }
        else if (estimate.Rank == 3)
        {
            title = "季军";
            symbol = "季";
            medalBrush = AwardBronzeBrush;
            ribbonBrush = AwardBronzeRibbonBrush;
            foregroundBrush = AwardLightTextBrush;
        }
        else
        {
            (title, symbol, medalBrush, ribbonBrush, foregroundBrush) = estimate.Medal switch
            {
                Medal.Gold => ("金牌", "金", AwardGoldBrush, AwardGoldRibbonBrush, AwardDarkTextBrush),
                Medal.Silver => ("银牌", "银", AwardSilverBrush, AwardSilverRibbonBrush, AwardDarkTextBrush),
                Medal.Bronze => ("铜牌", "铜", AwardBronzeBrush, AwardBronzeRibbonBrush, AwardLightTextBrush),
                _ => ("未获奖", "—", AwardNeutralBrush, AwardNeutralRibbonBrush, AwardLightTextBrush),
            };
        }

        EstimatedAwardTitle = title;
        EstimatedAwardSymbol = symbol;
        EstimatedAwardBrush = medalBrush;
        EstimatedAwardRibbonBrush = ribbonBrush;
        EstimatedAwardForegroundBrush = foregroundBrush;
        EstimatedAwardToolTip = estimate.Medal == Medal.None
            ? $"推算名次 #{estimate.Rank}，未进入铜牌线（截至第 {estimate.MedalLine.Bronze} 名）。"
            : $"推算名次 #{estimate.Rank}，获得{title}。{MedalLineSourceDetail}";
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
        => new SolidColorBrush(Color.FromArgb(0xFF, red, green, blue));

   private static string FormatMedalLineText(string medal, int cutoff)
        => cutoff > 0 ? $"{medal}牌线 #{cutoff}" : string.Empty;

    private static void MarkMedalBoundary(
        IReadOnlyList<OfficialStandingItem> rows,
        int cutoff,
        string label,
        Action<OfficialStandingItem> showBoundary,
        Action<OfficialStandingItem, string> setText)
    {
        if (cutoff <= 0) return;
        var target = rows.LastOrDefault(row => row.Rank > 0 && row.Rank <= cutoff);
        if (target == null) return;
        showBoundary(target);
        setText(target, $"{label} · 第 {cutoff} 名");
    }

    private async Task<bool> EnsureOfficialStandingAsync(VpDbContext db, string officialId)
    {
        var hasStandings = await db.OfficialStandings.AnyAsync(s => s.OfficialId == officialId);
        var hasProblems = await db.OfficialProblems.AnyAsync(p => p.OfficialId == officialId);
        if (hasStandings && hasProblems)
            return true;

        var sync = new RanklandSyncService(db, _ranklandClient);
        if (!await sync.SyncOneAsync(officialId))
            return hasStandings;

        return await db.OfficialStandings.AnyAsync(s => s.OfficialId == officialId);
    }

    private static async Task<int[]?> LoadOfficialMedalCountsAsync(VpDbContext db, string officialId)
    {
        var json = await db.OfficialContests
            .Where(o => o.Id == officialId)
            .Select(o => o.MedalRules)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(json)) return null;

        try { return JsonSerializer.Deserialize<int[]>(json); }
        catch { return null; }
    }

    private static OfficialStandingItem BuildVpStanding(VpEstimate estimate, VpResult vp, string handle)
        => new()
        {
            Rank = estimate.Rank,
            TeamName = vp.Kind == ParticipationKind.Virtual ? $"{handle}（VP）" : $"{handle}（练习）",
            Organization = null,
            SolvedCount = vp.SolvedCount,
            Penalty = vp.Penalty,
            IsVpRow = true,
        };

    private static VpResult? BuildInferredVirtualResult(
        GymContestItem item,
        IReadOnlyList<Submission> submissions,
        long contestDurationSeconds)
    {
        var run = SubmissionRunBuilder.BuildForCodeforces(submissions, contestDurationSeconds)
            .FirstOrDefault(r => r.Kind == ParticipationKind.Virtual);
        if (run == null || run.Submissions.Count == 0)
            return null;

        var aggregator = new Aggregator();
        var aggregate = aggregator.Aggregate(item.Handle, item.GymId, run.Submissions);
        if (aggregate.SolvedCount == 0)
            return null;

        return new VpResult
        {
            Platform = item.Platform,
            Handle = item.Handle,
            GymId = item.GymId,
            Kind = ParticipationKind.Virtual,
            SolvedCount = aggregate.SolvedCount,
            Penalty = aggregator.PenaltyFromRelativeTime(aggregate, run.Submissions),
            FirstAcPerProblem = JsonSerializer.Serialize(aggregate.FirstAcPerProblem),
        };
    }

    private async Task LoadProblemDetailsAsync(VpDbContext db, GymContestItem item,
        VpResult basis,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<OfficialProblem> officialProblems,
        IReadOnlyList<OfficialStanding> officialRows,
        long? officialStartAt,
        long? officialDurationSeconds)
    {
        var contestDuration = officialDurationSeconds ?? 5 * 3600;
        var scoreSubmissions = basis.Kind == ParticipationKind.Virtual
            ? ResolveScoringSubmissions(item.Platform, basis.Kind, submissions, officialStartAt, contestDuration)
            : new List<Submission>();
        var practiceSubmissions = ResolvePracticeSubmissions(item.Platform, submissions, officialStartAt, contestDuration);
        var firstAcByProblem = scoreSubmissions
            .Where(s => IsAccepted(s.Verdict))
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .GroupBy(s => NormalizeProblemIndex(s.ProblemIndex))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var practiceFirstAcByProblem = practiceSubmissions
            .Where(s => IsAccepted(s.Verdict))
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .GroupBy(s => NormalizeProblemIndex(s.ProblemIndex))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var urlByProblem = submissions
            .GroupBy(s => NormalizeProblemIndex(s.ProblemIndex))
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => s.ProblemUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? string.Empty,
                StringComparer.Ordinal);
        var qojDefinitionsById = item.Platform == Platform.Qoj
            ? await db.QojContestProblems
                .Where(p => p.ContestId == item.GymId)
                .ToDictionaryAsync(p => p.ProblemId, p => p)
            : new Dictionary<string, QojContestProblem>(StringComparer.Ordinal);
        var fallbackFirstAc = SafeDeserialize(basis.FirstAcPerProblem);

        var wrongBeforeAc = CountWrongAttemptsBeforeAccepted(scoreSubmissions);
        var officialByAlias = officialProblems
            .GroupBy(p => NormalizeProblemIndex(p.Alias))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var officialStatsByAlias = BuildOfficialProblemStats(officialRows, officialProblems);

        var indices = officialProblems
            .OrderBy(p => p.SortOrder)
            .Select(p => NormalizeProblemIndex(p.Alias))
            .Where(i => i.Length > 0)
            .Concat(ExtractOfficialProblemAliases(officialRows))
            .Concat(submissions
            .Select(s => NormalizeProblemIndex(s.ProblemIndex))
            .Where(i => i.Length > 0))
            .Concat(fallbackFirstAc.Keys)
            .Concat(practiceFirstAcByProblem.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (indices.Count == 0 && item.ProblemCount > 0)
            indices = Enumerable.Range(0, item.ProblemCount)
                .Select(i => ((char)('A' + i)).ToString())
                .ToList();

        var orderedIndices = indices
            .OrderBy(i => officialByAlias.TryGetValue(i, out var problem) ? problem.SortOrder : int.MaxValue)
            .ThenBy(i => i, StringComparer.Ordinal)
            .ToList();

        foreach (var idx in orderedIndices)
        {
            bool hasSubmission = firstAcByProblem.TryGetValue(idx, out var acSubmission);
            bool hasPracticeAc = practiceFirstAcByProblem.TryGetValue(idx, out var practiceAcSubmission);
            bool solvedInContest = hasSubmission ||
                                   (basis.Kind == ParticipationKind.Virtual && fallbackFirstAc.ContainsKey(idx));
            bool solved = solvedInContest || hasPracticeAc;
            long? relativeAcTime = hasSubmission
                ? ResolveRelativeTime(acSubmission!, officialStartAt, contestDuration)
                : null;
            var firstAcText = hasSubmission
                ? relativeAcTime is long rel ? FormatContestElapsed(rel) : FormatTime(acSubmission!.SubmitTime)
                : hasPracticeAc
                    ? FormatTime(practiceAcSubmission!.SubmitTime)
                : "—";
            var wrongAttempts = wrongBeforeAc.GetValueOrDefault(idx);
            var penaltyText = solved && relativeAcTime is long solvedAt
                ? FormatProblemPenalty(solvedAt, wrongAttempts)
                : "—";
            var statusText = ProblemSubmissionStatusFormatter.Format(
                scoreSubmissions,
                practiceSubmissions,
                idx,
                contestSolvedFallback: solvedInContest && !hasSubmission);
            officialByAlias.TryGetValue(idx, out var officialProblem);
            var hasOfficialStats = officialStatsByAlias.TryGetValue(idx, out var officialStats);
            Problems.Add(new ProblemDetailItem
            {
                Index = idx,
                Title = officialProblem?.Title,
                Solved = solved,
                StatusText = statusText,
                PenaltyText = penaltyText,
                FirstAcText = firstAcText,
                OfficialAcceptedCount = hasOfficialStats && officialStats.Accepted >= 0
                    ? officialStats.Accepted
                    : officialProblem?.AcceptedCount,
                OfficialSubmittedCount = hasOfficialStats && officialStats.Submitted >= 0
                    ? officialStats.Submitted
                    : officialProblem?.SubmittedCount,
                ContestSortOrder = officialProblem?.SortOrder ?? int.MaxValue,
                ProblemUrl = ResolveProblemUrl(item.Platform, item.GymId, idx, acSubmission, urlByProblem,
                    qojDefinitionsById),
            });
        }

        ApplyProblemSort();
    }

    private void ApplyProblemSort()
    {
        var ordered = _problemSortMode == ProblemSortMode.Difficulty
            ? Problems
                .OrderByDescending(problem => problem.OfficialAcceptedCount ?? -1)
                .ThenBy(problem => problem.OfficialSubmittedCount ?? int.MaxValue)
                .ThenBy(problem => problem.ContestSortOrder)
                .ThenBy(problem => problem.Index, StringComparer.Ordinal)
            : Problems
                .OrderBy(problem => problem.ContestSortOrder)
                .ThenBy(problem => problem.Index, StringComparer.Ordinal);
        var snapshot = ordered.ToList();
        if (snapshot.SequenceEqual(Problems))
            return;

        Problems.Clear();
        foreach (var problem in snapshot)
            Problems.Add(problem);
    }

    private static bool RefreshVirtualPenalty(VpResult result, IReadOnlyList<Submission> submissions)
    {
        if (result.Kind != ParticipationKind.Virtual || submissions.Count == 0)
            return false;

        var kindSubmissions = submissions
            .Where(s => ParticipationKindClassifier.Classify(s.ParticipantType) == ParticipationKind.Virtual)
            .ToList();
        if (kindSubmissions.Count == 0 || kindSubmissions.All(s => s.RelativeTime == null))
            return false;

        var aggregate = new Aggregator().Aggregate(result.Handle, result.GymId, kindSubmissions);
        var penalty = new Aggregator().PenaltyFromRelativeTime(aggregate, kindSubmissions);
        if (penalty == result.Penalty) return false;

        result.SolvedCount = aggregate.SolvedCount;
        result.Penalty = penalty;
        result.FirstAcPerProblem = JsonSerializer.Serialize(aggregate.FirstAcPerProblem);
        return true;
    }

    private sealed class StoredProblemStatus
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }
        [JsonPropertyName("timeSeconds")]
        public long? TimeSeconds { get; set; }
        [JsonPropertyName("tries")]
        public int Tries { get; set; }
    }

    private readonly record struct OfficialProblemStats(int Accepted, int Submitted);

    private static List<Submission> ResolveScoringSubmissions(
        Platform platform,
        ParticipationKind resultKind,
        IReadOnlyList<Submission> submissions,
        long? officialStartAt,
        long contestDurationSeconds)
    {
        if (platform == Platform.Codeforces && resultKind == ParticipationKind.Virtual)
        {
            return SubmissionRunBuilder.BuildForCodeforces(submissions, contestDurationSeconds)
                .FirstOrDefault(r => r.Kind == ParticipationKind.Virtual)
                ?.Submissions
                .ToList()
                ?? new List<Submission>();
        }

        return submissions
            .Where(s =>
            {
                var kind = ParticipationKindClassifier.Classify(s.ParticipantType);
                if (kind != resultKind)
                    return false;
                if (resultKind == ParticipationKind.Virtual)
                    return ResolveRelativeTime(s, officialStartAt, contestDurationSeconds) != null;
                return true;
            })
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();
    }

    private static List<Submission> ResolvePracticeSubmissions(
        Platform platform,
        IReadOnlyList<Submission> submissions,
        long? officialStartAt,
        long contestDurationSeconds)
    {
        if (platform == Platform.Codeforces)
        {
            var virtualRun = SubmissionRunBuilder.BuildForCodeforces(submissions, contestDurationSeconds)
                .FirstOrDefault(r => r.Kind == ParticipationKind.Virtual);
            var virtualIds = virtualRun?.Submissions.Select(s => s.Id).ToHashSet() ?? new HashSet<long>();
            return submissions
                .Where(s => !virtualIds.Contains(s.Id) &&
                            ParticipationKindClassifier.Classify(s.ParticipantType) != ParticipationKind.Virtual)
                .OrderBy(s => s.SubmitTime)
                .ThenBy(s => s.Id)
                .ToList();
        }

        return submissions
            .Where(s => ParticipationKindClassifier.Classify(s.ParticipantType) == ParticipationKind.Practice)
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();
    }

    private static long? ResolveRelativeTime(Submission submission, long? officialStartAt, long contestDurationSeconds)
    {
        if (submission.RelativeTime is long relativeTime)
            return IsWithinContest(relativeTime, contestDurationSeconds) ? relativeTime : null;

        if (officialStartAt is not long start)
            return null;

        var elapsed = submission.SubmitTime - start;
        return IsWithinContest(elapsed, contestDurationSeconds) ? elapsed : null;
    }

    private static bool IsWithinContest(long elapsedSeconds, long contestDurationSeconds)
        => elapsedSeconds >= 0 && elapsedSeconds < Math.Max(60, contestDurationSeconds);

    private static IReadOnlyDictionary<string, int> CountWrongAttemptsBeforeAccepted(IReadOnlyList<Submission> submissions)
    {
        var wrong = new Dictionary<string, int>(StringComparer.Ordinal);
        var solved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var submission in submissions.OrderBy(s => s.SubmitTime).ThenBy(s => s.Id))
        {
            var idx = NormalizeProblemIndex(submission.ProblemIndex);
            if (solved.Contains(idx)) continue;

            if (IsAccepted(submission.Verdict))
            {
                solved.Add(idx);
            }
            else if (IsPenalized(submission.Verdict))
            {
                wrong[idx] = wrong.GetValueOrDefault(idx) + 1;
            }
        }

        return wrong;
    }

    private static IReadOnlyDictionary<string, OfficialProblemStats> BuildOfficialProblemStats(
        IReadOnlyList<OfficialStanding> rows,
        IReadOnlyList<OfficialProblem> officialProblems)
    {
        var accepted = new Dictionary<string, int>(StringComparer.Ordinal);
        var attemptedTeams = new Dictionary<string, int>(StringComparer.Ordinal);
        var submittedAttempts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Official))
        {
            foreach (var (alias, status) in ParsePerProblem(row.PerProblem))
            {
                if (!IsBlankOfficialStatus(status))
                {
                    attemptedTeams[alias] = attemptedTeams.GetValueOrDefault(alias) + 1;
                    submittedAttempts[alias] = submittedAttempts.GetValueOrDefault(alias) + OfficialSubmissionCount(status);
                }
                if (IsSolvedOfficialStatus(status.Result))
                    accepted[alias] = accepted.GetValueOrDefault(alias) + 1;
            }
        }

        var problemStats = officialProblems
            .GroupBy(p => NormalizeProblemIndex(p.Alias))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var problem in officialProblems)
        {
            var alias = NormalizeProblemIndex(problem.Alias);
            if (!accepted.ContainsKey(alias) && (problem.AcceptedCount > 0 || problem.SubmittedCount > 0))
                accepted[alias] = problem.AcceptedCount;
        }

        return accepted.Keys
            .Concat(problemStats.Keys)
            .Concat(submittedAttempts.Keys)
            .Concat(attemptedTeams.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                alias => alias,
                alias =>
                {
                    var hasProblemStats = problemStats.TryGetValue(alias, out var problem)
                                       && (problem.SubmittedCount > 0 || problem.AcceptedCount > 0);
                    var submitted = hasProblemStats
                        ? problem!.SubmittedCount
                        : submittedAttempts.TryGetValue(alias, out var attempts) && attempts > 0
                            ? attempts
                            : attemptedTeams.GetValueOrDefault(alias, -1);
                    return new OfficialProblemStats(
                        accepted.GetValueOrDefault(alias, submitted >= 0 ? 0 : -1),
                        submitted);
                },
                StringComparer.Ordinal);
    }

    private static int OfficialSubmissionCount(StoredProblemStatus status)
    {
        if (status.Tries > 0)
            return status.Tries;

        return IsBlankOfficialStatus(status) ? 0 : 1;
    }

    private static IEnumerable<string> ExtractOfficialProblemAliases(IReadOnlyList<OfficialStanding> rows)
        => rows.SelectMany(r => ParsePerProblem(r.PerProblem).Keys)
            .Distinct(StringComparer.Ordinal);

    private static Dictionary<string, StoredProblemStatus> ParsePerProblem(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new(StringComparer.Ordinal);

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, StoredProblemStatus>>(json);
            return map?
                .ToDictionary(kv => NormalizeProblemIndex(kv.Key), kv => kv.Value, StringComparer.Ordinal)
                ?? new(StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }

    private static bool IsSolvedOfficialStatus(string? result)
        => result != null &&
           (result.Equals("AC", StringComparison.OrdinalIgnoreCase) ||
            result.Equals("FB", StringComparison.OrdinalIgnoreCase));

    private static bool IsBlankOfficialStatus(StoredProblemStatus status)
        => string.IsNullOrWhiteSpace(status.Result) &&
           status.TimeSeconds == null &&
           status.Tries == 0;

    private static string FormatContestElapsed(long seconds)
    {
        var capped = Math.Clamp(seconds, 0, 5 * 3600 - 1);
        var ts = TimeSpan.FromSeconds(capped);
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}";
    }

    private static string FormatProblemPenalty(long relativeSeconds, int wrongAttempts)
    {
        var penaltyMinutes = Math.Max(0, relativeSeconds) / 60 + wrongAttempts * 20;
        return $"{penaltyMinutes} min";
    }

    private static string BuildProblemUrl(Platform platform, long gymId, string index)
    {
        var escaped = Uri.EscapeDataString(index);
        return platform switch
        {
            Platform.Codeforces => $"https://codeforces.com/gym/{gymId}/problem/{escaped}",
            Platform.Qoj => $"https://qoj.ac/contest/{gymId}/problem/{escaped}",
            _ => string.Empty,
        };
    }

    private static string ResolveProblemUrl(Platform platform, long gymId, string index,
        Submission? acceptedSubmission, IReadOnlyDictionary<string, string> urlsByProblem,
        IReadOnlyDictionary<string, QojContestProblem>? qojDefinitionsById = null)
    {
        if (!string.IsNullOrWhiteSpace(acceptedSubmission?.ProblemUrl))
            return acceptedSubmission.ProblemUrl!;
        if (urlsByProblem.TryGetValue(index, out var storedUrl) && !string.IsNullOrWhiteSpace(storedUrl))
            return storedUrl;

        if (platform == Platform.Qoj && qojDefinitionsById != null)
        {
            var definition = qojDefinitionsById.Values.FirstOrDefault(p =>
                string.Equals(p.ProblemIndex, index, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(definition?.ProblemUrl))
                return definition.ProblemUrl;
        }

        return BuildProblemUrl(platform, gymId, index);
    }

    private static string NormalizeProblemIndex(string? index)
        => string.IsNullOrWhiteSpace(index) ? "?" : index.Trim();

    private static bool IsAccepted(string? verdict)
        => verdict != null &&
           (verdict.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            verdict.Equals("AC", StringComparison.OrdinalIgnoreCase) ||
            verdict.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase));

    private static bool IsPenalized(string? verdict)
        => !string.IsNullOrWhiteSpace(verdict) &&
           !verdict.Equals("COMPILATION_ERROR", StringComparison.OrdinalIgnoreCase) &&
           !verdict.Equals("CE", StringComparison.OrdinalIgnoreCase) &&
           !verdict.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase) &&
           !verdict.Equals("TESTING", StringComparison.OrdinalIgnoreCase) &&
           !verdict.Equals("?", StringComparison.OrdinalIgnoreCase) &&
           !verdict.Equals("UKE", StringComparison.OrdinalIgnoreCase);

    private void NotifyViewStates()
    {
        OnPropertyChanged(nameof(ShowDetailEmpty));
        OnPropertyChanged(nameof(ShowDetailContent));
        OnPropertyChanged(nameof(ShowOfficialPlaceholder));
        OnPropertyChanged(nameof(ShowOfficialStandings));
    }

    private static VpResult? PickDisplayResult(IReadOnlyList<VpResult> results, string? selectedHandle)
    {
        if (results.Count == 0) return null;

        var virtuals = results
            .Where(v => v.Kind == ParticipationKind.Virtual)
            .OrderByDescending(v => v.SolvedCount)
            .ThenBy(v => v.Penalty)
            .ToList();
        if (virtuals.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(selectedHandle))
                return virtuals.FirstOrDefault(v => v.Handle == selectedHandle) ?? virtuals[0];

            return virtuals[0];
        }

        return results
            .Where(v => v.Kind == ParticipationKind.Practice)
            .OrderByDescending(v => v.SolvedCount)
            .ThenBy(v => v.Penalty)
            .FirstOrDefault();
    }

    private static Dictionary<string, long> SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? new(); }
        catch { return new(); }
    }

    private static string FormatTime(long? unix)
    {
        if (unix is null or 0) return "—";
        return DateTimeOffset.FromUnixTimeSeconds(unix.Value).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatPenalty(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes} min";
    }
}
