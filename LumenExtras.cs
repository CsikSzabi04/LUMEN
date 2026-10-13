// Lumen – Fókusz-mód és Folyamat-boncolás.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GlassControlWpf
{
    public static class FocusMode
    {
        public static bool On;
        const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        static string wePath; static int weBusy;
        static string Mk { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus-toast.txt"); } }
        public static void RestoreIfNeeded() { try { if (On || File.Exists(Mk)) { On = true; Toggle(); } } catch { } }
        static string FindWe() { try { Process[] ps = Process.GetProcessesByName("wallpaper64"); if (ps.Length == 0) ps = Process.GetProcessesByName("wallpaper32"); string r = null; foreach (Process p in ps) { try { if (r == null) r = p.MainModule.FileName; } catch { } finally { p.Dispose(); } } return r; } catch { } return null; }
        // Wallpaper Engine megkeresése: futó folyamat, Steam-könyvtár, ismert hely
        static string LocateWe()
        {
            string p = FindWe(); if (p != null) return p; List<string> c = new List<string>();
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { string sp = k != null ? Convert.ToString(k.GetValue("SteamPath")) : null; if (!string.IsNullOrEmpty(sp)) c.Add(Path.Combine(sp.Replace('/', (char)92), @"steamapps\common\wallpaper_engine\wallpaper64.exe")); } } catch { }
            c.Add(@"D:\CsSzabj\Games\Steam\SteamDownloaded\steamapps\common\wallpaper_engine\wallpaper64.exe"); c.Add(@"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe");
            foreach (string x in c) if (File.Exists(x)) return x; return null;
        }
        // háttér újraindítása: a Wallpaper Engine folyamatai leállnak, majd csendben újraindulnak és lejátszás indul
        public static void RestartWallpaper(Action<string> done)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string msg; if (System.Threading.Interlocked.Exchange(ref weBusy, 1) == 1) { done(Loc.T("Már folyamatban…")); return; }
                try
                {
                    string exe = LocateWe(); if (exe == null) { done(Loc.T("A Wallpaper Engine nem található (Steam könyvtár?)")); return; }
                    foreach (string n in new string[] { "wallpaper64", "wallpaper32", "webwallpaper64", "webwallpaper32" }) foreach (Process p in Process.GetProcessesByName(n)) { try { p.Kill(); p.WaitForExit(3000); } catch { } finally { p.Dispose(); } }
                    System.Threading.Thread.Sleep(1500); wePath = exe; using (Process.Start(new ProcessStartInfo(exe, "-silent") { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false })) { }
                    System.Threading.Thread.Sleep(5000); We("play"); msg = Loc.T("✓ A háttér újraindult (") + Path.GetFileName(exe) + ")";
                }
                catch (Exception ex) { msg = Loc.T("Nem sikerült: ") + ex.Message; }
                finally { weBusy = 0; }
                done(msg);
            });
        }
        public static void Control(string verb) { We(verb); }
        static void We(string verb) { try { if (wePath == null) wePath = FindWe(); if (wePath != null) using (Process.Start(new ProcessStartInfo(wePath, "-control " + verb) { UseShellExecute = false, CreateNoWindow = true })) { } } catch { } }
        // be: seprés (a hívó végzi), Wallpaper Engine szünet, felugró értesítések némítása; ki: minden vissza
        public static bool Toggle()
        {
            On = !On;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ToastKey))
                {
                    if (On) { if (!File.Exists(Mk)) File.WriteAllText(Mk, Convert.ToString(k.GetValue("ToastEnabled", 1))); k.SetValue("ToastEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord); }
                    else { int old = 1; try { if (File.Exists(Mk)) { int.TryParse(File.ReadAllText(Mk).Trim(), out old); File.Delete(Mk); } } catch { } k.SetValue("ToastEnabled", old, Microsoft.Win32.RegistryValueKind.DWord); }
                }
            }
            catch { }
            We(On ? "pause" : "play"); return On;
        }
    }

    public static class ProcAutopsy
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PE32 { public uint size, usage, pid; public UIntPtr heap; public uint module, threads, parent; public int pri; public uint flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string exe; }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr h, ref PE32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr h, ref PE32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static Dictionary<uint, KeyValuePair<uint, string>> Snapshot()
        {
            Dictionary<uint, KeyValuePair<uint, string>> d = new Dictionary<uint, KeyValuePair<uint, string>>(); IntPtr h = CreateToolhelp32Snapshot(2, 0); if (h == (IntPtr)(-1)) return d;
            try { PE32 e = new PE32(); e.size = (uint)Marshal.SizeOf(typeof(PE32)); if (Process32First(h, ref e)) do { d[e.pid] = new KeyValuePair<uint, string>(e.parent, e.exe); } while (Process32Next(h, ref e)); } finally { CloseHandle(h); }
            return d;
        }

        public static void Show(Window owner, int pid, string name)
        {
            Window w = new Window { Title = Loc.T("Boncolás – ") + name, Width = 640, Height = 560, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(30, 30, 33)), Foreground = Brushes.White };
            TextBox tb = new TextBox { IsReadOnly = true, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(14), Text = Loc.T("Adatok gyűjtése…") };
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) }; Grid.SetRow(btns, 1); g.Children.Add(tb); g.Children.Add(btns); w.Content = g;
            Button off = new Button { Content = Loc.T("Indításkor tiltás"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false }; Button close = new Button { Content = Loc.T("Bezár"), Padding = new Thickness(14, 5, 14, 5) }; close.Click += delegate { w.Close(); }; btns.Children.Add(off); btns.Children.Add(close);
            List<KeyValuePair<string, string>> runEntries = new List<KeyValuePair<string, string>>();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder sb = new StringBuilder(); string exe = ""; try { using (Process p = Process.GetProcessById(pid)) { try { exe = p.MainModule.FileName; } catch { exe = Loc.T("(nincs hozzáférés)"); } sb.AppendLine(Loc.T("Név:          ") + p.ProcessName + "  (PID " + pid + ")"); try { sb.AppendLine(Loc.T("Elindult:     ") + p.StartTime.ToString("yyyy-MM-dd HH:mm:ss") + "  (" + (DateTime.Now - p.StartTime).ToString(@"d\.hh\:mm\:ss") + Loc.T(" ideje)")); } catch { } sb.AppendLine(Loc.T("Memória:      ") + (p.WorkingSet64 / 1048576) + Loc.T(" MB munkakészlet, ") + (p.PrivateMemorySize64 / 1048576) + Loc.T(" MB privát; ") + p.Threads.Count + Loc.T(" szál, ") + p.HandleCount + " handle"); } } catch (Exception ex) { sb.AppendLine(Loc.T("A folyamat már nem fut: ") + ex.Message); }
                sb.AppendLine(Loc.T("Útvonal:      ") + exe);
                try { int ex2; string cl = DevHub.Run("wmic.exe", "process where processid=" + pid + " get CommandLine /value", 6000, null, out ex2); foreach (string l in cl.Replace("\r", "").Split('\n')) if (l.StartsWith("CommandLine=")) sb.AppendLine(Loc.T("Parancssor:   ") + l.Substring(12)); } catch { }
                sb.AppendLine(); sb.AppendLine(Loc.T("Szülő-lánc:"));
                try { Dictionary<uint, KeyValuePair<uint, string>> snap = Snapshot(); uint cur = (uint)pid; string pad = "  "; for (int i = 0; i < 8; i++) { KeyValuePair<uint, string> kv; if (!snap.TryGetValue(cur, out kv)) break; sb.AppendLine(pad + kv.Value + " (" + cur + ")"); cur = kv.Key; pad += "  "; if (cur == 0) break; if (!snap.ContainsKey(cur)) { sb.AppendLine(pad + Loc.T("(a szülő már nem fut: ") + cur + ")"); break; } } } catch { }
                sb.AppendLine(); sb.AppendLine(Loc.T("Nyitott (figyelő) portok:")); bool any = false; try { foreach (DevHub.PortInfo pi in DevHub.ListenersFor(pid)) { sb.AppendLine("  " + pi.Port + "  (" + pi.Addr + ")"); any = true; } } catch { } if (!any) sb.AppendLine(Loc.T("  nincs"));
                sb.AppendLine(); sb.AppendLine(Loc.T("Indítási bejegyzések:")); string fn = exe.Length > 0 && exe.IndexOf('\\') >= 0 ? Path.GetFileName(exe) : name + ".exe"; bool found = false;
                foreach (string hive in new string[] { "HKCU", "HKLM" })
                {
                    try
                    {
                        using (Microsoft.Win32.RegistryKey k = (hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                            if (k != null) foreach (string vn in k.GetValueNames()) { string v = Convert.ToString(k.GetValue(vn)); if (v.IndexOf(fn, StringComparison.OrdinalIgnoreCase) >= 0) { sb.AppendLine("  " + hive + "\\...\\Run\\" + vn + "  =  " + v); found = true; if (hive == "HKCU") runEntries.Add(new KeyValuePair<string, string>(vn, v)); } }
                    }
                    catch { }
                }
                try { foreach (string sd in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }) if (Directory.Exists(sd)) foreach (string f in Directory.GetFiles(sd, "*.lnk")) if (Path.GetFileNameWithoutExtension(f).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { sb.AppendLine(Loc.T("  Indítópult: ") + f); found = true; } } catch { }
                if (!found) sb.AppendLine(Loc.T("  nincs Run / Indítópult bejegyzés (lehet ütemezett feladat vagy szolgáltatás)"));
                string text = sb.ToString(); try { string dmp = Environment.GetEnvironmentVariable("LUMEN_AUTOPSY_DUMP"); if (dmp != null) File.WriteAllText(dmp, text); } catch { } owner.Dispatcher.BeginInvoke(new Action(delegate { tb.Text = text; if (runEntries.Count > 0) off.IsEnabled = true; }));
            });
            off.Click += delegate
            {
                if (MessageBox.Show(w, Loc.T("Ezek a bejegyzések törlődnek az automatikus indításból (a program nem szűnik meg, csak nem indul el magától):\n\n") + string.Join("\n", runEntries.ConvertAll(delegate (KeyValuePair<string, string> e) { return e.Key; }).ToArray()), Loc.T("Indításkor tiltás"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                try { string bd = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devhub"); Directory.CreateDirectory(bd); using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) if (k != null) foreach (KeyValuePair<string, string> e in runEntries) { File.AppendAllText(Path.Combine(bd, "autostart-removed.txt"), e.Key + "=" + e.Value + Environment.NewLine); k.DeleteValue(e.Key, false); } off.IsEnabled = false; tb.Text += Loc.T("\n✓ Törölve az indításból (mentés: devhub\\autostart-removed.txt)."); } catch (Exception ex) { MessageBox.Show(w, ex.Message); }
            };
            w.Show();
        }
    }
}
