using System;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace VPHelper.Core.Scraping;

/// <summary>Authenticated QOJ session detected from the account controls in the top bar.</summary>
public sealed record QojSession(bool IsLoggedIn, string? Handle);

/// <summary>
/// Detects QOJ authentication from the same navigation block that contains the logout command.
/// Profile links elsewhere on the page belong to authors, teams, or ranking rows and must never be
/// interpreted as the current account.
/// </summary>
public static class QojSessionParser
{
    private static readonly HtmlParser Parser = new();
    private static readonly Regex ProfileHrefRegex =
        new(@"/user/profile/([^/?#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static QojSession Parse(string html)
    {
        var doc = Parser.ParseDocument(html ?? string.Empty);
        var logout = doc.QuerySelectorAll("a")
            .FirstOrDefault(IsLogoutLink);
        if (logout == null)
            return new QojSession(false, null);

        for (var container = logout.ParentElement; container != null; container = container.ParentElement)
        {
            var handle = FindHandle(container);
            if (!string.IsNullOrWhiteSpace(handle))
                return new QojSession(true, handle);

            if (container.Matches("header, [role='tablist'], .top-bar, .uoj-header"))
                break;
        }

        // The logout command is authoritative even if a future QOJ theme hides the profile link.
        return new QojSession(true, null);
    }

    private static bool IsLogoutLink(IElement link)
    {
        var href = link.GetAttribute("href") ?? string.Empty;
        return href.Contains("/logout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(link.TextContent.Trim(), "Logout", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindHandle(IElement container)
    {
        foreach (var link in container.QuerySelectorAll("a[href*='/user/profile/']"))
        {
            var match = ProfileHrefRegex.Match(link.GetAttribute("href") ?? string.Empty);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        return null;
    }
}
