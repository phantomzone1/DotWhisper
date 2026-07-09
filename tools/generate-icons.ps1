Add-Type -AssemblyName System.Drawing

$outputDir = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') 'artifacts') 'icons'
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }

# Remove stale frames from prior icon sets (e.g. success.ico) so the output dir only
# ever contains what this script currently generates.
Get-ChildItem -Path $outputDir -Filter '*.ico' -ErrorAction SilentlyContinue | Remove-Item -Force

function Save-Icon([System.Drawing.Bitmap]$bmp, [string]$name) {
    $icoPath = Join-Path $outputDir ($name + '.ico')
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([Int16]0)
    $bw.Write([Int16]1)
    $bw.Write([Int16]1)
    $bw.Write([byte]32)
    $bw.Write([byte]32)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([Int16]1)
    $bw.Write([Int16]32)
    $pngStream = New-Object System.IO.MemoryStream
    $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $pngStream.Dispose()
    $bw.Write([Int32]$pngBytes.Length)
    $bw.Write([Int32]22)
    $bw.Write($pngBytes)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
    Write-Host ('Created ' + $icoPath)
}

function New-Bitmap {
    $bmp = New-Object System.Drawing.Bitmap(32, 32)
    $bmp.SetResolution(96, 96)
    return $bmp
}

function New-Graphics([System.Drawing.Bitmap]$bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return $g
}

function EllipsePath([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddEllipse($x, $y, $w, $h)
    return $p
}

# Equalizer-style sound-wave bars, centered vertically. $heights are bar heights in px.
function Draw-Waveform([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, [float[]]$heights) {
    $barWidth = 3.2
    $spacing = 5.4
    $startX = 6.2
    $centerY = 16

    for ($i = 0; $i -lt $heights.Length; $i++) {
        $x = $startX + ($i * $spacing)
        $h = $heights[$i]
        $pen = New-Object System.Drawing.Pen($color, $barWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($pen, $x, $centerY - ($h / 2), $x, $centerY + ($h / 2))
        $pen.Dispose()
    }
}

# Filled brain silhouette: two unioned hemisphere blobs with scalloped top bumps
# and a brainstem nub, split by a carved-out centerline seam.
function Draw-Brain([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, [bool]$glowSpark) {
    $left = EllipsePath 4 10 13 15
    $region = New-Object System.Drawing.Region($left)
    $right = EllipsePath 15 10 13 15
    $region.Union($right)
    $bump1 = EllipsePath 6 6 8 8
    $region.Union($bump1)
    $bump2 = EllipsePath 12 4 9 9
    $region.Union($bump2)
    $bump3 = EllipsePath 18 6 8 8
    $region.Union($bump3)
    $stem = EllipsePath 13 23 6 5
    $region.Union($stem)

    $seam = New-Object System.Drawing.Drawing2D.GraphicsPath
    $seam.AddRectangle([System.Drawing.RectangleF]::new(15, 7, 2, 20))
    $region.Exclude($seam)

    if ($glowSpark) {
        $glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, $color.R, $color.G, $color.B))
        $g.FillEllipse($glowBrush, 1, 2, 30, 28)
        $glowBrush.Dispose()
    }

    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.FillRegion($brush, $region)
    $brush.Dispose()

    if ($glowSpark) {
        $sparkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        $g.FillEllipse($sparkBrush, 9, 13, 4, 4)
        $g.FillEllipse($sparkBrush, 19, 15, 3, 3)
        $sparkBrush.Dispose()
    }

    $region.Dispose(); $left.Dispose(); $right.Dispose()
    $bump1.Dispose(); $bump2.Dispose(); $bump3.Dispose(); $stem.Dispose(); $seam.Dispose()
}

# Colors
$idleGray = [System.Drawing.Color]::FromArgb(255, 150, 155, 160)
$neonGreen = [System.Drawing.Color]::FromArgb(255, 57, 255, 20)
$cyberBlue = [System.Drawing.Color]::FromArgb(255, 0, 180, 255)
$safetyRed = [System.Drawing.Color]::FromArgb(255, 220, 30, 30)

# --- IDLE: static gray sound-wave bars, calm/resting ---
$bmp = New-Bitmap
$g = New-Graphics $bmp
Draw-Waveform $g $idleGray @(8, 12, 16, 12, 8)
$g.Dispose()
Save-Icon $bmp 'idle'
$bmp.Dispose()

# --- LISTENING: animated 2-frame pulse, neon green sound-wave bars ---
$bmp = New-Bitmap
$g = New-Graphics $bmp
Draw-Waveform $g $neonGreen @(10, 18, 24, 18, 10)
$g.Dispose()
Save-Icon $bmp 'listening_1'
$bmp.Dispose()

$bmp = New-Bitmap
$g = New-Graphics $bmp
Draw-Waveform $g $neonGreen @(18, 26, 14, 26, 18)
$g.Dispose()
Save-Icon $bmp 'listening_2'
$bmp.Dispose()

# --- PROCESSING: animated 2-frame pulse, cyber blue brain "thinking" ---
$bmp = New-Bitmap
$g = New-Graphics $bmp
Draw-Brain $g $cyberBlue $false
$g.Dispose()
Save-Icon $bmp 'processing_1'
$bmp.Dispose()

$bmp = New-Bitmap
$g = New-Graphics $bmp
Draw-Brain $g $cyberBlue $true
$g.Dispose()
Save-Icon $bmp 'processing_2'
$bmp.Dispose()

# --- ERROR: solid circle + "!", safety red (unchanged) ---
$bmp = New-Bitmap
$g = New-Graphics $bmp
$solidBrush = New-Object System.Drawing.SolidBrush($safetyRed)
$g.FillEllipse($solidBrush, 4, 4, 24, 24)
$solidBrush.Dispose()
$font = New-Object System.Drawing.Font('Arial', 18, [System.Drawing.FontStyle]::Bold)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('!', $font, [System.Drawing.Brushes]::White, 16, 15, $sf)
$font.Dispose()
$sf.Dispose()
$g.Dispose()
Save-Icon $bmp 'error'
$bmp.Dispose()

Write-Host ''
Write-Host 'All icons generated'
