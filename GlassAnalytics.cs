// GlassControl – Analitika fül: folyamatosan méri, ki mennyi memóriát használ, mikor és miért; átlagot, csúcsot, trendet számol.
// A mérés a GlassControl futása alatt 5 mp-enként megy (háttérszálon), percenként naplózódik, így a hosszú távú átlag is megvan.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GlassTerm;

namespace GlassControlWpf
{
    public class GroupStat
    {
        public string Name, Path = ""; public double Last, Priv, Avg, Max; public long Present; public DateTime MaxAt, FirstSeen; public int Procs, MaxProcs; public double TrendPerHour; public bool HasTrend;
        public List<KeyValuePair<DateTime, double>> Recent = new List<KeyValuePair<DateTime, double>>();
    }
    public class SysSample { public DateTime T; public double Pct, UsedGb, AvailGb, TotalGb; }
    public class WiperEvent { public DateTime T; public int Count; public double FreedMb, BeforeGb, AfterGb; }

    public class AnalyticsPanel : ScrollViewer
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MEMSTAT { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalMemoryStatusEx(ref MEMSTAT s);

        const int IntervalSec = 5, RecentMax = 720, SysMax = 17280;     // 5 mp-es mérés; csoportonként 1 óra, rendszerből 24 óra marad a memóriában
        readonly object sync = new object();
        Dictionary<string, GroupStat> groups = new Dictionary<string, GroupStat>(); List<SysSample> sys = new List<SysSample>(); List<WiperEvent> wipes = new List<WiperEvent>();
        long samples; DateTime started = DateTime.Now, lastLog = DateTime.MinValue; string dir, logFile, wipeFile;
        double[] histSum = new double[24]; long[] histN = new long[24]; double histAll; long histCount; Dictionary<string, double[]> histApps = new Dictionary<string, double[]>();   // hosszú távú (a naplóból)
        DispatcherTimer timer = new DispatcherTimer(); bool busy, visibleNow; int rangeMin = 10; string sortBy = "last", selected;
        WrapPanel cards = new WrapPanel(); StackPanel tableBox = new StackPanel(), explain = new StackPanel(), hourBox = new StackPanel(), wipeBox = new StackPanel(); Canvas chart = new Canvas { Height = 170, ClipToBounds = true }; TextBlock chartNote = new TextBlock();
        static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(156, 163, 175)), CardBg = new SolidColorBrush(Color.FromArgb(150, 54, 54, 58)), CardBd = new SolidColorBrush(Color.FromArgb(110, 92, 92, 98)), Blue = new SolidColorBrush(Color.FromRgb(10, 132, 255)), Green = new SolidColorBrush(Color.FromRgb(48, 209, 88)), Orange = new SolidColorBrush(Color.FromRgb(255, 159, 10)), Red = new SolidColorBrush(Color.FromRgb(255, 69, 58));
        static readonly Dictionary<string, string[]> Info = BuildInfo();

        public event Action<double> Sampled;
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool inh, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll", SetLastError = true)] static extern bool EmptyWorkingSet(IntPtr h);
        public AnalyticsStore Store; Dictionary<string, int> fgCount = new Dictionary<string, int>(); LearnBox learnBox;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        public AnalyticsPanel(string appDir, AnalyticsStore store)
        {
            dir = appDir; Store = store; logFile = store.RawFile; wipeFile = store.WipeFile;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Focusable = false;
            LoadHistory(); BuildUi();
            ThreadPool.QueueUserWorkItem(delegate { try { Store.TrimRaw(); Store.Maintain(); Store.ReloadPolicy(); } catch { } });
            DispatcherTimer mt = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) }; mt.Tick += delegate { ThreadPool.QueueUserWorkItem(delegate { try { Store.Maintain(); Store.ReloadPolicy(); } catch { } }); }; mt.Start();
            timer.Interval = TimeSpan.FromSeconds(IntervalSec); timer.Tick += delegate { Kick(); }; timer.Start(); Kick();
            IsVisibleChanged += delegate { visibleNow = IsVisible; if (visibleNow) Redraw(); };
            SizeChanged += delegate { if (visibleNow) DrawChart(); };
        }

        // ---------------------------------------------------------------- mérés
        void Kick() { if (busy) return; busy = true; ThreadPool.QueueUserWorkItem(delegate { try { Sample(); } catch { } finally { busy = false; } }); }

        void Sample()
        {
            MEMSTAT m = new MEMSTAT(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT)); if (!GlobalMemoryStatusEx(ref m)) return; DateTime now = DateTime.Now;
            double total = m.ullTotalPhys / 1073741824.0, avail = m.ullAvailPhys / 1073741824.0;
            Dictionary<string, double[]> cur = new Dictionary<string, double[]>(); Dictionary<string, string> paths = new Dictionary<string, string>();
            uint fpid = 0; string fgName = ""; try { GetWindowThreadProcessId(GetForegroundWindow(), out fpid); } catch { }
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string n = p.ProcessName.ToLowerInvariant(); if (fpid != 0 && (uint)p.Id == fpid) fgName = n; double ws = p.WorkingSet64 / 1048576.0, pv = p.PrivateMemorySize64 / 1048576.0; double[] a; if (!cur.TryGetValue(n, out a)) { a = new double[3]; cur[n] = a; }
                    a[0] += ws; a[1] += pv; a[2] += 1;
                }
                catch { }
                finally { p.Dispose(); }
            }
            lock (sync)
            {
                if (fgName.Length > 0) { int fc; fgCount.TryGetValue(fgName, out fc); fgCount[fgName] = fc + 1; }
                SysSample s = new SysSample { T = now, TotalGb = total, AvailGb = avail, UsedGb = total - avail, Pct = (total - avail) / total * 100 }; sys.Add(s); if (sys.Count > SysMax) sys.RemoveAt(0); samples++;
                foreach (KeyValuePair<string, double[]> kv in cur)
                {
                    GroupStat g; if (!groups.TryGetValue(kv.Key, out g)) { g = new GroupStat { Name = kv.Key, FirstSeen = now }; groups[kv.Key] = g; }
                    g.Present++; g.Last = kv.Value[0]; g.Priv = kv.Value[1]; g.Procs = (int)kv.Value[2]; g.Avg += (g.Last - g.Avg) / g.Present; if (g.Last >= g.Max) { g.Max = g.Last; g.MaxAt = now; } if (g.Procs > g.MaxProcs) g.MaxProcs = g.Procs;
                    g.Recent.Add(new KeyValuePair<DateTime, double>(now, g.Last)); if (g.Recent.Count > RecentMax) g.Recent.RemoveAt(0); Trend(g);
                }
                foreach (GroupStat g in groups.Values) if (!cur.ContainsKey(g.Name)) { g.Last = 0; g.Procs = 0; }
                if (samples % 120 == 0) { List<string> dead = new List<string>(); foreach (GroupStat g in groups.Values) if (g.Last == 0 && g.Recent.Count > 0 && (now - g.Recent[g.Recent.Count - 1].Key).TotalHours > 2) dead.Add(g.Name); foreach (string k in dead) groups.Remove(k); }
                if ((now - lastLog).TotalSeconds >= 60) { lastLog = now; LogMinute(s); }
            }
            double pctNow = (total - avail) / total * 100;
            Dispatcher.BeginInvoke(new Action(delegate { if (Sampled != null) Sampled(pctNow); if (visibleNow && Shown()) Redraw(); }));
        }

        static void Trend(GroupStat g)
        {
            int n = g.Recent.Count; if (n < 60) { g.HasTrend = false; return; }                // legalább 5 perc
            int k = Math.Max(6, n / 5); double a = 0, b = 0; for (int i = 0; i < k; i++) { a += g.Recent[i].Value; b += g.Recent[n - 1 - i].Value; } a /= k; b /= k;
            double hours = (g.Recent[n - 1].Key - g.Recent[0].Key).TotalHours; if (hours < 0.05) { g.HasTrend = false; return; } g.TrendPerHour = (b - a) / (hours * 0.8); g.HasTrend = true;
        }

        void LogMinute(SysSample s)
        {
            try
            {
                List<GroupStat> top = new List<GroupStat>(groups.Values); top.Sort(delegate (GroupStat x, GroupStat y) { return y.Last.CompareTo(x.Last); }); StringBuilder sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 12; i++) { if (top[i].Last < 20) break; if (sb.Length > 0) sb.Append('|'); sb.Append(AnalyticsStore.Clean(top[i].Name)).Append(':').Append(top[i].Last.ToString("0", CultureInfo.InvariantCulture)); }
                string fg = ""; int best = 0; foreach (KeyValuePair<string, int> kv in fgCount) if (kv.Value > best) { best = kv.Value; fg = kv.Key; } fgCount.Clear(); fg = AnalyticsStore.Clean(fg);
                DateTime lt = s.T; double lp = s.Pct, lu = s.UsedGb, ltot = s.TotalGb; string ltop = sb.ToString(); string lfg = fg;
                ThreadPool.QueueUserWorkItem(delegate { Store.AppendMinute(lt, lp, lu, ltot, lfg, ltop); });
                histSum[s.T.Hour] += s.Pct; histN[s.T.Hour]++; histAll += s.Pct; histCount++;
            }
            catch { }
        }

        // előrejelzés: az elmúlt 30 perc memóriahasználatára illesztett egyenes alapján
        public string Forecast()
        {
            lock (sync)
            {
                int n = sys.Count; DateTime t0 = DateTime.Now.AddMinutes(-30); double sx = 0, sy = 0, sxx = 0, sxy = 0; int k = 0;
                for (int i = n - 1; i >= 0 && sys[i].T >= t0; i--) { double x = (sys[i].T - t0).TotalMinutes, y = sys[i].Pct; sx += x; sy += y; sxx += x * x; sxy += x * y; k++; }
                if (k < 24) return Loc.T("🔮 Előrejelzés: adatot gyűjtök…");
                double den = k * sxx - sx * sx; if (Math.Abs(den) < 1e-9) return Loc.T("🔮 Előrejelzés: stabil"); double slope = (k * sxy - sx * sy) / den, cur = sys[n - 1].Pct;
                if (slope > 0.05 && cur < 95) { double mins = (95 - cur) / slope; return Loc.T("🔮 Emelkedik (+") + slope.ToString("0.0", CultureInfo.InvariantCulture) + Loc.T("%/perc): kb. ") + (mins > 180 ? Loc.T("3+ óra") : Math.Round(mins) + Loc.T(" perc")) + Loc.T(" múlva eléri a 95%-ot"); }
                return slope < -0.05 ? Loc.T("🔮 A memóriahasználat csökken (") + slope.ToString("0.0", CultureInfo.InvariantCulture) + Loc.T("%/perc)") : Loc.T("🔮 A memóriahasználat stabil (") + cur.ToString("0") + "%)";
            }
        }
        bool Shown() { try { Window w = Window.GetWindow(this); return w != null && w.IsVisible && w.WindowState != WindowState.Minimized; } catch { return true; } }
        void LoadHistory()
        {
            try
            {
                if (File.Exists(logFile))
                {
                    DateTime cut = DateTime.Now.AddDays(-14);
                    string cutS = cut.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    foreach (string l in ShareRead.Lines(logFile))
                    {
                        if (l.Length < 16 || string.CompareOrdinal(l, 0, cutS, 0, 16) < 0) continue;
                        string[] p = l.Split(','); DateTime t; if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (t < cut) continue; double pct; if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out pct)) continue;
                        histSum[t.Hour] += pct; histN[t.Hour]++; histAll += pct; histCount++;
                        foreach (string part in p[4].Split('|')) { int c = part.LastIndexOf(':'); double mb; if (c > 0 && double.TryParse(part.Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out mb)) { string nm = part.Substring(0, c); double[] a; if (!histApps.TryGetValue(nm, out a)) { a = new double[2]; histApps[nm] = a; } a[0] += mb; a[1]++; } }
                    }
                }
                if (File.Exists(wipeFile))
                    foreach (string l in File.ReadAllLines(wipeFile, Encoding.UTF8))
                    {
                        string[] p = l.Split(','); DateTime t; double f, b, a; int c; if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) || t < DateTime.Now.AddDays(-14)) continue;
                        if (int.TryParse(p[1], out c) && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out f) && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out b) && double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out a)) wipes.Add(new WiperEvent { T = t, Count = c, FreedMb = f, BeforeGb = b, AfterGb = a });
                    }
            }
            catch { }
        }

        // a Wise Memory Wiper hívja minden optimalizálás után
        public void RecordWiper(int count, double freedMb, double beforeGb, double afterGb, string topFreed)
        {
            WiperEvent w = new WiperEvent { T = DateTime.Now, Count = count, FreedMb = freedMb, BeforeGb = beforeGb, AfterGb = afterGb };
            lock (sync) wipes.Add(w);
            string tf0 = topFreed ?? ""; System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { Store.AppendWipe(w.T, count, freedMb, beforeGb, afterGb, tf0); } catch { } });
            if (visibleNow) Redraw();
        }

        // ---------------------------------------------------------------- felület
        Border Card(string title, UIElement body, string sub)
        {
            StackPanel sp = new StackPanel(); Grid h = new Grid(); h.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13.5 });
            if (sub != null) h.Children.Add(new TextBlock { Text = sub, Foreground = Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(h); body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0)); sp.Children.Add(body);
            return new Border { Background = CardBg, BorderBrush = CardBd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = sp };
        }

        Border Chip(string text, bool on, Action click)
        {
            Border b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, Background = on ? (Brush)Blue : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) };
            b.Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold }; b.MouseLeftButtonUp += delegate { click(); }; return b;
        }

        void BuildUi()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            root.Children.Add(new TextBlock { Text = Loc.T("Folyamatos mérés 5 mp-enként, amíg a GlassControl nyitva van (percenként naplózva, 14 napra visszamenőleg). A Wise Memory Wiper és a Feladatkezelő adatai itt egyesülnek."), Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) });
            root.Children.Add(cards);
            Grid chartWrap = new Grid(); chartWrap.Children.Add(chart); StackPanel cp = new StackPanel(); cp.Children.Add(chartWrap); cp.Children.Add(chartNote); chartNote.Foreground = Muted; chartNote.FontSize = 11; chartNote.Margin = new Thickness(0, 6, 0, 0);
            chartCard = Card(Loc.T("Memóriahasználat az időben"), cp, null); root.Children.Add(chartCard);
            root.Children.Add(Card(Loc.T("Ki mennyit használ"), tableBox, Loc.T("kattints egy sorra a magyarázatért")));
            root.Children.Add(Card(Loc.T("Miért ennyi?"), explain, null));
            root.Children.Add(Card(Loc.T("Mikor terhelt a leginkább a gép? (napszak szerint, a naplóból)"), hourBox, null));
            root.Children.Add(Card(Loc.T("Wise Memory Wiper napló"), wipeBox, null));
            learnBox = new LearnBox(); root.Children.Add(Card(Loc.T("Tanult tudás (analytics/memory/memory.txt)"), learnBox.Body, Loc.T("naponta egyszer minden txt-ből frissül")));
            Content = root; GlassMenu.Attach(this, BgMenu);
        }
        Border chartCard;

        // ---- jobb klikkes menük
        MenuEntry[] BgMenu()
        {
            return new MenuEntry[] {
                GlassMenu.Menu(Loc.T("Rendezés"), GlassMenu.Check(Loc.T("Most használt"), sortBy == "last", delegate { sortBy = "last"; Redraw(); }), GlassMenu.Check(Loc.T("Átlag"), sortBy == "avg", delegate { sortBy = "avg"; Redraw(); }), GlassMenu.Check(Loc.T("Csúcs"), sortBy == "max", delegate { sortBy = "max"; Redraw(); }), GlassMenu.Check(Loc.T("Növekedés (trend)"), sortBy == "trend", delegate { sortBy = "trend"; Redraw(); })),
                GlassMenu.Menu(Loc.T("Grafikon időtáv"), GlassMenu.Check(Loc.T("10 perc"), rangeMin == 10, delegate { rangeMin = 10; Redraw(); }), GlassMenu.Check(Loc.T("1 óra"), rangeMin == 60, delegate { rangeMin = 60; Redraw(); }), GlassMenu.Check(Loc.T("Minden"), rangeMin == 0, delegate { rangeMin = 0; Redraw(); })),
                GlassMenu.Sep(),
                GlassMenu.Item(Loc.T("Tanulás most (minden txt beolvasása)"), delegate { ThreadPool.QueueUserWorkItem(delegate { try { Store.Maintain(); Store.Learn(); Store.ReloadPolicy(); } catch { } Dispatcher.BeginInvoke(new Action(delegate { Redraw(); })); }); }),
                GlassMenu.Item(Loc.T("Tanult tudás megnyitása (memory.txt)"), delegate { try { if (File.Exists(Store.MemoryFile)) Process.Start(new ProcessStartInfo(Store.MemoryFile) { UseShellExecute = true }); } catch { } }),
                GlassMenu.Item(Loc.T("Analitika mappa megnyitása"), delegate { try { Process.Start(new ProcessStartInfo("explorer.exe", Store.Root) { UseShellExecute = true }); } catch { } }),
                GlassMenu.Item(Loc.T("Frissítés"), delegate { Redraw(); })
            };
        }

        MenuEntry[] RowMenu(string name)
        {
            GroupStat g; lock (sync) { groups.TryGetValue(name, out g); } string path = g != null ? g.Path : "";
            return new MenuEntry[] {
                GlassMenu.Item(Loc.T("Magyarázat megjelenítése"), delegate { selected = name; Redraw(); }),
                GlassMenu.Item(Loc.T("RAM ürítés (az összes \"") + name + "\")", delegate { foreach (Process p in Process.GetProcessesByName(name)) { try { IntPtr h = OpenProcess(0x0100 | 0x1000, false, p.Id); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } catch { } p.Dispose(); } }),
                GlassMenu.Item(Loc.T("Program leállítása (összes példány)"), delegate
                {
                    if (Array.IndexOf(MainWindow.Protected, name.ToLowerInvariant()) >= 0) { MessageBox.Show(Loc.T("Ez egy védett rendszerfolyamat, nem állítom le."), Loc.T("Leállítás")); return; }
                    Process[] ps = Process.GetProcessesByName(name); if (ps.Length == 0) return;
                    bool ok = MessageBox.Show(Loc.T("Biztosan leállítod az összes \"") + name + Loc.T("\" folyamatot (") + ps.Length + Loc.T(" db)?\n\nA nem mentett munka elveszhet."), Loc.T("Leállítás"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes; foreach (Process p in ps) { try { if (ok) p.Kill(); } catch { } finally { p.Dispose(); } }
                }),
                GlassMenu.Sep(),
                !string.IsNullOrEmpty(path) && File.Exists(path) ? GlassMenu.Item(Loc.T("Útvonal megnyitása (Intéző)"), delegate { try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } }) : GlassMenu.Off(Loc.T("Útvonal megnyitása")),
                GlassMenu.Item(Loc.T("Összefoglaló másolása"), delegate { if (g != null) { try { Clipboard.SetText(Friendly(name) + Loc.T(": most ") + Mb(g.Last) + Loc.T(", átlag ") + Mb(g.Avg) + Loc.T(", csúcs ") + Mb(g.Max) + " (" + g.MaxAt.ToString("HH:mm") + "), " + g.Procs + Loc.T(" db folyamat")); } catch { } } })
            };
        }

        void Redraw() { try { DrawCards(); DrawChart(); DrawTable(); DrawExplain(); DrawHours(); DrawWipes(); learnBox.Draw(Store); } catch { } }

        class LearnBox
        {
            public StackPanel Body = new StackPanel();
            static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            public void Draw(AnalyticsStore st)
            {
                Body.Children.Clear(); MemModel m = st.Model; Func<string, Brush, double, TextBlock> tb = delegate (string t, Brush b, double sz) { return new TextBlock { Text = t, Foreground = b, FontSize = sz, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }; };
                if (m.Processed.Count == 0) { Body.Children.Add(tb(Loc.T("Még nincs lezárt 2 napos szakasz, ezért még nincs mit tanulni. Az első szakasz a mérés 3. napján zárul le; addig a percenkénti nyers napló (analytics/raw) gyűlik."), Muted, 12)); return; }
                Body.Children.Add(tb(st.Describe() + Loc.T(" · átlag ") + m.AvgPct.ToString("0.0") + Loc.T("% · csúcs ") + m.MaxPct.ToString("0") + "%", Brushes.White, 12.5));
                List<ProgProfile> l = new List<ProgProfile>(m.Progs.Values); l.Sort(delegate (ProgProfile x, ProgProfile y) { return (y.AvgMb * Math.Sqrt(y.PresentMin)).CompareTo(x.AvgMb * Math.Sqrt(x.PresentMin)); });
                for (int i = 0; i < l.Count && i < 8; i++)
                {
                    ProgProfile p = l[i]; string hrs = ""; double[] a = p.ActiveMin; List<int> hh = new List<int>(); for (int h = 0; h < 24; h++) if (a[h] > 0) hh.Add(h); hh.Sort(delegate (int x, int y) { return a[y].CompareTo(a[x]); }); for (int k = 0; k < hh.Count && k < 4; k++) hrs += (hrs.Length > 0 ? ", " : "") + hh[k] + Loc.T(" ó");
                    Body.Children.Add(tb(p.Name + Loc.T(": átlag ") + p.AvgMb.ToString("0") + " MB · " + p.Sessions + Loc.T(" indítás, átl. ") + p.AvgSessionMin.ToString("0") + Loc.T(" perc · előtérben ") + (p.FgShare * 100).ToString("0") + Loc.T("% · leggyakrabban aktív: ") + (hrs.Length > 0 ? hrs : "–") + (p.SweepCount > 0 ? Loc.T(" · seprés átl. ") + p.AvgFreedMb.ToString("0") + " MB" : ""), new SolidColorBrush(Color.FromRgb(210, 210, 216)), 11.5));
                }
                Body.Children.Add(tb(Loc.T("Okos seprés szabályai: először ezeket üríti: ") + (st.SweepFirst.Count > 0 ? string.Join(", ", st.SweepFirst) : "–") + Loc.T(" · kihagyja (kevés a nyereség): ") + (st.SkipLowGain.Count > 0 ? string.Join(", ", st.SkipLowGain) : "–") + Loc.T(" · gyakran használt (óvatosan): ") + (st.OftenInUse.Count > 0 ? string.Join(", ", st.OftenInUse) : "–"), Muted, 11.5));
            }
        }

        static string Gb(double g) { return g.ToString("0.0", CultureInfo.CurrentCulture) + " GB"; }
        static string Mb(double m) { return m >= 1024 ? (m / 1024).ToString("0.00", CultureInfo.CurrentCulture) + " GB" : m.ToString("0", CultureInfo.CurrentCulture) + " MB"; }

        Border Stat(string title, string big, string small, Brush accent)
        {
            StackPanel sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = title, Foreground = Muted, FontSize = 11 }); sp.Children.Add(new TextBlock { Text = big, Foreground = accent, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(new TextBlock { Text = small, Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 206)), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 170 });
            return new Border { Background = CardBg, BorderBrush = CardBd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 11), Margin = new Thickness(0, 0, 10, 12), MinWidth = 150, Child = sp };
        }

        void DrawCards()
        {
            cards.Children.Clear(); SysSample last; double avg = 0, max = 0; DateTime maxAt = DateTime.Now; double wsum = 0; int wn = 0;
            lock (sync)
            {
                if (sys.Count == 0) return; last = sys[sys.Count - 1]; foreach (SysSample s in sys) { avg += s.Pct; if (s.Pct >= max) { max = s.Pct; maxAt = s.T; } } avg /= sys.Count; foreach (WiperEvent w in wipes) { wsum += w.FreedMb; wn++; }
            }
            double longAvg = histCount > 0 ? histAll / histCount : avg; TimeSpan up = DateTime.Now - started;
            cards.Children.Add(Stat(Loc.T("Most"), last.Pct.ToString("0", CultureInfo.CurrentCulture) + " %", Gb(last.UsedGb) + Loc.T(" használatban, ") + Gb(last.AvailGb) + Loc.T(" elérhető (") + Gb(last.TotalGb) + Loc.T(" összesen)"), last.Pct > 85 ? Red : (last.Pct > 70 ? Orange : Green)));
            cards.Children.Add(Stat(Loc.T("Átlag (ez a munkamenet)"), avg.ToString("0.0", CultureInfo.CurrentCulture) + " %", Loc.T("hosszú távú átlag: ") + longAvg.ToString("0.0", CultureInfo.CurrentCulture) + " %", Blue));
            cards.Children.Add(Stat(Loc.T("Csúcs"), max.ToString("0", CultureInfo.CurrentCulture) + " %", Loc.T("ekkor: ") + maxAt.ToString("HH:mm:ss"), Orange));
            cards.Children.Add(Stat(Loc.T("Mérés"), samples + Loc.T(" minta"), Loc.T("fut: ") + (int)up.TotalHours + Loc.T(" ó ") + up.Minutes + " p", Brushes.White));
            cards.Children.Add(Stat("Wiper", wn + Loc.T(" tisztítás"), wn > 0 ? Loc.T("átlag ") + Mb(wsum / wn) + Loc.T(" felszabadítva") : Loc.T("még nem futott"), Green));
        }

        void DrawChart()
        {
            chart.Children.Clear(); double w = chart.ActualWidth > 100 ? chart.ActualWidth : Math.Max(400, ActualWidth - 80), h = chart.Height; List<SysSample> data = new List<SysSample>(); List<WiperEvent> ws = new List<WiperEvent>(); double avg = 0;
            lock (sync)
            {
                DateTime from = rangeMin > 0 ? DateTime.Now.AddMinutes(-rangeMin) : DateTime.MinValue; foreach (SysSample s in sys) if (s.T >= from) data.Add(s); foreach (WiperEvent e in wipes) if (e.T >= from) ws.Add(e);
                foreach (SysSample s in sys) avg += s.Pct; if (sys.Count > 0) avg /= sys.Count;
            }
            // tengelyek
            for (int i = 0; i <= 4; i++) { double y = 6 + (h - 24) * i / 4.0; Line l = new Line { X1 = 34, X2 = w, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 }; chart.Children.Add(l); TextBlock tb = new TextBlock { Text = (100 - i * 25) + "%", Foreground = Muted, FontSize = 10 }; Canvas.SetLeft(tb, 2); Canvas.SetTop(tb, y - 7); chart.Children.Add(tb); }
            if (data.Count < 2) { chartNote.Text = Loc.T("Gyűjtöm az adatokat…"); return; }
            if (data.Count > 1600) { int stride = data.Count / 1200; List<SysSample> dec = new List<SysSample>(); for (int i = 0; i < data.Count; i += stride) dec.Add(data[i]); dec.Add(data[data.Count - 1]); data = dec; }
            DateTime t0 = data[0].T, t1 = data[data.Count - 1].T; double span = Math.Max(1, (t1 - t0).TotalSeconds); Func<DateTime, double> X = delegate (DateTime t) { return 34 + (w - 40) * (t - t0).TotalSeconds / span; }; Func<double, double> Y = delegate (double p) { return 6 + (h - 24) * (100 - p) / 100.0; };
            Polygon area = new Polygon { Fill = new LinearGradientBrush(Color.FromArgb(90, 10, 132, 255), Color.FromArgb(0, 10, 132, 255), 90) }; area.Points.Add(new Point(X(t0), Y(0)));
            Polyline pl = new Polyline { Stroke = Blue, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (SysSample s in data) { Point p = new Point(X(s.T), Y(s.Pct)); pl.Points.Add(p); area.Points.Add(p); } area.Points.Add(new Point(X(t1), Y(0))); chart.Children.Add(area); chart.Children.Add(pl);
            Line al = new Line { X1 = 34, X2 = w, Y1 = Y(avg), Y2 = Y(avg), Stroke = Green, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 3 } }; chart.Children.Add(al); TextBlock at = new TextBlock { Text = Loc.T("átlag ") + avg.ToString("0", CultureInfo.CurrentCulture) + "%", Foreground = Green, FontSize = 10 }; Canvas.SetLeft(at, w - 62); Canvas.SetTop(at, Y(avg) - 14); chart.Children.Add(at);
            foreach (WiperEvent e in ws) { double x = X(e.T); Line wl = new Line { X1 = x, X2 = x, Y1 = 4, Y2 = h - 18, Stroke = Orange, StrokeThickness = 1.5 }; chart.Children.Add(wl); }
            TextBlock a0 = new TextBlock { Text = t0.ToString("HH:mm"), Foreground = Muted, FontSize = 10 }; Canvas.SetLeft(a0, 30); Canvas.SetTop(a0, h - 16); chart.Children.Add(a0); TextBlock a1 = new TextBlock { Text = t1.ToString("HH:mm"), Foreground = Muted, FontSize = 10 }; Canvas.SetLeft(a1, w - 34); Canvas.SetTop(a1, h - 16); chart.Children.Add(a1);
            chartNote.Text = Loc.T("Kék: használt memória % · zöld szaggatott: átlag · narancs függőleges: Wise Wiper tisztítás");
            // időtáv-választó a kártya fejlécében
            Grid hd = (Grid)((StackPanel)chartCard.Child).Children[0]; for (int i = hd.Children.Count - 1; i >= 1; i--) hd.Children.RemoveAt(i);
            StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; int[] rs = { 10, 60, 0 }; string[] rn = { Loc.T("10 perc"), Loc.T("1 óra"), Loc.T("Minden") };
            for (int i = 0; i < 3; i++) { int r = rs[i]; chips.Children.Add(Chip(rn[i], rangeMin == r, delegate { rangeMin = r; Redraw(); })); }
            hd.Children.Add(chips);
        }

        void SortBtn(Panel p, string key, string text) { p.Children.Add(Chip(text, sortBy == key, delegate { sortBy = key; Redraw(); })); }

        string Friendly(string n) { string[] i; return Info.TryGetValue(n, out i) ? i[0] : n; }

        void DrawTable()
        {
            tableBox.Children.Clear(); List<GroupStat> list; lock (sync) { list = new List<GroupStat>(groups.Values); }
            list.RemoveAll(delegate (GroupStat g) { return g.Last < 1 && g.Avg < 20; });
            Comparison<GroupStat> cmp = delegate (GroupStat x, GroupStat y) { return sortBy == "avg" ? y.Avg.CompareTo(x.Avg) : (sortBy == "max" ? y.Max.CompareTo(x.Max) : (sortBy == "trend" ? y.TrendPerHour.CompareTo(x.TrendPerHour) : y.Last.CompareTo(x.Last))); };
            list.Sort(cmp);
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) }; bar.Children.Add(new TextBlock { Text = Loc.T("Rendezés:"), Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            SortBtn(bar, "last", Loc.T("Most")); SortBtn(bar, "avg", Loc.T("Átlag")); SortBtn(bar, "max", Loc.T("Csúcs")); SortBtn(bar, "trend", Loc.T("Növekedés")); tableBox.Children.Add(bar);
            Grid head = Row(); string[] hs = { Loc.T("Program"), Loc.T("Most"), Loc.T("Átlag"), Loc.T("Csúcs"), Loc.T("Mikor"), "db", Loc.T("Trend / óra") }; for (int i = 0; i < hs.Length; i++) { TextBlock t = new TextBlock { Text = hs[i], Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right }; Grid.SetColumn(t, i); head.Children.Add(t); } tableBox.Children.Add(head);
            double top = list.Count > 0 ? Math.Max(1, list[0].Last) : 1; for (int i = 0; i < list.Count && i < 16; i++)
            {
                GroupStat g = list[i]; string nm = g.Name; Grid r = Row(); r.Background = selected == nm ? new SolidColorBrush(Color.FromArgb(70, 10, 132, 255)) : Brushes.Transparent; r.Cursor = Cursors.Hand; r.Margin = new Thickness(0, 1, 0, 1);
                Func<string, int, bool, Brush, TextBlock> cell = delegate (string text, int col, bool right, Brush fg) { TextBlock t = new TextBlock { Text = text, Foreground = fg, FontSize = 12, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(t, col); r.Children.Add(t); return t; };
                Grid nameCell = new Grid(); Rectangle fill = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(46, 10, 132, 255)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 22, RadiusX = 5, RadiusY = 5 }; nameCell.Children.Add(fill);
                double frac = g.Last / top; nameCell.SizeChanged += delegate { fill.Width = Math.Max(2, nameCell.ActualWidth * frac); };
                nameCell.Children.Add(new TextBlock { Text = "  " + Friendly(nm), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }); Grid.SetColumn(nameCell, 0); r.Children.Add(nameCell);
                cell(Mb(g.Last), 1, true, Brushes.White); cell(Mb(g.Avg), 2, true, new SolidColorBrush(Color.FromRgb(200, 200, 206))); cell(Mb(g.Max), 3, true, new SolidColorBrush(Color.FromRgb(200, 200, 206))); cell(g.MaxAt.ToString("HH:mm"), 4, true, Muted); cell(g.Procs > 0 ? g.Procs.ToString() : "–", 5, true, Muted);
                string tr = g.HasTrend ? ((g.TrendPerHour >= 0 ? "+" : "") + g.TrendPerHour.ToString("0", CultureInfo.CurrentCulture) + " MB") : "…"; cell(tr, 6, true, g.HasTrend && g.TrendPerHour > 150 ? (Brush)Red : (g.HasTrend && g.TrendPerHour > 40 ? Orange : Muted));
                r.MouseLeftButtonUp += delegate { selected = nm; Redraw(); }; GlassMenu.Attach(r, delegate { selected = nm; return RowMenu(nm); }); tableBox.Children.Add(r);
            }
        }
        Grid Row()
        {
            Grid g = new Grid { Height = 24 }; double[] w = { 3.2, 1.1, 1.1, 1.1, 0.9, 0.5, 1.3 }; for (int i = 0; i < w.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w[i], GridUnitType.Star) }); return g;
        }

        void DrawExplain()
        {
            explain.Children.Clear(); GroupStat g = null; lock (sync) { if (selected != null) groups.TryGetValue(selected, out g); if (g == null) { List<GroupStat> l = new List<GroupStat>(groups.Values); l.Sort(delegate (GroupStat x, GroupStat y) { return y.Last.CompareTo(x.Last); }); if (l.Count > 0) { g = l[0]; } } }
            if (g == null) { explain.Children.Add(new TextBlock { Text = Loc.T("Még nincs adat."), Foreground = Muted }); return; }
            string[] inf; Info.TryGetValue(g.Name, out inf); string path = ""; try { if (string.IsNullOrEmpty(g.Path)) { Process[] ps = Process.GetProcessesByName(g.Name); foreach (Process p in ps) { try { if (g.Path.Length == 0) g.Path = p.MainModule.FileName; } catch { } p.Dispose(); } if (g.Path.Length == 0) g.Path = Loc.T("(nincs hozzáférés)"); } path = g.Path; } catch { }
            explain.Children.Add(new TextBlock { Text = (inf != null ? inf[0] : g.Name) + "  ·  " + Mb(g.Last) + Loc.T(" most"), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold });
            StringBuilder sb = new StringBuilder();
            sb.Append("Mi ez: ").Append(inf != null ? inf[1] : GenericWhat(g, path)).Append("\n\n");
            sb.Append(Loc.T("Mikor és mennyi: a mérés kezdete (")).Append(g.FirstSeen.ToString("HH:mm")).Append(Loc.T(") óta ")).Append(g.Present).Append(Loc.T(" mintában futott; átlagosan ")).Append(Mb(g.Avg)).Append(Loc.T(" RAM-ot használt, a csúcs ")).Append(Mb(g.Max)).Append(" volt (").Append(g.MaxAt.ToString("HH:mm:ss")).Append("). ");
            if (g.MaxProcs > 1) sb.Append(Loc.T("Egyszerre legfeljebb ")).Append(g.MaxProcs).Append(Loc.T(" folyamatban futott. "));
            sb.Append(Loc.T("Ebből privát (csak az övé) most ")).Append(Mb(g.Priv)).Append(".\n\n");
            if (g.HasTrend) sb.Append(Loc.T("Trend: ")).Append(g.TrendPerHour > 150 ? Loc.T("gyorsan nő (") : (g.TrendPerHour > 40 ? Loc.T("lassan nő (") : (g.TrendPerHour < -40 ? Loc.T("csökken (") : Loc.T("stabil (")))).Append(g.TrendPerHour >= 0 ? "+" : "").Append(g.TrendPerHour.ToString("0", CultureInfo.CurrentCulture)).Append(Loc.T(" MB/óra). ")).Append(g.TrendPerHour > 150 ? Loc.T("Ha tovább nő, memóriaszivárgás gyanús: érdemes újraindítani.") : "").Append("\n\n");
            double[] ha; if (histApps.TryGetValue(g.Name, out ha) && ha[1] > 3) sb.Append(Loc.T("Hosszú távon (a naplóból) a nagy fogyasztók között átlag ")).Append(Mb(ha[0] / ha[1])).Append(" volt.\n\n");
            sb.Append(Loc.T("Tipp: ")).Append(inf != null ? inf[2] : (g.Procs > 8 ? Loc.T("sok al-folyamata van (böngésző/Electron jellegű): a megnyitott ablakok, lapok vagy bővítmények száma határozza meg a fogyasztást.") : Loc.T("ha sokat használ és nem kell, a Feladatkezelő fülön leállíthatod, vagy a RAM ürítés gombbal tömörítheted a munkakészletét.")));
            if (!string.IsNullOrEmpty(path)) sb.Append(Loc.T("\n\nÚtvonal: ")).Append(path);
            explain.Children.Add(new TextBlock { Text = sb.ToString(), Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 216)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 18 });
        }
        static string GenericWhat(GroupStat g, string path)
        {
            if (path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) return "Windows rendszerkomponens (" + g.Name + ").";
            if (path.Length > 0 && path != Loc.T("(nincs hozzáférés)")) return Loc.T("telepített program (") + g.Name + ").";
            return Loc.T("védett vagy rendszerfolyamat, a Windows a futtatásához szükséges (") + g.Name + ").";
        }

        void DrawHours()
        {
            hourBox.Children.Clear(); double[] avg = new double[24]; double best = 0; int bestH = -1; double mx = 1;
            for (int i = 0; i < 24; i++) { avg[i] = histN[i] > 0 ? histSum[i] / histN[i] : 0; if (avg[i] > best) { best = avg[i]; bestH = i; } mx = Math.Max(mx, avg[i]); }
            if (bestH < 0) { hourBox.Children.Add(new TextBlock { Text = Loc.T("A napszak-statisztikához legalább néhány percnyi napló kell (percenként mentődik)."), Foreground = Muted, FontSize = 12 }); return; }
            Grid bars = new Grid { Height = 74 }; for (int i = 0; i < 24; i++) bars.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 24; i++)
            {
                StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0) }; double bh = 8 + 44 * avg[i] / 100.0; if (histN[i] == 0) bh = 3;
                Rectangle r = new Rectangle { Height = bh, RadiusX = 3, RadiusY = 3, Fill = histN[i] == 0 ? (Brush)new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : (i == DateTime.Now.Hour ? (Brush)Blue : (avg[i] > 80 ? Red : (avg[i] > 65 ? Orange : Green))), ToolTip = i + Loc.T(":00 – átlag ") + avg[i].ToString("0") + " %" };
                sp.Children.Add(r); sp.Children.Add(new TextBlock { Text = i.ToString(), Foreground = Muted, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center }); Grid.SetColumn(sp, i); bars.Children.Add(sp);
            }
            hourBox.Children.Add(bars);
            hourBox.Children.Add(new TextBlock { Text = Loc.T("Legterheltebb időszak: ") + bestH + ":00–" + (bestH + 1) + Loc.T(":00 (átlag ") + best.ToString("0") + Loc.T(" %). A kék oszlop a mostani óra."), Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) });
        }

        void DrawWipes()
        {
            wipeBox.Children.Clear(); List<WiperEvent> l; lock (sync) { l = new List<WiperEvent>(wipes); }
            if (l.Count == 0) { wipeBox.Children.Add(new TextBlock { Text = Loc.T("Még nem futott tisztítás. A Wise Memory Wiper fülön az „Optimalizálás” gombbal indíthatod; minden futás itt jelenik meg."), Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }); return; }
            double total = 0; foreach (WiperEvent e in l) total += e.FreedMb;
            wipeBox.Children.Add(new TextBlock { Text = l.Count + Loc.T(" tisztítás, összesen ") + Mb(total) + Loc.T(" felszabadítva (átlag ") + Mb(total / l.Count) + ").", Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            for (int i = l.Count - 1, n = 0; i >= 0 && n < 8; i--, n++) { WiperEvent e = l[i]; wipeBox.Children.Add(new TextBlock { Text = e.T.ToString("MM.dd HH:mm:ss") + "   " + e.Count + Loc.T(" folyamat   ") + Mb(e.FreedMb) + Loc.T(" felszabadítva   elérhető: ") + Gb(e.BeforeGb) + " → " + Gb(e.AfterGb), Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 206)), FontSize = 12, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 1, 0, 0) }); }
        }

        // ---------------------------------------------------------------- magyarázatok: név -> { megnevezés, mi ez, tipp }
        static Dictionary<string, string[]> BuildInfo()
        {
            Dictionary<string, string[]> d = new Dictionary<string, string[]>();
            d["brave"] = new[] { Loc.T("Brave böngésző"), Loc.T("böngésző: minden lap, bővítmény és háttérszolgáltatás külön folyamat, ezért sok db egyszerre."), Loc.T("Kapcsold be a Memory Savert (brave://settings/system): az inaktív lapok alvó állapotba kerülnek, de nem vesznek el. A felesleges bővítményeket is kapcsold ki.") };
            d["chrome"] = new[] { Loc.T("Chrome böngésző"), Loc.T("böngésző: lapok és bővítmények külön folyamatban."), Loc.T("Memory Saver mód és kevesebb nyitott lap segít.") };
            d["msedge"] = new[] { "Microsoft Edge", Loc.T("böngésző és a Windows beágyazott webes felületeinek motorja."), Loc.T("Az Edge háttérben is futhat; kikapcsolható a Startup boost.") };
            d["firefox"] = new[] { "Firefox", Loc.T("böngésző, lapok külön tartalom-folyamatban."), Loc.T("Kevesebb lap és bővítmény = kevesebb RAM.") };
            d["antigravity"] = new[] { Loc.T("Antigravity (szerkesztő)"), Loc.T("Electron-alapú fejlesztői környezet: főablak, bővítmény-gazda, nyelvi szerverek és a beágyazott terminálok külön folyamatok."), Loc.T("Minden megnyitott ablak és bővítmény külön fogyaszt: zárd be a régi ablakokat, kapcsold ki a használaton kívüli bővítményeket.") };
            d["code"] = new[] { "VS Code", Loc.T("Electron-alapú szerkesztő, bővítmények külön folyamatban."), Loc.T("Zárd be a használaton kívüli ablakokat és bővítményeket.") };
            d["claude"] = new[] { Loc.T("Claude (CLI munkamenet)"), Loc.T("egy Claude Code munkamenet; a memóriája a beszélgetés hosszával nő."), Loc.T("Hosszú munkamenetnél használd a /compact parancsot, vagy indíts újat; ne fusson 3 párhuzamosan, ha nem kell.") };
            d["language_server_windows_x64"] = new[] { Loc.T("Nyelvi szerver (Antigravity)"), Loc.T("a szerkesztő kódelemző háttérfolyamata (kiegészítés, hibák)."), Loc.T("Nagy projektnél több RAM-ot kér; a megnyitott mappa méretétől függ.") };
            d["node"] = new[] { "Node.js", Loc.T("JavaScript futtatókörnyezet: fejlesztői szerverek, eszközök."), Loc.T("Állítsd le a már nem használt fejlesztői szervereket.") };
            d["python"] = new[] { "Python", Loc.T("Python program vagy szkript."), Loc.T("Ha egy szkript szivárog, újraindítás segít.") };
            d["msmpeng"] = new[] { Loc.T("Windows Defender (víruskereső)"), Loc.T("valós idejű védelem és vizsgálat; vizsgálat közben több RAM-ot és CPU-t használ."), Loc.T("Ne kapcsold ki; a nagy projektmappákat és a fejlesztői gyorsítótárat kiveheted a vizsgálatból (kizárások).") };
            d["memory compression"] = new[] { Loc.T("Memória-tömörítés"), Loc.T("a Windows a ritkán használt memóriát tömöríti: RAM-ot spórol, ez nem szivárgás."), Loc.T("Normális; nagy értéknél a gép már sokat kényszerül tömöríteni: több RAM vagy kevesebb nyitott program segít.") };
            d["svchost"] = new[] { Loc.T("Windows szolgáltatások"), Loc.T("a Windows háttérszolgáltatásai, sok kis folyamatban."), Loc.T("Normális; egyenként kicsik.") };
            d["explorer"] = new[] { Loc.T("Windows Intéző (asztal, tálca)"), Loc.T("az asztal, a tálca és a fájlkezelő folyamata."), Loc.T("Sok megnyitott Intéző-ablak vagy nagy mappák növelik.") };
            d["dwm"] = new[] { Loc.T("Asztali ablakkezelő"), Loc.T("ablakok kirajzolása, átlátszóság, animációk (a Glass hatásokat is ez rajzolja)."), Loc.T("Normális; sok monitor és hatás növeli.") };
            d["searchhost"] = new[] { Loc.T("Windows keresés"), Loc.T("a Start/keresés felülete."), Loc.T("A keresési indexelés időnként növeli.") };
            d["searchapp"] = new[] { Loc.T("Windows keresés"), Loc.T("a Start/keresés felülete."), Loc.T("A keresési indexelés időnként növeli.") };
            d["steamwebhelper"] = new[] { Loc.T("Steam felület"), Loc.T("a Steam böngésző-alapú felülete."), Loc.T("Ha nem játszol, zárd be a Steamet.") };
            d["steam"] = new[] { "Steam", Loc.T("játékindító és kliens."), Loc.T("Ha nem játszol, zárd be.") };
            d["discord"] = new[] { "Discord", Loc.T("Electron-alapú csevegő."), Loc.T("Háttérben is fut; kilépéssel felszabadul.") };
            d["spotify"] = new[] { "Spotify", Loc.T("zenelejátszó (Chromium-alapú)."), Loc.T("Zárd be, ha nem használod.") };
            d["wallpaper32"] = new[] { "Wallpaper Engine", Loc.T("élő háttérkép-motor."), Loc.T("A bonyolult háttérképek több RAM-ot és GPU-t használnak.") };
            d["wallpaper64"] = new[] { "Wallpaper Engine", Loc.T("élő háttérkép-motor."), Loc.T("A bonyolult háttérképek több RAM-ot és GPU-t használnak.") };
            d["radeonsoftware"] = new[] { "AMD Radeon Software", Loc.T("videokártya-vezérlőpult."), Loc.T("Bezárható, a vezérlő háttérszolgáltatása marad.") };
            d["amdrsserv"] = new[] { Loc.T("AMD háttérszolgáltatás"), Loc.T("a Radeon Software háttérszolgáltatása."), Loc.T("Normális.") };
            d["wisememoryoptimzer"] = new[] { "Wise Memory Optimizer", Loc.T("memóriatisztító program."), Loc.T("Kis fogyasztású.") };
            d["glassdock"] = new[] { "GlassDock", Loc.T("a saját dokkod, tálca-pilulák, Start-menü, Spotlight."), Loc.T("Kicsi; a háttérkép-elmosásokat csak megnyitáskor számolja.") };
            d["glasscontrol"] = new[] { Loc.T("GlassControl (ez az app)"), Loc.T("a vezérlőközpont, benne az analitika és a terminál."), Loc.T("A mérés 5 mp-enként fut, a fogyasztása kicsi.") };
            d["terminal"] = new[] { Loc.T("Terminal (saját)"), Loc.T("a macOS-kinézetű terminál."), Loc.T("Kicsi; a görgetési előzmények (10 000 sor) nőhetnek.") };
            d["powershell"] = new[] { "PowerShell", "parancssor.", Loc.T("Hosszú kimenetek és nagy változók nőhetnek.") };
            d["conhost"] = new[] { Loc.T("Konzol-gazda"), Loc.T("a parancssori ablakok kiszolgálója."), Loc.T("Kicsi.") };
            d["onedrive"] = new[] { "OneDrive", Loc.T("felhő-szinkron."), Loc.T("Szinkron közben nő; szüneteltethető.") };
            d["registry"] = new[] { Loc.T("Rendszerleíró adatbázis"), Loc.T("a Windows beállításai a memóriában."), Loc.T("Normális, rendszer által kezelt.") };
            d["system"] = new[] { Loc.T("Windows rendszermag"), Loc.T("a Windows kernelje és meghajtói."), Loc.T("Rendszer által kezelt.") };
            return d;
        }
    }
}
