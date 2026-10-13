using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Controls;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Rankland;
using VPHelper.Core.Scraping;
using VPHelper.Core.Services;

namespace VPHelper.WinUI.ViewModels;

/// <summary>Login detection state driving the page UI (no manual handle entry).</summary>
public enum LoginState
{
    Initializing,
    CloudflareChallenge,
    NotLoggedIn,
    LoggedIn,
}

/// <summary>A tracked account row for the accounts list.</summary>
public sealed class AccountItem
{
    public Platform Platform { get; init; }
    public string Handle { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public int VpSolved { get; init; }
    public int TotalSubmissions { get; init; }
    public int LastSyncCount { get; init; }
    public string? LastSyncError { get; init; }
    public string LastSyncedText { get; init; } = "从未同步";
    public string PlatformBadge => Platform == Platform.Qoj ? "QOJ" : "CF";
    public bool HasSyncError => !string.IsNullOrEmpty(LastSyncError);
    public string Subtitle => HasSyncError
        ? $"⚠ {LastSyncError}"
        : $"VP 过题 {VpSolved} · 提交 {TotalSubmissions} 条 · {LastSyncedText}";
    public string CurrentBadge => IsCurrent ? "当前登录" : string.Empty;
    public bool ShowCurrentBadge => IsCurrent;
}

public sealed class ConflictCandidateItem
{
    public Platform Platform { get; init; }
    public long GymId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public int SolvedCount { get; init; }
    public long PenaltySeconds { get; init; }
    public bool IsSelected { get; init; }
    public string Label => $"{Handle} — {SolvedCount} 题 / 罚时 {PenaltySeconds / 60} 分";
}

public sealed class ConflictItem
{
    public Platform Platform { get; init; }
    public long GymId { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsUnresolved { get; init; }
    public string StatusText => IsUnresolved ? "待选择" : "已选择";
    public ObservableCollection<ConflictCandidateItem> Candidates { get; } = new();
}

/// <summary>
/// Drives the multi-platform login + sync flow. Each platform (CF / QOJ) has independent login
/// state driven by its own WebView2. The tracked-accounts list and conflict resolution are
/// platform-agnostic (they cover all accounts across all judges).
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly BrowserFetcherHolder _holder;
    private readonly IDbContextFactory<VpDbContext> _dbFactory;

    // --- CF state ---
    private string _cfHandle = string.Empty;
    private LoginState _cfState = LoginState.Initializing;
    private string _cfStatus = "正在检测 Codeforces 登录状态…";

    // --- QOJ state ---
    private string _qojHandle = string.Empty;
    private LoginState _qojState = LoginState.Initializing;
    private string _qojStatus = "正在检测 QOJ 登录状态…";

    // --- shared ---
    private bool _isBusy;
    private double _progress;
    private string _syncStatus = string.Empty;
    private bool _hasConflicts;
    private CancellationTokenSource? _cts;

    private readonly RanklandClient _ranklandClient;

    public LoginViewModel(BrowserFetcherHolder holder, IDbContextFactory<VpDbContext> dbFactory, RanklandClient ranklandClient)
    {
        _ranklandClient = ranklandClient;
        _holder = holder;
        _dbFactory = dbFactory;
    }

    public ObservableCollection<AccountItem> Accounts { get; } = new();
    public ObservableCollection<ConflictItem> Conflicts { get; } = new();

    // ---- CF properties ----
    public string CfHandle
    {
        get => _cfHandle;
        private set
        {
            if (!SetProperty(ref _cfHandle, value)) return;
            OnPropertyChanged(nameof(CfHandleDisplay));
            OnPropertyChanged(nameof(Handle));
            OnPropertyChanged(nameof(HandleDisplay));
            NotifyCfSync();
        }
    }
    public string CfHandleDisplay => string.IsNullOrWhiteSpace(CfHandle) ? "（未能识别 handle）" : CfHandle;
    public LoginState CfState { get => _cfState; private set { if (SetProperty(ref _cfState, value)) NotifyCfSync(); } }
    public string CfStatus
    {
        get => _cfStatus;
        set => SetProperty(ref _cfStatus, value);
    }
    public string CfStateTitle => CfState switch
    {
        LoginState.LoggedIn => "已登录",
        LoginState.CloudflareChallenge => "需要人机验证",
        LoginState.NotLoggedIn => "未登录",
        _ => "正在检测",
    };
    public bool CfShowInitializing => CfState == LoginState.Initializing;
    public bool CfShowChallenge    => CfState == LoginState.CloudflareChallenge;
    public bool CfShowNotLoggedIn  => CfState == LoginState.NotLoggedIn;
    public bool CfShowLoggedIn     => CfState == LoginState.LoggedIn;
    public bool CanSyncCf => CfState == LoginState.LoggedIn && !IsBusy && !string.IsNullOrWhiteSpace(CfHandle);

    // ---- QOJ properties ----
    public string QojHandle
    {
        get => _qojHandle;
        private set
        {
            if (!SetProperty(ref _qojHandle, value)) return;
            OnPropertyChanged(nameof(QojHandleDisplay));
            NotifyQojSync();
        }
    }
    public string QojHandleDisplay => string.IsNullOrWhiteSpace(QojHandle) ? "（未能识别 handle）" : QojHandle;
    public LoginState QojState { get => _qojState; private set { if (SetProperty(ref _qojState, value)) NotifyQojSync(); } }
    public string QojStatus
    {
        get => _qojStatus;
        set => SetProperty(ref _qojStatus, value);
    }
    public string QojStateTitle => QojState switch
    {
        LoginState.LoggedIn => "已登录",
        LoginState.CloudflareChallenge => "需要验证",
        LoginState.NotLoggedIn => "未登录",
        _ => "正在检测",
    };
    public bool QojShowInitializing => QojState == LoginState.Initializing;
    public bool QojShowChallenge    => QojState == LoginState.CloudflareChallenge;
    public bool QojShowNotLoggedIn  => QojState == LoginState.NotLoggedIn;
    public bool QojShowLoggedIn     => QojState == LoginState.LoggedIn;
    public bool CanSyncQoj => QojState == LoginState.LoggedIn && !IsBusy && !string.IsNullOrWhiteSpace(QojHandle);

    // ---- shared properties ----
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) { NotifyCfSync(); NotifyQojSync(); OnPropertyChanged(nameof(SyncTitle)); OnPropertyChanged(nameof(SyncSeverity)); } } }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string SyncStatus
    {
        get => _syncStatus;
        set
        {
            if (!SetProperty(ref _syncStatus, value)) return;
            OnPropertyChanged(nameof(HasSyncStatus));
            OnPropertyChanged(nameof(SyncTitle));
            OnPropertyChanged(nameof(SyncSeverity));
        }
    }
    public bool HasSyncStatus => !string.IsNullOrWhiteSpace(SyncStatus);
    public string SyncTitle
    {
        get
        {
            var text = SyncStatus;
            if (IsBusy || text.Contains("正在", StringComparison.Ordinal) || text.Contains("Page ", StringComparison.Ordinal))
                return "同步进行中";
            if (text.Contains("待补全", StringComparison.Ordinal) || text.Contains("待重试", StringComparison.Ordinal))
                return "提交已保存，部分信息待补全";
            if (text.Contains("失败", StringComparison.Ordinal) || text.Contains("尚未就绪", StringComparison.Ordinal))
                return "同步失败";
            if (text.Contains("验证", StringComparison.Ordinal))
                return "需要验证";
            if (text.Contains("完成", StringComparison.Ordinal))
                return "同步完成";
            return "状态";
        }
    }
    public InfoBarSeverity SyncSeverity
    {
        get
        {
            var text = SyncStatus;
            if (text.Contains("失败", StringComparison.Ordinal) || text.Contains("尚未就绪", StringComparison.Ordinal))
                return InfoBarSeverity.Error;
            if (text.Contains("验证", StringComparison.Ordinal) || text.Contains("取消", StringComparison.Ordinal) ||
                text.Contains("待补全", StringComparison.Ordinal) || text.Contains("待重试", StringComparison.Ordinal))
                return InfoBarSeverity.Warning;
            if (text.Contains("完成", StringComparison.Ordinal))
                return InfoBarSeverity.Success;
            return InfoBarSeverity.Informational;
        }
    }
    public bool HasConflicts { get => _hasConflicts; private set => SetProperty(ref _hasConflicts, value); }

    // Backward compat for existing callers that only know about "Handle" (CF).
    public string Handle => CfHandle;
    public bool IsLoggedIn => CfState == LoginState.LoggedIn;
    public bool ShowLoggedIn => CfShowLoggedIn;
    public bool ShowChallenge => CfShowChallenge;
    public bool ShowNotLoggedIn => CfShowNotLoggedIn;
    public bool ShowInitializing => CfShowInitializing;
    public bool CanSync => CanSyncCf;
    public string Status { get => _cfStatus; set { CfStatus = value; SyncStatus = value; } }
    public string HandleDisplay => CfHandleDisplay;

    public event Action? CloudflareChallenge;
    public event Action? SyncCompleted;

    private void NotifyCfSync()
    {
        OnPropertyChanged(nameof(CanSyncCf));
        OnPropertyChanged(nameof(CanSync));
        OnPropertyChanged(nameof(CfShowInitializing));
        OnPropertyChanged(nameof(CfShowChallenge));
        OnPropertyChanged(nameof(CfShowNotLoggedIn));
        OnPropertyChanged(nameof(CfShowLoggedIn));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(ShowLoggedIn));
        OnPropertyChanged(nameof(ShowChallenge));
        OnPropertyChanged(nameof(ShowNotLoggedIn));
        OnPropertyChanged(nameof(ShowInitializing));
        OnPropertyChanged(nameof(CfStateTitle));
    }

    private void NotifyQojSync()
    {
        OnPropertyChanged(nameof(CanSyncQoj));
        OnPropertyChanged(nameof(QojShowInitializing));
        OnPropertyChanged(nameof(QojShowChallenge));
        OnPropertyChanged(nameof(QojShowNotLoggedIn));
        OnPropertyChanged(nameof(QojShowLoggedIn));
        OnPropertyChanged(nameof(QojStateTitle));
    }

    // ---- Detection (called by page code-behind) ----

    public void ApplyCfDetection(LoginState state, string? handle)
    {
        bool handleChanged = false;
        if (state == LoginState.LoggedIn && !string.IsNullOrWhiteSpace(handle) && handle != CfHandle)
        {
            CfHandle = handle!;
            handleChanged = true;
        }
        CfState = state;
        CfStatus = state switch
        {
            LoginState.LoggedIn => string.IsNullOrWhiteSpace(CfHandle)
                ? "已登录，但未能识别 handle。"
                : $"已登录：{CfHandle}",
            LoginState.CloudflareChallenge => "检测到 Cloudflare 人机验证，请在下方浏览器中完成验证。",
            LoginState.NotLoggedIn => "未登录。请在下方浏览器中登录 Codeforces。",
            _ => "正在检测 Codeforces 登录状态…",
        };
        if (handleChanged)
            _ = RefreshAccountsAsync();
    }

    public void ApplyQojDetection(LoginState state, string? handle)
    {
        bool handleChanged = false;
        if (state == LoginState.LoggedIn && !string.IsNullOrWhiteSpace(handle) && handle != QojHandle)
        {
            QojHandle = handle!;
            handleChanged = true;
        }
        else if (state == LoginState.NotLoggedIn && !string.IsNullOrWhiteSpace(QojHandle))
        {
            QojHandle = string.Empty;
            handleChanged = true;
        }
        QojState = state;
        QojStatus = state switch
        {
            LoginState.LoggedIn => string.IsNullOrWhiteSpace(QojHandle)
                ? "已登录，但未能识别 handle。"
                : $"已登录：{QojHandle}",
            LoginState.CloudflareChallenge => "检测到 Cloudflare 验证，请在下方浏览器中完成验证。",
            LoginState.NotLoggedIn => "未登录。请在下方浏览器中登录 QOJ。",
            _ => "正在检测 QOJ 登录状态…",
        };
        if (handleChanged)
            _ = RefreshAccountsAsync();
    }
    public void ApplyDetection(LoginState state, string? handle) => ApplyCfDetection(state, handle);

    public void Cancel() => _cts?.Cancel();

    // ---- Accounts + conflicts (platform-agnostic) ----

    public async Task RefreshAccountsAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var combined = new CombinedStatsService(db);

            var accounts = await db.Accounts.ToListAsync();
            var solvedByAccount = await combined.GetSolvedByAccountAsync();
            var submissionCountByAccount = await combined.GetSubmissionCountByAccountAsync();

            Accounts.Clear();
            foreach (var a in accounts.OrderByDescending(a => a.LastSyncedAt ?? 0))
                Accounts.Add(new AccountItem
                {
                    Platform = a.Platform,
                    Handle = a.Handle,
                    IsCurrent = (a.Platform == Platform.Codeforces && CfState == LoginState.LoggedIn && a.Handle == CfHandle)
                             || (a.Platform == Platform.Qoj && QojState == LoginState.LoggedIn && a.Handle == QojHandle),
                    VpSolved = solvedByAccount.GetValueOrDefault((a.Platform, a.Handle)),
                    TotalSubmissions = submissionCountByAccount.GetValueOrDefault((a.Platform, a.Handle)),
                    LastSyncCount = a.LastSyncCount,
                    LastSyncError = a.LastSyncError,
                    LastSyncedText = a.LastCompletedAt is { } t and > 0
                        ? "上次同步 " + DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                        : a.LastSyncedAt.HasValue ? "已有同步数据" : "从未同步",
                });

            await RefreshConflictsAsync(db);
        }
        catch { /* best-effort */ }
    }

    private async Task RefreshConflictsAsync(VpDbContext db)
    {
        var combined = new CombinedStatsService(db);
        var conflicts = await combined.GetConflictsAsync();
        Conflicts.Clear();
        foreach (var c in conflicts)
        {
            var item = new ConflictItem
            {
                Platform = c.Platform, GymId = c.GymId,
                Title = string.IsNullOrWhiteSpace(c.GymTitle) ? $"Gym {c.GymId}" : c.GymTitle!,
                IsUnresolved = c.IsUnresolved,
            };
            foreach (var cand in c.Candidates)
                item.Candidates.Add(new ConflictCandidateItem
                {
                    Platform = cand.Platform, GymId = c.GymId, Handle = cand.Handle,
                    SolvedCount = cand.SolvedCount, PenaltySeconds = cand.Penalty,
                    IsSelected = string.Equals(cand.Handle, c.SelectedHandle, StringComparison.Ordinal),
                });
            Conflicts.Add(item);
        }
        HasConflicts = Conflicts.Count > 0;
    }

    public async Task ResolveConflictAsync(Platform platform, long gymId, string handle)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await new CombinedStatsService(db).SelectAsync(platform, gymId, handle);
            await RefreshConflictsAsync(db);
            await RefreshAccountsAsync();
            SyncCompleted?.Invoke();
        }
        catch (Exception ex) { SyncStatus = $"冲突选择失败：{ex.Message}"; }
    }

    public async Task RemoveAccountAsync(Platform platform, string handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await new CombinedStatsService(db).RemoveAccountAsync(platform, handle);
            await RefreshAccountsAsync();
            SyncCompleted?.Invoke();
            SyncStatus = $"已移除账号 {handle}。";
        }
        catch (Exception ex) { SyncStatus = $"移除账号失败：{ex.Message}"; }
    }

    // ---- Sync ----

    public Task SyncAsync() => SyncCfAsync();

    public Task SyncCfAsync() => SyncCfHandleAsync(CfHandle);

    public Task SyncQojAsync() => SyncQojHandleAsync(QojHandle);

    private async Task SyncCfHandleAsync(string? handleToSync)
    {
        if (CfState != LoginState.LoggedIn || IsBusy || string.IsNullOrWhiteSpace(handleToSync)) return;
        if (!_holder.IsCfReady) { ReportCfSync("Codeforces 浏览器尚未就绪，请先完成登录。"); return; }

        SyncStatus = string.Empty;
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var handle = handleToSync.Trim();
        var diag = new Services.FileScrapeDiagnostics(AppHost.DataFolder);
        try
        {
            var (scraper, api, cloneService) = _holder.CreateCfSyncPipeline();
            scraper.Diagnostics = diag;
            api.Diagnostics = diag;
            cloneService.Diagnostics = diag;
            api.CloudflareChallengeDetected += _ => { ReportCfSync("检测到 CF 验证…"); CloudflareChallenge?.Invoke(); };
            scraper.CloudflareChallengeDetected += _ => { ReportCfSync("检测到 CF 验证…"); CloudflareChallenge?.Invoke(); };

            await using var db = await _dbFactory.CreateDbContextAsync(_cts.Token);
            var sync = new GymSyncService(db, scraper, api, cloneService: cloneService);
            var runToken = _cts.Token;
            var prog = new Progress<ScrapeProgress>(p => { diag.Log(p.Message); if (IsBusy && _cts?.Token == runToken) ReportCfSync(p.Message); });

            ReportCfSync($"正在同步 {handle} 的 CF gym 提交…");
            int stored = await sync.SyncAsync(handle, prog, _cts.Token);
            Progress = 100;
            var extra = await FinishAccountSyncAsync(Platform.Codeforces, handle, status => { diag.Log(status); ReportCfSync(status); }, _cts.Token);
            var pending = await db.Submissions.Where(s => s.Platform == Platform.Codeforces && s.Handle == handle &&
                (s.ParticipantType == null || s.ParticipantType == "" ||
                 ((s.ParticipantType == "VIRTUAL" || s.ParticipantType == "CONTESTANT") && s.RelativeTime == null)))
                .Select(s => s.GymId).Distinct().CountAsync();
            diag.Log($"=== CF full sync done: new={stored}, pendingTypeGyms={pending}; {extra} ===");
            ReportCfSync($"CF 提交同步完成，新增 {stored} 条。{extra}" +
                (pending > 0 ? $" {pending} 场参赛类型待补全，暂时失败的接口 6 小时后重试。" : string.Empty));
            await RefreshAccountsAsync();
            SyncCompleted?.Invoke();
        }
        catch (OperationCanceledException) { ReportCfSync("同步已取消。"); }
        catch (Exception ex) { ReportCfSync($"CF 同步失败：{ex.Message}"); }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    private async Task SyncQojHandleAsync(string? handleToSync)
    {
        if (QojState != LoginState.LoggedIn || IsBusy || string.IsNullOrWhiteSpace(handleToSync)) return;
        if (!_holder.IsQojReady) { ReportQojSync("QOJ 浏览器尚未就绪，请先完成登录。"); return; }

        SyncStatus = string.Empty;
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var handle = handleToSync.Trim();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(_cts.Token);
            var sync = _holder.CreateQojSyncPipeline(db);
            sync.Diagnostics = new Services.FileScrapeDiagnostics(AppHost.DataFolder);
            var runToken = _cts.Token;
            var prog = new Progress<ScrapeProgress>(p => { if (IsBusy && _cts?.Token == runToken) ReportQojSync(p.Message); });

            ReportQojSync($"正在同步 {handle} 的 QOJ 提交…");
            int stored = await sync.SyncAsync(handle, prog, _cts.Token);
            var total = await db.Submissions
                .CountAsync(s => s.Platform == Platform.Qoj && s.Handle == handle, _cts.Token);
            Progress = 100;
            var extra = await FinishAccountSyncAsync(Platform.Qoj, handle, ReportQojSync, _cts.Token);
            ReportQojSync(stored > 0
                ? $"QOJ 同步完成，新增 {stored} 条提交，当前共 {total} 条。{extra}"
                : $"QOJ 同步完成，没有新提交，当前共 {total} 条。{extra}");
            await RefreshAccountsAsync();
            SyncCompleted?.Invoke();
        }
        catch (OperationCanceledException) { ReportQojSync("同步已取消。"); }
        catch (Exception ex)
        {
            await RecordSyncFailureAsync(Platform.Qoj, handle, ex.Message);
            ReportQojSync($"QOJ 同步失败：{ex.Message}");
            await RefreshAccountsAsync();
        }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    private async Task<string> FinishAccountSyncAsync(Platform platform, string handle, Action<string> report, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FindAsync(new object[] { platform, handle }, ct);
        if (account != null) account.LastCompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SaveChangesAsync(ct);
        await RefreshAccountsAsync();
        SyncCompleted?.Invoke();
        bool acceptingProgress = true;
        try
        {
            var progress = new Progress<RanklandProgress>(p =>
            {
                if (acceptingProgress) report(p.Total > 0 ? $"榜单 {p.Done}/{p.Total}：{p.Message}" : p.Message);
            });
            var result = await new PostSyncService(db, _ranklandClient).RunAsync(platform, handle, progress, ct);
            return $"已自动关联，更新 {result.Updated} 场榜单" +
                (result.Failed > 0 ? $"；{result.Failed} 场榜单待重试。" : "。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "提交已保存，后续榜单更新已取消。";
        }
        catch (Exception ex)
        {
            return $"提交已保存，自动关联或榜单更新待重试：{ex.Message}";
        }
        finally { acceptingProgress = false; }
    }

    private async Task RecordSyncFailureAsync(Platform platform, string handle, string error)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var account = await db.Accounts.FindAsync(platform, handle);
            if (account == null) return;
            account.LastSyncCount = 0;
            account.LastSyncError = error;
            await db.SaveChangesAsync();
        }
        catch
        {
            // The original sync error remains the actionable message.
        }
    }

    private void ReportCfSync(string status)
    {
        CfStatus = status;
        SyncStatus = status;
    }

    private void ReportQojSync(string status)
    {
        QojStatus = status;
        SyncStatus = status;
    }
}
