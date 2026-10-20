<#
.SYNOPSIS
  Build and register Claude Usage Dock without Visual Studio (needs only the .NET 10 SDK + Developer Mode).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\deploy.ps1
  powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Configuration Release
  powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Uninstall

.NOTES
  This does what Visual Studio's "Deploy" does: build the project, then register the
  built package layout in place with Add-AppxPackage -Register (allowed by Developer Mode,
  no signing certificate needed). The installed extension runs from the bin folder,
  so don't delete bin\ while it's installed. After deploying, run "Reload" in Command Palette.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$PackageName = 'ClaudeUsageDock'
$Root = $PSScriptRoot
$Project = Join-Path $Root 'ClaudeUsageDock\ClaudeUsageDock.csproj'
$BinDir = Join-Path $Root 'ClaudeUsageDock\bin'

function Step($text) { Write-Host "`n>> $text" -ForegroundColor Cyan }

function Stop-Extension {
    # Command Palette keeps the extension process alive; it locks files during rebuilds.
    Get-Process -Name $PackageName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Remove-Existing {
    $existing = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($existing) {
        Step "Removing previously installed $PackageName ($($existing.Version))"
        $existing | Remove-AppxPackage
    }
}

if ($Uninstall) {
    Stop-Extension
    Remove-Existing
    Write-Host "Uninstalled. Run 'Reload' in Command Palette." -ForegroundColor Green
    return
}

# --- Preflight -------------------------------------------------------------
Step 'Checking prerequisites'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.10"
}
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^(1[0-9]|[2-9][0-9])\.' })) {
    throw ".NET 10 SDK or newer is required (found: $($sdks -join ', ')). Install it with:  winget install Microsoft.DotNet.SDK.10"
}

$devMode = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) {
    throw "Developer Mode is off. Turn it on: Settings > System > For developers > Developer Mode."
}
Write-Host "dotnet OK, Developer Mode OK, platform $Platform, $Configuration"

# --- Build -----------------------------------------------------------------
Stop-Extension

Step "Building ($Configuration|$Platform). First build restores NuGet packages, give it a minute."
& dotnet build $Project -c $Configuration -p:Platform=$Platform -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE). Paste the errors above to fix them." }

# --- Find the package layout -------------------------------------------------
Step 'Locating the built package manifest'
$manifest = Get-ChildItem -Path $BinDir -Recurse -Filter 'AppxManifest.xml' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\$Platform\\$Configuration\\" -or $_.FullName -match "\\$Configuration\\" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $manifest) {
    throw "No AppxManifest.xml was produced under $BinDir. The MSIX tooling didn't generate a layout; paste the build output so we can adjust."
}
Write-Host $manifest.FullName

# --- Register ----------------------------------------------------------------
Remove-Existing
Step 'Registering the package with Windows'
Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown

$installed = Get-AppxPackage -Name $PackageName
Write-Host "`nInstalled $($installed.Name) $($installed.Version)" -ForegroundColor Green
Write-Host "Next: Win+Alt+Space > run 'Reload', then Edit Dock > + > 'Claude usage'." -ForegroundColor Green
