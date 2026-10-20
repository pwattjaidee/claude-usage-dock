using ClaudeUsageDock.Core;
using ClaudeUsageDock.Platform;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

public sealed partial class ClaudeUsageDockCommandsProvider : CommandProvider
{
    private readonly UsageMonitor _monitor;
    private readonly UsageBandController _bandController;
    private readonly ICommandItem[] _commands;
    private readonly ICommandItem _band;

    public ClaudeUsageDockCommandsProvider()
    {
        Id = Ids.Provider; // dock bands require a provider Id
        DisplayName = "Claude Usage";
        Icon = Icons.Gauge;

        _monitor = new UsageMonitor(IdleDetector.GetIdleTime);

        var refresh = new RefreshCommand(_monitor);
        var openUsage = new OpenUrlCommand(Config.UsagePageUrl)
        {
            Id = Ids.OpenUsagePage,
            Name = "Open usage page",
            Icon = Icons.OpenInBrowser,
        };
        var detail = new UsageDetailPage(_monitor, refresh, openUsage);

        // The dock band: [ring 5h] [ring 7d] [timer], or [status] [timer] when there's no data.
        _bandController = new UsageBandController(_monitor, detail, refresh, openUsage);
        _band = _bandController.Band;

        // Palette entries (Win+Alt+Space): "Claude usage" and "Refresh Claude usage".
        _commands =
        [
            new CommandItem(detail)
            {
                Title = "Claude usage",
                Subtitle = "Session and weekly limits",
                MoreCommands = [new CommandContextItem(refresh), new CommandContextItem(openUsage)],
            },
            new CommandItem(refresh)
            {
                Title = "Refresh Claude usage",
                Subtitle = "Fetch the latest numbers now",
            },
        ];

        _monitor.Start();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_band];

    public override void Dispose()
    {
        // Safe to call twice (host and extension both may).
        _bandController.Dispose();
        _monitor.Dispose();
        base.Dispose();
    }
}
