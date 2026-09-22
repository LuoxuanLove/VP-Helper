using VPHelper.Core.Analysis;
using VPHelper.Core.Models;
using Xunit;

namespace VPHelper.Core.Tests;

public class AggregatorTests
{
    private static Submission Sub(long id, long gym, string idx, string verdict, long time, long? relativeTime = null)
        => new()
        {
            Id = id,
            Handle = "h",
            GymId = gym,
            ProblemIndex = idx,
            Verdict = verdict,
            SubmitTime = time,
            RelativeTime = relativeTime,
        };

    [Fact]
    public void CountsFirstAcAndIgnoresLaterSubmissions()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "WRONG_ANSWER", 10),
            Sub(2, 100, "A", "OK", 20),
            Sub(3, 100, "A", "OK", 30), // later AC ignored
            Sub(4, 100, "B", "OK", 40),
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        Assert.Equal(2, agg.SolvedCount);
        Assert.Equal(20, agg.FirstAcPerProblem["A"]);
        Assert.Equal(40, agg.FirstAcPerProblem["B"]);
    }

    [Fact]
    public void PenaltyOnlyForSolvedProblems_20MinPerWrong()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "WRONG_ANSWER", 10),
            Sub(2, 100, "A", "WRONG_ANSWER", 20),
            Sub(3, 100, "A", "OK", 30),        // 2 wrong before AC -> 40 min
            Sub(4, 100, "B", "WRONG_ANSWER", 5), // B never solved -> no penalty
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        Assert.Equal(1, agg.SolvedCount);
        Assert.Equal(2 * 20 * 60, agg.PenaltySeconds);
        Assert.Equal(2, agg.WrongAttemptsPerSolvedProblem["A"]);
    }

    [Fact]
    public void CompilationErrorDoesNotPenalize()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "COMPILATION_ERROR", 10),
            Sub(2, 100, "A", "OK", 20),
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        Assert.Equal(0, agg.PenaltySeconds);
    }

    [Fact]
    public void FiltersByGymId()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "OK", 10),
            Sub(2, 200, "A", "OK", 20),
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        Assert.Equal(1, agg.SolvedCount);
    }

    [Fact]
    public void PenaltyFromContestStart_AddsElapsedMinutesPlusWrong()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "WRONG_ANSWER", 1000 + 60),
            Sub(2, 100, "A", "OK", 1000 + 600), // 10 min after start
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        long p = new Aggregator().PenaltyFromContestStart(agg, 1000);
        // 10 minutes elapsed (600s) + 1 wrong * 1200s
        Assert.Equal(600 + 1200, p);
    }

    [Fact]
    public void PenaltyFromRelativeTime_AddsElapsedMinutesPlusWrong()
    {
        var subs = new[]
        {
            Sub(1, 100, "A", "WRONG_ANSWER", 1000, 60),
            Sub(2, 100, "A", "OK", 2000, 635), // floor to 10 min
            Sub(3, 100, "B", "OK", 3000, 121), // floor to 2 min
        };
        var agg = new Aggregator().Aggregate("h", 100, subs);
        long p = new Aggregator().PenaltyFromRelativeTime(agg, subs);
        Assert.Equal(600 + 1200 + 120, p);
    }
}
