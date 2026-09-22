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
using VPHelper.Core.Rankland;
using VPHelper.Core.Srk;

namespace VPHelper.Core.Services;

/// <summary>Progress payload while syncing rankland official contests.</summary>
public record RanklandProgress(int Done, int Total, string Message);

/// <summary>
/// Fetches the algoux/srk-collection official contests and persists them as
/// <see cref="OfficialContest"/> + <see cref="OfficialStanding"/> rows (DESIGN 6.4).
/// Uses per-file timestamp caching to avoid re-pulling unchanged srk files.
/// </summary>
public class RanklandSyncService
{
    private readonly VpDbContext _db;
    private readonly RanklandClient _client;

    public RanklandSyncService(VpDbContext db, RanklandClient client)
    {
        _db = db;
        _client = client;
    }

    /// <summary>
    /// Sync only the lightweight official contest catalog from config.yaml.
    /// This persists every leaf as an <see cref="OfficialContest"/> without fetching standings.
    /// </summary>
    public async Task<int> SyncCatalogAsync(CancellationToken ct = default)
    {
        var leaves = await _client.GetLeavesAsync(ct);
        await UpsertCatalogAsync(leaves, ct);
        await _db.SaveChangesAsync(ct);
        return leaves.Count;
    }

    /// <summary>Fetch the current official catalog without modifying local persisted contests.</summary>
    public Task<IReadOnlyList<RanklandLeaf>> GetCatalogLeavesAsync(CancellationToken ct = default)
        => _client.GetLeavesAsync(ct);

    /// <summary>
    /// Sync official contests. <paramref name="maxContests"/> bounds a first run for responsiveness;
    /// pass null to fetch all leaves. Returns the number of contests persisted/refreshed.
    /// </summary>
    public async Task<int> SyncAsync(int? maxContests = null, bool forceRefresh = false,
        IProgress<RanklandProgress>? progress = null, CancellationToken ct = default)
    {
        var leaves = await _client.GetLeavesAsync(ct);
        await UpsertCatalogAsync(leaves, ct);
        await _db.SaveChangesAsync(ct);

        var selected = maxContests is { } m ? leaves.Take(m).ToList() : leaves.ToList();

        int done = 0, persisted = 0;
        foreach (var leaf in selected)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress?.Report(new RanklandProgress(done, selected.Count, leaf.Name));

            // Skip if fetched recently unless forced (24h freshness window).
            var existing = await _db.OfficialContests.FindAsync(new object?[] { leaf.Path }, ct);
            if (!forceRefresh && existing != null &&
                existing.FetchedAt > DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds())
                continue;

            ResolvedOfficial? resolved;
            try
            {
                resolved = await _client.ResolveAsync(leaf, ct: ct);
            }
            catch
            {
                continue; // skip malformed/missing leaves
            }
            if (resolved == null) continue;

            await PersistAsync(resolved, ct);
            persisted++;
        }

        await _db.SaveChangesAsync(ct);
        return persisted;
    }

    /// <summary>
    /// Fetch and persist one official contest by its srk-collection leaf id. This is used by detail
    /// pages that need the full standing for an already-mapped contest even when only the lightweight
    /// catalog has been synced so far.
    /// </summary>
    public async Task<bool> SyncOneAsync(string officialId, bool forceRefresh = false, CancellationToken ct = default)
    {
        var leaves = await _client.GetLeavesAsync(ct);
        await UpsertCatalogAsync(leaves, ct);
        await _db.SaveChangesAsync(ct);

        var leaf = leaves.FirstOrDefault(l => string.Equals(l.Path, officialId, StringComparison.Ordinal));
        if (leaf == null) return false;

        var existing = await _db.OfficialContests.FindAsync(new object?[] { officialId }, ct);
        var hasStanding = await _db.OfficialStandings.AnyAsync(s => s.OfficialId == officialId, ct);
        var hasProblems = await _db.OfficialProblems.AnyAsync(p => p.OfficialId == officialId, ct);
        if (!forceRefresh &&
            hasStanding &&
            hasProblems &&
            existing != null &&
            existing.FetchedAt > DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds())
            return true;

        ResolvedOfficial? resolved;
        try
        {
            resolved = await _client.ResolveAsync(leaf, ct: ct);
        }
        catch
        {
            return false;
        }
        if (resolved == null) return false;

        await PersistAsync(resolved, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task UpsertCatalogAsync(IReadOnlyList<RanklandLeaf> leaves, CancellationToken ct)
    {
        foreach (var leaf in leaves)
        {
            ct.ThrowIfCancellationRequested();

            var oc = await _db.OfficialContests.FindAsync(new object?[] { leaf.Path }, ct);
            if (oc == null)
            {
                oc = new OfficialContest { Id = leaf.Path };
                _db.OfficialContests.Add(oc);
            }

            oc.Name = leaf.Name;
            oc.CategoryPath = leaf.CategoryPath;
            oc.Category = ContestClassifier
                .Classify(leaf.Name, string.Join('/', leaf.PathSegments), leaf.CategoryPath)
                .ToString();
            oc.SrkUrl = RanklandClient.BuildSrkUrl(leaf);
        }
    }

    private async Task PersistAsync(ResolvedOfficial r, CancellationToken ct)
    {
        var oc = await _db.OfficialContests.FindAsync(new object?[] { r.Id }, ct);
        if (oc == null)
        {
            oc = new OfficialContest { Id = r.Id };
            _db.OfficialContests.Add(oc);
        }
        oc.Name = r.Name;
        oc.CategoryPath = r.CategoryPath;
        oc.Category = r.Category.ToString();
        oc.SrkUrl = r.SrkUrl;
        oc.StartAt = r.StartAt;
        oc.DurationSeconds = r.DurationSeconds;
        oc.ProblemCount = r.ProblemCount;
        oc.MedalRules = r.MedalCounts != null ? JsonSerializer.Serialize(r.MedalCounts) : null;
        oc.FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Replace problem metadata and standings for this contest.
        var oldProblems = await _db.OfficialProblems.Where(p => p.OfficialId == r.Id).ToListAsync(ct);
        if (oldProblems.Count > 0) _db.OfficialProblems.RemoveRange(oldProblems);
        for (int i = 0; i < r.Ranklist.Problems.Count; i++)
        {
            var problem = r.Ranklist.Problems[i];
            var alias = string.IsNullOrWhiteSpace(problem.Alias)
                ? ((char)('A' + i)).ToString()
                : problem.Alias.Trim();
            _db.OfficialProblems.Add(new OfficialProblem
            {
                OfficialId = r.Id,
                Alias = alias,
                SortOrder = i,
                Title = problem.Title,
                AcceptedCount = problem.Accepted ?? 0,
                SubmittedCount = problem.Submitted ?? 0,
            });
        }

        var old = await _db.OfficialStandings.Where(s => s.OfficialId == r.Id).ToListAsync(ct);
        if (old.Count > 0) _db.OfficialStandings.RemoveRange(old);

        // Assign ranks to the srk rows directly so per-problem detail and the
        // valid-submission flag stay tied to the exact row (team names may repeat).
        var rankByRow = RankingEngine.RankRows(r.Ranklist);
        for (int i = 0; i < r.Ranklist.Rows.Count; i++)
        {
            var row = r.Ranklist.Rows[i];
            _db.OfficialStandings.Add(new OfficialStanding
            {
                OfficialId = r.Id,
                Rank = rankByRow[i],
                TeamName = row.User.Name,
                Organization = row.User.Organization,
                SolvedCount = row.SolvedValue,
                Penalty = row.PenaltySeconds,
                Official = row.User.Official,
                HadValidSubmission = MedalRuleProvider.HasValidSubmission(row),
                PerProblem = BuildPerProblem(r.Ranklist, row),
            });
        }
    }

    private static string BuildPerProblem(SrkRanklist rl, SrkRow row)
    {
        var map = new Dictionary<string, object?>();
        for (int i = 0; i < row.Statuses.Count && i < rl.Problems.Count; i++)
        {
            var alias = rl.Problems[i].Alias ?? ((char)('A' + i)).ToString();
            var st = row.Statuses[i];
            map[alias] = new { result = st.Result, timeSeconds = st.TimeSeconds, tries = st.Tries };
        }
        return JsonSerializer.Serialize(map);
    }
}
