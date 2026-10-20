using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageDock.Core;

internal enum FetchKind
{
    Ok,
    Unauthorized,
    RateLimited,
    ServerError,
    NetworkError,
    BadResponse,
}

internal sealed record FetchResult(FetchKind Kind, IReadOnlyList<Quota>? Quotas = null, TimeSpan? RetryAfter = null, int StatusCode = 0);

internal interface IUsageFetcher
{
    Task<FetchResult> FetchAsync(string accessToken, CancellationToken ct);
}

/// <summary>
/// One shared HttpClient, one GET per poll. The token goes only to api.anthropic.com over HTTPS.
/// </summary>
internal sealed partial class UsageClient : IUsageFetcher, IDisposable
{
    private readonly HttpClient _http;

    public UsageClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Config.HttpTimeout;
    }

    public async Task<FetchResult> FetchAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Config.UsageEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("anthropic-beta", Config.BetaHeader);
        req.Headers.TryAddWithoutValidation("User-Agent", Config.UserAgent);

        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var code = (int)resp.StatusCode;

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new FetchResult(FetchKind.Unauthorized, StatusCode: code);
            }

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new FetchResult(FetchKind.RateLimited, RetryAfter: ReadRetryAfter(resp), StatusCode: code);
            }

            if (code >= 500)
            {
                return new FetchResult(FetchKind.ServerError, StatusCode: code);
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new FetchResult(FetchKind.BadResponse, StatusCode: code);
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                return new FetchResult(FetchKind.Ok, UsageParser.Parse(body), StatusCode: code);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return new FetchResult(FetchKind.BadResponse, StatusCode: code);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FetchResult(FetchKind.NetworkError); // HttpClient timeout
        }
        catch (HttpRequestException)
        {
            return new FetchResult(FetchKind.NetworkError);
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra?.Delta is { } delta)
        {
            return delta;
        }

        if (ra?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
