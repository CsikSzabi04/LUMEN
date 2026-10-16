// "For AI programming" mód (AI/dev folyamatok védelme + RAM-karbantartás) és "Fastening" (gyorsítás, RAM-kímélő).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassControlWpf
{
    public static class AiGuard
    {
        public static volatile bool On;
        static string Flag { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aimode.txt"); } }
        public static void Load() { try { On = File.Exists(Flag); } catch { } }
        public static void Set(bool on) { On = on; try { if (on) File.WriteAllText(Flag, "1"); else if (File.Exists(Flag)) File.Delete(Flag); } catch { } }

        // AI-eszközök és helyi modell-szerverek: aktív használat közben nem kerülnek összehúzásra
        static readonly string[] Ai = { "claude", "antigravity", "cursor", "windsurf", "code", "codex", "gemini", "ollama", "ollama app", "ollama_llama_server", "lmstudio", "lm studio", "llama-server", "llamafile", "koboldcpp", "trae", "kiro", "zed", "aider", "jan", "comfyui" };
        // fejlesztői futtatók: csak ha szerverként figyelnek (localhost port) vagy nemrég dolgoztak
        static readonly string[] Dev = { "node", "python", "pythonw", "java", "javaw", "dotnet", "deno", "bun", "php", "php-cgi", "ruby", "go", "esbuild", "vite", "httpd", "nginx", "mysqld", "postgres", "mongod", "redis-server", "docker", "com.docker.backend" };

        static volatile HashSet<int> prot; static string summary = ""; static Dictionary<int, double[]> act = new Dictionary<int, double[]>(); static DateTime lastPrune = DateTime.Now;
        public static string Summary { get { return summary; } }
        public static bool IsProtected(int pid) { HashSet<int> p = prot; return p != null && p.Contains(pid); }

        [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(IntPtr tbl, ref int size, bool sort, int af, int cls, int rsv);
        // pid -> figyelt portok (IPv4 + IPv6, csak LISTEN)
        static Dictionary<int, List<int>> ListenPorts()
        {
            Dictionary<int, List<int>> r = new Dictionary<int, List<int>>();
            foreach (int af in new int[] { 2, 23 })
            {
                int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 3, 0); if (size <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, false, af, 3, 0) != 0) continue;
                    int n = Marshal.ReadInt32(buf), rowSz = af == 2 ? 24 : 56, portOff = af == 2 ? 8 : 20, pidOff = af == 2 ? 20 : 52;
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr row = IntPtr.Add(buf, 4 + i * rowSz); int pid = Marshal.ReadInt32(row, pidOff); int pr = Marshal.ReadInt32(row, portOff); int port = ((pr & 0xFF) << 8) | ((pr >> 8) & 0xFF);
                        List<int> l; if (!r.TryGetValue(pid, out l)) { l = new List<int>(); r[pid] = l; } if (!l.Contains(port)) l.Add(port);
                    }
                }
                catch { }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return r;
        }

        public static void Scan()
        {
            try
            {
                Dictionary<int, List<int>> ports = ListenPorts(); HashSet<int> keep = new HashSet<int>(); Dictionary<string, int> grp = new Dictionary<string, int>(); List<int> pl = new List<int>(); HashSet<int> alive = new HashSet<int>();
                double now = (DateTime.Now - new DateTime(2020, 1, 1)).TotalSeconds;
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        alive.Add(p.Id); string n = p.ProcessName.ToLowerInvariant(); bool ai = Array.IndexOf(Ai, n) >= 0, dev = Array.IndexOf(Dev, n) >= 0; if (!ai && !dev) continue;
                        double cpu = 0; try { cpu = p.TotalProcessorTime.TotalSeconds; } catch { }
                        double[] st; if (!act.TryGetValue(p.Id, out st)) { st = new double[] { cpu, now }; act[p.Id] = st; } else { if (cpu - st[0] > 0.3) st[1] = now; st[0] = cpu; }
                        List<int> lp; bool listening = ports.TryGetValue(p.Id, out lp); double idle = now - st[1];
                        if (listening || (ai && idle < 600) || (dev && idle < 180))
                        {
                            keep.Add(p.Id); int c; grp.TryGetValue(n, out c); grp[n] = c + 1;
                            if (listening) foreach (int x in lp) if (!pl.Contains(x)) pl.Add(x);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                if ((DateTime.Now - lastPrune).TotalMinutes > 5) { lastPrune = DateTime.Now; List<int> dead = new List<int>(); foreach (int k in act.Keys) if (!alive.Contains(k)) dead.Add(k); foreach (int k in dead) act.Remove(k); }
                prot = keep;
                List<KeyValuePair<string, int>> g = new List<KeyValuePair<string, int>>(grp); g.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                StringBuilder sb = new StringBuilder(); for (int i = 0; i < g.Count && i < 4; i++) { if (i > 0) sb.Append(", "); sb.Append(g[i].Key); if (g[i].Value > 1) sb.Append(" ×" + g[i].Value); }
                pl.Sort(); string ps = ""; for (int i = 0; i < pl.Count && i < 4; i++) ps += (i > 0 ? ", " : "") + pl[i];
                summary = keep.Count == 0 ? Loc.T("nem látok futó AI / fejlesztői folyamatot") : keep.Count + Loc.T(" folyamat védve") + " (" + sb + ")" + (pl.Count > 0 ? " · localhost: " + ps : "");
            }
            catch { }
        }
    }

    public static class Fastener
    {
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int cls, ref PowerThrottle info, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [StructLayout(LayoutKind.Sequential)] struct PowerThrottle { public uint Version, ControlMask, StateMask; }

        // rekurzív, hibatűrő takarítás: ami zárolt / nincs jogod hozzá, azt átugorja
        static void Clean(string dir, DateTime cut, bool isRoot, ref long bytes, ref int files)
        {
            try { foreach (string f in Directory.GetFiles(dir)) { try { FileInfo fi = new FileInfo(f); if (fi.LastWriteTime < cut && fi.LastAccessTime < cut && (fi.Attributes & FileAttributes.ReadOnly) == 0) { long l = fi.Length; fi.Delete(); bytes += l; files++; } } catch { } } } catch { }
            try
            {
                foreach (string d in Directory.GetDirectories(dir))
                {
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    Clean(d, cut, false, ref bytes, ref files);
                    try { if (Directory.GetFileSystemEntries(d).Length == 0 && Directory.GetLastWriteTime(d) < cut) Directory.Delete(d); } catch { }
                }
            }
            catch { }
        }

        // 1) a háttérben sokat számoló programok kisebb prioritást + hatékonysági módot kapnak, 2) 3 napnál régebbi felhasználói Temp-fájlok törlése
        public static string Run()
        {
            int lowered = 0; long tempBytes = 0; int tempFiles = 0; List<string> names = new List<string>();
            try
            {
                uint fg; GetWindowThreadProcessId(GetForegroundWindow(), out fg); int self = Process.GetCurrentProcess().Id;
                Dictionary<int, double> t0 = new Dictionary<int, double>(); Dictionary<int, string> nm = new Dictionary<int, string>();
                foreach (Process p in Process.GetProcesses()) { try { if (p.Id > 4) { t0[p.Id] = p.TotalProcessorTime.TotalMilliseconds; nm[p.Id] = p.ProcessName.ToLowerInvariant(); } } catch { } finally { p.Dispose(); } }
                System.Threading.Thread.Sleep(1500);
                List<KeyValuePair<int, double>> busy = new List<KeyValuePair<int, double>>(); int cores = Environment.ProcessorCount;
                foreach (KeyValuePair<int, double> kv in t0)
                {
                    try { using (Process p = Process.GetProcessById(kv.Key)) { double pct = (p.TotalProcessorTime.TotalMilliseconds - kv.Value) / 1500.0 * 100.0 / cores; if (pct >= 2.0) busy.Add(new KeyValuePair<int, double>(kv.Key, pct)); } } catch { }
                }
                busy.Sort(delegate (KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return b.Value.CompareTo(a.Value); });
                foreach (KeyValuePair<int, double> b in busy)
                {
                    if (lowered >= 12) break; int pid = b.Key; string n = nm[pid];
                    if (pid == fg || pid == self || Array.IndexOf(MainWindow.Protected, n) >= 0 || AiGuard.IsProtected(pid)) continue;
                    try
                    {
                        using (Process p = Process.GetProcessById(pid))
                        {
                            if (p.PriorityClass == ProcessPriorityClass.Normal || p.PriorityClass == ProcessPriorityClass.AboveNormal || p.PriorityClass == ProcessPriorityClass.High) { p.PriorityClass = ProcessPriorityClass.BelowNormal; PowerThrottle pt = new PowerThrottle { Version = 1, ControlMask = 1, StateMask = 1 }; try { SetProcessInformation(p.Handle, 4, ref pt, 12); } catch { } lowered++; if (names.Count < 4 && !names.Contains(n)) names.Add(n); }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            try { Clean(Path.GetTempPath(), DateTime.Now.AddDays(-3), true, ref tempBytes, ref tempFiles); } catch { }
            return Loc.T("⚡ Gyorsítva: ") + lowered + Loc.T(" háttérprogram visszavéve") + (names.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + ")" : "") + " · Temp: " + tempFiles + Loc.T(" régi fájl törölve, ") + (tempBytes / 1048576.0).ToString("0") + " MB";
        }
    }
}
