param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Glyph,
    [string]$Output
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\Fort.ind UWP\Assets\Toast"
}
Add-Type -AssemblyName System.Drawing

$font = New-Object System.Drawing.FontFamily("Segoe MDL2 Assets")
$text = [string][char][Convert]::ToInt32($Glyph, 16)

function Render([int]$size, [System.Drawing.Color]$ink, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $glyphPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $format = [System.Drawing.StringFormat]::GenericTypographic
        $glyphPath.AddString($text, $font, [int][System.Drawing.FontStyle]::Regular, [single]$size,
                             (New-Object System.Drawing.PointF([single]0, [single]0)), $format)

        $bounds = $glyphPath.GetBounds()
        $scale = [Math]::Min($size / $bounds.Width, $size / $bounds.Height)
        $matrix = New-Object System.Drawing.Drawing2D.Matrix
        $matrix.Translate([single](($size - $bounds.Width * $scale) / 2), [single](($size - $bounds.Height * $scale) / 2))
        $matrix.Scale([single]$scale, [single]$scale)
        $matrix.Translate([single](-$bounds.X), [single](-$bounds.Y))
        $glyphPath.Transform($matrix)

        $inkBrush = New-Object System.Drawing.SolidBrush($ink)
        $graphics.FillPath($inkBrush, $glyphPath)
        $inkBrush.Dispose()
        $matrix.Dispose()
        $glyphPath.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

if (-not (Test-Path -LiteralPath $Output)) { New-Item -ItemType Directory -Path $Output | Out-Null }

foreach ($scale in 100, 200, 400) {
    $size = [int](32 * $scale / 100)

    Render $size ([System.Drawing.Color]::White) (Join-Path $Output "$Name.scale-$scale.png")
    Render $size ([System.Drawing.Color]::Black) (Join-Path $Output "$Name.contrast-white_scale-$scale.png")
}

Write-Host "Rendered $Name ($Glyph) to $Output"
