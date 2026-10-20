using ClaudeUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Opens when you click the usage button in the dock (or run "Claude usage" from the palette).
/// Opening it triggers a refresh if the data is older than 30 s.
/// </summary>
internal sealed partial class UsageDetailPage : ListPage
{
    private static readonly OptionalColor OkBg = ColorHelpers.FromRgb(0x4C, 0xC2, 0xFF);
    private static readonly OptionalColor WarnBg = ColorHelpers.FromRgb(0xFF, 0xB5, 0x47);
    private static readonly OptionalColor CritBg = ColorHelpers.FromRgb(0xFF, 0x6F, 0x61);
    private static readonly OptionalColor TagText = ColorHelpers.FromRgb(0x14, 0x14, 0x14);

    private readonly UsageMonitor _monitor;
    private readonly RefreshCommand _refresh;
    private readonly OpenUrlCommand _openUsage;

    public UsageDetailPage(UsageMonitor monitor, RefreshCommand refresh, OpenUrlCommand openUsage)
    {
        _monitor = monitor;
        _refresh = refresh;
        _openUsage = openUsage;

        Id = Ids.Detail;
        Name = "Open details";
        Title = "Claude usage";
        Icon = Icons.Gauge;
        PlaceholderText = "Filter limits";

        _monitor.Changed += OnMonitorChanged;
    }

    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        IsLoading = _monitor.Snapshot.IsRefreshing;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        _monitor.RefreshIfOlderThan(Config.OpenPageRefreshAge);

        var s = _monitor.Snapshot;
        var now = _monitor.Now;
        var tz = TimeZoneInfo.Local;
        IsLoading = s.IsRefreshing;

        var items = new List<IListItem>();

        if (s.NeedsSignIn)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = s.Message ?? "Sign in to Claude Code",
                Subtitle = "Run claude in a terminal and log in. This page picks up the new sign-in by itself.",
                Icon = Icons.GaugeSignIn,
            });
        }
        else
        {
            if (s.Status is MonitorStatus.Error or MonitorStatus.RateLimited)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = s.Quotas.Count > 0 ? "Showing last known values" : "No data yet",
                    Subtitle = s.Message ?? string.Empty,
                    Icon = Icons.GaugeWarn,
                });
            }

            if (s.Quotas.Count == 0 && s.Status == MonitorStatus.Loading)
            {
                items.Add(new ListItem(new NoOpCommand()) { Title = "Loading…", Icon = Icons.Gauge });
            }

            foreach (var q in s.Quotas)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = q.DisplayName,
                    Subtitle = Format.Bar(q.Utilization) + "   " + Format.ResetLong(q, now, tz),
                    Icon = Icons.ForLevel(q.Level),
                    Tags = [PercentTag(q)],
                    MoreCommands = [new CommandContextItem(_refresh), new CommandContextItem(_openUsage)],
                });
            }
        }

        items.Add(new ListItem(_refresh)
        {
            Title = "Refresh now",
            Subtitle = Format.BandSubtitle(s, tz),
            Icon = Icons.Refresh,
        });

        items.Add(new ListItem(_openUsage)
        {
            Title = "Open usage page in browser",
            Subtitle = "claude.ai/settings/usage",
            Icon = Icons.OpenInBrowser,
        });

        return items.ToArray();
    }

    private static Tag PercentTag(Quota q) => new(Format.Percent(q.Utilization))
    {
        Foreground = TagText,
        Background = q.Level switch
        {
            UsageLevel.Crit => CritBg,
            UsageLevel.Warn => WarnBg,
            _ => OkBg,
        },
    };
}
