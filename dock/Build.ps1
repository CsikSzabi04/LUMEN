# Lefordítja a GlassDock.cs-t GlassDock.exe-re (a gépen levő .NET Framework fordítóval, nem kell hozzá semmi telepítés).
$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $PSScriptRoot 'GlassDock.exe'
if (Get-Process GlassDock -ErrorAction SilentlyContinue) {
    if (Test-Path $out) { & $out /restore }          # a tálca ikonjai/hátterei visszaállnak, mielőtt a dokk leáll
    Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force
}
& "$fw\csc.exe" /nologo /target:winexe /optimize+ "/out:$out" "/lib:$fw\WPF" `
    /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Data.dll `
    (Join-Path $PSScriptRoot '..\Loc.cs') ('/resource:' + (Join-Path $PSScriptRoot '..\lang\lang.tsv') + ',lang.tsv') `
    (Join-Path $PSScriptRoot 'GlassDock.cs') (Join-Path $PSScriptRoot 'Spotlight.cs') (Join-Path $PSScriptRoot 'Theme.cs') (Join-Path $PSScriptRoot '..\terminal\GlassMenu.cs')
if ($LASTEXITCODE -eq 0) { "OK: $out" } else { throw "Forditasi hiba" }
