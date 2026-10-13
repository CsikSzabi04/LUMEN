// Lumen – Feladatkezelő: Windows-szerű Teljesítmény (élő grafikonok) és Indítás (visszafordítható letiltás) nézet a Folyamatok mellett.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassControlWpf
{
    public class TaskManagerHost : Grid
    {
        UIElement procs; PerfView perf = new PerfView(); StartupView startup = new StartupView(); List<Border> pills = new List<Border>();
        public TaskManagerHost(UIElement procView)
        {
            procs = procView; RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) }; string[] names = { Loc.T("Folyamatok"), Loc.T("Teljesítmény"), Loc.T("Indítás") };
            for (int i = 0; i < names.Length; i++) { int k = i; Border b = new Border { Padding = new Thickness(14, 6, 14, 6), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Child = new TextBlock { Text = names[i], FontSize = 13, FontWeight = FontWeights.SemiBold } }; b.MouseLeftButtonUp += delegate { Select(k); }; pills.Add(b); bar.Children.Add(b); }
            Children.Add(bar); foreach (UIElement e in new UIElement[] { procs, perf, startup }) { Grid.SetRow(e, 1); Children.Add(e); } Select(0); int tmI; if (int.TryParse(Environment.GetEnvironmentVariable("LUMEN_TM"), out tmI)) Select(tmI);
        }
        void Select(int i)
        {
            procs.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed; perf.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed; startup.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
            for (int k = 0; k < pills.Count; k++) { pills[k].Background = k == i ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : Brushes.Transparent; ((TextBlock)pills[k].Child).Foreground = k == i ? Brushes.White : new SolidColorBrush(Color.FromRgb(160, 160, 168)); }
            perf.Active = i == 1; if (i == 2) startup.Reload();
        }
    }

    // ------------------------------------------------------------------ grafikon
    class GraphView : FrameworkElement
    {
        public double[] V = new double[60]; public Color C = Colors.DodgerBlue; public double Max = 100; public bool Big;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight; if (w < 4 || h < 4) return; Brush line = new SolidColorBrush(Color.FromArgb(Big ? (byte)150 : (byte)255, C.R, C.G, C.B)); Pen gp = new Pen(new SolidColorBrush(Color.FromArgb(28, C.R, C.G, C.B)), 1);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(Big ? (byte)14 : (byte)10, C.R, C.G, C.B)), new Pen(new SolidColorBrush(Color.FromArgb(120, C.R, C.G, C.B)), 1), new Rect(0.5, 0.5, w - 1, h - 1));
            if (Big) { for (int i = 1; i < 10; i++) dc.DrawLine(gp, new Point(0, h * i / 10), new Point(w, h * i / 10)); for (int i = 1; i < 6; i++) dc.DrawLine(gp, new Point(w * i / 6, 0), new Point(w * i / 6, h)); }
            StreamGeometry g = new StreamGeometry(); using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(0, h), true, true); for (int i = 0; i < V.Length; i++) { double x = w * i / (V.Length - 1), y = h - Math.Max(0, Math.Min(1, V[i] / Math.Max(Max, 1e-9))) * (h - 2); c.LineTo(new Point(x, y), true, false); } c.LineTo(new Point(w, h), true, false);
            }
            g.Freeze(); dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(Big ? (byte)60 : (byte)70, C.R, C.G, C.B)), new Pen(new SolidColorBrush(C), Big ? 1.6 : 1.2), g);
        }
    }

    // ------------------------------------------------------------------ teljesítmény
    class Metric { public string Name; public Color C; public double[] H = new double[60]; public double Cur, Max = 100; public string Sub = "", Unit = "%"; public GraphView Mini = new GraphView(); public Border Card; public TextBlock SubTxt; public bool Auto; }

    class PerfView : Grid
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct MEMSTAT { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMSTAT s);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [StructLayout(LayoutKind.Sequential)] struct PERF { public uint cb; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysTotal, PhysAvail, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize; public uint Handles, Processes, Threads; }
        [DllImport("psapi.dll")] static extern bool GetPerformanceInfo(out PERF p, uint cb);

        List<Metric> ms = new List<Metric>(); Metric sel; PerformanceCounter cpuC, diskC; DispatcherTimer timer; bool active; int tick; long lastBytes = -1; DateTime lastNet;
        StackPanel left = new StackPanel(); TextBlock title, big, subline; GraphView graph = new GraphView { Big = true }; WrapPanel stats = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        public bool Active { get { return active; } set { active = value; if (value) { Init(); timer.Start(); Sample(); } else if (timer != null) timer.Stop(); } }

        public PerfView()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) }); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); ColumnDefinitions.Add(new ColumnDefinition());
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = left }; Children.Add(sv);
            Grid right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 }); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(right, 2); Children.Add(right);
            Grid hd = new Grid(); hd.ColumnDefinitions.Add(new ColumnDefinition()); hd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }; big = new TextBlock { FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 198)), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) }; Grid.SetColumn(big, 1); hd.Children.Add(title); hd.Children.Add(big); right.Children.Add(hd);
            StackPanel mid = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; subline = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 158)), Margin = new Thickness(0, 0, 0, 4) }; Grid.SetRow(mid, 1); mid.Children.Add(subline); graph.Height = 230; graph.HorizontalAlignment = HorizontalAlignment.Stretch; mid.Children.Add(graph); mid.Children.Add(new TextBlock { Text = Loc.T("60 másodperc"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 138)), Margin = new Thickness(0, 3, 0, 0) }); right.Children.Add(mid);
            Grid.SetRow(stats, 2); right.Children.Add(stats);
        }
        void Init()
        {
            if (timer != null) return;
            ms.Add(new Metric { Name = Loc.T("Processzor"), C = Color.FromRgb(17, 125, 187) }); ms.Add(new Metric { Name = Loc.T("Memória"), C = Color.FromRgb(139, 18, 174) }); ms.Add(new Metric { Name = Loc.T("Lemez"), C = Color.FromRgb(76, 176, 36) }); ms.Add(new Metric { Name = Loc.T("Hálózat"), C = Color.FromRgb(204, 120, 30), Auto = true, Max = 100, Unit = " KB/s" }); ms.Add(new Metric { Name = "GPU", C = Color.FromRgb(60, 140, 200) });
            foreach (Metric m in ms) { Metric mm = m; m.Mini.C = m.C; m.Mini.Width = 62; m.Mini.Height = 44; m.SubTxt = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 178)), TextTrimming = TextTrimming.CharacterEllipsis };
                StackPanel tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; tx.Children.Add(new TextBlock { Text = m.Name, FontSize = 14, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }); tx.Children.Add(m.SubTxt);
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(m.Mini); row.Children.Add(tx);
                m.Card = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 4), Cursor = Cursors.Hand, Child = row, Background = Brushes.Transparent }; m.Card.MouseLeftButtonUp += delegate { Select(mm); }; left.Children.Add(m.Card); }
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += delegate { if (IsVisible) Sample(); }; Select(ms[0]);
        }
        void Select(Metric m) { sel = m; foreach (Metric x in ms) x.Card.Background = x == m ? new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)) : Brushes.Transparent; graph.C = m.C; Render(); }

        static double Push(Metric m, double v) { for (int i = 0; i < m.H.Length - 1; i++) m.H[i] = m.H[i + 1]; m.H[m.H.Length - 1] = v; m.Cur = v; if (m.Auto) { double mx = 100; foreach (double d in m.H) mx = Math.Max(mx, d); m.Max = mx * 1.15; } return v; }
        void Sample()
        {
            tick++; MEMSTAT s = new MEMSTAT(); s.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT)); GlobalMemoryStatusEx(ref s);
            if (!sBusy) { sBusy = true; bool dg = tick % 2 == 0; System.Threading.ThreadPool.QueueUserWorkItem(delegate { BgSample(dg); }); }      // a számlálók lassúak: háttérben mérünk, itt az utolsó eredményt mutatjuk
            double cpu = sCpu, disk = sDisk;
            Push(ms[0], cpu); ms[0].Sub = cpu.ToString("0") + "%"; double used = (s.ullTotalPhys - s.ullAvailPhys) / 1073741824.0, tot = s.ullTotalPhys / 1073741824.0; Push(ms[1], s.dwMemoryLoad); ms[1].Sub = used.ToString("0.0") + "/" + tot.ToString("0.0") + " GB (" + s.dwMemoryLoad + "%)";
            Push(ms[2], disk); ms[2].Sub = disk.ToString("0") + "%";
            double kb = sKb;
            Push(ms[3], kb); ms[3].Sub = kb.ToString("0") + " KB/s";
            if (tick % 2 == 0) { double g = sGpu; Push(ms[4], Math.Max(0, g)); ms[4].Sub = g < 0 ? "n/a" : g.ToString("0") + "%"; }
            foreach (Metric m in ms) { m.Mini.V = m.H; m.Mini.Max = m.Max; m.Mini.InvalidateVisual(); m.SubTxt.Text = m.Sub; }
            Render();
        }
        volatile bool sBusy; volatile bool cInit; double sCpu, sDisk, sKb, sGpu = -1; Dictionary<string, CounterSample> gpuPrev = new Dictionary<string, CounterSample>();
        void BgSample(bool doGpu)
        {
            try
            {
                if (!cInit) { cInit = true; try { cpuC = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpuC.NextValue(); } catch { cpuC = null; } try { diskC = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); diskC.NextValue(); } catch { diskC = null; } }
                try { if (cpuC != null) sCpu = Math.Min(100, cpuC.NextValue()); } catch { } try { if (diskC != null) sDisk = Math.Min(100, diskC.NextValue()); } catch { }
                try { long b = 0; foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) { if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue; IPInterfaceStatistics st = ni.GetIPStatistics(); b += st.BytesReceived + st.BytesSent; } DateTime n = DateTime.Now; if (lastBytes >= 0) sKb = Math.Max(0, (b - lastBytes) / 1024.0 / Math.Max(0.2, (n - lastNet).TotalSeconds)); lastBytes = b; lastNet = n; } catch { }
                if (doGpu) sGpu = Gpu();
            }
            catch { }
            finally { sBusy = false; }
        }
        // egyetlen kategória-olvasás mintánként (nem 60 külön számláló), a kilépett folyamatok példányai maguktól eltűnnek
        double Gpu()
        {
            try
            {
                InstanceDataCollection col = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"]; if (col == null) return -1;
                double sum = 0; Dictionary<string, CounterSample> now = new Dictionary<string, CounterSample>();
                foreach (InstanceData d in col.Values) { if (d.InstanceName.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue; now[d.InstanceName] = d.Sample; CounterSample pv; if (gpuPrev.TryGetValue(d.InstanceName, out pv)) sum += CounterSample.Calculate(pv, d.Sample); }
                gpuPrev = now; return Math.Min(100, sum);
            }
            catch { return -1; }
        }
        static string Gb(double bytes) { return (bytes / 1073741824.0).ToString("0.0") + " GB"; }
        void Stat(string label, string val) { StackPanel sp = new StackPanel { Margin = new Thickness(0, 0, 34, 10), MinWidth = 120 }; sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 158)) }); sp.Children.Add(new TextBlock { Text = val, FontSize = 19, Foreground = Brushes.White }); stats.Children.Add(sp); }
        void Render()
        {
            if (sel == null) return; title.Text = sel.Name; graph.V = sel.H; graph.Max = sel.Max; graph.InvalidateVisual(); stats.Children.Clear();
            PERF p = new PERF(); p.cb = (uint)Marshal.SizeOf(typeof(PERF)); bool ok = GetPerformanceInfo(out p, p.cb); MEMSTAT s = new MEMSTAT(); s.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT)); GlobalMemoryStatusEx(ref s); double ps = ok ? (double)(ulong)p.PageSize : 4096;
            TimeSpan up = TimeSpan.FromMilliseconds(GetTickCount64()); string upS = (int)up.TotalDays + ":" + up.Hours.ToString("00") + ":" + up.Minutes.ToString("00") + ":" + up.Seconds.ToString("00");
            if (sel == ms[0]) { big.Text = Environment.ProcessorCount + Loc.T(" logikai processzor"); subline.Text = Loc.T("% kihasználtság az elmúlt 60 másodpercben"); Stat(Loc.T("Kihasználtság"), sel.Cur.ToString("0") + "%"); if (ok) { Stat(Loc.T("Folyamatok"), p.Processes.ToString()); Stat(Loc.T("Szálak"), p.Threads.ToString()); Stat(Loc.T("Kezelők"), p.Handles.ToString()); } Stat(Loc.T("Működési idő"), upS); }
            else if (sel == ms[1]) { big.Text = Gb(s.ullTotalPhys); subline.Text = Loc.T("Memóriahasználat"); Stat(Loc.T("Használatban"), Gb(s.ullTotalPhys - s.ullAvailPhys)); Stat(Loc.T("Elérhető"), Gb(s.ullAvailPhys)); if (ok) { Stat(Loc.T("Lefoglalt"), Gb((double)(ulong)p.CommitTotal * ps) + " / " + Gb((double)(ulong)p.CommitLimit * ps)); Stat(Loc.T("Gyorsítótárazott"), Gb((double)(ulong)p.SystemCache * ps)); Stat(Loc.T("Kernel (lapozott / nem)"), Gb((double)(ulong)p.KernelPaged * ps) + " / " + Gb((double)(ulong)p.KernelNonpaged * ps)); } }
            else if (sel == ms[2]) { big.Text = Loc.T("Aktív idő"); subline.Text = Loc.T("Lemez-aktivitás (% idő)"); Stat(Loc.T("Aktív idő"), sel.Cur.ToString("0") + "%"); try { foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && d.IsReady) Stat(d.Name, Gb(d.AvailableFreeSpace) + Loc.T(" szabad / ") + Gb(d.TotalSize)); } catch { } }
            else if (sel == ms[3]) { big.Text = sel.Cur.ToString("0") + " KB/s"; subline.Text = Loc.T("Hálózati forgalom (küldés + fogadás), skála: ") + sel.Max.ToString("0") + " KB/s"; try { foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback) Stat(ni.Name, ni.Speed > 0 ? (ni.Speed / 1000000) + " Mb/s" : "–"); } catch { } }
            else { big.Text = sel.Cur > 0 || sel.Sub != "n/a" ? Loc.T("3D motor") : Loc.T("nem elérhető"); subline.Text = Loc.T("GPU használat (3D motorok összege)"); Stat(Loc.T("Használat"), sel.Sub == "n/a" ? Loc.T("nem mérhető") : sel.Cur.ToString("0") + "%"); }
        }
    }

    // ------------------------------------------------------------------ indítás
    class StartupView : Grid
    {
        StackPanel list = new StackPanel(); TextBlock info;
        public StartupView()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
            info = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 158)), Margin = new Thickness(2, 0, 0, 8), Text = Loc.T("Az automatikusan induló programok. A letiltás visszafordítható (a Windows saját Indítás-kapcsolóját állítja, mint a gyári Feladatkezelő).") }; Children.Add(info);
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }; Grid.SetRow(sv, 1); Children.Add(sv);
        }
        class Entry { public string Name, Cmd, Source, ApprovedKey, ApprovedName; public bool Enabled = true; }
        static bool IsDisabled(string key, string name) { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + key)) { byte[] b = k != null ? k.GetValue(name) as byte[] : null; return b != null && b.Length > 0 && (b[0] & 1) == 1; } } catch { return false; } }
        static void SetState(string key, string name, bool enabled)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + key)) { byte[] b = new byte[12]; b[0] = (byte)(enabled ? 2 : 3); if (!enabled) { long ft = DateTime.UtcNow.ToFileTimeUtc(); Array.Copy(BitConverter.GetBytes(ft), 0, b, 4, 8); } k.SetValue(name, b, RegistryValueKind.Binary); }
        }
        public void Reload()
        {
            list.Children.Clear(); List<Entry> es = new List<Entry>();
            foreach (KeyValuePair<RegistryKey, string> hv in new KeyValuePair<RegistryKey, string>[] { new KeyValuePair<RegistryKey, string>(Registry.CurrentUser, Loc.T("Felhasználó")), new KeyValuePair<RegistryKey, string>(Registry.LocalMachine, Loc.T("Rendszer")) })
            {
                try { using (RegistryKey k = hv.Key.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) if (k != null) foreach (string n in k.GetValueNames()) if (n.Length > 0) es.Add(new Entry { Name = n, Cmd = Convert.ToString(k.GetValue(n)), Source = hv.Value + " · Run", ApprovedKey = "Run", ApprovedName = n, Enabled = !IsDisabled("Run", n) }); } catch { }
            }
            foreach (string sd in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
                try { if (Directory.Exists(sd)) foreach (string f in Directory.GetFiles(sd)) { if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue; es.Add(new Entry { Name = Path.GetFileNameWithoutExtension(f), Cmd = f, Source = Loc.T("Indítópult"), ApprovedKey = "StartupFolder", ApprovedName = Path.GetFileName(f), Enabled = !IsDisabled("StartupFolder", Path.GetFileName(f)) }); } } catch { }
            es.Sort(delegate (Entry a, Entry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); }); info.Text = es.Count + Loc.T(" induló elem · a letiltás visszafordítható (a Windows saját Indítás-kapcsolóját állítja)");
            foreach (Entry e in es)
            {
                Entry en = e; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel tx = new StackPanel(); tx.Children.Add(new TextBlock { Text = e.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }); tx.Children.Add(new TextBlock { Text = e.Source + " · " + e.Cmd, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 158)), TextTrimming = TextTrimming.CharacterEllipsis }); g.Children.Add(tx);
                TextBlock st = new TextBlock { Text = e.Enabled ? Loc.T("Engedélyezve") : Loc.T("Letiltva"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = e.Enabled ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromRgb(150, 150, 158)) };
                Border sw = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Cursor = Cursors.Hand, Child = st, ToolTip = Loc.T("Kattints a váltáshoz") }; Grid.SetColumn(sw, 1); g.Children.Add(sw);
                sw.MouseLeftButtonUp += delegate { try { en.Enabled = !en.Enabled; SetState(en.ApprovedKey, en.ApprovedName, en.Enabled); st.Text = en.Enabled ? Loc.T("Engedélyezve") : Loc.T("Letiltva"); st.Foreground = en.Enabled ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromRgb(150, 150, 158)); } catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Indítás")); en.Enabled = !en.Enabled; } };
                list.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), Child = g });
            }
            if (es.Count == 0) list.Children.Add(new TextBlock { Text = Loc.T("Nincs automatikusan induló elem."), Foreground = Brushes.Gray, Margin = new Thickness(6) });
        }
    }
}
