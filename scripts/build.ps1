[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building solution (configuration: $Configuration)..." -ForegroundColor Cyan

Push-Location $repoRoot
try {
    dotnet build KeyPlaybackSuite.sln --configuration $Configuration
}
finally {
    Pop-Location
}
