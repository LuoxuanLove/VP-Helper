using VPHelper.Core.Analysis;
using VPHelper.Core.Models;
using Xunit;

namespace VPHelper.Core.Tests;

public class ProblemSubmissionStatusFormatterTests
{
    private static Submission Sub(long id, string verdict, long time, string index = "A")
        => new()
        {
            Id = id,
            GymId = 100,
            Handle = "h",
            ProblemIndex = index,
            Verdict = verdict,
            SubmitTime = time,
        };

    [Fact]
    public void ContestAcceptedShowsPenalizedFailuresBeforeAc()
    {
        var contest = new[]
        {
            Sub(1, "WRONG_ANSWER", 10),
            Sub(2, "WRONG_ANSWER", 20),
            Sub(3, "OK", 30),
        };

        Assert.Equal("+2", ProblemSubmissionStatusFormatter.Format(contest, [], "A"));
    }

    [Fact]
    public void ContestFailureShowsNegativeAttemptCount()
    {
        var contest = new[] { Sub(1, "WRONG_ANSWER", 10), Sub(2, "TIME_LIMIT_EXCEEDED", 20) };

        Assert.Equal("-2", ProblemSubmissionStatusFormatter.Format(contest, [], "A"));
    }

    [Fact]
    public void PostContestOnlyStateUsesParentheses()
    {
        Assert.Equal("(+)", ProblemSubmissionStatusFormatter.Format([], [Sub(1, "OK", 10)], "A"));
        Assert.Equal("(-2)", ProblemSubmissionStatusFormatter.Format([],
            [Sub(1, "WRONG_ANSWER", 10), Sub(2, "TIME_LIMIT_EXCEEDED", 20)], "A"));
    }

    [Fact]
    public void ContestFailureAndPostContestStateAreConcatenated()
    {
        var contest = new[] { Sub(1, "WRONG_ANSWER", 10), Sub(2, "TIME_LIMIT_EXCEEDED", 20) };
        var acceptedLater = new[] { Sub(3, "WRONG_ANSWER", 30), Sub(4, "OK", 40) };
        var failedLater = new[] { Sub(3, "WRONG_ANSWER", 30), Sub(4, "RUNTIME_ERROR", 40) };

        Assert.Equal("-2(+1)", ProblemSubmissionStatusFormatter.Format(contest, acceptedLater, "A"));
        Assert.Equal("-2(-2)", ProblemSubmissionStatusFormatter.Format(contest, failedLater, "A"));
    }

    [Fact]
    public void CompilationErrorsDoNotConsumeAttempts()
    {
        var contest = new[] { Sub(1, "COMPILATION_ERROR", 10), Sub(2, "OK", 20) };
        var postContest = new[] { Sub(3, "CE", 30), Sub(4, "WRONG_ANSWER", 40) };

        Assert.Equal("+", ProblemSubmissionStatusFormatter.Format(contest, postContest, "A"));
        Assert.Equal("(-1)", ProblemSubmissionStatusFormatter.Format([], postContest, "A"));
    }

    [Fact]
    public void ContestSolveDoesNotAppendPostContestAttempts()
    {
        Assert.Equal("+", ProblemSubmissionStatusFormatter.Format(
            [Sub(1, "OK", 10)],
            [Sub(2, "WRONG_ANSWER", 20)],
            "A"));
    }

    [Fact]
    public void MissingRawAcceptedSubmissionCanUseContestFallback()
    {
        Assert.Equal("+", ProblemSubmissionStatusFormatter.Format([], [], "A", contestSolvedFallback: true));
    }
}
