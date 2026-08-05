# Builds a multi-resolution .ico from a source PNG using embedded PNG frames
# (the modern ICO format Windows has supported since Vista).
param(
    [Parameter(Mandatory)][string]$SourcePng,
    [Parameter(Mandatory)][string]$OutIco,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng))

$frames = @()
foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
    $bmp.Dispose()
}
$source.Dispose()

$out = New-Object System.IO.FileStream $OutIco, ([System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter $out

# ICONDIR
$writer.Write([UInt16]0)      # reserved
$writer.Write([UInt16]1)      # type = icon
$writer.Write([UInt16]$frames.Count)

$headerSize = 6
$dirEntrySize = 16
$offset = $headerSize + ($dirEntrySize * $frames.Count)

foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }  # 0 means 256 in ICO format
    $writer.Write([byte]$dim)          # width
    $writer.Write([byte]$dim)          # height
    $writer.Write([byte]0)             # color palette
    $writer.Write([byte]0)             # reserved
    $writer.Write([UInt16]1)           # color planes
    $writer.Write([UInt16]32)          # bits per pixel
    $writer.Write([UInt32]$f.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}

foreach ($f in $frames) {
    $writer.Write($f.Bytes)
}

$writer.Flush()
$writer.Close()
$out.Close()

Write-Output "wrote $OutIco with $($frames.Count) frames"
