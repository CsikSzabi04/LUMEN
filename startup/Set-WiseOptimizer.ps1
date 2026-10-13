# Wise Memory Optimizer: optimalizál, ha a RAM-használat eléri az 50%-ot (elérhető memória < 50%).
# Rendszergazdai jog kell (a Wise is emelt jogon fut) - a feladatütemező "Highest"-tel indítja.
$Threshold = 50   # %, ennyi alatti elérhető memóriánál optimalizál (= 100 - használat)

$ini = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Wise Memory Optimzer\MemoryConfig.ini'
$dir = (Get-ItemProperty 'HKLM:\SOFTWARE\WiseCleaner\Wise Memory Optimizer').path
$exe = Join-Path $dir 'WiseMemoryOptimzer.exe'   # (a Wise elírása, így hívják a fájlt)

$want = [ordered]@{ AutoOpt = 1; AutoRun = 1; LowmemOpt = 1; LowMem = $Threshold; periodOpt = 0; Idle = 1; MinToTray = 1; ShowNotice = 0; ClearMem = 1; ClearStandy = 1 }

$lines = [IO.File]::ReadAllLines($ini); $changed = $false
foreach ($k in $want.Keys) {
    $i = [Array]::FindIndex($lines, [Predicate[string]]{ param($l) $l -match "^$k=" })
    $new = "$k=$($want[$k])"
    if ($i -ge 0 -and $lines[$i] -ne $new) { $lines[$i] = $new; $changed = $true }
}

$p = Get-Process WiseMemoryOptimzer -ErrorAction SilentlyContinue
if ($changed) {
    if ($p) { $p | Stop-Process -Force; Start-Sleep 1 }     # kilépéskor felülírná az ini-t, ezért előtte állítjuk le
    [IO.File]::WriteAllLines($ini, $lines)
    $p = $null
}
if (-not $p) { Start-Process $exe -ArgumentList '-a' }       # -a: a Wise saját csendes (tálcás) indítása
