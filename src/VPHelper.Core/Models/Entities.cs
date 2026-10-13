using System.Collections.Generic;

namespace VPHelper.Core.Models;

/// <summary>
/// The judge a contest/submission comes from. Part of the identity of every contest-scoped and
/// account-scoped entity: Codeforces gym ids and QOJ contest ids are both small integers that
/// collide (as do their submission ids), and the same person often reuses one handle on both
/// sites, so <c>(Platform, …)</c> is what actually identifies a row. Enum values are persisted;
/// do not renumber. Default 0 = <see cref="Codeforces"/>, so rows written before this field
/// existed read back as Codeforces.
/// </summary>
public enum Platform
{
    Codeforces = 0,
    Qoj = 1,
}

/// <summary>
/// How a submission participated in a gym contest. Derived from the CF API
/// <c>author.participantType</c> (only <c>contest.status</c> exposes this — the HTML submissions
/// page does not tag rows), and used to keep a real virtual run separate from post-contest
/// practice grinding.
///  • <see cref="Virtual"/> — a timed run counting as VP for standings (CF VIRTUAL, and a genuine
///    ranked CONTESTANT participation, both of which carry a participation start time).
///  • <see cref="Practice"/> — everything else (CF PRACTICE / OUT_OF_COMPETITION / MANAGER …):
///    upsolving that must never be mixed into the VP result.
/// </summary>
public enum ParticipationKind
{
    Virtual = 0,
    Practice = 1,
}

/// <summary>
/// A tracked account on one judge. Keyed by <c>(Platform, Handle)</c>: the same handle can name a
/// different person's account on a different judge, and here the same person genuinely reuses one
/// handle across Codeforces and QOJ, so the two must be distinct rows with independent watermarks.
/// <c>LastSyncedAt</c> is the incremental-sync watermark.
/// </summary>
public class Account
{
    /// <summary>The judge this account lives on. Part of the key.</summary>
    public Platform Platform { get; set; }
    public string Handle { get; set; } = string.Empty;
    /// <summary>Unix seconds of the newest submission seen at last sync (incremental watermark).</summary>
    public long? LastSyncedAt { get; set; }
    /// <summary>Wall-clock completion time, independent of the newest-submission watermark.</summary>
    public long? LastCompletedAt { get; set; }
    /// <summary>New submissions stored in the most recent sync run (0 = nothing new / no run yet).</summary>
    public int LastSyncCount { get; set; }
    /// <summary>Error message from the last sync run; null = last sync succeeded or never ran.</summary>
    public string? LastSyncError { get; set; }
    /// <summary>Total submissions ever stored for this account (not decremented on partial re-sync).</summary>
    public int TotalSubmissions { get; set; }
}

/// <summary>A raw submission row for a contest (from HTML discovery + CF API enrichment).</summary>
public class Submission
{
    /// <summary>The judge this submission comes from. Part of the key (submission ids collide across judges).</summary>
    public Platform Platform { get; set; }
    /// <summary>Submission id, unique only within its platform.</summary>
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    /// <summary>Contest id this submission belongs to (CF gym id / QOJ contest id).</summary>
    public long GymId { get; set; }
    /// <summary>
    /// Stable judge-native problem identity. Codeforces does not need a separate identity because
    /// its contest index is stable; QOJ uses its numeric problem id here because the account feed
    /// renders that id while the contest dashboard renders the letter used by the ranklist.
    /// </summary>
    public string? ProblemId { get; set; }
    public string ProblemIndex { get; set; } = string.Empty;
    public string? ProblemName { get; set; }
    /// <summary>Direct upsolve/problem URL when the platform exposes one.</summary>
    public string? ProblemUrl { get; set; }
    public string Verdict { get; set; } = string.Empty;
    /// <summary>Submission time, unix seconds.</summary>
    public long SubmitTime { get; set; }
    /// <summary>
    /// CF <c>author.participantType</c> verbatim (VIRTUAL / PRACTICE / CONTESTANT / …) when known
    /// from the API, else null (e.g. an HTML-only row from a legacy sync). Classified into
    /// <see cref="ParticipationKind"/> at aggregation time.
    /// </summary>
    public string? ParticipantType { get; set; }
    /// <summary>
    /// Seconds from the participation start to this submission (CF <c>relativeTimeSeconds</c>).
    /// Meaningful for a timed run (VIRTUAL / CONTESTANT); used to compute time-based VP penalty
    /// without needing a separate contest-start lookup. Null when unknown.
    /// </summary>
    public long? RelativeTime { get; set; }
}

/// <summary>
/// A contest aggregated from scraped submissions. Keyed by <c>(Platform, GymId)</c> — the
/// <c>GymId</c> is the platform's own contest id (CF gym id / QOJ contest id), which collides
/// across judges, so the platform is part of the identity.
/// </summary>
public class GymContest
{
    /// <summary>The judge this contest lives on. Part of the key.</summary>
    public Platform Platform { get; set; }
    /// <summary>The platform's contest id (CF gym id / QOJ contest id).</summary>
    public long GymId { get; set; }
    public string? Title { get; set; }
    public long? StartTime { get; set; }
    public int ProblemCount { get; set; }
    /// <summary>For Codeforces gyms cloned from another gym, the direct parent contest id.</summary>
    public long? CloneParentGymId { get; set; }
    /// <summary>Human-readable title of <see cref="CloneParentGymId"/> as shown by Codeforces.</summary>
    public string? CloneParentTitle { get; set; }
    /// <summary>The top-most contest id in the Codeforces clone chain; null when unknown or not cloned.</summary>
    public long? CloneRootGymId { get; set; }
    /// <summary>Unix seconds when the Codeforces clone relationship was last checked.</summary>
    public long? CloneCheckedAt { get; set; }
}

/// <summary>
/// The QOJ contest dashboard's authoritative mapping from its numeric problem id to the contest
/// letter and title. QOJ's global submission feed deliberately shows the numeric id instead of
/// that letter, so this cache keeps raw submissions aligned with the official ranklist columns.
/// </summary>
public class QojContestProblem
{
    public long ContestId { get; set; }
    public string ProblemId { get; set; } = string.Empty;
    public string ProblemIndex { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string ProblemUrl { get; set; } = string.Empty;
}

/// <summary>
/// Derived per-account, per-gym result, split by participation kind so a real VP run
/// (<see cref="ParticipationKind.Virtual"/>) and post-contest practice
/// (<see cref="ParticipationKind.Practice"/>) are recorded and shown separately, never mixed.
/// Recomputable from submissions.
/// </summary>
public class VpResult
{
    /// <summary>The judge this result belongs to. Part of the key.</summary>
    public Platform Platform { get; set; }
    public string Handle { get; set; } = string.Empty;
    public long GymId { get; set; }
    /// <summary>Part of the key: separates the VP aggregate from the practice aggregate.</summary>
    public ParticipationKind Kind { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>Total penalty in seconds (ICPC convention).</summary>
    public long Penalty { get; set; }
    /// <summary>JSON: {problemIndex: firstAcUnixSeconds}.</summary>
    public string FirstAcPerProblem { get; set; } = "{}";
}

/// <summary>
/// User's manual choice of which account's VP result counts for a gym when more than one account
/// virtual-participated the same contest (a conflict — the same gym cannot be double-counted in the
/// combined, all-accounts view). Absent selection ⇒ no conflict yet resolved.
/// </summary>
public class GymVpSelection
{
    /// <summary>The judge the gym belongs to. Part of the key.</summary>
    public Platform Platform { get; set; }
    public long GymId { get; set; }
    /// <summary>The handle whose VP result is chosen to represent this gym in combined stats.</summary>
    public string Handle { get; set; } = string.Empty;
}

/// <summary>An official contest sourced from algoux/srk-collection.</summary>
public class OfficialContest
{
    /// <summary>Leaf path, e.g. <c>icpc2024invitational-wuhan</c>.</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Top-level category path segment, e.g. <c>icpc</c> / <c>ccpc</c> / <c>provincial</c>.</summary>
    public string CategoryPath { get; set; } = string.Empty;
    /// <summary>Classified type: 区域赛 / 省赛 / 邀请赛 / 网络赛 / Final ... (Phase 2).</summary>
    public string? Category { get; set; }
    public string SrkUrl { get; set; } = string.Empty;
    public long? StartAt { get; set; }
    public long? DurationSeconds { get; set; }
    public int ProblemCount { get; set; }
    /// <summary>JSON of medal thresholds if known (official). Null → default rule applies.</summary>
    public string? MedalRules { get; set; }
    public long FetchedAt { get; set; }
}

/// <summary>A problem column from an official srk ranklist.</summary>
public class OfficialProblem
{
    public string OfficialId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? Title { get; set; }
    public int AcceptedCount { get; set; }
    public int SubmittedCount { get; set; }
}

/// <summary>A single official standing row parsed from srk <c>rows[]</c>.</summary>
public class OfficialStanding
{
    /// <summary>Synthetic surrogate key (team names are not unique within a contest).</summary>
    public long Id { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>Penalty normalized to seconds.</summary>
    public long Penalty { get; set; }
    /// <summary>JSON: {alias: {result, timeSeconds, tries}}.</summary>
    public string PerProblem { get; set; } = "{}";
    /// <summary>Whether this row is an official participant (srk user.official === true).</summary>
    public bool Official { get; set; }
    /// <summary>Whether this row had at least one valid submission (non-empty result in srk statuses).</summary>
    public bool HadValidSubmission { get; set; }
}

/// <summary>
/// gym ↔ official association (semi-automatic; persisted for reuse). Keyed by
/// <c>(Platform, GymId, OfficialId)</c> — the <c>GymId</c> is the platform's own contest id, which
/// collides across judges, so the platform is part of the identity.
/// </summary>
public class ContestMapping
{
    /// <summary>The judge the gym belongs to. Part of the key.</summary>
    public Platform Platform { get; set; }
    public long GymId { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public double Confidence { get; set; }
    /// <summary>0 = candidate, 1 = user-confirmed.</summary>
    public int Confirmed { get; set; }
}

/// <summary>Small key-value table for local data/schema metadata and derived-cache versions.</summary>
public class AppMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
