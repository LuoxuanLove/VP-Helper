using System;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Models;

namespace VPHelper.Core.Analysis;

/// <summary>Aggregated VP result for one account in one gym contest.</summary>
public record VpAggregate(
    string Handle,
    long GymId,
    int SolvedCount,
    long PenaltySeconds,
    IReadOnlyDictionary<string, long> FirstAcPerProblem,
    IReadOnlyDictionary<string, int> WrongAttemptsPerSolvedProblem)
{
    /// <summary>Problem indices that were solved, ordered.</summary>
    public IEnumerable<string> SolvedProblems => FirstAcPerProblem.Keys.OrderBy(k => k, StringComparer.Ordinal);
}

/// <summary>
/// Aggregates raw submissions into per-gym VP results: solved count, ICPC penalty, and
/// first-AC time per problem (DESIGN §5 VpResult, §8 Phase 1 step 2).
/// </summary>
public class Aggregator
{
    /// <summary>ICPC penalty per rejected attempt (default 20 min = 1200 s).</summary>
    public long PenaltyPerWrongSeconds { get; init; } = 20 * 60;

    /// <summary>Verdicts that count as a successful solve.</summary>
    private static readonly HashSet<string> AcceptVerdicts =
        new(StringComparer.OrdinalIgnoreCase) { "OK", "AC", "ACCEPTED" };

    /// <summary>
    /// Verdicts that do NOT incur penalty when they precede an AC (compile error, etc.),
    /// mirroring srk noPenaltyResults. Everything else before the AC is a penalized wrong attempt.
    /// </summary>
    private static readonly HashSet<string> NoPenaltyVerdicts =
        new(StringComparer.OrdinalIgnoreCase) { "COMPILATION_ERROR", "CE", "SKIPPED", "TESTING", "?", "UKE" };

    /// <summary>
    /// Aggregate submissions for a single (handle, gymId) group. The submissions may be for
    /// multiple gyms and are grouped internally; only submissions belonging to <paramref name="gymId"/>
    /// (when provided) are considered. Pass null gymId to require all inputs share one gym.
    /// </summary>
    public VpAggregate Aggregate(string handle, long gymId, IEnumerable<Submission> submissions)
    {
        // Order by submit time so the first AC per problem is deterministic.
        var subs = submissions
            .Where(s => s.GymId == gymId)
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();

        var firstAc = new Dictionary<string, long>();
        // Per-problem count of penalized wrong attempts strictly before the first AC.
        var wrongBeforeAc = new Dictionary<string, int>();
        var solvedSet = new HashSet<string>();

        foreach (var s in subs)
        {
            var idx = string.IsNullOrEmpty(s.ProblemIndex) ? "?" : s.ProblemIndex;
            if (solvedSet.Contains(idx)) continue; // already solved; ignore later submissions

            if (IsAccept(s.Verdict))
            {
                firstAc[idx] = s.SubmitTime;
                solvedSet.Add(idx);
            }
            else if (IsPenalized(s.Verdict))
            {
                wrongBeforeAc[idx] = wrongBeforeAc.GetValueOrDefault(idx) + 1;
            }
        }

        long penalty = 0;
        var wrongPerSolved = new Dictionary<string, int>();
        foreach (var idx in solvedSet)
        {
            // ICPC: only solved problems incur penalty. Time-to-solve is not derivable from
            // absolute submit time without the contest start; callers wanting time-based penalty
            // should use PenaltyFromContestStart. Here penalty = wrong-attempt penalty only.
            int wrong = wrongBeforeAc.GetValueOrDefault(idx);
            wrongPerSolved[idx] = wrong;
            penalty += wrong * PenaltyPerWrongSeconds;
        }

        return new VpAggregate(handle, gymId, solvedSet.Count, penalty, firstAc, wrongPerSolved);
    }

    /// <summary>
    /// Full ICPC penalty aligned to a known contest start: for each solved problem,
    /// penalty = (minutes from start to first AC, floored) + 20*wrongAttempts, summed.
    /// Use when the gym contest start time is known so VP penalty matches official scoring (DESIGN 6.6).
    /// </summary>
    public long PenaltyFromContestStart(VpAggregate agg, long contestStartUnix,
        IReadOnlyDictionary<string, int>? wrongAttemptsPerProblem = null)
    {
        var wrong = wrongAttemptsPerProblem ?? agg.WrongAttemptsPerSolvedProblem;
        long penalty = 0;
        foreach (var (idx, acTime) in agg.FirstAcPerProblem)
        {
            long elapsed = Math.Max(0, acTime - contestStartUnix);
            long minutes = elapsed / 60; // floor to minutes, ICPC convention
            penalty += minutes * 60;     // store penalty in seconds
            penalty += wrong.GetValueOrDefault(idx) * PenaltyPerWrongSeconds;
        }
        return penalty;
    }

    /// <summary>
    /// Full ICPC penalty using per-submission relative time from a timed participation
    /// (CF <c>relativeTimeSeconds</c>, QOJ in-contest elapsed seconds).
    /// </summary>
    public long PenaltyFromRelativeTime(VpAggregate agg, IEnumerable<Submission> submissions,
        IReadOnlyDictionary<string, int>? wrongAttemptsPerProblem = null)
    {
        var wrong = wrongAttemptsPerProblem ?? agg.WrongAttemptsPerSolvedProblem;
        var firstAcceptedSubmissions = submissions
            .Where(s => IsAccept(s.Verdict))
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .GroupBy(s => string.IsNullOrEmpty(s.ProblemIndex) ? "?" : s.ProblemIndex)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        long penalty = 0;
        foreach (var idx in agg.FirstAcPerProblem.Keys)
        {
            if (firstAcceptedSubmissions.TryGetValue(idx, out var accepted) &&
                accepted.RelativeTime is long relativeTime)
            {
                long elapsed = Math.Max(0, relativeTime);
                penalty += (elapsed / 60) * 60;
            }
            penalty += wrong.GetValueOrDefault(idx) * PenaltyPerWrongSeconds;
        }
        return penalty;
    }

    private static bool IsAccept(string? verdict)
        => verdict != null && AcceptVerdicts.Contains(verdict);

    private static bool IsPenalized(string? verdict)
        => !string.IsNullOrEmpty(verdict) && !NoPenaltyVerdicts.Contains(verdict);
}
