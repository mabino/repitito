[CmdletBinding()]
param(
    [string]$Glyph = "🎹",
    [string]$FontName = "Segoe UI Emoji",
    [int[]]$Sizes = @(256, 192, 128, 96, 64, 48, 32, 24, 16),
    [string]$OutputDir = "KeyPlaybackApp/Assets/Icons",
    [string]$IconName = "Repitito.ico"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $repoRoot $OutputDir
$pngDir = Join-Path $targetDir 'png'

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
New-Item -ItemType Directory -Path $pngDir -Force | Out-Null

function New-GlyphImage {
    param(
        [int]$Size,
        [string]$GlyphChar,
        [string]$FontFamily,
        [string]$Destination
    )

    $bitmap = New-Object System.Drawing.Bitmap ($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $fontSize = $Size * 0.8
    $font = New-Object System.Drawing.Font ($FontFamily, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        try {
            $format = New-Object System.Drawing.StringFormat
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center

            # shrink font until it fits within the bounds
            while ($true) {
                $measurement = $graphics.MeasureString($GlyphChar, $font)
                if ($measurement.Width -le $Size * 0.9 -and $measurement.Height -le $Size * 0.9) {
                    break
                }
                $font.Dispose()
                $fontSize *= 0.95
                $font = New-Object System.Drawing.Font ($FontFamily, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            }

            $rect = New-Object System.Drawing.RectangleF 0, 0, $Size, $Size
            $brush = [System.Drawing.Brushes]::Black
            $graphics.DrawString($GlyphChar, $font, $brush, $rect, $format)
        }
        finally {
            $font.Dispose()
        }

        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$pngPaths = @()
foreach ($size in $Sizes) {
    $pngPath = Join-Path $pngDir "${size}px.png"
    Write-Host "Generating ${size}px glyph..." -ForegroundColor Cyan
    New-GlyphImage -Size $size -GlyphChar $Glyph -FontFamily $FontName -Destination $pngPath
    $pngPaths += [PSCustomObject]@{ Size = $size; Path = $pngPath }
}

$iconPath = Join-Path $targetDir $IconName

$writerStream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter($writerStream)

try {
    $entries = @()
    $imageData = @()

    $writer.Write([UInt16]0) # reserved
    $writer.Write([UInt16]1) # type 1 = icon
    $writer.Write([UInt16]$pngPaths.Count)

    $offset = 6 + (16 * $pngPaths.Count)

    foreach ($pngInfo in $pngPaths) {
        $bytes = [System.IO.File]::ReadAllBytes($pngInfo.Path)
        $imageData += ,$bytes

        $width = if ($pngInfo.Size -ge 256) { [byte]0 } else { [byte]$pngInfo.Size }
        $height = if ($pngInfo.Size -ge 256) { [byte]0 } else { [byte]$pngInfo.Size }

        $writer.Write($width)
        $writer.Write($height)
        $writer.Write([byte]0) # color palette
        $writer.Write([byte]0) # reserved
        $writer.Write([UInt16]0) # color planes
        $writer.Write([UInt16]32) # bits per pixel
        $writer.Write([UInt32]$bytes.Length)
        $writer.Write([UInt32]$offset)

        $offset += $bytes.Length
    }

    foreach ($bytes in $imageData) {
        $writer.Write($bytes)
    }
}
finally {
    $writer.Dispose()
    $writerStream.Dispose()
}

Write-Host "Icon written to $iconPath" -ForegroundColor Green
Write-Host "PNGs available in $pngDir" -ForegroundColor Green
