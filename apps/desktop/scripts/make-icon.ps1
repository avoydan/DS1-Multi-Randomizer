# Builds build/icon.ico from the approved, unmodified source artwork (assets/branding/ds1tr-icon-source.png).
# The ICO holds real, separately scaled frames (PNG-compressed, alpha preserved): 16, 24, 32, 48, 64, 128 and 256 px.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'assets/branding/ds1tr-icon-source.png'
$target = Join-Path $root 'build/icon.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function Resize-Frame([System.Drawing.Bitmap] $image, [int] $size) {
  # Halve step by step while the source is more than twice as large, so small frames stay sharp instead of aliased.
  $current = $image
  while ($current.Width -gt $size * 2) {
    $half = [int][Math]::Max($size, [Math]::Floor($current.Width / 2))
    $next = New-Object System.Drawing.Bitmap $half, $half, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($next)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($current, 0, 0, $half, $half)
    $graphics.Dispose()
    if ($current -ne $image) { $current.Dispose() }
    $current = $next
  }
  $final = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($final)
  $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.DrawImage($current, 0, 0, $size, $size)
  $graphics.Dispose()
  if ($current -ne $image) { $current.Dispose() }
  return $final
}

$original = [System.Drawing.Bitmap]::FromFile($source)
if ($original.Width -ne $original.Height) { throw "The icon source must be square (found $($original.Width)x$($original.Height))." }
$frames = @()
foreach ($size in $sizes) {
  $frame = Resize-Frame $original $size
  $stream = New-Object System.IO.MemoryStream
  $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
  $frames += , @{ Size = $size; Data = $stream.ToArray() }
  $stream.Dispose()
  $frame.Dispose()
}
$original.Dispose()

New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0)               # reserved
$writer.Write([UInt16]1)               # type: icon
$writer.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
  $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
  $writer.Write([Byte]$dimension)      # width (0 = 256)
  $writer.Write([Byte]$dimension)      # height
  $writer.Write([Byte]0)               # palette
  $writer.Write([Byte]0)               # reserved
  $writer.Write([UInt16]1)             # planes
  $writer.Write([UInt16]32)            # bits per pixel
  $writer.Write([UInt32]$frame.Data.Length)
  $writer.Write([UInt32]$offset)
  $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write($frame.Data) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Output "Wrote $target ($($frames.Count) frames: $($sizes -join ', ') px, $((Get-Item $target).Length) bytes)"
