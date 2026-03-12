# Creates a simple speaker icon at src\VolumeAssistant.Service\Assets\volume.ico
# Run from repository root in PowerShell: .\scripts\Create-VolumeIcon.ps1
$iconPath = 'src\VolumeAssistant.Service\Assets\volume.ico'
New-Item -ItemType Directory -Path (Split-Path $iconPath) -Force | Out-Null

Add-Type -AssemblyName System.Drawing
$size = 64
$bmp = New-Object System.Drawing.Bitmap $size,$size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))

# Draw speaker body
$brush = [System.Drawing.Brushes]::Black
$g.FillRectangle($brush, 8,20,18,24)
$points = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(26,20),
    [System.Drawing.Point]::new(44,12),
    [System.Drawing.Point]::new(44,52),
    [System.Drawing.Point]::new(26,44)
)
$g.FillPolygon($brush, $points)

# Draw sound waves
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 3)
$g.DrawArc($pen, 40, 16, 18, 32, -60, 120)
$pen.Width = 2
$g.DrawArc($pen, 44, 12, 26, 40, -60, 120)

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
