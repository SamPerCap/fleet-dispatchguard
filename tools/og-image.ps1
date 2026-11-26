# Draws web/public/og.png, the image of the preview of the link (SPEC 0093 R7). Run by hand when its text changes; Windows only.
# Save this file as UTF-8 with BOM: Windows PowerShell 5.1 reads the accents wrong without it.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Same palette as web/src/index.css.
$navy = [System.Drawing.Color]::FromArgb(16, 27, 48)
$navyEnd = [System.Drawing.Color]::FromArgb(23, 42, 77)
$blue = [System.Drawing.Color]::FromArgb(36, 99, 235)
$blueBright = [System.Drawing.Color]::FromArgb(110, 160, 255)
$secondary = [System.Drawing.Color]::FromArgb(169, 182, 204)

$width = 1200
$height = 630
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$canvas = [System.Drawing.Graphics]::FromImage($bitmap)
# Under the gradient, whose edge is drawn half transparent.
$canvas.Clear($navy)
$canvas.SmoothingMode = 'AntiAlias'
$canvas.TextRenderingHint = 'AntiAliasGridFit'

function New-Gradient([System.Drawing.RectangleF]$Area, $From, $To) {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush $Area, $From, $To, 45
}

function New-RoundedSquare([single]$Left, [single]$Top, [single]$Side, [single]$Radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $Radius
    $right = $Left + $Side - $diameter
    $bottom = $Top + $Side - $diameter
    $path.AddArc($Left, $Top, $diameter, $diameter, 180, 90)
    $path.AddArc($right, $Top, $diameter, $diameter, 270, 90)
    $path.AddArc($right, $bottom, $diameter, $diameter, 0, 90)
    $path.AddArc($Left, $bottom, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Write-Line([string]$Text, [single]$Size, [string]$Style, $Colour, [single]$Top) {
    $font = New-Object System.Drawing.Font 'Segoe UI', $Size, ([System.Drawing.FontStyle]$Style), ([System.Drawing.GraphicsUnit]::Pixel)
    $area = New-Object System.Drawing.RectangleF 80, $Top, 1040, 300
    $canvas.DrawString($Text, $font, (New-Object System.Drawing.SolidBrush $Colour), $area)
}

$canvas.FillRectangle((New-Gradient (New-Object System.Drawing.RectangleF 0, 0, $width, $height) $navy $navyEnd), 0, 0, $width, $height)

# The mark of web/src/brand/BrandArt.tsx, drawn on its 40 x 40 grid.
$state = $canvas.Save()
$canvas.TranslateTransform(86, 64)
$canvas.ScaleTransform(2.4, 2.4)
$canvas.FillPath((New-Gradient (New-Object System.Drawing.RectangleF 0, 0, 40, 40) $blueBright $blue), (New-RoundedSquare 2 2 36 11))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 3.2
$pen.StartCap = 'Round'
$pen.EndCap = 'Round'
$canvas.DrawBezier($pen, 11, 28, 17, 28, 15, 18, 21, 18)
$canvas.DrawBezier($pen, 21, 18, 27, 18, 26, 12, 29, 12)
$canvas.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), 25.6, 8.6, 6.8, 6.8)
$canvas.Restore($state)

Write-Line 'Fleet DispatchGuard · Legal allocation of drivers and buses' 30 'Regular' $secondary 182
Write-Line 'It never saves an illegal assignment. Try to make it.' 64 'Bold' ([System.Drawing.Color]::White) 240
Write-Line 'Measured evidence · EC 561/2006 checked before saving · open demo' 28 'Regular' $blueBright 520

$target = Join-Path (Split-Path $PSScriptRoot) 'web\public\og.png'
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
$bitmap.Dispose()
