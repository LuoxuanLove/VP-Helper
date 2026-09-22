using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;

namespace VPHelper.Core.Services;

/// <summary>The VP result chosen to represent one contest in the combined, all-accounts view.</summary>
public record GymVpChoice(Platform Platform, long GymId, string Handle, int SolvedCount, long Penalty);

/// <summary>
/// A contest virtual-participated by more than one account. Only one account's run can count toward
/// the combined total (the same contest cannot be double-counted), so the user must pick which —
/// this carries the candidates and the currently selected handle (null ⇒ unresolved).
/// </summary>
public record GymVpConflict(Platform Platform, long GymId, string? GymTitle, IReadOnlyList<GymVpChoice> Candidates, string? SelectedHandle)
{
    /// <summary>True while no valid selection has been made among the candidates.</summary>
    public bool IsUnresolved => SelectedHandle == null || Candidates.All(c => c.Handle != SelectedHandle);
}

/// <summary>
/// Combines VP results across all tracked accounts (requirement: 所有账号的过题同时统计). A contest is
/// counted once, using the <see cref="ParticipationKind.Virtual"/> result of exactly one account:
///  • zero accounts VP'd it ⇒ not counted (practice-only contests contribute nothing to the VP total);
///  • one account VP'd it ⇒ that account counts automatically;
///  • more than one ⇒ a <see cref="GymVpConflict"/> the user resolves via <see cref="GymVpSelection"/>.
/// Until a conflict is resolved, that contest is excluded from the combined total rather than guessed.
///
/// Contests are keyed by <c>(Platform, GymId)</c> throughout: a Codeforces gym id and a QOJ contest
/// id are both small integers that collide, so keying by the bare id would silently merge two
/// unrelated contests into one combined entry.
/// </summary>
public class CombinedStatsService
{
    private readonly VpDbContext _db;

    public CombinedStatsService(VpDbContext db) => _db = db;

    /// <summary>
    /// Group every account's Virtual VP result by contest. Returns, per <c>(Platform, GymId)</c>, the
    /// list of candidate runs (one per account that virtual-participated) ordered best-first (more
    /// solved, then less penalty).
    /// </summary>
    private async Task<Dictionary<(Platform Platform, long GymId), List<GymVpChoice>>> LoadVirtualByGymAsync(CancellationToken ct)
    {
        var virtuals = await _db.VpResults
            .Where(v => v.Kind == ParticipationKind.Virtual && v.SolvedCount > 0)
            .ToListAsync(ct);

        var byGym = new Dictionary<(Platform, long), List<GymVpChoice>>();
        foreach (var v in virtuals)
        {
            var key = (v.Platform, v.GymId);
            if (!byGym.TryGetValue(key, out var list))
                byGym[key] = list = new List<GymVpChoice>();
            list.Add(new GymVpChoice(v.Platform, v.GymId, v.Handle, v.SolvedCount, v.Penalty));
        }
        foreach (var list in byGym.Values)
            list.Sort((a, b) => a.SolvedCount != b.SolvedCount
                ? b.SolvedCount.CompareTo(a.SolvedCount)     // more solved first
                : a.Penalty.CompareTo(b.Penalty));           // then lower penalty
        return byGym;
    }

    /// <summary>
    /// Resolve which account's Virtual run counts for each contest, honoring the user's saved
    /// selections and auto-resolving single-candidate contests. Contests with an unresolved
    /// multi-account conflict are omitted (see <see cref="GetConflictsAsync"/> to surface them).
    /// </summary>
    public async Task<IReadOnlyList<GymVpChoice>> GetChosenPerGymAsync(CancellationToken ct = default)
    {
        var byGym = await LoadVirtualByGymAsync(ct);
        var selections = await _db.GymVpSelections
            .ToDictionaryAsync(s => (s.Platform, s.GymId), s => s.Handle, ct);

        var chosen = new List<GymVpChoice>();
        foreach (var (key, candidates) in byGym)
        {
            if (candidates.Count == 1)
            {
                chosen.Add(candidates[0]);
                continue;
            }
            // Multi-account: only counts if the user picked a still-valid candidate.
            if (selections.TryGetValue(key, out var handle))
            {
                var pick = candidates.FirstOrDefault(c => c.Handle == handle);
                if (pick != null) chosen.Add(pick);
            }
            // else: unresolved conflict, excluded from the total.
        }
        return chosen;
    }

    /// <summary>
    /// The combined problems-solved total across all accounts: the sum of the chosen run's solved
    /// count per contest (each contest counted once).
    /// </summary>
    public async Task<int> GetCombinedSolvedAsync(CancellationToken ct = default)
        => (await GetChosenPerGymAsync(ct)).Sum(c => c.SolvedCount);

    /// <summary>
    /// Sum chosen virtual-participation solves per tracked account identity. The platform is part
    /// of the key because the same handle can name distinct Codeforces and QOJ accounts.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Platform Platform, string Handle), int>>
        GetSolvedByAccountAsync(CancellationToken ct = default)
        => (await GetChosenPerGymAsync(ct))
            .GroupBy(c => (c.Platform, c.Handle))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.SolvedCount));

    /// <summary>
    /// Count persisted raw submissions per account. The submissions table is the source of truth;
    /// the cached account total may be stale after an older client version staged new rows.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Platform Platform, string Handle), int>>
        GetSubmissionCountByAccountAsync(CancellationToken ct = default)
    {
        var counts = await _db.Submissions
            .GroupBy(s => new { s.Platform, s.Handle })
            .Select(g => new { g.Key.Platform, g.Key.Handle, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(
            x => (x.Platform, x.Handle),
            x => x.Count);
    }

    /// <summary>
    /// Contests virtual-participated by more than one account, with their candidates and current
    /// selection. Includes both resolved and unresolved conflicts so the UI can show and let the user
    /// change any of them.
    /// </summary>
    public async Task<IReadOnlyList<GymVpConflict>> GetConflictsAsync(CancellationToken ct = default)
    {
        var byGym = await LoadVirtualByGymAsync(ct);
        var conflictGyms = byGym.Where(kv => kv.Value.Count > 1).ToList();
        if (conflictGyms.Count == 0) return Array.Empty<GymVpConflict>();

        var selections = await _db.GymVpSelections
            .ToDictionaryAsync(s => (s.Platform, s.GymId), s => s.Handle, ct);

        // Titles are looked up per contest. GymContest is also keyed by (Platform, GymId), so build
        // the same-shaped lookup rather than filtering on the bare id (which would collide).
        var gyms = await _db.GymContests.ToListAsync(ct);
        var titles = gyms.ToDictionary(g => (g.Platform, g.GymId), g => g.Title);

        var result = new List<GymVpConflict>();
        foreach (var (key, candidates) in conflictGyms)
        {
            selections.TryGetValue(key, out var selected);
            titles.TryGetValue(key, out var title);
            result.Add(new GymVpConflict(key.Platform, key.GymId, title, candidates, selected));
        }
        // Unresolved first so the user sees what still needs a decision.
        return result
            .OrderByDescending(c => c.IsUnresolved)
            .ThenByDescending(c => c.Platform)
            .ThenByDescending(c => c.GymId)
            .ToList();
    }

    /// <summary>Whether any contest has an unresolved multi-account VP conflict.</summary>
    public async Task<bool> HasUnresolvedConflictsAsync(CancellationToken ct = default)
        => (await GetConflictsAsync(ct)).Any(c => c.IsUnresolved);

    /// <summary>
    /// Record the user's choice of which account's Virtual run counts for a contest (idempotent upsert).
    /// </summary>
    public async Task SelectAsync(Platform platform, long gymId, string handle, CancellationToken ct = default)
    {
        var existing = await _db.GymVpSelections.FindAsync(new object?[] { platform, gymId }, ct);
        if (existing == null)
            _db.GymVpSelections.Add(new GymVpSelection { Platform = platform, GymId = gymId, Handle = handle });
        else
            existing.Handle = handle;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Delete an account and everything derived from it: its submissions, its VP/practice results,
    /// and any conflict selection that pointed at it (so the contest falls back to auto/unresolved).
    /// Scoped to the account's platform, since the same handle can name accounts on both judges.
    /// Callers should rebuild affected combined views afterward.
    /// </summary>
    public async Task RemoveAccountAsync(Platform platform, string handle, CancellationToken ct = default)
    {
        var subs = _db.Submissions.Where(s => s.Platform == platform && s.Handle == handle);
        _db.Submissions.RemoveRange(subs);

        var vps = _db.VpResults.Where(v => v.Platform == platform && v.Handle == handle);
        _db.VpResults.RemoveRange(vps);

        var sels = _db.GymVpSelections.Where(s => s.Platform == platform && s.Handle == handle);
        _db.GymVpSelections.RemoveRange(sels);

        var acct = await _db.Accounts.FindAsync(new object?[] { platform, handle }, ct);
        if (acct != null) _db.Accounts.Remove(acct);

        await _db.SaveChangesAsync(ct);
    }
}
