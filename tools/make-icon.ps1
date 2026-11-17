# Generates src/Arcadia/Assets/Arcadia.ico (multi-resolution, PNG-compressed entries).
param([string]$Out = "$PSScriptRoot\..\src\Arcadia\Assets\Arcadia.ico")

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $r = $rect.Width * 0.27
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Y, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 157, 123, 255)), ([System.Drawing.Color]::FromArgb(255, 47, 181, 255)), 45.0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 157, 123, 255), [System.Drawing.Color]::FromArgb(255, 91, 108, 255), [System.Drawing.Color]::FromArgb(255, 47, 181, 255))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $path)

    # subtle top highlight
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
    $g.FillPath($hl, $path)

    # the "A" mark (same geometry as the in-app logo, 24x24 design grid)
    $s = $size / 24.0
    $penWidth = [Math]::Max(1.2, 2.5 * $s)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $dy = 0.4 * $s
    $pts = @(
        (New-Object System.Drawing.PointF (6.5 * $s), (19 * $s + $dy)),
        (New-Object System.Drawing.PointF (12 * $s), (5 * $s + $dy)),
        (New-Object System.Drawing.PointF (17.5 * $s), (19 * $s + $dy))
    )
    $g.DrawLines($pen, $pts)
    $g.DrawLine($pen, (8.6 * $s), (14 * $s + $dy), (15.4 * $s), (14 * $s + $dy))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($sz in $sizes) { $images += ,(New-IconPng $sz) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out) | ForEach-Object { Join-Path $_ (Split-Path $Out -Leaf) }))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Close()
Write-Output "Icon written: $Out"
