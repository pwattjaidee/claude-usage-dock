using ClaudeUsageDock.Core;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Dock text can't be colored, so the gauge icon carries the alert level.
/// Swap the PNGs in Assets\ for your own icon (keep the file names).
/// </summary>
internal static class Icons
{
    // FromRelativePaths(lightThemeIcon, darkThemeIcon): dark glyph on light theme, light glyph on dark theme.
    public static readonly IconInfo Gauge = IconHelpers.FromRelativePaths("Assets\\gauge-for-light-theme.png", "Assets\\gauge-for-dark-theme.png");
    public static readonly IconInfo GaugeWarn = IconHelpers.FromRelativePath("Assets\\gauge-warn.png");
    public static readonly IconInfo GaugeCrit = IconHelpers.FromRelativePath("Assets\\gauge-crit.png");
    public static readonly IconInfo GaugeSignIn = IconHelpers.FromRelativePath("Assets\\gauge-signin.png");

    // Segoe Fluent Icons glyphs.
    public static readonly IconInfo Refresh = new("");
    public static readonly IconInfo OpenInBrowser = new("");

    // Ring gauges: one pre-rendered PNG per level x percent (Assets\Rings), created lazily and reused.
    // A new path per value also sidesteps the host's icon cache.
    private static readonly IconInfo?[,] RingCache = new IconInfo?[3, 101];

    public static IconInfo Ring(double utilization)
    {
        var step = Format.RingStep(utilization);
        var level = RefreshPolicy.LevelOf(utilization);
        var li = (int)level;
        var cached = RingCache[li, step];
        if (cached is not null)
        {
            return cached;
        }

        var name = level switch
        {
            UsageLevel.Crit => "crit",
            UsageLevel.Warn => "warn",
            _ => "ok",
        };
        var icon = IconHelpers.FromRelativePath($"Assets\\Rings\\ring-{name}-{step:000}.png");
        RingCache[li, step] = icon;
        return icon;
    }

    public static IconInfo ForLevel(UsageLevel level) => level switch
    {
        UsageLevel.Crit => GaugeCrit,
        UsageLevel.Warn => GaugeWarn,
        _ => Gauge,
    };

    public static IconInfo For(UsageSnapshot s)
    {
        if (s.NeedsSignIn)
        {
            return GaugeSignIn;
        }

        return s.Quotas.Count == 0 ? Gauge : ForLevel(s.WorstLevel);
    }
}
