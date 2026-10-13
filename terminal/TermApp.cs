// GlassTerm – macOS Terminal kinézetű, valódi (ConPTY) terminál ablak: lapok, panelek, parancspaletta, quake mód.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GlassTerm
{
    // A macOS-ablak három "közlekedési lámpája"
    public class Lights : StackPanel
    {
        public Lights(Action close, Action min, Action zoom)
        {
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center; Margin = new Thickness(13, 0, 0, 0); Background = Brushes.Transparent;
            Color[] cols = { Color.FromRgb(255, 95, 87), Color.FromRgb(254, 188, 46), Color.FromRgb(40, 200, 64) }; string[] gl = { "×", "−", "+" }; Action[] acts = { close, min, zoom };
            for (int i = 0; i < 3; i++)
            {
                Grid g = new Grid { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Arrow };
                Shapes.Ellipse e = new Shapes.Ellipse { Fill = new SolidColorBrush(cols[i]), Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), StrokeThickness = 0.6 }; e.Tag = cols[i];
                TextBlock t = new TextBlock { Text = gl[i], FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(190, 40, 20, 10)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0), Visibility = Visibility.Hidden, IsHitTestVisible = false };
                g.Children.Add(e); g.Children.Add(t); Action a = acts[i]; g.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev) { a(); ev.Handled = true; }; g.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; };
                Children.Add(g);
            }
            MouseEnter += delegate { Glyphs(true); }; MouseLeave += delegate { Glyphs(false); };
        }
        void Glyphs(bool on) { if (!active) return; foreach (Grid g in Children) g.Children[1].Visibility = on ? Visibility.Visible : Visibility.Hidden; }
        bool active = true;
        public void SetActive(bool a)
        {
            active = a;
            foreach (Grid g in Children) { Shapes.Ellipse e = (Shapes.Ellipse)g.Children[0]; e.Fill = new SolidColorBrush(a ? (Color)e.Tag : Color.FromRgb(84, 84, 87)); if (!a) g.Children[1].Visibility = Visibility.Hidden; }
        }
    }

    public class TermWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO f);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const double M = 22; const int HOTKEY = 0x4C54;
        Grid outer, handles; Border body, shadow, titleBar; TermHost host; TextBlock titleText; Lights lights; bool maxed, quake, quakeShown = true; Rect restore, quakeRestore; bool quakeHotkey;
        PT rsCur; Rect rsRect; string rsDir; UIElement rsEl; Forms.NotifyIcon tray; double topmostBefore;

        public TermWindow(string profileName, int cols, int rows, bool startQuake)
        {
            Title = "Terminal"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
            FontFamily = new FontFamily("Inter, Segoe UI"); UseLayoutRounding = true;
            host = new TermHost(Path.Combine(Cfg.Dir, "fonts"), false);
            host.Win.NewWindow = delegate { try { Process.Start(Assembly.GetExecutingAssembly().Location); } catch { } };
            host.Win.ToggleMax = ToggleMax; host.Win.Minimize = delegate { WindowState = WindowState.Minimized; }; host.Win.CloseWindow = delegate { Close(); };
            host.Win.ToggleTopmost = delegate { Topmost = !Topmost; }; host.Win.IsTopmost = delegate { return Topmost; }; host.Win.IsMaxed = delegate { return maxed; };
            host.Win.WindowActive = delegate { return IsActive; }; host.Win.SetOpacity = delegate (double o) { Opacity = Math.Max(0.35, Math.Min(1.0, o)); };
            host.Win.Quake = ToggleQuake; host.Win.FlashWindow = Flash; host.Win.Balloon = Balloon;

            // az alapméret a betűmérettől függ: egy ideiglenes nézettel mérjük ki a cellát
            TermView probe = new TermView(host.FontDir); probe.FontSizePx = TermSettings.I.FontSize; probe.SetFontSize(probe.FontSizePx);
            double w = cols * probe.CellW + probe.Pad.Left + probe.Pad.Right + 2, h = rows * probe.CellH + probe.Pad.Top + probe.Pad.Bottom + 29; probe.Shutdown();
            Width = w + 2 * M; Height = h + 2 * M; MinWidth = 380 + 2 * M; MinHeight = 200 + 2 * M; WindowStartupLocation = WindowStartupLocation.CenterScreen;

            outer = new Grid { Margin = new Thickness(0) };
            shadow = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Black, Margin = new Thickness(M), Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 7, Direction = 270, Opacity = 0.5 }, IsHitTestVisible = false };
            body = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 120, 120, 125)), BorderThickness = new Thickness(1), Margin = new Thickness(M), ClipToBounds = true };
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            titleBar = new Border { CornerRadius = new CornerRadius(9, 9, 0, 0), Background = new LinearGradientBrush(Color.FromRgb(60, 60, 63), Color.FromRgb(46, 46, 49), 90) };
            Grid tg = new Grid();
            lights = new Lights(delegate { Close(); }, delegate { WindowState = WindowState.Minimized; }, ToggleMax); lights.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel tp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Shapes.Path folder = new Shapes.Path { Data = Geometry.Parse("M0,3.2 Q0,1.2 2,1.2 L5.2,1.2 L7,3 L14,3 Q16,3 16,5 L16,11 Q16,13 14,13 L2,13 Q0,13 0,11 Z"), Fill = new LinearGradientBrush(Color.FromRgb(104, 200, 255), Color.FromRgb(46, 150, 230), 90), Margin = new Thickness(0, 0, 7, 0), Height = 14, Width = 16, Stretch = Stretch.None };
            titleText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 180)), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 700 };
            tp.Children.Add(folder); tp.Children.Add(titleText);
            // jobb oldali gyorsgombok: paletta és keresés
            StackPanel rp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            rp.Children.Add(TitleBtn("⌘", Loc.T("Parancspaletta (Ctrl+Shift+P)"), delegate { host.ShowPalette(""); })); rp.Children.Add(TitleBtn("⌕", Loc.T("Keresés (Ctrl+F)"), delegate { host.ShowFind(); })); rp.Children.Add(TitleBtn("⊞", Loc.T("Osztás jobbra (Ctrl+Shift+D)"), delegate { host.RunAction("pane.right"); }));
            tg.Children.Add(tp); tg.Children.Add(lights); tg.Children.Add(rp); titleBar.Child = tg; Grid.SetRow(titleBar, 0); g.Children.Add(titleBar);
            titleBar.MouseLeftButtonDown += TitleDown;
            GlassMenu.Attach(titleBar, delegate
            {
                return new MenuEntry[] {
                    GlassMenu.Item(Loc.T("Új ablak"), "Ctrl+Shift+N", delegate { host.RunAction("win.new"); }),
                    GlassMenu.Item(maxed ? Loc.T("Visszaállítás") : Loc.T("Teljes méret"), "F11", ToggleMax),
                    GlassMenu.Item(Loc.T("Minimalizálás"), delegate { WindowState = WindowState.Minimized; }),
                    GlassMenu.Check(Loc.T("Mindig felül"), Topmost, delegate { Topmost = !Topmost; }),
                    GlassMenu.Check(Loc.T("Lenyíló (quake) mód"), quake, ToggleQuake),
                    GlassMenu.Sep(),
                    GlassMenu.Item(Loc.T("Parancspaletta…"), "Ctrl+Shift+P", delegate { host.ShowPalette(""); }),
                    GlassMenu.Item(Loc.T("Gyorsbillentyűk listája"), "F1", delegate { host.ShowCheatSheet(); }),
                    GlassMenu.Item(Loc.T("Beállítások (term.ini) megnyitása"), delegate { host.RunAction("open.ini"); }),
                    GlassMenu.Sep(),
                    GlassMenu.Item(Loc.T("Bezárás"), delegate { Close(); })
                };
            });
            Border sep = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)) }; Grid.SetRow(sep, 1); g.Children.Add(sep);
            Grid.SetRow(host, 2); g.Children.Add(host); body.Child = g;

            handles = new Grid { Margin = new Thickness(M - 5) };
            string[] dirs = { "N", "S", "W", "E", "NW", "NE", "SW", "SE" };
            foreach (string d in dirs) AddHandle(d);
            outer.Children.Add(shadow); outer.Children.Add(body); outer.Children.Add(handles); Content = outer;

            Activated += delegate { lights.SetActive(true); host.FocusCurrent(); };
            Deactivated += delegate { lights.SetActive(false); };
            host.TitleChanged += UpdateTitle; host.GridChanged += UpdateTitle;
            host.AllTabsClosed += delegate { if (quake) { quakeShown = false; Hide(); host.NewTab(ShellProfiles.Default(), null); } else Close(); };
            TermSettings.I.Changed += ApplyChrome; ApplyChrome();
            PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.F11) { ToggleMax(); e.Handled = true; } else if (e.Key == Key.Escape && quake && !e.Handled && Keyboard.Modifiers == ModifierKeys.None && false) { HideQuake(); } };
            Closing += delegate (object o, System.ComponentModel.CancelEventArgs e) { if (quake && !shuttingDown && Cfg.GetBool("quakehide", true) && !forceClose) { e.Cancel = true; HideQuake(); } };
            Closed += delegate { shuttingDown = true; Unhot(); if (tray != null) { tray.Visible = false; tray.Dispose(); } host.Shutdown(); };
            SourceInitialized += delegate { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(Hook); if (startQuake || Cfg.GetBool("quake", false)) { RegisterQuakeKey(); } };
            Loaded += delegate
            {
                if (profileName != null) { ShellProfile pr = null; foreach (ShellProfile x in ShellProfiles.Detect()) if (string.Equals(x.Name, profileName, StringComparison.OrdinalIgnoreCase)) pr = x; host.NewTab(pr ?? ShellProfiles.Default(), Cfg.Get("startdir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))); }
                else host.OpenFirst(true);
                UpdateTitle(); if (startQuake) EnterQuake(false); host.FocusCurrent();
            };
        }

        bool shuttingDown, forceClose;
        public TermHost Host { get { return host; } }
        public FrameworkElement Root { get { return outer; } }
        public void ForceClose() { forceClose = true; Close(); }

        Border TitleBtn(string glyph, string tip, Action click)
        {
            Border b = new Border { Width = 24, Height = 20, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = new TextBlock { Text = glyph, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 156)), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) } };
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); }; b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; }; b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { click(); e.Handled = true; }; return b;
        }

        // a téma színe adja a keret és a címsor árnyalatát
        void ApplyChrome()
        {
            Color bg = TermSettings.I.CurTheme.Bg; double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            Func<Color, int, Color> shift = delegate (Color c, int d) { return Color.FromRgb((byte)Math.Max(0, Math.Min(255, c.R + d)), (byte)Math.Max(0, Math.Min(255, c.G + d)), (byte)Math.Max(0, Math.Min(255, c.B + d))); };
            int up = lum > 0.5 ? -14 : 22;
            body.Background = new SolidColorBrush(bg); titleBar.Background = new LinearGradientBrush(shift(bg, up + (lum > 0.5 ? -4 : 8)), shift(bg, up), 90);
            titleText.Foreground = new SolidColorBrush(lum > 0.5 ? Color.FromRgb(70, 70, 76) : Color.FromRgb(176, 176, 180));
            Opacity = Math.Max(0.35, Math.Min(1.0, TermSettings.I.Opacity));
        }

        void UpdateTitle() { string t = host.TitleText; if (titleText.Text != t) { titleText.Text = t; Title = t; } }

        // ---- értesítések
        void Flash()
        {
            try { FLASHWINFO f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)), hwnd = new WindowInteropHelper(this).Handle, dwFlags = 3 | 12, uCount = 3, dwTimeout = 0 }; FlashWindowEx(ref f); } catch { }
        }
        void Balloon(string title, string text)
        {
            try
            {
                if (tray == null) { tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Terminal" }; tray.BalloonTipClicked += delegate { Reveal(); }; }
                tray.BalloonTipTitle = title; tray.BalloonTipText = text; tray.ShowBalloonTip(4000);
            }
            catch { }
        }
        void Reveal() { if (!IsVisible) Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); host.FocusCurrent(); }

        // ---- ablak mozgatás / méretezés / teljes méret
        void TitleDown(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { ToggleMax(); return; }
            if (maxed || quake) return; try { DragMove(); } catch { }
        }

        void ToggleMax()
        {
            if (quake) return;
            if (!maxed)
            {
                restore = new Rect(Left, Top, Width, Height); double k = Dpi(); Forms.Screen sc = Forms.Screen.FromRectangle(new System.Drawing.Rectangle((int)(Left * k), (int)(Top * k), (int)(Width * k), (int)(Height * k))); System.Drawing.Rectangle wa = sc.WorkingArea;
                outer.Margin = new Thickness(0); body.Margin = new Thickness(0); shadow.Visibility = Visibility.Collapsed; handles.Visibility = Visibility.Collapsed; body.CornerRadius = new CornerRadius(0); titleBar.CornerRadius = new CornerRadius(0);
                Left = wa.X / k; Top = wa.Y / k; Width = wa.Width / k; Height = wa.Height / k; maxed = true;
            }
            else
            {
                body.Margin = new Thickness(M); shadow.Visibility = Visibility.Visible; handles.Visibility = Visibility.Visible; body.CornerRadius = new CornerRadius(10); titleBar.CornerRadius = new CornerRadius(9, 9, 0, 0);
                Left = restore.X; Top = restore.Y; Width = restore.Width; Height = restore.Height; maxed = false;
            }
        }

        double Dpi() { PresentationSource ps = PresentationSource.FromVisual(this); return ps != null ? ps.CompositionTarget.TransformToDevice.M11 : 1.0; }

        // ---- quake (lenyíló) mód: a képernyő tetejéről lecsúszó terminál, globális gyorsbillentyűvel
        void ToggleQuake() { if (quake) LeaveQuake(); else { EnterQuake(true); } }
        void EnterQuake(bool focus)
        {
            if (quake) return; if (maxed) ToggleMax();
            quakeRestore = new Rect(Left, Top, Width, Height); quake = true; topmostBefore = Topmost ? 1 : 0; Topmost = true; RegisterQuakeKey();
            body.Margin = new Thickness(0); shadow.Visibility = Visibility.Collapsed; handles.Visibility = Visibility.Collapsed; body.CornerRadius = new CornerRadius(0, 0, 10, 10); titleBar.CornerRadius = new CornerRadius(0);
            PlaceQuake(); if (focus) { Activate(); host.FocusCurrent(); }
        }
        void PlaceQuake()
        {
            double k = Dpi(); Forms.Screen sc = Forms.Screen.FromPoint(Forms.Cursor.Position); System.Drawing.Rectangle wa = sc.WorkingArea; double frac = Math.Max(0.2, Math.Min(0.95, Cfg.GetInt("quakeheight", 45) / 100.0));
            Left = wa.X / k; Top = wa.Y / k; Width = wa.Width / k; Height = wa.Height * frac / k;
        }
        void LeaveQuake()
        {
            quake = false; Topmost = topmostBefore > 0; body.Margin = new Thickness(M); shadow.Visibility = Visibility.Visible; handles.Visibility = Visibility.Visible; body.CornerRadius = new CornerRadius(10); titleBar.CornerRadius = new CornerRadius(9, 9, 0, 0);
            Left = quakeRestore.X; Top = quakeRestore.Y; Width = quakeRestore.Width; Height = quakeRestore.Height; if (!IsVisible) Show();
        }
        void HideQuake() { quakeShown = false; Hide(); }
        void ShowQuake() { PlaceQuake(); quakeShown = true; Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); host.FocusCurrent(); }
        void RegisterQuakeKey()
        {
            if (quakeHotkey) return; IntPtr h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
            string spec = Cfg.Get("quakekey", "Ctrl+Alt+Space"); uint mod = 0x4000; uint vk = 0x20;     // MOD_NOREPEAT
            foreach (string part in spec.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "ctrl") mod |= 2; else if (p == "alt") mod |= 1; else if (p == "shift") mod |= 4; else if (p == "win") mod |= 8;
                else { try { Key k = (Key)Enum.Parse(typeof(Key), p, true); vk = (uint)KeyInterop.VirtualKeyFromKey(k); } catch { } }
            }
            quakeHotkey = RegisterHotKey(h, HOTKEY, mod, vk);
            if (!quakeHotkey) host.Toast("A(z) " + spec + Loc.T(" gyorsbillentyű foglalt – állítsd át: quakekey= a term.ini-ben"));
        }
        void Unhot() { if (quakeHotkey) { try { UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY); } catch { } quakeHotkey = false; } }
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg == 0x0312 && wp.ToInt32() == HOTKEY)
            {
                handled = true;
                if (!quake) EnterQuake(true);
                else if (IsVisible && IsActive) HideQuake(); else ShowQuake();
            }
            return IntPtr.Zero;
        }

        void AddHandle(string d)
        {
            Border b = new Border { Background = Brushes.Transparent, Tag = d };
            double t = 8, c = 14;
            switch (d)
            {
                case "N": b.Height = t; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(c, 0, c, 0); b.Cursor = Cursors.SizeNS; break;
                case "S": b.Height = t; b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(c, 0, c, 0); b.Cursor = Cursors.SizeNS; break;
                case "W": b.Width = t; b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, c, 0, c); b.Cursor = Cursors.SizeWE; break;
                case "E": b.Width = t; b.HorizontalAlignment = HorizontalAlignment.Right; b.Margin = new Thickness(0, c, 0, c); b.Cursor = Cursors.SizeWE; break;
                case "NW": b.Width = c; b.Height = c; b.HorizontalAlignment = HorizontalAlignment.Left; b.VerticalAlignment = VerticalAlignment.Top; b.Cursor = Cursors.SizeNWSE; break;
                case "SE": b.Width = c; b.Height = c; b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Bottom; b.Cursor = Cursors.SizeNWSE; break;
                case "NE": b.Width = c; b.Height = c; b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; b.Cursor = Cursors.SizeNESW; break;
                case "SW": b.Width = c; b.Height = c; b.HorizontalAlignment = HorizontalAlignment.Left; b.VerticalAlignment = VerticalAlignment.Bottom; b.Cursor = Cursors.SizeNESW; break;
            }
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { rsDir = d; rsEl = b; GetCursorPos(out rsCur); rsRect = new Rect(Left, Top, Width, Height); b.CaptureMouse(); e.Handled = true; };
            b.MouseMove += delegate (object o, MouseEventArgs e)
            {
                if (rsEl != b || !b.IsMouseCaptured) return; PT c2; GetCursorPos(out c2); double k = Dpi(), dx = (c2.X - rsCur.X) / k, dy = (c2.Y - rsCur.Y) / k;
                double l = rsRect.X, tp = rsRect.Y, w = rsRect.Width, h = rsRect.Height;
                if (rsDir.Contains("E")) w = Math.Max(MinWidth, rsRect.Width + dx);
                if (rsDir.Contains("S")) h = Math.Max(MinHeight, rsRect.Height + dy);
                if (rsDir.Contains("W")) { w = Math.Max(MinWidth, rsRect.Width - dx); l = rsRect.Right - w; }
                if (rsDir.Contains("N")) { h = Math.Max(MinHeight, rsRect.Height - dy); tp = rsRect.Bottom - h; }
                Left = l; Top = tp; Width = w; Height = h;
            };
            b.MouseLeftButtonUp += delegate { b.ReleaseMouseCapture(); rsEl = null; };
            handles.Children.Add(b);
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h); [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc); [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags); [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
        static string Arg(string[] a, string name) { for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }
        static bool Has(string[] a, string name) { foreach (string s in a) if (s == name) return true; return false; }

        static string lastErr = ""; static DateTime lastErrAt = DateTime.MinValue;
        [STAThread]
        public static void Main(string[] args)
        {
            try { System.Runtime.ProfileOptimization.SetProfileRoot(AppDomain.CurrentDomain.BaseDirectory); System.Runtime.ProfileOptimization.StartProfile("terminal.jit"); } catch { }      // többmagos JIT: a következő indítás gyorsabb
            string shot = Arg(args, "/shot"), dump = Arg(args, "/dump"), profile = Arg(args, "/profile") ?? Arg(args, "/cmd"), type = Arg(args, "/type"), raw = Arg(args, "/raw"), run = Arg(args, "/run"), exec = Arg(args, "/exec"), previewUrl = Arg(args, "/preview"), screen = Arg(args, "/screen"), features = Arg(args, "/features"); int after = 4000;
            if (Arg(args, "/after") != null) int.TryParse(Arg(args, "/after"), out after);
            bool quake = Has(args, "/quake");
            Application app = new Application(); GlassMenu.ApplyTheme();
            app.DispatcherUnhandledException += delegate (object o, DispatcherUnhandledExceptionEventArgs e) { try { string cl = Path.Combine(Cfg.Dir, "crash.log"); FileInfo cfi = new FileInfo(cl); if (cfi.Exists && cfi.Length > 500000) File.Delete(cl); if (e.Exception.Message != lastErr || (DateTime.Now - lastErrAt).TotalSeconds > 5) { lastErr = e.Exception.Message; lastErrAt = DateTime.Now; File.AppendAllText(cl, DateTime.Now + " " + e.Exception + Environment.NewLine); } } catch { } e.Handled = true; };
            TermSettings.I.Load(); if (shot != null || dump != null) TermHost.NoSession = true;     // a tesztfuttatások ne írják felül a munkamenetet
            if (features != null)       // csak a funkciólista előállítása a műveleti jegyzékből
            {
                TermHost h = new TermHost(Path.Combine(Cfg.Dir, "fonts"), true); File.WriteAllText(features, FeatureDoc.Build(h), new UTF8Encoding(false)); return;
            }
            TermWindow w = new TermWindow(profile, Cfg.GetInt("cols", 100), Cfg.GetInt("rows", 28), quake);
            if (raw != null) { w.Loaded += delegate { if (w.Host.CurrentView != null) w.Host.CurrentView.RawLog = raw; }; }
            if (type != null)
            {
                DispatcherTimer tt = new DispatcherTimer(); tt.Interval = TimeSpan.FromMilliseconds(Math.Max(1200, after / 3)); tt.Tick += delegate { tt.Stop(); if (w.Host.CurrentView != null) w.Host.CurrentView.Send(type.Replace("\\r", "\r")); }; tt.Start();
            }
            if (previewUrl != null)
            {
                DispatcherTimer pt = new DispatcherTimer(); pt.Interval = TimeSpan.FromMilliseconds(Math.Max(1500, after / 3)); pt.Tick += delegate { pt.Stop(); w.Host.OpenPreview(previewUrl); }; pt.Start();
            }
            if (exec != null)
            {
                DispatcherTimer et = new DispatcherTimer(); et.Interval = TimeSpan.FromMilliseconds(Math.Max(1200, after / 4)); et.Tick += delegate { et.Stop(); if (w.Host.CurrentView != null) w.Host.CurrentView.RunCommand(exec, true); }; et.Start();
            }
            if (run != null)
            {
                string[] ids = run.Split(','); int ri = 0; DispatcherTimer rt = new DispatcherTimer(); rt.Interval = TimeSpan.FromMilliseconds(Math.Max(1500, after / 2)); rt.Tick += delegate { rt.Interval = TimeSpan.FromMilliseconds(900); if (ri >= ids.Length) { rt.Stop(); return; } string id = ids[ri++].Trim(); if (id.StartsWith("exec:")) { if (w.Host.CurrentView != null) w.Host.CurrentView.RunCommand(id.Substring(5), true); } else w.Host.RunAction(id); }; rt.Start();
            }
            if (screen != null)       // valódi képernyőkép az ablakról (a WebView2 tartalma csak így látszik)
            {
                DispatcherTimer sc = new DispatcherTimer(); sc.Interval = TimeSpan.FromMilliseconds(after); sc.Tick += delegate
                {
                    sc.Stop(); try { w.Activate(); double k = PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice.M11; Point sp0 = w.PointToScreen(new Point(0, 0)); System.Drawing.Rectangle r = new System.Drawing.Rectangle((int)sp0.X, (int)sp0.Y, (int)(w.ActualWidth * k), (int)(w.ActualHeight * k));
                        File.WriteAllText(screen + ".txt", "rect=" + r + " left=" + w.Left + " top=" + w.Top + " vis=" + w.IsVisible + " state=" + w.WindowState + " active=" + w.IsActive + " k=" + k);
                        using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(r.Width, r.Height)) { using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp)) { IntPtr hd = g.GetHdc(); PrintWindow(new System.Windows.Interop.WindowInteropHelper(w).Handle, hd, 2); g.ReleaseHdc(hd); } bmp.Save(screen, System.Drawing.Imaging.ImageFormat.Png); } }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(Cfg.Dir, "crash.log"), ex.ToString()); }
                    w.ForceClose(); Application.Current.Shutdown();
                }; sc.Start();
            }
            if (shot != null || dump != null)
            {
                DispatcherTimer t = new DispatcherTimer(); t.Interval = TimeSpan.FromMilliseconds(after);
                t.Tick += delegate
                {
                    t.Stop();
                    try
                    {
                        if (shot != null)
                        {
                            FrameworkElement r = w.Root; r.UpdateLayout(); RenderTargetBitmap rtb = new RenderTargetBitmap((int)r.ActualWidth, (int)r.ActualHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(r);
                            PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using (FileStream fs = File.Create(shot)) enc.Save(fs);
                        }
                        if (dump != null)
                        {
                            StringBuilder sb = new StringBuilder(); TermView v = w.Host.CurrentView; VtScreen s = v.Scr; sb.AppendLine("cursor=" + s.X + "," + s.Y + " size=" + s.Cols + "x" + s.Rows + " sb=" + s.Sb.Count + " tabs=" + w.Host.TabCount + " actions=" + w.Host.ActionCount + " cwd=" + v.Cwd + " marks=" + v.Marks.Count + " last=" + v.LastCommand + " chips=" + w.Host.DebugChips()); foreach (PromptMark pm in v.Marks) sb.AppendLine("mark id=" + pm.Id + " started=" + pm.Started + " done=" + pm.Done + " exit=" + pm.Exit + " secs=" + pm.Seconds.ToString("0.0") + " cmd=" + pm.Cmd);
                            for (int y = 0; y < s.Rows; y++) sb.AppendLine(VtScreen.LineText(s.Grid[y], 0, s.Cols));
                            foreach (TermView av in w.Host.AllViews()) if (av != v) { sb.AppendLine(Loc.T("--- másik panel: ") + av.Cols + "x" + av.Rows + " marks=" + av.Marks.Count + " sbTrim=" + av.Scr.Trimmed); foreach (PromptMark pm in av.Marks) sb.AppendLine("mark id=" + pm.Id + " out=" + pm.OutId + " end=" + pm.EndId + " started=" + pm.Started + " done=" + pm.Done + " exit=" + pm.Exit + " cmd=" + pm.Cmd); for (int y = 0; y < av.Scr.Rows; y++) sb.AppendLine(VtScreen.LineText(av.Scr.Grid[y], 0, av.Scr.Cols)); }
                            File.WriteAllText(dump, sb.ToString(), Encoding.UTF8);
                        }
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(Cfg.Dir, "crash.log"), ex.ToString()); }
                    w.ForceClose(); Application.Current.Shutdown();
                };
                t.Start();
            }
            app.Run(w);
        }
    }
}
