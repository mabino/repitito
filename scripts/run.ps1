[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Launching Repitito (configuration: $Configuration)..." -ForegroundColor Cyan

$restoreArgs = @()
if ($NoRestore) {
    $restoreArgs += '--no-restore'
}

Push-Location $repoRoot
try {
    dotnet run --configuration $Configuration @restoreArgs --project KeyPlaybackApp/KeyPlaybackApp.csproj
}
finally {
    Pop-Location
}
