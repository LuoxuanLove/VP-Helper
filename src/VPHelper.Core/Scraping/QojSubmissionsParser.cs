using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace VPHelper.Core.Scraping;

/// <summary>A submission row parsed from a QOJ (qoj.ac) submissions HTML table.</summary>
/// <remarks>
/// QOJ has no public API, so submissions are scraped from HTML like Codeforces gym.
/// Two page layouts are supported by the same parser:
/// <list type="bullet">
/// <item>Contest page <c>/contest/{id}/submissions</c> — 8 columns, in-contest rows carry a
/// relative <c>HH:MM:SS</c> submit time and a <c>&lt;sup&gt;</c> contest marker.</item>
/// <item>Global page <c>/submissions</c> — 9 columns (extra Language column); times are always
/// absolute, so the <c>&lt;sup&gt;</c> contest marker is the only in-contest signal.</item>
/// </list>
/// Virtual-participation submissions are detected via <see cref="InContest"/> (the
/// <c>&lt;sup&gt;</c> marker), which on the contest page is perfectly correlated with a
/// relative submit time.
/// </remarks>
public record QojParsedSubmission(
    long Id,
    long ContestId,
    string ProblemId,
    string ProblemIndex,
    string ProblemName,
    string ProblemUrl,
    string Verdict,
    long SubmitTimeUnix,
    long? RelativeSeconds,
    bool InContest,
    string Submitter);

/// <summary>One parsed page of a QOJ submissions table.</summary>
public record QojSubmissionsPage(
    IReadOnlyList<QojParsedSubmission> Submissions,
    int? MaxPageIndex);

/// <summary>One problem as defined by a QOJ contest dashboard.</summary>
public record QojContestProblemDefinition(
    long ContestId,
    string ProblemId,
    string ProblemIndex,
    string Title,
    string ProblemUrl);

/// <summary>
/// Parses QOJ submissions pages (contest and global). Extracts rows that carry both a
/// submission link (<c>/submission/{id}</c>) and a problem link
/// (<c>/contest/{cid}/problem/{pid}</c>); header and non-submission rows are ignored.
/// </summary>
public static class QojSubmissionsParser
{
    private static readonly HtmlParser Parser = new();

    private static readonly Regex SubmissionIdRegex =
        new(@"/submission/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProblemHrefRegex =
        new(@"/contest/(\d+)/problem/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContestHrefRegex =
        new(@"/contest/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UserProfileHrefRegex =
        new(@"/user/profile/([^/?#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RelativeTimeRegex =
        new(@"^(\d{1,2}):(\d{2}):(\d{2})$", RegexOptions.Compiled);
    private static readonly Regex PageQueryRegex =
        new(@"[?&]page=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse the authoritative problem table from <c>/contest/{id}</c>. The table maps a contest
    /// letter such as <c>J</c> to QOJ's globally-scoped numeric problem id such as <c>18316</c>.
    /// This is deliberately separate from submission parsing because the account feed only emits
    /// the numeric form (<c>#18316. ...</c>).
    /// </summary>
    public static IReadOnlyList<QojContestProblemDefinition> ParseContestProblems(string html, long contestId)
    {
        var doc = Parser.ParseDocument(html);
        var results = new List<QojContestProblemDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in doc.QuerySelectorAll("tr"))
        {
            var problemLink = row.QuerySelectorAll("a")
                .FirstOrDefault(a => ProblemHrefRegex.IsMatch(a.GetAttribute("href") ?? string.Empty));
            if (problemLink == null) continue;

            var (rowContestId, problemId) = ParseProblemHref(problemLink.GetAttribute("href") ?? string.Empty);
            if (rowContestId != contestId || string.IsNullOrWhiteSpace(problemId) || !seen.Add(problemId))
                continue;

            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 2) continue;

            var index = cells[0].TextContent.Trim();
            var title = problemLink.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(title)) continue;

            results.Add(new QojContestProblemDefinition(
                rowContestId,
                problemId,
                index,
                title,
                BuildProblemUrl(rowContestId, problemId)));
        }

        return results;
    }

    /// <summary>Parse one QOJ submissions page's HTML.</summary>
    public static QojSubmissionsPage Parse(string html)
    {
        var doc = Parser.ParseDocument(html);
        var results = new List<QojParsedSubmission>();

        foreach (var row in doc.QuerySelectorAll("tr"))
        {
            // A submission row has a link to the submission source and a link to the problem.
            var submissionLink = row.QuerySelector("a[href*='/submission/']");
            if (submissionLink == null) continue;

            var idHref = submissionLink.GetAttribute("href") ?? string.Empty;
            var idMatch = SubmissionIdRegex.Match(idHref);
            if (!idMatch.Success ||
                !long.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;

            var problemLink = row.QuerySelectorAll("a")
                .FirstOrDefault(a => ProblemHrefRegex.IsMatch(a.GetAttribute("href") ?? string.Empty));
            if (problemLink == null) continue;

            var (contestId, problemId) = ParseProblemHref(problemLink.GetAttribute("href") ?? string.Empty);
            if (contestId == 0) continue;

            var (index, name) = SplitProblemLabel(problemLink.TextContent.Trim());

            string verdict = ExtractVerdict(row);

            var submitter = ExtractSubmitter(row);

            // The <sup> contest marker after the submitter flags an in-contest (virtual) row.
            bool inContest = row.QuerySelectorAll("sup a")
                .Any(a => ContestHrefRegex.IsMatch(a.GetAttribute("href") ?? string.Empty));

            var (submitUnix, relativeSeconds) = ExtractSubmitTime(row);

            results.Add(new QojParsedSubmission(
                id, contestId, problemId, index, name, BuildProblemUrl(contestId, problemId),
                verdict, submitUnix, relativeSeconds, inContest, submitter));
        }

        return new QojSubmissionsPage(results, ExtractMaxPage(doc));
    }

    /// <summary>Parse "/contest/2575/problem/14823" → (2575, "14823"). Returns (0, "") when not matched.</summary>
    public static (long contestId, string problemId) ParseProblemHref(string href)
    {
        var m = ProblemHrefRegex.Match(href ?? string.Empty);
        if (!m.Success) return (0, string.Empty);
        long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid);
        return (cid, m.Groups[2].Value);
    }

    public static string BuildProblemUrl(long contestId, string problemId)
        => string.IsNullOrWhiteSpace(problemId)
            ? $"https://qoj.ac/contest/{contestId}"
            : $"https://qoj.ac/contest/{contestId}/problem/{Uri.EscapeDataString(problemId)}";

    /// <summary>
    /// Split a QOJ problem label into (index, name). Contest pages show "J. Judge of Gensokyo"
    /// (letter index); global pages show "#18433. Lottery" (problem-number index). Returns the
    /// head as the index (letter kept as-is, "#" stripped) and the tail as the name.
    /// </summary>
    internal static (string index, string name) SplitProblemLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return ("?", string.Empty);

        var dot = label.IndexOf(". ", StringComparison.Ordinal);
        string head, tail;
        if (dot >= 0)
        {
            head = label[..dot].Trim();
            tail = label[(dot + 2)..].Trim();
        }
        else
        {
            head = label.Trim();
            tail = string.Empty;
        }

        head = head.TrimStart('#').Trim();
        if (head.Length == 0) head = "?";
        return (head, tail);
    }

    private static string ExtractVerdict(AngleSharp.Dom.IElement row)
    {
        // Full/partial results render as <a class="uoj-score">AC ✓ / WA / TL / …</a>.
        var score = row.QuerySelector("a.uoj-score");
        if (score != null)
            return NormalizeVerdict(score.TextContent);

        // Compile errors (and similar pre-judge states) render as <a class="small">Compile Error</a>.
        var small = row.QuerySelector("a.small");
        if (small != null)
            return NormalizeVerdict(small.TextContent);

        return "UNKNOWN";
    }

    /// <summary>Normalize a QOJ verdict cell's text into a token the aggregator understands.</summary>
    internal static string NormalizeVerdict(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "UNKNOWN";

        // Strip the "✓"/"✗" glyphs and surrounding whitespace QOJ appends to score cells.
        var t = text.Replace("✓", string.Empty).Replace("✗", string.Empty).Trim();
        if (t.Length == 0) return "UNKNOWN";

        var lower = t.ToLowerInvariant();
        if (lower.Contains("compile error") || lower.Contains("compilation error"))
            return "COMPILATION_ERROR";
        if (t.Equals("AC", StringComparison.OrdinalIgnoreCase) || lower.Contains("accepted"))
            return "AC";

        // A fully-scored numeric result (100) counts as accepted; other numbers are partial → wrong.
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts))
            return pts >= 100 ? "AC" : t;

        return t;
    }

    /// <summary>
    /// Extract the account identity from either QOJ username rendering. Older pages use an
    /// <c>&lt;a class="uoj-username"&gt;</c>; current authenticated pages render the same class on a
    /// <c>&lt;span data-nickname="..."&gt;</c>. Matching the class instead of the element type keeps
    /// account-filtered submission pages stable across both layouts.
    /// </summary>
    internal static string ExtractSubmitter(AngleSharp.Dom.IElement row)
    {
        var username = row.QuerySelector(".uoj-username");
        if (username == null) return string.Empty;

        var nickname = username.GetAttribute("data-nickname")?.Trim();
        if (!string.IsNullOrWhiteSpace(nickname)) return nickname;

        var href = username.GetAttribute("href") ?? string.Empty;
        var match = UserProfileHrefRegex.Match(href);
        if (match.Success)
            return Uri.UnescapeDataString(match.Groups[1].Value).Trim();

        return username.TextContent.Trim();
    }

    /// <summary>
    /// Read the submit-time cell. Returns (absoluteUnix, relativeSeconds); either may be
    /// "empty" (0 / null) depending on the page layout. In-contest rows on the contest page
    /// carry a relative <c>HH:MM:SS</c>; every other row carries an absolute date-time.
    /// </summary>
    private static (long unix, long? relativeSeconds) ExtractSubmitTime(AngleSharp.Dom.IElement row)
    {
        // The submit time is the trailing <small> in the row.
        var smalls = row.QuerySelectorAll("small");
        var timeSmall = smalls.LastOrDefault();
        if (timeSmall == null) return (0, null);

        var text = timeSmall.TextContent.Trim();
        if (text.Length == 0) return (0, null);

        var rel = RelativeTimeRegex.Match(text);
        if (rel.Success)
        {
            long h = long.Parse(rel.Groups[1].Value, CultureInfo.InvariantCulture);
            long m = long.Parse(rel.Groups[2].Value, CultureInfo.InvariantCulture);
            long s = long.Parse(rel.Groups[3].Value, CultureInfo.InvariantCulture);
            return (0, h * 3600 + m * 60 + s);
        }

        return (ParseQojTime(text), null);
    }

    /// <summary>Parse an absolute QOJ time like "2026-01-19 01:29:41" into unix seconds (UTC assumed).</summary>
    public static long ParseQojTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Trim();
        string[] formats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm",
        };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
            return new DateTimeOffset(dt2, TimeSpan.Zero).ToUnixTimeSeconds();
        return 0;
    }

    /// <summary>Find the highest page index from the pagination control, if present.</summary>
    internal static int? ExtractMaxPage(AngleSharp.Dom.IDocument doc)
    {
        int max = 0;
        foreach (var a in doc.QuerySelectorAll(".pagination a, ul.pagination a, nav a"))
        {
            var href = a.GetAttribute("href") ?? string.Empty;
            var text = a.TextContent.Trim();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
            var m = PageQueryRegex.Match(href);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pn))
                max = Math.Max(max, pn);
        }
        return max > 0 ? max : null;
    }
}
