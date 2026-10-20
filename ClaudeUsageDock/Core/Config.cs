namespace ClaudeUsageDock.Core;

/// <summary>
/// Every tunable knob in one place. Change these and rebuild.
/// </summary>
internal static class Config
{
    // --- Endpoint (unofficial: the same one Claude Code's /usage uses) ---
    public const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "oauth-2025-04-20";
    public const string UserAgent = "ClaudeUsageDock/0.1";
    public const string UsagePageUrl = "https://claude.ai/settings/usage";

    // --- Alert thresholds (percent used) ---
    public const double WarnPercent = 60;
    public const double CritPercent = 85;

    // --- Adaptive polling ---
    public static readonly TimeSpan PollCalm = TimeSpan.FromMinutes(5);   // under 60%
    public static readonly TimeSpan PollWarm = TimeSpan.FromMinutes(2);   // 60-84%
    public static readonly TimeSpan PollHot = TimeSpan.FromMinutes(1);    // 85%+ or reset within HotResetWindow
    public static readonly TimeSpan HotResetWindow = TimeSpan.FromMinutes(10);

    // Floor for automatic polls, and the manual-refresh debounce.
    public static readonly TimeSpan MinAutoPollGap = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ManualDebounce = TimeSpan.FromSeconds(10);

    // Opening the detail page refreshes only if the data is older than this.
    public static readonly TimeSpan OpenPageRefreshAge = TimeSpan.FromSeconds(30);

    // Stop polling when nobody has touched keyboard/mouse for this long (also covers a locked screen).
    public static readonly TimeSpan IdlePause = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IdleRecheck = TimeSpan.FromSeconds(60);

    // Errors: exponential backoff 1, 2, 4, 8 min, capped here.
    public static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    // Signed out: re-check the local credentials file (no network) this often.
    public static readonly TimeSpan SignedOutRecheck = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    // Local re-render cadence for the reset countdown (no network).
    public static readonly TimeSpan UiTick = TimeSpan.FromSeconds(30);
}
