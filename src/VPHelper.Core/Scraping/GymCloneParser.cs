using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace VPHelper.Core.Scraping;

/// <summary>Direct clone-parent information parsed from a Codeforces Gym page.</summary>
public record GymCloneInfo(long ParentGymId, string ParentTitle);

/// <summary>
/// Parses the Codeforces right-sidebar "Clone Contest to Mashup" box.
/// Clone pages say they "was cloned from" another gym and link to the parent contest.
/// </summary>
public static partial class GymCloneParser
{
    private static readonly HtmlParser Parser = new();

    public static GymCloneInfo? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var doc = Parser.ParseDocument(html);
        foreach (var element in doc.QuerySelectorAll(".roundbox, .datatable, div, p")
                     .Where(e => e.TextContent.Contains("cloned from", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => CollapseWhitespace(e.TextContent).Length))
        {
            var text = CollapseWhitespace(element.TextContent);
            if (!text.Contains("cloned from", StringComparison.OrdinalIgnoreCase))
                continue;

            var scopedMatch = CloneParentRegex().Match(element.InnerHtml);
            if (scopedMatch.Success)
            {
                var scopedTitle = StripTags(scopedMatch.Groups["title"].Value);
                var scopedParent = TryBuild($"/gym/{scopedMatch.Groups["id"].Value}", scopedTitle);
                if (scopedParent != null)
                    return scopedParent;
            }

            var parent = element.QuerySelectorAll("a[href*='/gym/']")
                .Select(a => TryBuild(a.GetAttribute("href"), a.TextContent))
                .FirstOrDefault(x => x != null);
            if (parent != null)
                return parent;
        }

        var regexMatch = CloneParentRegex().Match(html);
        if (!regexMatch.Success)
            return null;

        var title = StripTags(regexMatch.Groups["title"].Value);
        return TryBuild($"/gym/{regexMatch.Groups["id"].Value}", title);
    }

    private static GymCloneInfo? TryBuild(string? href, string? title)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var match = GymHrefRegex().Match(href);
        if (!match.Success ||
            !long.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var cleanTitle = CollapseWhitespace(title ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleanTitle))
            cleanTitle = $"Gym {id}";

        return new GymCloneInfo(id, cleanTitle);
    }

    private static string CollapseWhitespace(string value)
        => WhitespaceRegex().Replace(value, " ").Trim();

    private static string StripTags(string html)
        => CollapseWhitespace(TagRegex().Replace(html, string.Empty));

    [GeneratedRegex(@"(?:https?://codeforces\.com)?/gym/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GymHrefRegex();

    [GeneratedRegex(@"was\s+cloned\s+from\s*<a[^>]+href\s*=\s*[""'](?:https?://codeforces\.com)?/gym/(?<id>\d+)[""'][^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CloneParentRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
