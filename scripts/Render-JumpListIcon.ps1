param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Glyph,
    [string]$Output
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\Fort.ind UWP\Assets\JumpList"
}
Add-Type -AssemblyName System.Drawing

$plate = [System.Drawing.Color]::FromArgb(255, 0x2D, 0x1B, 0x69)
$font = New-Object System.Drawing.FontFamily("Segoe MDL2 Assets")
$text = [string][char][Convert]::ToInt32($Glyph, 16)

function Render([int]$size, [double]$emRatio, [double]$topRatio, [System.Drawing.Color]$ink, [bool]$plated, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        if ($plated) {
            $brush = New-Object System.Drawing.SolidBrush($plate)
            $graphics.FillRectangle($brush, [single]0.5, [single]0.5, [single]($size - 1), [single]($size - 1))
            $brush.Dispose()
        }

        $em = $size * $emRatio
        $left = ($size - $em) / 2
        $top = $size * $topRatio

        $glyphPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $format = [System.Drawing.StringFormat]::GenericTypographic
        $glyphPath.AddString($text, $font, [int][System.Drawing.FontStyle]::Regular, [single]$em,
                             (New-Object System.Drawing.PointF([single]$left, [single]$top)), $format)

        $inkBrush = New-Object System.Drawing.SolidBrush($ink)
        $graphics.FillPath($inkBrush, $glyphPath)
        $inkBrush.Dispose()
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

    Render $size (78.4 / 128) (20.8 / 128) ([System.Drawing.Color]::White) $true (Join-Path $Output "$Name.scale-$scale.png")
    Render $size (110 / 128) (3.1 / 128) ([System.Drawing.Color]::White) $false (Join-Path $Output "$Name.altform-unplated_scale-$scale.png")
    Render $size (110 / 128) (3.1 / 128) $plate $false (Join-Path $Output "$Name.altform-lightunplated_scale-$scale.png")
}

Write-Host "Rendered $Name ($Glyph) to $Output"
