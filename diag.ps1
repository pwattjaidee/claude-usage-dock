# Claude Usage Dock - diagnostics. Prints structure only; the token itself is NEVER printed.
# Run: powershell -ExecutionPolicy Bypass -File .\diag.ps1

$ErrorActionPreference = 'Stop'

$dir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$path = Join-Path $dir '.credentials.json'
Write-Host "CLAUDE_CONFIG_DIR : $(if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { '(not set)' })"
Write-Host "Credentials file  : $path"
Write-Host "Exists            : $(Test-Path $path)"
if (-not (Test-Path $path)) { return }

$raw = Get-Content $path -Raw
Write-Host "File size         : $($raw.Length) chars"
Write-Host "Last written      : $((Get-Item $path).LastWriteTime)"

# Other places Claude Code can get a login from (names / existence only, never values)
$envNames = Get-ChildItem env: | Where-Object { $_.Name -match '^(ANTHROPIC_|CLAUDE_CODE_)' } | ForEach-Object Name
Write-Host "Auth env vars set : $(if ($envNames) { $envNames -join ', ' } else { '(none)' })"
$anth = Join-Path $env:APPDATA 'Anthropic'
Write-Host "%APPDATA%\Anthropic: $(if (Test-Path $anth) { 'exists -> ' + ((Get-ChildItem $anth -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($anth.Length + 1) }) -join ', ') } else { '(none)' })"
try { $j = $raw | ConvertFrom-Json } catch { Write-Host "JSON parse        : FAILED ($($_.Exception.Message))"; return }

Write-Host "Top-level keys    : $(($j.PSObject.Properties.Name) -join ', ')"
$o = $j.claudeAiOauth
if (-not $o) { Write-Host "claudeAiOauth     : MISSING  <-- extension can't find a token"; return }
Write-Host "claudeAiOauth keys: $(($o.PSObject.Properties.Name) -join ', ')"

foreach ($k in 'refreshToken', 'expiresAt', 'subscriptionType', 'rateLimitTier') {
    $v = $o.$k
    $desc = if ($null -eq $v) { 'null' } elseif ($v -is [string]) { if ($v.Length -eq 0) { 'empty string' } else { "string, length $($v.Length)" } } else { "$($v.GetType().Name)" }
    if ($k -in 'subscriptionType', 'rateLimitTier' -and $v) { $desc += " = $v" }
    if ($k -eq 'expiresAt' -and $null -ne $v) { $desc += " = $v" }
    Write-Host ("  {0,-16}: {1}" -f $k, $desc)
}
Write-Host ("  {0,-16}: {1}" -f 'scopes', $(if ($o.scopes) { "$(@($o.scopes).Count) entries" } else { 'empty' }))

$tok = [string]$o.accessToken
Write-Host "accessToken       : $(if ($tok) { "present, length $($tok.Length), starts '$($tok.Substring(0, [Math]::Min(8, $tok.Length)))...'" } else { 'EMPTY' })"

if ($o.expiresAt) {
    $exp = [long]$o.expiresAt
    $asMs = [DateTimeOffset]::FromUnixTimeMilliseconds($exp).ToLocalTime()
    Write-Host "expiresAt (raw)   : $exp"
    Write-Host "  as milliseconds : $asMs  $(if ($asMs -lt [DateTimeOffset]::Now) { '<-- IN THE PAST' } else { '(future, OK)' })"
    if ($exp -lt 100000000000) {
        Write-Host "  as seconds      : $([DateTimeOffset]::FromUnixTimeSeconds($exp).ToLocalTime())  <-- looks like SECONDS, not ms"
    }
} else {
    Write-Host "expiresAt         : (none)"
}
if ($o.subscriptionType) { Write-Host "subscriptionType  : $($o.subscriptionType)" }

if (-not $tok) { return }

Write-Host "`nTest call to https://api.anthropic.com/api/oauth/usage (same headers as the extension)..."
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{
    'Authorization'  = "Bearer $tok"
    'anthropic-beta' = 'oauth-2025-04-20'
    'Accept'         = 'application/json'
}
try {
    $r = Invoke-WebRequest -Uri 'https://api.anthropic.com/api/oauth/usage' -Headers $headers -UserAgent 'ClaudeUsageDock/0.1' -UseBasicParsing -TimeoutSec 15
    Write-Host "HTTP status       : $($r.StatusCode)  (OK)"
    $u = $r.Content | ConvertFrom-Json
    foreach ($k in 'five_hour', 'seven_day') {
        if ($u.$k) { Write-Host ("  {0,-10}: {1}%  resets {2}" -f $k, $u.$k.utilization, $u.$k.resets_at) }
    }
} catch {
    $code = $null
    if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
    Write-Host "HTTP status       : $(if ($code) { $code } else { 'no response' })  <-- FAILED"
    Write-Host "Error             : $($_.Exception.Message)"
}
