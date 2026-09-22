using Microsoft.EntityFrameworkCore;
using VPHelper.Core.Data;
using VPHelper.Core.Models;

namespace VPHelper.Core.Services;

/// <summary>A local run and its placement in a confirmed official contest.</summary>
public sealed record OfficialParticipation(
    string OfficialId, Platform Platform, long GymId, string Handle,
    ParticipationKind Kind, int Solved, long Penalty, int? Rank, int? TotalTeams);

/// <summary>Builds a local-only directory projection without downloading standings per row.</summary>
public sealed class OfficialParticipationService(VpDbContext db)
{
    public async Task<IReadOnlyList<OfficialParticipation>> LoadAsync(CancellationToken ct = default)
    {
        var mappings = (await db.ContestMappings.AsNoTracking()
                .Where(m => m.Confirmed == MappingStatus.ManualConfirmed || m.Confirmed == MappingStatus.AutoConfirmed)
                .ToListAsync(ct))
            .GroupBy(m => (m.Platform, m.GymId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Confirmed == MappingStatus.ManualConfirmed)
                .ThenByDescending(m => m.Confidence).First().OfficialId);
        var results = await db.VpResults.AsNoTracking().ToListAsync(ct);
        var officialIds = mappings.Values.Distinct().ToList();
        var standings = (await db.OfficialStandings.AsNoTracking()
                .Where(s => officialIds.Contains(s.OfficialId))
                .Select(s => new OfficialStanding
                {
                    OfficialId = s.OfficialId, SolvedCount = s.SolvedCount, Penalty = s.Penalty,
                    Official = s.Official, HadValidSubmission = s.HadValidSubmission
                }).ToListAsync(ct))
            .ToLookup(s => s.OfficialId);
        var estimator = new MappingService(db);
        return results.Select(v =>
        {
            mappings.TryGetValue((v.Platform, v.GymId), out var officialId);
            var rows = officialId == null ? [] : standings[officialId].ToList();
            var estimate = rows.Count > 0 && v.Kind == ParticipationKind.Virtual
                ? estimator.EstimateFromStandings(v.SolvedCount, v.Penalty, rows, null) : null;
            return new OfficialParticipation(officialId ?? string.Empty, v.Platform, v.GymId, v.Handle,
                v.Kind, v.SolvedCount, v.Penalty, estimate?.Rank, estimate?.TotalTeams);
        }).ToList();
    }
}
