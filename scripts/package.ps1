[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [string]$Output = 'dist',

    [switch]$FrameworkDependent,

    [switch]$DisableSingleFile
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot $Output

if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir | Out-Null
}

Write-Host "Publishing Repitito ($(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }))..." -ForegroundColor Cyan

$publishArgs = @(
    'publish',
    'KeyPlaybackApp/KeyPlaybackApp.csproj',
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--output', $publishDir,
    '--self-contained', ($FrameworkDependent ? 'false' : 'true')
)

if (-not $DisableSingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
    if (-not $FrameworkDependent) {
        $publishArgs += '/p:SelfContained=true'
    }
} else {
    $publishArgs += '/p:PublishSingleFile=false'
}

Push-Location $repoRoot
try {
    dotnet @publishArgs
    Write-Host "Artifacts available in: $publishDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
