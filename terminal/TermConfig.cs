// Lumen Terminal – beállítások (term.ini), témák, shell-profilok és a PowerShell-integráció.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;

namespace GlassTerm
{
    // ------------------------------------------------------------------------------------------ term.ini
    public static class Cfg
    {
        public static string Dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        static Dictionary<string, string> d; static readonly object lk = new object(); static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static void Load()
        {
            if (d != null) return; d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { string p = Path.Combine(Dir, "term.ini"); if (File.Exists(p)) foreach (string l in File.ReadAllLines(p)) { int i = l.IndexOf('='); if (i > 0 && !l.TrimStart().StartsWith("#")) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim(); } } catch { }
        }
        public static string Get(string k, string def) { lock (lk) { Load(); string v; return d.TryGetValue(k, out v) && v.Length > 0 ? v : def; } }
        public static int GetInt(string k, int def) { int n; return int.TryParse(Get(k, ""), out n) ? n : def; }
        public static double GetDouble(string k, double def) { double n; return double.TryParse(Get(k, ""), NumberStyles.Float, IC, out n) ? n : def; }
        public static bool GetBool(string k, bool def) { string v = Get(k, ""); if (v.Length == 0) return def; return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("igen", StringComparison.OrdinalIgnoreCase); }
        public static void Set(string k, string v) { lock (lk) { Load(); d[k] = v; } }
        public static void SetBool(string k, bool v) { Set(k, v ? "1" : "0"); }
        public static void SetDouble(string k, double v) { Set(k, v.ToString("0.###", IC)); }
        public static void Save()
        {
            lock (lk)
            {
                Load();
                try
                {
                    List<string> l = new List<string> { Loc.T("# Lumen Terminal beállításai (a Terminal maga írja; kézzel is szerkeszthető, mentés után indítsd újra)"), Loc.T("# shell: powershell | pwsh | cmd | tetszőleges parancssor"), Loc.T("# profile.1=Név|parancssor|induló mappa   (saját profilok a + menüben)") };
                    List<string> keys = new List<string>(d.Keys); keys.Sort(StringComparer.OrdinalIgnoreCase); foreach (string k in keys) l.Add(k + "=" + d[k]);
                    string p = Path.Combine(Dir, "term.ini"), tmp = p + ".tmp"; File.WriteAllLines(tmp, l.ToArray(), new UTF8Encoding(false)); if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------------------------------ témák
    public class TermTheme
    {
        public string Name; public Color Fg, Bg, Cursor, Sel; public Color[] P = new Color[16]; public bool Light;
        static Color H(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
        static TermTheme T(string name, int bg, int fg, int cur, int sel, int[] pal) { TermTheme t = new TermTheme { Name = name, Bg = H(bg), Fg = H(fg), Cursor = H(cur), Sel = H(sel) }; for (int i = 0; i < 16; i++) t.P[i] = H(pal[i]); t.Light = (0.299 * t.Bg.R + 0.587 * t.Bg.G + 0.114 * t.Bg.B) > 150; return t; }
        static readonly int[] Basic = { 0x000000, 0xC23621, 0x25BC24, 0xADAD27, 0x5A4AE8, 0xD338D3, 0x33BBC8, 0xCBCCCD, 0x818383, 0xFC391F, 0x31E722, 0xEAEC23, 0x6F6BFF, 0xF935F8, 0x14F0F0, 0xE9EBEB };
        public static readonly List<TermTheme> All = new List<TermTheme> {
            T("macOS Pro", 0x1E1E1E, 0xECECEC, 0xC4C4C6, 0x6E96DC, Basic),
            T(Loc.T("macOS Basic (világos)"), 0xFFFFFF, 0x000000, 0x4C4C4C, 0xA9C5F3, new[] { 0x000000, 0xC23621, 0x25BC24, 0x949400, 0x492EE1, 0xD338D3, 0x33BBC8, 0xCBCCCD, 0x818383, 0xFC391F, 0x31E722, 0xB5B500, 0x5833FF, 0xF935F8, 0x14B0B0, 0xE9EBEB }),
            T("Homebrew", 0x000000, 0x00FF00, 0x23FF18, 0x1E501E, Basic),
            T("Ocean", 0x224FBC, 0xFFFFFF, 0x7F7F7F, 0x3A6CDD, Basic),
            T("Dracula", 0x282A36, 0xF8F8F2, 0xF8F8F2, 0x44475A, new[] { 0x21222C, 0xFF5555, 0x50FA7B, 0xF1FA8C, 0xBD93F9, 0xFF79C6, 0x8BE9FD, 0xF8F8F2, 0x6272A4, 0xFF6E6E, 0x69FF94, 0xFFFFA5, 0xD6ACFF, 0xFF92DF, 0xA4FFFF, 0xFFFFFF }),
            T("Nord", 0x2E3440, 0xD8DEE9, 0xD8DEE9, 0x434C5E, new[] { 0x3B4252, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x88C0D0, 0xE5E9F0, 0x4C566A, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x8FBCBB, 0xECEFF4 }),
            T("Solarized Dark", 0x002B36, 0x839496, 0x93A1A1, 0x073642, new[] { 0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5, 0x002B36, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3 }),
            T("Solarized Light", 0xFDF6E3, 0x657B83, 0x586E75, 0xEEE8D5, new[] { 0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5, 0x002B36, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3 }),
            T("Tokyo Night", 0x1A1B26, 0xC0CAF5, 0xC0CAF5, 0x33467C, new[] { 0x15161E, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xA9B1D6, 0x414868, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xC0CAF5 }),
            T("One Dark", 0x282C34, 0xABB2BF, 0x528BFF, 0x3E4451, new[] { 0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xABB2BF, 0x5C6370, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xFFFFFF }),
            T("Gruvbox Dark", 0x282828, 0xEBDBB2, 0xEBDBB2, 0x504945, new[] { 0x282828, 0xCC241D, 0x98971A, 0xD79921, 0x458588, 0xB16286, 0x689D6A, 0xA89984, 0x928374, 0xFB4934, 0xB8BB26, 0xFABD2F, 0x83A598, 0xD3869B, 0x8EC07C, 0xEBDBB2 }),
            T("Monokai", 0x272822, 0xF8F8F2, 0xF8F8F0, 0x49483E, new[] { 0x272822, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF8F8F2, 0x75715E, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF9F8F5 }),
            T("Matrix", 0x0D0D0D, 0x00FF41, 0x00FF41, 0x0B3D14, new[] { 0x000000, 0x008F11, 0x00FF41, 0x6EE06E, 0x00A86B, 0x2E8B57, 0x00CED1, 0x9AE59A, 0x2F4F2F, 0x00B300, 0x39FF14, 0xADFF2F, 0x3CB371, 0x66CDAA, 0x7FFFD4, 0xCCFFCC }),
            T("Red Sands", 0x7A251E, 0xD7C9A7, 0xFFFFFF, 0x4A1511, Basic),
            T("Man Page", 0xFEF49C, 0x000000, 0x000000, 0xFFE55B, new[] { 0x000000, 0xB22222, 0x228B22, 0x8B6914, 0x1E50A2, 0x8B008B, 0x008B8B, 0x555555, 0x555555, 0xCD2626, 0x2E8B2E, 0xA07C14, 0x2E64C8, 0xAA00AA, 0x00A0A0, 0x222222 })
        };
        public static TermTheme Find(string name) { foreach (TermTheme t in All) if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t; return All[0]; }
    }

    // ------------------------------------------------------------------------------------------ közös beállítások
    public class TermSettings
    {
        public static readonly TermSettings I = new TermSettings(); public event Action Changed;
        public string Theme = "macOS Pro", Font = "DejaVu Sans Mono";
        public double FontSize = 15, LineFactor = 1.2, Opacity = 1.0; public int PadX = 10, PadY = 6, CursorStyle, Scrollback = 20000, NotifySec = 10;
        public bool SecretGuard = true, CursorBlink, VisualBell = true, AudibleBell, Badges = true, PasteConfirm = true, SmartPaste = true, AutoCopy, MiddlePaste = true, LinkNeedsCtrl = false, Notify = true, DimInactive = true, RestoreSession = true, ConfirmClose, ShowTabsAlways, ScrollOnOutput, ScrollOnKey = true, TrimCopy = true, AutoPin;
        public int WheelLines = 3;

        public void Load()
        {
            Theme = Cfg.Get("theme", Theme); Font = Cfg.Get("font", Font); FontSize = Cfg.GetDouble("fontsize", FontSize); LineFactor = Cfg.GetDouble("linefactor", LineFactor); Opacity = Cfg.GetDouble("opacity", Opacity);
            PadX = Cfg.GetInt("padx", PadX); PadY = Cfg.GetInt("pady", PadY); CursorStyle = Cfg.GetInt("cursorstyle", CursorStyle); Scrollback = Cfg.GetInt("scrollback", Scrollback); NotifySec = Cfg.GetInt("notifysec", NotifySec); WheelLines = Cfg.GetInt("wheellines", WheelLines);
            CursorBlink = Cfg.GetBool("cursorblink", CursorBlink); VisualBell = Cfg.GetBool("visualbell", VisualBell); AudibleBell = Cfg.GetBool("audiblebell", AudibleBell); Badges = Cfg.GetBool("badges", Badges); PasteConfirm = Cfg.GetBool("pasteconfirm", PasteConfirm);
            SmartPaste = Cfg.GetBool("smartpaste", SmartPaste); AutoCopy = Cfg.GetBool("autocopy", AutoCopy); MiddlePaste = Cfg.GetBool("middlepaste", MiddlePaste); LinkNeedsCtrl = Cfg.GetBool("linkctrl", LinkNeedsCtrl); Notify = Cfg.GetBool("notify", Notify);
            DimInactive = Cfg.GetBool("diminactive", DimInactive); SecretGuard = Cfg.GetBool("secretguard", SecretGuard); RestoreSession = Cfg.GetBool("restoresession", RestoreSession); ConfirmClose = Cfg.GetBool("confirmclose", ConfirmClose); ShowTabsAlways = Cfg.GetBool("tabsalways", ShowTabsAlways);
            ScrollOnOutput = Cfg.GetBool("scrolloutput", ScrollOnOutput); ScrollOnKey = Cfg.GetBool("scrollkey", ScrollOnKey); TrimCopy = Cfg.GetBool("trimcopy", TrimCopy);
        }
        public void Save()
        {
            Cfg.Set("theme", Theme); Cfg.Set("font", Font); Cfg.SetDouble("fontsize", FontSize); Cfg.SetDouble("linefactor", LineFactor); Cfg.SetDouble("opacity", Opacity); Cfg.Set("padx", PadX.ToString()); Cfg.Set("pady", PadY.ToString()); Cfg.Set("cursorstyle", CursorStyle.ToString());
            Cfg.Set("scrollback", Scrollback.ToString()); Cfg.Set("notifysec", NotifySec.ToString()); Cfg.Set("wheellines", WheelLines.ToString());
            Cfg.SetBool("cursorblink", CursorBlink); Cfg.SetBool("visualbell", VisualBell); Cfg.SetBool("audiblebell", AudibleBell); Cfg.SetBool("badges", Badges); Cfg.SetBool("pasteconfirm", PasteConfirm); Cfg.SetBool("smartpaste", SmartPaste); Cfg.SetBool("autocopy", AutoCopy);
            Cfg.SetBool("middlepaste", MiddlePaste); Cfg.SetBool("linkctrl", LinkNeedsCtrl); Cfg.SetBool("notify", Notify); Cfg.SetBool("diminactive", DimInactive); Cfg.SetBool("secretguard", SecretGuard); Cfg.SetBool("restoresession", RestoreSession); Cfg.SetBool("confirmclose", ConfirmClose);
            Cfg.SetBool("tabsalways", ShowTabsAlways); Cfg.SetBool("scrolloutput", ScrollOnOutput); Cfg.SetBool("scrollkey", ScrollOnKey); Cfg.SetBool("trimcopy", TrimCopy); Cfg.Save();
        }
        public void Apply() { if (Changed != null) Changed(); }
        public TermTheme CurTheme { get { return TermTheme.Find(Theme); } }
    }

    // ------------------------------------------------------------------------------------------ shell-profilok
    public class ShellProfile
    {
        public string Name, CmdLine, Cwd, Kind, PromptRegex, Icon = ">_";
        public ShellProfile Clone() { return (ShellProfile)MemberwiseClone(); }
    }

    public static class ShellProfiles
    {
        public const string PsVersion = "5";
        static string FindExe(params string[] candidates) { foreach (string c in candidates) { try { if (File.Exists(c)) return c; } catch { } } return null; }
        static string OnPath(string exe)
        {
            string p = Environment.GetEnvironmentVariable("PATH") ?? ""; foreach (string dir in p.Split(';')) { try { if (dir.Trim().Length == 0) continue; string f = Path.Combine(dir.Trim(), exe); if (File.Exists(f)) return f; } catch { } }
            return null;
        }
        public static string PsRegex { get { return @"^[\w.\-]+@[\w.\-]+ .*? %(?: |$)"; } }
        public static string CmdRegex { get { return @"^[A-Za-z]:\\[^>]*>"; } }

        static string ProfileScript { get { return Path.Combine(Cfg.Dir, "lumen-profile.ps1"); } }

        // a PowerShell-integráció (prompt, git ág, kimeneti kód, segédparancsok) külön, szerkeszthető fájlban él; verzióváltáskor frissül
        public static void EnsureProfileScript()
        {
            try
            {
                string f = ProfileScript, cur = ""; if (File.Exists(f)) using (StreamReader sr = new StreamReader(f, Encoding.UTF8)) cur = (sr.ReadLine() ?? "").Trim();
                if (cur == "# lumen-profile v" + PsVersion) return;
                File.WriteAllText(f, PsScript.Replace("@@V@@", PsVersion), new UTF8Encoding(true));
            }
            catch { }
        }

        public static string BuildPs(bool pwsh, bool mac)
        {
            string exe = pwsh ? (OnPath("pwsh.exe") ?? "pwsh.exe") : Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            if (!mac) return "\"" + exe + "\" -NoLogo";
            EnsureProfileScript();
            return "\"" + exe + "\" -NoLogo -NoExit -ExecutionPolicy Bypass -Command \". '" + ProfileScript.Replace("'", "''") + "'\"";
        }

        public static ShellProfile Default()
        {
            string sh = Cfg.Get("shell", "powershell"); string low = sh.ToLowerInvariant(); bool mac = Cfg.GetBool("macprompt", true);
            foreach (ShellProfile p in Detect()) if (string.Equals(p.Name, sh, StringComparison.OrdinalIgnoreCase)) return p;
            if (low == "powershell") return Detect()[0];
            if (low == "pwsh") return new ShellProfile { Name = "PowerShell 7", Kind = "ps", CmdLine = BuildPs(true, mac), PromptRegex = PsRegex };
            if (low == "cmd") return new ShellProfile { Name = Loc.T("Parancssor"), Kind = "cmd", CmdLine = "cmd.exe", PromptRegex = CmdRegex };
            return new ShellProfile { Name = sh, Kind = "custom", CmdLine = sh };
        }

        static List<ShellProfile> cache; static DateTime cacheAt = DateTime.MinValue;
        public static List<ShellProfile> Detect()
        {
            if (cache != null && (DateTime.Now - cacheAt).TotalSeconds < 60) return cache;
            bool mac = Cfg.GetBool("macprompt", true); List<ShellProfile> l = new List<ShellProfile>();
            l.Add(new ShellProfile { Name = "Windows PowerShell", Kind = "ps", CmdLine = BuildPs(false, mac), PromptRegex = mac ? PsRegex : null, Icon = "PS" });
            if (OnPath("pwsh.exe") != null || File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe")) l.Add(new ShellProfile { Name = "PowerShell 7", Kind = "ps", CmdLine = BuildPs(true, mac), PromptRegex = mac ? PsRegex : null, Icon = "PS7" });
            l.Add(new ShellProfile { Name = Loc.T("Parancssor (cmd)"), Kind = "cmd", CmdLine = "cmd.exe /k prompt $P$G", PromptRegex = CmdRegex, Icon = "C:\\" });
            string gitBash = FindExe(@"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe", Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Programs\Git\bin\bash.exe");
            if (gitBash != null) l.Add(new ShellProfile { Name = "Git Bash", Kind = "bash", CmdLine = "\"" + gitBash + "\" --login -i", Icon = "$" });
            string wsl = OnPath("wsl.exe");
            if (wsl != null)
            {
                List<string> distros = WslDistros(); if (distros.Count == 0) l.Add(new ShellProfile { Name = "WSL", Kind = "wsl", CmdLine = "wsl.exe", Icon = "λ" });
                foreach (string dn in distros) l.Add(new ShellProfile { Name = "WSL: " + dn, Kind = "wsl", CmdLine = "wsl.exe -d \"" + dn.Replace("\"", "") + "\"", Icon = "λ" });
            }
            string py = OnPath("python.exe") ?? OnPath("py.exe"); if (py != null) l.Add(new ShellProfile { Name = "Python", Kind = "repl", CmdLine = "\"" + py + "\"", Icon = "py" });
            string node = OnPath("node.exe"); if (node != null) l.Add(new ShellProfile { Name = "Node.js", Kind = "repl", CmdLine = "\"" + node + "\"", Icon = "js" });
            foreach (string h in SshHosts()) { if (h.StartsWith("-") || h.IndexOfAny(new char[] { (char)34, (char)39, (char)32, (char)38, (char)124, (char)59 }) >= 0) continue; l.Add(new ShellProfile { Name = "SSH: " + h, Kind = "ssh", CmdLine = "ssh.exe -- " + h, Icon = "ssh" }); }
            for (int i = 1; i < 30; i++) { string v = Cfg.Get("profile." + i, ""); if (v.Length == 0) continue; string[] p = v.Split('|'); if (p.Length >= 2) l.Add(new ShellProfile { Name = p[0], Kind = "custom", CmdLine = p[1], Cwd = p.Length > 2 ? p[2] : null, Icon = "★" }); }
            cache = l; cacheAt = DateTime.Now; return l;
        }

        static List<string> WslDistros()
        {
            List<string> r = new List<string>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("wsl.exe", "-l -q") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.Unicode };
                using (Process p = Process.Start(psi)) { System.Threading.Tasks.Task<string> rd = System.Threading.Tasks.Task.Factory.StartNew<string>(delegate { return p.StandardOutput.ReadToEnd(); }); string o = ""; if (!rd.Wait(2500)) { try { p.Kill(); } catch { } return r; } o = rd.Result; p.WaitForExit(500); foreach (string x in o.Split('\n')) { string n = x.Replace("\0", "").Replace("\r", "").Trim(); if (n.Length > 0 && n.IndexOf("docker-desktop", StringComparison.OrdinalIgnoreCase) < 0) r.Add(n); } }
            }
            catch { }
            return r;
        }

        static List<string> SshHosts()
        {
            List<string> r = new List<string>();
            try
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".ssh\config");
                if (File.Exists(f)) foreach (string l in File.ReadAllLines(f)) { string t = l.Trim(); if (t.StartsWith("Host ", StringComparison.OrdinalIgnoreCase)) foreach (string h in t.Substring(5).Split(' ')) if (h.Length > 0 && h.IndexOf('*') < 0 && h.IndexOf('?') < 0 && !r.Contains(h)) r.Add(h); }
            }
            catch { }
            return r;
        }

        // ---- a PowerShell-integráció: prompt (felhasználó@gép mappa (git ág) %), kimeneti kód (OSC 133;D), munkamappa (OSC 7), segédparancsok
        const string PsScript = @"# lumen-profile v@@V@@
# Lumen Terminal PowerShell-integráció. Ezt a fájlt a Terminal frissítheti; a saját kiegészítéseidet a lumen-user.ps1 fájlba írd.
$global:__lumen_first = $true; $global:__lumen_dir = $PSScriptRoot
function global:prompt {
    $ok = $?; $code = 0; if (-not $ok) { $code = 1; if ($global:LASTEXITCODE -is [int] -and $global:LASTEXITCODE -ne 0) { $code = $global:LASTEXITCODE } }
    $e = [char]27; $b = [char]7; $loc = $executionContext.SessionState.Path.CurrentLocation; $p = $loc.Path; $h = $env:USERPROFILE
    if ($p -eq $h) { $d = '~' } else { $d = Split-Path -Leaf $p; if (-not $d) { $d = $p } }
    $git = ''
    try { $dir = $p; for ($i = 0; $i -lt 12 -and $dir; $i++) { $gh = Join-Path $dir '.git\HEAD'; if (Test-Path -LiteralPath $gh) { $hd = Get-Content -LiteralPath $gh -TotalCount 1; if ($hd -match 'ref: refs/heads/(.+)') { $git = ' (' + $Matches[1] + ')' } elseif ($hd.Length -ge 7) { $git = ' (' + $hd.Substring(0, 7) + ')' }; break }; $dir = Split-Path -Parent $dir } } catch { }
    $seq = ''
    if (-not $global:__lumen_first) { $seq += ""$e]133;D;$code$b"" }
    $global:__lumen_first = $false
    if ($loc.Provider.Name -eq 'FileSystem') { $seq += ""$e]7;file://localhost/"" + ((($p -replace '\\', '/') -split '/' | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/') + $b }
    [Console]::Out.Write($seq)
    return ($env:USERNAME.ToLower() + '@' + $env:COMPUTERNAME.ToLower() + ' ' + $d + $git + ' % ')
}
try {
    Set-PSReadLineOption -BellStyle None -HistoryNoDuplicates -HistorySearchCursorMovesToEnd -MaximumHistoryCount 20000
    Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward; Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
    Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete; Set-PSReadLineKeyHandler -Chord 'Ctrl+d' -Function DeleteCharOrExit
    Set-PSReadLineKeyHandler -Chord 'Ctrl+w' -Function BackwardKillWord; Set-PSReadLineKeyHandler -Chord 'Alt+d' -Function KillWord
} catch { }
# ---- navigáció
function global:.. { Set-Location .. }
function global:... { Set-Location ..\.. }
function global:.... { Set-Location ..\..\.. }
function global:~ { Set-Location ~ }
function global:cdd { if ($global:__lumen_prev) { $t = $PWD.Path; Set-Location $global:__lumen_prev; $global:__lumen_prev = $t } }
function global:mkcd { param([string]$n) New-Item -ItemType Directory -Force -Path $n | Out-Null; Set-Location $n }
function global:up { param([int]$n = 1) for ($i = 0; $i -lt $n; $i++) { Set-Location .. } }
function global:desk { Set-Location ([Environment]::GetFolderPath('Desktop')) }
function global:docs { Set-Location ([Environment]::GetFolderPath('MyDocuments')) }
function global:dl { Set-Location (Join-Path $env:USERPROFILE 'Downloads') }
# ---- fájlok
function global:ll { Get-ChildItem -Force @args | Format-Table Mode, @{n='Méret';e={if ($_.PSIsContainer) { '<DIR>' } else { '{0:N0}' -f $_.Length }};a='right'}, LastWriteTime, Name -AutoSize }
function global:la { Get-ChildItem -Force @args }
function global:l { Get-ChildItem @args }
function global:touch { param([string[]]$f) foreach ($x in $f) { if (Test-Path $x) { (Get-Item $x).LastWriteTime = Get-Date } else { New-Item -ItemType File -Path $x | Out-Null } } }
function global:which { param([string]$n) Get-Command $n -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { if ($_.Source) { $_.Source } else { $_.Definition } } }
function global:open { param([string]$p = '.') Invoke-Item -LiteralPath $p }
function global:edit { param([string]$p = '.') $c = Get-Command code, antigravity, notepad -ErrorAction SilentlyContinue | Select-Object -First 1; if ($c) { & $c.Source $p } }
function global:head { param([string]$f, [int]$n = 10) Get-Content $f -TotalCount $n }
function global:tail { param([string]$f, [int]$n = 10, [switch]$follow) Get-Content $f -Tail $n -Wait:$follow }
function global:wc { param([string]$f) $c = Get-Content $f; '{0} sor, {1} szó, {2} bájt' -f $c.Count, ($c -join ' ' -split '\s+').Count, (Get-Item $f).Length }
function global:grep { param([string]$pat, [string]$path = '.', [switch]$i) Get-ChildItem -Recurse -File -Path $path -ErrorAction SilentlyContinue | Select-String -Pattern $pat -CaseSensitive:(-not $i) }
function global:ff { param([string]$n) Get-ChildItem -Recurse -ErrorAction SilentlyContinue -Filter ""*$n*"" | ForEach-Object { $_.FullName } }
function global:size { param([string]$p = '.') Get-ChildItem -Force $p | ForEach-Object { $s = if ($_.PSIsContainer) { (Get-ChildItem $_.FullName -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum } else { $_.Length }; [pscustomobject]@{ Név = $_.Name; MB = [math]::Round($s / 1MB, 1) } } | Sort-Object MB -Descending }
function global:rmrf { param([string]$p) Remove-Item -Recurse -Force -LiteralPath $p }
function global:cpv { param([string]$a, [string]$b) Copy-Item -Recurse -Force $a $b -Verbose }
function global:trash { param([string]$p) Add-Type -AssemblyName Microsoft.VisualBasic; $f = Resolve-Path $p; if ((Get-Item $f).PSIsContainer) { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($f, 'OnlyErrorDialogs', 'SendToRecycleBin') } else { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($f, 'OnlyErrorDialogs', 'SendToRecycleBin') } }
# ---- rendszer, hálózat
function global:ports { Get-NetTCPConnection -State Listen | Sort-Object LocalPort | ForEach-Object { [pscustomobject]@{ Port = $_.LocalPort; PID = $_.OwningProcess; Program = (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } } }
function global:killport { param([int]$p) Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue; ""leállítva: PID $($_.OwningProcess)"" } }
function global:myip { try { (Invoke-RestMethod https://api.ipify.org) } catch { 'nincs hálózat' }; Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '169.*' -and $_.IPAddress -ne '127.0.0.1' } | ForEach-Object { 'helyi: ' + $_.IPAddress } }
function global:sysinfo { $o = Get-CimInstance Win32_OperatingSystem; $c = Get-CimInstance Win32_Processor | Select-Object -First 1; '{0}  |  {1}  |  RAM: {2:N1}/{3:N1} GB szabad' -f $o.Caption, $c.Name.Trim(), ($o.FreePhysicalMemory / 1MB), ($o.TotalVisibleMemorySize / 1MB) }
function global:path { $env:PATH -split ';' | Where-Object { $_ } }
function global:uptime { $b = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime; (Get-Date) - $b }
function global:admin { Start-Process powershell -Verb RunAs -ArgumentList '-NoExit', ""-Command Set-Location '$PWD'"" }
function global:reload { . (Join-Path $global:__lumen_dir 'lumen-profile.ps1') }
# ---- git
function global:gs { git status -sb @args }
function global:ga { git add @args }
function global:gaa { git add -A }
function global:gcm { git commit -m @args }
function global:gps { git push @args }
function global:gpl { git pull @args }
function global:glg { git log --oneline --graph --decorate -n 20 @args }
function global:gdf { git diff @args }
function global:gco { git checkout @args }
function global:gbr { git branch @args }
function global:gcb { git checkout -b @args }
function global:gsh { git stash @args }
function global:gundo { git reset --soft HEAD~1 }
# ---- fejlesztői eszközök
function global:serve { param([int]$p = 8000) if (Get-Command python -ErrorAction SilentlyContinue) { python -m http.server $p } else { npx serve -l $p } }
function global:json { param([Parameter(ValueFromPipeline = $true)]$i) process { $i | ConvertFrom-Json | ConvertTo-Json -Depth 20 } }
function global:b64 { param([string]$s) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }
function global:ub64 { param([string]$s) [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s)) }
function global:sha256 { param([string]$f) (Get-FileHash -Algorithm SHA256 $f).Hash.ToLower() }
function global:uuid { [guid]::NewGuid().ToString() }
function global:now { Get-Date -Format 'yyyy-MM-dd HH:mm:ss' }
function global:epoch { [int][double]::Parse((Get-Date -UFormat %s)) }
function global:urlenc { param([string]$s) [Uri]::EscapeDataString($s) }
function global:pn { pnpm @args }
function global:preview { param([string]$t = '') if ($t -match '^\d+$') { $t = ""http://localhost:$t"" } elseif ($t -and $t -notmatch '^[a-z]+://') { $t = ""http://$t"" }; [Console]::Out.Write(""$([char]27)]777;preview;$t$([char]7)"") }
function global:dev { if (Test-Path package.json) { if (Get-Command pnpm -ErrorAction SilentlyContinue) { pnpm run dev } else { npm run dev } } else { 'nincs package.json' } }
# ---- Lumen súgó
function global:lumen { param([string]$c = 'help')
    switch ($c) {
        'help' { @'
Lumen Terminal – gyorsbillentyűk: Ctrl+Shift+P parancspaletta | Ctrl+T új lap | Ctrl+Shift+D / E osztás | Ctrl+F keresés | Ctrl+R előzmények
  Ctrl+Fel/Le előző/következő parancs | Ctrl+Shift+O utolsó kimenet másolása | Ctrl+kattintás link/útvonal megnyitása
Segédparancsok: ll la .. ... mkcd up desk docs dl touch which open edit head tail wc grep ff size trash ports killport myip sysinfo path uptime admin
  gs ga gaa gcm gps gpl glg gdf gco gbr gcb gsh gundo | serve json b64 ub64 sha256 uuid now epoch urlenc dev
'@ }
        default { 'lumen help' }
    }
}
if (Test-Path (Join-Path $PSScriptRoot 'lumen-user.ps1')) { . (Join-Path $PSScriptRoot 'lumen-user.ps1') }
Clear-Host; Write-Host ('Last login: ' + (Get-Date).ToString('ddd MMM d HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) + ' on ttys000   (lumen help)')
";
    }
}
