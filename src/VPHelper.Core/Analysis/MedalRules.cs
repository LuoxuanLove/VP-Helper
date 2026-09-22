using System;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Srk;

namespace VPHelper.Core.Analysis;

public enum Medal { None, Bronze, Silver, Gold }

/// <summary>Gold/silver/bronze rank thresholds (rank &lt;= threshold earns that medal). Cumulative.</summary>
public record MedalLine(int Gold, int Silver, int Bronze)
{
    /// <summary>Which medal a given 1-based rank earns.</summary>
    public Medal MedalFor(int rank)
    {
        if (rank <= 0) return Medal.None;
        if (rank <= Gold) return Medal.Gold;
        if (rank <= Silver) return Medal.Silver;
        if (rank <= Bronze) return Medal.Bronze;
        return Medal.None;
    }
}

/// <summary>
/// Computes medal lines. Prefers an official line (from srk series medal counts,
/// or manually supplied); otherwise falls back to the default 10/30/60% rule over
/// official teams that made at least one valid submission (DESIGN 6.6).
/// </summary>
public class MedalRuleProvider
{
    public double GoldRatio { get; init; } = 0.10;
    public double SilverRatio { get; init; } = 0.30;
    public double BronzeRatio { get; init; } = 0.60;

    /// <summary>
    /// Determine the medal line for a ranklist.
    /// <paramref name="officialCounts"/>: optional [gold, silver, bronze] counts (from srk series or manual).
    /// A non-null array with a positive gold count is treated as an authoritative official line.
    /// </summary>
    public MedalLine Compute(SrkRanklist ranklist, int[]? officialCounts = null)
    {
        var official = FromOfficialCounts(officialCounts);
        if (official != null) return official;
        return DefaultLine(EffectiveOfficialTeamCount(ranklist));
    }

    /// <summary>
    /// Try to read an authoritative overall medal line from srk series. Only <b>unfiltered</b>
    /// series (no marker filter) count as the overall line: marker-filtered series such as
    /// <c>byMarker: university / provincial / pss</c> are sub-category awards (e.g. a provincial
    /// contest's separate university and primary-school medal counts) and must not override the
    /// overall line. Returns the first unfiltered series carrying positive counts.
    /// </summary>
    public MedalLine? FromSrkSeries(SrkRanklist ranklist)
    {
        foreach (var s in ranklist.Series)
        {
            if (!string.IsNullOrEmpty(s.FilterByMarker)) continue; // sub-category award, not the overall line
            var line = FromOfficialCounts(s.MedalCounts);
            if (line != null) return line;
        }
        return null;
    }

    /// <summary>
    /// Convert srk medal counts [gold, silver, bronze] (per-medal, non-cumulative) to a cumulative
    /// <see cref="MedalLine"/>. Returns null if counts are absent or all zero ([0,0,0] = unspecified).
    /// </summary>
    public static MedalLine? FromOfficialCounts(int[]? counts)
    {
        if (counts == null || counts.Length < 3) return null;
        int g = counts[0], s = counts[1], b = counts[2];
        if (g <= 0 && s <= 0 && b <= 0) return null; // [0,0,0] means unspecified
        // srk counts are per-band; medal line thresholds are cumulative ranks.
        return new MedalLine(g, g + s, g + s + b);
    }

    /// <summary>Default 10/30/60% ceil rule over an official-team base N.</summary>
    public MedalLine DefaultLine(int n)
    {
        if (n <= 0) return new MedalLine(0, 0, 0);
        int gold = (int)Math.Ceiling(n * GoldRatio);
        int silver = (int)Math.Ceiling(n * SilverRatio);
        int bronze = (int)Math.Ceiling(n * BronzeRatio);
        return new MedalLine(gold, silver, bronze);
    }

    /// <summary>
    /// Base N = number of official (user.official==true) teams with at least one valid submission
    /// (a status with a non-empty result). Pure zero/no-submission teams are excluded.
    /// </summary>
    public static int EffectiveOfficialTeamCount(SrkRanklist ranklist)
        => ranklist.Rows.Count(r => r.User.Official && HasValidSubmission(r));

    public static bool HasValidSubmission(SrkRow row)
        => row.Statuses.Any(s => !string.IsNullOrEmpty(s.Result));
}
