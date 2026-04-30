param(
    [string] $OutPath = ".\assets\icon.ico",
    [string] $PngPath = ".\assets\shush-vault-logo.png",
    [int]    $PngSize = 512
)

Add-Type -AssemblyName System.Drawing

# Each rect: x, y, w, h, hex (32x32 logical canvas).
# Mirrors assets/shush-vault-logo.svg.
$rects = @(
    @(0,  0, 6,  32, 'EF4444'),
    @(6,  0, 5,  32, 'F97316'),
    @(11, 0, 5,  32, 'FACC15'),
    @(16, 0, 5,  32, '22C55E'),
    @(21, 0, 5,  32, '3B82F6'),
    @(26, 0, 6,  32, 'A855F7'),

    @(13, 3,  6,  1, 'FFFFFF'),
    @(12, 4,  8,  1, 'FFFFFF'),
    @(11, 5,  10, 1, 'FFFFFF'),
    @(11, 6,  3,  1, 'FFFFFF'),
    @(18, 6,  3,  1, 'FFFFFF'),
    @(11, 7,  2,  1, 'FFFFFF'),
    @(19, 7,  2,  1, 'FFFFFF'),
    @(11, 8,  2,  1, 'FFFFFF'),
    @(19, 8,  2,  1, 'FFFFFF'),
    @(11, 9,  3,  1, 'FFFFFF'),
    @(18, 9,  3,  1, 'FFFFFF'),
    @(11, 10, 10, 1, 'FFFFFF'),
    @(12, 11, 8,  1, 'FFFFFF'),
    @(13, 12, 6,  1, 'FFFFFF'),

    @(14, 13, 4, 10, 'FFFFFF'),

    @(14, 23, 7, 1, 'FFFFFF'),
    @(14, 24, 2, 1, 'FFFFFF'),
    @(14, 25, 5, 1, 'FFFFFF'),
    @(14, 26, 2, 1, 'FFFFFF'),
    @(14, 27, 3, 1, 'FFFFFF')
)

function Render-Png([int] $size) {
    $scale = $size / 32.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor

    foreach ($r in $rects) {
        $hex = $r[4]
        $color = [System.Drawing.Color]::FromArgb(
            255,
            [Convert]::ToInt32($hex.Substring(0,2),16),
            [Convert]::ToInt32($hex.Substring(2,2),16),
            [Convert]::ToInt32($hex.Substring(4,2),16))
        $brush = New-Object System.Drawing.SolidBrush($color)
        $x = [int]([Math]::Round($r[0] * $scale))
        $y = [int]([Math]::Round($r[1] * $scale))
        $w = [int]([Math]::Round(($r[0] + $r[2]) * $scale)) - $x
        $h = [int]([Math]::Round(($r[1] + $r[3]) * $scale)) - $y
        $g.FillRectangle($brush, $x, $y, $w, $h)
        $brush.Dispose()
    }
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

# Build .ico (multi-image, each entry stored as PNG)
$count = $pngs.Count
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

$bw.Write([UInt16] 0)        # reserved
$bw.Write([UInt16] 1)        # type (1 = icon)
$bw.Write([UInt16] $count)   # number of images

$headerSize = 6 + (16 * $count)
$dataOffset = $headerSize
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]
    $dim = if ($size -ge 256) { 0 } else { [byte] $size }

    $bw.Write([byte] $dim)            # width
    $bw.Write([byte] $dim)            # height
    $bw.Write([byte] 0)               # palette
    $bw.Write([byte] 0)               # reserved
    $bw.Write([UInt16] 1)             # color planes
    $bw.Write([UInt16] 32)            # bits per pixel
    $bw.Write([UInt32] $data.Length)  # image size
    $bw.Write([UInt32] $dataOffset)   # offset
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
