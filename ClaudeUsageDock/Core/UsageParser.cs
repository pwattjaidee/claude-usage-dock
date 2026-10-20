using System.Globalization;
using System.Text.Json;

namespace ClaudeUsageDock.Core;

/// <summary>
/// Turns the usage endpoint's JSON into a quota list. Uses JsonDocument only
/// (no reflection), so it stays trim/AOT-safe.
///
/// Response shape (unofficial, may change):
///   { "five_hour": { "utilization": 42.0, "resets_at": "2026-10-20T07:30:00+00:00" },
///     "seven_day": { ... }, "seven_day_opus": { ... } | null, ...,
///     "limits": [ { "kind", "group", "percent", "resets_at", "scope": { "model": { "display_name" } } } ] }
/// Newer responses carry per-model weekly limits only inside "limits"; those are
/// mapped onto the same prefix as the unscoped limit that shares their group.
/// </summary>
internal static class UsageParser
{
    public static IReadOnlyList<Quota> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Usage response is not a JSON object.");
        }

        var quotas = new List<Quota>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var resetToKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var prop in root.EnumerateObject())
        {
            var value = prop.Value;
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("utilization", out var util) ||
                util.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var rawReset = ReadString(value, "resets_at");
            var reset = ParseTime(rawReset);

            // Hide placeholder fields that are neither a known period nor have a reset window.
            if (reset is null && !IsKnownPeriod(prop.Name))
            {
                continue;
            }

            quotas.Add(new Quota(prop.Name, DisplayNameFor(prop.Name), util.GetDouble(), reset));
            seenKeys.Add(prop.Name);
            if (!string.IsNullOrEmpty(rawReset))
            {
                resetToKey.TryAdd(rawReset, prop.Name);
            }
        }

        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            AddScopedLimits(limits, resetToKey, seenKeys, quotas);
        }

        // Session first, weekly second, the rest in response order.
        return quotas
            .Select((q, i) => (q, i))
            .OrderBy(t => t.q.Key == Quota.SessionKey ? 0 : t.q.Key == Quota.WeeklyKey ? 1 : 2)
            .ThenBy(t => t.i)
            .Select(t => t.q)
            .ToList();
    }

    private static void AddScopedLimits(
        JsonElement limits,
        Dictionary<string, string> resetToKey,
        HashSet<string> seenKeys,
        List<Quota> quotas)
    {
        // group -> key prefix, learned from the unscoped limit of that group.
        var groupPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object || HasScope(limit))
            {
                continue;
            }

            var group = ReadString(limit, "group");
            var reset = ReadString(limit, "resets_at");
            if (!string.IsNullOrEmpty(group) && !string.IsNullOrEmpty(reset) && resetToKey.TryGetValue(reset, out var key))
            {
                groupPrefix.TryAdd(group, key);
            }
        }

        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object || !HasScope(limit))
            {
                continue;
            }

            var group = ReadString(limit, "group");
            var model = ReadModelName(limit);
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(model) || !groupPrefix.TryGetValue(group, out var prefix))
            {
                continue;
            }

            if (!limit.TryGetProperty("percent", out var pct) || pct.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var key = prefix + "_" + Slug(model);
            if (!seenKeys.Add(key))
            {
                continue; // a top-level field already carries this one, with better precision
            }

            var reset = ParseTime(ReadString(limit, "resets_at"));
            quotas.Add(new Quota(key, PeriodLabel(prefix) + " · " + model, pct.GetDouble(), reset));
        }
    }

    public static string DisplayNameFor(string key)
    {
        if (key == Quota.SessionKey)
        {
            return "Session · 5 hours";
        }

        if (key == Quota.WeeklyKey)
        {
            return "Weekly · all models";
        }

        foreach (var prefix in new[] { Quota.SessionKey, Quota.WeeklyKey })
        {
            if (key.StartsWith(prefix + "_", StringComparison.Ordinal))
            {
                return PeriodLabel(prefix) + " · " + Pretty(key[(prefix.Length + 1)..]);
            }
        }

        return Pretty(key);
    }

    private static string PeriodLabel(string prefix) => prefix switch
    {
        Quota.SessionKey => "Session",
        Quota.WeeklyKey => "Weekly",
        _ => Pretty(prefix),
    };

    private static bool IsKnownPeriod(string key) =>
        key == Quota.SessionKey || key == Quota.WeeklyKey ||
        key.StartsWith(Quota.SessionKey + "_", StringComparison.Ordinal) ||
        key.StartsWith(Quota.WeeklyKey + "_", StringComparison.Ordinal);

    private static string Pretty(string raw)
    {
        var words = raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant() switch
            {
                "oauth" => "OAuth",
                "api" => "API",
                "ai" => "AI",
                _ => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant(),
            });
        return string.Join(' ', words);
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    private static bool HasScope(JsonElement limit) =>
        limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object;

    private static string? ReadModelName(JsonElement limit)
    {
        if (limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object &&
            scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            return ReadString(model, "display_name");
        }

        return null;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? ParseTime(string? raw) =>
        !string.IsNullOrEmpty(raw) &&
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
}
