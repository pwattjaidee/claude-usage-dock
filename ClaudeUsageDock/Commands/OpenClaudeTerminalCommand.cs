using System.ComponentModel;
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Opens a terminal running Claude Code, so you can sign in (or let it renew its login)
/// without opening a terminal yourself. The extension only launches it; you do the login.
/// Windows Terminal is used when installed, plain PowerShell otherwise. The window stays
/// open after Claude Code exits so any error stays readable.
/// </summary>
internal sealed partial class OpenClaudeTerminalCommand : InvokableCommand
{
    public OpenClaudeTerminalCommand()
    {
        Id = Ids.OpenTerminal;
        Name = "Sign in with Claude Code";
        Icon = Icons.Terminal;
    }

    public override CommandResult Invoke()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claude = FindClaude(home);

        // PowerShell runs claude; -NoExit keeps the window if claude is missing or fails.
        var psCommand = "& '" + claude.Replace("'", "''", StringComparison.Ordinal) + "'";
        var psArgs = "-NoLogo -NoExit -Command \"" + psCommand + "\"";

        try
        {
            Start("wt.exe", $"-d \"{home}\" --title \"Claude Code\" powershell.exe {psArgs}", home);
        }
        catch (Win32Exception)
        {
            // Windows Terminal not installed: fall back to a plain PowerShell window.
            try
            {
                Start("powershell.exe", psArgs, home);
            }
            catch (Win32Exception)
            {
                return CommandResult.ShowToast("Couldn't open a terminal. Run 'claude' in PowerShell yourself.");
            }
        }

        // The band re-checks the credentials file every 60 s while signed out, so it
        // picks up the new login by itself once you finish in the terminal.
        return CommandResult.Dismiss();
    }

    /// <summary>The native installer's location first, then whatever "claude" resolves to on PATH (WinGet, npm).</summary>
    private static string FindClaude(string home)
    {
        var native = Path.Combine(home, ".local", "bin", "claude.exe");
        return File.Exists(native) ? native : "claude";
    }

    private static void Start(string file, string args, string workingDir)
    {
        using var _ = Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDir,
        });
    }
}
