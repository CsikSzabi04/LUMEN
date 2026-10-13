# Letolti a TaskbarX-et a GitHub release-bol (ha meg nincs), majd beallitja, hogy bejelentkezeskor azonnal
# alkalmazza az iOS-es tálcát (Apply-Taskbar.ps1). Nem kell hozza rendszergazdai jog.
#   .\Install-Taskbar.ps1                -> telepites + inditas most
#   .\Install-Taskbar.ps1 -Uninstall     -> eltavolitas, az eredeti tálca visszaall
param([switch]$DownloadOnly, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$tools = Join-Path $PSScriptRoot 'tools\TaskbarX'
$taskName = 'ADVANCED_USE - iOS taskbar'

if ($Uninstall) {
    Unregister-ScheduledTask $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Get-Process TaskbarX -ErrorAction SilentlyContinue | Stop-Process -Force
    & (Join-Path $tools 'TaskbarX.exe') -stop 2>$null
    Stop-Process -Name explorer -Force   # visszaall az alap tálca (az explorer magatol ujraindul)
    return
}

if (-not (Test-Path (Join-Path $tools 'TaskbarX.exe'))) {
    $rel = Invoke-RestMethod 'https://api.github.com/repos/ChrisAnd1998/TaskbarX/releases/latest' -UseBasicParsing
    $asset = $rel.assets | Where-Object name -like 'TaskbarX_*_x64.zip' | Select-Object -First 1
    $zip = Join-Path $env:TEMP $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    New-Item -ItemType Directory -Force $tools | Out-Null
    Expand-Archive $zip $tools -Force; Remove-Item $zip
}
if ($DownloadOnly) { return }

$apply = Join-Path $PSScriptRoot 'Apply-Taskbar.ps1'
$action = New-ScheduledTaskAction -Execute "$env:windir\System32\conhost.exe" `
    -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$apply`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
Register-ScheduledTask $taskName -Action $action -Trigger $trigger -Settings $set -Force | Out-Null

& $apply
