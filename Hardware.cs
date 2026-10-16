// Hardver-vezérlő ablak (MSI Afterburner-szerű): monitorok fényereje/kontrasztja (DDC/CI), hőmérséklet + ventilátor (MSI Afterburner), LED (OpenRGB).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassControlWpf
{
    public class HardwareWindow : Window
    {
        // ---- monitorok: DDC/CI a Windows saját dxva2 felületén keresztül
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        delegate bool MonEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)] struct PHYS { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonEnumProc cb, IntPtr data);
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PHYS[] arr);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, [In] PHYS[] arr);
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
        [DllImport("dxva2.dll")] static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorContrast(IntPtr h, uint v);

        class Mon { public IntPtr H; public string Name; public bool Bri, Con; public uint BMin, BCur, BMax, CMin, CCur, CMax; public PHYS[] Arr; }
        List<Mon> mons = new List<Mon>();

        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(28, 28, 32)), Card = new SolidColorBrush(Color.FromRgb(42, 42, 48)), Muted = new SolidColorBrush(Color.FromRgb(154, 154, 162)), Accent = new SolidColorBrush(Color.FromRgb(10, 132, 255));
        StackPanel body, monPanel, sensPanel; DispatcherTimer tick; TextBlock sensInfo;

        public HardwareWindow()
        {
            Title = "Hardware"; Width = 560; Height = 720; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterScreen; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = true;
            Border root = new Border { Background = Bg, CornerRadius = new CornerRadius(16), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(6) };
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
            DockPanel bar = new DockPanel { Margin = new Thickness(18, 12, 12, 6), Background = Brushes.Transparent };
            TextBlock x = new TextBlock { Text = "✕", Foreground = Brushes.White, FontSize = 15, Cursor = Cursors.Hand, Padding = new Thickness(10, 2, 10, 2) }; x.MouseLeftButtonUp += delegate { Close(); }; DockPanel.SetDock(x, Dock.Right); bar.Children.Add(x);
            bar.Children.Add(new TextBlock { Text = Loc.T("🎛  Hardver-vezérlő"), Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { try { DragMove(); } catch { } };
            body = new StackPanel { Margin = new Thickness(18, 4, 18, 18) };
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            Grid.SetRow(sv, 1); g.Children.Add(bar); g.Children.Add(sv); root.Child = g; Content = root;

            body.Children.Add(Head(Loc.T("Monitorok (fényerő / kontraszt)")));
            monPanel = new StackPanel(); monPanel.Children.Add(Note(Loc.T("Monitorok keresése…"))); body.Children.Add(monPanel);
            body.Children.Add(Head(Loc.T("Hőmérséklet és ventilátor")));
            sensInfo = Note(""); body.Children.Add(sensInfo); sensPanel = new StackPanel(); body.Children.Add(sensPanel); BuildFanButtons();
            body.Children.Add(Head(Loc.T("LED / RGB világítás"))); BuildLed();

            ThreadPool.QueueUserWorkItem(delegate { List<Mon> l = FindMonitors(); Dispatcher.BeginInvoke(new Action(delegate { mons = l; BuildMonitors(); })); });
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tick.Tick += delegate { RefreshSensors(); }; tick.Start(); RefreshSensors();
            Closed += delegate { tick.Stop(); foreach (Mon m in mons) { try { DestroyPhysicalMonitors((uint)m.Arr.Length, m.Arr); } catch { } } mons.Clear(); };
        }

        public void SavePng(string path) { try { UpdateLayout(); FrameworkElement c = (FrameworkElement)Content; RenderTargetBitmap rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(c); PngBitmapEncoder en = new PngBitmapEncoder(); en.Frames.Add(BitmapFrame.Create(rtb)); using (FileStream fs = File.Create(path)) en.Save(fs); } catch { } }
        static TextBlock Head(string t) { return new TextBlock { Text = t, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 6) }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) }; }
        static Border Pill(string t, Action a) { Border b = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Child = new TextBlock { Text = t, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold } }; b.MouseLeftButtonUp += delegate { try { a(); } catch { } }; return b; }

        List<Mon> FindMonitors()
        {
            List<Mon> r = new List<Mon>(); int idx = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hm, IntPtr hdc, ref RECT rc, IntPtr d)
            {
                idx++; uint n; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n) || n == 0) return true;
                PHYS[] arr = new PHYS[n]; if (!GetPhysicalMonitorsFromHMONITOR(hm, n, arr)) return true;
                for (int i = 0; i < n; i++)
                {
                    Mon m = new Mon { H = arr[i].h, Arr = arr, Name = Loc.T("Monitor ") + idx + " · " + (rc.R - rc.L) + "×" + (rc.B - rc.T) + (rc.L == 0 && rc.T == 0 ? Loc.T(" (fő)") : "") };
                    try { m.Bri = GetMonitorBrightness(m.H, out m.BMin, out m.BCur, out m.BMax); } catch { }
                    try { m.Con = GetMonitorContrast(m.H, out m.CMin, out m.CCur, out m.CMax); } catch { }
                    r.Add(m);
                }
                return true;
            }, IntPtr.Zero);
            return r;
        }

        void BuildMonitors()
        {
            monPanel.Children.Clear();
            if (mons.Count == 0) { monPanel.Children.Add(Note(Loc.T("Nem találtam vezérelhető monitort."))); return; }
            foreach (Mon m in mons)
            {
                Border card = new Border { Background = Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) }; StackPanel sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "🖥  " + m.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13 });
                if (!m.Bri && !m.Con) sp.Children.Add(Note(Loc.T("Ez a monitor nem engedi a vezérlést (kapcsold be a monitor saját menüjében a DDC/CI opciót).")));
                if (m.Bri) sp.Children.Add(Slider(Loc.T("Fényerő"), m.BMin, m.BMax, m.BCur, delegate (uint v) { SetMonitorBrightness(m.H, v); }));
                if (m.Con) sp.Children.Add(Slider(Loc.T("Kontraszt"), m.CMin, m.CMax, m.CCur, delegate (uint v) { SetMonitorContrast(m.H, v); }));
                card.Child = sp; monPanel.Children.Add(card);
            }
        }

        // a csúszka elengedés után / rövid szünet után ír (a DDC lassú, ne záporozzon)
        UIElement Slider(string label, uint min, uint max, uint cur, Action<uint> apply)
        {
            Grid gr = new Grid { Margin = new Thickness(0, 6, 0, 0) }; gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) }); gr.ColumnDefinitions.Add(new ColumnDefinition()); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            TextBlock lb = new TextBlock { Text = label, Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Slider sl = new Slider { Minimum = min, Maximum = Math.Max(max, min + 1), Value = cur, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            TextBlock val = new TextBlock { Text = cur.ToString(), Foreground = Brushes.White, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DispatcherTimer deb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            deb.Tick += delegate { deb.Stop(); uint v = (uint)Math.Round(sl.Value); ThreadPool.QueueUserWorkItem(delegate { try { apply(v); } catch { } }); };
            sl.ValueChanged += delegate { val.Text = ((int)Math.Round(sl.Value)).ToString(); deb.Stop(); deb.Start(); };
            Grid.SetColumn(sl, 1); Grid.SetColumn(val, 2); gr.Children.Add(lb); gr.Children.Add(sl); gr.Children.Add(val); return gr;
        }

        // ---- hőmérséklet / ventilátor: az MSI Afterburner megosztott memóriájából (MAHMSharedMemory)
        static string AfterburnerExe()
        {
            foreach (string p in new string[] { @"C:\Program Files (x86)\MSI Afterburner\MSIAfterburner.exe", @"C:\Program Files\MSI Afterburner\MSIAfterburner.exe" }) if (File.Exists(p)) return p;
            return null;
        }
        void BuildFanButtons()
        {
            WrapPanel w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            string ab = AfterburnerExe();
            if (ab == null) { body.Children.Add(Note(Loc.T("MSI Afterburner nincs telepítve: a ventilátor-sebesség és a hőmérséklet csak vele (vagy gyártói szoftverrel) vezérelhető."))); return; }
            body.Children.Add(Note(Loc.T("A ventilátor-görbét az Afterburnerben állítod be és mented profilba (1–5); itt egy kattintással átváltasz közöttük.")));
            for (int i = 1; i <= 5; i++) { int n = i; w.Children.Add(Pill(Loc.T("Profil ") + n, delegate { Process.Start(new ProcessStartInfo(ab, "-Profile" + n) { UseShellExecute = true }); })); }
            w.Children.Add(Pill(Loc.T("MSI Afterburner megnyitása"), delegate { Process.Start(new ProcessStartInfo(ab) { UseShellExecute = true }); }));
            body.Children.Add(w);
        }

        void RefreshSensors()
        {
            try
            {
                List<string[]> rows = ReadMahm();
                if (rows == null) { sensInfo.Text = AfterburnerExe() == null ? "" : Loc.T("Az MSI Afterburner nem fut – indítsd el (alul), és itt megjelennek az élő értékek."); sensPanel.Children.Clear(); return; }
                sensInfo.Text = Loc.T("Élő értékek (MSI Afterburner)");
                while (sensPanel.Children.Count < rows.Count) sensPanel.Children.Add(new TextBlock { Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 1) });
                while (sensPanel.Children.Count > rows.Count) sensPanel.Children.RemoveAt(sensPanel.Children.Count - 1);
                for (int i = 0; i < rows.Count; i++) ((TextBlock)sensPanel.Children[i]).Text = rows[i][0].PadRight(26) + rows[i][1];
            }
            catch { }
        }
        static string Ansi(MemoryMappedViewAccessor v, long off, int len) { byte[] b = new byte[len]; v.ReadArray(off, b, 0, len); int e = Array.IndexOf(b, (byte)0); return Encoding.Default.GetString(b, 0, e < 0 ? len : e); }
        static List<string[]> ReadMahm()
        {
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor v = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    uint hdr = v.ReadUInt32(8), num = v.ReadUInt32(12), esz = v.ReadUInt32(16); if (num == 0 || num > 200 || esz < 800) return new List<string[]>();
                    long dataOff = esz >= 1324 ? 1300 : 780; List<string[]> r = new List<string[]>();
                    for (uint i = 0; i < num; i++)
                    {
                        long b = hdr + (long)i * esz; string name = Ansi(v, b, 260), unit = Ansi(v, b + 260, 260); float d = v.ReadSingle(b + dataOff);
                        if (name.Length == 0 || float.IsNaN(d) || d > 1e30f) continue;
                        r.Add(new string[] { name, d.ToString(Math.Abs(d) >= 100 ? "0" : "0.0") + " " + unit });
                    }
                    return r;
                }
            }
            catch { return null; }
        }

        // ---- LED: OpenRGB (nyílt forrású, sok alaplap / RAM / GPU / periféria LED-jét kezeli)
        static string OpenRgbExe()
        {
            List<string> c = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\OpenRGB\OpenRGB.exe", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs\OpenRGB\OpenRGB.exe", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\OpenRGB\OpenRGB.exe" };
            foreach (string p in c) if (File.Exists(p)) return p;
            try { foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) { if (d.Length == 0) continue; string f = Path.Combine(d.Trim('"'), "OpenRGB.exe"); if (File.Exists(f)) return f; } } catch { }
            return null;
        }
        void BuildLed()
        {
            string orgb = OpenRgbExe(); string board = "";
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS")) if (k != null) board = k.GetValue("BaseBoardManufacturer") + " " + k.GetValue("BaseBoardProduct"); } catch { }
            if (orgb == null)
            {
                body.Children.Add(Note(Loc.T("LED-vezérléshez az ingyenes, nyílt forrású OpenRGB kell (az alaplap: ") + board + Loc.T("). Telepítés után itt színt választhatsz.")));
                WrapPanel w0 = new WrapPanel(); w0.Children.Add(Pill(Loc.T("OpenRGB letöltése"), delegate { Process.Start(new ProcessStartInfo("https://openrgb.org") { UseShellExecute = true }); })); body.Children.Add(w0); return;
            }
            body.Children.Add(Note(Loc.T("Szín az összes támogatott eszközre (OpenRGB):")));
            WrapPanel w = new WrapPanel();
            string[][] cols = { new[] { "🔴", "FF0000" }, new[] { "🟠", "FF8000" }, new[] { "🟡", "FFFF00" }, new[] { "🟢", "00FF00" }, new[] { "🔵", "0A84FF" }, new[] { "🟣", "8000FF" }, new[] { "⚪", "FFFFFF" }, new[] { Loc.T("Ki"), "000000" } };
            foreach (string[] c in cols) { string hex = c[1]; w.Children.Add(Pill(c[0], delegate { Process.Start(new ProcessStartInfo(orgb, "--mode static --color " + hex) { UseShellExecute = false, CreateNoWindow = true }); })); }
            w.Children.Add(Pill(Loc.T("OpenRGB megnyitása"), delegate { Process.Start(new ProcessStartInfo(orgb) { UseShellExecute = true }); }));
            body.Children.Add(w);
        }
    }
}
