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
    public IReadOnlyList<string> FailedContests { get; private set; } = Array.Empty<string>();

    public async Task<int> SyncAsync(int? maxContests = null, bool forceRefresh = false,
        IProgress<RanklandProgress>? progress = null, CancellationToken ct = default)
    {
        var leaves = await _client.GetLeavesAsync(ct);
        return await SyncLeavesAsync(maxContests is { } m ? leaves.Take(m).ToList() : leaves,
            leaves, forceRefresh, progress, ct);
    }

    /// <summary>Fetch one catalog for a whole batch; download up to four independent public files.</summary>
    public async Task<int> SyncSelectedAsync(IEnumerable<string> ids, bool forceRefresh = false,
        IProgress<RanklandProgress>? progress = null, CancellationToken ct = default)
    {
        var leaves = await _client.GetLeavesAsync(ct);
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        var selected = leaves.Where(l => wanted.Contains(l.Path)).ToList();
        var count = await SyncLeavesAsync(selected, leaves, forceRefresh, progress, ct);
        FailedContests = FailedContests.Concat(wanted.Except(leaves.Select(l => l.Path))).ToList();
        return count;
    }

    public async Task<bool> SyncOneAsync(string officialId, bool forceRefresh = false, CancellationToken ct = default)
    {
        await SyncSelectedAsync(new[] { officialId }, forceRefresh, ct: ct);
        return FailedContests.Count == 0;
    }

    private async Task<int> SyncLeavesAsync(IReadOnlyList<RanklandLeaf> selected,
        IReadOnlyList<RanklandLeaf> catalog, bool forceRefresh,
        IProgress<RanklandProgress>? progress, CancellationToken ct)
    {
        await UpsertCatalogAsync(catalog, ct);
        await _db.SaveChangesAsync(ct);
        var freshSince = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var recent = (await _db.OfficialContests.Where(o => o.FetchedAt > freshSince)
            .Select(o => o.Id).ToListAsync(ct)).ToHashSet();
        var standings = (await _db.OfficialStandings.Select(s => s.OfficialId).Distinct().ToListAsync(ct)).ToHashSet();
        var problems = (await _db.OfficialProblems.Select(s => s.OfficialId).Distinct().ToListAsync(ct)).ToHashSet();
        var needed = selected.Where(l => forceRefresh || !recent.Contains(l.Path) ||
            !standings.Contains(l.Path) || !problems.Contains(l.Path)).ToList();
        var failed = new List<string>();
        int done = selected.Count - needed.Count, persisted = 0;
        progress?.Report(new RanklandProgress(done, selected.Count, "正在更新官方榜单"));
        foreach (var batch in needed.Chunk(4))
        {
            var downloads = await Task.WhenAll(batch.Select(async leaf =>
            {
                try { return await _client.ResolveAsync(leaf, ct: ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { return null; }
            }));
            for (int i = 0; i < batch.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (downloads[i] is { } resolved)
                {
                    // SaveChanges is atomic: retain the previous complete standing until replacement succeeds.
                    await PersistAsync(resolved, ct);
                    await _db.SaveChangesAsync(ct);
                    foreach (var entry in _db.ChangeTracker.Entries().Where(e =>
                        e.Entity is OfficialStanding or OfficialProblem).ToList())
                        entry.State = EntityState.Detached;
                    persisted++;
                }
                else failed.Add(batch[i].Path);
                done++;
                progress?.Report(new RanklandProgress(done, selected.Count,
                    $"{batch[i].Name} · 更新 {persisted} 场，失败 {failed.Count} 场"));
            }
        }
        FailedContests = failed;
        return persisted;
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
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < r.Ranklist.Problems.Count; i++)
        {
            var problem = r.Ranklist.Problems[i];
            var alias = string.IsNullOrWhiteSpace(problem.Alias)
                ? ((char)('A' + i)).ToString()
                : problem.Alias.Trim();
            aliases.Add(alias);
            var entity = oldProblems.FirstOrDefault(p => p.Alias == alias);
            if (entity == null)
            {
                entity = new OfficialProblem { OfficialId = r.Id, Alias = alias };
                _db.OfficialProblems.Add(entity);
            }
            entity.SortOrder = i;
            entity.Title = problem.Title;
            entity.AcceptedCount = problem.Accepted ?? 0;
            entity.SubmittedCount = problem.Submitted ?? 0;
        }
        _db.OfficialProblems.RemoveRange(oldProblems.Where(p => !aliases.Contains(p.Alias)));

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
