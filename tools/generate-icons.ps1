Add-Type -AssemblyName System.Drawing

$outputDir = Join-Path (Join-Path (Join-Path $PSScriptRoot "..") "artifacts") "icons"
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }

function Save-Icon([System.Drawing.Bitmap]$bmp, [string]$name) {
    $icoPath = Join-Path $outputDir "$name.ico"
    $ms = New-Object System.IO.MemoryStream

    # ICO header
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([Int16]0)       # reserved
    $bw.Write([Int16]1)       # type: icon
    $bw.Write([Int16]1)       # image count

    # ICO directory entry (one 32x32 image)
    $bw.Write([byte]32)       # width
    $bw.Write([byte]32)       # height
    $bw.Write([byte]0)        # color palette
    $bw.Write([byte]0)        # reserved
    $bw.Write([Int16]1)       # color planes
    $bw.Write([Int16]32)      # bits per pixel

    # Save PNG to temp stream to get size
    $pngStream = New-Object System.IO.MemoryStream
    $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $pngStream.Dispose()

    $bw.Write([Int32]$pngBytes.Length)   # image size
    $bw.Write([Int32]22)                  # offset (6 header + 16 entry)

    $bw.Write($pngBytes)
    $bw.Flush()

    [System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
    Write-Host "Created $icoPath"
}

function New-Bitmap {
    $bmp = New-Object System.Drawing.Bitmap(32, 32)
    $bmp.SetResolution(96, 96)
    return $bmp
}

# --- IDLE: ear shape (ready to listen) ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
# Outer ear (C-shape)
$earPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3)
$earPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$earPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($earPen, 8, 4, 18, 24, -80, 250)
# Inner ear curve
$innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 2)
$innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($innerPen, 13, 10, 8, 12, -60, 200)
$earPen.Dispose()
$innerPen.Dispose()
$g.Dispose()
Save-Icon $bmp "idle"
$bmp.Dispose()

# --- LISTENING: red circle with white inner ring (mic/record indicator) ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 40, 40))), 4, 4, 24, 24)
$g.FillEllipse([System.Drawing.Brushes]::White, 10, 10, 12, 12)
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 40, 40))), 12, 12, 8, 8)
$g.Dispose()
Save-Icon $bmp "listening"
$bmp.Dispose()

# --- PROCESSING: blue circle with animated-look arcs (sound waves) ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
# Center dot
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(50, 120, 220))), 12, 12, 8, 8)
# Wave arcs
$wavePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(50, 120, 220), 2)
$g.DrawArc($wavePen, 7, 7, 18, 18, -45, 90)
$g.DrawArc($wavePen, 2, 2, 28, 28, -45, 90)
# Left side arcs
$g.DrawArc($wavePen, 7, 7, 18, 18, 135, 90)
$g.DrawArc($wavePen, 2, 2, 28, 28, 135, 90)
$wavePen.Dispose()
$g.Dispose()
Save-Icon $bmp "processing"
$bmp.Dispose()

# --- SUCCESS: green circle with white checkmark ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 180, 60))), 4, 4, 24, 24)
$checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3)
$checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($checkPen, 10, 16, 14, 21)
$g.DrawLine($checkPen, 14, 21, 22, 11)
$checkPen.Dispose()
$g.Dispose()
Save-Icon $bmp "success"
$bmp.Dispose()

# --- ERROR: red circle with white X ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 40, 40))), 4, 4, 24, 24)
$xPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3)
$xPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$xPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($xPen, 11, 11, 21, 21)
$g.DrawLine($xPen, 21, 11, 11, 21)
$xPen.Dispose()
$g.Dispose()
Save-Icon $bmp "error"
$bmp.Dispose()

Write-Host "`nAll icons generated in $outputDir"
