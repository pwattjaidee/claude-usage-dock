namespace ClaudeUsageDock.Core;

internal enum UsageLevel
{
    Ok,
    Warn,
    Crit,
}

internal enum MonitorStatus
{
    /// <summary>Started, nothing fetched yet.</summary>
    Loading,

    /// <summary>Last fetch succeeded.</summary>
    Ok,

    /// <summary>No credentials file / no token, or the server rejected the token.</summary>
    SignedOut,

    /// <summary>The token's own expiry time has passed. Claude Code refreshes it next time it runs.</summary>
    TokenExpired,

    /// <summary>Last fetch failed (network, 5xx, unexpected response). Last good values are kept.</summary>
    Error,

    /// <summary>Server said slow down (429). Last good values are kept.</summary>
    RateLimited,
}

internal sealed record Quota(string Key, string DisplayName, double Utilization, DateTimeOffset? ResetsAt)
{
    public const string SessionKey = "five_hour";
    public const string WeeklyKey = "seven_day";

    public bool IsHeadline => Key is SessionKey or WeeklyKey;

    public UsageLevel Level => RefreshPolicy.LevelOf(Utilization);
}

internal sealed record UsageSnapshot(
    MonitorStatus Status,
    IReadOnlyList<Quota> Quotas,
    DateTimeOffset? FetchedAt,
    string? Message,
    bool IsRefreshing)
{
    public static readonly UsageSnapshot Initial = new(MonitorStatus.Loading, Array.Empty<Quota>(), null, null, false);

    public Quota? Session => Quotas.FirstOrDefault(q => q.Key == Quota.SessionKey);

    public Quota? Weekly => Quotas.FirstOrDefault(q => q.Key == Quota.WeeklyKey);

    public bool NeedsSignIn => Status is MonitorStatus.SignedOut or MonitorStatus.TokenExpired;

    /// <summary>Worst level across the headline quotas (falls back to all quotas).</summary>
    public UsageLevel WorstLevel
    {
        get
        {
            var pool = Quotas.Where(q => q.IsHeadline).ToList();
            if (pool.Count == 0)
            {
                pool = Quotas.ToList();
            }

            return pool.Count == 0 ? UsageLevel.Ok : pool.Max(q => q.Level);
        }
    }
}
