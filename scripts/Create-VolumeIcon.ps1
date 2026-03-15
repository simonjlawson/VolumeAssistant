# Creates a white-outline speaker "broadcasting" icon at src\VolumeAssistant.App\Assets\volume.ico
# Run from repository root in PowerShell: .\scripts\Create-VolumeIcon.ps1
$iconPath = 'src\VolumeAssistant.App\Assets\volume.ico'
New-Item -ItemType Directory -Path (Split-Path $iconPath) -Force | Out-Null

Add-Type -AssemblyName System.Drawing

$size = 64
$bmp = New-Object System.Drawing.Bitmap $size,$size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))

# White pen for outline
$whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 4)
$whitePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

# Draw speaker body as an outlined polygon (trapezoid + rectangle)
# Rectangle (speaker base)
$g.DrawRectangle($whitePen, 8, 20, 18, 24)

# Trapezoid (speaker cone)
$points = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(26,20),
    [System.Drawing.Point]::new(44,12),
    [System.Drawing.Point]::new(44,52),
    [System.Drawing.Point]::new(26,44)
)
$g.DrawPolygon($whitePen, $points)

# Draw broadcasting arcs (three arcs with decreasing thickness)
$arcPen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 4)
$arcPen1.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$arcPen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3)
$arcPen3 = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 2)

# Outer arcs (approximating sound waves)
$g.DrawArc($arcPen1, 40, 12, 18, 40, -60, 120)
$g.DrawArc($arcPen2, 44, 8, 28, 48, -60, 120)
$g.DrawArc($arcPen3, 50, 4, 36, 56, -60, 120)

# Save as PNG bytes
$pngMs = New-Object System.IO.MemoryStream
$bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngMs.ToArray()

# Build ICO structure (ICONDIR + ICONDIRENTRY + PNG data)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([int16]0)      # reserved
$bw.Write([int16]1)      # type (1 = icon)
$bw.Write([int16]1)      # count
$bw.Write([byte]$size)   # width
$bw.Write([byte]$size)   # height
$bw.Write([byte]0)       # color count
$bw.Write([byte]0)       # reserved
$bw.Write([int16]1)      # planes
$bw.Write([int16]32)     # bit count
$bw.Write([int32]$pngBytes.Length)  # bytes in resource
$bw.Write([int32](6 + 16))          # image offset (header + dir entry)
$bw.Flush()

# Write final ico
$ms.ToArray() | ForEach-Object { } # ensure buffer
$iconBytes = $ms.ToArray() + $pngBytes
[System.IO.File]::WriteAllBytes($iconPath, $iconBytes)
Write-Output "Wrote $iconPath"
