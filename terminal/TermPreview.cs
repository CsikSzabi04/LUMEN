// Előnézeti panel a terminál alatt: valódi Edge (WebView2) motor, így a localhost-os oldalak (Vite, Next, Express…) mint a böngészőben jelennek meg.
// A WebView2 DLL-ek a terminal\webview2 mappában vannak, futásidőben (reflection) töltődnek be – ha hiányoznak, a terminál ettől még teljesen működik.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassTerm
{
    public class WebPreview : Border
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        public static string Dir;                       // a WebView2 DLL-ek mappája (a gazda állítja be)
        static Assembly wpfAsm; static string loadError = "";

        public event Action CloseRequested; public event Action<string> ExternalRequested; public event Action<string> Navigated;
        FrameworkElement wv; Type wvT; TextBox urlBox; Grid stage; TextBlock info; DispatcherTimer poll; Border bBack, bFwd; int mode; string lastSrc = ""; bool disposed;

        static readonly Brush Fg = Brushes.White, Muted = new SolidColorBrush(Color.FromRgb(160, 160, 168));

        public static bool RuntimeInstalled()
        {
            try
            {
                foreach (string k in new string[] { @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" })
                {
                    using (RegistryKey r = Registry.LocalMachine.OpenSubKey(k)) { if (r != null && r.GetValue("pv") != null) return true; }
                    using (RegistryKey r = Registry.CurrentUser.OpenSubKey(k)) { if (r != null && r.GetValue("pv") != null) return true; }
                }
            }
            catch { }
            return false;
        }
        public static string WhyNot()
        {
            if (Dir == null || !File.Exists(Path.Combine(Dir, "Microsoft.Web.WebView2.Wpf.dll")) || !File.Exists(Path.Combine(Dir, "Microsoft.Web.WebView2.Core.dll")) || !File.Exists(Path.Combine(Dir, "WebView2Loader.dll"))) return Loc.T("Hiányoznak a WebView2 DLL-ek (terminal\\webview2 mappa).");
            if (!RuntimeInstalled()) return Loc.T("Nincs telepítve a Microsoft Edge WebView2 Runtime.");
            return TryLoad() ? null : Loc.T("A WebView2 nem tölthető be: ") + loadError;
        }
        static bool TryLoad()
        {
            if (wpfAsm != null) return true;
            try
            {
                LoadLibrary(Path.Combine(Dir, "WebView2Loader.dll")); Assembly.LoadFrom(Path.Combine(Dir, "Microsoft.Web.WebView2.Core.dll")); wpfAsm = Assembly.LoadFrom(Path.Combine(Dir, "Microsoft.Web.WebView2.Wpf.dll")); return true;
            }
            catch (Exception ex) { loadError = ex.Message; return false; }
        }

        Border Btn(string glyph, string tip, Action click)
        {
            Border b = new Border { Width = 28, Height = 24, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 2, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = tip, Child = new TextBlock { Text = glyph, Foreground = Fg, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            b.MouseEnter += delegate { if (b.Opacity > 0.9) b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); }; b.MouseLeave += delegate { b.Background = Brushes.Transparent; }; b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; if (b.Opacity > 0.9) click(); };
            return b;
        }

        public WebPreview()
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)); BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); BorderThickness = new Thickness(0, 1, 0, 0);
            Grid root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); Child = root;
            Grid bar = new Grid { Margin = new Thickness(8, 4, 8, 4) }; bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal }; bBack = Btn("◀", Loc.T("Vissza"), delegate { Call("GoBack"); }); bFwd = Btn("▶", Loc.T("Előre"), delegate { Call("GoForward"); }); left.Children.Add(bBack); left.Children.Add(bFwd); left.Children.Add(Btn("↻", Loc.T("Újratöltés (F5)"), delegate { Reload(); }));
            urlBox = new TextBox { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), Foreground = Fg, CaretBrush = Fg, BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 8, 1), FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), SelectionBrush = new SolidColorBrush(Color.FromRgb(10, 132, 255)) };
            GlassMenu.ForTextBox(urlBox); Grid.SetColumn(urlBox, 1);
            urlBox.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Enter) { string t = urlBox.Text.Trim(); if (t.Length > 0) { int port; if (int.TryParse(t, out port)) t = "http://localhost:" + port; else if (t.IndexOf("://") < 0) t = "http://" + t; Navigate(t); } e.Handled = true; } };
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(right, 2); info = new TextBlock { Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            right.Children.Add(info); right.Children.Add(Btn("💻", Loc.T("Teljes szélesség"), delegate { SetMode(0); })); right.Children.Add(Btn("▭", Loc.T("Tablet (820 px)"), delegate { SetMode(1); })); right.Children.Add(Btn("📱", Loc.T("Telefon (390 px)"), delegate { SetMode(2); })); right.Children.Add(Btn("🛠", Loc.T("Fejlesztői eszközök (F12)"), delegate { OpenDevTools(); }));
            right.Children.Add(Btn("↗", Loc.T("Megnyitás a böngészőben"), delegate { if (ExternalRequested != null) ExternalRequested(CurrentUrl); })); right.Children.Add(Btn("✕", Loc.T("Előnézet bezárása"), delegate { if (CloseRequested != null) CloseRequested(); }));
            bar.Children.Add(left); bar.Children.Add(urlBox); bar.Children.Add(right); root.Children.Add(bar);
            stage = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 16, 18)) }; Grid.SetRow(stage, 1); root.Children.Add(stage);
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; poll.Tick += delegate { Poll(); };
            PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.F5) { Reload(); e.Handled = true; } else if (e.Key == Key.F12) { OpenDevTools(); e.Handled = true; } };
        }

        public string CurrentUrl { get { try { if (wv != null) { object s = wvT.GetProperty("Source").GetValue(wv, null); if (s != null) return ((Uri)s).AbsoluteUri; } } catch { } return urlBox.Text; } }

        // a böngészőmotor képe csak hardveres WPF-rajzolással jelenik meg: amíg előnézet van nyitva, átkapcsolunk, utána vissza
        static int hwUsers; static bool hwSwitched;
        static void HwOn() { hwUsers++; if (RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly) { RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default; hwSwitched = true; } }
        static void HwOff() { hwUsers = Math.Max(0, hwUsers - 1); if (hwUsers == 0 && hwSwitched) { RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly; hwSwitched = false; } }
        bool hwHeld;
        bool failed;
        void EnsureControl()
        {
            if (wv != null || disposed || failed) return; string why = WhyNot(); if (why == null && !hwHeld) { hwHeld = true; HwOn(); }
            if (why != null) { failed = true; stage.Children.Add(new TextBlock { Text = why + Loc.T("\nNyisd meg a böngészőben a ↗ gombbal."), Foreground = Muted, FontSize = 13, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap }); return; }
            try
            {
                Type t = wpfAsm.GetType("Microsoft.Web.WebView2.Wpf.WebView2CompositionControl") ?? wpfAsm.GetType("Microsoft.Web.WebView2.Wpf.WebView2"); wvT = t; wv = (FrameworkElement)Activator.CreateInstance(t);
                object cp = Activator.CreateInstance(wpfAsm.GetType("Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties")); string ud = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Lumen\WebView2");
                cp.GetType().GetProperty("UserDataFolder").SetValue(cp, ud, null); t.GetProperty("CreationProperties").SetValue(wv, cp, null); stage.Children.Add(wv); ApplyMode(); poll.Start();

            }
            catch (Exception ex) { wv = null; failed = true; stage.Children.Add(new TextBlock { Text = Loc.T("Az előnézet nem indítható: ") + ex.Message, Foreground = Muted, FontSize = 13, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap }); }
        }

        public void Navigate(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return; EnsureControl(); urlBox.Text = u.AbsoluteUri; if (wv == null) return;
            try { object cur = wvT.GetProperty("Source").GetValue(wv, null); if (cur != null && ((Uri)cur).AbsoluteUri == u.AbsoluteUri && Core() != null) Call("Reload"); else wvT.GetProperty("Source").SetValue(wv, u, null); } catch (Exception ex) { info.Text = ex.Message; }
        }
        public void Reload() { if (wv == null) return; if (Core() != null) Call("Reload"); else { string u = urlBox.Text; Uri x; if (Uri.TryCreate(u, UriKind.Absolute, out x)) try { wvT.GetProperty("Source").SetValue(wv, x, null); } catch { } } }
        object Core() { try { return wv != null ? wvT.GetProperty("CoreWebView2").GetValue(wv, null) : null; } catch { return null; } }
        void Call(string method) { try { if (wv != null) wvT.GetMethod(method, Type.EmptyTypes).Invoke(wv, null); } catch { } }
        public void OpenDevTools() { try { object c = Core(); if (c != null) c.GetType().GetMethod("OpenDevToolsWindow").Invoke(c, null); else info.Text = Loc.T("még tölt…"); } catch { } }

        public void SetMode(int m) { mode = m; ApplyMode(); }
        void ApplyMode()
        {
            if (wv == null) return; wv.HorizontalAlignment = mode == 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Center; wv.Width = mode == 0 ? double.NaN : (mode == 1 ? 820 : 390); wv.Margin = new Thickness(0);
            info.Text = mode == 0 ? "" : (mode == 1 ? "820 px" : "390 px");
        }

        // a WebView2 eseményeit nem kötjük le (reflection), inkább rövid időközönként olvassuk az állapotot
        void Poll()
        {
            if (wv == null || disposed || !IsVisible) return;
            try
            {
                object s = wvT.GetProperty("Source").GetValue(wv, null); string src = s != null ? ((Uri)s).AbsoluteUri : ""; if (src != lastSrc) { lastSrc = src; if (!urlBox.IsKeyboardFocusWithin) urlBox.Text = src; if (Navigated != null) Navigated(src); }
                if (mode == 0) { object core = Core(); string ti = ""; if (core != null) { try { ti = (string)core.GetType().GetProperty("DocumentTitle").GetValue(core, null); } catch { } } info.Text = core == null ? Loc.T("indul…") : Trunc(ti, 28); }
                bool cb = (bool)wvT.GetProperty("CanGoBack").GetValue(wv, null), cf = (bool)wvT.GetProperty("CanGoForward").GetValue(wv, null); bBack.Opacity = cb ? 1 : 0.35; bFwd.Opacity = cf ? 1 : 0.35;
            }
            catch { }
        }

        static string Trunc(string s, int n) { return s != null && s.Length > n ? s.Substring(0, n - 1) + "…" : s; }
        public void Shutdown()
        {
            disposed = true; poll.Stop(); if (hwHeld) { hwHeld = false; HwOff(); }
            try { if (wv != null) { stage.Children.Remove(wv); IDisposable d = wv as IDisposable; if (d != null) d.Dispose(); } } catch { }
            wv = null;
        }
    }
}
