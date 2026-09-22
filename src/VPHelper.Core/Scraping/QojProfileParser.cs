using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace VPHelper.Core.Scraping;

/// <summary>A virtual-participation contest discovered on a QOJ user profile page.</summary>
public record QojProfileContest(long ContestId, string Title, long? StartTimeUnix);

/// <summary>
/// Parses a QOJ user profile page (<c>/user/profile/{handle}</c>) to discover the account's
/// virtual participations. The profile renders one table (<c>Name | Start time</c>) whose rows
/// link to <c>/contest/{id}</c>; this mirrors the Codeforces contest-list → detail discovery
/// model and gives the sync pipeline a bounded set of contests to scrape submissions for.
/// </summary>
public static class QojProfileParser
{
    private static readonly HtmlParser Parser = new();

    private static readonly Regex ContestHrefRegex =
        new(@"/contest/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parse the profile page's virtual-participations table.</summary>
    public static IReadOnlyList<QojProfileContest> ParseVirtualParticipations(string html)
    {
        var doc = Parser.ParseDocument(html);
        var results = new List<QojProfileContest>();
        var seen = new HashSet<long>();

        // The VP list is a table of rows linking to /contest/{id} with a start-time cell.
        foreach (var row in doc.QuerySelectorAll("tr"))
        {
            var link = row.QuerySelectorAll("a")
                .FirstOrDefault(a => ContestHrefRegex.IsMatch(a.GetAttribute("href") ?? string.Empty));
            if (link == null) continue;

            var m = ContestHrefRegex.Match(link.GetAttribute("href") ?? string.Empty);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out var contestId)) continue;
            if (!seen.Add(contestId)) continue;

            var title = link.TextContent.Trim();

            // Start time lives in a sibling cell; take the first cell that parses as a QOJ time.
            long? startUnix = null;
            foreach (var cell in row.QuerySelectorAll("td"))
            {
                if (cell == link.ParentElement) continue;
                var t = QojSubmissionsParser.ParseQojTime(cell.TextContent.Trim());
                if (t != 0) { startUnix = t; break; }
            }

            results.Add(new QojProfileContest(contestId, title, startUnix));
        }

        return results;
    }
}
