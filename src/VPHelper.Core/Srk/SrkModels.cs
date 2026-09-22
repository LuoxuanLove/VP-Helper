using System.Collections.Generic;

namespace VPHelper.Core.Srk;

/// <summary>Parsed srk (standard-ranklist) document, normalized (times in seconds).</summary>
public class SrkRanklist
{
    public string? Type { get; set; }
    public string? Version { get; set; }
    public SrkContest Contest { get; set; } = new();
    public List<SrkProblem> Problems { get; set; } = new();
    public List<SrkRow> Rows { get; set; } = new();
    public SrkSorter? Sorter { get; set; }
    /// <summary>series[] with medal-count rules (may carry gold/silver/bronze counts).</summary>
    public List<SrkSeries> Series { get; set; } = new();
}

public class SrkContest
{
    public string Title { get; set; } = string.Empty;
    /// <summary>Unix seconds, or null if unparseable.</summary>
    public long? StartAt { get; set; }
    /// <summary>Contest duration in seconds.</summary>
    public long? DurationSeconds { get; set; }
    public long? FrozenDurationSeconds { get; set; }
}

public class SrkProblem
{
    public string? Alias { get; set; }
    public string? Title { get; set; }
    public int? Accepted { get; set; }
    public int? Submitted { get; set; }
}

public class SrkRow
{
    public SrkUser User { get; set; } = new();
    /// <summary>Number of solved problems (score.value).</summary>
    public int SolvedValue { get; set; }
    /// <summary>Penalty / total time in seconds (score.time normalized).</summary>
    public long PenaltySeconds { get; set; }
    /// <summary>Per-problem statuses, aligned to <see cref="SrkRanklist.Problems"/> order.</summary>
    public List<SrkProblemStatus> Statuses { get; set; } = new();
}

public class SrkUser
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public bool Official { get; set; }
    public List<string>? Markers { get; set; }
}

public class SrkProblemStatus
{
    /// <summary>'FB' | 'AC' | 'RJ' | '?' | 'CE' | ... | null.</summary>
    public string? Result { get; set; }
    /// <summary>Time of AC in seconds (if solved).</summary>
    public long? TimeSeconds { get; set; }
    public int Tries { get; set; }
}

public class SrkSorter
{
    public string? Algorithm { get; set; }
    /// <summary>Penalty per wrong submission in seconds (ICPC config.penalty).</summary>
    public long? PenaltyPerWrongSeconds { get; set; }
    /// <summary>Results that incur no penalty time (e.g. FB, AC, ?, CE, UKE, null).</summary>
    public List<string?> NoPenaltyResults { get; set; } = new();
    /// <summary>Time precision unit for penalty rounding: "s" or "min".</summary>
    public string? TimePrecision { get; set; }
    /// <summary>"floor" | "round" | "ceil".</summary>
    public string? TimeRounding { get; set; }
}

/// <summary>A ranking series; the primary one may carry medal counts [gold, silver, bronze].</summary>
public class SrkSeries
{
    public string? Title { get; set; }
    public string? Preset { get; set; }
    /// <summary>Medal counts [gold, silver, bronze] from rule.options.count.value; null if absent/scalar.</summary>
    public int[]? MedalCounts { get; set; }
    /// <summary>Optional marker filter (e.g. "provincial") restricting which rows this series ranks.</summary>
    public string? FilterByMarker { get; set; }
}
