namespace ClaudeUsageDock.Core;

/// <summary>
/// Pure decisions about levels and poll timing. No clocks, no I/O: easy to test.
/// </summary>
internal static class RefreshPolicy
{
    public static UsageLevel LevelOf(double percent) =>
        percent >= Config.CritPercent ? UsageLevel.Crit :
        percent >= Config.WarnPercent ? UsageLevel.Warn :
        UsageLevel.Ok;

    /// <summary>
    /// Under 60%: 5 min. 60-84%: 2 min. 85%+ or a headline quota resetting within 10 min: 1 min.
    /// </summary>
    public static TimeSpan NextInterval(IReadOnlyList<Quota> quotas, DateTimeOffset now)
    {
        var headline = quotas.Where(q => q.IsHeadline).ToList();
        if (headline.Count == 0)
        {
            headline = quotas.ToList();
        }

        if (headline.Count == 0)
        {
            return Config.PollCalm;
        }

        var max = headline.Max(q => q.Utilization);
        var resetSoon = headline.Any(q =>
            q.ResetsAt is { } r && r > now && r - now <= Config.HotResetWindow);

        if (max >= Config.CritPercent || resetSoon)
        {
            return Config.PollHot;
        }

        return max >= Config.WarnPercent ? Config.PollWarm : Config.PollCalm;
    }

    /// <summary>1, 2, 4, 8 min ... capped at 15 min.</summary>
    public static TimeSpan Backoff(int consecutiveFailures)
    {
        var n = Math.Clamp(consecutiveFailures, 1, 10);
        var ticks = Config.BackoffBase.Ticks * (1L << (n - 1));
        return ticks >= Config.MaxBackoff.Ticks ? Config.MaxBackoff : TimeSpan.FromTicks(ticks);
    }
}
