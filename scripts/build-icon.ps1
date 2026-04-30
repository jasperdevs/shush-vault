param(
    [string] $OutPath = ".\assets\icon.ico",
    [string] $PngPath = ".\assets\shush-vault-logo.png",
    [int]    $PngSize = 512
)

Add-Type -AssemblyName System.Drawing

# 16x16 pixel-art key. Y = key body, . = transparent.
# Vertical key: hollow round bow on top, shaft going down with two teeth on the right.
$grid = @(
    "................",
    "................",
    "......YYYY......",
    ".....YYYYYY.....",
    "....YY....YY....",
    "....YY....YY....",
    "....YY....YY....",
    ".....YYYYYY.....",
    "......YYYY......",
    ".......YY.......",
    ".......YY.......",
    ".......YYYY.....",
    ".......YY.......",
    ".......YY.......",
    ".......YYY......",
    ".......YY......."
)

$gridSize = 16
$keyColor = [System.Drawing.Color]::FromArgb(255, 0xF1, 0xC4, 0x0F)

function Render-Png([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $scale = $size / [double]$gridSize

    for ($py = 0; $py -lt $gridSize; $py++) {
        $row = $grid[$py]
        for ($px = 0; $px -lt $gridSize; $px++) {
            if ($row[$px] -ne 'Y') { continue }
            $x0 = [int][Math]::Floor($px * $scale)
            $y0 = [int][Math]::Floor($py * $scale)
            $x1 = [int][Math]::Floor(($px + 1) * $scale)
            $y1 = [int][Math]::Floor(($py + 1) * $scale)
            if ($x1 -le $x0) { $x1 = $x0 + 1 }
            if ($y1 -le $y0) { $y1 = $y0 + 1 }
            for ($y = $y0; $y -lt $y1; $y++) {
                for ($x = $x0; $x -lt $x1; $x++) {
                    $bmp.SetPixel($x, $y, $keyColor)
                }
            }
        }
    }

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
