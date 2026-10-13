# Terminal.ico: macOS Terminal-szerű ikon (fekete kijelző, szürke keret, fehér >_ prompt). PNG-alapú ICO, több mérettel.
Add-Type -AssemblyName System.Drawing
function New-Frame([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)
    $m = [int]($S * 0.06); $r = [int]($S * 0.20)
    function RR($x, $y, $w, $h, $rad) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $rad * 2; $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90); $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
    $w = $S - 2 * $m
    $frame = RR $m $m $w $w $r
    $gf = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, $m), (New-Object System.Drawing.Point 0, ($S - $m)), [System.Drawing.Color]::FromArgb(255, 214, 214, 218), [System.Drawing.Color]::FromArgb(255, 150, 150, 156))
    $g.FillPath($gf, $frame)
    $b = [int]($S * 0.045); $inner = RR ($m + $b) ($m + $b) ($w - 2 * $b) ($w - 2 * $b) ($r - $b)
    $gi = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, $m), (New-Object System.Drawing.Point 0, ($S - $m)), [System.Drawing.Color]::FromArgb(255, 44, 44, 46), [System.Drawing.Color]::FromArgb(255, 10, 10, 11))
    $g.FillPath($gi, $inner)
    $f = New-Object System.Drawing.Font('Consolas', [single]($S * 0.30), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('>_', $f, [System.Drawing.Brushes]::White, [single]($S * 0.17), [single]($S * 0.26))
    $g.Dispose(); $bmp
}
$sizes = 16, 32, 48, 256; $png = @()
foreach ($s in $sizes) { $bmp = New-Frame $s; $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $png += , $ms.ToArray(); $bmp.Dispose() }
$out = Join-Path $PSScriptRoot 'Terminal.ico'
$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png[$i].Length); $bw.Write([uint32]$off); $off += $png[$i].Length
}
foreach ($p in $png) { $bw.Write($p) }
$bw.Close(); $fs.Close(); "OK: $out"
