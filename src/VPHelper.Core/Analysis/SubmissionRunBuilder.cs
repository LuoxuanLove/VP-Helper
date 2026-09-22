using System;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Models;

namespace VPHelper.Core.Analysis;

/// <summary>
/// Builds contest-scoring submission groups from raw submissions. Codeforces HTML rows may lack
/// participant type and relative time; when no explicit virtual rows exist, the first bounded
/// contest window is treated as an inferred VP run so old local data can be rebuilt.
/// </summary>
public sealed record SubmissionRun(ParticipationKind Kind, IReadOnlyList<Submission> Submissions);

public static class SubmissionRunBuilder
{
    public static IReadOnlyList<SubmissionRun> BuildForCodeforces(
        IReadOnlyList<Submission> submissions,
        long contestDurationSeconds)
    {
        var explicitVirtual = submissions
            .Where(IsExplicitVirtualSubmission)
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();
        var explicitPractice = submissions
            .Where(IsExplicitPracticeSubmission)
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();
        var unknown = submissions
            .Where(s => string.IsNullOrWhiteSpace(s.ParticipantType))
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();

        var groups = new List<SubmissionRun>();

        if (explicitVirtual.Count > 0)
        {
            groups.Add(new SubmissionRun(
                ParticipationKind.Virtual,
                EnsureRelativeTimes(explicitVirtual, contestDurationSeconds)));

            var practice = explicitPractice.Concat(unknown).ToList();
            if (practice.Count > 0)
                groups.Add(new SubmissionRun(ParticipationKind.Practice, practice));

            return groups;
        }

        if (unknown.Count == 0)
        {
            if (explicitPractice.Count > 0)
                groups.Add(new SubmissionRun(ParticipationKind.Practice, explicitPractice));
            return groups;
        }

        var inferredStart = unknown.Min(s => s.SubmitTime);
        var inferredVirtual = unknown
            .Select(s => WithRelativeTime(s, s.SubmitTime - inferredStart))
            .Where(s => IsWithinContest(s.RelativeTime ?? -1, contestDurationSeconds))
            .ToList();
        if (inferredVirtual.Count > 0)
            groups.Add(new SubmissionRun(ParticipationKind.Virtual, inferredVirtual));

        var inferredPractice = explicitPractice
            .Concat(unknown.Where(s => !IsWithinContest(s.SubmitTime - inferredStart, contestDurationSeconds)))
            .OrderBy(s => s.SubmitTime)
            .ThenBy(s => s.Id)
            .ToList();
        if (inferredPractice.Count > 0)
            groups.Add(new SubmissionRun(ParticipationKind.Practice, inferredPractice));

        return groups;
    }

    public static IReadOnlyList<SubmissionRun> BuildTypedOnly(IReadOnlyList<Submission> submissions)
        => submissions
            .GroupBy(s => ParticipationKindClassifier.Classify(s.ParticipantType))
            .Select(g => new SubmissionRun(
                g.Key,
                g.OrderBy(s => s.SubmitTime).ThenBy(s => s.Id).ToList()))
            .ToList();

    public static Submission WithRelativeTime(Submission source, long relativeTime)
        => new()
        {
            Platform = source.Platform,
            Id = source.Id,
            Handle = source.Handle,
            GymId = source.GymId,
            ProblemIndex = source.ProblemIndex,
            ProblemName = source.ProblemName,
            ProblemUrl = source.ProblemUrl,
            Verdict = source.Verdict,
            SubmitTime = source.SubmitTime,
            ParticipantType = string.IsNullOrWhiteSpace(source.ParticipantType)
                ? "VIRTUAL_INFERRED"
                : source.ParticipantType,
            RelativeTime = relativeTime,
        };

    public static bool IsWithinContest(long elapsedSeconds, long contestDurationSeconds)
        => elapsedSeconds >= 0 && elapsedSeconds < Math.Max(60, contestDurationSeconds);

    private static IReadOnlyList<Submission> EnsureRelativeTimes(
        IReadOnlyList<Submission> submissions,
        long contestDurationSeconds)
    {
        if (submissions.All(s => s.RelativeTime != null))
            return submissions;

        var knownStart = submissions
            .Where(s => s.RelativeTime is long rel && IsWithinContest(rel, contestDurationSeconds))
            .Select(s => (long?)(s.SubmitTime - s.RelativeTime!.Value))
            .OrderBy(s => s)
            .FirstOrDefault();
        var start = knownStart ?? submissions.Min(s => s.SubmitTime);

        return submissions
            .Select(s => s.RelativeTime != null ? s : WithRelativeTime(s, s.SubmitTime - start))
            .ToList();
    }

    private static bool IsExplicitVirtualSubmission(Submission submission)
        => !string.IsNullOrWhiteSpace(submission.ParticipantType) &&
           ParticipationKindClassifier.Classify(submission.ParticipantType) == ParticipationKind.Virtual;

    private static bool IsExplicitPracticeSubmission(Submission submission)
        => !string.IsNullOrWhiteSpace(submission.ParticipantType) &&
           ParticipationKindClassifier.Classify(submission.ParticipantType) == ParticipationKind.Practice;
}
