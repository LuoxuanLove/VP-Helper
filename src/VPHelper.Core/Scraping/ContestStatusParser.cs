using System;
using System.Collections.Generic;
using System.Text.Json;
using VPHelper.Core.Models;

namespace VPHelper.Core.Scraping;

/// <summary>
/// A submission row parsed from the Codeforces <c>contest.status</c> API JSON. Unlike the HTML
/// submissions page, this carries <see cref="ParticipantType"/> — the only machine-readable source
/// that separates a virtual run from post-contest practice (DESIGN 6.2).
/// </summary>
public record ApiSubmission(
    long Id,
    long GymId,
    string ProblemIndex,
    string ProblemName,
    string Verdict,
    long SubmitTimeUnix,
    string ParticipantType,
    long? RelativeTime);

/// <summary>Outcome of parsing a <c>contest.status</c> response.</summary>
public record ContestStatusResult(
    bool Ok,
    IReadOnlyList<ApiSubmission> Submissions,
    string? Comment);

/// <summary>
/// Parses the Codeforces <c>contest.status</c> API envelope. This endpoint (unlike the HTML
/// submissions page) returns each submission's <c>author.participantType</c>, which is how VP is
/// told apart from practice. Public gyms return JSON to the logged-in browser session anonymously;
/// private/group gyms would need an API key (surfaced via a non-OK comment for the caller to
/// report rather than silently mis-classify).
/// </summary>
public static class ContestStatusParser
{
    /// <summary>
    /// Build the API URL for one gym + handle. <paramref name="count"/> caps rows per call; the CF
    /// API tolerates a large count and simply returns fewer when the gym has fewer submissions.
    /// </summary>
    public static string BuildUrl(long gymId, string handle, int from = 1, int count = 10000)
        => $"https://codeforces.com/api/contest.status?contestId={gymId}"
         + $"&handle={Uri.EscapeDataString(handle)}&from={from}&count={count}";

    /// <summary>
    /// Parse a <c>contest.status</c> response body. A non-OK CF envelope (e.g. a private gym needing
    /// auth, or a bad contestId) yields <c>Ok=false</c> with the CF comment so the caller can report
    /// it; malformed JSON yields <c>Ok=false</c> too. Only rows whose problem carries a numeric
    /// <c>contestId</c> (the gym id) are returned.
    /// </summary>
    public static ContestStatusResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ContestStatusResult(false, Array.Empty<ApiSubmission>(), "empty response");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return new ContestStatusResult(false, Array.Empty<ApiSubmission>(), $"invalid JSON: {ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new ContestStatusResult(false, Array.Empty<ApiSubmission>(), "unexpected root");

            var status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString() : null;
            if (!string.Equals(status, "OK", StringComparison.Ordinal))
            {
                var comment = root.TryGetProperty("comment", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : $"status={status ?? "?"}";
                return new ContestStatusResult(false, Array.Empty<ApiSubmission>(), comment);
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return new ContestStatusResult(true, Array.Empty<ApiSubmission>(), null);

            var list = new List<ApiSubmission>();
            foreach (var row in result.EnumerateArray())
            {
                var parsed = ParseRow(row);
                if (parsed != null) list.Add(parsed);
            }
            return new ContestStatusResult(true, list, null);
        }
    }

    private static ApiSubmission? ParseRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetLong(row, "id", out var id)) return null;

        // creationTimeSeconds is the absolute submit time; relativeTimeSeconds is offset from the
        // participation start (int-max sentinel for practice, which we normalize to null).
        long submitTime = TryGetLong(row, "creationTimeSeconds", out var cts) ? cts : 0;
        long? relative = TryGetLong(row, "relativeTimeSeconds", out var rel) && rel < int.MaxValue ? rel : null;

        string verdict = row.TryGetProperty("verdict", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty; // absent verdict ⇒ still being judged; treat as blank

        long gymId = 0;
        string index = string.Empty;
        string name = string.Empty;
        if (row.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object)
        {
            if (TryGetLong(problem, "contestId", out var pcid)) gymId = pcid;
            if (problem.TryGetProperty("index", out var pi) && pi.ValueKind == JsonValueKind.String)
                index = pi.GetString() ?? string.Empty;
            if (problem.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String)
                name = pn.GetString() ?? string.Empty;
        }
        if (gymId == 0) return null; // a gym submission must resolve its contest id

        string participantType = string.Empty;
        if (row.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
            && author.TryGetProperty("participantType", out var pt) && pt.ValueKind == JsonValueKind.String)
            participantType = pt.GetString() ?? string.Empty;

        return new ApiSubmission(id, gymId,
            string.IsNullOrEmpty(index) ? "?" : index, name, verdict, submitTime, participantType, relative);
    }

    private static bool TryGetLong(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt64(out value);
    }
}
