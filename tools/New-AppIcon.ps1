<#
.SYNOPSIS
    Renders src/Pupitre/Assets/pupitre.ico from the drawing of pupitre.svg (64-unit grid).
.DESCRIPTION
    Windows PowerShell only (System.Drawing). Keep the shapes below in sync with pupitre.svg.
    Each size is drawn natively rather than downscaled, so small sizes stay sharp.
#>
param([string]$OutputPath = (Join-Path $PSScriptRoot '..\src\Pupitre\Assets\pupitre.ico'))

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function New-RoundedRectangle([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $radius
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Get-Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-IconFrame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.PixelOffsetMode = 'HighQuality'
    $graphics.ScaleTransform($size / 64, $size / 64)

    $graphics.FillPath((New-Object System.Drawing.SolidBrush (Get-Color '#1b1c22')), (New-RoundedRectangle 0 0 64 64 14))
    $tiles = @(@{ Y = 12; Color = '#2f6fb3' }, @{ Y = 27; Color = '#d9541e' }, @{ Y = 42; Color = '#178a72' })
    foreach ($tile in $tiles) {
        $graphics.FillPath((New-Object System.Drawing.SolidBrush (Get-Color $tile.Color)), (New-RoundedRectangle 9 $tile.Y 13 11 3))
    }

    $graphics.FillEllipse((New-Object System.Drawing.SolidBrush (Get-Color '#f0b43c')), 17, 24, 8, 8)
    $graphics.DrawEllipse((New-Object System.Drawing.Pen (Get-Color '#1b1c22'), 2), 17, 24, 8, 8)

    $prompt = New-Object System.Drawing.Pen (Get-Color '#e4e5ea'), 4
    $prompt.StartCap = 'Round'; $prompt.EndCap = 'Round'; $prompt.LineJoin = 'Round'
    $graphics.DrawLines($prompt, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 30, 26), (New-Object System.Drawing.PointF 38, 32), (New-Object System.Drawing.PointF 30, 38)))
    $graphics.FillPath((New-Object System.Drawing.SolidBrush (Get-Color '#e4e5ea')), (New-RoundedRectangle 41 36 13 4 2))

    $graphics.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

# ICO container with PNG-compressed frames (supported since Windows Vista).
$frames = $sizes | ForEach-Object { , (New-IconFrame $_) }
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutputPath)).Path + '\' + (Split-Path $OutputPath -Leaf), $output.ToArray())
Write-Output "Icon written: $OutputPath ($($sizes -join ', ') px)"
