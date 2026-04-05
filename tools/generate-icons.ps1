Add-Type -AssemblyName System.Drawing

$outputDir = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') 'artifacts') 'icons'
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }

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

# Colors
$darkGray = [System.Drawing.Color]::FromArgb(120, 130, 130, 130)
$neonGreen = [System.Drawing.Color]::FromArgb(255, 57, 255, 20)
$cyberBlue = [System.Drawing.Color]::FromArgb(255, 0, 180, 255)
$brightGreen = [System.Drawing.Color]::FromArgb(255, 0, 220, 60)
$safetyRed = [System.Drawing.Color]::FromArgb(255, 220, 30, 30)

# --- IDLE: Hollow circle + thin ear, dark gray / low opacity ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
# Hollow circle
$circlePen = New-Object System.Drawing.Pen($darkGray, 1.5)
$g.DrawEllipse($circlePen, 6, 4, 20, 20)
$circlePen.Dispose()
# Thin ear
$earPen = New-Object System.Drawing.Pen($darkGray, 2)
$earPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$earPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($earPen, 9, 6, 16, 20, -80, 240)
$innerPen = New-Object System.Drawing.Pen($darkGray, 1.5)
$innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($innerPen, 13, 11, 7, 10, -60, 190)
$earPen.Dispose()
$innerPen.Dispose()
$g.Dispose()
Save-Icon $bmp 'idle'
$bmp.Dispose()

# --- LISTENING: Solid circle + bold ear, neon green ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
# Glow circle
$glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 57, 255, 20))
$g.FillEllipse($glowBrush, 3, 1, 26, 26)
$glowBrush.Dispose()
# Solid circle
$solidBrush = New-Object System.Drawing.SolidBrush($neonGreen)
$g.FillEllipse($solidBrush, 6, 4, 20, 20)
$solidBrush.Dispose()
# Bold ear (dark for contrast)
$earPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 10, 40, 5), 3)
$earPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$earPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($earPen, 9, 6, 16, 20, -80, 240)
$innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 10, 40, 5), 2)
$innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($innerPen, 13, 11, 7, 10, -60, 190)
$earPen.Dispose()
$innerPen.Dispose()
$g.Dispose()
Save-Icon $bmp 'listening'
$bmp.Dispose()

# --- PROCESSING: Rotating segments / pulse, cyber blue ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
# Center dot
$centerBrush = New-Object System.Drawing.SolidBrush($cyberBlue)
$g.FillEllipse($centerBrush, 12, 10, 8, 8)
$centerBrush.Dispose()
# Rotating arc segments (staggered thickness for motion feel)
$arcPen1 = New-Object System.Drawing.Pen($cyberBlue, 3)
$arcPen1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$arcPen1.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($arcPen1, 4, 2, 24, 24, -30, 60)
$g.DrawArc($arcPen1, 4, 2, 24, 24, 150, 60)
$arcPen1.Dispose()
$arcPen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 0, 180, 255), 2)
$arcPen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$arcPen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($arcPen2, 4, 2, 24, 24, 60, 40)
$g.DrawArc($arcPen2, 4, 2, 24, 24, 240, 40)
$arcPen2.Dispose()
$g.Dispose()
Save-Icon $bmp 'processing'
$bmp.Dispose()

# --- SUCCESS: Solid circle + checkmark, bright green ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$solidBrush = New-Object System.Drawing.SolidBrush($brightGreen)
$g.FillEllipse($solidBrush, 4, 4, 24, 24)
$solidBrush.Dispose()
$checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3)
$checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($checkPen, 10, 16, 14, 21)
$g.DrawLine($checkPen, 14, 21, 22, 11)
$checkPen.Dispose()
$g.Dispose()
Save-Icon $bmp 'success'
$bmp.Dispose()

# --- ERROR: Solid circle + "!", safety red ---
$bmp = New-Bitmap
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$solidBrush = New-Object System.Drawing.SolidBrush($safetyRed)
$g.FillEllipse($solidBrush, 4, 4, 24, 24)
$solidBrush.Dispose()
# Exclamation mark
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
