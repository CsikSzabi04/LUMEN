# Builds Lumen, GlassDock and Terminal with the .NET Framework compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build-GlassControl.ps1')
& (Join-Path $PSScriptRoot 'dock\Build.ps1')
& (Join-Path $PSScriptRoot 'terminal\Build.ps1') -KeepRunning
'Done. Start: .\GlassControl.exe /tray   and   explorer.exe "$PSScriptRoot\dock\GlassDock.exe"'
