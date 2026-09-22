using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Analysis;
using VPHelper.Core.Models;
using VPHelper.Core.Services;

namespace VPHelper.Core.Data;

/// <summary>
/// Version guard for the built-in Gym -> official contest directory. The curated directory is a
/// source of confirmed mappings, so older local databases should receive newly added entries
/// without requiring the user to press "auto match" again. Manual mappings always win.
/// </summary>
public static class CuratedMappingGuard
{
    public const int CurrentVersion = 11;

    private const string VersionKey = "curatedMappingVersion";

    private static readonly IReadOnlyDictionary<long, string[]> RetiredAutoMappings =
        new Dictionary<long, string[]>
        {
            // Gym 106139 is the 2026 Hunan provincial contest. srk-collection currently has no
            // 2026 Hunan official standing, so old non-manual guesses to hncpc20th must be removed.
            [106139L] = ["hncpc20th"],
        };

    // These QOJ contests are shared problem sets or Universal Cup mirrors. They are useful
    // directory links, but QOJ submissions alone cannot identify which aliased official event
    // the user meant; remove any auto-mapping written by an older curated table.
    private static readonly IReadOnlyDictionary<long, string[]> RetiredQojAutoMappings =
        new Dictionary<long, string[]>
        {
            [1780L] = ["zjcpc21st"],
            [2021L] = ["zjcpc22nd"],
            [2058L] = ["gdcpc22nd"],
            [2521L] = ["jxcpc2025"],
            [3588L] = ["icpc2026invitational-shenzhen"],
            [3749L] = ["zjcpc23rd"],
            [3756L] = ["gdcpc23rd"],
            [3766L] = ["icpc2026invitational-xi_an"],
        };

    public static void Reconcile(VpDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        var stored = db.AppMetadata.Find(VersionKey);
        if (stored != null &&
            int.TryParse(stored.Value, out var version) &&
            version >= CurrentVersion)
        {
            return;
        }

        ApplyCuratedMappings(db);

        if (stored == null)
        {
            stored = new AppMetadata { Key = VersionKey };
            db.AppMetadata.Add(stored);
        }

        stored.Value = CurrentVersion.ToString();
        db.SaveChanges();
    }

    private static void ApplyCuratedMappings(VpDbContext db)
    {
        var gyms = db.GymContests
            .Where(g => g.Platform == Platform.Codeforces || g.Platform == Platform.Qoj)
            .ToList();
        var mappings = db.ContestMappings
            .Where(m => m.Platform == Platform.Codeforces || m.Platform == Platform.Qoj)
            .ToList();
        var byContest = mappings.GroupBy(m => (m.Platform, m.GymId))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var gym in gyms)
        {
            CuratedEntry? entry;
            if (gym.Platform == Platform.Codeforces)
                CuratedContestMap.GymMappings.TryGetValue(gym.GymId, out entry);
            else
                entry = CuratedContestMap.TryGetQoj(gym.GymId);

            if (entry == null)
                continue;

            byContest.TryGetValue((gym.Platform, gym.GymId), out var existing);
            if (existing?.Any(m => m.Confirmed == MappingStatus.ManualConfirmed) == true)
                continue;

            var keep = existing?.FirstOrDefault(m => m.OfficialId == entry.OfficialId);
            if (existing != null)
                db.ContestMappings.RemoveRange(existing.Where(m => m.OfficialId != entry.OfficialId));

            if (keep != null)
            {
                keep.Confidence = 0.99;
                keep.Confirmed = MappingStatus.AutoConfirmed;
            }
            else
            {
                db.ContestMappings.Add(new ContestMapping
                {
                    Platform = gym.Platform,
                    GymId = gym.GymId,
                    OfficialId = entry.OfficialId,
                    Confidence = 0.99,
                    Confirmed = MappingStatus.AutoConfirmed,
                });
            }
        }

        var codeforcesGymIds = gyms
            .Where(g => g.Platform == Platform.Codeforces)
            .Select(g => g.GymId)
            .ToHashSet();
        RemoveRetiredAutoMappings(db, Platform.Codeforces, codeforcesGymIds, RetiredAutoMappings);

        var qojContestIds = gyms
            .Where(g => g.Platform == Platform.Qoj)
            .Select(g => g.GymId)
            .ToHashSet();
        RemoveRetiredAutoMappings(db, Platform.Qoj, qojContestIds, RetiredQojAutoMappings);
    }

    private static void RemoveRetiredAutoMappings(
        VpDbContext db,
        Platform platform,
        HashSet<long> contestIds,
        IReadOnlyDictionary<long, string[]> retiredMappings)
    {
        foreach (var (contestId, retiredOfficialIds) in retiredMappings)
        {
            if (!contestIds.Contains(contestId))
                continue;

            var rows = db.ContestMappings
                .Where(m => m.Platform == platform &&
                            m.GymId == contestId &&
                            m.Confirmed != MappingStatus.ManualConfirmed)
                .ToList();
            rows = rows
                .Where(m => retiredOfficialIds.Contains(m.OfficialId))
                .ToList();
            if (rows.Count > 0)
                db.ContestMappings.RemoveRange(rows);
        }
    }

}
