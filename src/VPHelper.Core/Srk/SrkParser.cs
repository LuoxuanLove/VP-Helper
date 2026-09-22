using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace VPHelper.Core.Srk;

/// <summary>
/// Parses srk (standard-ranklist) v0.3.x JSON into normalized <see cref="SrkRanklist"/>.
/// Tolerant of version differences (0.3.2 ~ 0.3.13): every optional field is existence-checked.
/// All time durations normalized to seconds.
/// </summary>
public static class SrkParser
{
    public static SrkRanklist Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rl = new SrkRanklist
        {
            Type = GetString(root, "type"),
            Version = GetString(root, "version"),
        };

        if (root.TryGetProperty("contest", out var contest))
        {
            rl.Contest.Title = ParseText(contest, "title") ?? string.Empty;
            rl.Contest.StartAt = ParseIsoToUnix(GetString(contest, "startAt"));
            rl.Contest.DurationSeconds = ParseDurationSeconds(contest, "duration");
            rl.Contest.FrozenDurationSeconds = ParseDurationSeconds(contest, "frozenDuration");
        }

        if (root.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in problems.EnumerateArray())
            {
                var prob = new SrkProblem
                {
                    Alias = GetString(p, "alias"),
                    Title = ParseText(p, "title"),
                };
                if (p.TryGetProperty("statistics", out var stat) && stat.ValueKind == JsonValueKind.Object)
                {
                    prob.Accepted = GetInt(stat, "accepted");
                    prob.Submitted = GetInt(stat, "submitted");
                }
                rl.Problems.Add(prob);
            }
        }

        rl.Sorter = ParseSorter(root);
        rl.Series = ParseSeries(root);

        if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rows.EnumerateArray())
                rl.Rows.Add(ParseRow(r));
        }

        return rl;
    }

    private static SrkRow ParseRow(JsonElement r)
    {
        var row = new SrkRow();
        if (r.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            row.User.Id = GetString(user, "id");
            row.User.Name = ParseText(user, "name") ?? string.Empty;
            row.User.Organization = ParseText(user, "organization");
            row.User.Official = GetBool(user, "official") ?? false;
            if (user.TryGetProperty("markers", out var markers) && markers.ValueKind == JsonValueKind.Array)
            {
                row.User.Markers = new List<string>();
                foreach (var m in markers.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.String) row.User.Markers.Add(m.GetString()!);
            }
        }

        if (r.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Object)
        {
            row.SolvedValue = GetInt(score, "value") ?? 0;
            row.PenaltySeconds = ParseDurationSeconds(score, "time") ?? 0;
        }

        if (r.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in statuses.EnumerateArray())
            {
                row.Statuses.Add(new SrkProblemStatus
                {
                    Result = GetString(s, "result"),
                    TimeSeconds = ParseDurationSeconds(s, "time"),
                    Tries = GetInt(s, "tries") ?? 0,
                });
            }
        }
        return row;
    }

    private static SrkSorter? ParseSorter(JsonElement root)
    {
        if (!root.TryGetProperty("sorter", out var sorter) || sorter.ValueKind != JsonValueKind.Object)
            return null;
        var s = new SrkSorter { Algorithm = GetString(sorter, "algorithm") };
        if (sorter.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
        {
            s.PenaltyPerWrongSeconds = ParseDurationSeconds(cfg, "penalty");
            s.TimePrecision = GetString(cfg, "timePrecision");
            s.TimeRounding = GetString(cfg, "timeRounding");
            if (cfg.TryGetProperty("noPenaltyResults", out var npr) && npr.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in npr.EnumerateArray())
                    s.NoPenaltyResults.Add(v.ValueKind == JsonValueKind.Null ? null : v.GetString());
            }
        }
        return s;
    }

    private static List<SrkSeries> ParseSeries(JsonElement root)
    {
        var list = new List<SrkSeries>();
        if (!root.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var se in series.EnumerateArray())
        {
            var item = new SrkSeries { Title = ParseText(se, "title") };
            if (se.TryGetProperty("rule", out var rule) && rule.ValueKind == JsonValueKind.Object)
            {
                item.Preset = GetString(rule, "preset");
                if (rule.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
                {
                    if (opts.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Object
                        && count.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Array)
                    {
                        var arr = new List<int>();
                        foreach (var v in val.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.Number) arr.Add(v.GetInt32());
                        if (arr.Count > 0) item.MedalCounts = arr.ToArray();
                    }
                    if (opts.TryGetProperty("filter", out var filter) && filter.ValueKind == JsonValueKind.Object)
                        item.FilterByMarker = GetString(filter, "byMarker");
                }
            }
            list.Add(item);
        }
        return list;
    }

    // --- primitives ---

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static bool? GetBool(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>Text may be a plain string or an i18n object {fallback, en, zh-CN, ...}. Prefer fallback, then zh-CN, then any.</summary>
    private static string? ParseText(JsonElement parent, string prop)
    {
        if (!parent.TryGetProperty(prop, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("fallback", out var fb) && fb.ValueKind == JsonValueKind.String) return fb.GetString();
            if (el.TryGetProperty("zh-CN", out var zh) && zh.ValueKind == JsonValueKind.String) return zh.GetString();
            foreach (var m in el.EnumerateObject())
                if (m.Value.ValueKind == JsonValueKind.String) return m.Value.GetString();
        }
        return null;
    }

    /// <summary>Parse a [value, unit] TimeDuration tuple into seconds. Units: ms|s|min|h|d.</summary>
    internal static long? ParseDurationSeconds(JsonElement parent, string prop)
    {
        if (!parent.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        var arr = el;
        if (arr.GetArrayLength() < 2) return null;
        var valEl = arr[0];
        var unitEl = arr[1];
        if (valEl.ValueKind != JsonValueKind.Number || unitEl.ValueKind != JsonValueKind.String) return null;
        double value = valEl.GetDouble();
        string unit = unitEl.GetString()!;
        double seconds = unit switch
        {
            "ms" => value / 1000.0,
            "s" => value,
            "min" => value * 60.0,
            "h" => value * 3600.0,
            "d" => value * 86400.0,
            _ => value, // unknown unit: treat as seconds
        };
        return (long)Math.Round(seconds);
    }

    /// <summary>Parse ISO8601 (with offset) into unix seconds.</summary>
    internal static long? ParseIsoToUnix(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return dto.ToUnixTimeSeconds();
        return null;
    }
}
