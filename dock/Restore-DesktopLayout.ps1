# Az asztali ikonok MENTETT (alap) elhelyezését állítja vissza név szerint. Csak az ikonok helyét állítja, mást nem érint.
#   .\Restore-DesktopLayout.ps1          -> csak ellenőriz (nem mozdít semmit)
#   .\Restore-DesktopLayout.ps1 -Apply   -> visszateszi az ikonokat az alap helyükre
# Koordináták: a lista-nézet koordinátái (bal monitor: x 0..1919, jobb/fő monitor: x 1920..3839), rács: x = 16 + 80*k, y = 2 + 98*m
param([switch]$Apply)
$layout = [ordered]@{
    'Antigravity' = 16, 2;            'Obsidian' = 96, 2
    'Visual Studio Code' = 16, 100;   'Visual Studio' = 96, 100
    'LogMeIn Hamachi' = 16, 198;      'ZAP 2.17.0' = 96, 198
    'SKlauncher 4.0' = 16, 394;       'SKlauncher' = 96, 394
    'TeamViewer' = 16, 492;           'Among Us' = 96, 492
    'Wallpaper Engine' = 16, 590
    'Discord' = 16, 884
    'CapCut' = 16, 982;               'Stitch' = 96, 982
    'Microsoft Store' = 1776, 2;      'Xbox' = 1856, 2
    'EA' = 1856, 100
    'GameDataHub' = 1776, 296;        'Community Feed - DBD Hub' = 1856, 296
    'Cs?k Szabolcs*' = 1856, 394
    'MSI Afterburner' = 1776, 590;    'Wise Memory Optimizer' = 1856, 590
    'WizTree' = 1856, 688
    'MinecraftServer' = 1856, 982
    'Epic Games Launcher' = 3696, 2;  'Steam' = 3776, 2
    'Riot Client' = 3696, 100;        'FiveM' = 3776, 100
    'Rockstar Games Launcher' = 3776, 198
    'Dead by Daylight' = 3696, 394;   'Grand Theft Auto V Legacy' = 3776, 394
    'The Elder Scrolls Online' = 3696, 492; 'SeeRPG.exe' = 3776, 492
    'Counter-Strike 2' = 3696, 590;   'League of Legends' = 3776, 590
    'Team Fortress 2' = 3776, 688
    'CliMarkAI' = 1936, 786
    'Brave' = 1936, 884
    'Bin' = 3776, 884;                'Recycle Bin' = 3776, 884
}
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Text;
public static class DeskLV {
  delegate bool EP(IntPtr hh, IntPtr ll);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint pid);
  [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, uint size, uint type, uint prot);
  [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr a, uint size, uint type);
  [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] buf, int size, out int read);
  [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] buf, int size, out int written);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  static IntPtr Find() { IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr x, IntPtr y) { IntPtr dv = FindWindowEx(x, IntPtr.Zero, "SHELLDLL_DefView", null); if (dv != IntPtr.Zero) { IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null); if (lv != IntPtr.Zero) { found = lv; return false; } } return true; }, IntPtr.Zero);
    if (found == IntPtr.Zero) { IntPtr pm = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null); IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null); found = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null); }
    return found; }
  public static List<string> Items() {   // "index|nev|x|y"
    var res = new List<string>(); IntPtr lv = Find(); if (lv == IntPtr.Zero) return res; uint pid; GetWindowThreadProcessId(lv, out pid);
    IntPtr hp = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid); if (hp == IntPtr.Zero) return res;
    int n = (int)SendMessage(lv, 0x1004, IntPtr.Zero, IntPtr.Zero); IntPtr rm = VirtualAllocEx(hp, IntPtr.Zero, 4096, 0x3000, 4);
    for (int i = 0; i < n; i++) {
      byte[] li = new byte[88]; BitConverter.GetBytes(1).CopyTo(li, 0); BitConverter.GetBytes(i).CopyTo(li, 4); BitConverter.GetBytes((long)(rm.ToInt64() + 128)).CopyTo(li, 24); BitConverter.GetBytes(260).CopyTo(li, 32);
      int w; WriteProcessMemory(hp, rm, li, 88, out w); SendMessage(lv, 0x1073, (IntPtr)i, rm);
      byte[] tb = new byte[520]; int rd; ReadProcessMemory(hp, (IntPtr)(rm.ToInt64() + 128), tb, 520, out rd); string name = Encoding.Unicode.GetString(tb).Split('\0')[0];
      SendMessage(lv, 0x1010, (IntPtr)i, (IntPtr)(rm.ToInt64() + 600)); byte[] pb = new byte[8]; ReadProcessMemory(hp, (IntPtr)(rm.ToInt64() + 600), pb, 8, out rd);
      res.Add(i + "|" + name + "|" + BitConverter.ToInt32(pb, 0) + "|" + BitConverter.ToInt32(pb, 4)); }
    VirtualFreeEx(hp, rm, 0, 0x8000); CloseHandle(hp); return res; }
  public static void SetPos(int index, int x, int y) { IntPtr lv = Find(); SendMessage(lv, 0x100F, (IntPtr)index, (IntPtr)((y << 16) | (x & 0xFFFF))); }
}
'@
$items = foreach ($l in [DeskLV]::Items()) { $p = $l.Split('|'); [pscustomobject]@{ Index = [int]$p[0]; Name = $p[1]; X = [int]$p[2]; Y = [int]$p[3] } }
$plan = @(); $unmatched = @()
foreach ($it in $items) {
    $key = $layout.Keys | Where-Object { $it.Name -like $_ } | Sort-Object { $_.Length } -Descending | Select-Object -First 1
    if ($key) { $plan += [pscustomobject]@{ Index = $it.Index; Name = $it.Name; FromX = $it.X; FromY = $it.Y; ToX = $layout[$key][0]; ToY = $layout[$key][1] } } else { $unmatched += $it.Name }
}
"elemek: $($items.Count) | illesztve: $($plan.Count) | nem illeszkedő (új, érintetlen marad): $($unmatched -join ', ')"
$target = @{}; foreach ($p in $plan) { $target[$p.Index] = @($p.ToX, $p.ToY) }
function Get-Cur { $r = @{}; foreach ($l in [DeskLV]::Items()) { $q = $l.Split('|'); $r[[int]$q[0]] = @([int]$q[2], [int]$q[3], $q[1]) }; $r }
$cur = Get-Cur
$wrong = @($target.Keys | Where-Object { $cur[$_][0] -ne $target[$_][0] -or $cur[$_][1] -ne $target[$_][1] })
"áthelyezendő: $($wrong.Count)"
if (-not $Apply) { foreach ($k in $wrong) { "{0,-34} {1},{2} -> {3},{4}" -f $cur[$k][2], $cur[$k][0], $cur[$k][1], $target[$k][0], $target[$k][1] }; return }
for ($pass = 1; $pass -le 12; $pass++) {
    $cur = Get-Cur
    $wrong = @($target.Keys | Where-Object { $cur[$_][0] -ne $target[$_][0] -or $cur[$_][1] -ne $target[$_][1] }); if ($wrong.Count -eq 0) { break }
    $occ = @{}; foreach ($k in $cur.Keys) { $occ["$($cur[$k][0]),$($cur[$k][1])"] = $k }
    $moved = 0
    foreach ($k in $wrong) {                       # csak SZABAD célcellába mozgatunk (különben az Explorer máshová teszi)
        $cell = "$($target[$k][0]),$($target[$k][1])"
        if (-not $occ.ContainsKey($cell) -or $occ[$cell] -eq $k) { [DeskLV]::SetPos($k, $target[$k][0], $target[$k][1]); $occ.Remove("$($cur[$k][0]),$($cur[$k][1])"); $occ[$cell] = $k; $moved++ }
    }
    if ($moved -eq 0) {                            # körkörös foglaltság: az egyik ikont szabad átmeneti cellába tesszük
        $k = $wrong[0]; foreach ($tx in 1936, 2016, 2096) { foreach ($ty in 1080, 982, 884) { if (-not $occ.ContainsKey("$tx,$ty")) { [DeskLV]::SetPos($k, $tx, $ty); break } } break }
    }
    Start-Sleep -Milliseconds 700
}
$cur = Get-Cur; $bad = 0
foreach ($k in $target.Keys) { if ($cur[$k][0] -ne $target[$k][0] -or $cur[$k][1] -ne $target[$k][1]) { $bad++; "nem sikerült: $($cur[$k][2]) -> $($cur[$k][0]),$($cur[$k][1]) (cél $($target[$k][0]),$($target[$k][1]))" } }
"kész, eltérés: $bad"
