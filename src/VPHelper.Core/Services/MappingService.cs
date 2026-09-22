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

namespace VPHelper.Core.Services;

/// <summary>The estimated live placement of a VP result inside an official standing.</summary>
public record VpEstimate(
    string OfficialId,
    string OfficialName,
    int Rank,
    int TotalTeams,
    int Solved,
    long PenaltySeconds,
    Medal Medal,
    MedalLine MedalLine);

/// <summary>
/// Semantics of <see cref="ContestMapping.Confirmed"/>. Stored as an int column so extending the
/// set needs no schema change. Usable-for-estimation = <see cref="ManualConfirmed"/> or
/// <see cref="AutoConfirmed"/>.
/// </summary>
public static class MappingStatus
{
    /// <summary>Per-gym candidate persisted by the one-at-a-time manual flow.</summary>
    public const int Candidate = 0;
    /// <summary>User picked this official explicitly. Never overwritten by auto-matching.</summary>
    public const int ManualConfirmed = 1;
    /// <summary>Bulk auto-match from the curated contest directory; treated as usable.</summary>
    public const int AutoConfirmed = 2;
    /// <summary>Legacy/manual-review candidate from older fuzzy bulk matching.</summary>
    public const int Pending = 3;
    /// <summary>Synthetic: no mapping row exists for the gym (overview only, never persisted).</summary>
    public const int Unmapped = -1;
}

/// <summary>One row of the gym↔official mapping overview (DESIGN 6.3, bulk auto-match).</summary>
public record MappingOverviewRow(
    long GymId,
    string GymTitle,
    string? OfficialId,
    string? OfficialName,
    double Confidence,
    int Status,
    Platform Platform = Platform.Codeforces);

/// <summary>Outcome counts of a bulk auto-match run.</summary>
public record AutoMatchSummary(
    int Total,
    int AutoConfirmed,
    int Pending,
    int SkippedManual,
    int NoCandidate);

/// <summary>Progress tick for a bulk auto-match run.</summary>
public record MatchProgress(int Done, int Total, string Current);

/// <summary>
/// Manages gym↔official mappings (candidate generation, confirmation, reuse) and estimates
/// VP placement inside an official standing (DESIGN 6.3 / 6.6). Pure persistence + analysis;
/// no UI dependency.
/// </summary>
public class MappingService
{
    private readonly VpDbContext _db;
    private readonly MedalRuleProvider _medalProvider;

    public MappingService(VpDbContext db, ContestMatcher? matcher = null, MedalRuleProvider? medalProvider = null)
    {
        _db = db;
        // Kept for source compatibility with older callers. Candidate generation no longer invokes
        // fuzzy matching; only curated directory records and clone-chain nodes are eligible.
        _medalProvider = medalProvider ?? new MedalRuleProvider();
    }

    /// <summary>Generate (and persist as confirmed=0) candidate officials for a gym.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> GenerateCandidatesAsync(long gymId, int topN = 5,
        CancellationToken ct = default)
        => await GenerateCandidatesAsync(Platform.Codeforces, gymId, topN, ct);

    /// <summary>Generate candidate officials for one platform contest.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> GenerateCandidatesAsync(
        Platform platform, long gymId, int topN = 5, CancellationToken ct = default)
    {
        _ = topN;
        var allGyms = await _db.GymContests
            .Where(g => g.Platform == platform)
            .ToListAsync(ct);
        var gymsById = allGyms.ToDictionary(g => g.GymId);
        if (!gymsById.TryGetValue(gymId, out var gym))
            gym = await _db.GymContests.FindAsync(new object?[] { platform, gymId }, ct);
        if (gym == null) return Array.Empty<MatchCandidate>();

        var officials = await _db.OfficialContests
            .Select(o => new MatchOfficial(o.Id, o.Name, o.StartAt, o.ProblemCount))
            .ToListAsync(ct);

        // Only an explicit directory record on this Gym or one of its clone-chain nodes is a
        // candidate. Similarity between titles, dates, and problem counts is not a binding method.
        var curated = GetCuratedCandidateForCloneChain(
            platform, gym, gymsById, officials);
        var candidates = curated == null
            ? new List<MatchCandidate>()
            : new List<MatchCandidate> { curated };

        // Persist candidates unless the user has already manually confirmed this gym.
        bool manuallyConfirmed = await _db.ContestMappings
            .AnyAsync(m => m.Platform == platform && m.GymId == gymId && m.Confirmed == MappingStatus.ManualConfirmed, ct);
        if (!manuallyConfirmed)
        {
            // Clear stale candidate rows for this gym, then re-add. Auto/pending rows are left in
            // place — they're the bulk-match result and reflect the same best candidate.
            var existing = await _db.ContestMappings
                .Where(m => m.Platform == platform && m.GymId == gymId).ToListAsync(ct);
            var stale = existing.Where(m => m.Confirmed == MappingStatus.Candidate).ToList();
            _db.ContestMappings.RemoveRange(stale);
            // Officials that still have a non-candidate row (auto/pending) for this gym: skip them to
            // avoid a (GymId, OfficialId) primary-key collision with the bulk-match result.
            var reserved = existing
                .Where(m => m.Confirmed != MappingStatus.Candidate)
                .Select(m => m.OfficialId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var c in candidates)
            {
                if (reserved.Contains(c.OfficialId)) continue;
                _db.ContestMappings.Add(new ContestMapping
                {
                    Platform = platform,
                    GymId = gymId,
                    OfficialId = c.OfficialId,
                    Confidence = c.Confidence,
                    Confirmed = MappingStatus.Candidate,
                });
            }
            await _db.SaveChangesAsync(ct);
        }
        return candidates;
    }

    /// <summary>Confirm a gym↔official mapping (manual, status 1) and drop other rows for the gym.</summary>
    public async Task ConfirmAsync(long gymId, string officialId, CancellationToken ct = default)
        => await ConfirmAsync(Platform.Codeforces, gymId, officialId, ct);

    public async Task ConfirmAsync(Platform platform, long gymId, string officialId, CancellationToken ct = default)
    {
        var rows = await _db.ContestMappings
            .Where(m => m.Platform == platform && m.GymId == gymId).ToListAsync(ct);
        // Reuse the row for the chosen official if it already exists (avoids delete+add on same key).
        var keep = rows.FirstOrDefault(m => m.OfficialId == officialId);
        _db.ContestMappings.RemoveRange(rows.Where(m => m.OfficialId != officialId));
        if (keep != null)
        {
            keep.Confidence = 1.0;
            keep.Confirmed = MappingStatus.ManualConfirmed;
        }
        else
        {
            _db.ContestMappings.Add(new ContestMapping
            {
                Platform = platform,
                GymId = gymId,
                OfficialId = officialId,
                Confidence = 1.0,
                Confirmed = MappingStatus.ManualConfirmed,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Remove every mapping row for a gym so it can be selected again from scratch.</summary>
    public async Task ClearAsync(long gymId, CancellationToken ct = default)
        => await ClearAsync(Platform.Codeforces, gymId, ct);

    public async Task ClearAsync(Platform platform, long gymId, CancellationToken ct = default)
    {
        var rows = await _db.ContestMappings
            .Where(m => m.Platform == platform && m.GymId == gymId)
            .ToListAsync(ct);
        _db.ContestMappings.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get the usable official id for a gym: a manual (status 1) or auto (status 2) confirmation.
    /// Manual wins over auto when both somehow exist for the same gym.
    /// </summary>
    public async Task<string?> GetConfirmedOfficialAsync(long gymId, CancellationToken ct = default)
        => await GetConfirmedOfficialAsync(Platform.Codeforces, gymId, ct);

    public async Task<string?> GetConfirmedOfficialAsync(
        Platform platform, long gymId, CancellationToken ct = default)
    {
        var direct = await PickConfirmedOfficialAsync(platform, gymId, ct);
        if (direct != null)
            return direct;

        if (platform != Platform.Codeforces)
            return null;

        var allGyms = await _db.GymContests
            .Where(g => g.Platform == Platform.Codeforces)
            .ToListAsync(ct);
        var gym = allGyms.FirstOrDefault(g => g.GymId == gymId);
        if (gym == null)
            return null;

        var gymsById = allGyms.ToDictionary(g => g.GymId);
        foreach (var chainGymId in GetCloneChainIds(gym, gymsById))
        {
            if (chainGymId == gymId)
                continue;

            var inherited = await PickConfirmedOfficialAsync(platform, chainGymId, ct);
            if (inherited != null)
                return inherited;
        }

        return null;
    }

    private async Task<string?> PickConfirmedOfficialAsync(Platform platform, long gymId, CancellationToken ct)
    {
        var usable = await _db.ContestMappings
            .Where(x => x.Platform == platform && x.GymId == gymId &&
                        (x.Confirmed == MappingStatus.ManualConfirmed || x.Confirmed == MappingStatus.AutoConfirmed))
            .ToListAsync(ct);
        if (usable.Count == 0) return null;
        // Prefer manual, then highest confidence.
        var pick = usable
            .OrderByDescending(m => m.Confirmed == MappingStatus.ManualConfirmed ? 1 : 0)
            .ThenByDescending(m => m.Confidence)
            .First();
        return pick.OfficialId;
    }

    /// <summary>
    /// Bulk-match gyms using only the curated contest directory. A gym is auto-confirmed only when
    /// its GymId appears there; otherwise prior auto/pending rows are removed and manual matching
    /// remains available.
    /// </summary>
    public async Task<AutoMatchSummary> AutoMatchAllAsync(
        double autoConfirmThreshold = 0.72,
        IProgress<MatchProgress>? progress = null,
        CancellationToken ct = default)
    {
        _ = autoConfirmThreshold;
        var gyms = await _db.GymContests
            .Where(g => g.StartTime != null || g.ProblemCount > 0)
            .ToListAsync(ct);
        var cfGymsById = gyms
            .Where(g => g.Platform == Platform.Codeforces)
            .ToDictionary(g => g.GymId);
        var officials = await _db.OfficialContests
            .Select(o => new MatchOfficial(o.Id, o.Name, o.StartAt, o.ProblemCount))
            .ToListAsync(ct);

        // Gyms with a manual confirmation are off-limits to auto-matching.
        var manualContestKeys = (await _db.ContestMappings
            .Where(m => m.Confirmed == MappingStatus.ManualConfirmed)
            .Select(m => new { m.Platform, m.GymId })
            .ToListAsync(ct)).Select(x => (x.Platform, x.GymId)).ToHashSet();

        int auto = 0, pending = 0, skipped = 0, noCandidate = 0;
        int done = 0;

        foreach (var gym in gyms)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var title = gym.Title ?? $"Gym {gym.GymId}";
            progress?.Report(new MatchProgress(done, gyms.Count, title));

            if (manualContestKeys.Contains((gym.Platform, gym.GymId))) { skipped++; continue; }

            var prior = await _db.ContestMappings
                .Where(m => m.Platform == gym.Platform && m.GymId == gym.GymId && m.Confirmed != MappingStatus.ManualConfirmed)
                .ToListAsync(ct);

            var best = GetCuratedCandidateForCloneChain(
                gym.Platform, gym, cfGymsById, officials);
            if (best == null)
            {
                _db.ContestMappings.RemoveRange(prior);
                noCandidate++;
                continue;
            }

            auto++;

            // Drop every prior non-manual row except one for the winning official, which we update in
            // place — deleting and re-adding the same (GymId, OfficialId) key in one unit of work would
            // trip EF change tracking.
            var keep = prior.FirstOrDefault(m => m.OfficialId == best.OfficialId);
            _db.ContestMappings.RemoveRange(prior.Where(m => m.OfficialId != best.OfficialId));
            if (keep != null)
            {
                keep.Confidence = best.Confidence;
                keep.Confirmed = MappingStatus.AutoConfirmed;
            }
            else
            {
                _db.ContestMappings.Add(new ContestMapping
                {
                    Platform = gym.Platform,
                    GymId = gym.GymId,
                    OfficialId = best.OfficialId,
                    Confidence = best.Confidence,
                    Confirmed = MappingStatus.AutoConfirmed,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return new AutoMatchSummary(gyms.Count, auto, pending, skipped, noCandidate);
    }

    /// <summary>
    /// Build the mapping overview: one row per gym with its current usable mapping (manual/auto)
    /// or unmapped. Ordered by status priority then gym start time (newest first).
    /// </summary>
    public async Task<IReadOnlyList<MappingOverviewRow>> GetOverviewAsync(CancellationToken ct = default)
    {
        var gyms = await _db.GymContests
            .Where(g => g.StartTime != null || g.ProblemCount > 0)
            .OrderByDescending(g => g.StartTime ?? 0)
            .ToListAsync(ct);
        var mappings = await _db.ContestMappings
            .ToListAsync(ct);
        var officialNames = await _db.OfficialContests
            .ToDictionaryAsync(o => o.Id, o => ContestDisplayNameBuilder.BuildDisplayName(o.Id, o.Name), ct);

        var byGym = mappings
            .Where(m => m.Confirmed == MappingStatus.ManualConfirmed || m.Confirmed == MappingStatus.AutoConfirmed)
            .GroupBy(m => (m.Platform, m.GymId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var cfGymsById = gyms.Where(g => g.Platform == Platform.Codeforces).ToDictionary(g => g.GymId);

        var rows = new List<MappingOverviewRow>();
        foreach (var gym in gyms)
        {
            var title = gym.Title ?? $"Gym {gym.GymId}";
            var chainIds = gym.Platform == Platform.Codeforces
                ? GetCloneChainIds(gym, cfGymsById)
                : new[] { gym.GymId };
            List<ContestMapping>? ms = null;
            foreach (var chainGymId in chainIds)
            {
                if (byGym.TryGetValue((gym.Platform, chainGymId), out var chainMappings) &&
                    chainMappings.Count > 0)
                {
                    ms = chainMappings;
                    break;
                }
            }

            if (ms is { Count: > 0 })
            {
                // Manual first, then auto, then pending; break ties by confidence.
                var pick = ms
                    .OrderBy(m => StatusPriority(m.Confirmed))
                    .ThenByDescending(m => m.Confidence)
                    .First();
                officialNames.TryGetValue(pick.OfficialId, out var name);
                rows.Add(new MappingOverviewRow(gym.GymId, title, pick.OfficialId,
                    name ?? pick.OfficialId, pick.Confidence, pick.Confirmed, gym.Platform));
            }
            else
            {
                rows.Add(new MappingOverviewRow(gym.GymId, title, null, null, 0, MappingStatus.Unmapped, gym.Platform));
            }
        }

        return rows
            .OrderBy(r => StatusPriority(r.Status))
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }

    /// <summary>
    /// Return a <see cref="MatchCandidate"/> for a gym if it appears in the curated table.
    /// Confidence is 0.99 normally; reduced to 0.75 when both sides have a known problem count and they
    /// disagree (the mapping is still almost certainly correct, just the stored count may be stale).
    /// The <paramref name="officials"/> list is used only to surface a human-readable name.
    /// </summary>
    private static MatchCandidate? GetCuratedCandidateForCloneChain(
        Platform platform,
        GymContest sourceGym,
        IReadOnlyDictionary<long, GymContest> gymsById,
        IEnumerable<MatchOfficial> officials)
    {
        var chainIds = platform == Platform.Codeforces
            ? GetCloneChainIds(sourceGym, gymsById)
            : new[] { sourceGym.GymId };

        foreach (var chainGymId in chainIds)
        {
            var chainGym = gymsById.TryGetValue(chainGymId, out var knownGym)
                ? knownGym
                : sourceGym;
            var candidate = GetCuratedCandidate(
                platform,
                chainGymId,
                EffectiveProblemCount(chainGym, sourceGym),
                officials,
                sourceGymId: sourceGym.GymId);
            if (candidate != null)
                return candidate;
        }

        // Older local records may not have clone metadata yet. Only after every known chain node
        // misses do we use the browser-verified source-Gym fallback; this cannot override a live
        // parent/root match above.
        if (platform == Platform.Codeforces)
        {
            var fallback = GetCuratedCandidate(
                platform,
                sourceGym.GymId,
                sourceGym.ProblemCount,
                officials,
                useSourceGymAlias: true);
            if (fallback != null)
                return fallback;
        }

        return null;
    }

    private static MatchCandidate? GetCuratedCandidate(Platform platform, long gymId, int gymProblemCount,
        IEnumerable<MatchOfficial> officials, long? sourceGymId = null, bool useSourceGymAlias = false)
    {
        var entry = platform == Platform.Qoj
            ? CuratedContestMap.TryGetQoj(gymId)
            : CuratedContestMap.TryGet(gymId);
        bool sourceAlias = false;
        if (entry == null && platform == Platform.Codeforces && useSourceGymAlias)
        {
            entry = CuratedContestMap.TryGetSourceGymAlias(gymId);
            sourceAlias = entry != null;
        }
        if (entry == null) return null;

        var expectedProblemCount = platform == Platform.Qoj
            ? entry.QojExpectedProblemCount ?? entry.ExpectedProblemCount
            : entry.ExpectedProblemCount;
        bool probMismatch = expectedProblemCount > 0
                         && gymProblemCount > 0
                         && gymProblemCount != expectedProblemCount;
        double confidence = probMismatch ? 0.75 : 0.99;
        string reason = probMismatch
            ? $"curated=true platform={platform} prob_mismatch={gymProblemCount}/{expectedProblemCount}"
            : $"curated=true platform={platform} prob={gymProblemCount}/{expectedProblemCount}";
        if (sourceGymId.HasValue && sourceGymId.Value != gymId)
            reason += $" clone_chain_match={gymId}";
        if (sourceAlias)
            reason += " source_alias=true";

        // Resolve official display name from the already-fetched list when available.
        var official = officials
            .FirstOrDefault(o => string.Equals(o.OfficialId, entry.OfficialId, StringComparison.Ordinal));
        string officialName = ContestDisplayNameBuilder.BuildDisplayName(
            entry.OfficialId,
            official?.Name ?? entry.DisplayName,
            entry.DisplayName);

        return new MatchCandidate(entry.OfficialId, officialName, confidence, reason);
    }

    private static int EffectiveProblemCount(GymContest matchGym, GymContest sourceGym)
        => matchGym.ProblemCount > 0 ? matchGym.ProblemCount : sourceGym.ProblemCount;

    /// <summary>
    /// Return every known Codeforces Gym in the clone chain, starting at the source and ending at
    /// the best-known root. A curated mapping may belong to any of these nodes: training contests
    /// and ranking contests commonly clone an official contest through one or more intermediate
    /// mashups. The explicit root is retained as a fallback when an intermediate parent is not in
    /// the local database yet.
    /// </summary>
    private static IReadOnlyList<long> GetCloneChainIds(
        GymContest gym, IReadOnlyDictionary<long, GymContest> gymsById)
    {
        var ids = new List<long> { gym.GymId };
        var seen = new HashSet<long>(ids);
        var current = gym;

        while (current.CloneParentGymId is long parentId && parentId > 0)
        {
            if (!seen.Add(parentId))
                break;

            ids.Add(parentId);
            if (!gymsById.TryGetValue(parentId, out var parentGym))
            {
                AddKnownRoot(gym, ids, seen);
                break;
            }

            current = parentGym;
        }

        AddKnownRoot(current, ids, seen);
        AddKnownRoot(gym, ids, seen);
        return ids;
    }

    private static void AddKnownRoot(
        GymContest gym, ICollection<long> ids, ISet<long> seen)
    {
        if (gym.CloneRootGymId is long rootId && rootId > 0 && seen.Add(rootId))
            ids.Add(rootId);
    }

    // Overview ordering: pending first (needs attention), then unmapped, then confirmed.
    private static int StatusPriority(int status) => status switch
    {
        MappingStatus.Pending => 0,
        MappingStatus.Unmapped => 1,
        MappingStatus.AutoConfirmed => 2,
        MappingStatus.ManualConfirmed => 3,
        _ => 4,
    };

    /// <summary>
    /// Estimate the VP placement for (handle, gymId) inside its confirmed official standing.
    /// Returns null if there's no confirmed mapping or no VP result. (DESIGN 6.6)
    /// </summary>
    public async Task<VpEstimate?> EstimateAsync(string handle, long gymId, CancellationToken ct = default)
        => await EstimateAsync(Platform.Codeforces, handle, gymId, ct);

    public async Task<VpEstimate?> EstimateAsync(
        Platform platform, string handle, long gymId, CancellationToken ct = default)
    {
        var officialId = await GetConfirmedOfficialAsync(platform, gymId, ct);
        if (officialId == null) return null;

        // Estimation always ranks the VIRTUAL run — post-contest practice must never be scored as VP.
        var vp = await _db.VpResults.FindAsync(
            new object?[] { platform, handle, gymId, ParticipationKind.Virtual }, ct);
        if (vp == null) return null;

        var oc = await _db.OfficialContests.FindAsync(new object?[] { officialId }, ct);
        if (oc == null) return null;

        var standings = await _db.OfficialStandings.Where(s => s.OfficialId == officialId).ToListAsync(ct);
        if (standings.Count == 0) return null;

        int[]? medalCounts = oc.MedalRules != null
            ? JsonSerializer.Deserialize<int[]>(oc.MedalRules)
            : null;

        var estimate = EstimateFromStandings(vp.SolvedCount, vp.Penalty, standings, medalCounts);
        return estimate with
        {
            OfficialId = officialId,
            OfficialName = ContestDisplayNameBuilder.BuildDisplayName(oc.Id, oc.Name),
        };
    }

    /// <summary>
    /// Pure estimation over persisted standings: rank the VP score against official teams and
    /// apply the medal line (official counts if present, else default 10/30/60% over official teams
    /// with a valid submission). Exposed for testing.
    /// </summary>
    public VpEstimate EstimateFromStandings(int vpSolved, long vpPenalty,
        IReadOnlyList<OfficialStanding> standings, int[]? officialMedalCounts)
    {
        var vp = new ScoreEntry(vpSolved, vpPenalty);

        // Rank against official teams (fall back to all if none flagged official).
        var officialTeams = standings.Where(s => s.Official).ToList();
        var baseTeams = officialTeams.Count > 0 ? officialTeams : standings.ToList();

        int strictlyAhead = baseTeams.Count(t =>
            RankingEngine.Compare(new ScoreEntry(t.SolvedCount, t.Penalty), vp) < 0);
        int rank = strictlyAhead + 1;

        // Medal line.
        MedalLine line;
        var official = MedalRuleProvider.FromOfficialCounts(officialMedalCounts);
        if (official != null)
        {
            line = official;
        }
        else
        {
            // Base N = official teams with at least one valid submission (persisted at sync time
            // from full srk statuses, which are lossless — DESIGN 6.6).
            int n = baseTeams.Count(t => t.HadValidSubmission);
            if (n == 0) n = baseTeams.Count; // fallback if flag was never populated
            line = _medalProvider.DefaultLine(n);
        }
        var medal = line.MedalFor(rank);

        return new VpEstimate(string.Empty, string.Empty, rank, baseTeams.Count,
            vpSolved, vpPenalty, medal, line);
    }
}
