// Lumen – Fejlesztői Hub: kattintásos eszköz-telepítő, projekt-felfedező, port-figyelő, környezet-szerkesztő, gyorseszközök,
// takarító, Docker és projekt-sablonok. A hosszabb műveletek a Terminál fül új lapján futnak, így minden látható és megszakítható.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GlassTerm;
using Forms = System.Windows.Forms;

namespace GlassControlWpf
{
    public partial class DevHub : Grid
    {
        // a hívó (MainWindow) köti be: cím, munkamappa, parancs → új terminál-lap a Terminál fülön
        public Action<string, string, string> RunInTerm; public Action<string> PreviewInTerm;
        string dataDir; Grid pages = new Grid(), overlay = new Grid(); WrapPanel nav = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        List<Border> navBtns = new List<Border>(); Func<FrameworkElement>[] builders; FrameworkElement[] built; int cur = -1; Border toastBox, modal, scrim;
        const int PgTools = 0, PgWinget = 1, PgProj = 2, PgNew = 3, PgPorts = 4, PgEnv = 5, PgQuick = 6, PgClean = 7, PgDocker = 8;

        // ---------------------------------------------------------------- színek és építőelemek
        static Brush Br(byte r, byte g, byte b, byte a) { SolidColorBrush s = new SolidColorBrush(Color.FromArgb(a, r, g, b)); s.Freeze(); return s; }
        static readonly Brush Muted = Br(160, 160, 168, 255), Accent = Br(10, 132, 255, 255), Green = Br(48, 209, 88, 255), Red = Br(255, 69, 58, 255), Orange = Br(255, 159, 10, 255), CardBg = Br(255, 255, 255, 15), CardBorder = Br(255, 255, 255, 26), Faint = Br(255, 255, 255, 10);
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        static TextBlock Txt(string s, double size, Brush fg, bool bold) { return new TextBlock { Text = s, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Head(string s) { return new TextBlock { Text = s.ToUpperInvariant(), FontSize = 11, Foreground = Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 14, 0, 6) }; }

        // kind: 0 semleges, 1 kék (fő), 2 piros (veszélyes), 3 zöld
        static Border Chip(string text, Action click, int kind)
        {
            Brush bg = kind == 1 ? Accent : kind == 2 ? Br(255, 69, 58, 56) : kind == 3 ? Br(48, 209, 88, 56) : Br(255, 255, 255, 28);
            Border b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6), Background = bg, Cursor = click != null ? Cursors.Hand : Cursors.Arrow, Opacity = click != null ? 1 : 0.4, Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = kind == 2 ? Br(255, 130, 120, 255) : kind == 3 ? Br(110, 230, 140, 255) : Brushes.White } };
            if (click != null) { b.MouseEnter += delegate { b.Opacity = 0.8; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; try { click(); } catch (Exception ex) { try { MessageBox.Show(Loc.T("Hiba: ") + ex.Message, "Dev Hub"); } catch { } } }; }
            return b;
        }
        static Border Chip(string text, Action click) { return Chip(text, click, 0); }
        static void SetChipText(Border chip, string t) { ((TextBlock)chip.Child).Text = t; }

        static Border CardBox(UIElement child) { return new Border { Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = child }; }

        static TextBox Field(string initial, double width)
        {
            TextBox t = new TextBox { Text = initial ?? "", Background = Br(0, 0, 0, 90), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = CardBorder, BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = Accent };
            if (width > 0) t.Width = width; GlassMenu.ForTextBox(t); return t;
        }
        // szövegmező halvány súgóval
        static FrameworkElement Hinted(TextBox tb, string hint)
        {
            Grid g = new Grid(); TextBlock h = new TextBlock { Text = hint, Foreground = Br(255, 255, 255, 90), FontSize = 13, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            g.Children.Add(tb); g.Children.Add(h); Action upd = delegate { h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }; tb.TextChanged += delegate { upd(); }; upd(); if (!double.IsNaN(tb.Width)) g.Width = tb.Width; return g;
        }
        static ScrollViewer Scroll(UIElement content)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content, Focusable = false }; return sv;
        }

        // ---------------------------------------------------------------- háttérmunka, visszajelzések
        void Bg<T>(Func<T> work, Action<T> done)
        {
            ThreadPool.QueueUserWorkItem(delegate { T r = default(T); try { r = work(); } catch { } try { Dispatcher.BeginInvoke(new Action(delegate { try { done(r); } catch { } })); } catch { } });
        }
        public void Toast(string text)
        {
            if (toastBox != null) overlay.Children.Remove(toastBox);
            Border me = new Border { CornerRadius = new CornerRadius(14), Background = Br(36, 36, 40, 238), BorderBrush = Br(255, 255, 255, 60), BorderThickness = new Thickness(1), Padding = new Thickness(16, 8, 16, 9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16), IsHitTestVisible = false, Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }, Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45, Direction = 270 } };
            toastBox = me; overlay.Children.Add(me); DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) }; t.Tick += delegate { t.Stop(); overlay.Children.Remove(me); }; t.Start();
        }
        void Copy(string text, string msg) { try { Clipboard.SetText(text); Toast(msg ?? Loc.T("Másolva")); } catch { Toast(Loc.T("A vágólap épp foglalt, próbáld újra")); } }
        void Term(string title, string cwd, string cmd) { if (RunInTerm != null) RunInTerm(title, cwd, cmd); else Toast(Loc.T("A terminál nem érhető el")); }

        void ShowModal(Border panel)
        {
            HideModal(); scrim = new Border { Background = Br(0, 0, 0, 100) }; scrim.MouseLeftButtonDown += delegate { HideModal(); }; overlay.Children.Add(scrim);
            panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Center; modal = panel; overlay.Children.Add(panel);
        }
        void HideModal() { if (scrim != null) overlay.Children.Remove(scrim); if (modal != null) overlay.Children.Remove(modal); scrim = null; modal = null; }
        Border ModalPanel(UIElement child, double width) { return new Border { Width = width, CornerRadius = new CornerRadius(16), Background = Br(30, 30, 34, 246), BorderBrush = Br(255, 255, 255, 70), BorderThickness = new Thickness(1), Padding = new Thickness(18, 16, 18, 14), Child = child, Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.55, Direction = 270 } }; }

        void Ask(string title, string initial, string ok, Action<string> done)
        {
            StackPanel sp = new StackPanel(); sp.Children.Add(Txt(title, 13, Brushes.White, true)); TextBox tb = Field(initial, 0); tb.Margin = new Thickness(0, 10, 0, 12); tb.FontSize = 15; sp.Children.Add(tb);
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Action go = delegate { string v = tb.Text; HideModal(); done(v); };
            row.Children.Add(Chip(Loc.T("Mégse"), delegate { HideModal(); })); row.Children.Add(Chip(ok ?? "OK", go, 1)); sp.Children.Add(row);
            tb.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Enter) { go(); e.Handled = true; } else if (e.Key == Key.Escape) { HideModal(); e.Handled = true; } };
            ShowModal(ModalPanel(sp, 440)); Dispatcher.BeginInvoke(new Action(delegate { tb.Focus(); tb.SelectAll(); Keyboard.Focus(tb); }), DispatcherPriority.Input);
        }
        void Confirm(string title, string text, string yes, Action act)
        {
            StackPanel sp = new StackPanel(); sp.Children.Add(Txt(title, 15, Brushes.White, true));
            sp.Children.Add(new TextBlock { Text = text, Foreground = Br(214, 214, 220, 255), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14) });
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; row.Children.Add(Chip(Loc.T("Mégse"), delegate { HideModal(); })); row.Children.Add(Chip(yes, delegate { HideModal(); act(); }, 2)); sp.Children.Add(row);
            ShowModal(ModalPanel(sp, 480));
        }

        // ---------------------------------------------------------------- külső folyamatok
        static string cachedPath; static DateTime cachedPathAt = DateTime.MinValue;
        // a regisztrációs adatbázisból olvassuk a PATH-t, így a frissen telepített eszközök újraindítás nélkül is látszanak
        internal static string FreshPath()
        {
            string cp0 = cachedPath; if (cp0 != null && (DateTime.Now - cachedPathAt).TotalSeconds < 4) return cp0;
            string m = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "", u = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            string fresh = Environment.ExpandEnvironmentVariables(m + ";" + u); cachedPath = fresh; cachedPathAt = DateTime.Now; return fresh;
        }
        internal static string FindExe(params string[] names)
        {
            string[] exts = { ".exe", ".cmd", ".bat", ".com" };
            foreach (string d0 in FreshPath().Split(';'))
            {
                string d = d0.Trim().Trim('"'); if (d.Length == 0) continue;
                foreach (string n in names) { string nn = n.Trim(); if (nn.Length == 0) continue; if (Path.HasExtension(nn)) { try { string p0 = Path.Combine(d, nn); if (File.Exists(p0)) return p0; } catch { } continue; } foreach (string x in exts) { try { string p = Path.Combine(d, nn + x); if (File.Exists(p)) return p; } catch { } } }
            }
            return null;
        }
        internal static string Run(string file, string args, int timeoutMs, string cwd, out int exit)
        {
            exit = -1;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd)) psi.WorkingDirectory = cwd; psi.EnvironmentVariables["Path"] = FreshPath();
                using (Process p = Process.Start(psi))
                {
                    try { p.StandardInput.Close(); } catch { }
                    Task<string> o = p.StandardOutput.ReadToEndAsync(), e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { KillTree(p); return (o.Wait(800) ? o.Result : "") + (e.Wait(800) ? e.Result : ""); }
                    exit = p.ExitCode; return (o.Wait(2000) ? o.Result : "") + (e.Wait(2000) ? e.Result : "");
                }
            }
            catch (Exception ex) { return Loc.T("Hiba: ") + ex.Message; }
        }
        // időtúllépéskor a gyerekfolyamatok is leállnak (különben a cmd /c alatt futó winget/docker tovább él és a csővezetéket nyitva tartja)
        static void KillTree(Process p)
        {
            try { int id = p.Id; Process tk = Process.Start(new ProcessStartInfo("taskkill.exe", "/T /F /PID " + id) { UseShellExecute = false, CreateNoWindow = true }); if (tk != null) { tk.WaitForExit(4000); tk.Dispose(); } } catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }
        internal static string RunCmd(string line, int timeoutMs, string cwd, out int exit) { return Run("cmd.exe", "/d /s /c \"" + line + "\"", timeoutMs, cwd, out exit); }
        internal static string Git(string repo, string args, int timeoutMs, out int exit)
        {
            string git = FindExe("git"); if (git == null) { exit = -1; return Loc.T("git nincs telepítve"); }
            if (repo.Length > 3) repo = repo.TrimEnd((char)92); else if (repo.EndsWith("\\")) repo += "."; return Run(git, "-C \"" + repo + "\" " + args, timeoutMs, null, out exit);
        }
        static string PsQuote(string s) { StringBuilder sb = new StringBuilder("'"); foreach (char c in (s ?? "").Replace("\r", " ").Replace("\n", " ")) { if (c == (char)39 || c == (char)0x2018 || c == (char)0x2019 || c == (char)0x201A || c == (char)0x201B) sb.Append(c); sb.Append(c); } return sb.Append((char)39).ToString(); }
        static string FirstLine(string s) { foreach (string l in (s ?? "").Replace('\r', '\n').Split('\n')) if (l.Trim().Length > 0) return l.Trim(); return ""; }
        static string Trunc(string s, int n) { return s != null && s.Length > n ? s.Substring(0, n - 1) + "…" : s; }
        static string FmtSize(long b) { if (b < 1024) return b + " B"; double v = b / 1024.0; string[] u = { "KB", "MB", "GB", "TB" }; int i = 0; while (v >= 1024 && i < 3) { v /= 1024; i++; } return v.ToString(v >= 100 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + u[i]; }
        static void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { } }
        static void OpenFolder(string p) { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = true }); } catch { } }
        // szerkesztő indítása (VS Code / Antigravity): először a PATH-on, aztán a megszokott telepítési helyen
        static bool OpenInEditor(string which, string path)
        {
            string exe = which == "code" ? FindExe("code") : FindExe("antigravity");
            if (exe == null) { string l = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); string c = which == "code" ? Path.Combine(l, @"Programs\Microsoft VS Code\Code.exe") : Path.Combine(l, @"Programs\Antigravity\Antigravity.exe"); if (File.Exists(c)) exe = c; }
            if (exe == null) return false;
            try { string ext = Path.GetExtension(exe).ToLowerInvariant(); if (ext == ".cmd" || ext == ".bat") Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"\"" + exe + "\" \"" + path + "\"\"") { CreateNoWindow = true, UseShellExecute = false }); else Process.Start(new ProcessStartInfo(exe, "\"" + path + "\"") { UseShellExecute = false }); return true; } catch { return false; }
        }

        // ---------------------------------------------------------------- váz: navigáció és lapok
        public DevHub(string appDir, Action<string, string, string> runInTerm)
        {
            RunInTerm = runInTerm; dataDir = Path.Combine(appDir, "devhub"); try { Directory.CreateDirectory(dataDir); } catch { }
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ScrollTheme.Apply(this);
            string[] names = { Loc.T("🧰 Eszközök"), "🔎 Winget", Loc.T("📁 Projektek"), Loc.T("✨ Új projekt"), Loc.T("🔌 Portok"), Loc.T("🌐 Környezet"), Loc.T("⚡ Gyors"), Loc.T("🧹 Takarítás"), "🐳 Docker" };
            builders = new Func<FrameworkElement>[] { BuildTools, BuildWinget, BuildProjects, BuildNewProject, BuildPorts, BuildEnv, BuildQuick, BuildClean, BuildDocker }; built = new FrameworkElement[builders.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i; Border b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = new TextBlock { Text = names[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Muted } };
                b.MouseLeftButtonUp += delegate { Select(idx); }; b.MouseEnter += delegate { if (idx != cur) b.Background = Faint; }; b.MouseLeave += delegate { if (idx != cur) b.Background = Brushes.Transparent; };
                navBtns.Add(b); nav.Children.Add(b);
            }
            Grid.SetRow(nav, 0); Children.Add(nav); Grid.SetRow(pages, 1); Children.Add(pages); Grid.SetRow(overlay, 0); Grid.SetRowSpan(overlay, 2); Children.Add(overlay);
            PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Escape && modal != null) { HideModal(); e.Handled = true; } };
            IsVisibleChanged += delegate { PortsVisibility(); };
            Select(Math.Max(0, Math.Min(builders.Length - 1, LoadInt("page", 0))));
        }

        public void Select(int i)
        {
            if (i < 0 || i >= builders.Length) return;
            if (built[i] == null) { try { built[i] = builders[i](); } catch (Exception ex) { built[i] = new TextBlock { Text = Loc.T("A lap nem tölthető be: ") + ex.Message, Foreground = Red, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap }; } pages.Children.Add(built[i]); }
            for (int k = 0; k < built.Length; k++) { if (built[k] != null) built[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed; }
            for (int k = 0; k < navBtns.Count; k++) { bool sel = k == i; navBtns[k].Background = sel ? Br(255, 255, 255, 40) : Brushes.Transparent; ((TextBlock)navBtns[k].Child).Foreground = sel ? Brushes.White : Muted; }
            cur = i; SaveInt("page", i); PortsVisibility();
        }

        // ---------------------------------------------------------------- beállítások (devhub\settings.ini)
        string SettingsFile { get { return Path.Combine(dataDir, "settings.ini"); } }
        string LoadStr(string key, string def)
        {
            try { if (File.Exists(SettingsFile)) foreach (string l in File.ReadAllLines(SettingsFile)) { int i = l.IndexOf('='); if (i > 0 && l.Substring(0, i) == key) return l.Substring(i + 1); } } catch { }
            return def;
        }
        int LoadInt(string key, int def) { int v; return int.TryParse(LoadStr(key, ""), out v) ? v : def; }
        void SaveStr(string key, string val)
        {
            try
            {
                List<string> l = new List<string>(); bool done = false; if (File.Exists(SettingsFile)) foreach (string x in File.ReadAllLines(SettingsFile)) { int i = x.IndexOf('='); if (i > 0 && x.Substring(0, i) == key) { if (!done) l.Add(key + "=" + val); done = true; } else l.Add(x); }
                if (!done) l.Add(key + "=" + val); File.WriteAllLines(SettingsFile, l.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
        void SaveInt(string key, int v) { SaveStr(key, v.ToString()); }

        // ================================================================== 1. ESZKÖZÖK (winget alapú telepítő / frissítő / eltávolító)
        class ToolDef { public string Name, Group, Icon, Id, Ver, Exe; public string[] Extra; }
        class ToolRow { public ToolDef Def; public Border Row; public CheckBox Chk; public TextBlock Status, Upd; public Border BtnMain, BtnDel; public bool Installed, Known; public string Version = "", Available = ""; public Timer Watch; }
        static ToolDef T(string name, string group, string icon, string id, string ver, string exe, params string[] extra) { return new ToolDef { Name = name, Group = group, Icon = icon, Id = id, Ver = ver, Exe = exe, Extra = extra }; }
        static readonly ToolDef[] Catalog = {
            T("Python", Loc.T("Nyelvek és futtatók"), "🐍", "Python.Python.3.12", "--version", "python"), T("Node.js", Loc.T("Nyelvek és futtatók"), "⬢", "OpenJS.NodeJS", "--version", "node"),
            T("Java (JDK 21)", Loc.T("Nyelvek és futtatók"), "☕", "EclipseAdoptium.Temurin.21.JDK", "-version", "java"), T("Go", Loc.T("Nyelvek és futtatók"), "🐹", "GoLang.Go", "version", "go"),
            T("Rust", Loc.T("Nyelvek és futtatók"), "🦀", "Rustlang.Rustup", "--version", "rustc,cargo"), T(".NET SDK 8", Loc.T("Nyelvek és futtatók"), "🟣", "Microsoft.DotNet.SDK.8", "--version", "dotnet"),
            T("Deno", Loc.T("Nyelvek és futtatók"), "🦕", "DenoLand.Deno", "--version", "deno"), T("Bun", Loc.T("Nyelvek és futtatók"), "🥟", "Oven-sh.Bun", "--version", "bun"),
            T("pnpm", Loc.T("Csomagkezelők"), "📦", "pnpm.pnpm", "--version", "pnpm"), T("Yarn", Loc.T("Csomagkezelők"), "🧶", "Yarn.Yarn", "--version", "yarn"), T("uv (Python)", Loc.T("Csomagkezelők"), "⚡", "astral-sh.uv", "--version", "uv"),
            T("Git", Loc.T("Fejlesztői eszközök"), "🌿", "Git.Git", "--version", "git"), T("GitHub CLI", Loc.T("Fejlesztői eszközök"), "🐙", "GitHub.cli", "--version", "gh"), T("VS Code", Loc.T("Fejlesztői eszközök"), "💠", "Microsoft.VisualStudioCode", "--version", "code", @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"),
            T("Docker Desktop", Loc.T("Fejlesztői eszközök"), "🐳", "Docker.DockerDesktop", "--version", "docker"), T("PowerShell 7", Loc.T("Fejlesztői eszközök"), "🔷", "Microsoft.PowerShell", "--version", "pwsh"), T("Windows Terminal", Loc.T("Fejlesztői eszközök"), "⬛", "Microsoft.WindowsTerminal", null, "wt"),
            T("CMake", Loc.T("Fejlesztői eszközök"), "🛠", "Kitware.CMake", "--version", "cmake"), T("jq (JSON)", Loc.T("Fejlesztői eszközök"), "🧮", "jqlang.jq", "--version", "jq"), T("FFmpeg", Loc.T("Fejlesztői eszközök"), "🎞", "Gyan.FFmpeg", "-version", "ffmpeg"),
            T("ngrok", Loc.T("Fejlesztői eszközök"), "🌍", "Ngrok.Ngrok", "version", "ngrok"), T("Azure CLI", Loc.T("Fejlesztői eszközök"), "☁", "Microsoft.AzureCLI", null, "az"), T("AWS CLI", Loc.T("Fejlesztői eszközök"), "☁", "Amazon.AWSCLI", "--version", "aws"),
            T("Postman", Loc.T("Alkalmazások"), "🚀", "Postman.Postman", null, "", @"%LOCALAPPDATA%\Postman\Postman.exe"), T("Notepad++", Loc.T("Alkalmazások"), "📝", "Notepad++.Notepad++", null, "", @"%ProgramFiles%\Notepad++\notepad++.exe"),
            T("DB Browser for SQLite", Loc.T("Alkalmazások"), "🗄", "DBBrowserForSQLite.DBBrowserForSQLite", null, "", @"%ProgramFiles%\DB Browser for SQLite\DB Browser for SQLite.exe"), T("7-Zip", Loc.T("Alkalmazások"), "🗜", "7zip.7zip", null, "7z", @"%ProgramFiles%\7-Zip\7z.exe"),
            T("Android Studio", Loc.T("Alkalmazások"), "🤖", "Google.AndroidStudio", null, "", @"%ProgramFiles%\Android\Android Studio\bin\studio64.exe"), T("GitHub Desktop", Loc.T("Alkalmazások"), "🖥", "GitHub.GitHubDesktop", null, "", @"%LOCALAPPDATA%\GitHubDesktop\GitHubDesktop.exe")
        };
        static readonly Dictionary<string, string> WebUrls = new Dictionary<string, string> {
            { "Python.Python.3.12", "https://www.python.org/downloads/windows/" }, { "OpenJS.NodeJS", "https://nodejs.org/en/download" }, { "EclipseAdoptium.Temurin.21.JDK", "https://adoptium.net/temurin/releases/?version=21" }, { "GoLang.Go", "https://go.dev/dl/" }, { "Rustlang.Rustup", "https://rustup.rs/" },
            { "Microsoft.DotNet.SDK.8", "https://dotnet.microsoft.com/download" }, { "Git.Git", "https://git-scm.com/download/win" }, { "GitHub.cli", "https://cli.github.com/" }, { "Microsoft.VisualStudioCode", "https://code.visualstudio.com/Download" }, { "Docker.DockerDesktop", "https://www.docker.com/products/docker-desktop/" },
            { "Microsoft.PowerShell", "https://github.com/PowerShell/PowerShell/releases/latest" }, { "Postman.Postman", "https://www.postman.com/downloads/" }, { "Notepad++.Notepad++", "https://notepad-plus-plus.org/downloads/" }, { "7zip.7zip", "https://www.7-zip.org/download.html" }, { "Google.AndroidStudio", "https://developer.android.com/studio" },
            { "Oven-sh.Bun", "https://bun.sh/" }, { "DenoLand.Deno", "https://deno.com/" }, { "astral-sh.uv", "https://docs.astral.sh/uv/getting-started/installation/" }, { "Kitware.CMake", "https://cmake.org/download/" }, { "GitHub.GitHubDesktop", "https://desktop.github.com/" }, { "Ngrok.Ngrok", "https://ngrok.com/download" }, { "Gyan.FFmpeg", "https://www.gyan.dev/ffmpeg/builds/" }
        };
        List<ToolRow> toolRows = new List<ToolRow>(); TextBlock toolSummary; bool toolsChecking, upgradesLoaded; TextBox toolFilter;

        static bool Detect(ToolDef d, out string ver)
        {
            ver = ""; string exe = d.Exe.Length > 0 ? FindExe(d.Exe.Split(',')) : null;
            if (exe == null && d.Extra != null) foreach (string x in d.Extra) { string p = Environment.ExpandEnvironmentVariables(x); if (File.Exists(p)) { exe = p; break; } }
            if (exe == null) return false; if (d.Ver == null || exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0 && d.Exe == "wt") { ver = Loc.T("telepítve"); return true; }
            int ex; string ext = Path.GetExtension(exe).ToLowerInvariant(), o;
            if (!d.Exe.Contains(Path.GetFileNameWithoutExtension(exe)) && d.Exe.Length > 0) { ver = Loc.T("telepítve"); return true; }          // pl. csak a bővített útvonalon találtuk meg
            o = (ext == ".cmd" || ext == ".bat") ? RunCmd("\"" + exe + "\" " + d.Ver, 9000, null, out ex) : Run(exe, d.Ver, 9000, null, out ex);
            string first = FirstLine(o); string low = o.ToLowerInvariant();
            if (exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0 && (ex != 0 || low.Contains("microsoft store") || low.Contains("was not found"))) return false;      // a Store-os "python" helyettesítő nem valódi telepítés
            ver = first.Length > 0 && ex == 0 ? Trunc(first, 46) : Loc.T("telepítve"); return true;
        }

        FrameworkElement BuildTools()
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; toolFilter = Field("", 200); FrameworkElement fh = Hinted(toolFilter, Loc.T("Szűrés…")); fh.Margin = new Thickness(0, 0, 8, 6); toolFilter.TextChanged += delegate { FilterTools(); };
            top.Children.Add(fh); top.Children.Add(Chip(Loc.T("↻ Újraellenőrzés"), delegate { CheckAllTools(); }, 0)); top.Children.Add(Chip(Loc.T("⬆ Frissítések keresése"), delegate { LoadUpgrades(); }, 0));
            top.Children.Add(Chip(Loc.T("⬇ Kijelöltek telepítése"), delegate { InstallSelected(); }, 1)); top.Children.Add(Chip(Loc.T("⟳ Minden frissítése"), delegate { UpgradeAll(); }, 0));
            toolSummary = Txt("", 12, Muted, false); toolSummary.Margin = new Thickness(6, 0, 0, 6); top.Children.Add(toolSummary); Grid.SetRow(top, 0); g.Children.Add(top);
            StackPanel list = new StackPanel(); string grp = null;
            foreach (ToolDef d in Catalog)
            {
                if (d.Group != grp) { grp = d.Group; list.Children.Add(Head(grp)); }
                ToolRow r = new ToolRow { Def = d }; toolRows.Add(r); list.Children.Add(MakeToolRow(r));
            }
            ScrollViewer sv = Scroll(list); Grid.SetRow(sv, 1); g.Children.Add(sv);
            CheckAllTools(); return g;
        }

        FrameworkElement MakeToolRow(ToolRow r)
        {
            ToolDef d = r.Def; Grid g = new Grid { Margin = new Thickness(0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r.Chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc.T("Kijelölés csoportos telepítéshez") }; Grid.SetColumn(r.Chk, 0);
            TextBlock ic = Txt(d.Icon, 16, Brushes.White, false); Grid.SetColumn(ic, 1); TextBlock nm = Txt(d.Name, 13, Brushes.White, true); Grid.SetColumn(nm, 2);
            r.Status = Txt(Loc.T("… ellenőrzés"), 12, Muted, false); r.Status.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(r.Status, 3);
            r.Upd = Txt("", 11.5, Orange, true); r.Upd.Margin = new Thickness(8, 0, 10, 0); Grid.SetColumn(r.Upd, 4);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(btns, 5);
            r.BtnMain = Chip(Loc.T("Telepítés"), delegate { MainAction(r); }, 1); r.BtnMain.Margin = new Thickness(0, 0, 4, 0); r.BtnDel = Chip(Loc.T("Eltávolít"), delegate { Uninstall(r); }, 2); r.BtnDel.Margin = new Thickness(0); r.BtnDel.Visibility = Visibility.Collapsed;
            btns.Children.Add(r.BtnMain); btns.Children.Add(r.BtnDel);
            g.Children.Add(r.Chk); g.Children.Add(ic); g.Children.Add(nm); g.Children.Add(r.Status); g.Children.Add(r.Upd); g.Children.Add(btns);
            Border row = new Border { Padding = new Thickness(4, 6, 4, 6), CornerRadius = new CornerRadius(9), Child = g, Background = Brushes.Transparent }; row.MouseEnter += delegate { row.Background = Faint; }; row.MouseLeave += delegate { row.Background = Brushes.Transparent; }; r.Row = row;
            GlassMenu.Attach(row, delegate
            {
                List<MenuEntry> m = new List<MenuEntry>(); m.Add(GlassMenu.Item(r.Installed ? Loc.T("Frissítés (winget upgrade)") : Loc.T("Telepítés (winget install)"), delegate { MainAction(r); }));
                if (r.Installed) m.Add(GlassMenu.Item(Loc.T("Eltávolítás…"), delegate { Uninstall(r); }));
                m.Add(GlassMenu.Sep()); m.Add(GlassMenu.Item(Loc.T("Csomag-azonosító másolása"), delegate { Copy(d.Id, Loc.T("Másolva: ") + d.Id); })); m.Add(GlassMenu.Item(Loc.T("Telepítő parancs másolása"), delegate { Copy("winget install --id " + d.Id + " -e", Loc.T("Parancs másolva")); }));
                string web; if (WebUrls.TryGetValue(d.Id, out web)) m.Add(GlassMenu.Item(Loc.T("Letöltés a gyártó oldaláról (ha a winget nem megy)"), delegate { OpenUrl(web); }));
                m.Add(GlassMenu.Item(Loc.T("Csomag adatai (winget show)"), delegate { Term("Winget: " + d.Name, null, "winget show --id " + d.Id + " -e"); })); m.Add(GlassMenu.Item(Loc.T("Újraellenőrzés"), delegate { CheckOne(r); }));
                return m.ToArray();
            });
            return row;
        }

        void Uninstall(ToolRow r) { Confirm(Loc.T("Eltávolítás"), r.Def.Name + Loc.T(" eltávolítása a gépről (winget uninstall)?"), Loc.T("Eltávolít"), delegate { WingetRun("uninstall", r.Def.Id, Loc.T("Eltávolítás: ") + r.Def.Name); Watch(r); }); }
        void MainAction(ToolRow r)
        {
            string verb = r.Installed ? "upgrade" : "install"; WingetRun(verb, r.Def.Id, (r.Installed ? Loc.T("Frissítés: ") : Loc.T("Telepítés: ")) + r.Def.Name); Watch(r);
        }
        static bool? wingetNet; static DateTime wingetNetAt = DateTime.MinValue;
        // a winget a csomagforrást a CDN-ről tölti; ha az nem érhető el, a parancs percekig lógna – ezt előre jelezzük
        static bool WingetReachable()
        {
            if (wingetNet.HasValue && (DateTime.Now - wingetNetAt).TotalSeconds < (wingetNet.Value ? 90 : 10)) return wingetNet.Value;
            bool ok = false; try { System.Net.HttpWebRequest rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://cdn.winget.microsoft.com/cache/source2.msix"); rq.Method = "HEAD"; rq.Timeout = 4000; using (rq.GetResponse()) ok = true; } catch (System.Net.WebException ex) { ok = ex.Response != null; } catch { ok = false; }
            wingetNet = ok; wingetNetAt = DateTime.Now; return ok;
        }
        void WingetRun(string verb, string id, string title)
        {
            if (FindExe("winget") == null) { Toast(Loc.T("A winget nem található – telepítsd az „App Installer” alkalmazást a Microsoft Store-ból")); return; }
            string flags = verb == "uninstall" ? "" : " --accept-package-agreements --accept-source-agreements"; string cmd = "winget " + verb + " --id " + id + " -e" + flags;
            if (verb == "uninstall") { Term(title, null, cmd); return; }
            Bg(delegate { return WingetReachable(); }, delegate (bool ok)
            {
                if (ok) { Term(title, null, cmd); return; }
                string web; bool has = WebUrls.TryGetValue(id, out web);
                Confirm(Loc.T("A winget forrás nem érhető el"), Loc.T("A cdn.winget.microsoft.com nem válaszol (hálózat / tűzfal / VPN?), ezért a winget most nem tud telepíteni.") + (has ? Loc.T("\n\nMegnyissam a gyártó letöltő oldalát?") : ""), has ? Loc.T("Letöltő oldal") : Loc.T("Értem"), delegate { if (has) OpenUrl(web); });
            });
        }
        void InstallSelected()
        {
            List<string> cmds = new List<string>(); int n = 0;
            List<ToolRow> sel = new List<ToolRow>(); foreach (ToolRow r in toolRows) if (r.Chk != null && r.Chk.IsChecked == true) { cmds.Add("winget " + (r.Installed ? "upgrade" : "install") + " --id " + r.Def.Id + " -e --accept-package-agreements --accept-source-agreements"); n++; sel.Add(r); }
            if (n == 0) { Toast(Loc.T("Jelölj ki legalább egy eszközt a bal oldali pipával")); return; }
            if (FindExe("winget") == null) { Toast(Loc.T("A winget nem található")); return; }
            foreach (ToolRow r in sel) Watch(r);
            Term(Loc.T("Telepítés (") + n + " db)", null, string.Join("; ", cmds.ToArray()));
        }
        void UpgradeAll() { if (FindExe("winget") == null) { Toast(Loc.T("A winget nem található")); return; } Confirm(Loc.T("Minden frissítése"), Loc.T("A winget minden frissíthető programot frissít a gépen (nem csak a fejlesztői eszközöket). Folytatod?"), Loc.T("Frissít"), delegate { Term(Loc.T("Minden frissítése"), null, "winget upgrade --all --accept-package-agreements --accept-source-agreements"); }); }

        void FilterTools()
        {
            string q = toolFilter.Text.Trim().ToLowerInvariant();
            foreach (ToolRow r in toolRows) r.Row.Visibility = q.Length == 0 || r.Def.Name.ToLowerInvariant().Contains(q) || r.Def.Id.ToLowerInvariant().Contains(q) || r.Def.Group.ToLowerInvariant().Contains(q) ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetRow(ToolRow r, bool installed, string ver)
        {
            r.Known = true; r.Installed = installed; r.Version = ver;
            if (installed) { r.Status.Text = "✓ " + ver; r.Status.Foreground = Green; SetChipText(r.BtnMain, Loc.T("Frissítés")); r.BtnMain.Background = Br(255, 255, 255, 28); r.BtnDel.Visibility = Visibility.Visible; }
            else { r.Status.Text = Loc.T("✗ nincs telepítve"); r.Status.Foreground = Muted; SetChipText(r.BtnMain, Loc.T("Telepítés")); r.BtnMain.Background = Accent; r.BtnDel.Visibility = Visibility.Collapsed; }
            string av; if (upgradeMap != null && installed && upgradeMap.TryGetValue(r.Def.Id.ToLowerInvariant(), out av)) { r.Available = av; r.Upd.Text = "⬆ " + av; } else { r.Available = ""; r.Upd.Text = ""; }
        }
        void UpdateToolSummary() { int ins = 0, upd = 0; foreach (ToolRow r in toolRows) { if (r.Installed) ins++; if (r.Available.Length > 0) upd++; } toolSummary.Text = ins + " / " + toolRows.Count + Loc.T(" telepítve") + (upgradesLoaded ? " · " + upd + Loc.T(" frissítés elérhető") : ""); }

        void CheckAllTools()
        {
            if (toolsChecking) return; toolsChecking = true; foreach (ToolRow r in toolRows) { r.Status.Text = Loc.T("… ellenőrzés"); r.Status.Foreground = Muted; }
            List<ToolRow> rows = new List<ToolRow>(toolRows); int next = -1, remaining = 4;
            for (int w = 0; w < 4; w++)      // 4 párhuzamos munkás: a lassú eszközök (az, code) nem tartják fel a többit
                ThreadPool.QueueUserWorkItem(delegate
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next); if (i >= rows.Count) break; ToolRow r = rows[i]; string v = ""; bool ok = false; try { ok = Detect(r.Def, out v); } catch { v = ""; }
                        bool o2 = ok; string v2 = v; try { Dispatcher.BeginInvoke(new Action(delegate { SetRow(r, o2, v2); UpdateToolSummary(); })); } catch { }
                    }
                    if (Interlocked.Decrement(ref remaining) == 0) { try { Dispatcher.BeginInvoke(new Action(delegate { toolsChecking = false; UpdateToolSummary(); })); } catch { } }
                });
        }
        void CheckOne(ToolRow r) { Bg(delegate { string v; bool ok = Detect(r.Def, out v); return new KeyValuePair<bool, string>(ok, v); }, delegate (KeyValuePair<bool, string> res) { SetRow(r, res.Key, res.Value ?? ""); UpdateToolSummary(); }); }

        // telepítés / eltávolítás után figyeljük, mikor változik az állapot (5 mp-enként, legfeljebb 8 percig)
        void Watch(ToolRow r)
        {
            if (r.Watch != null) return; bool before = r.Installed; string verBefore = r.Version; int ticks = 0, busy = 0; string[] exes = r.Def.Exe.Length > 0 ? r.Def.Exe.Split(',') : new string[0];
            r.Watch = new Timer(delegate
            {
                if (Interlocked.Exchange(ref busy, 1) == 1) return;       // az előző ellenőrzés még fut: kihagyjuk (nem pörgetünk párhuzamos folyamatokat)
                try
                {
                    ticks++; bool present = exes.Length > 0 && FindExe(exes) != null;
                    if (!present && r.Def.Extra != null) foreach (string x in r.Def.Extra) { try { if (File.Exists(Environment.ExpandEnvironmentVariables(x))) { present = true; break; } } catch { } }
                    string v = ""; bool ok = present, changed = present != before;
                    if (present && (changed || (before && ticks % 3 == 0))) { try { ok = Detect(r.Def, out v); } catch { ok = present; v = ""; } changed = ok != before || (ok && v != verBefore && verBefore.Length > 0 && v != Loc.T("telepítve")); }
                    bool ch = changed, ok2 = ok; string v2 = v; bool giveUp = ticks > 96;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (ch || giveUp) { Timer t = r.Watch; r.Watch = null; if (t != null) t.Dispose(); }
                        if (ch) { SetRow(r, ok2, v2); UpdateToolSummary(); Toast((ok2 ? "✓ " : "✗ ") + r.Def.Name + (ok2 ? Loc.T(" kész: ") + v2 : Loc.T(" eltávolítva"))); }
                    }));
                }
                catch { }
                finally { Interlocked.Exchange(ref busy, 0); }
            }, null, 5000, 5000);
        }

        Dictionary<string, string> upgradeMap;
        void LoadUpgrades()
        {
            if (FindExe("winget") == null) { Toast(Loc.T("A winget nem található")); return; }
            Toast(Loc.T("Frissítések keresése… (néhány másodperc)"));
            Bg(delegate { int ex; return RunCmd("winget upgrade --accept-source-agreements", 50000, null, out ex); }, delegate (string o)
            {
                if (string.IsNullOrWhiteSpace(o) || o.Length < 20) { Toast(Loc.T("A winget nem válaszolt (a forrás – cdn.winget.microsoft.com – nem érhető el: hálózat / tűzfal / VPN?)")); return; }
                Dictionary<string, string> map = new Dictionary<string, string>(); foreach (string[] c in ParseTable(o ?? "")) if (c.Length >= 4 && c[3].Length > 0) map[c[1].ToLowerInvariant()] = c[3];
                upgradeMap = map; upgradesLoaded = true; foreach (ToolRow r in toolRows) if (r.Known) SetRow(r, r.Installed, r.Version); UpdateToolSummary();
                Toast(map.Count == 0 ? Loc.T("Minden naprakész (vagy a winget nem adott listát)") : map.Count + Loc.T(" program frissíthető"));
            });
        }

        // a winget táblázatos kimenetét oszlopokra bontja (a fejléc pozíciói alapján; nyelvfüggetlen)
        static List<string[]> ParseTable(string raw)
        {
            List<string[]> res = new List<string[]>(); string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); int sep = -1;
            for (int i = 1; i < lines.Length; i++) { string t = lines[i].Trim(); if (t.Length >= 10 && t.Replace("-", "").Length == 0) { sep = i; break; } }
            if (sep < 1) return res; List<int> starts = new List<int>(); foreach (Match m in Regex.Matches(lines[sep - 1], @"\S+(?: \S+)*")) starts.Add(m.Index); if (starts.Count < 2) return res;
            for (int i = sep + 1; i < lines.Length; i++)
            {
                string l = lines[i]; if (l.Trim().Length == 0) { if (res.Count > 0) break; continue; } if (l.Length <= starts[1]) continue; if (Regex.IsMatch(l.Trim(), @"^\d+ \S+")) { if (res.Count > 0) break; continue; } string[] cols = new string[starts.Count];
                for (int c = 0; c < starts.Count; c++) { int a = starts[c], z = c + 1 < starts.Count ? starts[c + 1] : l.Length; cols[c] = a < l.Length ? l.Substring(a, Math.Min(z, l.Length) - a).Trim() : ""; }
                if (Regex.IsMatch(cols[1], @"^[A-Za-z0-9][\w.\-+]*$")) res.Add(cols);
            }
            return res;
        }

        // ================================================================== 2. WINGET-KERESŐ
        StackPanel wingetList; TextBox wingetBox; TextBlock wingetInfo;
        FrameworkElement BuildWinget()
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; wingetBox = Field("", 320); FrameworkElement h = Hinted(wingetBox, Loc.T("Program neve (pl. postman, obsidian, vlc)…")); h.Margin = new Thickness(0, 0, 8, 6);
            Action go = delegate { SearchWinget(); }; wingetBox.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Enter) { go(); e.Handled = true; } };
            top.Children.Add(h); top.Children.Add(Chip(Loc.T("🔎 Keresés"), go, 1)); top.Children.Add(Chip(Loc.T("📋 Telepítettek listája"), delegate { Term(Loc.T("Winget lista"), null, "winget list"); })); wingetInfo = Txt("", 12, Muted, false); wingetInfo.Margin = new Thickness(6, 0, 0, 6); top.Children.Add(wingetInfo);
            Grid.SetRow(top, 0); g.Children.Add(top); wingetList = new StackPanel(); ScrollViewer sv = Scroll(wingetList); Grid.SetRow(sv, 1); g.Children.Add(sv);
            wingetList.Children.Add(new TextBlock { Text = Loc.T("Írj be egy nevet: a winget több tízezer csomagja közül kereshetsz, és egy kattintással telepítheted őket."), Foreground = Muted, FontSize = 12.5, Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            return g;
        }
        void SearchWinget()
        {
            string q = wingetBox.Text.Trim(); if (q.Length == 0) return; if (FindExe("winget") == null) { Toast(Loc.T("A winget nem található")); return; }
            wingetInfo.Text = Loc.T("keresés…"); wingetList.Children.Clear();
            Bg(delegate { int ex; return RunCmd("winget search \"" + q.Replace("\"", "") + "\" --accept-source-agreements", 45000, null, out ex); }, delegate (string o)
            {
                if (string.IsNullOrWhiteSpace(o) || o.Length < 20) { wingetInfo.Text = ""; wingetList.Children.Add(new TextBlock { Text = Loc.T("A winget nem válaszolt – a csomagforrás (cdn.winget.microsoft.com) nem érhető el. Ellenőrizd a hálózatot / tűzfalat / VPN-t, vagy használd az Eszközök fül „Letöltés a gyártó oldaláról” menüpontját (jobb klikk egy soron)."), Foreground = Orange, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 0, 0) }); return; }
                List<string[]> rows = ParseTable(o ?? ""); wingetInfo.Text = rows.Count + Loc.T(" találat"); wingetList.Children.Clear();
                if (rows.Count == 0) { wingetList.Children.Add(new TextBlock { Text = Loc.T("Nincs találat."), Foreground = Muted, Margin = new Thickness(4, 8, 0, 0) }); return; }
                for (int i = 0; i < rows.Count && i < 60; i++)
                {
                    string[] c = rows[i]; string id = c[1], name = c[0], ver = c.Length > 2 ? c[2] : "";
                    Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    StackPanel left = new StackPanel(); left.Children.Add(Txt(name, 13, Brushes.White, true)); left.Children.Add(Txt(id + (ver.Length > 0 ? "  ·  " + ver : ""), 11, Muted, false));
                    StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(b, 1);
                    b.Children.Add(Chip(Loc.T("Telepítés"), delegate { WingetRun("install", id, Loc.T("Telepítés: ") + name); }, 1)); b.Children.Add(Chip(Loc.T("Adatok"), delegate { Term("Winget: " + name, null, "winget show --id " + id + " -e"); })); b.Children.Add(Chip(Loc.T("ID másolása"), delegate { Copy(id, Loc.T("Másolva: ") + id); }));
                    row.Children.Add(left); row.Children.Add(b); Border item = new Border { Padding = new Thickness(8, 6, 8, 0), CornerRadius = new CornerRadius(9), Child = row, Background = Brushes.Transparent }; item.MouseEnter += delegate { item.Background = Faint; }; item.MouseLeave += delegate { item.Background = Brushes.Transparent; }; wingetList.Children.Add(item);
                }
            });
        }
    }
}
