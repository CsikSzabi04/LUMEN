using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using GlassTerm;
using Microsoft.Win32;

namespace GlassControlWpf
{
    public class App : Application
    {
        static System.Threading.Mutex singleMutex;      // statikus: a GC ne szabadítsa fel (különben a második példány is elindulna)
        [STAThread]
        public static void Main()
        {
            try { System.Runtime.ProfileOptimization.SetProfileRoot(AppDomain.CurrentDomain.BaseDirectory); System.Runtime.ProfileOptimization.StartProfile("lumen.jit"); } catch { }      // többmagos JIT: a következő indítás gyorsabb
            try
            {
                // szoftveres WPF-rajzolás: a GPU-illesztő ~60 MB-os folyamaton belüli foglalása elmarad (a terminál-előnézet idejére átvált hardveresre)
                if (Environment.GetEnvironmentVariable("LUMEN_HW") != "1" && !File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwrender.txt"))) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                App app = new App(); GlassMenu.ApplyTheme();
                string memDump = Environment.GetEnvironmentVariable("LUMEN_MEMDUMP");
                if (memDump != null) { DispatcherTimer md = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) }; md.Tick += (s9, e9) => { md.Stop(); long mBefore = GC.GetTotalMemory(false); long mAfter = GC.GetTotalMemory(true); Process me = Process.GetCurrentProcess(); File.WriteAllText(memDump, "managed " + (mBefore >> 20) + Loc.T(" MB -> GC után ") + (mAfter >> 20) + " MB | WS " + (me.WorkingSet64 >> 20) + " MB | priv " + (me.PrivateMemorySize64 >> 20) + " MB | gen0/1/2 " + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2)); }; md.Start(); }
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                app.DispatcherUnhandledException += (s, e) => {
                    GlassControlWpf.MainWindow.LogCrash(baseDir, e.Exception);
                    e.Handled = true;                  // egy elromlott időzítő/esemény ne vigye el az egész (tálcán futó) alkalmazást
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => { GlassControlWpf.MainWindow.LogCrash(baseDir, e.ExceptionObject as Exception); };
                string[] ca0 = Environment.GetCommandLineArgs();
                if (Array.IndexOf(ca0, "/uninstall") >= 0) { Installer.Uninstall(); return; }
                Installer.EnsureInstalled(Array.IndexOf(ca0, "/install") >= 0);
                if (Array.IndexOf(ca0, "/install-only") >= 0) return;
                bool testMode = Array.IndexOf(ca0, "/shot") >= 0 || Array.IndexOf(ca0, "/menu") >= 0 || Array.IndexOf(ca0, "/adv") >= 0 || Array.IndexOf(ca0, "/devterm") >= 0 || Array.IndexOf(ca0, "/devprev") >= 0 || Array.IndexOf(ca0, "/autopsy") >= 0;
                bool createdNew = true; System.Threading.EventWaitHandle showEv = null;
                if (!testMode)
                {
                    singleMutex = new System.Threading.Mutex(true, "Lumen-GlassControl-single", out createdNew);
                    showEv = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Lumen-GlassControl-show");
                    if (!createdNew) { showEv.Set(); return; }          // már fut: azt hozzuk elő, nem indítunk másodikat (két mérés, két tálca ikon)
                }
                MainWindow mw = new MainWindow();
                if (!testMode) { try { System.Threading.EventWaitHandle sweepEv = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Lumen-GlassControl-sweep"); System.Threading.Thread sth = new System.Threading.Thread(() => { while (sweepEv.WaitOne()) { try { mw.Dispatcher.BeginInvoke(new Action(() => mw.SweepNow())); } catch { } } }) { IsBackground = true }; sth.Start(); GC.KeepAlive(sweepEv); } catch { } }
                if (showEv != null) { System.Threading.EventWaitHandle ev = showEv; System.Threading.Thread th = new System.Threading.Thread(() => { while (ev.WaitOne()) { try { mw.Dispatcher.BeginInvoke(new Action(() => mw.BringBack())); } catch { } } }); th.IsBackground = true; th.Start(); }
                string[] ca = Environment.GetCommandLineArgs(); string shot = null; int tab = -1, after = 3000;
                for (int i = 1; i < ca.Length; i++) { if (ca[i] == "/tab" && i + 1 < ca.Length) int.TryParse(ca[++i], out tab); else if (ca[i] == "/shot" && i + 1 < ca.Length) shot = ca[++i]; else if (ca[i] == "/after" && i + 1 < ca.Length) after = int.Parse(ca[++i]); }
                if (Array.IndexOf(ca, "/tray") >= 0) { mw.ShowInTaskbar = false; mw.WindowState = WindowState.Minimized; mw.Loaded += (s2, e2) => mw.Dispatcher.BeginInvoke(new Action(() => mw.HideToTrayExt()), DispatcherPriority.ApplicationIdle); }
                int mi = Array.IndexOf(ca, "/menu"); if (mi >= 0 && mi + 1 < ca.Length) { int which = int.Parse(ca[mi + 1]); mw.Loaded += (s2, e2) => { DispatcherTimer mt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) }; mt.Tick += (s3, e3) => { mt.Stop(); mw.TestMenu(which); }; mt.Start(); }; }
                int ai = Array.IndexOf(ca, "/adv"); if (ai >= 0 && ai + 1 < ca.Length) { double ah = double.Parse(ca[ai + 1]); mw.Loaded += (s2, e2) => mw.ShowAdvancedTest(ah); }
                if (tab >= 0) mw.Loaded += (s2, e2) => mw.SelectTabExt(tab);
                int hwi = Array.IndexOf(ca, "/hwtest"); if (hwi >= 0 && hwi + 1 < ca.Length) { string hwf = ca[hwi + 1]; mw.Loaded += (s2, e2) => { HardwareWindow hw0 = new HardwareWindow(); hw0.Show(); DispatcherTimer ht = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) }; ht.Tick += (s3, e3) => { ht.Stop(); hw0.SavePng(hwf); Application.Current.Shutdown(); }; ht.Start(); }; }
                int api = Array.IndexOf(ca, "/autopsy"); if (api >= 0 && api + 1 < ca.Length) { int apid; int.TryParse(ca[api + 1], out apid); mw.Loaded += (s2, e2) => { DispatcherTimer at = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) }; at.Tick += (s3, e3) => { at.Stop(); ProcAutopsy.Show(mw, apid, "test"); }; at.Start(); }; }
                int dpi2 = Array.IndexOf(ca, "/devprev"); if (dpi2 >= 0 && dpi2 + 1 < ca.Length) { string dpu = ca[dpi2 + 1]; mw.Loaded += (s2, e2) => { DispatcherTimer dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) }; dt.Tick += (s3, e3) => { dt.Stop(); mw.TestPreview(dpu); }; dt.Start(); }; }
                int dti = Array.IndexOf(ca, "/devterm"); if (dti >= 0 && dti + 1 < ca.Length) { string dtc = ca[dti + 1]; mw.Loaded += (s2, e2) => { DispatcherTimer dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) }; dt.Tick += (s3, e3) => { dt.Stop(); mw.TestRunInTerminal(dtc); }; dt.Start(); }; }
                if (shot != null) { DispatcherTimer st = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(after) }; st.Tick += (s2, e2) => { st.Stop(); try { if (shot.EndsWith(".pw.png")) mw.SaveShotPw(shot); else mw.SaveShot(shot); } catch (Exception ex) { File.WriteAllText("crash.log", ex.ToString()); } Application.Current.Shutdown(); }; st.Start(); }
                app.Run(mw);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
            }
        }
    }

    // egy fül tartalma csak az első megjelenéskor jön létre
    public class LazyHost : Decorator
    {
        Func<UIElement> make;
        public LazyHost(Func<UIElement> f) { make = f; IsVisibleChanged += delegate { if (IsVisible) Ensure(); }; }
        public bool Created { get { return make == null; } }
        public void Ensure()
        {
            if (make == null) return; Func<UIElement> m = make; make = null;
            try { Child = m(); } catch (Exception ex) { Child = new TextBlock { Text = Loc.T("A fül nem tölthető be: ") + ex.Message, Foreground = Brushes.OrangeRed, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap }; }
        }
    }

    public class ProcRow { public int PID { get; set; } public string Name { get; set; } public string RAM { get; set; } public string Path { get; set; } }
    public class DevRow { public string Name { get; set; } public string Status { get; set; } public string Cmd { get; set; } }

    public class MainWindow : Window
    {
        // Win32 API for Working Set Trimming
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // Win32 Acrylic Glass Blur API
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        // Color Palette (macOS Terminal Dark Charcoal Grey Glass + Dark Violet Accents)
        private readonly SolidColorBrush BrushGlassBg = new SolidColorBrush(Color.FromArgb(248, 30, 30, 30)); // macOS Terminal Dark Charcoal Glass #14151C
        private readonly SolidColorBrush BrushCardBg = new SolidColorBrush(Color.FromArgb(150, 54, 54, 58)); // Translucent Dark Grey Card #20222E
        private readonly SolidColorBrush BrushBorder = new SolidColorBrush(Color.FromArgb(120, 92, 92, 98)); // Soft Border #4B466E
        private readonly SolidColorBrush BrushVioletAccent = new SolidColorBrush(Color.FromRgb(10, 132, 255)); // #7C3AED Dark Violet
        private readonly SolidColorBrush BrushVioletHover = new SolidColorBrush(Color.FromRgb(64, 156, 255)); // #8B5CF6
        private readonly SolidColorBrush BrushCyan = new SolidColorBrush(Color.FromRgb(120, 214, 255)); // #38BDF8
        private readonly SolidColorBrush BrushEmerald = new SolidColorBrush(Color.FromRgb(48, 209, 88)); // #10B981
        private readonly SolidColorBrush BrushTabSel = new SolidColorBrush(Color.FromArgb(58, 255, 255, 255));
        private readonly SolidColorBrush BrushTextMuted = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // #9CA3AF

        // UI Controls
        private Border rootGlassBorder;
        private Grid mainGrid;
        private StackPanel tabNavPanel;
        private Grid contentPanel;
        private List<Button> tabButtons = new List<Button>();
        private List<UIElement> tabViews = new List<UIElement>();
        private System.Windows.Forms.NotifyIcon notifyIcon;

        // Wiper Controls
        private Canvas chartCanvas;
        private TextBlock txtRamPercentCenter;
        private TextBlock txtUsedValue;
        private TextBlock txtFreeValue;
        private TextBlock txtStatusText;
        private Button btnOptimizePill;

        // Process List Controls
        private ListView procListView;
        private TextBox txtProcSearch;

        private TextBlock headerTitle; private DateTime termStartedAt = DateTime.MinValue;
        private AnalyticsPanel analytics; private AnalyticsStore store;
        private SweepConfig cfg = new SweepConfig(); private DateTime lastSweep = DateTime.Now; private bool sweeping; private string lastReason = ""; private WiperAdvanced wiperAdv; private DispatcherTimer pinTimer; private string pinFile;
        private TextBlock autoLabel;
        private const double WM = 20;                 // az ablak körüli átlátszó sáv (itt van az árnyék és a méretező fogók)
        private Grid outerGrid, handlesGrid; private Border shadowBorder; private RectangleGeometry windowClip; private bool maxed; private Rect restoreRect;
        [StructLayout(LayoutKind.Sequential)] private struct PT2 { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out PT2 p);
        private PT2 rsCur; private Rect rsRect; private string rsDir; private UIElement rsEl;
        private int curTab = 0;
        private TermHost termHost; private bool termStarted; private int termQuick;
        private static readonly string[] tabNames = { Loc.T("memória"), Loc.T("folyamatok"), Loc.T("analitika"), "glass-ui", Loc.T("fejlesztés"), "terminal", Loc.T("súgó") };
        private string TitleFor(int tab) { return Environment.UserName.ToLower() + " \u2014 " + tabNames[tab] + " \u2014 " + (int)(this.Width - (maxed ? 0 : 2 * WM)) + "\u00D7" + (int)(this.Height - (maxed ? 0 : 2 * WM)); }

        // Terminal Controls
        private TextBox txtTerminalLog;
        private TextBox txtTerminalInput;

        // DevTools Controls

        private DispatcherTimer timer;
        private float currentRamPercent = 0;
        private double usedGb = 0;
        private double freeGb = 0;
        private double totalGb = 0;

        public MainWindow()
        {
            this.Title = Installer.AppName;
            Rect wa0 = SystemParameters.WorkArea;
            this.Width = Math.Min(980 + 2 * WM, wa0.Width); this.Height = Math.Min(700 + 2 * WM, wa0.Height);
            this.MinWidth = Math.Min(820 + 2 * WM, this.Width); this.MinHeight = Math.Min(580 + 2 * WM, this.Height);
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = Brushes.Transparent;
            this.FontFamily = new FontFamily("Inter, Segoe UI");
            ScrollTheme.Apply(this);
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;

            this.Loaded += MainWindow_Loaded;

            BuildNativeWindowLayout();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.Tick += (s, e) => {
                if (!this.IsVisible || this.WindowState == WindowState.Minimized) { SelfTrimWhenHidden(); return; } if (this.IsActive) lastActiveAt = DateTime.Now; else if ((DateTime.Now - lastActiveAt).TotalSeconds > 60) SelfTrimWhenHidden();
                if (this.IsActive) selfTrimmed = false;
                RefreshStats(); if (curTab == 0 && forecastTxt != null && analytics != null) forecastTxt.Text = analytics.Forecast();
                if (curTab == 0) DrawDonutChart();
                if (procListView != null && procListView.IsVisible && this.IsVisible && this.WindowState != WindowState.Minimized) RefreshProcesses();
            };
            timer.Start();
        }

        internal static void LogCrash(string baseDir, Exception ex)
        {
            try
            {
                string log = System.IO.Path.Combine(baseDir, "crash.log"); FileInfo fi = new FileInfo(log); if (fi.Exists && fi.Length > 500000) File.Delete(log);
                string msg = ex != null ? ex.Message : "?"; if (msg == lastCrashMsg && (DateTime.Now - lastCrashAt).TotalSeconds < 30) return; lastCrashMsg = msg; lastCrashAt = DateTime.Now;
                File.AppendAllText(log, DateTime.Now + " " + ex + Environment.NewLine);
            }
            catch { }
        }
        static string lastCrashMsg = ""; static DateTime lastCrashAt = DateTime.MinValue;
        // háttérben (tálcán) a saját munkakészlet is összehúzódik – az alkalmazás a memóriáról szól, ne ő legyen a nagy fogyasztó
        private bool selfTrimmed; private DateTime lastActiveAt = DateTime.Now, selfTrimAt;
        private void SelfTrimWhenHidden()
        {
            if (selfTrimmed && (DateTime.Now - selfTrimAt).TotalMinutes < 15) return; selfTrimmed = true; selfTrimAt = DateTime.Now;      // rejtve 15 percenként újra (a mintavétel visszanöveli)
            try { System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(2, GCCollectionMode.Forced, true, true); using (Process me = Process.GetCurrentProcess()) EmptyWorkingSet(me.Handle); } catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshStats();
            DrawDonutChart();
            SetupSystemTrayAndTaskbar();
        }

        private void SetupNativeDragHook()
        {
            try
            {
                var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
                var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(windowHelper.Handle);
                if (hwndSource != null)
                {
                    hwndSource.AddHook(new System.Windows.Interop.HwndSourceHook(WndProc));
                }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0084 && this.IsLoaded) // WM_NCHITTEST safely after window load
            {
                try
                {
                    int x = (short)(lParam.ToInt32() & 0xFFFF);
                    int y = (short)(lParam.ToInt32() >> 16);
                    Point p = this.PointFromScreen(new Point(x, y));

                    // Top 48px header area
                    if (p.Y >= 0 && p.Y <= 34 && p.X >= 0 && p.X <= this.ActualWidth)
                    {
                        // Allow normal clicks on top-left Mac dots (first 90px)
                        if (p.X <= 84)
                        {
                            return IntPtr.Zero; // HTCLIENT
                        }

                        handled = true;
                        return new IntPtr(2); // HTCAPTION -> Native Hardware Accelerated Dragging!
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (pinFile != null && System.IO.File.Exists(pinFile)) System.IO.File.Delete(pinFile); } catch { }
            try { if (termHost != null) termHost.Shutdown(); } catch { }
            try { if (FocusMode.On) FocusMode.Toggle(); } catch { }
            try { cfg.Save(); } catch { }
            if (notifyIcon != null)
            {
                try { notifyIcon.Visible = false; notifyIcon.Dispose(); } catch { }
                notifyIcon = null;
            }
            base.OnClosed(e);
        }

        private void SetupSystemTrayAndTaskbar()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                // 1. Create System Tray Notification Icon
                notifyIcon = new System.Windows.Forms.NotifyIcon();
                try
                {
                    notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
                catch
                {
                    notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
                notifyIcon.Text = Installer.AppName;
                notifyIcon.Visible = true;

                var menu = new System.Windows.Forms.ContextMenu();
                menu.MenuItems.Add(Loc.T("📂 Lumen megnyitása"), (s, ev) => {
                    this.Show();
                    this.ShowInTaskbar = true; this.WindowState = WindowState.Normal;
                    this.Activate();
                });
                menu.MenuItems.Add(Loc.T("🧹 Gyors Memóriatisztítás"), (s, ev) => PerformWiperSweep());
                menu.MenuItems.Add("-");
                menu.MenuItems.Add(Loc.T("❌ Kilépés"), (s, ev) => {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                    Application.Current.Shutdown();
                });

                notifyIcon.ContextMenu = menu;
                notifyIcon.DoubleClick += (s, ev) => {
                    this.Show();
                    this.ShowInTaskbar = true; this.WindowState = WindowState.Normal;
                    this.Activate();
                };

                // 2. Put Taskbar & Start Menu Shortcuts
                string psScript = string.Format(@"
                    $exePath = '{0}'
                    $taskbarPath = [System.IO.Path]::Combine($env:APPDATA, 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
                    if (Test-Path $taskbarPath) {{
                        $shortcutPath = [System.IO.Path]::Combine($taskbarPath, 'GlassControl Suite.lnk')
                        if (-not (Test-Path $shortcutPath)) {{
                            $wsh = New-Object -ComObject WScript.Shell
                            $s = $wsh.CreateShortcut($shortcutPath)
                            $s.TargetPath = $exePath
                            $s.WorkingDirectory = [System.IO.Path]::GetDirectoryName($exePath)
                            $s.Description = 'GlassControl Suite - macOS Dark Grey Glass App'
                            $s.Save()
                        }}
                    }}
                    $startMenuPath = [System.IO.Path]::Combine($env:APPDATA, 'Microsoft\Windows\Start Menu\Programs')
                    if (Test-Path $startMenuPath) {{
                        $shortcutPath2 = [System.IO.Path]::Combine($startMenuPath, 'GlassControl Suite.lnk')
                        if (-not (Test-Path $shortcutPath2)) {{
                            $wsh = New-Object -ComObject WScript.Shell
                            $s2 = $wsh.CreateShortcut($shortcutPath2)
                            $s2.TargetPath = $exePath
                            $s2.WorkingDirectory = [System.IO.Path]::GetDirectoryName($exePath)
                            $s2.Description = 'GlassControl Suite - macOS Dark Grey Glass App'
                            $s2.Save()
                        }}
                    }}
                ", exePath.Replace("'", "''"));

                if (Installer.JustInstalled) { try { notifyIcon.ShowBalloonTip(6000, Loc.T("Lumen telepítve"), Loc.T("Parancsikon az asztalon és a Start-menüben (keresd: Lumen)."), System.Windows.Forms.ToolTipIcon.Info); } catch { } }
            }
            catch { }
        }

        private void EnableAcrylicBlur()
        {
            try
            {
                var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
                
                // Translucent macOS Dark Charcoal Grey Acrylic Gradient: Alpha=0xC0 (192), R=20, G=21, B=28 (#14151C)
                int gradientColor = unchecked((int)0xB01E1E1E);

                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = gradientColor
                };

                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch { }
        }

        private void BuildNativeWindowLayout()
        {
            // Outer Glass Border with smooth 20px rounded geometry clipping
            RectangleGeometry windowClipGeometry = new RectangleGeometry { RadiusX = 16, RadiusY = 16, Rect = new Rect(0, 0, this.Width - 2 * WM, this.Height - 2 * WM) }; windowClip = windowClipGeometry;
            
            rootGlassBorder = new Border {
                CornerRadius = new CornerRadius(16),
                Background = BrushGlassBg,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                Clip = windowClipGeometry
            };
            rootGlassBorder.SizeChanged += (s, e) => { windowClipGeometry.Rect = new Rect(0, 0, rootGlassBorder.ActualWidth, rootGlassBorder.ActualHeight); };

            this.SizeChanged += (s, e) => {
                if (headerTitle != null && curTab != 5) headerTitle.Text = TitleFor(curTab);
            };

            mainGrid = new Grid { ClipToBounds = true };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); // Header (macOS Terminal style)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // Tab Nav
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // 1. macOS Window Header with 🔴 🟡 🟢 Colored Window Dots
            Grid headerGrid = new Grid { Background = new LinearGradientBrush(Color.FromArgb(250, 62, 62, 65), Color.FromArgb(250, 46, 46, 49), 90) };

            // macOS Colored Window Control Dots (Top-Left)
            StackPanel macDotsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
            macDotsPanel.MouseLeftButtonDown += (s, e) => { e.Handled = true; };

            Button dotClose = CreateMacDot(Color.FromRgb(255, 95, 86)); // 🔴 Red Close
            dotClose.Click += (s, e) => { if (cfg.CloseToTray) HideToTray(); else this.Close(); };

            Button dotMin = CreateMacDot(Color.FromRgb(254, 188, 46)); // 🟡 Yellow Minimize
            dotMin.Click += (s, e) => this.WindowState = WindowState.Minimized;

            Button dotMax = CreateMacDot(Color.FromRgb(40, 200, 64)); // 🟢 Green Expand
            dotMax.Click += (s, e) => ToggleMax();
            headerGrid.MouseLeftButtonDown += HeaderDown;

            macDotsPanel.Children.Add(dotClose);
            macDotsPanel.Children.Add(dotMin);
            macDotsPanel.Children.Add(dotMax);

            // Title Text in Header Center
            StackPanel titlePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            
            System.Windows.Shapes.Path folderIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,3.2 Q0,1.2 2,1.2 L5.2,1.2 L7,3 L14,3 Q16,3 16,5 L16,11 Q16,13 14,13 L2,13 Q0,13 0,11 Z"), Fill = new LinearGradientBrush(Color.FromRgb(104, 200, 255), Color.FromRgb(46, 150, 230), 90), Margin = new Thickness(0, 0, 7, 0), Width = 16, Height = 14 };
            headerTitle = new TextBlock { Text = TitleFor(0), Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 180)), FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            titlePanel.IsHitTestVisible = false;
            titlePanel.Children.Add(folderIcon);
            titlePanel.Children.Add(headerTitle);

            // Lumen márkajelzés a jobb oldalon: kis üveg-gyöngyszem + név
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), IsHitTestVisible = false };
            System.Windows.Shapes.Ellipse gem = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), Stroke = new LinearGradientBrush(Color.FromRgb(235, 245, 255), Color.FromRgb(90, 190, 255), 90), StrokeThickness = 2.4 };
            TextBlock brandTxt = new TextBlock { Text = "LUMEN", FontWeight = FontWeights.Bold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(130, 210, 255), 0), new GradientStop(Color.FromRgb(255, 255, 255), 0.55), new GradientStop(Color.FromRgb(120, 160, 255), 1) }, new Point(0, 0), new Point(1, 0)) };
            brand.Children.Add(gem); brand.Children.Add(brandTxt);
            headerGrid.Children.Add(macDotsPanel);
            headerGrid.Children.Add(brand);
            headerGrid.Children.Add(titlePanel);

            // 2. iOS Floating Glass Tab Bar
            Border tabBorder = new Border {
                Background = new SolidColorBrush(Color.FromArgb(70, 40, 40, 43)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(0, 1, 0, 1)
            };
            tabNavPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            string[] tabTitles = new string[] { "🧹 Wise Memory Wiper", Loc.T("📊 Feladatkezelő"), Loc.T("📈 Analitika"), "🍏 iOS Glass UI", Loc.T("🛠️ Fejlesztői Hub"), Loc.T("💻 Terminál"), Loc.T("📖 Súgó") };
            for (int i = 0; i < tabTitles.Length; i++)
            {
                int index = i;
                Button tabBtn = new Button {
                    Content = tabTitles[i],
                    Padding = new Thickness(14, 5, 14, 5),
                    Margin = new Thickness(3, 0, 3, 0),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (i == 0) ? Brushes.White : BrushTextMuted,
                    Background = (i == 0) ? BrushTabSel : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };

                ControlTemplate template = new ControlTemplate(typeof(Button));
                FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
                borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

                FrameworkElementFactory presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                borderFactory.AppendChild(presenterFactory);
                template.VisualTree = borderFactory;
                tabBtn.Template = template;

                tabBtn.Click += (s, e) => SelectTab(index);
                tabButtons.Add(tabBtn);
                tabNavPanel.Children.Add(tabBtn);
            }
            tabBorder.Child = tabNavPanel;

            // 3. Content Views
            contentPanel = new Grid { Margin = new Thickness(20) };

            UIElement viewWiper = CreateWiperView();
            UIElement viewProcs = CreateProcessesView();
            string appDir0 = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); store = new AnalyticsStore(appDir0); cfg.Load(System.IO.Path.Combine(store.Root, "autosweep.ini")); cfg.Changed += () => { UpdateAutoLabel(); if (wiperAdv != null) wiperAdv.UpdateStatus(); }; UpdateAutoLabel(); if (wiperAdv != null) wiperAdv.RefreshAll();
            pinFile = System.IO.Path.Combine(appDir0, "dock", "pin.txt"); pinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) }; pinTimer.Tick += (s9, e9) => PinTick(); pinTimer.Start(); StartupSweepTimer();
            analytics = new AnalyticsPanel(appDir0, store); analytics.Sampled += (pct) => CheckAuto(pct);
            // a nehéz fülek csak az első megnyitáskor épülnek fel (kevesebb memória és gyorsabb indulás)
            lazyGlass = new LazyHost(CreateGlassUIView); lazyHub = new LazyHost(CreateDevToolsView); lazyTerm = new LazyHost(CreateTerminalView);
            UIElement viewGlassUI = lazyGlass, viewDevTools = lazyHub, viewTerminal = lazyTerm;

            tabViews.Add(viewWiper);
            tabViews.Add(new TaskManagerHost(viewProcs));
            tabViews.Add(analytics);
            tabViews.Add(viewGlassUI);
            tabViews.Add(viewDevTools);
            tabViews.Add(viewTerminal);
            lazyHelp = new LazyHost(() => { helpVault = new HelpVault(appDir0); helpVault.Navigate = HelpNavigate; return helpVault; }); tabViews.Add(lazyHelp);

            foreach (var v in tabViews)
            {
                v.Visibility = Visibility.Collapsed;
                contentPanel.Children.Add(v);
            }
            tabViews[0].Visibility = Visibility.Visible;

            // Assemble Layout
            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(tabBorder, 1);
            Grid.SetRow(contentPanel, 2);

            tabBorder.MouseLeftButtonDown += HeaderDown; GlassMenu.Attach(tabBorder, WindowMenu); GlassMenu.Attach(headerGrid, WindowMenu);
            mainGrid.Children.Add(headerGrid);
            mainGrid.Children.Add(tabBorder);
            mainGrid.Children.Add(contentPanel);

            rootGlassBorder.Child = mainGrid;
            outerGrid = new Grid();
            // árnyék effekt nélkül (szoftveres rajzolásnál a blur minden frissítésnél újraszámolódna): egymásba ágyazott, halványuló keretek
            Border sh = null; int[] shA = { 8, 14, 22, 32, 46, 64 }; for (int i = shA.Length - 1; i >= 0; i--) { Border b = new Border { CornerRadius = new CornerRadius(16 + i * 2), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromArgb((byte)shA[shA.Length - 1 - i], 0, 0, 0)), Child = sh }; sh = b; }
            foreach (Border b in new Border[] { sh }) { Border x = b; while (x != null) { x.BorderBrush.Freeze(); x = x.Child as Border; } }
            shadowBorder = new Border { Margin = new Thickness(WM - 12, WM - 8, WM - 12, WM - 16), IsHitTestVisible = false, Child = sh };
            rootGlassBorder.Margin = new Thickness(WM);
            handlesGrid = new Grid { Margin = new Thickness(WM - 5) };
            foreach (string hd in new string[] { "N", "S", "W", "E", "NW", "NE", "SW", "SE" }) AddHandle(hd);
            outerGrid.Children.Add(shadowBorder); outerGrid.Children.Add(rootGlassBorder); outerGrid.Children.Add(handlesGrid);
            this.Content = outerGrid;
        }

        private double DpiScale() { PresentationSource ps = PresentationSource.FromVisual(this); return ps != null ? ps.CompositionTarget.TransformToDevice.M11 : 1.0; }

        private void HeaderDown(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { ToggleMax(); return; }
            if (maxed) return; try { this.DragMove(); } catch { }
        }

        private void ToggleMax()
        {
            if (!maxed)
            {
                restoreRect = new Rect(Left, Top, Width, Height); double k = DpiScale();
                System.Windows.Forms.Screen sc; try { sc = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle); } catch { sc = System.Windows.Forms.Screen.PrimaryScreen; } System.Drawing.Rectangle wa = sc.WorkingArea;
                rootGlassBorder.Margin = new Thickness(0); shadowBorder.Visibility = Visibility.Collapsed; handlesGrid.Visibility = Visibility.Collapsed; rootGlassBorder.CornerRadius = new CornerRadius(0); windowClip.RadiusX = 0; windowClip.RadiusY = 0;
                MinWidth = 0; MinHeight = 0; Left = wa.X / k; Top = wa.Y / k; Width = wa.Width / k; Height = wa.Height / k; maxed = true;
            }
            else
            {
                rootGlassBorder.Margin = new Thickness(WM); shadowBorder.Visibility = Visibility.Visible; handlesGrid.Visibility = Visibility.Visible; rootGlassBorder.CornerRadius = new CornerRadius(16); windowClip.RadiusX = 16; windowClip.RadiusY = 16;
                Rect vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight); if (!vs.IntersectsWith(restoreRect)) { restoreRect.X = 60; restoreRect.Y = 60; }
                Rect wa1 = SystemParameters.WorkArea; restoreRect.Width = Math.Min(restoreRect.Width, wa1.Width); restoreRect.Height = Math.Min(restoreRect.Height, wa1.Height);
                MinWidth = Math.Min(820 + 2 * WM, restoreRect.Width); MinHeight = Math.Min(580 + 2 * WM, restoreRect.Height); Left = restoreRect.X; Top = restoreRect.Y; Width = restoreRect.Width; Height = restoreRect.Height; maxed = false;
            }
            if (headerTitle != null && curTab != 5) headerTitle.Text = TitleFor(curTab);
        }

        // méretező fogók az ablak szélein és sarkain (kézi méretezés a képernyő-koordinátákból, ezért nem remeg)
        private void AddHandle(string d)
        {
            Border b = new Border { Background = Brushes.Transparent, Tag = d }; double t = 8, c = 14;
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
            b.MouseLeftButtonDown += (o, e) => { rsDir = d; rsEl = b; GetCursorPos(out rsCur); rsRect = new Rect(Left, Top, Width, Height); b.CaptureMouse(); e.Handled = true; };
            b.MouseMove += (o, e) =>
            {
                if (rsEl != b || !b.IsMouseCaptured) return; PT2 c2; GetCursorPos(out c2); double k = DpiScale(), dx = (c2.X - rsCur.X) / k, dy = (c2.Y - rsCur.Y) / k;
                double l = rsRect.X, tp = rsRect.Y, w = rsRect.Width, h = rsRect.Height;
                if (rsDir.Contains("E")) w = Math.Max(MinWidth, rsRect.Width + dx);
                if (rsDir.Contains("S")) h = Math.Max(MinHeight, rsRect.Height + dy);
                if (rsDir.Contains("W")) { w = Math.Max(MinWidth, rsRect.Width - dx); l = rsRect.Right - w; }
                if (rsDir.Contains("N")) { h = Math.Max(MinHeight, rsRect.Height - dy); tp = rsRect.Bottom - h; }
                Left = l; Top = tp; Width = w; Height = h;
            };
            b.MouseLeftButtonUp += (o, e) => { b.ReleaseMouseCapture(); rsEl = null; };
            handlesGrid.Children.Add(b);
        }

        private Button CreateMacDot(Color dotColor)
        {
            Button btn = new Button {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(dotColor),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            ControlTemplate t = new ControlTemplate(typeof(Button));
            FrameworkElementFactory b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            b.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            t.VisualTree = b;
            btn.Template = t;
            return btn;
        }

        public void SelectTabExt(int i) { SelectTab(i); }
        public void ShowAdvancedTest(double h) { this.Height = h + 2 * WM; this.Top = 0; if (wiperAdv != null) { wiperAdv.Visibility = Visibility.Visible; wiperAdv.RefreshAll(); } }
        public void TestMenu(int which)
        {
            this.Topmost = true; this.Left = 200; this.Top = 60; System.Windows.Forms.Cursor.Position = new System.Drawing.Point(560, 360);
            if (which == 1) { SelectTab(1); RefreshProcesses(); if (procListView.Items.Count > 0) procListView.SelectedIndex = 1; }
            if (which == 2) SelectTab(0);
            this.Dispatcher.BeginInvoke(new Action(() => { MenuEntry[] m = which == 1 ? ProcMenu() : (which == 2 ? WiperMenu() : WindowMenu()); GlassMenu.Show(rootGlassBorder, m); }), DispatcherPriority.ApplicationIdle);
        }

        private void HideToTray() { try { this.Hide(); } catch { } }
        public void HideToTrayExt() { if (notifyIcon != null) HideToTray(); else { this.ShowInTaskbar = true; this.WindowState = WindowState.Normal; } }
        public void BringBack() { try { this.Show(); this.ShowInTaskbar = true; if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal; this.Activate(); } catch { } }
        private static string RunKey { get { return @"Software\Microsoft\Windows\CurrentVersion\Run"; } }
        private bool IsAutostart() { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("GlassControl") != null; } catch { return false; } }
        private void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return; if (on) k.SetValue("GlassControl", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"" + (cfg.StartHidden ? " /tray" : "")); else k.DeleteValue("GlassControl", false);
                }
            }
            catch { }
        }
        private void OpenPath(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); } catch { } }
        private void RevealFile(string file) { try { if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } }
        private void CopyText(string t) { try { Clipboard.SetText(t); } catch { } }

        private MenuEntry[] WindowMenu()
        {
            string appDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return new MenuEntry[] {
                GlassMenu.Check(Loc.T("Mindig felül"), this.Topmost, () => { this.Topmost = !this.Topmost; }),
                GlassMenu.Item(maxed ? Loc.T("Visszaállítás") : Loc.T("Teljes méret"), Loc.T("dupla kattintás"), () => ToggleMax()),
                GlassMenu.Item(Loc.T("Minimalizálás"), () => { this.WindowState = WindowState.Minimized; }),
                GlassMenu.Item(Loc.T("Háttérbe (tálca ikon)"), () => HideToTray()),
                GlassMenu.Sep(),
                GlassMenu.Check(Loc.T("Indítás a Windows-szal (háttérben)"), IsAutostart(), () => SetAutostart(!IsAutostart())),
                GlassMenu.Item(Loc.T("Analitika megnyitása"), () => SelectTab(2)),
                GlassMenu.Item(Loc.T("Analitika mappa megnyitása"), () => OpenPath(System.IO.Path.Combine(appDir, "analytics"))),
                GlassMenu.Item(Loc.T("GlassControl mappa megnyitása"), () => OpenPath(appDir)),
                GlassMenu.Item(Loc.T("Parancsikonok újralétrehozása (asztal, Start)"), () => { Installer.EnsureInstalled(true); }),
                GlassMenu.Item(Loc.T("Terminal megnyitása (külön ablak)"), () => { try { Process.Start(System.IO.Path.Combine(appDir, "terminal", "Terminal.exe")); } catch { } }),
                GlassMenu.Sep(),
                GlassMenu.Item(Loc.T("Kilépés (a mérés is leáll)"), () => { if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); } Application.Current.Shutdown(); })
            };
        }

        // ---- automatikus és okos seprés
        [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        private double IdleSeconds() { LASTINPUTINFO li = new LASTINPUTINFO(); li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)); if (!GetLastInputInfo(ref li)) return 0; return (uint)Environment.TickCount - li.dwTime > 0 ? ((uint)Environment.TickCount - li.dwTime) / 1000.0 : 0; }

        private void UpdateAutoLabel() { if (autoLabel != null) autoLabel.Text = cfg.Summary() + Loc.T("  (kattints: be/ki)"); }

        private bool pinOn;
        private void PinTick()
        {
            try
            {
                bool want = curTab == 3 && this.IsVisible && this.WindowState != WindowState.Minimized;      // az iOS Glass UI fül aktív: a dokk és a pilulák látszanak, hogy lásd az élő változást
                if (want) { string pd = System.IO.Path.GetDirectoryName(pinFile); if (!System.IO.Directory.Exists(pd)) return; System.IO.File.WriteAllText(pinFile, DateTime.UtcNow.Ticks.ToString()); pinOn = true; }
                else if (pinOn) { if (System.IO.File.Exists(pinFile)) System.IO.File.Delete(pinFile); pinOn = false; }
            }
            catch { }
        }

        private void StartupSweepTimer()
        {
            if (!cfg.StartupSweep) return;
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(10, cfg.StartupDelaySec)) }; t.Tick += (s, e) => { t.Stop(); if (cfg.StartupSweep && !sweeping) { lastReason = Loc.T("indítás utáni seprés"); PerformWiperSweep(true); } }; t.Start();
        }

        // melyik feltétel miatt kell most seperni? (null = semmi)
        private string DecideSweep(double pct)
        {
            if (!cfg.Auto) return null;
            if ((DateTime.Now - lastSweep).TotalMinutes < cfg.Cooldown * backoff) return null;
            if (!cfg.InHours(DateTime.Now.Hour)) return null;
            if (UserBusy()) return null;
            if (cfg.IdleOnly && IdleSeconds() < cfg.IdleSec) return null;
            string reason = null; if (cfg.Threshold > 0 && pct < cfg.Threshold - 5) backoff = 1;
            if (cfg.Threshold > 0 && pct >= cfg.Threshold) reason = Loc.T("memória ") + pct.ToString("0") + "% ≥ " + cfg.Threshold + "%";
            else if (cfg.AvailBelowGb > 0 && AvailGb() < cfg.AvailBelowGb) reason = Loc.T("szabad memória < ") + cfg.AvailBelowGb.ToString("0.#") + " GB";
            else if (cfg.IntervalMin > 0 && (DateTime.Now - lastSweep).TotalMinutes >= cfg.IntervalMin) reason = Loc.T("időzítés (") + cfg.IntervalMin + Loc.T(" perc)");
            else if (cfg.Smart && cfg.Threshold > 0 && store.Model.Minutes > 600)      // tanult előrejelzés: ha ez az óra általában nehéz, kicsit előbb indul
            {
                double hourAvg = store.Model.HourPct(DateTime.Now.Hour);
                if (pct >= cfg.Threshold - 8 && hourAvg >= cfg.Threshold + 6 && (DateTime.Now - lastSweep).TotalMinutes >= cfg.Cooldown * 2) reason = Loc.T("tanult: ez az óra általában nehéz (") + hourAvg.ToString("0") + "%)";
            }
            return reason;
        }

        private void CheckAuto(double pct)
        {
            if (sweeping || btnOptimizePill == null || !btnOptimizePill.IsEnabled) return;
            string reason = DecideSweep(pct); if (reason == null) return;
            lastReason = Loc.T("Utolsó automatikus indok: ") + reason + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            PerformWiperSweep(true);
        }

        [StructLayout(LayoutKind.Sequential)] struct PMC { public uint cb, PageFaultCount; public UIntPtr PeakWS, WS, QPP, QPNP, QNPP, QNNP, PageFile, PeakPageFile; }
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr h, out PMC p, uint cb);
        // a munkakészlet a már megnyitott kezelőből olvasható – nem kell minden folyamathoz teljes rendszer-pillanatképet készíteni
        static long WsOf(IntPtr h) { PMC m; m = new PMC(); m.cb = (uint)Marshal.SizeOf(typeof(PMC)); return GetProcessMemoryInfo(h, out m, m.cb) ? (long)m.WS.ToUInt64() : 0; }
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int s);
        // teljes képernyős játék / videó / prezentáció közben nem seperünk
        static bool UserBusy() { try { int st; return SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4); } catch { return false; } }
        internal static readonly string[] Protected = { "system", "idle", "dwm", "explorer", "csrss", "winlogon", "services", "lsass", "smss", "wininit", "registry", "memory compression", "fontdrvhost", "glasscontrol", "glassdock", "searchhost", "textinputhost", "shellexperiencehost", "startmenuexperiencehost", "audiodg", "msmpeng", "sihost", "ctfmon", "securityhealthservice", "lumen", "vmmem", "vmmemwsl", "taskhostw", "runtimebroker" };

        private void SetAuto(bool on) { cfg.Auto = on; cfg.Save(); if (wiperAdv != null) wiperAdv.RefreshAll(); }
        private void OpenMemoryFile() { try { if (System.IO.File.Exists(store.MemoryFile)) Process.Start(new ProcessStartInfo(store.MemoryFile) { UseShellExecute = true }); else OpenPath(store.Root); } catch { } }
        private void ThresholdSet(int v) { cfg.Threshold = v; cfg.Save(); if (wiperAdv != null) wiperAdv.RefreshAll(); }
        private void CooldownSet(int v) { cfg.Cooldown = v; cfg.Save(); if (wiperAdv != null) wiperAdv.RefreshAll(); }

        private MenuEntry[] WiperMenu()
        {
            return new MenuEntry[] {
                GlassMenu.Item(Loc.T("Optimalizálás most"), () => PerformWiperSweep(false)),
                GlassMenu.Item(Loc.T("📈 Heti riport (HTML kártya)"), () => WeeklyReport.Open(store)),
                GlassMenu.Check(Loc.T("Automatikus seprés"), cfg.Auto, () => SetAuto(!cfg.Auto)),
                GlassMenu.Menu(Loc.T("Küszöb (ennyi % felett seper)"),
                    GlassMenu.Check("50%", cfg.Threshold == 50, () => ThresholdSet(50)), GlassMenu.Check("60%", cfg.Threshold == 60, () => ThresholdSet(60)),
                    GlassMenu.Check("70%", cfg.Threshold == 70, () => ThresholdSet(70)), GlassMenu.Check("80%", cfg.Threshold == 80, () => ThresholdSet(80))),
                GlassMenu.Menu(Loc.T("Legfeljebb ennyi percenként"),
                    GlassMenu.Check(Loc.T("5 perc"), cfg.Cooldown == 5, () => CooldownSet(5)), GlassMenu.Check(Loc.T("10 perc"), cfg.Cooldown == 10, () => CooldownSet(10)),
                    GlassMenu.Check(Loc.T("20 perc"), cfg.Cooldown == 20, () => CooldownSet(20)), GlassMenu.Check(Loc.T("30 perc"), cfg.Cooldown == 30, () => CooldownSet(30))),
                GlassMenu.Check(Loc.T("Okos mód (a tanult szabályokból)"), cfg.Smart, () => { cfg.Smart = !cfg.Smart; cfg.Save(); if (wiperAdv != null) wiperAdv.RefreshAll(); }),
                GlassMenu.Sep(),
                GlassMenu.Item(Loc.T("Haladó beállítások…"), () => { if (wiperAdv != null) { wiperAdv.Visibility = Visibility.Visible; wiperAdv.BringIntoView(); } }),
                GlassMenu.Item(Loc.T("Analitika megnyitása"), () => SelectTab(2)),
                GlassMenu.Item(Loc.T("Tanult tudás (memory.txt) megnyitása"), () => OpenMemoryFile()),
                GlassMenu.Item(Loc.T("Analitika mappa megnyitása"), () => OpenPath(store.Root))
            };
        }

        private MenuEntry[] ProcMenu()
        {
            ProcRow r = SelectedProc(); if (r == null) return new MenuEntry[] { GlassMenu.Off(Loc.T("Jelölj ki egy folyamatot")), GlassMenu.Sep(), GlassMenu.Item(Loc.T("Frissítés"), () => RefreshProcesses()) };
            string path = r.Path != null && r.Path.StartsWith("(") ? null : r.Path; string nm = r.Name;
            return new MenuEntry[] {
                GlassMenu.Item(Loc.T("RAM ürítés (csak ez)"), () => TrimSelectedProc()),
                GlassMenu.Item(Loc.T("Leállítás"), Loc.T("megerősítéssel"), () => KillSelectedProc()),
                GlassMenu.Item(Loc.T("Összes \"") + nm + Loc.T("\" leállítása"), () => KillAllNamed(nm)),
                GlassMenu.Menu(Loc.T("Prioritás"), GlassMenu.Item(Loc.T("Alacsony"), () => SetPriority(r.PID, ProcessPriorityClass.BelowNormal)), GlassMenu.Item(Loc.T("Normál"), () => SetPriority(r.PID, ProcessPriorityClass.Normal)), GlassMenu.Item(Loc.T("Magas"), () => SetPriority(r.PID, ProcessPriorityClass.AboveNormal))),
                GlassMenu.Sep(),
                path != null ? GlassMenu.Item(Loc.T("Útvonal megnyitása (Intéző)"), () => RevealFile(path)) : GlassMenu.Off(Loc.T("Útvonal megnyitása (nincs hozzáférés)")),
                GlassMenu.Item(Loc.T("Útvonal másolása"), () => CopyText(r.Path ?? "")),
                GlassMenu.Item(Loc.T("PID másolása"), () => CopyText(r.PID.ToString())),
                GlassMenu.Item(Loc.T("Keresés az interneten"), () => { try { Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(nm + " process what is it")) { UseShellExecute = true }); } catch { } }),
                GlassMenu.Sep(),
                GlassMenu.Item(Loc.T("Boncolás… (szülő, parancssor, portok, indítás)"), () => ProcAutopsy.Show(this, r.PID, nm)),
                GlassMenu.Item(Loc.T("Megnyitás az Analitikában"), () => { SelectTab(2); }),
                GlassMenu.Item(Loc.T("Lista frissítése"), () => RefreshProcesses())
            };
        }
        private void SetPriority(int pid, ProcessPriorityClass pc) { try { using (Process pp = Process.GetProcessById(pid)) pp.PriorityClass = pc; } catch (Exception ex) { MessageBox.Show(Loc.T("Nem sikerült: ") + ex.Message); } }
        private void KillAllNamed(string name)
        {
            if (Array.IndexOf(Protected, name.ToLowerInvariant()) >= 0) { MessageBox.Show(Loc.T("Ez egy védett rendszerfolyamat, nem állítom le."), Loc.T("Folyamatok leállítása")); return; }
            Process[] ps = Process.GetProcessesByName(name); if (ps.Length == 0) return;
            if (MessageBox.Show(Loc.T("Biztosan leállítod az összes \"") + name + Loc.T("\" folyamatot (") + ps.Length + Loc.T(" db)?\n\nA nem mentett munka elveszhet."), Loc.T("Folyamatok leállítása"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { foreach (Process p in ps) p.Dispose(); return; }
            foreach (Process p in ps) { try { p.Kill(); } catch { } finally { p.Dispose(); } } RefreshProcesses();
        }
        public void SaveShot(string file)
        {
            FrameworkElement r = rootGlassBorder; r.UpdateLayout();
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)r.ActualWidth, (int)r.ActualHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(r);
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool isSelected = (i == index);
                tabButtons[i].Foreground = isSelected ? Brushes.White : BrushTextMuted;
                tabButtons[i].Background = isSelected ? BrushTabSel : Brushes.Transparent;
                tabViews[i].Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            }
            curTab = index; if (headerTitle != null) headerTitle.Text = TitleFor(index); PinTick();
            LazyHost lh = index >= 0 && index < tabViews.Count ? tabViews[index] as LazyHost : null; if (lh != null) lh.Ensure();
            if (index == 5) StartTerminalOnce();
            if (index == 0) DrawDonutChart();
            if (index == 1) RefreshProcesses();
        }

        private void ApplyDarkVioletTableStyle(ListView listView)
        {
            listView.Background = BrushCardBg;
            listView.Foreground = Brushes.White;
            listView.BorderBrush = BrushBorder;
            listView.BorderThickness = new Thickness(1);

            // Dark Violet Header Style
            Style headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(58, 58, 62))));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.ForegroundProperty, new SolidColorBrush(Color.FromRgb(204, 204, 210))));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.PaddingProperty, new Thickness(12, 8, 12, 8)));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderBrushProperty, BrushBorder));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));

            ControlTemplate headerTemplate = new ControlTemplate(typeof(GridViewColumnHeader));
            FrameworkElementFactory headerBorder = new FrameworkElementFactory(typeof(Border));
            headerBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(GridViewColumnHeader.BackgroundProperty));
            headerBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(GridViewColumnHeader.BorderBrushProperty));
            headerBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(GridViewColumnHeader.BorderThicknessProperty));
            headerBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(GridViewColumnHeader.PaddingProperty));

            FrameworkElementFactory headerPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            headerPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            headerPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            headerBorder.AppendChild(headerPresenter);
            headerTemplate.VisualTree = headerBorder;
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.TemplateProperty, headerTemplate));

            // Dark Violet Rows Style
            Style itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(ListViewItem.ForegroundProperty, Brushes.White));
            itemStyle.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0, 2, 0, 2)));

            ControlTemplate itemTemplate = new ControlTemplate(typeof(ListViewItem));
            FrameworkElementFactory itemBorder = new FrameworkElementFactory(typeof(Border));
            itemBorder.Name = "Bd";
            itemBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(110, 52, 52, 56)));
            itemBorder.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            itemBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            itemBorder.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));

            FrameworkElementFactory itemContent = new FrameworkElementFactory(typeof(GridViewRowPresenter));
            itemContent.SetValue(GridViewRowPresenter.ColumnsProperty, new TemplateBindingExtension(GridView.ColumnCollectionProperty));
            itemBorder.AppendChild(itemContent);
            itemTemplate.VisualTree = itemBorder;

            Trigger isSelectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            isSelectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(10, 132, 255)), "Bd"));
            isSelectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(204, 204, 210)), "Bd"));

            Trigger isMouseOverTrigger = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
            isMouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(74, 74, 80)), "Bd"));

            itemTemplate.Triggers.Add(isSelectedTrigger);
            itemTemplate.Triggers.Add(isMouseOverTrigger);
            itemStyle.Setters.Add(new Setter(ListViewItem.TemplateProperty, itemTemplate));

            listView.Resources.Add(typeof(GridViewColumnHeader), headerStyle);
            listView.ItemContainerStyle = itemStyle;
        }

        #region Wiper View
        // ---- "For AI programming" / "Fastening" / Hardware gombok az azonnali seprés alatt
        private DispatcherTimer aiTimer; private TextBlock aiStatus, aiBtnTxt; private Border aiBtn, fastBtn; private bool fastBusy;
        private Border AiPill(string text, out TextBlock tb, string tip) { tb = new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center }; return new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(5, 8, 5, 0), Cursor = Cursors.Hand, Child = tb, ToolTip = tip }; }
        private UIElement CreateAiRow()
        {
            AiGuard.Load(); WrapPanel row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            aiBtn = AiPill("", out aiBtnTxt, Loc.T("Bekapcsolva folyamatosan észleli az AI-eszközöket (Claude, Antigravity, Cursor, Ollama…) és a localhost szervereket, azokat nem húzza össze, a többit viszont karbantartja, hogy mindig legyen szabad RAM"));
            aiBtn.MouseLeftButtonUp += (s, e) => SetAiMode(!AiGuard.On);
            TextBlock ft; fastBtn = AiPill(Loc.T("⚡  Fastening"), out ft, Loc.T("Gyorsítás RAM-kímélően: a háttérben sokat számoló programok alacsonyabb prioritást kapnak, a 3 napnál régebbi Temp-fájlok törlődnek, majd memória-seprés"));
            fastBtn.MouseLeftButtonUp += (s, e) => { if (fastBusy) return; fastBusy = true; ft.Text = Loc.T("⚡  Gyorsítás…"); aiStatus.Text = Loc.T("Gyorsítás folyamatban…");
                Task.Run(() => { string r = Fastener.Run(); Dispatcher.BeginInvoke(new Action(() => { fastBusy = false; ft.Text = Loc.T("⚡  Fastening"); aiStatus.Text = r; if (!sweeping) { lastReason = "Fastening"; PerformWiperSweep(false); } })); }); };
            TextBlock ht; Border hwBtn = AiPill(Loc.T("🎛  Hardware"), out ht, Loc.T("Monitorok fényereje, ventilátorok, LED-ek (MSI Afterburner / OpenRGB)"));
            HardwareWindow hw = null; hwBtn.MouseLeftButtonUp += (s, e) => { try { if (hw != null && hw.IsLoaded) { hw.Activate(); return; } hw = new HardwareWindow(); hw.Show(); } catch { } };
            row.Children.Add(aiBtn); row.Children.Add(fastBtn); row.Children.Add(hwBtn);
            aiStatus = new TextBlock { FontSize = 11.5, Foreground = BrushTextMuted, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 6, 20, 0), TextAlignment = TextAlignment.Center };
            StackPanel sp = new StackPanel(); sp.Children.Add(row); sp.Children.Add(aiStatus); ApplyAiUi(); if (AiGuard.On) StartAiTimer(); return sp;
        }
        private void ApplyAiUi() { aiBtnTxt.Text = AiGuard.On ? Loc.T("🤖  For AI programming: BE") : Loc.T("🤖  For AI programming: KI"); aiBtn.Background = AiGuard.On ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)); if (!AiGuard.On && aiStatus != null) aiStatus.Text = ""; }
        private void SetAiMode(bool on) { AiGuard.Set(on); ApplyAiUi(); if (on) { StartAiTimer(); AiTick(); } else if (aiTimer != null) aiTimer.Stop(); }
        private void StartAiTimer() { if (aiTimer == null) { aiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; aiTimer.Tick += (s, e) => AiTick(); } aiTimer.Start(); }
        private void AiTick()
        {
            Task.Run(() => { AiGuard.Scan(); Dispatcher.BeginInvoke(new Action(() => {
                if (!AiGuard.On) return; double avail = AvailGb(), thr = Math.Max(2.5, RamTotalGb() * 0.12);
                if (!fastBusy) aiStatus.Text = "🤖 " + AiGuard.Summary + " · " + Loc.T("szabad: ") + avail.ToString("0.0") + " GB";
                if (!sweeping && avail < thr && (DateTime.Now - lastSweep).TotalSeconds > 45) { lastReason = Loc.T("AI mód: kevés szabad RAM"); PerformWiperSweep(true); }
            })); });
        }

        private UIElement CreateWiperView()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1. Donut Chart Canvas Container
            Grid chartContainer = new Grid { Height = 210, Width = 210, HorizontalAlignment = HorizontalAlignment.Center };
            chartCanvas = new Canvas { Width = 210, Height = 210 };
            
            txtRamPercentCenter = new TextBlock {
                Text = "0%",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            chartContainer.Children.Add(chartCanvas);
            chartContainer.Children.Add(txtRamPercentCenter);

            // 2. Stat Cards (Used RAM & Free RAM in Translucent Glass Style)
            StackPanel cardsPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Width = 420 };
            
            // Used RAM Card
            Border cardUsed = CreateGlassCard(new Color[] { Color.FromArgb(150, 36, 52, 78), Color.FromArgb(130, 28, 36, 54) }, Color.FromRgb(10, 132, 255));
            StackPanel pUsed = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 12, 20, 12) };
            pUsed.Children.Add(new TextBlock { Text = Loc.T("Használatban: "), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrushVioletHover });
            txtUsedValue = new TextBlock { Text = "0.00 GB (0%)", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            pUsed.Children.Add(txtUsedValue);
            cardUsed.Child = pUsed;

            // Free RAM Card
            Border cardFree = CreateGlassCard(new Color[] { Color.FromArgb(160, 20, 50, 40), Color.FromArgb(140, 15, 35, 30) }, Color.FromRgb(48, 209, 88));
            cardFree.Margin = new Thickness(0, 12, 0, 0);
            StackPanel pFree = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 12, 20, 12) };
            pFree.Children.Add(new TextBlock { Text = Loc.T("Elérhető: "), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrushEmerald });
            txtFreeValue = new TextBlock { Text = "0.00 GB (0%)", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            pFree.Children.Add(txtFreeValue);
            cardFree.Child = pFree;

            cardsPanel.Children.Add(cardUsed);
            cardsPanel.Children.Add(cardFree);

            // 3. Status Text & Big iOS Pill Action Button
            StackPanel actionPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Width = 420 };

            txtStatusText = new TextBlock {
                Text = Loc.T("Felszabadítás: Még nem futott"),
                FontSize = 12,
                Foreground = BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            btnOptimizePill = new Button {
                Content = Loc.T("🔀  Optimalizálás / AZONNALI SEPERGETÉS"),
                Height = 52,
                Width = 420,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(Color.FromRgb(10, 132, 255), Color.FromRgb(0, 104, 224), 90),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            
            ControlTemplate btnTemplate = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bFactory = new FrameworkElementFactory(typeof(Border));
            bFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(26));
            bFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory pFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            pFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            pFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bFactory.AppendChild(pFactory);
            btnTemplate.VisualTree = bFactory;
            btnOptimizePill.Template = btnTemplate;

            btnOptimizePill.Click += (s, e) => PerformWiperSweep();

            actionPanel.Children.Add(txtStatusText);
            actionPanel.Children.Add(btnOptimizePill);

            // automatikus seprés vezérlő sor
            autoLabel = new TextBlock { FontSize = 11.5, Foreground = BrushTextMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, ToolTip = Loc.T("Jobb klikk az ablakon: küszöb, gyakoriság, okos mód") };
            UpdateAutoLabel(); autoLabel.Cursor = Cursors.Hand; autoLabel.MouseLeftButtonUp += (s, e) => { SetAuto(!cfg.Auto); };
            actionPanel.Children.Add(autoLabel);
            GlassMenu.Attach(grid, WiperMenu);
            Grid.SetRow(chartContainer, 0);
            Grid.SetRow(cardsPanel, 1);
            Grid.SetRow(actionPanel, 2);

            grid.Children.Add(chartContainer);
            grid.Children.Add(cardsPanel);
            grid.Children.Add(actionPanel);

            // Haladó beállítások (lenyitható)
            wiperAdv = new WiperAdvanced(cfg, () => IsAutostart(), (on) => SetAutostart(on), () => { lastReason = Loc.T("kézi (okos) seprés"); PerformWiperSweep(false); }, () => OpenMemoryFile());
            wiperAdv.LastInfo = () => lastReason; wiperAdv.Visibility = Visibility.Collapsed; wiperAdv.Margin = new Thickness(0, 0, 8, 10);
            TextBlock advTxt = new TextBlock { Text = Loc.T("⚙  Haladó beállítások  ▾"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            Border advBtn = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Padding = new Thickness(22, 9, 22, 9), Margin = new Thickness(0, 14, 0, 12), HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, Child = advTxt };
            advBtn.MouseLeftButtonUp += (s, e) => { bool open = wiperAdv.Visibility != Visibility.Visible; wiperAdv.Visibility = open ? Visibility.Visible : Visibility.Collapsed; advTxt.Text = open ? Loc.T("⚙  Haladó beállítások  ▴") : Loc.T("⚙  Haladó beállítások  ▾"); if (open) { wiperAdv.RefreshAll(); wiperAdv.BringIntoView(); } };
            forecastTxt = new TextBlock { FontSize = 12, Foreground = BrushTextMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Text = Loc.T("🔮 Előrejelzés: adatot gyűjtök…") };
            TextBlock fTxt = new TextBlock { Text = Loc.T("🎯  Fókusz-mód: KI"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            Border focusBtn = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Padding = new Thickness(22, 9, 22, 9), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, Child = fTxt, ToolTip = Loc.T("Seprés + Wallpaper Engine szünet + felugró értesítések némítása; újra megnyomva minden visszaáll") };
            focusBtn.MouseLeftButtonUp += (s, e) => { bool on = FocusMode.Toggle(); fTxt.Text = on ? Loc.T("🎯  Fókusz-mód: BE") : Loc.T("🎯  Fókusz-mód: KI"); focusBtn.Background = on ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)); if (on && !sweeping) { lastReason = Loc.T("fókusz-mód"); PerformWiperSweep(false); } };
            StackPanel stack = new StackPanel(); stack.Children.Add(grid); stack.Children.Add(CreateAiRow()); stack.Children.Add(forecastTxt); stack.Children.Add(focusBtn); stack.Children.Add(advBtn); stack.Children.Add(wiperAdv);
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack, Focusable = false };
            GlassMenu.Attach(sv, WiperMenu);
            return sv;
        }

        private Border CreateGlassCard(Color[] gradient, Color borderColor)
        {
            return new Border {
                Background = new LinearGradientBrush(gradient[0], gradient[1], 45),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = new DropShadowEffect { Color = borderColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.4 }
            };
        }

        private void DrawDonutChart()
        {
            if (chartCanvas == null) return;
            chartCanvas.Children.Clear();

            double cx = 105, cy = 105, radius = 80, thickness = 18;
            double angleUsed = Math.Min(359.9, (currentRamPercent / 100.0) * 360.0);

            // Background Ring (Emerald - Free RAM)
            Ellipse bgRing = new Ellipse {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = BrushEmerald,
                StrokeThickness = thickness,
                Margin = new Thickness(cx - radius, cy - radius, 0, 0)
            };
            chartCanvas.Children.Add(bgRing);

            // Active Arc (Violet - Used RAM)
            if (angleUsed > 0)
            {
                System.Windows.Shapes.Path arcPath = new System.Windows.Shapes.Path {
                    Stroke = BrushVioletAccent,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                PathGeometry geom = new PathGeometry();
                PathFigure fig = new PathFigure();
                
                double startRad = -Math.PI / 2;
                double endRad = startRad + (angleUsed * Math.PI / 180.0);

                fig.StartPoint = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
                fig.Segments.Add(new ArcSegment(
                    new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad)),
                    new Size(radius, radius), 0, angleUsed > 180, SweepDirection.Clockwise, true
                ));
                geom.Figures.Add(fig);
                arcPath.Data = geom;
                chartCanvas.Children.Add(arcPath);
            }

            txtRamPercentCenter.Text = string.Format("{0:F0}%", currentRamPercent);
        }

        private void RefreshStats()
        {
            try
            {
                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    freeGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    usedGb = totalGb - freeGb;
                    currentRamPercent = (float)memStatus.dwMemoryLoad;

                    if (txtUsedValue != null) txtUsedValue.Text = string.Format("{0:F2} GB ({1:F0}%)", usedGb, currentRamPercent);
                    if (txtFreeValue != null) txtFreeValue.Text = string.Format("{0:F2} GB ({1:F0}%)", freeGb, 100 - currentRamPercent);
                }
            }
            catch { }
        }

        private double AvailGb()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return GlobalMemoryStatusEx(ref m) ? m.ullAvailPhys / (1024.0 * 1024.0 * 1024.0) : 0;
        }

        private void PerformWiperSweep() { lastReason = Loc.T("kézi seprés"); PerformWiperSweep(false); }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        private void PerformWiperSweep(bool auto)
        {
            if (sweeping) return; sweeping = true; lastSweep = DateTime.Now;
            btnOptimizePill.IsEnabled = false;
            btnOptimizePill.Content = auto ? Loc.T("Automatikus seprés...") : Loc.T("Optimalizálás folyamatban...");
            bool smart = cfg.Smart, protectFg = cfg.ProtectFg; long minBytes = cfg.MinProcMb * 1048576L; HashSet<string> skipLow = new HashSet<string>(store.SkipLowGain), inUse = new HashSet<string>(store.OftenInUse), first = new HashSet<string>(store.SweepFirst);

            Task.Run(() => { try {
                int count = 0;
                long freedBytes = 0;
                double availBefore = AvailGb();
                string fg = ""; try { uint fpid; GetWindowThreadProcessId(GetForegroundWindow(), out fpid); if (fpid != 0) { Process fp = Process.GetProcessById((int)fpid); fg = fp.ProcessName.ToLowerInvariant(); fp.Dispose(); } } catch { }
                Dictionary<string, double> byName = new Dictionary<string, double>();

                bool aiOn = AiGuard.On; if (aiOn) AiGuard.Scan();      // "For AI programming": a futó AI / fejlesztői folyamatokhoz nem nyúlunk
                int selfPid = Process.GetCurrentProcess().Id; List<Process> procs = new List<Process>(Process.GetProcesses());
                // okos mód: a tanultan "mindig felfúvódó, háttérben futó" programok előre
                if (smart) procs.Sort((x, y) => { int fx = 0, fy = 0; try { fx = first.Contains(x.ProcessName.ToLowerInvariant()) ? 0 : 1; } catch { } try { fy = first.Contains(y.ProcessName.ToLowerInvariant()) ? 0 : 1; } catch { } if (fx != fy) return fx.CompareTo(fy); long wx = 0, wy = 0; try { wx = x.WorkingSet64; } catch { } try { wy = y.WorkingSet64; } catch { } return wy.CompareTo(wx); });
                foreach (Process p in procs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (p.Id <= 4 || p.Id == selfPid || Array.IndexOf(Protected, name) >= 0) continue;
                        if (cfg.IsExcluded(name)) continue;                                      // a te kizárási listád
                        if (aiOn && AiGuard.IsProtected(p.Id)) continue;                         // AI mód: aktív AI / dev folyamat marad
                        if (protectFg && name == fg) continue;                                   // amit éppen használsz, ahhoz nem nyúlunk
                        if (smart && skipLow.Contains(name)) continue;                           // tanult: itt eddig alig volt nyereség
                        if (smart && inUse.Contains(name) && p.WorkingSet64 < 300L * 1048576) continue;      // gyakran használt kisebb program: marad
                        if (minBytes > 0 && p.WorkingSet64 < minBytes) continue;

                        IntPtr hProc = OpenProcess(0x0100 | 0x1000, false, p.Id);
                        if (hProc != IntPtr.Zero)
                        {
                            long before = WsOf(hProc);
                            if (EmptyWorkingSet(hProc))
                            {
                                long after = WsOf(hProc);
                                long f = Math.Max(0, before - after); freedBytes += f; count++;
                                double cur; byName.TryGetValue(name, out cur); byName[name] = cur + f / 1048576.0;
                            }
                            CloseHandle(hProc);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                System.Threading.Thread.Sleep(600);
                double freedMb = freedBytes / (1024.0 * 1024.0), availAfter = AvailGb();
                List<KeyValuePair<string, double>> tops = new List<KeyValuePair<string, double>>(byName); tops.Sort((x, y) => y.Value.CompareTo(x.Value)); StringBuilder topSb = new StringBuilder();
                for (int k = 0; k < tops.Count && k < 10; k++) { if (tops[k].Value < 5) break; if (topSb.Length > 0) topSb.Append('|'); topSb.Append(AnalyticsStore.Clean(tops[k].Key)).Append(':').Append(tops[k].Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)); }

                this.Dispatcher.Invoke(() => {
                    RefreshStats();
                    DrawDonutChart();
                    txtStatusText.Inlines.Clear();
                    txtStatusText.Inlines.Add(new Run((auto ? Loc.T("Automatikus optimalizálás · ") : Loc.T("Utolsó optimalizálás · ")) + DateTime.Now.ToString("HH:mm:ss") + (smart ? Loc.T(" · okos") : "")) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                    txtStatusText.Inlines.Add(new LineBreak());
                    txtStatusText.Inlines.Add(new Run(count + Loc.T(" folyamat · ")) { Foreground = BrushTextMuted });
                    txtStatusText.Inlines.Add(new Run(freedMb >= 1024 ? string.Format("{0:F2} GB", freedMb / 1024.0) : string.Format("{0:F0} MB", freedMb)) { FontWeight = FontWeights.Bold, Foreground = BrushEmerald });
                    txtStatusText.Inlines.Add(new Run(Loc.T(" felszabadítva · elérhető: ") + availBefore.ToString("F1") + " → " + availAfter.ToString("F1") + " GB") { Foreground = BrushTextMuted });
                    btnOptimizePill.Content = Loc.T("🔀  Optimalizálás / AZONNALI SEPERGETÉS");
                    btnOptimizePill.IsEnabled = true; sweeping = false; lastSweep = DateTime.Now;
                    if (analytics != null) analytics.RecordWiper(count, freedMb, availBefore, availAfter, topSb.ToString());
                    if (auto && cfg.Notify && notifyIcon != null) { try { notifyIcon.ShowBalloonTip(3000, "Wise Memory Wiper", string.Format(Loc.T("{0:F0} MB felszabadítva ({1} folyamat). Elérhető: {2:F1} → {3:F1} GB"), freedMb, count, availBefore, availAfter), System.Windows.Forms.ToolTipIcon.Info); } catch { } }
                    if (wiperAdv != null) wiperAdv.UpdateStatus();
                    if (auto)      // ha az automatikus seprés alig szabadít fel valamit, ritkábban próbálkozik (visszalépés); ha sokat, normál ütemben marad
                    {
                        double totalGb = availAfter + (RamTotalGb() - availAfter); if (freedMb < Math.Max(150, totalGb * 1024 * 0.02)) backoff = Math.Min(8, backoff * 2); else backoff = 1;
                    }
                });
            } catch { Dispatcher.BeginInvoke(new Action(() => { sweeping = false; btnOptimizePill.IsEnabled = true; btnOptimizePill.Content = Loc.T("🔀  Optimalizálás / AZONNALI SEPERGETÉS"); })); } });
        }
        private int backoff = 1;
        private double RamTotalGb() { MEMORYSTATUSEX m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); return GlobalMemoryStatusEx(ref m) ? m.ullTotalPhys / (1024.0 * 1024.0 * 1024.0) : 16; }
        #endregion

        #region Processes View
        private UIElement CreateProcessesView()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            txtProcSearch = new TextBox { Width = 250, Height = 32, FontSize = 13, Background = BrushCardBg, Foreground = Brushes.White, BorderBrush = BrushBorder, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0) };
            txtProcSearch.TextChanged += (s, e) => RefreshProcesses(); GlassMenu.ForTextBox(txtProcSearch);

            top.Children.Add(txtProcSearch);
            Button bTrim = CreatePillActionButton(Loc.T("RAM ürítés"), BrushVioletAccent); bTrim.Width = 120; bTrim.Height = 32; bTrim.Margin = new Thickness(10, 0, 0, 0); bTrim.Click += (s, e) => TrimSelectedProc();
            Button bKill = CreatePillActionButton(Loc.T("Leállítás"), new SolidColorBrush(Color.FromRgb(255, 69, 58))); bKill.Width = 110; bKill.Height = 32; bKill.Margin = new Thickness(8, 0, 0, 0); bKill.Click += (s, e) => KillSelectedProc();
            top.Children.Add(bTrim); top.Children.Add(bKill);

            procListView = new ListView {
                FontFamily = new FontFamily("Consolas")
            };
            ApplyDarkVioletTableStyle(procListView);
            procListView.PreviewMouseRightButtonDown += (s, e) => { DependencyObject d = e.OriginalSource as DependencyObject; while (d != null && !(d is ListViewItem)) d = VisualTreeHelper.GetParent(d); if (d is ListViewItem) ((ListViewItem)d).IsSelected = true; };
            GlassMenu.Attach(procListView, ProcMenu);

            GridView gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "PID", DisplayMemberBinding = new Binding("PID"), Width = 70 });
            gv.Columns.Add(new GridViewColumn { Header = Loc.T("Név"), DisplayMemberBinding = new Binding("Name"), Width = 180 });
            gv.Columns.Add(new GridViewColumn { Header = "RAM (MB)", DisplayMemberBinding = new Binding("RAM"), Width = 110 });
            gv.Columns.Add(new GridViewColumn { Header = Loc.T("Útvonal"), DisplayMemberBinding = new Binding("Path"), Width = 450 });
            procListView.View = gv;

            Grid.SetRow(top, 0);
            Grid.SetRow(procListView, 1);
            grid.Children.Add(top);
            grid.Children.Add(procListView);

            return grid;
        }

        private void RefreshProcesses()
        {
            if (procListView == null || procBusy) return; procBusy = true;
            string q = txtProcSearch.Text.ToLower();
            // a lista adatai háttérszálon készülnek (a folyamatok lekérése és az útvonalak lassúak) – a felület közben nem akad
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<ProcRow> rows = new List<ProcRow>();
                try
                {
                    Process[] procs = Process.GetProcesses(); List<KeyValuePair<long, ProcRow>> all = new List<KeyValuePair<long, ProcRow>>();
                    foreach (Process p in procs) { try { string nm = p.ProcessName; if (q.Length == 0 || nm.ToLower().Contains(q)) all.Add(new KeyValuePair<long, ProcRow>(p.WorkingSet64, new ProcRow { PID = p.Id, Name = nm })); } catch { } finally { p.Dispose(); } }
                    all.Sort(delegate (KeyValuePair<long, ProcRow> x, KeyValuePair<long, ProcRow> y) { return y.Key.CompareTo(x.Key); });
                    for (int i = 0; i < all.Count && i < 80; i++) { ProcRow r = all[i].Value; r.RAM = string.Format("{0:F1} MB", all[i].Key / 1048576.0); r.Path = ExePathOf(r.PID, r.Name); rows.Add(r); }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    procBusy = false; ProcRow selRow = procListView.SelectedItem as ProcRow; int selPid = selRow != null ? selRow.PID : -1;
                    procListView.Items.Clear(); foreach (ProcRow r in rows) { procListView.Items.Add(r); if (r.PID == selPid) procListView.SelectedItem = r; }
                }));
            });
        }

        private volatile bool procBusy;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder sb, ref int size);
        // útvonal PID+név szerint gyorsítótárazva (PID-újrahasznosításkor sem mutat más programot); MainModule helyett gyors, korlátozott jogú lekérdezés
        private string ExePathOf(int pid, string name)
        {
            string key = pid + "|" + name, exe; lock (pathCache) { if (pathCache.TryGetValue(key, out exe)) return exe; }
            exe = Loc.T("(nincs hozzáférés)"); IntPtr h = OpenProcess(0x1000, false, pid);
            if (h != IntPtr.Zero) { try { System.Text.StringBuilder sb = new System.Text.StringBuilder(1024); int n = sb.Capacity; if (QueryFullProcessImageName(h, 0, sb, ref n)) exe = sb.ToString(); } finally { CloseHandle(h); } }
            lock (pathCache) { if (pathCache.Count > 4000) pathCache.Clear(); pathCache[key] = exe; } return exe;
        }

        private Dictionary<string, string> pathCache = new Dictionary<string, string>();
        private ProcRow SelectedProc() { return procListView.SelectedItem as ProcRow; }

        private void KillSelectedProc()
        {
            ProcRow r = SelectedProc(); if (r == null) { MessageBox.Show(Loc.T("Előbb jelölj ki egy folyamatot a listában."), Loc.T("Folyamat leállítása"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show(string.Format(Loc.T("Biztosan leállítod?\n\n{0} (PID {1}, {2})\n\nA nem mentett munka elveszhet."), r.Name, r.PID, r.RAM), Loc.T("Folyamat leállítása"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (Array.IndexOf(Protected, (r.Name ?? "").ToLowerInvariant()) >= 0) { MessageBox.Show(Loc.T("Ez egy védett rendszerfolyamat, nem állítom le."), Loc.T("Folyamat leállítása")); return; }
            try { using (Process kp = Process.GetProcessById(r.PID)) { if (!string.Equals(kp.ProcessName, r.Name, StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(Loc.T("A PID közben másik folyamaté lett – a lista frissül."), Loc.T("Folyamat leállítása")); } else { kp.Kill(); kp.WaitForExit(1500); } } } catch (Exception ex) { MessageBox.Show(Loc.T("Nem sikerült leállítani: ") + ex.Message, Loc.T("Folyamat leállítása")); }
            RefreshProcesses();
        }

        private void TrimSelectedProc()
        {
            ProcRow r = SelectedProc(); if (r == null) { MessageBox.Show(Loc.T("Előbb jelölj ki egy folyamatot a listában."), Loc.T("RAM ürítés"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try { IntPtr h = OpenProcess(0x0100 | 0x1000, false, r.PID); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } catch { }
            RefreshProcesses();
        }
        #endregion

        #region Glass UI View
        private UIElement CreateGlassUIView()
        {
            string appDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string dockDir = System.IO.Path.Combine(appDir, "dock");
            return new GlassUiPanel(System.IO.Path.Combine(dockDir, "settings.ini"), (cmd) => { Task.Run(() => RunPowerShell(cmd)); }, System.IO.Path.Combine(dockDir, "GlassDock.exe"));
        }

        private Button CreatePillActionButton(string text, Brush bg)
        {
            Button btn = new Button {
                Content = text,
                Height = 45,
                Width = 380,
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = Brushes.White,
                Background = bg,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ControlTemplate t = new ControlTemplate(typeof(Button));
            FrameworkElementFactory b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(22));
            b.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory p = new FrameworkElementFactory(typeof(ContentPresenter));
            p.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            p.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(p);
            t.VisualTree = b;
            btn.Template = t;
            return btn;
        }
        #endregion

        #region DevTools View
        public void SweepNow() { if (!sweeping) { lastReason = Loc.T("Spotlight kérés"); PerformWiperSweep(false); } }
        private TextBlock forecastTxt; private DevHub devHub; private HelpVault helpVault; private LazyHost lazyGlass, lazyHub, lazyTerm, lazyHelp;
        private void HelpNavigate(string a)
        {
            try { if (a.StartsWith("tab:")) SelectTab(int.Parse(a.Substring(4))); else if (a.StartsWith("hub:")) { SelectTab(4); lazyHub.Ensure(); if (devHub != null) devHub.Select(int.Parse(a.Substring(4))); } } catch { }
        }
        private UIElement CreateDevToolsView()
        {
            string appDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            devHub = new DevHub(appDir, RunInTerminal); devHub.PreviewInTerm = PreviewInTerminal;
            return devHub;
        }

        // a Dev Hub hosszabb műveletei a Terminál fül új lapján futnak (látható, megszakítható)
        // a Dev Hub "Terminal preview": a Terminál fül aktuális lapja alatt nyílik meg az oldal (valódi Edge-motorral)
        private void PreviewInTerminal(string url)
        {
            if (lazyTerm != null) lazyTerm.Ensure(); if (termHost == null) return;
            if (!termStarted) { termStarted = true; termStartedAt = DateTime.Now; termQuick = 0; termHost.UpdateLayout(); termHost.OpenFirst(false); }
            SelectTab(5); termHost.OpenPreview(url);
        }
        public void TestPreview(string url) { PreviewInTerminal(url); }
        public void SaveShotPw(string file)
        {
            try { Topmost = true; Activate(); } catch { } IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle; int w = (int)(ActualWidth * 1.0), hh = (int)(ActualHeight * 1.0);
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, hh)) { using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp)) { IntPtr hd = g.GetHdc(); PrintWindowNative(h, hd, 2); g.ReleaseHdc(hd); } bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png); }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PrintWindow")] private static extern bool PrintWindowNative(IntPtr h, IntPtr dc, uint flags);
        public void TestRunInTerminal(string cmd) { RunInTerminal(Loc.T("Teszt"), null, cmd); }
        private void RunInTerminal(string title, string cwd, string cmd)
        {
            if (lazyTerm != null) lazyTerm.Ensure(); if (termHost == null) return;
            if (!termStarted) { termStarted = true; termStartedAt = DateTime.Now; termQuick = 0; termHost.UpdateLayout(); termHost.OpenFirst(false); }
            SelectTab(5); termHost.RunInNewTab(title, cwd, cmd);
        }
        #endregion

        #region Terminal View
        private UIElement CreateTerminalView()
        {
            string appDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string tdir = System.IO.Path.Combine(appDir, "terminal"); Cfg.Dir = tdir;
            termHost = new TermHost(System.IO.Path.Combine(tdir, "fonts"), true);
            termHost.Win.WindowActive = () => IsActive;
            Action title = () => { if (curTab == 5 && headerTitle != null) headerTitle.Text = termHost.TitleText; };
            termHost.TitleChanged += title; termHost.GridChanged += title;
            termHost.AllTabsClosed += () =>
            {
                // az utolsó lap bezárása után új shellt nyitunk; ha azonnal újra és újra leállna, nem pörgetjük
                if ((DateTime.Now - termStartedAt).TotalSeconds < 3 && ++termQuick >= 3) { termHost.Toast(Loc.T("A shell azonnal leáll – nézd meg a profilt (term.ini)")); termStarted = false; return; }
                termStartedAt = DateTime.Now; termHost.NewTab(ShellProfiles.Default(), Cfg.Get("startdir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            };
            // a terminál az ablak teljes tartalmi felületét kitölti (a külső 20 px margó ellenében)
            Border b = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), Margin = new Thickness(-20), Child = termHost };
            return b;
        }

        private void StartTerminalOnce()
        {
            if (termStarted || termHost == null) { if (termHost != null) termHost.FocusCurrent(); return; }
            termStarted = true; termStartedAt = DateTime.Now; termQuick = 0;
            termHost.Dispatcher.BeginInvoke(new Action(() => {
                termHost.UpdateLayout();
                termHost.OpenFirst(false);
                termHost.FocusCurrent();
            }), DispatcherPriority.Loaded);
        }
        #endregion

        private string RunPowerShell(string cmd)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command " + cmd) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }))
                {
                    var errTask = p.StandardError.ReadToEndAsync(); var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(45000)) { try { p.Kill(); } catch { } return Loc.T("időtúllépés"); }
                    return (outTask.Wait(2000) ? outTask.Result : "") + (errTask.Wait(2000) ? errTask.Result : "");
                }
            }
            catch (Exception ex) { return Loc.T("Hiba: ") + ex.Message; }
        }
    }
}
