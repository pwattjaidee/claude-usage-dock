using ClaudeUsageDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// A ring button for one headline quota: ring icon (filled to the %, colored by level) + "5h 40%".
/// Click: opens the detail page. Right-click: Refresh now / Open usage page.
/// </summary>
internal sealed partial class QuotaBandItem : ListItem
{
    private readonly string _key;
    private IconInfo? _icon;

    public QuotaBandItem(string key, string subtitle, UsageDetailPage detail, RefreshCommand refresh, OpenUrlCommand openUsage, OpenClaudeTerminalCommand terminal)
        : base(detail)
    {
        _key = key;
        Title = key == Quota.SessionKey ? "5h" : "7d";
        Subtitle = subtitle;
        Icon = _icon = Icons.Ring(0);
        MoreCommands = [new CommandContextItem(refresh), new CommandContextItem(openUsage), new CommandContextItem(terminal)];
    }

    public string Key => _key;

    public void Render(Quota q)
    {
        var title = Format.ShortLabel(q) + " " + Format.Percent(q.Utilization);
        if (Title != title)
        {
            Title = title;
        }

        var icon = Icons.Ring(q.Utilization);
        if (!ReferenceEquals(icon, _icon))
        {
            _icon = icon;
            Icon = icon;
        }
    }
}

/// <summary>
/// Shown instead of the rings when there's no data: "Sign in", "Claude" (loading), "Offline".
/// </summary>
internal sealed partial class StatusBandItem : ListItem
{
    private IconInfo? _icon;

    public StatusBandItem(UsageDetailPage detail, RefreshCommand refresh, OpenUrlCommand openUsage, OpenClaudeTerminalCommand terminal)
        : base(detail)
    {
        Title = "Claude";
        Subtitle = "Loading…";
        Icon = _icon = Icons.Gauge;
        MoreCommands = [new CommandContextItem(refresh), new CommandContextItem(openUsage), new CommandContextItem(terminal)];
    }

    public void Render(UsageSnapshot s, TimeZoneInfo tz)
    {
        var title = Format.BandTitle(s);
        if (Title != title)
        {
            Title = title;
        }

        var subtitle = Format.BandSubtitle(s, tz);
        if (Subtitle != subtitle)
        {
            Subtitle = subtitle;
        }

        var icon = Icons.For(s);
        if (!ReferenceEquals(icon, _icon))
        {
            _icon = icon;
            Icon = icon;
        }
    }
}

/// <summary>
/// Timer button: refresh icon + session reset time ("15:40"), or a countdown when a limit is maxed.
/// Subtitle: "Updated 13:28" / "Updating…" / "Stale · 13:28". Click: refresh now.
/// </summary>
internal sealed partial class ResetBandItem : ListItem
{
    public ResetBandItem(RefreshCommand refresh, UsageDetailPage detail, OpenUrlCommand openUsage, OpenClaudeTerminalCommand terminal)
        : base(refresh)
    {
        Title = "--:--";
        Subtitle = "Refresh";
        Icon = Icons.Refresh;
        MoreCommands = [new CommandContextItem(detail), new CommandContextItem(openUsage), new CommandContextItem(terminal)];
    }

    public void Render(UsageSnapshot s, DateTimeOffset now, TimeZoneInfo tz)
    {
        var title = Format.ResetTitle(s, now, tz);
        if (Title != title)
        {
            Title = title;
        }

        var subtitle = Format.BandSubtitle(s, tz);
        if (Subtitle != subtitle)
        {
            Subtitle = subtitle;
        }
    }
}

/// <summary>
/// Owns the dock band and decides which buttons it shows:
///   data:      [ring 5h] [ring 7d] [timer]
///   no data:   [status]           [timer]
/// Re-renders on every monitor change and every 30 s locally (countdown), with no network call.
/// </summary>
internal sealed partial class UsageBandController : IDisposable
{
    private readonly UsageMonitor _monitor;
    private readonly QuotaBandItem _session;
    private readonly QuotaBandItem _weekly;
    private readonly StatusBandItem _status;
    private readonly ResetBandItem _reset;
    private readonly Timer _tick;
    private readonly object _renderLock = new();
    private IListItem[] _current = [];

    public UsageBandController(UsageMonitor monitor, UsageDetailPage detail, RefreshCommand refresh, OpenUrlCommand openUsage, OpenClaudeTerminalCommand terminal)
    {
        _monitor = monitor;
        _session = new QuotaBandItem(Quota.SessionKey, "Session", detail, refresh, openUsage, terminal);
        _weekly = new QuotaBandItem(Quota.WeeklyKey, "Weekly", detail, refresh, openUsage, terminal);
        _status = new StatusBandItem(detail, refresh, openUsage, terminal);
        _reset = new ResetBandItem(refresh, detail, openUsage, terminal);

        _current = [_status, _reset];
        Band = new WrappedDockItem(_current, Ids.Band, "Claude usage");

        _monitor.Changed += OnChanged;
        _tick = new Timer(_ => Render(), null, Config.UiTick, Config.UiTick);
        Render();
    }

    public WrappedDockItem Band { get; }

    private void OnChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        // Poller thread and timer thread can both land here; keep renders serial.
        lock (_renderLock)
        {
            try
            {
                var s = _monitor.Snapshot;
                var tz = TimeZoneInfo.Local;
                var wanted = new List<IListItem>(3);

                if (!s.NeedsSignIn && (s.Session is not null || s.Weekly is not null))
                {
                    if (s.Session is { } session)
                    {
                        _session.Render(session);
                        wanted.Add(_session);
                    }

                    if (s.Weekly is { } weekly)
                    {
                        _weekly.Render(weekly);
                        wanted.Add(_weekly);
                    }
                }
                else
                {
                    _status.Render(s, tz);
                    wanted.Add(_status);
                }

                _reset.Render(s, _monitor.Now, tz);
                wanted.Add(_reset);

                // Only swap the band's items when the set of buttons actually changes.
                if (!wanted.SequenceEqual(_current))
                {
                    _current = wanted.ToArray();
                    Band.Items = _current;
                }
            }
            catch (Exception)
            {
                // Host went away mid-update (e.g. palette restarting). Next tick retries.
            }
        }
    }

    public void Dispose()
    {
        _monitor.Changed -= OnChanged;
        _tick.Dispose();
    }
}
