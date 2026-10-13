// Dev Hub – Portok, Környezet (PATH / változók / git / SSH), Gyorseszközök, Takarítás, Docker.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GlassTerm;
using Forms = System.Windows.Forms;

namespace GlassControlWpf
{
    public partial class DevHub
    {
        // ================================================================== PORTOK (élő lista: ki figyel melyik porton)
        [DllImport("iphlpapi.dll", SetLastError = true)] static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);
        internal static List<PortInfo> ListenersFor(int pid) { return ReadListeners().FindAll(delegate (PortInfo p) { return p.Pid == pid; }); }
        internal class PortInfo { public int Port, Pid; public string Addr, Proc; }
        static readonly string[] DevProcs = { "node", "npm", "pnpm", "yarn", "bun", "deno", "python", "pythonw", "py", "uvicorn", "gunicorn", "dotnet", "java", "javaw", "php", "ruby", "go", "cargo", "vite", "next-server", "nginx", "httpd", "apache", "mysqld", "postgres", "mongod", "redis-server", "docker", "com.docker.backend", "wslrelay", "code", "ngrok", "webpack", "esbuild", "caddy", "http-server", "live-server" };
        static readonly string[] SysProcs = { "system", "svchost", "wininit", "services", "lsass", "spoolsv", "idle", "winlogon", "searchindexer" };
        DispatcherTimer portTimer; StackPanel portList; TextBlock portInfo; TextBox portFilter; CheckBox portDevOnly; string portSig = "";

        internal static List<PortInfo> ReadListeners()
        {
            List<PortInfo> res = new List<PortInfo>(); Dictionary<int, string> names = new Dictionary<int, string>(); bool namesLoaded = false;
            foreach (int af in new int[] { 2, 23 })
            {
                int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, true, af, 3, 0); if (size <= 0) continue; size += 8192; IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, true, af, 3, 0) != 0) continue; int n = Marshal.ReadInt32(buf); int rs = af == 2 ? 24 : 56;
                    for (int i = 0; i < n; i++)
                    {
                        long row = buf.ToInt64() + 4 + (long)i * rs; IntPtr r = new IntPtr(row); int portRaw, pid; string addr;
                        if (af == 2) { int a = Marshal.ReadInt32(r, 4); portRaw = Marshal.ReadInt32(r, 8); pid = Marshal.ReadInt32(r, 20); addr = a == 0 ? "mindenhol" : (a & 0xFF) == 127 ? "localhost" : (a & 0xFF) + "." + ((a >> 8) & 0xFF) + "." + ((a >> 16) & 0xFF) + "." + ((a >> 24) & 0xFF); }
                        else { byte[] ab = new byte[16]; Marshal.Copy(r, ab, 0, 16); bool zero = true, loop = true; for (int k = 0; k < 16; k++) { if (ab[k] != 0) zero = false; if (k < 15 ? ab[k] != 0 : ab[k] != 1) loop = false; } portRaw = Marshal.ReadInt32(r, 20); pid = Marshal.ReadInt32(r, 52); addr = zero ? "mindenhol" : loop ? "localhost" : "IPv6"; }
                        int port = ((portRaw & 0xFF) << 8) | ((portRaw >> 8) & 0xFF); if (!namesLoaded) { namesLoaded = true; try { foreach (Process pr in Process.GetProcesses()) { try { names[pr.Id] = pr.ProcessName; } catch { } finally { pr.Dispose(); } } } catch { } } string nm; if (!names.TryGetValue(pid, out nm)) nm = pid == 4 ? "System" : "pid " + pid;
                        if (res.Exists(delegate (PortInfo x) { return x.Port == port && x.Pid == pid; })) continue; res.Add(new PortInfo { Port = port, Pid = pid, Addr = addr, Proc = nm });
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            res.Sort(delegate (PortInfo a, PortInfo b) { return a.Port.CompareTo(b.Port); }); return res;
        }
        static readonly int[] DevPorts = { 3000, 3001, 3002, 3333, 4000, 4200, 4321, 5000, 5001, 5173, 5174, 5500, 5555, 6006, 7000, 8000, 8001, 8080, 8081, 8088, 8787, 8888, 9000, 9229, 24678 };
        static bool IsDevPort(PortInfo p)
        {
            string n = p.Proc.ToLowerInvariant(); if (Array.IndexOf(DevProcs, n) >= 0) return true; if (Array.IndexOf(SysProcs, n) >= 0) return false;
            return Array.IndexOf(DevPorts, p.Port) >= 0 && !n.Contains("teamviewer") && !n.Contains("anydesk");
        }

        FrameworkElement BuildPorts()
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; portFilter = Field("", 170); FrameworkElement ph = Hinted(portFilter, Loc.T("Szűrés (port / program)")); ph.Margin = new Thickness(0, 0, 10, 6); portFilter.TextChanged += delegate { portSig = ""; RefreshPorts(); };
            portDevOnly = new CheckBox { Content = Loc.T("Csak fejlesztői"), IsChecked = LoadInt("portsdev", 1) == 1, Foreground = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 6) }; portDevOnly.Click += delegate { SaveInt("portsdev", portDevOnly.IsChecked == true ? 1 : 0); portSig = ""; RefreshPorts(); };
            TextBox kill = Field("", 90); FrameworkElement kh = Hinted(kill, "port"); kh.Margin = new Thickness(0, 0, 6, 6);
            top.Children.Add(ph); top.Children.Add(portDevOnly); top.Children.Add(Chip("📺 Terminal preview…", delegate { Ask(Loc.T("Előnézet a terminál alatt – cím vagy port"), "http://localhost:5173", Loc.T("Megnyit"), delegate (string s) { s = s.Trim(); int p; if (int.TryParse(s, out p)) s = "http://localhost:" + p; else if (s.Length > 0 && s.IndexOf("://") < 0) s = "http://" + s; if (s.Length > 0) Preview(s); }); }, 3)); top.Children.Add(kh);
            top.Children.Add(Chip(Loc.T("⛔ Port felszabadítása"), delegate { int pt; if (!int.TryParse(kill.Text.Trim(), out pt)) { Toast(Loc.T("Írj be egy port-számot")); return; } List<PortInfo> l = ReadListeners().FindAll(delegate (PortInfo x) { return x.Port == pt; }); if (l.Count == 0) { Toast("A " + pt + Loc.T(" port szabad")); return; } KillPort(l[0]); }, 2));
            portInfo = Txt("", 12, Muted, false); portInfo.Margin = new Thickness(8, 0, 0, 6); top.Children.Add(portInfo); Grid.SetRow(top, 0); g.Children.Add(top);
            portList = new StackPanel(); ScrollViewer sv = Scroll(portList); Grid.SetRow(sv, 1); g.Children.Add(sv);
            portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; portTimer.Tick += delegate { RefreshPorts(); }; RefreshPorts(); return g;
        }
        void PortsVisibility()
        {
            if (portTimer == null) return; bool want = cur == PgPorts && IsVisible; if (want && !portTimer.IsEnabled) { RefreshPorts(); portTimer.Start(); } else if (!want && portTimer.IsEnabled) portTimer.Stop();
        }
        void RefreshPorts()
        {
            if (portList == null) return; List<PortInfo> all; try { all = ReadListeners(); } catch { return; }
            string q = portFilter.Text.Trim().ToLowerInvariant(); bool dev = portDevOnly.IsChecked == true; List<PortInfo> show = all.FindAll(delegate (PortInfo p) { if (dev && !IsDevPort(p)) return false; return q.Length == 0 || p.Port.ToString().Contains(q) || p.Proc.ToLowerInvariant().Contains(q); });
            StringBuilder sb = new StringBuilder(); foreach (PortInfo p in show) sb.Append(p.Port + ":" + p.Pid + ":" + p.Addr + ";"); portInfo.Text = show.Count + Loc.T(" port") + (dev ? " (" + all.Count + Loc.T(" összesen)") : "") + Loc.T(" · 3 mp-enként frissül");
            if (sb.ToString() == portSig) return; portSig = sb.ToString(); portList.Children.Clear();
            if (show.Count == 0) { portList.Children.Add(new TextBlock { Text = dev ? Loc.T("Most nem fut fejlesztői szerver. Indíts egyet (pl. egy projekt „dev” gombjával), és itt megjelenik.") : Loc.T("Nincs találat."), Foreground = Muted, FontSize = 13, Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap }); return; }
            foreach (PortInfo p in show)
            {
                PortInfo pi = p; Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock port = new TextBlock { Text = p.Port.ToString(), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = IsDevPort(p) ? Accent : Muted, VerticalAlignment = VerticalAlignment.Center };
                StackPanel mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(mid, 1); mid.Children.Add(Txt(p.Proc, 13, Brushes.White, true)); mid.Children.Add(Txt("PID " + p.Pid + "  ·  " + p.Addr, 11, Muted, false));
                StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(b, 2);
                string url = "http://localhost:" + p.Port; b.Children.Add(Chip(Loc.T("🌐 Megnyitás"), delegate { OpenUrl(url); }, 1)); b.Children.Add(Chip(Loc.T("📺 Terminal preview"), delegate { Preview(url); }, 3)); b.Children.Add(Chip(Loc.T("📋 URL"), delegate { Copy(url, Loc.T("Másolva: ") + url); })); b.Children.Add(Chip(Loc.T("⛔ Leállít"), delegate { KillPort(pi); }, 2));
                row.Children.Add(port); row.Children.Add(mid); row.Children.Add(b); Border it = new Border { Padding = new Thickness(10, 6, 6, 0), CornerRadius = new CornerRadius(10), Child = row, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2) }; it.MouseEnter += delegate { it.Background = Faint; }; it.MouseLeave += delegate { it.Background = Brushes.Transparent; }; portList.Children.Add(it);
            }
        }
        void Preview(string url) { if (PreviewInTerm != null) PreviewInTerm(url); else Toast(Loc.T("A terminál nem érhető el")); }
        void KillPort(PortInfo p)
        {
            Confirm(Loc.T("Folyamat leállítása"), p.Proc + " (PID " + p.Pid + ") a(z) " + p.Port + Loc.T(" porton.\nA nem mentett munka elveszhet."), Loc.T("Leállít"), delegate
            {
                try { if (p.Pid <= 4 || p.Pid == Process.GetCurrentProcess().Id) { Toast(Loc.T("Ezt a folyamatot nem állítom le")); return; } using (Process pr = Process.GetProcessById(p.Pid)) { if (!string.Equals(pr.ProcessName, p.Proc, StringComparison.OrdinalIgnoreCase)) { Toast(Loc.T("A PID közben másik folyamaté lett – frissítem a listát")); } else { KillTree(pr); Toast(Loc.T("✓ Leállítva: ") + p.Proc); } } } catch (Exception ex) { Toast(Loc.T("Nem sikerült: ") + ex.Message); }
                Dispatcher.BeginInvoke(new Action(delegate { portSig = ""; RefreshPorts(); }), DispatcherPriority.Background);
            });
        }

        // ================================================================== KÖRNYEZET
        StackPanel envPath, envMachine, envVars; TextBlock gitName, gitMail, sshInfo; bool machineShown;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, string l, uint flags, uint timeout, out UIntPtr res);
        static void BroadcastEnv() { ThreadPool.QueueUserWorkItem(delegate { try { UIntPtr r; SendMessageTimeout((IntPtr)0xFFFF, 0x1A, UIntPtr.Zero, "Environment", 2, 1500, out r); } catch { } }); }
        // a felhasználói környezet a registryből, kibontatlanul (a %VAR% hivatkozások megmaradnak)
        static string RawUserVar(string name) { try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment")) { if (k == null) return null; object v = k.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames); return v as string; } } catch { return null; } }
        static void WriteUserVar(string name, string value)
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                if (k == null) throw new InvalidOperationException(Loc.T("A felhasználói környezet nem írható"));
                if (value == null) k.DeleteValue(name, false); else k.SetValue(name, value, (value.IndexOf('%') >= 0 || string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase)) ? Microsoft.Win32.RegistryValueKind.ExpandString : Microsoft.Win32.RegistryValueKind.String);
            }
            BroadcastEnv(); cachedPath = null;
        }
        string BackupEnv()
        {
            try
            {
                StringBuilder sb = new StringBuilder(); sb.AppendLine("PATH(User)=" + (RawUserVar("Path") ?? ""));
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment")) { if (k != null) foreach (string n in k.GetValueNames()) { if (string.Equals(n, "Path", StringComparison.OrdinalIgnoreCase)) continue; string v = RawUserVar(n); if (v == null) continue; sb.AppendLine(n + "=" + (Regex.IsMatch(n, "(KEY|TOKEN|SECRET|PASSWORD|PASS)", RegexOptions.IgnoreCase) ? Loc.T("***(titkos, nem mentve)***") : v)); } }
                string f = Path.Combine(dataDir, "env-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"); File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false));
                try { string[] all = Directory.GetFiles(dataDir, "env-backup-*.txt"); Array.Sort(all); for (int i = 0; i < all.Length - 20; i++) { try { File.Delete(all[i]); } catch { } } } catch { }
                return f;
            }
            catch { return null; }
        }
        List<string> UserPath() { List<string> l = new List<string>(); string p = RawUserVar("Path") ?? ""; foreach (string x in p.Split(';')) if (x.Trim().Length > 0) l.Add(x.Trim()); return l; }
        void SavePath(List<string> l) { try { BackupEnv(); WriteUserVar("Path", string.Join(";", l.ToArray())); } catch (Exception ex) { Toast(Loc.T("A PATH nem menthető: ") + ex.Message); return; } RefreshEnv(); Toast(Loc.T("PATH mentve (biztonsági mentés: devhub mappa). Az új terminálok már ezt látják.")); }
        static bool DirOk(string p) { try { return Directory.Exists(Environment.ExpandEnvironmentVariables(p)); } catch { return false; } }

        FrameworkElement BuildEnv()
        {
            StackPanel root = new StackPanel();
            // --- git + ssh
            StackPanel gs = new StackPanel(); gs.Children.Add(Txt(Loc.T("Git és SSH"), 14, Brushes.White, true)); gitName = Txt(Loc.T("név: …"), 12.5, Muted, false); gitMail = Txt("e-mail: …", 12.5, Muted, false); sshInfo = Txt(Loc.T("SSH-kulcs: …"), 12.5, Muted, false); gitName.Margin = new Thickness(0, 6, 0, 0); gs.Children.Add(gitName); gs.Children.Add(gitMail); gs.Children.Add(sshInfo);
            WrapPanel gc = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            gc.Children.Add(Chip(Loc.T("✎ Név…"), delegate { Ask("git user.name", "", Loc.T("Mentés"), delegate (string v) { GitCfg("user.name", v); }); })); gc.Children.Add(Chip("✎ E-mail…", delegate { Ask("git user.email", "", Loc.T("Mentés"), delegate (string v) { GitCfg("user.email", v); }); }));
            gc.Children.Add(Chip(Loc.T("Alap ág: main"), delegate { GitCfg("init.defaultBranch", "main"); })); gc.Children.Add(Chip("autocrlf = true", delegate { GitCfg("core.autocrlf", "true"); })); gc.Children.Add(Chip("autocrlf = input", delegate { GitCfg("core.autocrlf", "input"); })); gc.Children.Add(Chip(Loc.T("Hitelesítő: manager"), delegate { GitCfg("credential.helper", "manager"); })); gc.Children.Add(Chip("pull.rebase = false", delegate { GitCfg("pull.rebase", "false"); }));
            gc.Children.Add(Chip(Loc.T("📜 Minden git-beállítás"), delegate { Term("git config", null, "git config --global --list"); })); gc.Children.Add(Chip(Loc.T("🔑 SSH-kulcs készítése…"), delegate { Ask(Loc.T("E-mail az SSH-kulcshoz"), "", Loc.T("Készít"), delegate (string m) { if (m.Trim().Length > 0) Term("SSH-kulcs", null, "ssh-keygen -t ed25519 -C " + PsQuote(m.Trim())); }); })); gc.Children.Add(Chip(Loc.T("📋 Nyilvános kulcs másolása"), delegate { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".ssh\id_ed25519.pub"); if (File.Exists(f)) Copy(File.ReadAllText(f).Trim(), Loc.T("Nyilvános SSH-kulcs másolva")); else Toast(Loc.T("Még nincs id_ed25519.pub – készíts kulcsot")); })); gc.Children.Add(Chip(Loc.T("🐙 GitHub SSH-kulcsok"), delegate { OpenUrl("https://github.com/settings/keys"); }));
            gs.Children.Add(gc); root.Children.Add(CardBox(gs));
            // --- PATH
            StackPanel ps = new StackPanel(); ps.Children.Add(Txt(Loc.T("PATH (felhasználói) – adminisztrátori jog nélkül szerkeszthető"), 14, Brushes.White, true));
            WrapPanel pc = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) }; pc.Children.Add(Chip(Loc.T("+ Mappa hozzáadása"), delegate { using (Forms.FolderBrowserDialog dlg = new Forms.FolderBrowserDialog { Description = Loc.T("Melyik mappa kerüljön a PATH-ba?") }) { if (dlg.ShowDialog() == Forms.DialogResult.OK) { List<string> l = UserPath(); if (!l.Exists(delegate (string x) { return string.Equals(x, dlg.SelectedPath, StringComparison.OrdinalIgnoreCase); })) { l.Add(dlg.SelectedPath); SavePath(l); } else Toast(Loc.T("Már szerepel a PATH-ban")); } } }, 1));
            pc.Children.Add(Chip(Loc.T("🧹 Hibás és duplikált elemek törlése"), delegate { List<string> l = UserPath(), n = new List<string>(); foreach (string x in l) { if (!DirOk(x)) continue; if (n.Exists(delegate (string y) { return string.Equals(y, x, StringComparison.OrdinalIgnoreCase); })) continue; n.Add(x); } if (n.Count == l.Count) Toast(Loc.T("Nincs mit törölni – a PATH rendben van")); else SavePath(n); }));
            pc.Children.Add(Chip(Loc.T("👁 Rendszer PATH"), delegate { machineShown = !machineShown; RefreshEnv(); })); pc.Children.Add(Chip(Loc.T("📂 Biztonsági mentések"), delegate { OpenFolder(dataDir); })); ps.Children.Add(pc);
            envPath = new StackPanel(); ps.Children.Add(envPath); envMachine = new StackPanel(); ps.Children.Add(envMachine); root.Children.Add(CardBox(ps));
            // --- változók
            StackPanel vs = new StackPanel(); vs.Children.Add(Txt(Loc.T("Felhasználói környezeti változók"), 14, Brushes.White, true)); WrapPanel vc = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            vc.Children.Add(Chip(Loc.T("+ Új változó…"), delegate { Ask(Loc.T("Változó neve (pl. OPENAI_API_KEY)"), "", Loc.T("Tovább"), delegate (string n) { n = n.Trim(); if (!Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$")) { Toast(Loc.T("Érvénytelen név")); return; } if (Regex.IsMatch(n, "^(path|pathext|temp|tmp|comspec|systemroot|windir|systemdrive|userprofile|appdata|localappdata)$", RegexOptions.IgnoreCase)) { Toast(Loc.T("Ez a név védett")); return; } Ask(Loc.T("Érték: ") + n, "", Loc.T("Mentés"), delegate (string v) { if (v.Length == 0) { Toast(Loc.T("Üres érték nem menthető")); return; } try { BackupEnv(); WriteUserVar(n, v); } catch (Exception ex) { Toast(Loc.T("Nem sikerült: ") + ex.Message); return; } RefreshEnv(); Toast(n + Loc.T(" mentve – új terminálban látszik")); }); }); }, 1));
            vc.Children.Add(Chip(Loc.T("⚙ Rendszer-ablak (sysdm)"), delegate { try { Process.Start("rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"); } catch { } })); vc.Children.Add(Chip(Loc.T("🧾 hosts fájl (admin)"), delegate { try { Process.Start(new ProcessStartInfo("notepad.exe", @"C:\Windows\System32\drivers\etc\hosts") { Verb = "runas", UseShellExecute = true }); } catch { } })); vs.Children.Add(vc);
            envVars = new StackPanel(); vs.Children.Add(envVars); root.Children.Add(CardBox(vs));
            RefreshEnv(); return Scroll(root);
        }
        void GitCfg(string key, string val) { if (FindExe("git") == null) { Toast(Loc.T("A git nincs telepítve")); return; } Bg(delegate { try { int ex; Git(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "config --global " + key + " \"" + val.Replace("\"", "").Replace("\r", "").Replace("\n", "") + "\"", 8000, out ex); return ex; } catch { return -1; } }, delegate (int ex) { Toast(ex == 0 ? "✓ " + key + " = " + val : Loc.T("A beállítás nem sikerült")); RefreshEnv(); }); }

        void RefreshEnv()
        {
            if (envPath == null) return; envPath.Children.Clear(); List<string> l = UserPath(); HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int bad = 0;
            for (int i = 0; i < l.Count; i++)
            {
                string p = l[i]; int idx = i; bool ok = DirOk(p), dup = !seen.Add(Environment.ExpandEnvironmentVariables(p)); if (!ok) bad++;
                Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock mark = Txt(ok ? (dup ? "⧉" : "✓") : "⚠", 13, ok ? (dup ? Orange : Green) : Red, true); TextBlock t = new TextBlock { Text = p, FontFamily = Mono, FontSize = 12, Foreground = ok ? Brushes.White : Red, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = ok ? (dup ? Loc.T("Duplikált elem") : p) : Loc.T("A mappa nem létezik") }; Grid.SetColumn(t, 1);
                StackPanel b = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(b, 2); Border up = Chip("↑", idx > 0 ? (Action)delegate { List<string> n = UserPath(); if (idx <= 0 || idx >= n.Count) return; string x = n[idx]; n.RemoveAt(idx); n.Insert(idx - 1, x); SavePath(n); } : null); Border dn = Chip("↓", idx < l.Count - 1 ? (Action)delegate { List<string> n = UserPath(); if (idx < 0 || idx >= n.Count - 1) return; string x = n[idx]; n.RemoveAt(idx); n.Insert(idx + 1, x); SavePath(n); } : null); Border op = Chip("📂", ok ? (Action)delegate { OpenFolder(Environment.ExpandEnvironmentVariables(p)); } : null); Border rm = Chip("✕", delegate { List<string> n = UserPath(); if (idx < n.Count) { n.RemoveAt(idx); SavePath(n); } }, 2);
                foreach (Border c in new Border[] { up, dn, op, rm }) { c.Margin = new Thickness(0, 0, 3, 0); c.Padding = new Thickness(8, 2, 8, 2); b.Children.Add(c); }
                g.Children.Add(mark); g.Children.Add(t); g.Children.Add(b); g.Margin = new Thickness(0, 2, 0, 2); envPath.Children.Add(g);
            }
            if (l.Count == 0) envPath.Children.Add(Txt(Loc.T("A felhasználói PATH üres."), 12, Muted, false)); else envPath.Children.Add(new TextBlock { Text = l.Count + Loc.T(" elem") + (bad > 0 ? " · " + bad + Loc.T(" hibás") : Loc.T(" · mind létezik")), Foreground = bad > 0 ? Orange : Muted, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) });
            envMachine.Children.Clear(); if (machineShown) { envMachine.Children.Add(Head(Loc.T("Rendszer PATH (csak olvasható)"))); foreach (string x in (Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "").Split(';')) { if (x.Trim().Length == 0) continue; bool ok = DirOk(x); envMachine.Children.Add(new TextBlock { Text = (ok ? "✓  " : "⚠  ") + x, FontFamily = Mono, FontSize = 11.5, Foreground = ok ? Muted : Red, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis }); } }
            envVars.Children.Clear(); List<System.Collections.DictionaryEntry> vars = new List<System.Collections.DictionaryEntry>(); foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User)) { string k = (string)e.Key; if (string.Equals(k, "Path", StringComparison.OrdinalIgnoreCase) || k == "TEMP" || k == "TMP") continue; vars.Add(e); }
            vars.Sort(delegate (System.Collections.DictionaryEntry a, System.Collections.DictionaryEntry b) { return string.Compare((string)a.Key, (string)b.Key, StringComparison.OrdinalIgnoreCase); });
            foreach (System.Collections.DictionaryEntry e in vars)
            {
                string k = (string)e.Key, v = RawUserVar((string)e.Key) ?? (string)e.Value; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool secret = Regex.IsMatch(k, "(KEY|TOKEN|SECRET|PASSWORD|PASS)", RegexOptions.IgnoreCase); TextBlock kt = new TextBlock { Text = k, FontFamily = Mono, FontSize = 12, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; TextBlock vt = new TextBlock { Text = secret ? new string('•', Math.Min(24, v.Length)) : v, FontFamily = Mono, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = secret ? Loc.T("Érzékeny érték elrejtve") : v }; Grid.SetColumn(vt, 1);
                StackPanel b = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(b, 2); Border ed = Chip("✎", delegate { Ask(Loc.T("Új érték: ") + k, v, Loc.T("Mentés"), delegate (string nv) { if (nv.Length == 0) { Toast(Loc.T("Üres érték nem menthető (törléshez használd az ✕-et)")); return; } try { BackupEnv(); WriteUserVar(k, nv); } catch (Exception ex) { Toast(Loc.T("Nem sikerült: ") + ex.Message); return; } RefreshEnv(); Toast(k + Loc.T(" frissítve")); }); }); Border cp = Chip("📋", delegate { Copy(v, k + Loc.T(" értéke másolva")); }); Border rm = Chip("✕", delegate { Confirm(Loc.T("Változó törlése"), k + Loc.T(" törlése a felhasználói környezetből?"), Loc.T("Töröl"), delegate { try { BackupEnv(); WriteUserVar(k, null); } catch (Exception ex) { Toast(Loc.T("Nem sikerült: ") + ex.Message); return; } RefreshEnv(); }); }, 2);
                foreach (Border c in new Border[] { ed, cp, rm }) { c.Margin = new Thickness(0, 0, 3, 0); c.Padding = new Thickness(8, 2, 8, 2); b.Children.Add(c); }
                g.Children.Add(kt); g.Children.Add(vt); g.Children.Add(b); g.Margin = new Thickness(0, 2, 0, 2); envVars.Children.Add(g);
            }
            if (vars.Count == 0) envVars.Children.Add(Txt(Loc.T("Nincs saját környezeti változó."), 12, Muted, false));
            string pub = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".ssh\id_ed25519.pub"); bool hasKey = File.Exists(pub) || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".ssh\id_rsa.pub")); sshInfo.Text = Loc.T("SSH-kulcs: ") + (hasKey ? Loc.T("✓ van") : Loc.T("✗ még nincs (készíts egyet a GitHubhoz)")); sshInfo.Foreground = hasKey ? Green : Muted;
            if (FindExe("git") != null) Bg(delegate { int ex; string n = Git(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "config --global user.name", 5000, out ex); string m = Git(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "config --global user.email", 5000, out ex); return new string[] { FirstLine(n), FirstLine(m) }; }, delegate (string[] r) { if (r == null) return; gitName.Text = Loc.T("név: ") + (r[0].Length > 0 ? r[0] : Loc.T("— nincs beállítva")); gitName.Foreground = r[0].Length > 0 ? Brushes.White : Orange; gitMail.Text = "e-mail: " + (r[1].Length > 0 ? r[1] : Loc.T("— nincs beállítva")); gitMail.Foreground = r[1].Length > 0 ? Brushes.White : Orange; });
            else { gitName.Text = Loc.T("A git nincs telepítve (Eszközök → Git)"); gitMail.Text = ""; }
        }

        // ================================================================== GYORSESZKÖZÖK (szövegből – vágólapról)
        TextBox qIn, qOut, qRegex; CheckBox qAuto; TextBlock qInfo;
        class QTool { public string Group, Name; public Func<string, string> F; }
        List<QTool> qTools = new List<QTool>();
        static readonly Random Rng = new Random();

        FrameworkElement BuildQuick()
        {
            StackPanel root = new StackPanel(); qIn = Field("", 0); qIn.AcceptsReturn = true; qIn.TextWrapping = TextWrapping.Wrap; qIn.Height = 120; qIn.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; qIn.FontFamily = Mono; qIn.FontSize = 12.5; qIn.VerticalContentAlignment = VerticalAlignment.Top;
            StackPanel inp = new StackPanel(); WrapPanel ih = new WrapPanel(); ih.Children.Add(Txt(Loc.T("Bemenet"), 13, Brushes.White, true)); ih.Children.Add(new Border { Width = 12 }); ih.Children.Add(Chip(Loc.T("📋 Beillesztés"), delegate { try { if (Clipboard.ContainsText()) qIn.Text = Clipboard.GetText(); } catch { } })); ih.Children.Add(Chip(Loc.T("📂 Fájl betöltése…"), delegate { using (Forms.OpenFileDialog dlg = new Forms.OpenFileDialog()) { if (dlg.ShowDialog() == Forms.DialogResult.OK) { try { FileInfo fi = new FileInfo(dlg.FileName); if (fi.Length > 4000000) { Toast(Loc.T("A fájl túl nagy (max 4 MB)")); return; } qIn.Text = File.ReadAllText(dlg.FileName); } catch (Exception ex) { Toast(ex.Message); } } } })); ih.Children.Add(Chip(Loc.T("✕ Törlés"), delegate { qIn.Text = ""; qOut.Text = ""; }));
            qAuto = new CheckBox { Content = Loc.T("Eredmény automatikus másolása"), IsChecked = LoadInt("qauto", 1) == 1, Foreground = Muted, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 6) }; qAuto.Click += delegate { SaveInt("qauto", qAuto.IsChecked == true ? 1 : 0); }; ih.Children.Add(qAuto);
            inp.Children.Add(ih); inp.Children.Add(qIn); root.Children.Add(CardBox(inp));
            qTools.Clear(); BuildQuickTools(); string grp = null; WrapPanel wp = null;
            foreach (QTool t in qTools) { if (t.Group != grp) { grp = t.Group; root.Children.Add(Head(grp)); wp = new WrapPanel(); root.Children.Add(wp); } QTool tt = t; wp.Children.Add(Chip(t.Name, delegate { RunQuick(tt); })); }
            root.Children.Add(Head(Loc.T("Reguláris kifejezés"))); WrapPanel rx = new WrapPanel(); qRegex = Field("", 360); qRegex.FontFamily = Mono; FrameworkElement rh = Hinted(qRegex, "minta, pl. \\d{3}-\\d{4}"); rh.Margin = new Thickness(0, 0, 8, 6); rx.Children.Add(rh); rx.Children.Add(Chip(Loc.T("Illesztések a bemeneten"), delegate { RunQuick(new QTool { Name = "regex", F = RegexTest }); }, 1)); root.Children.Add(rx);
            StackPanel outp = new StackPanel(); WrapPanel oh = new WrapPanel(); oh.Children.Add(Txt(Loc.T("Eredmény"), 13, Brushes.White, true)); oh.Children.Add(new Border { Width = 12 }); oh.Children.Add(Chip(Loc.T("📋 Másolás"), delegate { Copy(qOut.Text, Loc.T("Eredmény másolva")); }, 1)); oh.Children.Add(Chip(Loc.T("↑ Vissza a bemenetre"), delegate { qIn.Text = qOut.Text; })); qInfo = Txt("", 11.5, Muted, false); qInfo.Margin = new Thickness(8, 0, 0, 6); oh.Children.Add(qInfo);
            qOut = Field("", 0); qOut.IsReadOnly = true; qOut.AcceptsReturn = true; qOut.TextWrapping = TextWrapping.Wrap; qOut.MinHeight = 120; qOut.MaxHeight = 260; qOut.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; qOut.FontFamily = Mono; qOut.FontSize = 12.5; qOut.VerticalContentAlignment = VerticalAlignment.Top; outp.Children.Add(oh); outp.Children.Add(qOut);
            Border ob = CardBox(outp); ob.Margin = new Thickness(0, 12, 0, 10); root.Children.Add(ob); return Scroll(root);
        }
        void RunQuick(QTool t)
        {
            try { string r = t.F(qIn.Text ?? ""); qOut.Text = r; qInfo.Text = t.Name + " · " + r.Length + Loc.T(" karakter"); if (qAuto.IsChecked == true && r.Length > 0 && r.Length < 200000) { try { Clipboard.SetText(r); Toast("✓ " + t.Name + Loc.T(" – eredmény a vágólapon")); } catch { } } }
            catch (Exception ex) { qOut.Text = Loc.T("Hiba: ") + ex.Message; qInfo.Text = ""; }
        }
        void Q(string group, string name, Func<string, string> f) { qTools.Add(new QTool { Group = group, Name = name, F = f }); }

        static string[] Words(string s) { s = Regex.Replace(s, @"([a-z0-9])([A-Z])", "$1 $2"); return Regex.Split(s.Trim(), @"[^\p{L}\p{N}]+").Length == 0 ? new string[0] : Array.FindAll(Regex.Split(s.Trim(), @"[^\p{L}\p{N}]+"), delegate (string w) { return w.Length > 0; }); }
        static string Cap(string w) { return w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant(); }
        static string Slug(string s) { string n = s.Normalize(NormalizationForm.FormD); StringBuilder sb = new StringBuilder(); foreach (char c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c); return string.Join("-", Array.ConvertAll(Words(sb.ToString().Replace(Loc.T("ő"), "o").Replace(Loc.T("ű"), "u").Replace(Loc.T("Ő"), "O").Replace(Loc.T("Ű"), "U")), delegate (string w) { return w.ToLowerInvariant(); })); }
        static string B64Dec(string s) { s = s.Trim().Replace('-', '+').Replace('_', '/'); while (s.Length % 4 != 0) s += "="; return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        static string HashHex(HashAlgorithm a, string s) { byte[] h = a.ComputeHash(Encoding.UTF8.GetBytes(s)); StringBuilder sb = new StringBuilder(); foreach (byte b in h) sb.Append(b.ToString("x2")); return sb.ToString(); }
        static string RandStr(int n, string alphabet)
        {
            StringBuilder sb = new StringBuilder(); int lim = 256 - (256 % alphabet.Length); byte[] b = new byte[64];
            using (RandomNumberGenerator g = RandomNumberGenerator.Create()) { while (sb.Length < n) { g.GetBytes(b); foreach (byte x in b) { if (x < lim && sb.Length < n) sb.Append(alphabet[x % alphabet.Length]); } } }
            return sb.ToString();
        }
        string RegexTest(string input)
        {
            if (qRegex.Text.Length == 0) return Loc.T("Írj be egy reguláris kifejezést fent."); Regex r = new Regex(qRegex.Text, RegexOptions.Multiline, TimeSpan.FromSeconds(2)); MatchCollection ms = r.Matches(input); StringBuilder sb = new StringBuilder(); sb.AppendLine(ms.Count + Loc.T(" illesztés")); int n = 0;
            foreach (Match m in ms) { if (++n > 200) { sb.AppendLine("…"); break; } sb.AppendLine("[" + m.Index + "] " + m.Value); for (int g = 1; g < m.Groups.Count; g++) sb.AppendLine(Loc.T("    csoport ") + g + ": " + m.Groups[g].Value); }
            return sb.ToString();
        }

        void BuildQuickTools()
        {
            string S = Loc.T("Szöveg"), C = Loc.T("Kódolás"), J = "JSON", H = "Hash", G = Loc.T("Generátorok"), T = Loc.T("Idő és számok");
            Q(S, Loc.T("kisbetű"), delegate (string s) { return s.ToLowerInvariant(); }); Q(S, Loc.T("NAGYBETŰ"), delegate (string s) { return s.ToUpperInvariant(); }); Q(S, "camelCase", delegate (string s) { string[] w = Words(s); StringBuilder sb = new StringBuilder(); for (int i = 0; i < w.Length; i++) sb.Append(i == 0 ? w[i].ToLowerInvariant() : Cap(w[i])); return sb.ToString(); });
            Q(S, "PascalCase", delegate (string s) { StringBuilder sb = new StringBuilder(); foreach (string w in Words(s)) sb.Append(Cap(w)); return sb.ToString(); }); Q(S, "snake_case", delegate (string s) { return string.Join("_", Array.ConvertAll(Words(s), delegate (string w) { return w.ToLowerInvariant(); })); }); Q(S, "kebab-case", delegate (string s) { return string.Join("-", Array.ConvertAll(Words(s), delegate (string w) { return w.ToLowerInvariant(); })); });
            Q(S, "CONSTANT_CASE", delegate (string s) { return string.Join("_", Array.ConvertAll(Words(s), delegate (string w) { return w.ToUpperInvariant(); })); }); Q(S, "URL-slug", delegate (string s) { return Slug(s); });
            Q(S, Loc.T("Sorok rendezése"), delegate (string s) { List<string> l = new List<string>(s.Replace("\r", "").Split('\n')); l.Sort(StringComparer.CurrentCultureIgnoreCase); return string.Join("\n", l.ToArray()); }); Q(S, Loc.T("Duplikátumok törlése"), delegate (string s) { HashSet<string> h = new HashSet<string>(); List<string> o = new List<string>(); foreach (string l in s.Replace("\r", "").Split('\n')) if (h.Add(l)) o.Add(l); return string.Join("\n", o.ToArray()); });
            Q(S, Loc.T("Sorok fordítva"), delegate (string s) { List<string> l = new List<string>(s.Replace("\r", "").Split('\n')); l.Reverse(); return string.Join("\n", l.ToArray()); }); Q(S, Loc.T("Sorszámozás"), delegate (string s) { string[] l = s.Replace("\r", "").Split('\n'); StringBuilder sb = new StringBuilder(); for (int i = 0; i < l.Length; i++) sb.AppendLine((i + 1) + ". " + l[i]); return sb.ToString().TrimEnd(); });
            Q(S, Loc.T("Üres sorok és szóközök törlése"), delegate (string s) { List<string> o = new List<string>(); foreach (string l in s.Replace("\r", "").Split('\n')) if (l.Trim().Length > 0) o.Add(l.Trim()); return string.Join("\n", o.ToArray()); }); Q(S, Loc.T("Egy sorba"), delegate (string s) { return Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim(); });
            Q(S, Loc.T("Szám: karakter / szó / sor"), delegate (string s) { return s.Length + Loc.T(" karakter · ") + Words(s).Length + Loc.T(" szó · ") + s.Replace("\r", "").Split('\n').Length + Loc.T(" sor · ") + Encoding.UTF8.GetByteCount(s) + Loc.T(" bájt"); }); Q(S, "Lorem ipsum", delegate (string s) { return "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat."; });
            Q(C, Loc.T("Base64 ▸ kódol"), delegate (string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }); Q(C, Loc.T("Base64 ◂ dekódol"), delegate (string s) { return B64Dec(s); }); Q(C, Loc.T("URL ▸ kódol"), delegate (string s) { return Uri.EscapeDataString(s); }); Q(C, Loc.T("URL ◂ dekódol"), delegate (string s) { return Uri.UnescapeDataString(s.Replace("+", " ")); });
            Q(C, Loc.T("HTML ▸ kódol"), delegate (string s) { return System.Net.WebUtility.HtmlEncode(s); }); Q(C, Loc.T("HTML ◂ dekódol"), delegate (string s) { return System.Net.WebUtility.HtmlDecode(s); }); Q(C, "Escape (\\n, \\\")", delegate (string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t"); }); Q(C, "Unescape", delegate (string s) { return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\"); });
            Q(C, Loc.T("Hex ▸ szövegből"), delegate (string s) { StringBuilder sb = new StringBuilder(); foreach (byte b in Encoding.UTF8.GetBytes(s)) sb.Append(b.ToString("x2")).Append(' '); return sb.ToString().Trim(); }); Q(C, Loc.T("Hex ◂ szövegbe"), delegate (string s) { string h = Regex.Replace(s, @"[^0-9a-fA-F]", ""); byte[] b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16); return Encoding.UTF8.GetString(b); });
            Q(C, Loc.T("JWT dekódolás"), delegate (string s) { string[] p = s.Trim().Split('.'); if (p.Length < 2) throw new FormatException(Loc.T("Ez nem JWT (fejléc.tartalom.aláírás)")); string hd = JsonFmt(B64Dec(p[0]), true), pl = JsonFmt(B64Dec(p[1]), true); Match ex = Regex.Match(pl, "\"exp\"\\s*:\\s*(\\d+)"); string exp = ""; if (ex.Success) { DateTime d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(long.Parse(ex.Groups[1].Value)).ToLocalTime(); exp = Loc.T("\n\nLejár: ") + d.ToString("yyyy-MM-dd HH:mm:ss") + (d < DateTime.Now ? Loc.T("  (LEJÁRT)") : ""); } return Loc.T("// fejléc\n") + hd + "\n\n// tartalom\n" + pl + exp; });
            Q(J, Loc.T("JSON szépítés"), delegate (string s) { return JsonFmt(s, true); }); Q(J, Loc.T("JSON tömörítés"), delegate (string s) { return JsonFmt(s, false); }); Q(J, Loc.T("JSON érvényes?"), delegate (string s) { JsonFmt(s, false); return Loc.T("✓ Érvényes JSON"); });
            Q(H, "MD5", delegate (string s) { return HashHex(MD5.Create(), s); }); Q(H, "SHA-1", delegate (string s) { return HashHex(SHA1.Create(), s); }); Q(H, "SHA-256", delegate (string s) { return HashHex(SHA256.Create(), s); }); Q(H, "SHA-512", delegate (string s) { return HashHex(SHA512.Create(), s); });
            Q(H, Loc.T("Fájl SHA-256…"), delegate (string s) { using (Forms.OpenFileDialog dlg = new Forms.OpenFileDialog()) { if (dlg.ShowDialog() != Forms.DialogResult.OK) return qOut.Text; using (FileStream fs = File.OpenRead(dlg.FileName)) { byte[] h = SHA256.Create().ComputeHash(fs); StringBuilder sb = new StringBuilder(); foreach (byte b in h) sb.Append(b.ToString("x2")); return sb + "  " + Path.GetFileName(dlg.FileName); } } });
            Q(G, "UUID", delegate (string s) { return Guid.NewGuid().ToString(); }); Q(G, Loc.T("10 UUID"), delegate (string s) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < 10; i++) sb.AppendLine(Guid.NewGuid().ToString()); return sb.ToString().TrimEnd(); });
            Q(G, Loc.T("Jelszó (20)"), delegate (string s) { return RandStr(20, "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"); }); Q(G, Loc.T("Erős jelszó (32, jelekkel)"), delegate (string s) { return RandStr(32, "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%^&*-_=+"); }); Q(G, Loc.T("Véletlen hex (32)"), delegate (string s) { return RandStr(32, "0123456789abcdef"); }); Q(G, Loc.T("API-kulcs (sk_…)"), delegate (string s) { return "sk_" + RandStr(40, "abcdefghijklmnopqrstuvwxyz0123456789"); });
            Q(G, Loc.T("Véletlen szám 1–100"), delegate (string s) { return Rng.Next(1, 101).ToString(); });
            Q(T, Loc.T("Most: epoch + ISO"), delegate (string s) { DateTime n = DateTime.Now; long e = (long)(n.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; return "epoch (s):  " + e + "\nepoch (ms): " + e + "000\nISO (UTC):  " + n.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + Loc.T("\nhelyi:      ") + n.ToString("yyyy-MM-dd HH:mm:ss"); });
            Q(T, Loc.T("Epoch → dátum"), delegate (string s) { long v = long.Parse(s.Trim()); DateTime d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(v > 99999999999L ? v / 1000.0 : v); return "UTC:   " + d.ToString("yyyy-MM-dd HH:mm:ss") + Loc.T("\nhelyi: ") + d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); });
            Q(T, Loc.T("Dátum → epoch"), delegate (string s) { DateTime d = DateTime.Parse(s.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal); long e = (long)(d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; return e + "  (" + e + "000 ms)"; });
            Q(T, Loc.T("Szám → hex / bin / okt"), delegate (string s) { long v = long.Parse(s.Trim()); return "dec " + v + "\nhex 0x" + v.ToString("X") + "\nbin " + Convert.ToString(v, 2) + "\noct " + Convert.ToString(v, 8); }); Q(T, Loc.T("Hex → decimális"), delegate (string s) { return Convert.ToInt64(s.Trim().Replace("0x", ""), 16).ToString(); });
            Q(T, Loc.T("HEX szín → RGB"), delegate (string s) { string h = s.Trim().TrimStart('#'); if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2]; int r = Convert.ToInt32(h.Substring(0, 2), 16), g = Convert.ToInt32(h.Substring(2, 2), 16), b = Convert.ToInt32(h.Substring(4, 2), 16); return "rgb(" + r + ", " + g + ", " + b + ")"; });
            Q(T, Loc.T("RGB → HEX szín"), delegate (string s) { MatchCollection m = Regex.Matches(s, @"\d+"); if (m.Count < 3) throw new FormatException(Loc.T("Három szám kell (r g b)")); return "#" + int.Parse(m[0].Value).ToString("X2") + int.Parse(m[1].Value).ToString("X2") + int.Parse(m[2].Value).ToString("X2"); });
        }

        // saját JSON-szépítő / ellenőrző (nem kell hozzá külső könyvtár; hibánál megmondja, hol a baj)
        static string JsonFmt(string s, bool pretty)
        {
            int i = 0; StringBuilder sb = new StringBuilder(); JsonVal(s, ref i, sb, pretty, 0); JsonWs(s, ref i); if (i < s.Length) throw new FormatException(Loc.T("Váratlan karakter a(z) ") + i + Loc.T(". pozíción: '") + s[i] + "'"); return sb.ToString();
        }
        static void JsonWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static void JsonNl(StringBuilder sb, bool pretty, int lvl) { if (!pretty) return; sb.Append('\n'); sb.Append(' ', lvl * 2); }
        static readonly Regex JNum = new Regex(@"\G(-?\d+(\.\d+)?([eE][+-]?\d+)?|true|false|null)");
        static void JsonVal(string s, ref int i, StringBuilder sb, bool pretty, int lvl)
        {
            if (lvl > 200) throw new FormatException(Loc.T("Túl mély egymásba ágyazás"));
            JsonWs(s, ref i); if (i >= s.Length) throw new FormatException(Loc.T("Váratlan vég")); char c = s[i];
            if (c == '{' || c == '[')
            {
                char close = c == '{' ? '}' : ']'; sb.Append(c); i++; JsonWs(s, ref i); if (i < s.Length && s[i] == close) { sb.Append(close); i++; return; }
                while (true)
                {
                    JsonNl(sb, pretty, lvl + 1); JsonWs(s, ref i);
                    if (c == '{') { if (i >= s.Length || s[i] != '"') throw new FormatException(Loc.T("Kulcs (idézőjeles szöveg) kellene a(z) ") + i + Loc.T(". pozíción")); JsonStr(s, ref i, sb); JsonWs(s, ref i); if (i >= s.Length || s[i] != ':') throw new FormatException(Loc.T("Hiányzó ':' a(z) ") + i + Loc.T(". pozíción")); i++; sb.Append(pretty ? ": " : ":"); }
                    JsonVal(s, ref i, sb, pretty, lvl + 1); JsonWs(s, ref i); if (i >= s.Length) throw new FormatException(Loc.T("Lezáratlan ") + c);
                    if (s[i] == ',') { sb.Append(','); i++; continue; } if (s[i] == close) { i++; JsonNl(sb, pretty, lvl); sb.Append(close); return; } throw new FormatException(Loc.T("',' vagy '") + close + Loc.T("' kellene a(z) ") + i + Loc.T(". pozíción"));
                }
            }
            if (c == '"') { JsonStr(s, ref i, sb); return; }
            Match m = JNum.Match(s, i); if (!m.Success) throw new FormatException(Loc.T("Érvénytelen érték a(z) ") + i + Loc.T(". pozíción: '") + c + "'"); sb.Append(m.Value); i += m.Length;
        }
        static void JsonStr(string s, ref int i, StringBuilder sb)
        {
            int st = i; i++; while (i < s.Length) { if (s[i] == '\\') { i += 2; continue; } if (s[i] == '"') { i++; sb.Append(s, st, i - st); return; } i++; }
            throw new FormatException(Loc.T("Lezáratlan szöveg a(z) ") + st + Loc.T(". pozíciótól"));
        }

        // ================================================================== TAKARÍTÁS
        class CacheDef { public string Name, Note; public string[] Paths; public TextBlock Size; public long Bytes; public Border Row; }
        List<CacheDef> caches = new List<CacheDef>(); TextBlock cleanSummary, diskInfo; StackPanel cleanProj;
        static string Env(string s) { return Environment.ExpandEnvironmentVariables(s); }

        FrameworkElement BuildClean()
        {
            StackPanel root = new StackPanel(); caches.Clear();
            caches.Add(new CacheDef { Name = "npm cache", Note = Loc.T("letöltött csomagok"), Paths = new string[] { Env(@"%LOCALAPPDATA%\npm-cache") } });
            caches.Add(new CacheDef { Name = "pnpm store", Note = Loc.T("közös csomagtár (újra letöltődik)"), Paths = new string[] { Env(@"%LOCALAPPDATA%\pnpm\store"), Env(@"%LOCALAPPDATA%\pnpm-cache") } });
            caches.Add(new CacheDef { Name = "Yarn cache", Note = "", Paths = new string[] { Env(@"%LOCALAPPDATA%\Yarn\Cache") } });
            caches.Add(new CacheDef { Name = "pip cache", Note = Loc.T("Python csomagok"), Paths = new string[] { Env(@"%LOCALAPPDATA%\pip\Cache") } });
            caches.Add(new CacheDef { Name = Loc.T("NuGet csomagok"), Note = Loc.T(".NET – újra letöltődik"), Paths = new string[] { Env(@"%USERPROFILE%\.nuget\packages") } });
            caches.Add(new CacheDef { Name = "Gradle cache", Note = "", Paths = new string[] { Env(@"%USERPROFILE%\.gradle\caches") } });
            caches.Add(new CacheDef { Name = "Maven repo", Note = "", Paths = new string[] { Env(@"%USERPROFILE%\.m2\repository") } });
            caches.Add(new CacheDef { Name = "Cargo registry", Note = "Rust", Paths = new string[] { Env(@"%USERPROFILE%\.cargo\registry") } });
            caches.Add(new CacheDef { Name = "Go modul-cache", Note = "", Paths = new string[] { Env(@"%USERPROFILE%\go\pkg\mod") } });
            caches.Add(new CacheDef { Name = Loc.T("VS Code gyorsítótár"), Note = "Cache, CachedData, GPUCache…", Paths = new string[] { Env(@"%APPDATA%\Code\Cache"), Env(@"%APPDATA%\Code\CachedData"), Env(@"%APPDATA%\Code\Code Cache"), Env(@"%APPDATA%\Code\GPUCache") } });
            caches.Add(new CacheDef { Name = Loc.T("Ideiglenes fájlok (Temp)"), Note = Loc.T("használatban lévők kimaradnak"), Paths = new string[] { SafeTemp() } });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; top.Children.Add(Chip(Loc.T("↻ Méretek újramérése"), delegate { MeasureCaches(); }, 0)); top.Children.Add(Chip(Loc.T("🗑 Minden gyorsítótár törlése"), delegate { long sum = 0; foreach (CacheDef c in caches) sum += c.Bytes; Confirm(Loc.T("Minden gyorsítótár törlése"), "Kb. " + FmtSize(sum) + Loc.T(" szabadul fel. A csomagok szükség esetén újra letöltődnek (lassabb első telepítés)."), Loc.T("Töröl"), delegate { foreach (CacheDef c in caches) EmptyCache(c, false); }); }, 2));
            top.Children.Add(Chip("🐳 docker system prune", delegate { Confirm(Loc.T("Docker takarítás"), Loc.T("Törli a leállított konténereket, a nem használt hálózatokat és a címke nélküli képeket."), Loc.T("Futtat"), delegate { Term("Docker prune", null, "docker system prune -f"); }); })); top.Children.Add(Chip("npm cache clean", delegate { Term("npm cache", null, "npm cache clean --force"); })); top.Children.Add(Chip("pnpm store prune", delegate { Term("pnpm prune", null, "pnpm store prune"); })); top.Children.Add(Chip("pip cache purge", delegate { Term("pip cache", null, "python -m pip cache purge"); }));
            root.Children.Add(top); diskInfo = Txt("", 12, Muted, false); diskInfo.Margin = new Thickness(2, 0, 0, 4); root.Children.Add(diskInfo); cleanSummary = Txt("", 12, Muted, false); cleanSummary.Margin = new Thickness(2, 0, 0, 6); root.Children.Add(cleanSummary);
            root.Children.Add(Head(Loc.T("Gyorsítótárak"))); StackPanel list = new StackPanel(); root.Children.Add(list);
            foreach (CacheDef c in caches)
            {
                CacheDef cc = c; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel l = new StackPanel(); l.Children.Add(Txt(c.Name, 13, Brushes.White, true)); l.Children.Add(Txt(c.Note.Length > 0 ? c.Note + "  ·  " + c.Paths[0] : c.Paths[0], 11, Muted, false));
                c.Size = Txt("…", 13, Accent, true); c.Size.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(c.Size, 1); StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(b, 2); b.Children.Add(Chip("📂", delegate { foreach (string p in cc.Paths) if (Directory.Exists(p)) { OpenFolder(p); return; } Toast(Loc.T("A mappa nem létezik (még üres)")); })); b.Children.Add(Chip(Loc.T("🗑 Törlés"), delegate { Confirm(Loc.T("Törlés: ") + cc.Name, "A(z) " + cc.Name + Loc.T(" tartalma törlődik (") + FmtSize(cc.Bytes) + ").", Loc.T("Töröl"), delegate { EmptyCache(cc, true); }); }, 2));
                g.Children.Add(l); g.Children.Add(c.Size); g.Children.Add(b); Border it = new Border { Padding = new Thickness(10, 6, 6, 0), CornerRadius = new CornerRadius(10), Child = g, Background = Brushes.Transparent }; it.MouseEnter += delegate { it.Background = Faint; }; it.MouseLeave += delegate { it.Background = Brushes.Transparent; }; list.Children.Add(it);
            }
            root.Children.Add(Head(Loc.T("Projektek – node_modules és build-mappák"))); cleanProj = new StackPanel(); root.Children.Add(cleanProj);
            MeasureCaches(); LoadCleanProjects(); return Scroll(root);
        }
        void MeasureCaches()
        {
            try { DriveInfo di = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))); diskInfo.Text = "💾 " + di.Name + Loc.T(" szabad: ") + FmtSize(di.AvailableFreeSpace) + " / " + FmtSize(di.TotalSize); } catch { }
            foreach (CacheDef c in caches) c.Size.Text = "…"; List<CacheDef> l = new List<CacheDef>(caches);
            ThreadPool.QueueUserWorkItem(delegate
            {
                long total = 0;
                foreach (CacheDef c in l) { long s = 0; bool cap = false, any = false; foreach (string p in c.Paths) if (Directory.Exists(p)) { any = true; bool cp; s += DirSize(p, 8000, out cp); cap |= cp; } CacheDef cc = c; long ss = s; bool cp2 = cap, a2 = any; total += s; Dispatcher.BeginInvoke(new Action(delegate { cc.Bytes = ss; cc.Size.Text = a2 ? FmtSize(ss) + (cp2 ? "+" : "") : "—"; })); }
                long tt = total; Dispatcher.BeginInvoke(new Action(delegate { cleanSummary.Text = Loc.T("Összesen kb. ") + FmtSize(tt) + Loc.T(" törölhető gyorsítótár"); }));
            });
        }
        // a Temp mappa csak akkor törölhető, ha tényleg "temp" jellegű útvonal (átirányított TEMP esetén ne ürítsen valami szélesebbet)
        static string SafeTemp()
        {
            string t = Path.GetTempPath().TrimEnd((char)92); string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd((char)92);
            if (t.Length < 12 || t.IndexOf("temp", StringComparison.OrdinalIgnoreCase) < 0 || string.Equals(t, up, StringComparison.OrdinalIgnoreCase)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            return t;
        }
        void EmptyCache(CacheDef c, bool announce)
        {
            if (announce) Toast(Loc.T("Törlés: ") + c.Name + "…"); long before = c.Bytes;
            Bg(delegate
            {
                foreach (string root in c.Paths)
                {
                    if (!Directory.Exists(root)) continue; try { foreach (string f in Directory.GetFiles(root)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { } } } catch { }
                    try { foreach (string d in Directory.GetDirectories(root)) { try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) { Directory.Delete(d, true); continue; } } catch { } int ex; RunCmd("rmdir /s /q \"" + LongPath(d) + "\"", 300000, null, out ex); } } catch { }
                }
                long left = 0; foreach (string p in c.Paths) if (Directory.Exists(p)) { bool cp; left += DirSize(p, 6000, out cp); } return left;
            }, delegate (long left) { c.Bytes = left; c.Size.Text = FmtSize(left); Toast("✓ " + c.Name + ": " + FmtSize(Math.Max(0, before - left)) + Loc.T(" felszabadítva")); });
        }
        void LoadCleanProjects()
        {
            EnsureProjects(); cleanProj.Children.Clear(); if (projects.Count == 0) { cleanProj.Children.Add(Txt(Loc.T("Nincs projekt a listában (Projektek fül → Keresés a gépen)."), 12, Muted, false)); return; }
            string[] junk = { "node_modules", ".next", "dist", "build", "target", ".venv", "venv", "bin", "obj", "__pycache__" }; int n = 0;
            foreach (Proj p in projects)
            {
                foreach (string j in junk)
                {
                    string full = Path.Combine(p.Path, j); if (!Directory.Exists(full)) continue; if ((j == "bin" || j == "obj") && !File.Exists(Path.Combine(p.Path, Path.GetFileName(p.Path) + ".csproj")) && Directory.GetFiles(p.Path, "*.csproj").Length == 0) continue; n++;
                    Proj pp = p; string jj = j; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    StackPanel l = new StackPanel(); l.Children.Add(Txt(p.Name + " / " + j, 13, Brushes.White, true)); l.Children.Add(Txt(full, 11, Muted, false)); TextBlock sz = Txt("…", 13, Accent, true); sz.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(sz, 1);
                    StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(b, 2); Border del = null; del = Chip(Loc.T("🗑 Törlés"), delegate { Confirm(Loc.T("Törlés: ") + pp.Name + "/" + jj, full + Loc.T("\n\nA mappa véglegesen törlődik."), Loc.T("Töröl"), delegate { Toast(Loc.T("Törlés…")); Bg(delegate { int ex; RunCmd("rmdir /s /q \"" + LongPath(full) + "\"", 600000, null, out ex); return !Directory.Exists(full); }, delegate (bool ok) { Toast(ok ? Loc.T("✓ Törölve") : Loc.T("Nem sikerült teljesen")); if (ok) { sz.Text = "—"; del.Visibility = Visibility.Collapsed; } }); }); }, 2); b.Children.Add(del);
                    g.Children.Add(l); g.Children.Add(sz); g.Children.Add(b); Border it = new Border { Padding = new Thickness(10, 6, 6, 0), CornerRadius = new CornerRadius(10), Child = g, Background = Brushes.Transparent }; it.MouseEnter += delegate { it.Background = Faint; }; it.MouseLeave += delegate { it.Background = Brushes.Transparent; }; cleanProj.Children.Add(it);
                    SizeAsync(full, delegate (string s2) { sz.Text = s2; });
                }
            }
            if (n == 0) cleanProj.Children.Add(Txt(Loc.T("A projektekben nincs törölhető build- vagy függőség-mappa."), 12, Muted, false));
        }

        // ================================================================== DOCKER
        StackPanel dockerList; TextBlock dockerInfo;
        FrameworkElement BuildDocker()
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; top.Children.Add(Chip(Loc.T("↻ Frissítés"), delegate { RefreshDocker(); }, 1)); top.Children.Add(Chip(Loc.T("🧹 prune (leállítottak törlése)"), delegate { Confirm(Loc.T("Docker takarítás"), Loc.T("Törli a leállított konténereket, nem használt hálózatokat és címke nélküli képeket."), Loc.T("Futtat"), delegate { Term("Docker prune", null, "docker system prune -f"); }); })); top.Children.Add(Chip(Loc.T("📊 Lemezhasználat"), delegate { Term("Docker df", null, "docker system df"); })); top.Children.Add(Chip("🧩 compose ps", delegate { Term("compose ps", null, "docker compose ls"); }));
            dockerInfo = Txt("", 12, Muted, false); dockerInfo.Margin = new Thickness(8, 0, 0, 6); top.Children.Add(dockerInfo); Grid.SetRow(top, 0); g.Children.Add(top); dockerList = new StackPanel(); ScrollViewer sv = Scroll(dockerList); Grid.SetRow(sv, 1); g.Children.Add(sv); RefreshDocker(); return g;
        }
        void DockerMsg(string text, bool startBtn)
        {
            dockerList.Children.Clear(); dockerList.Children.Add(new TextBlock { Text = text, Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 0, 8) });
            if (FindExe("docker") == null) { WrapPanel w = new WrapPanel(); w.Children.Add(Chip(Loc.T("⬇ Docker Desktop telepítése"), delegate { WingetRun("install", "Docker.DockerDesktop", Loc.T("Telepítés: Docker Desktop")); }, 1)); dockerList.Children.Add(w); }
            else if (startBtn) { WrapPanel w = new WrapPanel(); w.Children.Add(Chip(Loc.T("▶ Docker Desktop indítása"), delegate { string p = Env(@"%ProgramFiles%\Docker\Docker\Docker Desktop.exe"); try { if (File.Exists(p)) { Process.Start(p); Toast(Loc.T("Docker Desktop indul… pár perc múlva frissíts")); } else Toast(Loc.T("A Docker Desktop nem található")); } catch { } }, 1)); dockerList.Children.Add(w); }
        }
        void RefreshDocker()
        {
            if (dockerList == null) return; if (FindExe("docker") == null) { DockerMsg(Loc.T("A Docker nincs telepítve."), false); return; } dockerInfo.Text = Loc.T("betöltés…");
            Bg(delegate
            {
                int ex; string ps = RunCmd("docker ps -a --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}\"", 20000, null, out ex); if (ex != 0) return new string[] { null, ps };
                string im = RunCmd("docker images --format \"{{.Repository}}:{{.Tag}}|{{.Size}}|{{.ID}}\"", 20000, null, out ex); return new string[] { ps, im };
            }, delegate (string[] r)
            {
                if (r == null || r[0] == null) { dockerInfo.Text = ""; DockerMsg(Loc.T("A Docker most nem érhető el – valószínűleg nem fut a Docker Desktop.\n\n") + Trunc(FirstLine(r != null ? r[1] : ""), 160), true); return; }
                dockerList.Children.Clear(); int run = 0, tot = 0; dockerList.Children.Add(Head(Loc.T("Konténerek")));
                foreach (string l in r[0].Replace("\r", "").Split('\n'))
                {
                    string[] c = l.Split('|'); if (c.Length < 5) continue; tot++; bool up = c[3].StartsWith("Up"); if (up) run++; string id = c[0], name = c[1], ports = c[4]; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock dot = Txt("●", 14, up ? Green : Muted, false); StackPanel m = new StackPanel(); Grid.SetColumn(m, 1); m.Children.Add(Txt(name, 13, Brushes.White, true)); m.Children.Add(Txt(c[2] + "  ·  " + c[3] + (ports.Length > 0 ? "  ·  " + Trunc(ports, 70) : ""), 11, Muted, false));
                    WrapPanel b = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Width = 330 }; Grid.SetColumn(b, 2);
                    b.Children.Add(Chip(up ? "⏹ Stop" : "▶ Start", delegate { Dock((up ? "stop " : "start ") + id); }, up ? 0 : 1)); b.Children.Add(Chip("🔄", delegate { Dock("restart " + id); })); b.Children.Add(Chip(Loc.T("📜 Napló"), delegate { Term(Loc.T("Napló: ") + name, null, "docker logs -f --tail 200 " + id); })); if (up) b.Children.Add(Chip("💻 Shell", delegate { Term("Shell: " + name, null, "docker exec -it " + id + " sh"); }));
                    Match pm = Regex.Match(ports, @":(\d+)->"); if (up && pm.Success) { string url = "http://localhost:" + pm.Groups[1].Value; b.Children.Add(Chip("🌐 " + pm.Groups[1].Value, delegate { OpenUrl(url); })); }
                    b.Children.Add(Chip("🗑", delegate { Confirm(Loc.T("Konténer törlése"), name + Loc.T(" konténer törlése (docker rm -f)?"), Loc.T("Töröl"), delegate { Dock("rm -f " + id); }); }, 2));
                    g.Children.Add(dot); g.Children.Add(m); g.Children.Add(b); Border it = new Border { Padding = new Thickness(8, 6, 6, 0), CornerRadius = new CornerRadius(10), Child = g, Background = Brushes.Transparent }; it.MouseEnter += delegate { it.Background = Faint; }; it.MouseLeave += delegate { it.Background = Brushes.Transparent; }; dockerList.Children.Add(it);
                }
                if (tot == 0) dockerList.Children.Add(Txt(Loc.T("Nincs konténer."), 12, Muted, false)); dockerInfo.Text = run + Loc.T(" fut / ") + tot + Loc.T(" konténer");
                dockerList.Children.Add(Head(Loc.T("Képek")));
                foreach (string l in (r[1] ?? "").Replace("\r", "").Split('\n'))
                {
                    string[] c = l.Split('|'); if (c.Length < 3) continue; string img = c[0], iid = c[2]; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock n = Txt(img, 12.5, Brushes.White, true); TextBlock s = Txt(c[1], 12, Accent, true); s.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(s, 1); StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(b, 2);
                    b.Children.Add(Chip(Loc.T("▶ Futtatás"), delegate { Term(Loc.T("Futtatás: ") + img, null, "docker run -it --rm " + iid); })); b.Children.Add(Chip("🗑", delegate { Confirm(Loc.T("Kép törlése"), img + Loc.T(" törlése?"), Loc.T("Töröl"), delegate { Dock("rmi " + iid); }); }, 2));
                    g.Children.Add(n); g.Children.Add(s); g.Children.Add(b); g.Margin = new Thickness(8, 3, 6, 3); dockerList.Children.Add(g);
                }
            });
        }
        void Dock(string args)
        {
            Toast("docker " + args.Split(' ')[0] + "…"); Bg(delegate { int ex; string o = RunCmd("docker " + args, 60000, null, out ex); return new KeyValuePair<int, string>(ex, o); }, delegate (KeyValuePair<int, string> r) { Toast(r.Key == 0 ? Loc.T("✓ Kész") : Loc.T("Hiba: ") + Trunc(FirstLine(r.Value), 120)); RefreshDocker(); });
        }
    }
}
