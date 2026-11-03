using System.ComponentModel;
using System.Diagnostics;
using ClaudeUsageDock.Core;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Opens a terminal that checks the Claude Code sign-in (claude auth status), signs in only if
/// needed (claude auth login), then closes itself. The extension watches Claude Code's
/// credentials file and refreshes the band as soon as it changes. No message is sent to Claude,
/// so this uses no quota and starts no 5-hour window. Windows Terminal if installed, else PowerShell.
/// </summary>
internal sealed partial class OpenClaudeTerminalCommand : InvokableCommand
{
    private static readonly TimeSpan WatchFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(2);

    private readonly UsageMonitor _monitor;
    private int _watching;

    public OpenClaudeTerminalCommand(UsageMonitor monitor)
    {
        _monitor = monitor;
        Id = Ids.OpenTerminal;
        Name = "Sign in with Claude Code";
        Icon = Icons.Terminal;
    }

    public override CommandResult Invoke()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var encoded = SignInScript.Encode(SignInScript.Build(FindClaude(home)));
        var psArgs = "-NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;

        try
        {
            Start("wt.exe", $"-d \"{home}\" --title \"Claude Code sign-in\" powershell.exe {psArgs}", home);
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
                return CommandResult.ShowToast("Couldn't open a terminal. Run 'claude auth login' in PowerShell yourself.");
            }
        }

        WatchCredentials();
        return CommandResult.Dismiss();
    }

    /// <summary>
    /// Refresh the band the moment Claude Code rewrites its credentials file, instead of waiting
    /// for the 60 s signed-out re-check. Watches the file's timestamp only, never its contents.
    /// </summary>
    private void WatchCredentials()
    {
        if (Interlocked.Exchange(ref _watching, 1) == 1)
        {
            return; // already watching from an earlier click
        }

        var path = CredentialReader.CredentialsPath;
        var before = Stamp(path);
        _ = Task.Run(async () =>
        {
            try
            {
                var until = DateTime.UtcNow + WatchFor;
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(WatchEvery).ConfigureAwait(false);
                    if (Stamp(path) != before)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false); // let the write finish
                        _monitor.RequestRefresh();
                        return;
                    }
                }

                // No change (the login was already valid): one refresh so the band catches up anyway.
                _monitor.RequestRefresh();
            }
            finally
            {
                Interlocked.Exchange(ref _watching, 0);
            }
        });
    }

    private static DateTime Stamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
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
