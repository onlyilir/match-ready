# Draws docs/images/social-preview.png (1280x640) for the GitHub repo's social preview.
# Usage: powershell -File build\Make-SocialPreview.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$shot = Join-Path $root 'docs\images\boost-active.png'
$out  = Join-Path $root 'docs\images\social-preview.png'

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

$bmp = New-Object System.Drawing.Bitmap 1280, 640
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear((C '#16181d'))

# Logo: rounded green square with a white bolt (same shape as logo.svg, scaled to 96 px)
$x0 = 80; $y0 = 120; $s = 0.96
$r = 22 * $s; $w = 100 * $s
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc($x0, $y0, 2*$r, 2*$r, 180, 90); $path.AddArc($x0+$w-2*$r, $y0, 2*$r, 2*$r, 270, 90)
$path.AddArc($x0+$w-2*$r, $y0+$w-2*$r, 2*$r, 2*$r, 0, 90); $path.AddArc($x0, $y0+$w-2*$r, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new($x0, $y0)), ([System.Drawing.PointF]::new($x0, $y0+$w)), (C '#2ebe6a'), (C '#1c8046')
$g.FillPath($grad, $path)
$pts = @(58,10, 24,56, 46,56, 38,90, 76,40, 53,40, 64,10)
$poly = for ($i = 0; $i -lt $pts.Count; $i += 2) { [System.Drawing.PointF]::new($x0 + $pts[$i]*$s, $y0 + $pts[$i+1]*$s) }
$g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$poly)

$text  = New-Object System.Drawing.SolidBrush (C '#e8eaed')
$muted = New-Object System.Drawing.SolidBrush (C '#9aa0a8')
$green = New-Object System.Drawing.SolidBrush (C '#2ebe6a')
$title = New-Object System.Drawing.Font 'Segoe UI Semibold', 64, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tag   = New-Object System.Drawing.Font 'Segoe UI', 34, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$small = New-Object System.Drawing.Font 'Segoe UI', 24, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)

$g.DrawString('Match Ready', $title, $text, 76, 236)
$g.DrawString("One button to get your PC`nready for a CS2 match.", $tag, $text, 80, 330)
$g.DrawString('FACEIT-safe: never touches the game or anti-cheat.', $small, $green, 82, 434)
$dot = [char]0x00B7   # middle dot; kept out of the source so PowerShell 5.1 reads the file the same in any code page
$g.DrawString("Free and open source  $dot  Windows 10 and 11", $small, $muted, 82, 474)

# Screenshot on the right, scaled to 560 px high, with a thin border
$img = [System.Drawing.Image]::FromFile($shot)
$h = 560; $wImg = [int]($img.Width * $h / $img.Height)
$sx = 1280 - 80 - $wImg; $sy = 40
$g.DrawImage($img, $sx, $sy, $wImg, $h)
$pen = New-Object System.Drawing.Pen (C '#353a45'), 2
$g.DrawRectangle($pen, $sx - 1, $sy - 1, $wImg + 1, $h + 1)

$img.Dispose(); $g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"Wrote $out"
