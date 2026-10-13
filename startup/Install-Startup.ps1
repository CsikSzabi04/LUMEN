# Bejelentkezeskor lefuto feladatok beallitasa (Wise Memory Optimizer + Wallpaper Engine).
# Rendszergazdai jog kell (a Wise emelt jogon fut) - a script maga kri el az UAC-ot.
#   .\Install-Startup.ps1             -> telepites (+ a Wise azonnali beallitasa, a WE hatter szinkronja)
#   .\Install-Startup.ps1 -Uninstall  -> a feladatok torlese
param([switch]$Uninstall)
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") + $(if ($Uninstall) { '-Uninstall' })
    Start-Process powershell -Verb RunAs -ArgumentList $a -Wait; return
}
$ErrorActionPreference = 'Stop'
$names = 'ADVANCED_USE - Wise Memory Optimizer', 'ADVANCED_USE - Wallpaper Engine', 'ADVANCED_USE - Wallpaper sync'
if ($Uninstall) { $names | % { Unregister-ScheduledTask $_ -Confirm:$false -ErrorAction SilentlyContinue }; return }

$me = "$env:USERDOMAIN\$env:USERNAME"
function Add-Task($name, $script, $scriptArgs, $trigger, $runLevel) {
    # conhost --headless: ne villanjon fel konzolablak
    $a = New-ScheduledTaskAction -Execute "$env:windir\System32\conhost.exe" -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\$script`" $scriptArgs"
    $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    $p = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel $runLevel
    Register-ScheduledTask $name -Action $a -Trigger $trigger -Settings $s -Principal $p -Force | Out-Null
}
$logon = New-ScheduledTaskTrigger -AtLogOn -User $me
$logonDelay = New-ScheduledTaskTrigger -AtLogOn -User $me; $logonDelay.Delay = 'PT20S'   # a Wise sajat inditasa utan
$every30 = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 30)

Add-Task $names[0] 'Set-WiseOptimizer.ps1'     ''          $logonDelay 'Highest'
Add-Task $names[1] 'Start-WallpaperEngine.ps1' ''          $logon      'Limited'
Add-Task $names[2] 'Start-WallpaperEngine.ps1' '-SyncOnly' $every30    'Limited'

& "$PSScriptRoot\Set-WiseOptimizer.ps1"      # a Wise beallitasa azonnal is
