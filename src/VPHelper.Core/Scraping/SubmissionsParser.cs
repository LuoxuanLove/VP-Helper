using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AngleSharp.Html.Parser;

namespace VPHelper.Core.Scraping;

/// <summary>A submission row parsed from the Codeforces submissions HTML table.</summary>
public record ParsedSubmission(
    long Id,
    long GymId,
    string ProblemIndex,
    string ProblemName,
    string ProblemUrl,
    string Verdict,
    long SubmitTimeUnix);

/// <summary>One parsed page of the submissions table.</summary>
public record SubmissionsPage(
    IReadOnlyList<ParsedSubmission> Submissions,
    int? MaxPageIndex);

/// <summary>
/// Parses Codeforces <c>/submissions/{handle}</c> pages (DESIGN 6.2). Extracts only gym
/// submissions (rows whose problem link points at <c>/gym/{id}/...</c>); regular rated
/// contest rows are ignored per project scope.
/// </summary>
public static class SubmissionsParser
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Parse one submissions page's HTML.</summary>
    public static SubmissionsPage Parse(string html)
    {
        var doc = Parser.ParseDocument(html);
        var results = new List<ParsedSubmission>();

        // The submissions table has rows with data-submission-id.
        var rows = doc.QuerySelectorAll("tr[data-submission-id]");
        foreach (var row in rows)
        {
            var idAttr = row.GetAttribute("data-submission-id");
            if (!long.TryParse(idAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;

            // Problem link: the <a> pointing at the PROBLEM (…/problem/X), not the
            // view-source link (…/submission/{id}) which also carries /gym/ or /contest/.
            // Real CF rows put the source link first, so we must filter to /problem/.
            var anchors = row.QuerySelectorAll("a");
            var problemLink =
                anchors.FirstOrDefault(a => IsProblemLink(a.GetAttribute("href")))
                ?? anchors.FirstOrDefault(a => IsProblemHref(a.GetAttribute("href")));
            if (problemLink == null) continue;

            var href = problemLink.GetAttribute("href") ?? string.Empty;
            var (gymId, index) = ParseProblemHref(href);
            if (gymId == 0) continue; // only gym submissions

            // Problem name is the link text, often "A - Problem Name" or just the name.
            string linkText = problemLink.TextContent.Trim();
            string problemName = ExtractProblemName(linkText, index);
            if (string.IsNullOrEmpty(index))
                index = ExtractIndexFromText(linkText) ?? "?";

            // Verdict cell: span[submissionverdict] or a cell with class verdict-*.
            string verdict = ExtractVerdict(row);

            // Submit time: a cell with class "format-time" carrying data (unix) or text.
            long submitTime = ExtractSubmitTime(row);

            results.Add(new ParsedSubmission(id, gymId, index, problemName, BuildProblemUrl(gymId, index), verdict, submitTime));
        }

        int? maxPage = ExtractMaxPage(doc);
        return new SubmissionsPage(results, maxPage);
    }

    private static bool IsProblemHref(string? href)
    {
        if (string.IsNullOrEmpty(href)) return false;
        return href.Contains("/gym/", StringComparison.OrdinalIgnoreCase)
            || href.Contains("/contest/", StringComparison.OrdinalIgnoreCase)
            || href.Contains("/problemset/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True only for a link to a problem page (…/problem/X), excluding source/submission links.</summary>
    private static bool IsProblemLink(string? href)
        => IsProblemHref(href) && href!.Contains("/problem/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parse "/gym/104520/problem/A" → (104520, "A"). Returns (0, "") for non-gym.</summary>
    public static (long gymId, string index) ParseProblemHref(string href)
    {
        // Normalize and split.
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Look for "gym" segment followed by numeric id.
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "gym", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(parts[i + 1], out var gid))
            {
                string idx = string.Empty;
                // .../problem/A
                var pi = Array.FindIndex(parts, p => string.Equals(p, "problem", StringComparison.OrdinalIgnoreCase));
                if (pi >= 0 && pi + 1 < parts.Length) idx = parts[pi + 1].ToUpperInvariant();
                return (gid, idx);
            }
        }
        return (0, string.Empty);
    }

    public static string BuildProblemUrl(long gymId, string index)
        => string.IsNullOrWhiteSpace(index)
            ? $"https://codeforces.com/gym/{gymId}"
            : $"https://codeforces.com/gym/{gymId}/problem/{Uri.EscapeDataString(index)}";

    private static string ExtractProblemName(string linkText, string index)
    {
        // "A - Problem Name" → "Problem Name"
        var dash = linkText.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0) return linkText[(dash + 3)..].Trim();
        return linkText;
    }

    private static string? ExtractIndexFromText(string linkText)
    {
        var dash = linkText.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) return linkText[..dash].Trim().ToUpperInvariant();
        return null;
    }

    private static string ExtractVerdict(AngleSharp.Dom.IElement row)
    {
        // Prefer the explicit verdict span used by CF.
        var span = row.QuerySelector("span[submissionverdict]");
        if (span != null)
        {
            var v = span.GetAttribute("submissionverdict");
            if (!string.IsNullOrEmpty(v)) return v.Trim();
        }
        var cell = row.QuerySelector("td.status-cell, td[waiting]");
        if (cell != null)
        {
            var text = cell.TextContent.Trim();
            if (!string.IsNullOrEmpty(text)) return NormalizeVerdictText(text);
        }
        // Fallback: any submissionVerdict-carrying descendant.
        var any = row.QuerySelectorAll("span")
            .FirstOrDefault(s => s.GetAttribute("submissionverdict") != null);
        return any?.GetAttribute("submissionverdict")?.Trim() ?? "UNKNOWN";
    }

    private static string NormalizeVerdictText(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("accepted")) return "OK";
        if (t.Contains("wrong answer")) return "WRONG_ANSWER";
        if (t.Contains("time limit")) return "TIME_LIMIT_EXCEEDED";
        if (t.Contains("memory limit")) return "MEMORY_LIMIT_EXCEEDED";
        if (t.Contains("runtime error")) return "RUNTIME_ERROR";
        if (t.Contains("compilation error")) return "COMPILATION_ERROR";
        return text.Trim();
    }

    private static long ExtractSubmitTime(AngleSharp.Dom.IElement row)
    {
        var timeCell = row.QuerySelector(".format-time, span.format-time, td.format-time");
        if (timeCell != null)
        {
            // CF stores epoch in the text or a data attr in some skins.
            var text = timeCell.TextContent.Trim();
            var parsed = ParseCfTime(text);
            if (parsed != 0) return parsed;
        }
        return 0;
    }

    /// <summary>Parse CF time text like "Oct/19/2025 13:05" or "2025-10-19 13:05" into unix seconds (UTC assumed).</summary>
    public static long ParseCfTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Trim();
        string[] formats =
        {
            "MMM/dd/yyyy HH:mm", "MMM/dd/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss",
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
        foreach (var a in doc.QuerySelectorAll("div.pagination a, .pagination a"))
        {
            var href = a.GetAttribute("href") ?? string.Empty;
            var text = a.TextContent.Trim();
            if (int.TryParse(text, out var n)) max = Math.Max(max, n);
            // hrefs like /submissions/handle/page/7
            var idx = href.IndexOf("/page/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var tail = href[(idx + 6)..].Split('/', '?')[0];
                if (int.TryParse(tail, out var pn)) max = Math.Max(max, pn);
            }
        }
        return max > 0 ? max : null;
    }
}
