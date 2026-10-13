# Lefordítja a Terminal.exe-t (macOS Terminal kinézetű terminál) a gépen levő .NET Framework fordítóval.
param([switch]$KeepRunning)
$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $PSScriptRoot 'Terminal.exe'
if (-not $KeepRunning) { Get-Process Terminal -ErrorAction SilentlyContinue | Stop-Process -Force }
$ico = Join-Path $PSScriptRoot 'Terminal.ico'
$args2 = @('-nologo', '/target:winexe', '/optimize+', "/out:$out", "/lib:$fw\WPF", '/r:PresentationCore.dll', '/r:PresentationFramework.dll', '/r:WindowsBase.dll', '/r:System.Xaml.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
if (Test-Path $ico) { $args2 += "/win32icon:$ico" }
$args2 += (Join-Path $PSScriptRoot '..\Loc.cs'); $args2 += ('/resource:' + (Join-Path $PSScriptRoot '..\lang\lang.tsv') + ',lang.tsv')
foreach ($f in 'TermCore.cs','TermConfig.cs','TermView.cs','TermPreview.cs','TermHost.cs','FeatureDoc.cs','TermApp.cs','GlassMenu.cs') { $args2 += (Join-Path $PSScriptRoot $f) }
& "$fw\csc.exe" @args2
if ($LASTEXITCODE -eq 0) { "OK: $out" } else { throw "Forditasi hiba" }
