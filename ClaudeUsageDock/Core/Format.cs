using System.Globalization;

namespace ClaudeUsageDock.Core;

/// <summary>
/// All user-facing strings. 24-hour clock, dd/MM dates, metric-friendly, culture-invariant.
/// </summary>
internal static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Percent(double utilization) =>
        Math.Max(0, Math.Round(utilization, MidpointRounding.AwayFromZero)).ToString("0", Inv) + "%";

    /// <summary>Ring icon step 0..100, rounded the same way as <see cref="Percent"/> so ring and text agree.</summary>
    public static int RingStep(double utilization) =>
        (int)Math.Clamp(Math.Round(utilization, MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>Short dock label for a headline quota.</summary>
    public static string ShortLabel(Quota q) => q.Key switch
    {
        Quota.SessionKey => "5h",
        Quota.WeeklyKey => "7d",
        _ => q.DisplayName,
    };

    /// <summary>Dock band, usage button: "5h 42% · 7d 18%".</summary>
    public static string BandTitle(UsageSnapshot s)
    {
        if (s.NeedsSignIn)
        {
            return "Sign in";
        }

        var parts = new List<string>(2);
        if (s.Session is { } session)
        {
            parts.Add("5h " + Percent(session.Utilization));
        }

        if (s.Weekly is { } weekly)
        {
            parts.Add("7d " + Percent(weekly.Utilization));
        }

        if (parts.Count > 0)
        {
            return string.Join(" · ", parts);
        }

        return s.Status switch
        {
            MonitorStatus.Loading => "Claude",
            MonitorStatus.Error or MonitorStatus.RateLimited => "Offline",
            _ => "No data",
        };
    }

    /// <summary>Dock band, usage button second line: "Updated 10:29".</summary>
    public static string BandSubtitle(UsageSnapshot s, TimeZoneInfo tz)
    {
        if (s.IsRefreshing)
        {
            return "Updating…";
        }

        if (s.NeedsSignIn)
        {
            return "Open Claude Code";
        }

        var stale = s.Status is MonitorStatus.Error or MonitorStatus.RateLimited;
        if (s.FetchedAt is { } fetched)
        {
            return (stale ? "Stale · " : "Updated ") + Clock(fetched, tz);
        }

        return stale ? (s.Message ?? "Offline") : "Loading…";
    }

    /// <summary>
    /// Dock band, timer button: the 5-hour reset clock ("14:30"), or a countdown
    /// ("in 1 h 12 min") when a headline limit is maxed out.
    /// </summary>
    public static string ResetTitle(UsageSnapshot s, DateTimeOffset now, TimeZoneInfo tz)
    {
        if (s.IsRefreshing)
        {
            return "Updating";
        }

        if (s.NeedsSignIn || s.Quotas.Count == 0)
        {
            return "--:--";
        }

        // If you're locked out, show the wait for the limit that frees you last.
        var blocking = s.Quotas
            .Where(q => q.IsHeadline && q.Utilization >= 100 && q.ResetsAt is { } r && r > now)
            .OrderByDescending(q => q.ResetsAt)
            .FirstOrDefault();
        if (blocking?.ResetsAt is { } until)
        {
            return Countdown(until - now);
        }

        if (s.Session?.ResetsAt is { } reset && reset > now)
        {
            return ClockOrDay(reset, now, tz, withDate: false);
        }

        return "--:--";
    }

    /// <summary>Detail page: "Resets 14:30" today, "Resets Mon 26/10 07:00" otherwise.</summary>
    public static string ResetLong(Quota q, DateTimeOffset now, TimeZoneInfo tz)
    {
        if (q.ResetsAt is not { } r)
        {
            return "No active window";
        }

        return r <= now ? "Resetting now" : "Resets " + ClockOrDay(r, now, tz, withDate: true);
    }

    public static string Countdown(TimeSpan left)
    {
        if (left <= TimeSpan.Zero)
        {
            return "now";
        }

        var totalMinutes = (int)Math.Ceiling(left.TotalMinutes);
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        if (h == 0)
        {
            return $"in {m} min";
        }

        return m == 0 ? $"in {h} h" : $"in {h} h {m} min";
    }

    public static string Clock(DateTimeOffset t, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTime(t, tz).ToString("HH:mm", Inv);

    private static string ClockOrDay(DateTimeOffset t, DateTimeOffset now, TimeZoneInfo tz, bool withDate)
    {
        var local = TimeZoneInfo.ConvertTime(t, tz);
        var today = TimeZoneInfo.ConvertTime(now, tz);
        if (local.Date == today.Date)
        {
            return local.ToString("HH:mm", Inv);
        }

        return local.ToString(withDate ? "ddd dd/MM HH:mm" : "ddd HH:mm", Inv);
    }

    /// <summary>Text meter for the detail page: ▰▰▰▰▱▱▱▱▱▱</summary>
    public static string Bar(double utilization, int cells = 10)
    {
        var filled = (int)Math.Round(Math.Clamp(utilization, 0, 100) / 100 * cells, MidpointRounding.AwayFromZero);
        return new string('▰', filled) + new string('▱', cells - filled);
    }
}
