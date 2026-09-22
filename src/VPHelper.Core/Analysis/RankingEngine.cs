using System;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Models;
using VPHelper.Core.Srk;

namespace VPHelper.Core.Analysis;

/// <summary>A team's (solved, penalty) score used for ICPC ranking.</summary>
public record ScoreEntry(int Solved, long PenaltySeconds);

public record RankedTeam(int Rank, string TeamName, string? Organization, int Solved, long PenaltySeconds, bool Official);

public record RankedOfficialStanding(int Rank, OfficialStanding Standing);

/// <summary>Result of inserting a VP score into an official standing.</summary>
public record VpInsertionResult(
    int Rank,
    int OfficialTeamsAbove,
    int TotalTeams,
    Medal Medal,
    MedalLine MedalLine);

/// <summary>
/// ICPC-style ranking: more solved first, then less penalty. Provides insertion of a VP
/// score to estimate the rank the account would have achieved live (DESIGN 6.6).
/// </summary>
public static class RankingEngine
{
    /// <summary>Compare two scores by ICPC rule. Returns negative if a ranks strictly ahead of b.</summary>
    public static int Compare(ScoreEntry a, ScoreEntry b)
    {
        if (a.Solved != b.Solved) return b.Solved - a.Solved; // more solved first
        return a.PenaltySeconds.CompareTo(b.PenaltySeconds);   // less penalty first
    }

    /// <summary>
    /// Assign 1-based ICPC ranks, returning an array aligned to the original
    /// <see cref="SrkRanklist.Rows"/> order (so callers can tie ranks back to the exact row).
    /// Ties share the higher rank; the next distinct score skips accordingly.
    /// </summary>
    public static int[] RankRows(SrkRanklist ranklist)
    {
        var indexed = ranklist.Rows
            .Select((r, i) => new
            {
                Index = i,
                Score = new ScoreEntry(r.SolvedValue, r.PenaltySeconds)
            })
            .OrderBy(x => x.Score.Solved == 0 ? 1 : 0)
            .ThenByDescending(x => x.Score.Solved)
            .ThenBy(x => x.Score.PenaltySeconds)
            .ToList();

        var ranks = new int[ranklist.Rows.Count];
        int rank = 0, seen = 0;
        ScoreEntry? prev = null;
        foreach (var x in indexed)
        {
            seen++;
            if (prev == null || Compare(prev, x.Score) != 0)
                rank = seen;
            prev = x.Score;
            ranks[x.Index] = rank;
        }
        return ranks;
    }

    /// <summary>Order rows by ICPC rule and assign 1-based ranks (ties share the higher rank, next rank skips).</summary>
    public static List<RankedTeam> RankStandings(SrkRanklist ranklist)
    {
        var ordered = ranklist.Rows
            .Select(r => new
            {
                Row = r,
                Score = new ScoreEntry(r.SolvedValue, r.PenaltySeconds)
            })
            .OrderBy(x => x.Score.Solved == 0 ? 1 : 0) // zero-solve teams sink, but keep stable within
            .ThenByDescending(x => x.Score.Solved)
            .ThenBy(x => x.Score.PenaltySeconds)
            .ToList();

        var result = new List<RankedTeam>(ordered.Count);
        int rank = 0, seen = 0;
        ScoreEntry? prev = null;
        foreach (var x in ordered)
        {
            seen++;
            if (prev == null || Compare(prev, x.Score) != 0)
                rank = seen;
            prev = x.Score;
            result.Add(new RankedTeam(rank, x.Row.User.Name, x.Row.User.Organization,
                x.Score.Solved, x.Score.PenaltySeconds, x.Row.User.Official));
        }
        return result;
    }

    /// <summary>
    /// Rank persisted standings by the same participant set used for VP estimation: official
    /// teams when present, otherwise all rows. This avoids mixing srk source row ranks with the
    /// filtered official-team rank shown in the UI.
    /// </summary>
    public static List<RankedOfficialStanding> RankPersistedStandings(
        IReadOnlyList<OfficialStanding> standings,
        bool officialTeamsOnly = true)
    {
        var officialRows = officialTeamsOnly
            ? standings.Where(s => s.Official).ToList()
            : new List<OfficialStanding>();
        var baseRows = officialRows.Count > 0 ? officialRows : standings.ToList();

        var ordered = baseRows
            .OrderByDescending(s => s.SolvedCount)
            .ThenBy(s => s.Penalty)
            .ThenBy(s => s.Rank)
            .ToList();

        var result = new List<RankedOfficialStanding>(ordered.Count);
        int rank = 0, seen = 0;
        ScoreEntry? previous = null;
        foreach (var row in ordered)
        {
            seen++;
            var score = new ScoreEntry(row.SolvedCount, row.Penalty);
            if (previous == null || Compare(previous, score) != 0)
                rank = seen;
            previous = score;
            result.Add(new RankedOfficialStanding(rank, row));
        }

        return result;
    }

    /// <summary>
    /// Insert a VP score into the official standings and compute the rank it would take.
    /// Rank = 1 + number of teams that rank strictly ahead of the VP score.
    /// The VP entry does not displace real teams from the base count.
    /// </summary>
    public static VpInsertionResult InsertVp(
        SrkRanklist ranklist,
        ScoreEntry vp,
        MedalRuleProvider medalProvider,
        int[]? officialMedalCounts = null,
        bool officialTeamsOnly = true)
    {
        // Base teams for ranking: DESIGN says rank against real teams; use official teams when available.
        var baseRows = officialTeamsOnly
            ? ranklist.Rows.Where(r => r.User.Official).ToList()
            : ranklist.Rows.ToList();
        if (baseRows.Count == 0) baseRows = ranklist.Rows.ToList();

        int strictlyAhead = 0;
        int officialAhead = 0;
        foreach (var r in baseRows)
        {
            var score = new ScoreEntry(r.SolvedValue, r.PenaltySeconds);
            if (Compare(score, vp) < 0) // this team ranks strictly ahead of the VP score
            {
                strictlyAhead++;
                if (r.User.Official) officialAhead++;
            }
        }

        int rank = strictlyAhead + 1;
        // Medal line: prefer an explicit official count, else the srk series line, else default 10/30/60%.
        var line = ResolveMedalLine(ranklist, medalProvider, officialMedalCounts);
        var medal = line.MedalFor(rank);
        return new VpInsertionResult(rank, officialAhead, baseRows.Count, medal, line);
    }

    /// <summary>Prefer explicit official counts, then srk series counts, else the default-rule line.</summary>
    public static MedalLine ResolveMedalLine(
        SrkRanklist ranklist, MedalRuleProvider medalProvider, int[]? officialMedalCounts)
    {
        var explicitLine = MedalRuleProvider.FromOfficialCounts(officialMedalCounts);
        if (explicitLine != null) return explicitLine;
        var seriesLine = medalProvider.FromSrkSeries(ranklist);
        if (seriesLine != null) return seriesLine;
        return medalProvider.DefaultLine(MedalRuleProvider.EffectiveOfficialTeamCount(ranklist));
    }
}
