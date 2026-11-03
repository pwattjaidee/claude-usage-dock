using System.Text;

namespace ClaudeUsageDock.Core;

/// <summary>
/// The PowerShell script the "Sign in with Claude Code" terminal runs:
///   1. claude auth status   - checks the login and exits; sends no message, uses no quota,
///                             starts no 5-hour window.
///   2. claude auth login    - only if there's still no valid login: opens the browser.
///   3. Closes the window by itself when signed in; stays open with a message otherwise.
/// The extension never sees or handles the login; Claude Code writes it to its own file.
/// </summary>
internal static class SignInScript
{
    public static string Build(string claudePath)
    {
        var claude = claudePath.Replace("'", "''", StringComparison.Ordinal);
        return """
$Host.UI.RawUI.WindowTitle = 'Claude Code sign-in'
$claude = '__CLAUDE__'
$dir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$cred = Join-Path $dir '.credentials.json'

# True when Claude Code's credentials file holds a login valid for at least another minute.
function Test-Login {
    try {
        $o = (Get-Content -LiteralPath $cred -Raw -ErrorAction Stop | ConvertFrom-Json).claudeAiOauth
        return [bool]($o.accessToken) -and ([long]$o.expiresAt -gt ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + 60000))
    } catch { return $false }
}

function Close-Done {
    Write-Host "`nSigned in. Claude Usage Dock updates in a moment. Closing..." -ForegroundColor Green
    Start-Sleep -Seconds 3
    exit 0
}

Write-Host 'Checking your Claude Code sign-in...' -ForegroundColor Cyan
try { & $claude auth status --text } catch { Write-Host $_.Exception.Message -ForegroundColor Red }
if (Test-Login) { Close-Done }

Write-Host "`nSign-in needed. Your browser will open: finish signing in there." -ForegroundColor Yellow
try { & $claude auth login } catch { Write-Host $_.Exception.Message -ForegroundColor Red }
if (Test-Login) { Close-Done }

Write-Host "`nStill not signed in. Run 'claude' and use /login, or check the messages above." -ForegroundColor Red
Read-Host 'Press Enter to close'
""".Replace("__CLAUDE__", claude, StringComparison.Ordinal);
    }

    /// <summary>For powershell.exe -EncodedCommand: base64 of UTF-16LE. Avoids every quoting problem.</summary>
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
