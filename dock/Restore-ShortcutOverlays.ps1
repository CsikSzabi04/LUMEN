# Visszahozza a parancsikon-nyilat és az UAC-pajzsot (az eredeti Windows-beállítás). Rendszergazdai jog kell (UAC).
param([switch]$Elevated)
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
if (-not $admin) {
    if ($Elevated) { throw 'Nem sikerult rendszergazdai jogot kapni.' }
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated'
    return
}
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
foreach ($n in '29', '77') { Remove-ItemProperty $k $n -ErrorAction SilentlyContinue }
Stop-Process -Name explorer -Force
