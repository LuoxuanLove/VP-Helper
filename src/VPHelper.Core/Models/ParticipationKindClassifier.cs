using System;

namespace VPHelper.Core.Models;

/// <summary>
/// Maps a Codeforces <c>author.participantType</c> string to a <see cref="ParticipationKind"/>.
/// A single source of truth so the sync (tagging rows) and the aggregator (splitting VP from
/// practice) agree on what counts as a virtual run.
/// </summary>
public static class ParticipationKindClassifier
{
    /// <summary>
    /// Classify a CF participant type. VIRTUAL and CONTESTANT are timed, ranked runs that count as
    /// VP; everything else (PRACTICE / OUT_OF_COMPETITION / MANAGER / unknown / null) is practice.
    /// A null/blank type (e.g. a legacy HTML-only row from before API enrichment) is treated as
    /// practice so it never inflates a VP result it was never verified to belong to.
    /// </summary>
    public static ParticipationKind Classify(string? participantType)
        => participantType?.Trim().ToUpperInvariant() switch
        {
            "VIRTUAL" => ParticipationKind.Virtual,
            "CONTESTANT" => ParticipationKind.Virtual,
            _ => ParticipationKind.Practice,
        };
}
