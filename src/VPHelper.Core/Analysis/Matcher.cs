using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VPHelper.Core.Analysis;

/// <summary>Minimal contest facts the matcher needs from either side.</summary>
public record MatchGym(long GymId, string Title, long? StartTime, int ProblemCount);

public record MatchOfficial(string OfficialId, string Name, long? StartAt, int ProblemCount);

public record MatchCandidate(string OfficialId, string OfficialName, double Confidence, string Reason)
{
    // Mutable ordering handled by caller; record is a value carrier.
}

/// <summary>
/// Generates gym → official candidate matches by combining title similarity, problem-count
/// agreement, and start-time proximity (DESIGN 6.3). Pure and deterministic for testing.
/// </summary>
public class ContestMatcher
{
    public double TitleWeight { get; init; } = 0.6;
    public double TimeWeight { get; init; } = 0.25;
    public double ProblemWeight { get; init; } = 0.15;

    /// <summary>Return official candidates for a gym, ordered by descending confidence.</summary>
    public List<MatchCandidate> Match(MatchGym gym, IEnumerable<MatchOfficial> officials, int topN = 5)
    {
        var scored = new List<MatchCandidate>();
        foreach (var o in officials)
        {
            double title = TitleSimilarity(gym.Title, o.Name);
            double time = TimeScore(gym.StartTime, o.StartAt);
            double prob = ProblemScore(gym.ProblemCount, o.ProblemCount);
            double conf = TitleWeight * title + TimeWeight * time + ProblemWeight * prob;
            var reason = $"title={title:F2} time={time:F2} prob={prob:F2}";
            scored.Add(new MatchCandidate(o.OfficialId, o.Name, Math.Round(conf, 4), reason));
        }
        return scored
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.OfficialId, StringComparer.Ordinal)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Title similarity in [0,1]: token Jaccard over normalized tokens combined with
    /// character-bigram Dice, so it works for both Latin and CJK titles.
    /// </summary>
    public static double TitleSimilarity(string a, string b)
    {
        string na = Normalize(a);
        string nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return 0;
        if (na == nb) return 1.0;

        double token = TokenJaccard(na, nb);
        double bigram = BigramDice(na, nb);
        return Math.Max(token, bigram);
    }

    private static double TokenJaccard(string a, string b)
    {
        var sa = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var sb = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (sa.Count == 0 || sb.Count == 0) return 0;
        int inter = sa.Count(sb.Contains);
        int union = sa.Count + sb.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    private static double BigramDice(string a, string b)
    {
        var ba = Bigrams(a.Replace(" ", ""));
        var bb = Bigrams(b.Replace(" ", ""));
        if (ba.Count == 0 || bb.Count == 0) return 0;
        int inter = 0;
        var bbCopy = new Dictionary<string, int>(bb);
        foreach (var g in ba)
        {
            if (bbCopy.TryGetValue(g.Key, out var c) && c > 0)
            {
                inter += Math.Min(g.Value, c);
                bbCopy[g.Key] = c - Math.Min(g.Value, c);
            }
        }
        int total = ba.Values.Sum() + bb.Values.Sum();
        return total == 0 ? 0 : 2.0 * inter / total;
    }

    private static Dictionary<string, int> Bigrams(string s)
    {
        var d = new Dictionary<string, int>();
        for (int i = 0; i + 1 < s.Length; i++)
        {
            var g = s.Substring(i, 2);
            d[g] = d.TryGetValue(g, out var c) ? c + 1 : 1;
        }
        if (s.Length == 1) d[s] = 1;
        return d;
    }

    /// <summary>Lowercase, strip punctuation, collapse whitespace. Keeps CJK and alphanumerics.</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append(' ');
            else sb.Append(' '); // punctuation -> separator
        }
        // collapse multiple spaces
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Time proximity score: 1.0 within a day, decaying to 0 beyond ~30 days.</summary>
    public static double TimeScore(long? a, long? b)
    {
        if (a == null || b == null) return 0;
        long diff = Math.Abs(a.Value - b.Value);
        const long day = 86400;
        if (diff <= day) return 1.0;
        if (diff >= 30 * day) return 0;
        return 1.0 - (double)(diff - day) / (30 * day - day);
    }

    /// <summary>Problem-count agreement: 1.0 exact, decaying with the difference.</summary>
    public static double ProblemScore(int a, int b)
    {
        if (a <= 0 || b <= 0) return 0;
        int diff = Math.Abs(a - b);
        if (diff == 0) return 1.0;
        if (diff >= 5) return 0;
        return 1.0 - diff / 5.0;
    }
}
