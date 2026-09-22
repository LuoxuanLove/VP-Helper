using System;
using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Models;

namespace VPHelper.Core.Analysis;

/// <summary>
/// Formats per-problem contest and post-contest submission states. Parentheses identify
/// post-contest attempts. A positive number is the count of penalized failures before AC;
/// compile errors and other no-penalty verdicts do not consume an attempt.
/// </summary>
public static class ProblemSubmissionStatusFormatter
{
    private static readonly HashSet<string> AcceptedVerdicts =
        new(StringComparer.OrdinalIgnoreCase) { "OK", "AC", "ACCEPTED" };

    private static readonly HashSet<string> IgnoredVerdicts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "COMPILATION_ERROR", "CE", "SKIPPED", "TESTING", "?", "UKE",
        };

    public static string Format(
        IEnumerable<Submission> contestSubmissions,
        IEnumerable<Submission> postContestSubmissions,
        string problemIndex,
        bool contestSolvedFallback = false)
    {
        var contest = Summarize(contestSubmissions, problemIndex);
        if (!contest.HasAttempts && contestSolvedFallback)
            contest = new ProblemAttemptStatus(true, 0);

        var contestToken = FormatToken(contest);
        if (contest.Solved)
            return contestToken!;

        var postContest = Summarize(postContestSubmissions, problemIndex);
        var postContestToken = FormatToken(postContest);

        if (postContestToken == null)
            return contestToken ?? "—";

        return contestToken == null
            ? $"({postContestToken})"
            : $"{contestToken}({postContestToken})";
    }

    internal static ProblemAttemptStatus Summarize(
        IEnumerable<Submission> submissions,
        string problemIndex)
    {
        var wrongAttempts = 0;
        foreach (var submission in submissions
                     .Where(s => SameProblem(s.ProblemIndex, problemIndex))
                     .OrderBy(s => s.SubmitTime)
                     .ThenBy(s => s.Id))
        {
            if (IsAccepted(submission.Verdict))
                return new ProblemAttemptStatus(true, wrongAttempts);

            if (IsCountedWrong(submission.Verdict))
                wrongAttempts++;
        }

        return new ProblemAttemptStatus(false, wrongAttempts);
    }

    private static string? FormatToken(ProblemAttemptStatus status)
    {
        if (!status.Solved && !status.HasAttempts)
            return null;

        if (status.Solved)
            return status.WrongAttempts == 0 ? "+" : $"+{status.WrongAttempts}";

        return $"-{status.WrongAttempts}";
    }

    private static bool SameProblem(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsAccepted(string? verdict)
        => verdict != null && AcceptedVerdicts.Contains(verdict);

    private static bool IsCountedWrong(string? verdict)
        => !string.IsNullOrWhiteSpace(verdict) && !IgnoredVerdicts.Contains(verdict);
}

internal readonly record struct ProblemAttemptStatus(bool Solved, int WrongAttempts)
{
    public bool HasAttempts => WrongAttempts > 0;
}
