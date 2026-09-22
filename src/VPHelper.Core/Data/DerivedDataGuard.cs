using System.Linq;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Models;
using VPHelper.Core.Services;

namespace VPHelper.Core.Data;

/// <summary>
/// Version guard for derived local caches. Raw submissions are durable source data, while
/// GymContests/VpResults are recomputable and must be rebuilt when the aggregation algorithm
/// changes.
/// </summary>
public static class DerivedDataGuard
{
    public const int CurrentVersion = 3;

    private const string VersionKey = "derivedDataVersion";

    public static void Reconcile(VpDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        ReconcileAccountSubmissionTotals(db);

        var stored = db.AppMetadata.Find(VersionKey);
        var storedVersion = stored != null && int.TryParse(stored.Value, out var parsedVersion)
            ? parsedVersion
            : (int?)null;
        if (storedVersion >= CurrentVersion)
        {
            return;
        }

        RebuildAll(db, storedVersion);

        if (stored == null)
        {
            stored = new AppMetadata { Key = VersionKey };
            db.AppMetadata.Add(stored);
        }
        stored.Value = CurrentVersion.ToString();
        db.SaveChanges();
    }

    private static void ReconcileAccountSubmissionTotals(VpDbContext db)
    {
        var totals = db.Submissions
            .GroupBy(s => new { s.Platform, s.Handle })
            .Select(g => new { g.Key.Platform, g.Key.Handle, Count = g.Count() })
            .ToList()
            .ToDictionary(x => (x.Platform, x.Handle), x => x.Count);

        var changed = false;
        foreach (var account in db.Accounts)
        {
            var actual = totals.GetValueOrDefault((account.Platform, account.Handle));
            if (account.TotalSubmissions == actual) continue;

            account.TotalSubmissions = actual;
            changed = true;
        }

        if (changed)
            db.SaveChanges();
    }

    private static void RebuildAll(VpDbContext db, int? storedVersion)
    {
        db.VpResults.RemoveRange(db.VpResults);
        db.SaveChanges();

        foreach (var handle in db.Submissions
                     .Where(s => s.Platform == Platform.Codeforces)
                     .Select(s => s.Handle)
                     .Distinct()
                     .ToList())
        {
            new GymSyncService(db, scraper: null!).RebuildDerivedAsync(handle)
                .GetAwaiter()
                .GetResult();
        }

        foreach (var handle in db.Submissions
                     .Where(s => s.Platform == Platform.Qoj)
                     .Select(s => s.Handle)
                     .Distinct()
                     .ToList())
        {
            NormalizeStoredQojProblemIndices(db, handle, storedVersion);
            new QojSyncService(db, fetcher: null!, throttle: null!).RebuildDerivedAsync(handle)
                .GetAwaiter()
                .GetResult();
        }
    }

    private static void NormalizeStoredQojProblemIndices(VpDbContext db, string handle, int? storedVersion)
    {
        var definitions = db.QojContestProblems
            .ToList()
            .ToDictionary(p => (p.ContestId, p.ProblemId));

        if (definitions.Count == 0) return;

        foreach (var submission in db.Submissions
                     .Where(s => s.Platform == Platform.Qoj && s.Handle == handle)
                     .ToList())
        {
            var problemId = submission.ProblemId;
            // Before v3 QOJ rows did not persist ProblemId and stored the numeric feed label in
            // ProblemIndex. It is safe to recover that identifier only during this known upgrade.
            if (string.IsNullOrWhiteSpace(problemId) && storedVersion is < CurrentVersion)
                problemId = submission.ProblemIndex;
            if (string.IsNullOrWhiteSpace(problemId) ||
                !definitions.TryGetValue((submission.GymId, problemId), out var definition))
                continue;

            submission.ProblemId = definition.ProblemId;
            submission.ProblemIndex = definition.ProblemIndex;
            submission.ProblemName = definition.Title;
            submission.ProblemUrl = definition.ProblemUrl;
        }

        db.SaveChanges();
    }
}
