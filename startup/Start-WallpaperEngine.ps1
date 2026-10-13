# Wallpaper Engine: induláskor azonnal a beállított háttér, ablak nélkül.
#  1) A WE config.json-ból kiolvassa, melyik háttér van monitoronként kiválasztva, és annak előnézeti képét
#     beállítja Windows-háttérnek -> amíg a WE betölt, nem az alap háttér látszik, hanem ugyanaz a kép.
#  2) Elindítja a Wallpaper Engine-t -silent móddal (nem nyílik meg a felület, csak a tálcaikon).
# -SyncOnly: csak a Windows-háttér szinkronizálása (a 30 percenkénti feladat ezt használja).
param([switch]$SyncOnly)
$ErrorActionPreference = 'Stop'

$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run').WallpaperEngine
$exe = if ($run -match '"([^"]+wallpaper(?:64|32)\.exe)"') { $Matches[1] } else { throw 'Wallpaper Engine nem talalhato' }
$weDir = Split-Path $exe
$cache = Join-Path $env:LOCALAPPDATA 'ADVANCED_USE'; New-Item -ItemType Directory -Force $cache | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper {
  void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
  [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
  [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
  uint GetMonitorDevicePathCount();
  void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out int l, out int t, out int r, out int b);
  void SetBackgroundColor(uint color);
  uint GetBackgroundColor();
  void SetPosition(int position);
}
[ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")] public class DesktopWallpaperClass {}
public static class Wp {
  // monitoronkent beallitja a hatteret (images[i] -> i. monitor, a tobbi az utolsot kapja), 4 = kitoltes
  public static void Apply(string[] images) {
    var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
    uint n = dw.GetMonitorDevicePathCount();
    for (uint i = 0; i < n; i++) {
      string id = dw.GetMonitorDevicePathAt(i), img = images[Math.Min((int)i, images.Length - 1)];
      if (!string.IsNullOrEmpty(id) && dw.GetWallpaper(id) != img) { dw.SetWallpaper(id, img); dw.SetPosition(4); }
    }
  }
}
'@

function Get-Preview($file) {
    $dir = Split-Path $file; $pj = Join-Path $dir 'project.json'
    $name = if (Test-Path $pj) { (Get-Content $pj -Raw | ConvertFrom-Json).preview } else { 'preview.jpg' }
    $img = Join-Path $dir $name
    if ($img -match '\.(jpe?g|png|bmp)$') { return $img }
    $out = Join-Path $cache ((Split-Path $dir -Leaf) + '.jpg')           # gif -> jpg (elso kocka)
    if (-not (Test-Path $out)) { $b = [Drawing.Image]::FromFile($img); $b.Save($out, [Drawing.Imaging.ImageFormat]::Jpeg); $b.Dispose() }
    $out
}

function Sync-Wallpaper {
    $cfg = Get-Content (Join-Path $weDir 'config.json') -Raw | ConvertFrom-Json
    $user = $cfg.PSObject.Properties | ? { $_.Value.general.wallpaperconfig } | Select-Object -First 1
    $sel = $user.Value.general.wallpaperconfig.selectedwallpapers
    $files = @($sel.PSObject.Properties | Sort-Object Name | % { $_.Value.file } | ? { $_ })
    if (-not $files) { return }
    [Wp]::Apply([string[]]($files | % { Get-Preview $_ }))
}

try { Sync-Wallpaper } catch { Add-Content (Join-Path $cache 'boot.log') "$(Get-Date -f s) sync: $_" }
if (-not $SyncOnly -and -not (Get-Process wallpaper64, wallpaper32 -ErrorAction SilentlyContinue)) {
    Start-Process $exe -ArgumentList '-silent'
}
