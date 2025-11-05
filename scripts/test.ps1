[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Running tests in configuration '$Configuration'..." -ForegroundColor Cyan

Push-Location $repoRoot
try {
    dotnet run --configuration $Configuration --project KeyPlaybackApp.Tests/KeyPlaybackApp.Tests.csproj
}
finally {
    Pop-Location
}
