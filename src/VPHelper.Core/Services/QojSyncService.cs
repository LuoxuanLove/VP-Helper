using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Scraping;

namespace VPHelper.Core.Services;

/// <summary>
/// Scrapes QOJ (<c>qoj.ac</c>) submissions for an account and persists them incrementally, then
/// refreshes the derived <see cref="GymContest"/> and <see cref="VpResult"/> rows.
///
/// Discovery flow (QOJ has no public API):
/// <list type="number">
/// <item>Fetch the account's profile page (<c>/user/profile/{handle}</c>) and parse the
/// "Virtual Participations" table to get the bounded set of contest ids to scrape.</item>
/// <item>Read the account-filtered global submissions feed. It returns all contest submissions in
/// a few pages and keeps absolute submit times; the profile participation start time converts VP
/// rows to relative contest time.</item>
/// <item>Read every discovered contest dashboard's problem table. It maps the numeric problem ids
/// in the account feed back to the contest letters used by QOJ standings and Rankland.</item>
/// <item>If the account feed is temporarily unavailable or unexpectedly empty, fall back to each
/// discovered contest's submissions page.</item>
/// </list>
///
/// Unlike Codeforces, no separate API enrichment step is needed: the HTML already carries the
/// VP/practice signal.
/// </summary>
public class QojSyncService
{
    private readonly VpDbContext _db;
    private readonly IBrowserFetcher? _fetcher;
    private readonly Throttle? _throttle;
    private readonly BackoffPolicy _backoff;
    private readonly Aggregator _aggregator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public QojSyncService(
        VpDbContext db,
        IBrowserFetcher? fetcher = null,
        Throttle? throttle = null,
        BackoffPolicy? backoff = null,
        Aggregator? aggregator = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _db = db;
        _fetcher = fetcher;
        _throttle = throttle;
        _backoff = backoff ?? new BackoffPolicy();
        _aggregator = aggregator ?? new Aggregator();
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    /// <summary>Raised when a Cloudflare challenge is detected; UI should surface the WebView.</summary>
    public event Action<string>? CloudflareChallengeDetected;

    /// <summary>Optional diagnostics sink for live QOJ sync runs.</summary>
    public IScrapeDiagnostics? Diagnostics { get; set; }

    /// <summary>
    /// Sync QOJ submissions for <paramref name="handle"/>. Returns the number of new submissions stored.
    /// Discovers VP contests via the profile page, then scrapes each contest's submission history.
    /// </summary>
    public async Task<int> SyncAsync(
        string handle,
        IProgress<ScrapeProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_fetcher == null || _throttle == null)
            throw new InvalidOperationException("A browser fetcher and throttle are required for live sync.");

        Diagnostics?.Log($"=== QOJ sync start: handle='{handle}' ===");

        // ---- step 1: discover VP contests from the profile page ----
        progress?.Report(new ScrapeProgress(0, 0, "正在读取 QOJ 主页…"));
        var profileUrl = $"https://qoj.ac/user/profile/{handle}";
        var profileResult = await FetchWithRetryAsync(profileUrl, ct).ConfigureAwait(false);
        if (profileResult == null)
            throw new InvalidOperationException("无法读取 QOJ 个人主页，请确认浏览器仍处于登录状态后重试。");

        var session = QojSessionParser.Parse(profileResult.Body ?? string.Empty);
        Diagnostics?.Log($"Profile: bodyLen={profileResult.Body?.Length ?? 0}, loggedIn={session.IsLoggedIn}, sessionHandle='{session.Handle ?? ""}'");
        Diagnostics?.CaptureRaw($"qoj_profile_{handle}.html", profileResult.Body ?? string.Empty);
        if (!session.IsLoggedIn)
            throw new InvalidOperationException("QOJ 登录状态已失效，请先在内嵌浏览器中重新登录。");
        if (!string.IsNullOrWhiteSpace(session.Handle)
            && !string.Equals(session.Handle, handle, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"QOJ 当前登录账号为 {session.Handle}，与待同步账号 {handle} 不一致。");

        var vpContests = QojProfileParser.ParseVirtualParticipations(profileResult.Body ?? string.Empty);
        Diagnostics?.Log($"Profile: discovered {vpContests.Count} virtual contest(s)");
        if (vpContests.Count == 0)
            throw new InvalidOperationException("QOJ 个人主页没有可同步的虚拟参赛记录。");

        // ---- step 2: load the contest-native problem-id -> letter mapping ----
        var problemsByContest = await LoadContestProblemsAsync(vpContests, ct).ConfigureAwait(false);
        await NormalizeStoredProblemIndicesAsync(handle, problemsByContest, ct).ConfigureAwait(false);

        // ---- step 3: scrape each contest's submissions page ----
        // Known-id set scoped to QOJ: submission ids collide between judges.
        var knownIds = new HashSet<long>(
            await _db.Submissions
                .Where(s => s.Platform == Platform.Qoj && s.Handle == handle)
                .Select(s => s.Id)
                .ToListAsync(ct));

        int totalStored = 0;
        var affectedContests = new HashSet<long>();
        int successfulContests = 0;
        int failedContests = 0;

        var contestLookup = vpContests.ToDictionary(c => c.ContestId);
        var accountScrape = await ScrapeAccountAsync(handle, contestLookup, knownIds, ct)
            .ConfigureAwait(false);

        if (accountScrape.FetchSucceeded && accountScrape.ParsedRows > 0
            && accountScrape.IdentifiedSubmitterRows == 0)
            throw new InvalidOperationException(
                "QOJ 提交页包含提交记录，但无法识别提交者。页面结构可能已更新，请升级后重试。");

        if (accountScrape.FetchSucceeded && accountScrape.ParsedRows > 0
            && accountScrape.HandleRows == 0)
            throw new InvalidOperationException(
                $"QOJ 账号筛选页没有返回 {handle} 的提交，未修改本地数据。");

        if (accountScrape.FetchSucceeded && accountScrape.SeenContestIds.Count > 0)
        {
            successfulContests = accountScrape.SeenContestIds.Count;
            foreach (var s in accountScrape.Submissions)
            {
                knownIds.Add(s.Id);
                affectedContests.Add(s.ContestId);
                StoreSubmission(handle, NormalizeProblem(s, problemsByContest));
                totalStored++;
            }
            Diagnostics?.Log(
                $"Account feed: parsedRows={accountScrape.ParsedRows}, " +
                $"identifiedSubmitters={accountScrape.IdentifiedSubmitterRows}, " +
                $"handleRows={accountScrape.HandleRows}, eligibleContests={accountScrape.SeenContestIds.Count}, " +
                $"newRows={accountScrape.Submissions.Count}");
        }
        else
        {
            Diagnostics?.Log($"Account feed unavailable or empty; falling back to {vpContests.Count} contest page(s)");

            int contestDone = 0;
            int fallbackParsedRows = 0;
            int fallbackIdentifiedSubmitters = 0;
            foreach (var contest in vpContests)
            {
                ct.ThrowIfCancellationRequested();
                contestDone++;
                progress?.Report(new ScrapeProgress(contestDone, vpContests.Count,
                    $"QOJ 比赛 {contest.ContestId}：{contest.Title}（{contestDone}/{vpContests.Count}）"));

                var scrape = await ScrapeContestAsync(
                    handle, contest.ContestId, knownIds, progress, ct).ConfigureAwait(false);
                if (!scrape.FetchSucceeded)
                {
                    failedContests++;
                    Diagnostics?.Log($"Contest {contest.ContestId}: fetch failed");
                    continue;
                }
                successfulContests++;
                fallbackParsedRows += scrape.ParsedRows;
                fallbackIdentifiedSubmitters += scrape.IdentifiedSubmitterRows;
                Diagnostics?.Log(
                    $"Contest {contest.ContestId}: parsedRows={scrape.ParsedRows}, " +
                    $"identifiedSubmitters={scrape.IdentifiedSubmitterRows}, " +
                    $"handleRows={scrape.HandleRows}, newRows={scrape.Submissions.Count}");

                foreach (var s in scrape.Submissions)
                {
                    knownIds.Add(s.Id);
                    affectedContests.Add(contest.ContestId);
                    StoreSubmission(handle, NormalizeProblem(s, problemsByContest));
                    totalStored++;
                }
            }

            if (fallbackParsedRows > 0 && fallbackIdentifiedSubmitters == 0)
                throw new InvalidOperationException(
                    "QOJ 比赛提交页包含提交记录，但无法识别提交者。页面结构可能已更新，请升级后重试。");
        }

        if (successfulContests == 0)
            throw new InvalidOperationException($"QOJ 的 {failedContests} 场比赛提交页均读取失败，未修改本地数据。请稍后重试。");

        if (failedContests > 0)
            progress?.Report(new ScrapeProgress(successfulContests, vpContests.Count,
                $"已读取 {successfulContests}/{vpContests.Count} 场比赛，{failedContests} 场暂时失败。"));

        // Advance watermark and persist sync result.
        var account = await _db.Accounts.FindAsync(new object?[] { Platform.Qoj, handle }, ct);
        if (account == null)
        {
            account = new Account { Platform = Platform.Qoj, Handle = handle };
            _db.Accounts.Add(account);
        }
        account.LastSyncedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        account.LastSyncCount = totalStored;
        account.LastSyncError = null;
        // knownIds began with every persisted row for this account and includes all submissions
        // staged during this run. CountAsync would omit those Added entities until SaveChanges.
        account.TotalSubmissions = knownIds.Count;
        await _db.SaveChangesAsync(ct);

        // Rebuild both newly changed contests and prior stored contests whose problem indices were
        // normalized from QOJ's numeric feed identity to the contest letter.
        var contestsToRebuild = affectedContests
            .Concat(problemsByContest.Keys)
            .Distinct()
            .ToArray();
        if (contestsToRebuild.Length > 0)
            await RebuildDerivedAsync(handle, contest: null, onlyContests: contestsToRebuild, ct: ct);

        Diagnostics?.Log($"=== QOJ sync done: new={totalStored}, okContests={successfulContests}, failedContests={failedContests} ===");
        return totalStored;
    }

    private void StoreSubmission(string handle, QojParsedSubmission s)
    {
        _db.Submissions.Add(new Submission
        {
            Platform = Platform.Qoj,
            Id = s.Id,
            Handle = handle,
            GymId = s.ContestId,
            ProblemId = s.ProblemId,
            ProblemIndex = s.ProblemIndex,
            ProblemName = s.ProblemName,
            ProblemUrl = s.ProblemUrl,
            Verdict = s.Verdict,
            SubmitTime = s.SubmitTimeUnix,
            ParticipantType = s.InContest ? "VIRTUAL" : "PRACTICE",
            RelativeTime = s.RelativeSeconds,
        });
    }

    private async Task<Dictionary<long, IReadOnlyDictionary<string, QojContestProblemDefinition>>> LoadContestProblemsAsync(
        IReadOnlyList<QojProfileContest> contests,
        CancellationToken ct)
    {
        var results = new Dictionary<long, IReadOnlyDictionary<string, QojContestProblemDefinition>>();
        foreach (var contest in contests)
        {
            ct.ThrowIfCancellationRequested();
            var result = await FetchWithRetryAsync(BuildContestDashboardUrl(contest.ContestId), ct).ConfigureAwait(false);
            if (result == null) continue;

            var definitions = QojSubmissionsParser.ParseContestProblems(result.Body ?? string.Empty, contest.ContestId);
            if (definitions.Count == 0)
            {
                Diagnostics?.Log($"Contest {contest.ContestId}: no parsable problem definitions");
                continue;
            }

            var byId = definitions.ToDictionary(p => p.ProblemId, StringComparer.Ordinal);
            results[contest.ContestId] = byId;
            await UpsertContestProblemsAsync(definitions, ct).ConfigureAwait(false);
            Diagnostics?.Log($"Contest {contest.ContestId}: loaded {definitions.Count} problem definition(s)");
        }

        return results;
    }

    private async Task UpsertContestProblemsAsync(
        IReadOnlyList<QojContestProblemDefinition> definitions,
        CancellationToken ct)
    {
        foreach (var definition in definitions)
        {
            var existing = await _db.QojContestProblems.FindAsync(
                new object?[] { definition.ContestId, definition.ProblemId }, ct);
            if (existing == null)
            {
                _db.QojContestProblems.Add(new QojContestProblem
                {
                    ContestId = definition.ContestId,
                    ProblemId = definition.ProblemId,
                    ProblemIndex = definition.ProblemIndex,
                    Title = definition.Title,
                    ProblemUrl = definition.ProblemUrl,
                });
                continue;
            }

            existing.ProblemIndex = definition.ProblemIndex;
            existing.Title = definition.Title;
            existing.ProblemUrl = definition.ProblemUrl;
        }
    }

    private async Task NormalizeStoredProblemIndicesAsync(
        string handle,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, QojContestProblemDefinition>> problemsByContest,
        CancellationToken ct)
    {
        if (problemsByContest.Count == 0) return;

        var existing = await _db.Submissions
            .Where(s => s.Platform == Platform.Qoj && s.Handle == handle)
            .ToListAsync(ct);

        foreach (var submission in existing)
        {
            if (!problemsByContest.TryGetValue(submission.GymId, out var byId)) continue;

            var problemId = !string.IsNullOrWhiteSpace(submission.ProblemId)
                ? submission.ProblemId
                : QojSubmissionsParser.ParseProblemHref(submission.ProblemUrl ?? string.Empty).problemId;
            if (string.IsNullOrWhiteSpace(problemId) || !byId.TryGetValue(problemId, out var definition)) continue;

            submission.ProblemId = definition.ProblemId;
            submission.ProblemIndex = definition.ProblemIndex;
            submission.ProblemName = definition.Title;
            submission.ProblemUrl = definition.ProblemUrl;
        }
    }

    private static QojParsedSubmission NormalizeProblem(
        QojParsedSubmission submission,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, QojContestProblemDefinition>> problemsByContest)
    {
        if (!problemsByContest.TryGetValue(submission.ContestId, out var byId) ||
            !byId.TryGetValue(submission.ProblemId, out var definition))
            return submission;

        return submission with
        {
            ProblemIndex = definition.ProblemIndex,
            ProblemName = definition.Title,
            ProblemUrl = definition.ProblemUrl,
        };
    }

    private async Task<QojContestScrapeResult> ScrapeAccountAsync(
        string handle,
        IReadOnlyDictionary<long, QojProfileContest> contests,
        HashSet<long> knownIds,
        CancellationToken ct)
    {
        var fresh = new List<QojParsedSubmission>();
        var seen = new HashSet<long>();
        int? maxPage = null;
        bool fetchSucceeded = false;
        int parsedRows = 0;
        int identifiedSubmitterRows = 0;
        int handleRows = 0;
        var seenContestIds = new HashSet<long>();

        for (int page = 1; page <= 50; page++)
        {
            if (maxPage != null && page > maxPage) break;
            var url = BuildAccountPageUrl(handle, page);
            var result = await FetchWithRetryAsync(url, ct).ConfigureAwait(false);
            if (result == null) break;
            fetchSucceeded = true;

            var parsed = QojSubmissionsParser.Parse(result.Body ?? string.Empty);
            parsedRows += parsed.Submissions.Count;
            maxPage ??= parsed.MaxPageIndex;
            if (page == 1)
                Diagnostics?.CaptureRaw($"qoj_submissions_{handle}_page1.html", result.Body ?? string.Empty);

            bool hitKnown = false;
            int newOnPage = 0;
            foreach (var raw in parsed.Submissions)
            {
                if (!string.IsNullOrWhiteSpace(raw.Submitter)) identifiedSubmitterRows++;
                if (!string.Equals(raw.Submitter, handle, StringComparison.OrdinalIgnoreCase)) continue;
                handleRows++;
                if (!contests.ContainsKey(raw.ContestId)) continue;
                seenContestIds.Add(raw.ContestId);
                if (!seen.Add(raw.Id)) continue;

                var submission = raw;
                if (raw.InContest && raw.RelativeSeconds == null && raw.SubmitTimeUnix > 0
                    && contests.TryGetValue(raw.ContestId, out var contest)
                    && contest.StartTimeUnix is > 0)
                {
                    submission = raw with
                    {
                        RelativeSeconds = Math.Max(0, raw.SubmitTimeUnix - contest.StartTimeUnix.Value),
                    };
                }

                if (knownIds.Contains(raw.Id)) { hitKnown = true; continue; }
                fresh.Add(submission);
                newOnPage++;
            }

            if (parsed.Submissions.Count == 0) break;
            if (parsed.Submissions.All(s => string.IsNullOrWhiteSpace(s.Submitter))) break;
            if (hitKnown && newOnPage == 0) break;
        }

        return new QojContestScrapeResult(
            fresh, fetchSucceeded, parsedRows, identifiedSubmitterRows, handleRows, seenContestIds);
    }

    /// <summary>
    /// Scrape one QOJ contest's submissions page for a specific handle, up to a reasonable page
    /// limit. Returns only new submissions not yet in <paramref name="knownIds"/>.
    /// </summary>
    private async Task<QojContestScrapeResult> ScrapeContestAsync(
        string handle,
        long contestId,
        HashSet<long> knownIds,
        IProgress<ScrapeProgress>? progress,
        CancellationToken ct)
    {
        var fresh = new List<QojParsedSubmission>();
        var seenThisContest = new HashSet<long>();
        int? maxPage = null;
        const int maxPages = 50; // safety ceiling per contest
        bool fetchSucceeded = false;
        int parsedRows = 0;
        int identifiedSubmitterRows = 0;
        int handleRows = 0;
        var seenContestIds = new HashSet<long>();

        for (int page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (maxPage != null && page > maxPage) break;

            var url = BuildContestPageUrl(contestId, page);
            var result = await FetchWithRetryAsync(url, ct).ConfigureAwait(false);
            if (result == null) break;
            fetchSucceeded = true;

            var parsed = QojSubmissionsParser.Parse(result.Body ?? string.Empty);
            parsedRows += parsed.Submissions.Count;
            maxPage ??= parsed.MaxPageIndex;
            if (page == 1)
                Diagnostics?.CaptureRaw($"qoj_contest_{contestId}_page1.html", result.Body ?? string.Empty);

            bool hitKnown = false;
            foreach (var s in parsed.Submissions)
            {
                if (!string.IsNullOrWhiteSpace(s.Submitter)) identifiedSubmitterRows++;
                if (!string.Equals(s.Submitter, handle, StringComparison.OrdinalIgnoreCase)) continue;
                handleRows++;
                seenContestIds.Add(s.ContestId);
                if (!seenThisContest.Add(s.Id)) continue; // duplicate page
                if (knownIds.Contains(s.Id)) { hitKnown = true; continue; }
                fresh.Add(s);
            }

            if (parsed.Submissions.Count == 0) break;
            if (parsed.Submissions.All(s => string.IsNullOrWhiteSpace(s.Submitter))) break;
            if (maxPage == null && seenThisContest.Count == 0) break; // no handle rows + no pagination
            // Incremental: once we see a known submission and no new ones on the page, stop.
            if (hitKnown && fresh.Count == 0) break;
        }

        return new QojContestScrapeResult(
            fresh, fetchSucceeded, parsedRows, identifiedSubmitterRows, handleRows, seenContestIds);
    }

    /// <summary>
    /// Rebuild derived <see cref="GymContest"/> and <see cref="VpResult"/> rows from stored QOJ
    /// submissions. <paramref name="onlyContests"/> limits the rebuild to specific contest ids when
    /// provided (incremental sync); pass null for a full rebuild.
    /// </summary>
    public async Task RebuildDerivedAsync(
        string handle,
        long? contest = null,
        IReadOnlyCollection<long>? onlyContests = null,
        CancellationToken ct = default)
    {
        var query = _db.Submissions.Where(s => s.Platform == Platform.Qoj && s.Handle == handle);
        if (contest != null)
            query = query.Where(s => s.GymId == contest.Value);
        else if (onlyContests != null)
            query = query.Where(s => onlyContests.Contains(s.GymId));

        var subs = await query.ToListAsync(ct);
        var byContest = subs.GroupBy(s => s.GymId);

        foreach (var g in byContest)
        {
            long cid = g.Key;

            var gym = await _db.GymContests.FindAsync(new object?[] { Platform.Qoj, cid }, ct);
            if (gym == null)
            {
                gym = new GymContest { Platform = Platform.Qoj, GymId = cid };
                _db.GymContests.Add(gym);
            }
            gym.Title ??= $"QOJ {cid}";
            gym.ProblemCount = g.Select(s => s.ProblemIndex).Distinct().Count();

            // QOJ uses absolute submit times for practice rows; for in-contest rows only
            // RelativeTime is set. The contest start is not stored here (future: fetch it from
            // GymContest.StartTime), so use the minimum submit time as a proxy.
            gym.StartTime = g.Where(s => s.SubmitTime > 0).Select(s => s.SubmitTime)
                .DefaultIfEmpty(0).Min() is long t && t > 0 ? t : gym.StartTime;

            foreach (var kindGroup in g.GroupBy(s => ParticipationKindClassifier.Classify(s.ParticipantType)))
            {
                var kind = kindGroup.Key;
                var agg = _aggregator.Aggregate(handle, cid, kindGroup);

                var vp = await _db.VpResults.FindAsync(
                    new object?[] { Platform.Qoj, handle, cid, kind }, ct);
                if (vp == null)
                {
                    vp = new VpResult { Platform = Platform.Qoj, Handle = handle, GymId = cid, Kind = kind };
                    _db.VpResults.Add(vp);
                }
                vp.SolvedCount = agg.SolvedCount;
                vp.Penalty = kind == ParticipationKind.Virtual
                    ? _aggregator.PenaltyFromRelativeTime(agg, kindGroup)
                    : agg.PenaltySeconds;
                vp.FirstAcPerProblem = JsonSerializer.Serialize(agg.FirstAcPerProblem);
            }

            // Prune stale kind rows (same pattern as GymSyncService).
            var liveKinds = g.Select(s => ParticipationKindClassifier.Classify(s.ParticipantType)).ToHashSet();
            var existing = await _db.VpResults
                .Where(v => v.Platform == Platform.Qoj && v.Handle == handle && v.GymId == cid)
                .ToListAsync(ct);
            foreach (var stale in existing.Where(v => !liveKinds.Contains(v.Kind)))
                _db.VpResults.Remove(stale);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Contest submissions page URL (paginated via <c>?page={n}</c>).</summary>
    internal static string BuildContestPageUrl(long contestId, int page)
        => page <= 1
            ? $"https://qoj.ac/contest/{contestId}/submissions"
            : $"https://qoj.ac/contest/{contestId}/submissions?page={page}";

    internal static string BuildContestDashboardUrl(long contestId)
        => $"https://qoj.ac/contest/{contestId}";

    /// <summary>QOJ global submissions page filtered to one account.</summary>
    internal static string BuildAccountPageUrl(string handle, int page)
    {
        var prefix = $"https://qoj.ac/submissions?submitter={Uri.EscapeDataString(handle)}";
        return page <= 1 ? prefix : $"{prefix}&page={page}";
    }

    /// <summary>Fetch a URL with throttling and exponential-backoff retry.</summary>
    private async Task<FetchResult?> FetchWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= _backoff.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (_fetcher == null || _throttle == null)
                throw new InvalidOperationException("A browser fetcher and throttle are required for live sync.");

            await _throttle.WaitAsync(ct).ConfigureAwait(false);

            FetchResult result;
            try
            {
                result = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = new FetchResult(url, null, FetchStatus.Error, ex.Message);
            }

            Diagnostics?.Log($"Fetch {url}: attempt={attempt + 1}/{_backoff.MaxRetries + 1}, status={result.Status}, bodyLen={result.Body?.Length ?? 0}, message='{result.Message ?? ""}'");

            switch (result.Status)
            {
                case FetchStatus.Ok:
                    return result;
                case FetchStatus.CloudflareChallenge:
                    CloudflareChallengeDetected?.Invoke(url);
                    if (attempt == _backoff.MaxRetries) return null;
                    await _delay(_backoff.DelayFor(attempt), ct).ConfigureAwait(false);
                    break;
                case FetchStatus.RateLimited:
                case FetchStatus.Error:
                    if (attempt == _backoff.MaxRetries) return null;
                    await _delay(_backoff.DelayFor(attempt), ct).ConfigureAwait(false);
                    break;
            }
        }
        return null;
    }

    private sealed record QojContestScrapeResult(
        IReadOnlyList<QojParsedSubmission> Submissions,
        bool FetchSucceeded,
        int ParsedRows,
        int IdentifiedSubmitterRows,
        int HandleRows,
        IReadOnlySet<long> SeenContestIds);
}
