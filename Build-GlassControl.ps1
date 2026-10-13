# Lefordítja a GlassControl.exe-t (GlassControlWpf.cs + a terminál-mag: terminal\TermCore.cs) a gépen levő .NET Framework fordítóval.
$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $PSScriptRoot 'GlassControl.exe'
Get-Process GlassControl -ErrorAction SilentlyContinue | Stop-Process -Force
$a = @('/nologo', '/target:winexe', '/optimize+', "/out:$out", "/lib:$fw\WPF", '/r:PresentationCore.dll', '/r:PresentationFramework.dll', '/r:WindowsBase.dll', '/r:System.Xaml.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Core.dll')
$ico = Join-Path $PSScriptRoot 'Lumen.ico'
if (Test-Path $ico) { $a += "/win32icon:$ico" }
$a += (Join-Path $PSScriptRoot 'GlassControlWpf.cs'); $a += (Join-Path $PSScriptRoot 'GlassUiPanel.cs'); $a += (Join-Path $PSScriptRoot 'Loc.cs'); $a += "/resource:$(Join-Path $PSScriptRoot 'lang\lang.tsv'),lang.tsv"; foreach ($f in 'DevHub.cs','DevHubProjects.cs','DevHubMisc.cs','HelpVault.cs','HelpContent.cs','LumenExtras.cs','WeeklyReport.cs','TaskPerf.cs') { $a += (Join-Path $PSScriptRoot $f) }; $a += (Join-Path $PSScriptRoot 'Installer.cs'); $a += (Join-Path $PSScriptRoot 'WiperAdvanced.cs'); $a += (Join-Path $PSScriptRoot 'GlassAnalytics.cs'); $a += (Join-Path $PSScriptRoot 'GlassAnalyticsStore.cs'); $a += (Join-Path $PSScriptRoot 'terminal\GlassMenu.cs'); foreach ($f in 'TermCore.cs','TermConfig.cs','TermView.cs','TermPreview.cs','TermHost.cs','FeatureDoc.cs') { $a += (Join-Path $PSScriptRoot (Join-Path 'terminal' $f)) }
& "$fw\csc.exe" @a
if ($LASTEXITCODE -eq 0) { "OK: $out" } else { throw "Forditasi hiba" }
