using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Data;
using VPHelper.Core.Models;
using VPHelper.Core.Scraping;

namespace VPHelper.Core.Services;

/// <summary>
/// Scrapes gym submissions for an account and persists them incrementally, then refreshes
/// the derived <see cref="GymContest"/> and <see cref="VpResult"/> rows (DESIGN §8 Phase 1).
///
/// Two-source flow (DESIGN 6.2): the HTML submissions page is the only way to <b>discover</b> which
/// gyms a handle touched (the <c>user.status</c> API excludes gym submissions), but it does not tag
/// rows as virtual vs practice. So each discovered gym is then enriched via the
/// <c>contest.status</c> API, which returns <c>participantType</c> — the field that separates a real
/// VP run from post-contest upsolving. When the API is available, its participant-typed rows are the
/// source of truth for that gym; when it is not (private gym needing auth, network give-up), the
/// HTML rows are still stored so the gym is not lost, just untyped (classified as practice).
/// </summary>
public class GymSyncService
{
    private const long DefaultContestDurationSeconds = 5 * 60 * 60;

    private readonly VpDbContext _db;
    private readonly SubmissionScraper? _scraper;
    private readonly ContestStatusApi? _api;
    private readonly GymCloneService? _cloneService;
    private readonly Analysis.Aggregator _aggregator;

    public GymSyncService(VpDbContext db, SubmissionScraper? scraper = null,
        ContestStatusApi? api = null, Analysis.Aggregator? aggregator = null,
        GymCloneService? cloneService = null)
    {
        _db = db;
        _scraper = scraper;
        _api = api;
        _cloneService = cloneService;
        _aggregator = aggregator ?? new Analysis.Aggregator();
    }

    /// <summary>
    /// Sync submissions for <paramref name="handle"/>. Returns the number of new submissions stored.
    /// Incremental: stops when a locally known submission id is encountered.
    /// </summary>
    public async Task<int> SyncAsync(string handle, IProgress<ScrapeProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_scraper == null)
            throw new InvalidOperationException("A submission scraper is required for live sync.");

        // Known-id set for incremental stop. Scoped to this platform: a submission id is unique only
        // within Codeforces, so a same-numbered QOJ row must not shadow a CF one.
        var knownIds = new HashSet<long>(
            await _db.Submissions
                .Where(s => s.Platform == Platform.Codeforces && s.Handle == handle)
                .Select(s => s.Id).ToListAsync(ct));

        var fresh = await _scraper.ScrapeAsync(handle, id => knownIds.Contains(id),
            progress: progress, ct: ct);

        int stored = 0;
        // Track which gyms gained new submissions so the derived rebuild can touch only those,
        // instead of re-aggregating every gym in history on an incremental sync that added a
        // handful of rows.
        var affectedGyms = new HashSet<long>();
        foreach (var s in fresh)
        {
            if (knownIds.Contains(s.Id)) continue;
            _db.Submissions.Add(new Submission
            {
                Platform = Platform.Codeforces,
                Id = s.Id,
                Handle = handle,
                GymId = s.GymId,
                ProblemIndex = s.ProblemIndex,
                ProblemName = s.ProblemName,
                ProblemUrl = s.ProblemUrl,
                Verdict = s.Verdict,
                SubmitTime = s.SubmitTimeUnix,
                // Participant type is unknown from HTML; the API enrichment below fills it in.
                ParticipantType = null,
                RelativeTime = null,
            });
            knownIds.Add(s.Id);
            affectedGyms.Add(s.GymId);
            stored++;
        }

        // Advance watermark to newest submission time seen.
        var account = await _db.Accounts.FindAsync(new object?[] { Platform.Codeforces, handle }, ct);
        if (account == null)
        {
            account = new Account { Platform = Platform.Codeforces, Handle = handle };
            _db.Accounts.Add(account);
        }
        long newest = fresh.Count > 0 ? fresh.Max(s => s.SubmitTimeUnix) : (account.LastSyncedAt ?? 0);
        account.LastSyncedAt = Math.Max(account.LastSyncedAt ?? 0, newest);
        account.LastSyncCount = stored;
        account.LastSyncError = null; // clear any prior error on success
        account.TotalSubmissions = stored + await _db.Submissions
            .CountAsync(s => s.Platform == Platform.Codeforces && s.Handle == handle, ct);

        await _db.SaveChangesAsync(ct);

        // Enrich gyms with participant type from the CF API (the only source that separates VP
        // from practice). Include old HTML-only rows too; otherwise an incremental no-op sync
        // would never repair rows written before API enrichment existed or after a prior API
        // failure.
        var gymsToEnrich = new HashSet<long>(affectedGyms);
        if (_api != null)
        {
            var staleTypedGyms = await _db.Submissions
                .Where(s => s.Platform == Platform.Codeforces &&
                            s.Handle == handle &&
                            (s.ParticipantType == null ||
                             s.ParticipantType == "" ||
                             ((s.ParticipantType == "VIRTUAL" ||
                               s.ParticipantType == "CONTESTANT") &&
                              s.RelativeTime == null)))
                .Select(s => s.GymId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var gymId in staleTypedGyms)
                gymsToEnrich.Add(gymId);
        }

        int apiInserted = 0;
        if (gymsToEnrich.Count > 0 && _api != null)
            apiInserted = await EnrichParticipantTypesAsync(handle, gymsToEnrich, progress, ct);

        if (apiInserted > 0)
        {
            stored += apiInserted;
            account.LastSyncCount = stored;
            account.TotalSubmissions = await _db.Submissions
                .CountAsync(s => s.Platform == Platform.Codeforces && s.Handle == handle, ct);
            await _db.SaveChangesAsync(ct);
        }

        // Rebuild gyms that gained submissions. If this is an incremental no-op but the database
        // already contains submissions with missing derived rows (e.g. after an older build or a
        // failed previous rebuild), repair all derived rows for this handle.
        var gymsToRebuild = gymsToEnrich.Count > 0 ? gymsToEnrich : affectedGyms;
        if (gymsToRebuild.Count > 0)
            await RebuildDerivedAsync(handle, gymsToRebuild, ct);
        else if (await NeedsDerivedRepairAsync(handle, ct))
            await RebuildDerivedAsync(handle, onlyGyms: null, ct);

        if (_cloneService != null)
            await RefreshCloneChainsAsync(gymsToRebuild.Count > 0 ? gymsToRebuild : affectedGyms, progress, ct);

        return stored;
    }

    private async Task RefreshCloneChainsAsync(IReadOnlyCollection<long> recentlyTouchedGyms,
        IProgress<ScrapeProgress>? progress, CancellationToken ct)
    {
        var ids = recentlyTouchedGyms.ToHashSet();
        var cache = new SyncRetryCache(_db);
        var allGyms = await _db.GymContests.Where(g => g.Platform == Platform.Codeforces).ToListAsync(ct);
        _cloneService!.SeedCache(allGyms);
        ids.ExceptWith(allGyms.Where(g => g.CloneCheckedAt.HasValue).Select(g => g.GymId));

        foreach (var id in ids.ToList())
            if (await cache.IsCoolingDownAsync($"clone-retry:{id}", ct)) ids.Remove(id);
        int historical = 0;
        foreach (var gym in allGyms.Where(g => g.CloneCheckedAt == null).OrderByDescending(g => g.StartTime ?? 0))
        {
            if (ids.Contains(gym.GymId) || await cache.IsCoolingDownAsync($"clone-retry:{gym.GymId}", ct)) continue;
            ids.Add(gym.GymId);
            if (++historical == 5) break;
        }

        if (ids.Count == 0)
            return;

        int done = 0;
        foreach (var gymId in ids)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress?.Report(new ScrapeProgress(0, 0, $"检查 gym {gymId} 克隆链 ({done}/{ids.Count})..."));

            var gym = await _db.GymContests.FindAsync(new object?[] { Platform.Codeforces, gymId }, ct);
            if (gym == null)
                continue;

            if (gym.CloneCheckedAt.HasValue || await cache.IsCoolingDownAsync($"clone-retry:{gymId}", ct)) continue;
            var chain = await _cloneService!.ResolveAsync(gymId, ct).ConfigureAwait(false);
            ApplyCloneChain(gym, chain);
            await cache.SetAsync($"clone-retry:{gymId}", chain.IsComplete ? null : TimeSpan.FromHours(6), ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private void ApplyCloneChain(GymContest sourceGym, GymCloneChain chain)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        sourceGym.CloneParentGymId = chain.DirectParentGymId;
        sourceGym.CloneParentTitle = chain.DirectParentTitle;
        sourceGym.CloneRootGymId = chain.RootGymId;
        sourceGym.CloneCheckedAt = chain.IsComplete ? now : null;

        foreach (var edge in chain.Edges)
        {
            var child = _db.GymContests.Local.FirstOrDefault(g =>
                g.Platform == Platform.Codeforces && g.GymId == edge.GymId);
            if (child == null)
                child = _db.GymContests.Find(Platform.Codeforces, edge.GymId);
            if (child != null)
            {
                child.CloneParentGymId = edge.ParentGymId;
                child.CloneParentTitle = edge.ParentTitle;
                child.CloneRootGymId = chain.RootGymId;
                child.CloneCheckedAt = chain.IsComplete ? now : null;
            }

            var parent = _db.GymContests.Local.FirstOrDefault(g =>
                g.Platform == Platform.Codeforces && g.GymId == edge.ParentGymId);
            if (parent == null)
                parent = _db.GymContests.Find(Platform.Codeforces, edge.ParentGymId);
            if (parent == null)
            {
                parent = new GymContest
                {
                    Platform = Platform.Codeforces,
                    GymId = edge.ParentGymId,
                    Title = edge.ParentTitle,
                    CloneRootGymId = chain.RootGymId == edge.ParentGymId ? null : chain.RootGymId,
                    CloneCheckedAt = chain.IsComplete && chain.RootGymId == edge.ParentGymId ? now : null,
                };
                _db.GymContests.Add(parent);
            }
            else
            {
                parent.Title ??= edge.ParentTitle;
                parent.CloneRootGymId = chain.RootGymId == edge.ParentGymId ? null : chain.RootGymId;
            }

            if (chain.IsComplete && chain.RootGymId == edge.ParentGymId)
            {
                parent.CloneParentGymId = null;
                parent.CloneParentTitle = null;
                parent.CloneCheckedAt = now;
            }
        }
    }

    /// <summary>
    /// For each affected gym, fetch <c>contest.status</c> and write each row's participant type +
    /// relative time back onto the stored submission. Rows the API returns that were not seen via
    /// HTML are also inserted (the API is authoritative for gym membership), so a gym fully
    /// discovered here is complete. A gym the API cannot serve is left as-is.
    /// </summary>
    private async Task<int> EnrichParticipantTypesAsync(string handle, IReadOnlyCollection<long> gyms,
        IProgress<ScrapeProgress>? progress, CancellationToken ct)
    {
        int done = 0;
        int inserted = 0;
        foreach (var gymId in gyms)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress?.Report(new ScrapeProgress(0, 0, $"读取 gym {gymId} 参与类型 ({done}/{gyms.Count})…"));

            var cache = new SyncRetryCache(_db);
            var retryKey = $"cf-type-retry:{handle}:{gymId}";
            if (await cache.IsCoolingDownAsync(retryKey, ct))
            {
                progress?.Report(new ScrapeProgress(0, 0, $"gym {gymId} 参赛类型暂不可用，已跳过重复请求 ({done}/{gyms.Count})"));
                continue;
            }
            var result = await _api!.FetchGymAsync(gymId, handle, ct).ConfigureAwait(false);
            await cache.SetAsync(retryKey, result.Ok ? null : TimeSpan.FromHours(6), ct);
            if (!result.Ok)
            {
                progress?.Report(new ScrapeProgress(0, 0, $"gym {gymId} 参赛类型待补全：{result.Comment} ({done}/{gyms.Count})"));
                continue;
            }

            // Index the API rows by submission id for a single pass over the stored rows.
            var byId = result.Submissions.ToDictionary(a => a.Id);

            var stored = await _db.Submissions
                .Where(s => s.Platform == Platform.Codeforces && s.Handle == handle && s.GymId == gymId)
                .ToListAsync(ct);
            var storedIds = new HashSet<long>(stored.Select(s => s.Id));

            foreach (var s in stored)
            {
                if (byId.TryGetValue(s.Id, out var api))
                {
                    s.ParticipantType = api.ParticipantType;
                    s.RelativeTime = api.RelativeTime;
                    // The API verdict is authoritative (HTML verdicts can lag); keep it in sync.
                    if (!string.IsNullOrEmpty(api.Verdict)) s.Verdict = api.Verdict;
                }
            }

            // Insert API rows that HTML never surfaced (e.g. rejected practice runs the HTML page
            // paginated past). Keeps the participant-typed picture complete for this gym.
            foreach (var api in result.Submissions)
            {
                if (storedIds.Contains(api.Id)) continue;
                _db.Submissions.Add(new Submission
                {
                    Platform = Platform.Codeforces,
                    Id = api.Id,
                    Handle = handle,
                    GymId = api.GymId,
                    ProblemIndex = api.ProblemIndex,
                    ProblemName = api.ProblemName,
                    ProblemUrl = SubmissionsParser.BuildProblemUrl(api.GymId, api.ProblemIndex),
                    Verdict = api.Verdict,
                    SubmitTime = api.SubmitTimeUnix,
                    ParticipantType = api.ParticipantType,
                    RelativeTime = api.RelativeTime,
                });
                storedIds.Add(api.Id);
                inserted++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return inserted;
    }

    private async Task<bool> NeedsDerivedRepairAsync(string handle, CancellationToken ct)
    {
        var hasSubmissions = await _db.Submissions
            .AnyAsync(s => s.Platform == Platform.Codeforces && s.Handle == handle, ct);
        if (!hasSubmissions) return false;

        var hasGymContests = await _db.GymContests
            .AnyAsync(g => g.Platform == Platform.Codeforces, ct);
        var hasResults = await _db.VpResults
            .AnyAsync(v => v.Platform == Platform.Codeforces && v.Handle == handle, ct);
        return !hasGymContests || !hasResults;
    }

    /// <summary>
    /// Rebuild derived <see cref="GymContest"/> and <see cref="VpResult"/> rows from stored
    /// submissions. Pass <paramref name="onlyGyms"/> to limit the rebuild to specific gyms
    /// (incremental sync); pass null to rebuild every gym for the handle (full rebuild).
    ///
    /// Each gym yields up to two <see cref="VpResult"/> rows — one per
    /// <see cref="ParticipationKind"/> — so a real VP run and post-contest practice stay separate.
    /// </summary>
    public async Task RebuildDerivedAsync(string handle, IReadOnlyCollection<long>? onlyGyms = null,
        CancellationToken ct = default)
    {
        var query = _db.Submissions.Where(s => s.Platform == Platform.Codeforces && s.Handle == handle);
        if (onlyGyms != null)
            query = query.Where(s => onlyGyms.Contains(s.GymId));
        var subs = await query.ToListAsync(ct);
        var byGym = subs.GroupBy(s => s.GymId);

        foreach (var g in byGym)
        {
            long gymId = g.Key;

            // GymContest facts (shared across kinds).
            var gym = await _db.GymContests.FindAsync(new object?[] { Platform.Codeforces, gymId }, ct);
            if (gym == null)
            {
                gym = new GymContest { Platform = Platform.Codeforces, GymId = gymId };
                _db.GymContests.Add(gym);
            }
            // Title: derive a placeholder from the gym id; the real title requires a gym page fetch
            // (future). Keep any existing user/scraped title.
            gym.Title ??= $"Gym {gymId}";
            gym.StartTime = g.Min(s => s.SubmitTime);
            gym.ProblemCount = g.Select(s => s.ProblemIndex).Distinct().Count();

            var derivedGroups = SubmissionRunBuilder.BuildForCodeforces(g.ToList(), DefaultContestDurationSeconds);

            // Split by participation kind and aggregate each independently.
            foreach (var kindGroup in derivedGroups)
            {
                var kind = kindGroup.Kind;
                var agg = _aggregator.Aggregate(handle, gymId, kindGroup.Submissions);

                var vp = await _db.VpResults.FindAsync(
                    new object?[] { Platform.Codeforces, handle, gymId, kind }, ct);
                if (vp == null)
                {
                    vp = new VpResult { Platform = Platform.Codeforces, Handle = handle, GymId = gymId, Kind = kind };
                    _db.VpResults.Add(vp);
                }
                vp.SolvedCount = agg.SolvedCount;
                vp.Penalty = kind == ParticipationKind.Virtual
                    ? _aggregator.PenaltyFromRelativeTime(agg, kindGroup.Submissions)
                    : agg.PenaltySeconds;
                vp.FirstAcPerProblem = System.Text.Json.JsonSerializer.Serialize(agg.FirstAcPerProblem);
            }

            // Prune stale kind rows: a gym that used to have (say) a practice aggregate but no
            // longer has any practice submission must not keep the old row around.
            var liveKinds = derivedGroups.Select(group => group.Kind).ToHashSet();
            var existing = await _db.VpResults
                .Where(v => v.Platform == Platform.Codeforces && v.Handle == handle && v.GymId == gymId)
                .ToListAsync(ct);
            foreach (var stale in existing.Where(v => !liveKinds.Contains(v.Kind)))
                _db.VpResults.Remove(stale);
        }

        await _db.SaveChangesAsync(ct);
    }

}
