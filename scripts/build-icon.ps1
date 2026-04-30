param(
    [string] $OutPath = ".\assets\icon.ico",
    [string] $PngPath = ".\assets\shush-vault-logo.png",
    [int]    $PngSize = 512
)

Add-Type -AssemblyName System.Drawing

# White key with dark stroke, rotated -45deg. 32x32 logical canvas (mirrors SVG).
function Render-Png([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $scale = $size / 32.0

    # Rotate around center.
    $g.TranslateTransform(($size / 2.0), ($size / 2.0))
    $g.RotateTransform(-45)
    $g.TranslateTransform(-($size / 2.0), -($size / 2.0))

    $strokeWidth = [Math]::Max(1.0, 1.2 * $scale)
    $stroke = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 10, 10, 10), $strokeWidth)
    $stroke.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $stroke.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stroke.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    function S([double] $v) { return $v * $scale }

    # Bow with inner hole (annulus): two concentric ellipses with FillMode.Alternate.
    $bow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bow.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $outerR = S 5.0
    $innerR = S 2.0
    $cx = S 16.0
    $cy = S 6.0
    $bow.AddEllipse(($cx - $outerR), ($cy - $outerR), ($outerR * 2), ($outerR * 2))
    $bow.StartFigure()
    $bow.AddEllipse(($cx - $innerR), ($cy - $innerR), ($innerR * 2), ($innerR * 2))
    $g.FillPath($whiteBrush, $bow)
    $g.DrawPath($stroke, $bow)
    $bow.Dispose()

    # Shaft.
    $shaftRect = [System.Drawing.RectangleF]::FromLTRB((S 14.5), (S 11.0), (S 17.5), (S 27.0))
    $g.FillRectangle($whiteBrush, $shaftRect)
    $g.DrawRectangle($stroke, $shaftRect.X, $shaftRect.Y, $shaftRect.Width, $shaftRect.Height)

    # Long tooth.
    $tooth1 = [System.Drawing.RectangleF]::FromLTRB((S 17.5), (S 20.0), (S 20.5), (S 21.5))
    $g.FillRectangle($whiteBrush, $tooth1)
    $g.DrawRectangle($stroke, $tooth1.X, $tooth1.Y, $tooth1.Width, $tooth1.Height)

    # Short tooth.
    $tooth2 = [System.Drawing.RectangleF]::FromLTRB((S 17.5), (S 24.0), (S 19.5), (S 25.5))
    $g.FillRectangle($whiteBrush, $tooth2)
    $g.DrawRectangle($stroke, $tooth2.X, $tooth2.Y, $tooth2.Width, $tooth2.Height)

    $stroke.Dispose()
    $whiteBrush.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,($ms.ToArray())
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $pngs += ,(Render-Png $s)
}

$count = $pngs.Count
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

$bw.Write([UInt16] 0)
$bw.Write([UInt16] 1)
$bw.Write([UInt16] $count)

$headerSize = 6 + (16 * $count)
$dataOffset = $headerSize
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]
    $dim = if ($size -ge 256) { 0 } else { [byte] $size }

    $bw.Write([byte] $dim)
    $bw.Write([byte] $dim)
    $bw.Write([byte] 0)
    $bw.Write([byte] 0)
    $bw.Write([UInt16] 1)
    $bw.Write([UInt16] 32)
    $bw.Write([UInt32] $data.Length)
    $bw.Write([UInt32] $dataOffset)
    $dataOffset += $data.Length
}

foreach ($data in $pngs) {
    $bw.Write($data)
}

$bw.Flush()
$dir = Split-Path -Parent $OutPath
if ($dir -and -not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir | Out-Null
}
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
$out.Dispose()
Write-Host "Wrote $OutPath ($($out.Length) bytes, $count sizes)"

if ($PngPath) {
    $pngBytes = Render-Png $PngSize
    $pngDir = Split-Path -Parent $PngPath
    if ($pngDir -and -not (Test-Path $pngDir)) {
        New-Item -ItemType Directory -Path $pngDir | Out-Null
    }
    [System.IO.File]::WriteAllBytes($PngPath, $pngBytes)
    Write-Host "Wrote $PngPath ($($pngBytes.Length) bytes, ${PngSize}x${PngSize})"
}
