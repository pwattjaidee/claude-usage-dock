using ClaudeUsageDock.Core;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Manual refresh. Used by the dock timer button, both right-click menus,
/// the detail page, and the "Refresh Claude usage" palette command.
/// Debouncing lives in <see cref="UsageMonitor"/>, so spamming this is harmless.
/// </summary>
internal sealed partial class RefreshCommand : InvokableCommand
{
    private readonly UsageMonitor _monitor;

    public RefreshCommand(UsageMonitor monitor)
    {
        _monitor = monitor;
        Id = Ids.Refresh;
        Name = "Refresh now";
        Icon = Icons.Refresh;
    }

    public override CommandResult Invoke()
    {
        _monitor.RequestRefresh();
        return CommandResult.KeepOpen();
    }
}

internal static class Ids
{
    public const string Provider = "pwattjaidee.claudeusagedock";
    public const string Band = Provider + ".band";
    public const string Detail = Provider + ".detail";
    public const string Refresh = Provider + ".refresh";
    public const string OpenUsagePage = Provider + ".openusagepage";
}
