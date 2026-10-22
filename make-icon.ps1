param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size / 64.0), ($size / 64.0))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(2, 12, 40, 40, 90, 180)
    $shape.AddArc(22, 12, 40, 40, 270, 180)
    $shape.CloseFigure()
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#101115'))
    $outline = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#A5BFFF'), 2)
    $pulse = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#A6F5C5'), 4)
    $pulse.StartCap = $pulse.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pulse.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.FillPath($dark, $shape)
    $graphics.DrawPath($outline, $shape)
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(12, 33)),
        (New-Object System.Drawing.PointF(23, 33)),
        (New-Object System.Drawing.PointF(28, 23)),
        (New-Object System.Drawing.PointF(35, 42)),
        (New-Object System.Drawing.PointF(41, 30)),
        (New-Object System.Drawing.PointF(52, 30))
    )
    $graphics.DrawLines($pulse, $points)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $size; Data = $stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $OutputDirectory 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $pulse.Dispose(); $outline.Dispose(); $dark.Dispose(); $shape.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $OutputDirectory 'IslandMonitor.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $file.Dispose() }
