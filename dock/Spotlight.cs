// GlassSpotlight - macOS Spotlight-szerű keresősáv a GlassDock-ban: Alt+Space (vagy Win+S).
// Alkalmazások (a Start-menü listája, a saját hozzáadottakkal), rendszer-műveletek, számolás, fájlok (Windows Search index), web.
// C# 5 kompatibilis.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GlassDock
{
    class SpotResult
    {
        public string Title, Sub, Kind, Path, Glyph, Key; public Action Run; public double Score; public Border Row; public Image Img; public bool IsApp, IsFile;
    }

    class Spotlight : Window
    {
        static Spotlight inst; public static bool IsOpen { get { return inst != null && inst.open; } }
        public static void Toggle(Settings s) { if (inst == null) inst = new Spotlight(s); inst.Flip(); }
        public static void CloseNow() { if (inst != null) inst.CloseIt(); }
        public static void SetQuery(string q) { if (inst != null) inst.box.Text = q; }
        public static void Dump(string file) { if (inst != null) inst.DumpPng(file); }
        void DumpPng(string file)
        {
            root.Measure(new Size(W, Height)); root.Arrange(new Rect(0, 0, W, Height)); root.UpdateLayout(); root.Opacity = 1; st.ScaleX = st.ScaleY = 1;
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)(W * scale), (int)Math.Ceiling(Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); rtb.Render(root);
            PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using (FileStream fs = File.Create(file)) enc.Save(fs);
        }

        const double W = 700, SearchH = 62, RowH = 50, Pad = 8; const int MaxRows = 8;
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        Settings S; double scale = 1; IntPtr hwnd; BitmapSource frost; CanvasView bg = new CanvasView(); Grid root = new Grid(); ScaleTransform st = new ScaleTransform(1, 1);
        TextBox box = new TextBox(); TextBlock hint = new TextBlock(); StackPanel list = new StackPanel(); Border sep = new Border();
        List<SpotResult> res = new List<SpotResult>(), fileRes = new List<SpotResult>(); int sel, bx, by, bw, atg, gen; double op, a0;
        bool open, built, activated, prevDown; int themeVer = -1; DateTime openedAt, closedAt = DateTime.MinValue; Stopwatch aw = new Stopwatch();
        DispatcherTimer anim = new DispatcherTimer(DispatcherPriority.Render), watch = new DispatcherTimer(DispatcherPriority.Input), debounce = new DispatcherTimer(DispatcherPriority.Background);
        Dictionary<string, BitmapSource> icons = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase); Dictionary<string, int> usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static FontFamily Inter { get { return Theme.Font(); } }
        static readonly string[] SkipPaths = { "\\AppData\\", "\\.vscode\\", "\\node_modules\\", "\\.git\\", "\\.antigravity\\", "\\Windows\\", "\\Program Files", "\\ProgramData\\", "\\$Recycle.Bin\\", "\\.cache\\" };

        Spotlight(Settings s)
        {
            S = s; using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; AllowsTransparency = false; Background = Brushes.Transparent;
            Content = root; bg.Draw = PaintBg; RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.HighQuality); Width = W; Height = SearchH; Left = -30000; Top = -30000;
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle; N.SetWindowLong(hwnd, -20, N.GetWindowLong(hwnd, -20) | 0x80);
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1; N.DwmExtendFrameIntoClientArea(hwnd, ref mg); int pol = 1; N.DwmSetWindowAttribute(hwnd, 2, ref pol, 4); N.PlainWindow(hwnd);
            };
            Deactivated += delegate { if (open && activated && (DateTime.Now - openedAt).TotalMilliseconds > 400) CloseIt(); };
            Activated += delegate { activated = true; };
            PreviewKeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { CloseIt(); e.Handled = true; }
                else if (e.Key == Key.Down || (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)) { Select(sel + 1); e.Handled = true; }
                else if (e.Key == Key.Up || e.Key == Key.Tab) { Select(sel - 1); e.Handled = true; }
                else if (e.Key == Key.Enter) { RunSel(); e.Handled = true; }
            };
            anim.Interval = TimeSpan.FromMilliseconds(8); anim.Tick += AnimStep; watch.Interval = TimeSpan.FromMilliseconds(30); watch.Tick += WatchTick;
            debounce.Interval = TimeSpan.FromMilliseconds(220); debounce.Tick += delegate { debounce.Stop(); StartFileSearch(); };
        }

        static string F(string n) { string d = StartStyler.AppDir; if (string.IsNullOrEmpty(d)) d = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); return Path.Combine(d, n); }

        void PaintBg(DrawingContext dc)
        {
            double h = Math.Max(SearchH, ActualHeight); Rect full = new Rect(0.5, 0.5, W - 1, Math.Max(1, h - 1)); double r = Theme.MenuRound * 0.92;
            if (frost != null)
            {
                ImageBrush ib = new ImageBrush(frost); ib.Stretch = Stretch.Fill; ib.ViewportUnits = BrushMappingMode.Absolute; ib.Viewport = new Rect(0, 0, W, SearchH + 1 + MaxRows * RowH + 2 * Pad); ib.TileMode = TileMode.None;
                dc.DrawRoundedRectangle(ib, null, full, r, r);
            }
            byte ta = (byte)Math.Min(235, Math.Max(S.Get("glass", 135), frost != null ? 120 : 210));
            dc.DrawRoundedRectangle(new SolidColorBrush(Theme.TintA(ta)), null, full, r, r);
            dc.DrawRoundedRectangle(Theme.Gloss(), Theme.BorderPen(), full, r, r);
        }

        void Build()
        {
            built = true; box = new TextBox(); hint = new TextBlock(); list = new StackPanel(); sep = new Border(); root.Children.Add(bg); root.RenderTransform = st; root.RenderTransformOrigin = new Point(0.5, 0.0);
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SearchH) }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // keresősor: nagyító + szövegmező
            Grid sg = new Grid { Margin = new Thickness(22, 0, 22, 0) }; sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Canvas mag = new Canvas { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = Orientation_Left() };
            System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse { Width = 15, Height = 15, Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), StrokeThickness = 2.2 }; Canvas.SetLeft(el, 2); Canvas.SetTop(el, 2); mag.Children.Add(el);
            System.Windows.Shapes.Line ln = new System.Windows.Shapes.Line { X1 = 15, Y1 = 15, X2 = 21.5, Y2 = 21.5, Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; mag.Children.Add(ln);
            Grid.SetColumn(mag, 0); sg.Children.Add(mag);
            Grid tg = new Grid(); Grid.SetColumn(tg, 1);
            box.Background = Brushes.Transparent; box.BorderThickness = new Thickness(0); box.Foreground = Brushes.White; box.CaretBrush = Brushes.White; box.FontFamily = Inter; box.FontSize = Theme.FS(23); box.FontWeight = Theme.W(0); box.VerticalContentAlignment = VerticalAlignment.Center; box.VerticalAlignment = VerticalAlignment.Center;
            hint.Text = Loc.T("Spotlight keresés"); hint.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)); hint.FontFamily = Inter; hint.FontSize = Theme.FS(23); hint.FontWeight = Theme.W(0); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(2, 0, 0, 0);
            tg.Children.Add(box); tg.Children.Add(hint); sg.Children.Add(tg); Grid.SetRow(sg, 0); g.Children.Add(sg);
            box.TextChanged += delegate { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; Search(); };
            sep.Height = 1; sep.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)); sep.Margin = new Thickness(0, 0, 0, 0); sep.Visibility = Visibility.Collapsed; Grid.SetRow(sep, 1); g.Children.Add(sep);
            list.Margin = new Thickness(Pad, Pad, Pad, Pad); Grid.SetRow(list, 2); g.Children.Add(list); root.Children.Add(g);
            foreach (string ln2 in ReadUsage()) { int i = ln2.LastIndexOf('|'); int n; if (i > 0 && int.TryParse(ln2.Substring(i + 1), out n)) usage[ln2.Substring(0, i)] = n; }
        }
        static HorizontalAlignment Orientation_Left() { return HorizontalAlignment.Left; }
        static string[] ReadUsage() { try { string p = F("spotlight-usage.txt"); if (File.Exists(p)) return File.ReadAllLines(p, Encoding.UTF8); } catch { } return new string[0]; }
        void SaveUsage() { try { List<string> l = new List<string>(); foreach (KeyValuePair<string, int> kv in usage) l.Add(kv.Key + "|" + kv.Value); File.WriteAllLines(F("spotlight-usage.txt"), l, new UTF8Encoding(false)); } catch { } }

        // ---------------------------------------------------------------- keresés
        static double Match(string name, string q)
        {
            CompareInfo ci = CultureInfo.CurrentCulture.CompareInfo; CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            int i = ci.IndexOf(name, q, o); if (i < 0)
            {
                // betűk sorrendben (pl. "gdk" -> "GlassDock")
                int p = 0; for (int k = 0; k < name.Length && p < q.Length; k++) if (ci.Compare(name.Substring(k, 1), q.Substring(p, 1), o) == 0) p++;
                return p == q.Length ? 400 + name.Length * 0.5 : -1;
            }
            if (i == 0) return name.Length * 0.5;
            char pc = name[i - 1]; if (pc == ' ' || pc == '-' || pc == '_' || pc == '.' || pc == '(') return 100 + name.Length * 0.5;
            return 220 + name.Length * 0.5;
        }

        void Search()
        {
            string q = box.Text.Trim(); res.Clear(); sel = 0; gen++; fileRes.Clear(); debounce.Stop();
            if (q.Length > 0)
            {
                // számolás
                string calc = TryCalc(q); if (calc != null) { SpotResult r = new SpotResult { Title = calc, Sub = Loc.T("Számolás: ") + q + Loc.T(" (Enter = másolás)"), Kind = Loc.T("Számológép"), Glyph = "=" }; string cc = calc; r.Run = delegate { try { Clipboard.SetText(cc); } catch { } }; res.Add(r); }
                // műveletek: port felszabadítása, RAM seprés a Lumenben
                System.Text.RegularExpressions.Match pm = System.Text.RegularExpressions.Regex.Match(q, @"^(?:kill\s+)?port\s+(\d{2,5})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pm.Success) { string port = pm.Groups[1].Value; SpotResult pr = new SpotResult { Title = "Port " + port + Loc.T(" felszabadítása"), Sub = Loc.T("Az azon figyelő folyamat leállítása"), Kind = Loc.T("Művelet"), Glyph = "⛔" }; pr.Run = delegate { try { using (Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"Get-NetTCPConnection -State Listen -LocalPort " + port + " -ErrorAction SilentlyContinue | Select-Object -Expand OwningProcess -Unique | Where-Object { $_ -gt 4 } | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }\"") { CreateNoWindow = true, UseShellExecute = false })) { } } catch { } }; res.Add(pr); }
                if (q.Equals("ram", StringComparison.OrdinalIgnoreCase) || q.Equals("sweep", StringComparison.OrdinalIgnoreCase) || q.StartsWith(Loc.T("memó"), StringComparison.OrdinalIgnoreCase) || q.StartsWith("sepr", StringComparison.OrdinalIgnoreCase))
                { SpotResult sr = new SpotResult { Title = Loc.T("RAM seprés most"), Sub = Loc.T("A Lumen azonnal összehúzza a folyamatok munkakészletét"), Kind = Loc.T("Művelet"), Glyph = "🧹" }; sr.Run = delegate { try { using (System.Threading.EventWaitHandle ev = System.Threading.EventWaitHandle.OpenExisting("Lumen-GlassControl-sweep")) ev.Set(); } catch { } }; res.Add(sr); }
                // alkalmazások
                List<SpotResult> apps = new List<SpotResult>();
                bool anyDirect = false; foreach (AppEntry a0 in StartMenu.Entries()) { if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(a0.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) { anyDirect = true; break; } }
                foreach (AppEntry a in StartMenu.Entries())
                {
                    double sc = Match(a.Name, q); if (sc < 0 || (anyDirect && sc >= 400) || (q.Length < 3 && sc >= 400)) continue; int u; if (usage.TryGetValue(a.Path, out u)) sc -= Math.Min(u, 25) * 9;
                    AppEntry ae = a; SpotResult r = new SpotResult { Title = a.Name, Sub = a.Custom ? System.IO.Path.GetDirectoryName(a.Path) : Loc.T("Alkalmazás"), Kind = Loc.T("Alkalmazás"), Path = a.Path, IsApp = true, Score = sc, Key = a.Path };
                    r.Run = delegate { LaunchApp(ae); }; apps.Add(r);
                }
                apps.Sort(delegate (SpotResult x, SpotResult y) { return x.Score.CompareTo(y.Score); });
                for (int i = 0; i < apps.Count && i < 5; i++) res.Add(apps[i]);
                // rendszer-műveletek
                AddAction(q, Loc.T("Beállítások"), Loc.T("Windows beállítások"), "ms-settings:", null);
                AddAction(q, Loc.T("Feladatkezelő"), Loc.T("Folyamatok és teljesítmény"), "taskmgr.exe", null);
                AddAction(q, Loc.T("Parancssor"), "cmd.exe", "cmd.exe", null);
                AddAction(q, Loc.T("Zárolás"), Loc.T("Képernyő zárolása"), "rundll32.exe", "user32.dll,LockWorkStation");
                if (q.Length >= 2) debounce.Start();
            }
            else ClipboardActions();
            Rebuild();
        }

        // üres kereséskor a vágólap tartalmához kínálunk műveletet
        void ClipboardActions()
        {
            string t = null; try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { } if (string.IsNullOrWhiteSpace(t) || t.Length > 20000) return; t = t.Trim();
            if ((t.StartsWith("http://") || t.StartsWith("https://")) && t.IndexOf('\n') < 0) { string u = t; SpotResult r = new SpotResult { Title = Loc.T("Megnyitás: ") + (u.Length > 60 ? u.Substring(0, 60) + "…" : u), Sub = Loc.T("Link a vágólapon"), Kind = Loc.T("Vágólap"), Glyph = "🌐" }; r.Run = delegate { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { } }; res.Add(r); }
            else if (t.IndexOf('\n') < 0 && t.Length > 3 && t.Length < 260 && !t.Trim('"').StartsWith(@"\\") && (System.IO.File.Exists(t.Trim('"')) || System.IO.Directory.Exists(t.Trim('"')))) { string p = t.Trim('"'); SpotResult r = new SpotResult { Title = Loc.T("Megmutatás az Intézőben"), Sub = p, Kind = Loc.T("Vágólap"), Glyph = "📂" }; r.Run = delegate { try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch { } }; res.Add(r); }
            else if ((t.StartsWith("{") && t.EndsWith("}")) || (t.StartsWith("[") && t.EndsWith("]"))) { string j = t; SpotResult r = new SpotResult { Title = Loc.T("JSON szépítése → vágólap"), Sub = Loc.T("A vágólapon JSON van"), Kind = Loc.T("Vágólap"), Glyph = "{ }" }; r.Run = delegate { try { Clipboard.SetText(PrettyJson(j)); } catch { } }; res.Add(r); }
            else if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$")) { int cr = Convert.ToInt32(t.Substring(1, 2), 16), cg = Convert.ToInt32(t.Substring(3, 2), 16), cb = Convert.ToInt32(t.Substring(5, 2), 16); string rgb = "rgb(" + cr + ", " + cg + ", " + cb + ")"; SpotResult r = new SpotResult { Title = rgb, Sub = Loc.T("Szín a vágólapon (Enter = másolás RGB-ként)"), Kind = Loc.T("Vágólap"), Glyph = "🎨" }; r.Run = delegate { try { Clipboard.SetText(rgb); } catch { } }; res.Add(r); }
        }
        static string PrettyJson(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(); int lvl = 0; bool str = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i]; if (str) { sb.Append(c); if (c == '\\' && i + 1 < s.Length) sb.Append(s[++i]); else if (c == '"') str = false; continue; }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '"') { str = true; sb.Append(c); } else if (c == '{' || c == '[') { sb.Append(c); lvl++; sb.Append('\n').Append(' ', lvl * 2); } else if (c == '}' || c == ']') { lvl = Math.Max(0, lvl - 1); sb.Append('\n').Append(' ', lvl * 2).Append(c); } else if (c == ',') { sb.Append(c).Append('\n').Append(' ', lvl * 2); } else if (c == ':') sb.Append(": "); else sb.Append(c);
            }
            return sb.ToString();
        }
        void AddAction(string q, string name, string sub, string file, string args)
        {
            if (Match(name, q) < 0 || res.Count >= MaxRows - 1) return; SpotResult r = new SpotResult { Title = name, Sub = sub, Kind = Loc.T("Művelet"), Glyph = "⚙" };
            r.Run = delegate { try { ProcessStartInfo pi = new ProcessStartInfo(file); if (args != null) pi.Arguments = args; pi.UseShellExecute = true; Process.Start(pi); } catch { } }; res.Add(r);
        }

        static string TryCalc(string q)
        {
            if (q.Length > 60) return null; bool op = false, dig = false; foreach (char c in q) { if (c >= '0' && c <= '9') dig = true; else if ("+-*/%".IndexOf(c) >= 0) op = true; else if (".,() ".IndexOf(c) < 0) return null; }
            if (!op || !dig) return null;
            try { object v = new DataTable().Compute(q.Replace(',', '.'), ""); double d = Convert.ToDouble(v, CultureInfo.InvariantCulture); if (double.IsNaN(d) || double.IsInfinity(d)) return null; return d.ToString("G12", CultureInfo.CurrentCulture); } catch { return null; }
        }

        // fájlok a Windows Search indexből (háttérszálon, késleltetve)
        static int inflight; static DateTime inflightAt;
        static readonly string[] RiskyExt = { ".exe",".com",".bat",".cmd",".ps1",".psm1",".vbs",".vbe",".js",".jse",".wsf",".wsh",".wsc",".sct",".hta",".msi",".msp",".msc",".scr",".lnk",".cpl",".jar",".reg",".pif",".url",".appref-ms",".application",".inf",".py",".pyw",".pyz",".sh",".scf",".chm",".website",".settingcontent-ms",".appx",".msix",".appxbundle",".msixbundle" };
        void StartFileSearch()
        {
            string q = box.Text.Trim(); if (q.Length < 2) return; int g = gen; if (Interlocked.CompareExchange(ref inflight, 1, 0) != 0 && (DateTime.Now - inflightAt).TotalSeconds < 15) return; inflightAt = DateTime.Now; string esc = q.Replace("'", "''").Replace("%", "").Replace("_", "").Replace("[", "").Replace("]", "").Replace("\"", "");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found = new List<string>();
                try
                {
                    using (OleDbConnection c = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows'"))
                    {
                        if (g != gen) throw new OperationCanceledException(); c.Open(); OleDbCommand cmd = c.CreateCommand(); cmd.CommandTimeout = 4; cmd.CommandText = "SELECT TOP 60 System.ItemPathDisplay FROM SystemIndex WHERE SCOPE='file:' AND System.FileName LIKE '%" + esc + "%' ORDER BY System.DateModified DESC";
                        using (OleDbDataReader rd = cmd.ExecuteReader()) { while (rd.Read() && found.Count < 4) { if (rd.IsDBNull(0)) continue; string p = rd.GetString(0); bool skip = false; foreach (string sk in SkipPaths) if (p.IndexOf(sk, StringComparison.OrdinalIgnoreCase) >= 0) { skip = true; break; } if (p.IndexOf("\\.", StringComparison.Ordinal) >= 0) skip = true; if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) skip = true; if (!skip) found.Add(p); } }
                    }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Interlocked.Exchange(ref inflight, 0); if (g != gen) { if (open) StartFileSearch(); return; }
                    if (!open) return; fileRes.Clear(); SpotResult keepSel = (sel >= 0 && sel < res.Count) ? res[sel] : null;
                    foreach (string p in found) { string pp = p; SpotResult r = new SpotResult { Title = System.IO.Path.GetFileName(pp), Sub = System.IO.Path.GetDirectoryName(pp), Kind = Directory.Exists(pp) ? Loc.T("Mappa") : Loc.T("Fájl"), Path = pp, IsFile = true, Key = pp }; r.Run = delegate { try { string ex2 = System.IO.Path.GetExtension(pp).ToLowerInvariant(); bool risky = Array.IndexOf(RiskyExt, ex2) >= 0; Process.Start(risky ? new ProcessStartInfo("explorer.exe", "/select,\"" + pp + "\"") : new ProcessStartInfo(pp) { UseShellExecute = true }); } catch { } }; fileRes.Add(r); }
                    // a fájlok az alkalmazások és műveletek után, a webes keresés elé kerülnek
                    int keep = res.Count; for (int i = 0; i < fileRes.Count && res.Count < MaxRows - 1; i++) res.Add(fileRes[i]); Rebuild(false); int ni = keepSel != null ? res.IndexOf(keepSel) : -1; if (ni >= 0 && ni != sel) Select(ni);
                }));
            });
        }

        void LaunchApp(AppEntry a)
        {
            try
            {
                if (a.Custom) { ProcessStartInfo pi = new ProcessStartInfo(a.Path) { UseShellExecute = true }; try { pi.WorkingDirectory = System.IO.Path.GetDirectoryName(a.Path); } catch { } Process.Start(pi); }
                else Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + a.Path) { UseShellExecute = true });
            }
            catch { }
        }

        // ---------------------------------------------------------------- lista felépítése
        void Rebuild() { Rebuild(true); }
        void Rebuild(bool resetSel)
        {
            string q = box.Text.Trim();
            // webes keresés mindig az utolsó sor, ha van keresőszöveg
            for (int i = res.Count - 1; i >= 0; i--) if (res[i].Kind == "Web") res.RemoveAt(i);
            if (q.Length > 0) { SpotResult w = new SpotResult { Title = Loc.T("Keresés a weben: ") + q, Sub = Loc.T("Alapértelmezett böngésző"), Kind = "Web", Glyph = "↗" }; string qq = q; w.Run = delegate { try { Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(qq)) { UseShellExecute = true }); } catch { } }; res.Add(w); }
            while (res.Count > MaxRows) res.RemoveAt(res.Count - 2);
            if (resetSel || sel >= res.Count) sel = 0;
            list.Children.Clear();
            for (int i = 0; i < res.Count; i++) { SpotResult r = res[i]; r.Row = MakeRow(r, i); list.Children.Add(r.Row); }
            sep.Visibility = res.Count > 0 ? Visibility.Visible : Visibility.Collapsed; Select(sel);
            double h = res.Count > 0 ? SearchH + 1 + res.Count * RowH + 2 * Pad + 2 : SearchH; Height = h;
            if (hwnd != IntPtr.Zero && open) N.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, bw, (int)Math.Round(h * scale), 0x16);
            bg.InvalidateVisual(); LoadIconsLater();
        }

        Border MakeRow(SpotResult r, int idx)
        {
            Border row = new Border { Height = RowH, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(10, 0, 14, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(Theme.TileRound * 0.66), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), Background = new LinearGradientBrush(Color.FromArgb(Theme.TileA, 255, 255, 255), Color.FromArgb((byte)(Theme.TileA / 3), 255, 255, 255), 90) };
            if (r.Glyph != null) tile.Child = new TextBlock { Text = r.Glyph, Foreground = Brushes.White, FontSize = Theme.FS(19), FontWeight = Theme.W(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else { Image img = new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); tile.Child = img; r.Img = img; BitmapSource c; if (r.Key != null && icons.TryGetValue(r.Key, out c)) img.Source = c; }
            g.Children.Add(tile);
            StackPanel tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tp, 1);
            tp.Children.Add(new TextBlock { Text = r.Title, Foreground = Brushes.White, FontFamily = Inter, FontSize = Theme.FS(15), FontWeight = Theme.W(1), TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(r.Sub)) tp.Children.Add(new TextBlock { Text = r.Sub, Foreground = new SolidColorBrush(Color.FromArgb(125, 255, 255, 255)), FontFamily = Inter, FontSize = Theme.FS(11), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            g.Children.Add(tp);
            TextBlock kind = new TextBlock { Text = r.Kind, Foreground = new SolidColorBrush(Color.FromArgb(105, 255, 255, 255)), FontFamily = Inter, FontSize = Theme.FS(11), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(kind, 2); g.Children.Add(kind);
            row.Child = g; int i = idx;
            row.MouseEnter += delegate { Select(i); }; row.MouseLeftButtonUp += delegate { sel = i; RunSel(); };
            return row;
        }

        void Select(int i)
        {
            if (res.Count == 0) { sel = 0; return; } if (i < 0) i = res.Count - 1; if (i >= res.Count) i = 0; sel = i;
            for (int k = 0; k < res.Count; k++) if (res[k].Row != null) { bool on = k == sel; res[k].Row.Background = on ? new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)) : Brushes.Transparent; res[k].Row.BorderBrush = on ? new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)) : Brushes.Transparent; }
        }

        void LoadIconsLater()
        {
            foreach (SpotResult r in res)
            {
                if (r.Img == null || r.Key == null || r.Img.Source != null) continue; SpotResult rr = r;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (rr.Img == null || rr.Img.Source != null) return; BitmapSource c;
                    if (!icons.TryGetValue(rr.Key, out c))
                    {
                        try
                        {
                            BitmapSource big = ShellIcon.Jumbo(rr.IsApp ? "shell:AppsFolder\\" + rr.Path : rr.Path); if (big != null)
                            {
                                BitmapSource small = new TransformedBitmap(big, new ScaleTransform(0.22, 0.22)); small.Freeze(); BitmapSource prep = ShellIcon.StripPlate(small);
                                c = S.Get("mono", 1) > 0 ? N.Mono(prep, 86, Theme.IconGain(0.66)) : N.Own(prep); if (icons.Count > 400) icons.Clear(); icons[rr.Key] = c;
                            }
                        }
                        catch { }
                    }
                    if (c != null) rr.Img.Source = c;
                }), DispatcherPriority.Background);
            }
        }

        void RunSel()
        {
            if (res.Count == 0) return; SpotResult r = res[Math.Max(0, Math.Min(sel, res.Count - 1))];
            if (r.Key != null) { int u; usage.TryGetValue(r.Key, out u); usage[r.Key] = u + 1; SaveUsage(); }
            CloseIt(); try { if (r.Run != null) r.Run(); } catch { }
        }

        // ---------------------------------------------------------------- megnyitás / bezárás
        void Flip() { if (open) CloseIt(); else if ((DateTime.Now - closedAt).TotalMilliseconds > 250) OpenIt(); }

        void OpenIt()
        {
            if (built && themeVer != S.Version) { root.Children.Clear(); built = false; }
            if (!built) { Build(); themeVer = S.Version; } if (StartMenu.IsOpen) StartMenu.CloseNow();
            Forms.Screen sc = Forms.Screen.FromPoint(Forms.Cursor.Position); bw = (int)Math.Round(W * scale);
            int maxH = (int)Math.Round((SearchH + 1 + MaxRows * RowH + 2 * Pad + 2) * scale);
            bx = sc.Bounds.X + (sc.Bounds.Width - bw) / 2; by = sc.Bounds.Y + (int)(sc.Bounds.Height * 0.2);
            prevDown = true; if (box.Text.Length > 0) box.Text = ""; else Search();
            frost = Frost.Capture(bx, by, bw, maxH, 18); bg.InvalidateVisual();
            root.Opacity = 0; st.ScaleX = st.ScaleY = 0.97; op = 0; open = true; activated = false; openedAt = DateTime.Now; watch.Start();
            if (hwnd == IntPtr.Zero) { Left = bx / scale; Top = by / scale; Show(); }
            N.SetWindowPos(hwnd, new IntPtr(-1), bx, by, bw, (int)Math.Round(Height * scale), 0x40);
            Show(); Activate(); N.SwitchToThisWindow(hwnd, true); box.Focus(); Keyboard.Focus(box); StartAnim(1);
        }

        void CloseIt() { if (!open) return; open = false; closedAt = DateTime.Now; watch.Stop(); debounce.Stop(); gen++; StartAnim(0); }
        void StartAnim(int t) { atg = t; a0 = op; aw.Restart(); anim.Start(); }
        void AnimStep(object s, EventArgs e)
        {
            double t = Math.Min(1.0, aw.ElapsedMilliseconds / (atg == 1 ? 230.0 : 140.0) / Theme.Anim);
            op = a0 + (atg - a0) * (atg == 1 ? Ease.OutCubic(t) : Ease.InCubic(t)); double o = Math.Max(0, Math.Min(1, op)); root.Opacity = o; st.ScaleX = st.ScaleY = 0.97 + 0.03 * o;
            if (t >= 1) { anim.Stop(); if (atg == 0) Hide(); }
        }

        void WatchTick(object s, EventArgs e)
        {
            if (!open) return; bool down = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0; N.POINT c; N.GetCursorPos(out c); int hp = (int)Math.Round(Height * scale);
            if (down && !prevDown && (DateTime.Now - openedAt).TotalMilliseconds > 250) { if (c.X < bx || c.X >= bx + bw || c.Y < by || c.Y >= by + hp) { prevDown = down; CloseIt(); return; } }
            prevDown = down;
        }
    }
}
