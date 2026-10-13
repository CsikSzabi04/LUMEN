# Eltünteti a kis parancsikon-nyilat és az UAC-pajzsot az ikonokról (az asztalon és az Explorerben mindenhol).
# Rendszergazdai jog kell (HKLM), a script maga kéri el egyszer (UAC). Visszaállítás: Restore-ShortcutOverlays.ps1
param([switch]$Elevated)
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
if (-not $admin) {
    if ($Elevated) { throw 'Nem sikerult rendszergazdai jogot kapni.' }       # nincs vegtelen ujraindulas
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated'
    return
}
$blank = Join-Path $PSScriptRoot 'blank.ico'
if (-not (Test-Path $blank)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object Drawing.Bitmap 32, 32; $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray()
    $ico = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ico
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([byte]32); $bw.Write([byte]32); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$png.Length); $bw.Write([int]22); $bw.Write($png); $bw.Flush()
    [IO.File]::WriteAllBytes($blank, $ico.ToArray())
}
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
New-Item $k -Force | Out-Null
foreach ($n in '29', '77') { New-ItemProperty $k $n -Value "$blank,0" -PropertyType String -Force | Out-Null }   # 29 = parancsikon-nyil, 77 = UAC-pajzs
Stop-Process -Name explorer -Force      # az Explorer magatol ujraindul, ekkor olvassa be az uj beallitast
