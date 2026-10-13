# iOS-szeru, kozepre igazitott, lekerekitett, uvegszeru (acrylic) tálca Windows 10-en.
# Eszkoz: TaskbarX (GitHub: ChrisAnd1998/TaskbarX) - tools\TaskbarX\TaskbarX.exe
# Itt allithato a kinezet; a paramok magyarazata: https://github.com/ChrisAnd1998/TaskbarX#parameters
$TaskbarX = @(
    '-tbs=3'            # stilus: 1 atlatszo, 2 blur, 3 acrylic, 4 gradiens, 5 opaque
    '-color=24;24;28;70'  # szin R;G;B;A (sotet "uveg"; A kisebb = atlatszobb/uvegesebb, 0-255)
    '-tbr=18'           # lekerekites merteke
    '-tbsg=1'           # szegmensek: kulon "pilula" a start / ikonok / tray resznek
    '-as=cubiceaseinout'# ikon-animacio (a "none" azonnali)
    '-asp=450'          # animacio sebessege (ms)
    '-cfsa=1'           # teljes kepernyos appnal szuneteljen
    '-sti=0'            # ne legyen kulon tray ikon
)
$HideSearch   = $true   # Keresés mezo elrejtese
$HideTaskView = $true   # Feladatnézet gomb elrejtese
$BigIcons     = $true   # normal meretu ikonok (iOS dock-szeru), a kis ikonos mod helyett
$HideToolbars = $true   # "Hivatkozások" stb. eszkoztarak eltavolitasa (egyszer fut le, az explorer ujraindul; mentes: backup-taskbar-toolbars.reg)
$TaskbarHeight = 40    # a tálca magassaga px-ben (100%-os skalazasnal 40 = normal ikonmeret)
$UseDock      = $true   # true: a sajat GlassDock (..\dock) a pilula (mozgathato, atmeretezheto); false: TaskbarX pilula
$AutoHide     = $true   # a Windows tálca automatikusan elrejtőzik (nem foglal helyet, a maximalizált ablak teljes képernyős); a megjelenítését a dokk vezérli
$SecondaryTaskbar = $false # false: nincs tálca a 2. monitoron (ott nem foglal le üres sávot)

$exe = Join-Path $PSScriptRoot 'tools\TaskbarX\TaskbarX.exe'
if (-not $UseDock -and -not (Test-Path $exe)) { & (Join-Path $PSScriptRoot 'Install-Taskbar.ps1') -DownloadOnly }

# --- tálca-beallitasok (registry, explorer ujrainditas nelkul is ervenyes lesz a legtobb) ---
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
if ($HideSearch)   { Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' SearchboxTaskbarMode 0 -Type DWord }
if ($HideTaskView) { Set-ItemProperty $adv ShowTaskViewButton 0 -Type DWord }
if ($BigIcons)     { Set-ItemProperty $adv TaskbarSmallIcons 0 -Type DWord }
Set-ItemProperty $adv MMTaskbarEnabled ([int]$SecondaryTaskbar) -Type DWord

# --- eszkoztarak (Hivatkozasok) eltavolitasa: ez adja a masodik sort a talcan; egyszer fut le ---
Get-Process TaskbarX -ErrorAction SilentlyContinue | Stop-Process -Force
$flag = Join-Path $env:LOCALAPPDATA 'ADVANCED_USE\toolbars-reset'
if ($HideToolbars -and -not (Test-Path $flag)) {
    New-Item -ItemType Directory -Force (Split-Path $flag) | Out-Null
    reg export 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Streams\Desktop' (Join-Path $PSScriptRoot 'backup-taskbar-toolbars.reg') /y | Out-Null
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Streams\Desktop' TaskbarWinXP -ErrorAction SilentlyContinue
    Stop-Process -Name explorer -Force      # az explorer magatol ujraindul, a megnyitott programokat nem erinti
    Start-Sleep 4
    New-Item $flag -Force | Out-Null
}
# --- tálca magassaga (a StuckRects3 binarisban: [20] = magassag, [28] = tetejenek y-ja); ha elter, javitja + explorer ujrainditas ---
$sr = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
$k = [byte[]](Get-ItemProperty $sr).Settings
if ($k[12] -eq 3 -and [BitConverter]::ToInt32($k, 20) -ne $TaskbarHeight) {       # 3 = alul lévő tálca
    [BitConverter]::GetBytes([int]$TaskbarHeight).CopyTo($k, 20)
    [BitConverter]::GetBytes([BitConverter]::ToInt32($k, 36) - $TaskbarHeight).CopyTo($k, 28)
    Set-ItemProperty $sr Settings $k
    Stop-Process -Name explorer -Force; Start-Sleep 4
}
# --- automatikus elrejtes (mindket monitoron, explorer ujrainditas nelkul) ---
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class TbHide {
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public uint cb; public IntPtr hWnd; public uint msg, edge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m, ref APPBARDATA d);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
  public static void Set(bool on) {
    var d = new APPBARDATA { cb = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = FindWindow("Shell_TrayWnd", null) };
    bool isOn = (((ulong)SHAppBarMessage(4, ref d)) & 1) != 0;   // ABM_GETSTATE: ha mar jo az allapot, NEM nyulunk hozza (a felesleges ABM_SETSTATE megbolygatja az asztali ikonokat)
    if (isOn == on) return;
    d.lParam = (IntPtr)(on ? 3 : 2);
    SHAppBarMessage(0xA, ref d);   // ABM_SETSTATE: 1 = autohide, 2 = always on top
  }
}
'@
[TbHide]::Set($AutoHide)

# --- pilula inditasa: sajat GlassDock (alap) vagy TaskbarX ---
if ($UseDock) {
    $dock = Join-Path $PSScriptRoot '..\dock\GlassDock.exe'
    if (-not (Test-Path $dock)) { & (Join-Path $PSScriptRoot '..\dock\Build.ps1') }
    if (-not (Get-Process GlassDock -ErrorAction SilentlyContinue)) { Start-Process (Resolve-Path $dock) }
} else {
    Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Start-Process $exe -ArgumentList $TaskbarX -WindowStyle Hidden
}

