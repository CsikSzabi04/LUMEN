# Lumen.ico: üveg-lencse ikon (sötét kék négyzet, világító gyűrű, fényes mag). PNG-alapú ICO, több mérettel.
Add-Type -AssemblyName System.Drawing
function New-Frame([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $m = [single]($S * 0.05); $w = [single]($S - 2 * $m); $r = [single]($S * 0.23)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $r * 2
    $path.AddArc($m, $m, $d, $d, 180, 90); $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90); $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $S, $S), [System.Drawing.Color]::FromArgb(255, 52, 78, 190), [System.Drawing.Color]::FromArgb(255, 14, 18, 52))
    $g.FillPath($bg, $path)
    # fényes gyűrű
    $cx = $S / 2.0; $cy = $S / 2.0; $ro = $S * 0.30; $th = [single]($S * 0.085)
    $pen = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, ($cy - $ro)), (New-Object System.Drawing.PointF 0, ($cy + $ro)), [System.Drawing.Color]::FromArgb(255, 235, 245, 255), [System.Drawing.Color]::FromArgb(255, 90, 190, 255))
    $pen.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY; $ringPen = New-Object System.Drawing.Pen $pen, $th
    $g.DrawEllipse($ringPen, [single]($cx - $ro), [single]($cy - $ro), [single](2 * $ro), [single](2 * $ro))
    # mag (sugárzó pont)
    $rc = $S * 0.12; $core = New-Object System.Drawing.Drawing2D.GraphicsPath; $core.AddEllipse([single]($cx - $rc), [single]($cy - $rc), [single](2 * $rc), [single](2 * $rc))
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $core; $pg.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 255, 255); $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 110, 200, 255)); $g.FillPath($pg, $core)
    # fénycsík felül (üveg csillanás)
    $gl = New-Object System.Drawing.Drawing2D.GraphicsPath; $gl.AddEllipse([single]($m + $w * 0.08), [single]($m - $w * 0.30), [single]($w * 0.84), [single]($w * 0.62))
    $g.SetClip($path); $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, $m), (New-Object System.Drawing.PointF 0, ($m + $w * 0.34)), [System.Drawing.Color]::FromArgb(110, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255)); $g.FillPath($gb, $gl); $g.ResetClip()
    # vékony keret
    $bp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([single][Math]::Max(1, $S * 0.012)); $g.DrawPath($bp, $path)
    $g.Dispose(); $bmp
}
$sizes = 16, 32, 48, 256; $png = @()
foreach ($s in $sizes) { $bmp = New-Frame $s; $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $png += , $ms.ToArray(); if ($s -eq 256) { $bmp.Save((Join-Path $PSScriptRoot 'terminal\lumen-preview.png')) }; $bmp.Dispose() }
$out = Join-Path $PSScriptRoot 'Lumen.ico'
$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count); $off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) { $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }; $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png[$i].Length); $bw.Write([uint32]$off); $off += $png[$i].Length }
foreach ($p in $png) { $bw.Write($p) }
$bw.Close(); $fs.Close(); "OK: $out"

