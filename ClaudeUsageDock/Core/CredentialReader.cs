using System.Text.Json;

namespace ClaudeUsageDock.Core;

internal sealed record ClaudeCredential(string AccessToken, DateTimeOffset? ExpiresAt);

/// <summary>
/// Reads the OAuth access token that Claude Code keeps in
/// %USERPROFILE%\.claude\.credentials.json (or $CLAUDE_CONFIG_DIR\.credentials.json).
///
/// Rules: read-only, read fresh every time (Claude Code rotates the token),
/// never cache it on disk, never log it. Only the access token and its expiry
/// are read; the refresh token is left alone.
/// </summary>
internal static class CredentialReader
{
    public static string CredentialsPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }

            return Path.Combine(dir, ".credentials.json");
        }
    }

    public static ClaudeCredential? TryRead(string? path = null)
    {
        path ??= CredentialsPath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Share flags: Claude Code may be rewriting the file right now.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return FromJson(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Mid-rewrite or unreadable: treat as "no token right now".
            return null;
        }
    }

    internal static ClaudeCredential? FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object ||
            !oauth.TryGetProperty("accessToken", out var tok) ||
            tok.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var token = tok.GetString();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        DateTimeOffset? expires = null;
        if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var ms))
        {
            expires = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return new ClaudeCredential(token, expires);
    }
}
