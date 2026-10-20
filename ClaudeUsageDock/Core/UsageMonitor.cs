namespace ClaudeUsageDock.Core;

/// <summary>
/// The single background poller. One loop, one request at a time.
///
/// Schedule: adaptive interval from <see cref="RefreshPolicy"/>, never faster than
/// <see cref="Config.MinAutoPollGap"/> on its own. Manual refreshes (click, context
/// menu, palette command) wake it early, debounced by <see cref="Config.ManualDebounce"/>.
/// Paused while the user is idle. On a rejected token it stops calling the server
/// until Claude Code writes a different token.
/// </summary>
internal sealed partial class UsageMonitor : IDisposable
{
    private const string SignInMessage = "Sign in to Claude Code";
    private const string ExpiredMessage = "Sign-in expired. Open Claude Code to refresh it.";

    private readonly Func<ClaudeCredential?> _readCredential;
    private readonly IUsageFetcher _fetcher;
    private readonly bool _ownsFetcher;
    private readonly Func<TimeSpan> _idleTime;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();

    private Task? _loop;
    private volatile bool _disposed;
    private int _failures;
    private string? _rejectedToken;
    private DateTimeOffset _lastFetchStart = DateTimeOffset.MinValue;
    private DateTimeOffset _nextDue;
    private volatile UsageSnapshot _snapshot = UsageSnapshot.Initial;

    public UsageMonitor(Func<TimeSpan> idleTime)
        : this(() => CredentialReader.TryRead(), new UsageClient(), idleTime, TimeProvider.System, ownsFetcher: true)
    {
    }

    internal UsageMonitor(
        Func<ClaudeCredential?> readCredential,
        IUsageFetcher fetcher,
        Func<TimeSpan> idleTime,
        TimeProvider time,
        bool ownsFetcher = false)
    {
        _readCredential = readCredential;
        _fetcher = fetcher;
        _idleTime = idleTime;
        _time = time;
        _ownsFetcher = ownsFetcher;
    }

    /// <summary>Raised on any snapshot change, from the poller thread.</summary>
    public event EventHandler? Changed;

    public UsageSnapshot Snapshot => _snapshot;

    public DateTimeOffset Now => _time.GetUtcNow();

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Ask for a fetch now. Safe from any thread; extra calls collapse into one.</summary>
    public void RequestRefresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
        catch (ObjectDisposedException)
        {
            // Raced with shutdown.
        }
    }

    /// <summary>Refresh only if the data is older than <paramref name="maxAge"/> (used when the detail page opens).</summary>
    public void RefreshIfOlderThan(TimeSpan maxAge)
    {
        var s = _snapshot;
        if (s.IsRefreshing)
        {
            return;
        }

        if (s.FetchedAt is not { } fetched || _time.GetUtcNow() - fetched >= maxAge)
        {
            RequestRefresh();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var manual = false;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await TickAsync(manual, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Never let the loop die. Back off and keep the last good values.
                _failures++;
                Publish(_snapshot with { Status = MonitorStatus.Error, Message = "Unexpected error", IsRefreshing = false });
                delay = Max(RefreshPolicy.Backoff(_failures), Config.MinAutoPollGap);
            }

            try
            {
                manual = await _wake.WaitAsync(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One scheduling step. Returns how long to sleep before the next one.</summary>
    internal async Task<TimeSpan> TickAsync(bool manual, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        if (manual)
        {
            if (now - _lastFetchStart < Config.ManualDebounce)
            {
                return Remaining(now); // too soon: ignore the click, keep the existing schedule
            }
        }
        else if (_idleTime() >= Config.IdlePause)
        {
            return Schedule(now, Config.IdleRecheck); // nobody is looking: don't poll
        }

        var cred = _readCredential();
        if (cred is null)
        {
            _rejectedToken = null;
            Publish(_snapshot with { Status = MonitorStatus.SignedOut, Message = SignInMessage, IsRefreshing = false });
            return Schedule(now, Config.SignedOutRecheck);
        }

        if (cred.AccessToken == _rejectedToken)
        {
            // Server already said no to this exact token. Wait for Claude Code to write a new one.
            Publish(_snapshot with { Status = MonitorStatus.SignedOut, Message = ExpiredMessage, IsRefreshing = false });
            return Schedule(now, Config.SignedOutRecheck);
        }

        if (cred.ExpiresAt is { } expires && expires <= now)
        {
            Publish(_snapshot with { Status = MonitorStatus.TokenExpired, Message = ExpiredMessage, IsRefreshing = false });
            return Schedule(now, Config.SignedOutRecheck);
        }

        Publish(_snapshot with { IsRefreshing = true });
        _lastFetchStart = now;
        var result = await _fetcher.FetchAsync(cred.AccessToken, ct).ConfigureAwait(false);
        var done = _time.GetUtcNow();

        switch (result.Kind)
        {
            case FetchKind.Ok:
                {
                    _failures = 0;
                    _rejectedToken = null;
                    var quotas = result.Quotas ?? Array.Empty<Quota>();
                    Publish(new UsageSnapshot(MonitorStatus.Ok, quotas, done, null, false));
                    return Schedule(done, Max(RefreshPolicy.NextInterval(quotas, done), Config.MinAutoPollGap));
                }

            case FetchKind.Unauthorized:
                _failures = 0;
                _rejectedToken = cred.AccessToken;
                Publish(_snapshot with { Status = MonitorStatus.SignedOut, Message = ExpiredMessage, IsRefreshing = false });
                return Schedule(done, Config.SignedOutRecheck);

            case FetchKind.RateLimited:
                {
                    _failures++;
                    var wait = Max(result.RetryAfter ?? RefreshPolicy.Backoff(_failures), Config.MinAutoPollGap);
                    Publish(_snapshot with { Status = MonitorStatus.RateLimited, Message = "Rate limited by the server", IsRefreshing = false });
                    return Schedule(done, wait);
                }

            default:
                _failures++;
                Publish(_snapshot with { Status = MonitorStatus.Error, Message = Describe(result), IsRefreshing = false });
                return Schedule(done, Max(RefreshPolicy.Backoff(_failures), Config.MinAutoPollGap));
        }
    }

    private static string Describe(FetchResult r) => r.Kind switch
    {
        FetchKind.NetworkError => "Can't reach the server",
        FetchKind.ServerError => $"Server error ({r.StatusCode})",
        _ => r.StatusCode > 0 ? $"Unexpected response ({r.StatusCode})" : "Unexpected response",
    };

    private TimeSpan Schedule(DateTimeOffset from, TimeSpan delay)
    {
        _nextDue = from + delay;
        return delay;
    }

    private TimeSpan Remaining(DateTimeOffset now)
    {
        var left = _nextDue - now;
        return left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private void Publish(UsageSnapshot next)
    {
        if (next == _snapshot)
        {
            return;
        }

        _snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Idempotent: the host and the extension may both dispose the provider.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Changed = null;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutting down anyway.
        }

        if (_ownsFetcher && _fetcher is IDisposable d)
        {
            d.Dispose();
        }

        _cts.Dispose();
        _wake.Dispose();
    }
}
