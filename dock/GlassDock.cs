// GlassDock - kezzel mozgathato/atmeretezheto, uveges, lekerekitett dok (Windows 10, mindket monitoron).
//  * bal egerfogas a hatterre = mozgatas | a szelekre huzas = atmeretezes (az ikonok mindig kitoltik a meretet)
//  * gorgo = magassag/ikonmeret | Ctrl+gorgo = szelesseg | Shift+gorgo = uveghatas | jobb klikk = menu
//  * ikonra huzas = sorrend, kattintas = inditas/elohozas, Ctrl+huzas ikonrol = dok mozgatasa
// C# 5 kompatibilis (a gepen levo .NET Framework csc-vel fordul: Build.ps1)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    static class N
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [StructLayout(LayoutKind.Sequential)] struct ACCENT { public int State, Flags, Color, Anim; }
        [StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string cls, string title);
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential)] public struct DWMTHUMB { public int Flags; public RECT Dest; public RECT Src; public byte Opacity; [MarshalAs(UnmanagedType.Bool)] public bool Visible; [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly; }
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWMTHUMB p);
        public static bool In(POINT c, RECT r, int m) { return c.X >= r.L - m && c.X <= r.R + m && c.Y >= r.T - m && c.Y <= r.B + m; }

        // a Windows sajat tálca ikonjainak elrejtese (a dokk lecsereli oket); Start + tray + ora marad
        public static void HideTaskButtons(bool hide)
        {
            int cmd = hide ? 0 : 5;
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                IntPtr rb = FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
                IntPtr sw = rb == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(rb, IntPtr.Zero, "MSTaskSwWClass", null);
                if (sw != IntPtr.Zero && IsWindowVisible(sw) == hide) ShowWindow(sw, cmd);
            }
            IntPtr sec = IntPtr.Zero;
            while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                if (IsWindowVisible(sec) == hide) ShowWindow(sec, cmd);
        }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool alt);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string f, int idx, int cx, int cy, IntPtr[] ph, uint[] id, uint n, uint fl);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public static void Accent(IntPtr h, int state, int color)
        {
            ACCENT a = new ACCENT(); a.State = state; a.Flags = 2; a.Color = color;
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(a)); Marshal.StructureToPtr(a, p, false);
            WCAD d = new WCAD(); d.Attr = 19; d.Data = p; d.Size = Marshal.SizeOf(a);
            SetWindowCompositionAttribute(h, ref d); Marshal.FreeHGlobal(p);
        }

        [StructLayout(LayoutKind.Sequential)] struct BB { public int Flags; [MarshalAs(UnmanagedType.Bool)] public bool Enable; public IntPtr Rgn; [MarshalAs(UnmanagedType.Bool)] public bool Trans; }
        [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr h, ref BB b);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        // elmosas csak a lekerekitett teruleten (nincs SetWindowRgn: a sarkok a WPF-ben rajzolt, simitott alakzatbol jonnek)
        public static void Blur(IntPtr h, int w, int ht, int rpx, bool on)
        {
            BB b = new BB(); b.Flags = 3; b.Enable = on;
            IntPtr rg = on ? CreateRoundRectRgn(0, 0, w + 1, ht + 1, rpx * 2, rpx * 2) : IntPtr.Zero; b.Rgn = rg;
            DwmEnableBlurBehindWindow(h, ref b); if (rg != IntPtr.Zero) DeleteObject(rg);
        }
        [DllImport("user32.dll")] static extern int GetWindowRgnBox(IntPtr h, out RECT r);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
        public static bool CustomStart;   // sajat Start-menu: a bal pilula nem a tálca resze, hanem a mi (kattintható) ablakunk
        public static bool NoExclude;   // /noexcl: hibakereseshez (a dokk latszik a kepernyokepen, de elmosas nelkul)
        public static bool Exclude(IntPtr h) { return true; }   // NEM zarjuk ki a kepernyokepbol: a dokk a screenshotokon is latszik (a hatter-fotot az ablak megjelenese elott keszitjuk)
        [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public uint cb; public IntPtr hWnd; public uint msg, edge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m, ref APPBARDATA d);
        public static void TaskbarAutoHide(bool on)
        {
            APPBARDATA d = new APPBARDATA(); d.cb = (uint)Marshal.SizeOf(typeof(APPBARDATA)); d.hWnd = FindWindow("Shell_TrayWnd", null);
            bool isOn = (((ulong)SHAppBarMessage(4, ref d)) & 1) != 0;
            if (isOn != on) { d.lParam = (IntPtr)(on ? 3 : 2); SHAppBarMessage(0xA, ref d); }
        }

        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint f);
        // A Windows tálca automatikus elrejtesu (igy nem foglal helyet: a kimaximalizalt ablak a teljes kepernyot kitolti);
        // a mutatas/elrejtest a dokkal egyutt en vezerlem: mutatas = a lathato helyre teszem, elrejtes = ShowWindow(HIDE)
        public static void TaskbarShow(bool show)
        {
            IntPtr t = FindWindow("Shell_TrayWnd", null); if (t == IntPtr.Zero) return;
            RECT r; if (!GetWindowRect(t, out r)) return; int h = r.B - r.T; if (h < 10) return;
            int shownTop = Forms.Screen.FromHandle(t).Bounds.Bottom - h;
            if (show) { if (!IsWindowVisible(t)) ShowWindow(t, 8); if (r.T != shownTop) SetWindowPos(t, IntPtr.Zero, r.L, shownTop, r.R - r.L, h, 0x414); }   // NOZORDER|NOACTIVATE|NOSENDCHANGING
            else if (IsWindowVisible(t)) ShowWindow(t, 0);
        }
        // p: 0 = elrejtve (a kepernyo aljan kivul), 1 = latszik; kozte a tálca (a pilulak) felcsuszik / lecsuszik
        public static void TaskbarSlide(double p)
        {
            IntPtr t = FindWindow("Shell_TrayWnd", null); if (t == IntPtr.Zero) return;
            RECT r; if (!GetWindowRect(t, out r)) return; int h = r.B - r.T; if (h < 10) return;
            if (p <= 0.001) { if (IsWindowVisible(t)) ShowWindow(t, 0); return; }
            if (!IsWindowVisible(t)) ShowWindow(t, 8);
            int top = Forms.Screen.FromHandle(t).Bounds.Bottom - h + (int)Math.Round((1 - Math.Min(1.0, p)) * h);
            if (r.T != top) SetWindowPos(t, IntPtr.Zero, r.L, top, r.R - r.L, h, 0x414);
        }
        public static bool PillRects(out RECT lp, out RECT rp)
        {
            lp = new RECT(); rp = new RECT(); IntPtr tray = FindWindow("Shell_TrayWnd", null); if (tray == IntPtr.Zero) return false;
            RECT tr; if (!GetWindowRect(tray, out tr)) return false; int w = tr.R - tr.L, h = tr.B - tr.T; if (w < 300 || h < 20) return false;
            int bottom = Forms.Screen.FromHandle(tray).Bounds.Bottom, top = bottom - h; RECT s, n; int sx = 46, nx = w - 230;
            IntPtr start = FindWindowEx(tray, IntPtr.Zero, "Start", null), notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (start != IntPtr.Zero && GetWindowRect(start, out s) && s.R > s.L) sx = Math.Min(s.R - tr.L + 6, w / 3);
            if (notify != IntPtr.Zero && GetWindowRect(notify, out n) && n.R > n.L) nx = Math.Max(n.L - tr.L - 6, w * 2 / 3);
            lp.L = tr.L; lp.T = top; lp.R = tr.L + sx; lp.B = bottom; rp.L = tr.L + nx; rp.T = top; rp.R = tr.R; rp.B = bottom; return true;
        }
        public static void TaskbarVisible(bool v) { IntPtr t = FindWindow("Shell_TrayWnd", null); if (t != IntPtr.Zero && IsWindowVisible(t) != v) ShowWindow(t, v ? 8 : 0); }
        // a rejtett tálca helyet is lefoglal: a munkaterulet a teljes kepernyo marad (a pilulak a ablakok folott lebegnek)
        public static void FullWorkArea()
        {
            Forms.Screen p = Forms.Screen.PrimaryScreen;
            if (p.WorkingArea.Height != p.Bounds.Height || p.WorkingArea.Width != p.Bounds.Width)
            { RECT r = new RECT(); r.L = p.Bounds.Left; r.T = p.Bounds.Top; r.R = p.Bounds.Right; r.B = p.Bounds.Bottom; SystemParametersInfo(0x2F, 0, ref r, 2); }
        }
        public static void RestoreWorkArea()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null); RECT tr; if (tray == IntPtr.Zero || !GetWindowRect(tray, out tr)) return;
            Forms.Screen p = Forms.Screen.PrimaryScreen;
            if (tr.T < p.Bounds.Bottom - 10 && tr.T > p.Bounds.Top + 10) { RECT r = new RECT(); r.L = p.Bounds.Left; r.T = p.Bounds.Top; r.R = p.Bounds.Right; r.B = tr.T; SystemParametersInfo(0x2F, 0, ref r, 2); }
        }
        // nyitva van-e a Start/naptar/tálcaikon-felugro (ilyenkor a pilulak maradjanak)
        public static bool FlyoutOpen()
        {
            IntPtr fg = GetForegroundWindow(); if (fg == IntPtr.Zero) return false;
            StringBuilder c = new StringBuilder(64); GetClassName(fg, c, 64); string n = c.ToString();
            return n == "Windows.UI.Core.CoreWindow" || n == "NotifyIconOverflowWindow" || n == "TrayClockWClass";
        }
        static string pillSig = ""; static DateTime pillAccent = DateTime.MinValue;
        // A Windows tálca ketto resze (Start, ora/tray) marad meg; a hatterukat a Pill ablakok rajzoljak (mint a dokk), a tálca maga atlatszo.
        public static bool TaskbarPills(int radiusPx, out RECT lp, out RECT rp, out IntPtr tray)
        {
            lp = new RECT(); rp = new RECT(); tray = FindWindow("Shell_TrayWnd", null); if (tray == IntPtr.Zero) return false;
            RECT tr; if (!GetWindowRect(tray, out tr) || !IsWindowVisible(tray)) return false;
            int w = tr.R - tr.L, h = tr.B - tr.T; if (w < 300 || h < 20) return false;
            if (tr.T >= Forms.Screen.FromHandle(tray).Bounds.Bottom - 2) return false;          // teljesen a kepernyo alatt (elrejtve)
            IntPtr start = FindWindowEx(tray, IntPtr.Zero, "Start", null), notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT s, n; int sx = 46, nx = w - 230;
            if (start != IntPtr.Zero && GetWindowRect(start, out s) && s.R > s.L) sx = Math.Min(s.R - tr.L + 6, w / 3);
            if (notify != IntPtr.Zero && GetWindowRect(notify, out n) && n.R > n.L) nx = Math.Max(n.L - tr.L - 6, w * 2 / 3);
            RECT cur; int type = GetWindowRgnBox(tray, out cur); string sig = sx + "," + nx + "," + w + "," + h + "," + radiusPx + "," + CustomStart;
            if (type == 0 || sig != pillSig)
            {
                IntPtr a = CreateRoundRectRgn(0, 0, sx + 1, h + 1, radiusPx * 2, radiusPx * 2), b = CreateRoundRectRgn(nx, 0, w + 1, h + 1, radiusPx * 2, radiusPx * 2);
                IntPtr rgn; if (CustomStart) { DeleteObject(a); rgn = b; } else { CombineRgn(a, a, b, 2); DeleteObject(b); rgn = a; }
                if (SetWindowRgn(tray, rgn, true) == 0) DeleteObject(rgn); else pillSig = sig; pillAccent = DateTime.MinValue;
            }
            if ((DateTime.Now - pillAccent).TotalMilliseconds > 600) { Accent(tray, 2, 0x01000000); pillAccent = DateTime.Now; }   // teljesen atlatszo tálca
            lp.L = tr.L; lp.T = tr.T; lp.R = tr.L + sx; lp.B = tr.B; rp.L = tr.L + nx; rp.T = tr.T; rp.R = tr.R; rp.B = tr.B;
            return true;
        }
        public static void TaskbarPillsOff()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) { SetWindowRgn(tray, IntPtr.Zero, true); Accent(tray, 0, 0); }
            pillSig = "";
        }
        // sima ablak, de a DWM figyelembe veszi a WPF alfa-csatornajat (egy 1 px-es "elmosas" kapcsolja be); a tenyleges uveget a Frost rajzolja
        public static void PlainWindow(IntPtr h) { Accent(h, 0, 0); SetWindowRgn(h, IntPtr.Zero, false); Blur(h, 1, 1, 0, true); }

        public static int Tint(int a, int alphaAdd)
        {
            int al = Math.Max(6, Math.Min(240, a + alphaAdd));
            return unchecked((al << 24) | (26 << 16) | (20 << 8) | 17);   // ABGR: sotet, enyhen kekes uveg
        }

        public static BitmapSource Own(BitmapSource s) { FormatConvertedBitmap f = new FormatConvertedBitmap(s, PixelFormats.Pbgra32, null, 0); int w = f.PixelWidth, h = f.PixelHeight; byte[] px = new byte[w * h * 4]; f.CopyPixels(px, w * 4, 0); BitmapSource r = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4); r.Freeze(); return r; }
        public static BitmapSource Mono(BitmapSource src) { return Mono(src, 52, 0.84); }
        public static BitmapSource Mono(BitmapSource src, double floor, double gain)
        {
            try
            {
                BitmapSource f = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
                int w = f.PixelWidth, h = f.PixelHeight, st = w * 4; byte[] px = new byte[st * h]; f.CopyPixels(px, st, 0);
                for (int i = 0; i < px.Length; i += 4)
                {
                    int a = px[i + 3]; if (a == 0) continue;
                    double b = px[i] * 255.0 / a, g = px[i + 1] * 255.0 / a, r = px[i + 2] * 255.0 / a;
                    double y = 0.299 * r + 0.587 * g + 0.114 * b, o = Math.Min(255.0, floor + y * gain);
                    byte v = (byte)(o * a / 255.0); px[i] = v; px[i + 1] = v; px[i + 2] = v;
                }
                BitmapSource res = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, st); res.Freeze(); return res;
            }
            catch { return src; }
        }

        public static string ExePath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try { StringBuilder sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
            finally { CloseHandle(h); }
        }

        public static BitmapSource Icon(string path, int px)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                IntPtr[] h = new IntPtr[1]; uint[] id = new uint[1];
                uint n = PrivateExtractIcons(path, 0, px, px, h, id, 1, 0);
                if (n >= 1 && h[0] != IntPtr.Zero)
                {
                    try { BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(h[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); b.Freeze(); return b; } finally { DestroyIcon(h[0]); }
                }
            }
            catch { }
            try
            {
                using (System.Drawing.Icon ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    b.Freeze(); return b;
                }
            }
            catch { return null; }
        }

        public static string ResolveLnk(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                string tp = (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                if (!string.IsNullOrEmpty(tp) && File.Exists(tp)) return tp;
                string il = (string)sc.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null);   // pl. File Explorer: nincs target, de az ikon az explorer.exe
                if (!string.IsNullOrEmpty(il)) { int c = il.LastIndexOf(','); string ip = Environment.ExpandEnvironmentVariables(c > 0 ? il.Substring(0, c) : il); if (File.Exists(ip) && ip.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return ip; }
                return tp;
            }
            catch { return null; }
        }
    }

    class Settings
    {
        // A fájlt a GlassControl is írja. Mentéskor és újratöltéskor háromutas összefésülés: ami a fájlban változott, azt átvesszük, ami nálunk, azt megtartjuk.
        Dictionary<string, string> d = new Dictionary<string, string>(), written = new Dictionary<string, string>(); string path; DateTime stamp = DateTime.MinValue; public int Version;
        public Settings(string p) { path = p; bool ok; Merge(out ok); try { stamp = File.GetLastWriteTimeUtc(path); } catch { } }
        Dictionary<string, string> ReadFile()
        {
            Dictionary<string, string> e = new Dictionary<string, string>();
            try { if (File.Exists(path)) foreach (string l in File.ReadAllLines(path)) { int i = l.IndexOf('='); if (i > 0) e[l.Substring(0, i)] = l.Substring(i + 1); } } catch { return null; }      // átmeneti olvasási hiba (a másik program éppen ír): nem dobjuk el a beállításokat
            return e;
        }
        bool Merge(out bool ok)
        {
            ok = false; Dictionary<string, string> e = ReadFile(); if (e == null) return false; if (e.Count == 0 && written.Count > 3) return false;     // üres/félbehagyott fájl: később újra
            ok = true; bool ch = false;
            foreach (KeyValuePair<string, string> kv in e) { string w; if (!written.TryGetValue(kv.Key, out w) || w != kv.Value) { d[kv.Key] = kv.Value; written[kv.Key] = kv.Value; ch = true; } }
            foreach (string k in new List<string>(written.Keys)) if (!e.ContainsKey(k)) { string cur; if (d.TryGetValue(k, out cur) && cur == written[k]) d.Remove(k); written.Remove(k); ch = true; }
            return ch;
        }
        public bool Poll() { DateTime t; try { t = File.GetLastWriteTimeUtc(path); } catch { return false; } if (t == stamp) return false; bool ok; bool ch = Merge(out ok); if (!ok) return false; stamp = t; if (ch) { Version++; return true; } return false; }
        public double Get(string k, double def) { string s; double v; return d.TryGetValue(k, out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        public string GetStr(string k, string def) { string s; return d.TryGetValue(k, out s) && s.Length > 0 ? s : def; }
        public void Set(string k, double v) { d[k] = v.ToString("0.###", CultureInfo.InvariantCulture); }
        public void Remove(string prefix) { foreach (string k in new List<string>(d.Keys)) if (k.StartsWith(prefix)) d.Remove(k); }
        public void Save()
        {
            try { bool ok; if (Merge(out ok)) Version++; List<string> l = new List<string>(); foreach (KeyValuePair<string, string> kv in d) l.Add(kv.Key + "=" + kv.Value); string tmp = path + ".tmp"; File.WriteAllLines(tmp, l.ToArray()); if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); written = new Dictionary<string, string>(d); stamp = File.GetLastWriteTimeUtc(path); } catch { }
        }
    }

    class Item
    {
        public string Key, Launch, Name, IconPath; public bool Pinned, IsStart; public List<IntPtr> Wins = new List<IntPtr>();
    }

    class Model
    {
        static Dictionary<IntPtr, uint> uwpPid = new Dictionary<IntPtr, uint>();
        public List<Item> Pinned = new List<Item>(); public List<Item> Run = new List<Item>();
        public static Action StartAction; public Settings S; public string PinFile; public Item StartItem = new Item();
        List<string> order = new List<string>(); string sig = ""; uint self = (uint)Process.GetCurrentProcess().Id;
        public IntPtr Fg;

        public Model(Settings s, string dir)
        {
            S = s; PinFile = Path.Combine(dir, "pinned.txt"); StartItem.IsStart = true; StartItem.Name = "Start";
            bool pinOk = false; try { if (File.Exists(PinFile)) { foreach (string l in File.ReadAllLines(PinFile)) { string[] p = l.Split('|'); if (p.Length >= 3) Pinned.Add(Mk(p[0], p[1], p[2], true)); } pinOk = true; } } catch { pinOk = true; }
            if (!pinOk) Seed();
        }
        static Item Mk(string launch, string key, string name, bool pinned)
        {
            Item i = new Item(); i.Launch = launch; i.Key = key; i.Name = name; i.Pinned = pinned;
            i.IconPath = File.Exists(key) ? key : launch; return i;
        }
        void Seed()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.lnk"))
                {
                    string t = N.ResolveLnk(f); string key = (!string.IsNullOrEmpty(t) && File.Exists(t)) ? t.ToLowerInvariant() : f;
                    Pinned.Add(Mk(f, key, Path.GetFileNameWithoutExtension(f), true));
                }
            SavePins();
        }
        public void SavePins()
        {
            List<string> l = new List<string>(); foreach (Item i in Pinned) l.Add(i.Launch + "|" + i.Key + "|" + i.Name);
            try { File.WriteAllLines(PinFile, l.ToArray()); } catch { }
        }
        public void Pin(Item it) { if (it.Pinned) return; Item p = Mk(it.Launch ?? it.Key, it.Key, it.Name, true); p.Wins = it.Wins; Pinned.Add(p); SavePins(); }
        public void Unpin(Item it) { Pinned.Remove(it); SavePins(); }
        public void AddFile(string f)
        {
            string key = f, ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".lnk") { string t = N.ResolveLnk(f); if (!string.IsNullOrEmpty(t) && File.Exists(t)) key = t; }
            Pinned.Add(Mk(f, key.ToLowerInvariant(), Path.GetFileNameWithoutExtension(f), true)); SavePins();
        }

        public List<Item> Display()
        {
            List<Item> l = new List<Item>(); if (S.Get("start", 0) > 0) l.Add(StartItem);
            l.AddRange(Pinned); l.AddRange(Run); return l;
        }

        public bool Refresh()
        {
            Dictionary<string, List<IntPtr>> g = new Dictionary<string, List<IntPtr>>(); Dictionary<string, string> names = new Dictionary<string, string>();
            Dictionary<string, string> exes = new Dictionary<string, string>();
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!N.IsWindowVisible(h)) return true;
                int ex = N.GetWindowLong(h, -20); bool tool = (ex & 0x80) != 0, app = (ex & 0x40000) != 0, noact = (ex & 0x08000000) != 0;
                if ((noact || tool) && !app) return true;
                if (N.GetWindow(h, 4) != IntPtr.Zero && !app) return true;
                StringBuilder t = new StringBuilder(256); N.GetWindowText(h, t, 256); if (t.Length == 0) return true;
                if (!N.IsIconic(h)) { N.RECT wr; N.GetWindowRect(h, out wr); if (wr.R - wr.L < 200 || wr.B - wr.T < 100) return true; }   // kis ertesito-savok ne legyenek ablakok
                int cl; N.DwmGetWindowAttribute(h, 14, out cl, 4); if (cl != 0) return true;
                StringBuilder c = new StringBuilder(64); N.GetClassName(h, c, 64); string cn = c.ToString();
                if (cn == "Progman" || cn == "WorkerW" || cn.StartsWith("Shell_")) return true;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (cn == "ApplicationFrameWindow") { IntPtr core = N.FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null); uint p2 = 0; if (core != IntPtr.Zero) N.GetWindowThreadProcessId(core, out p2); if (p2 != 0) { if (uwpPid.Count > 300) uwpPid.Clear(); uwpPid[h] = p2; pid = p2; } else { uint k2; if (uwpPid.TryGetValue(h, out k2)) pid = k2; } }
                if (pid == self) return true;
                string exe = N.ExePath(pid); string key = exe != null ? exe.ToLowerInvariant() : "pid:" + pid;
                if (!g.ContainsKey(key)) { g[key] = new List<IntPtr>(); names[key] = exe != null ? Path.GetFileNameWithoutExtension(exe) : t.ToString(); exes[key] = exe; }
                g[key].Add(h); return true;
            }, IntPtr.Zero);

            foreach (Item p in Pinned) { List<IntPtr> w; p.Wins = g.TryGetValue(p.Key, out w) ? w : new List<IntPtr>(); }
            Fg = N.GetForegroundWindow();
            HashSet<string> pk = new HashSet<string>(); foreach (Item p in Pinned) pk.Add(p.Key);
            order.RemoveAll(delegate(string k) { return !g.ContainsKey(k) || pk.Contains(k); });
            foreach (string k in g.Keys) if (!pk.Contains(k) && !order.Contains(k)) order.Add(k);
            Run.Clear();
            foreach (string k in order)
            {
                Item i = new Item(); i.Key = k; i.Launch = exes[k]; i.Name = names[k]; i.IconPath = exes[k]; i.Wins = g[k]; Run.Add(i);
            }
            StringBuilder sb = new StringBuilder(); sb.Append(S.Get("start", 0));
            foreach (Item i in Pinned) { sb.Append(i.Key).Append(i.Wins.Count).Append(i.Wins.Contains(Fg) ? "*" : ""); }
            foreach (Item i in Run) { sb.Append(i.Key).Append(i.Wins.Count).Append(i.Wins.Contains(Fg) ? "*" : ""); }
            string ns = sb.ToString(); bool ch = ns != sig; sig = ns; return ch;
        }

        // kituzott program inditasa: a sajat mappajabol; ha nem megy, naplozzuk es explorer-en keresztul probaljuk
        static void StartApp(string path)
        {
            string log = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "launch.log");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path); psi.UseShellExecute = true;
                try { string d = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) psi.WorkingDirectory = d; } catch { }
                Process p = Process.Start(psi); File.AppendAllText(log, DateTime.Now.ToString("s") + " start ok: " + path + " pid=" + (p != null ? p.Id.ToString() : "-") + Environment.NewLine);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(log, DateTime.Now.ToString("s") + Loc.T(" HIBA: ") + path + " -> " + ex.Message + Environment.NewLine); } catch { }
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); } catch { }
            }
        }

        public void Activate(Item it)
        {
            if (it.IsStart) { if (StartAction != null) { StartAction(); return; } N.keybd_event(0x5B, 0, 0, UIntPtr.Zero); N.keybd_event(0x5B, 0, 2, UIntPtr.Zero); return; }
            if (it.Wins.Count == 0)
            {
                StartApp(it.Launch ?? it.Key); return;
            }
            IntPtr fg = N.GetForegroundWindow(); int idx = it.Wins.IndexOf(fg);
            if (it.Wins.Count == 1 && idx == 0 && !N.IsIconic(fg)) { N.ShowWindow(fg, 6); return; }
            IntPtr w = it.Wins[(idx + 1) % it.Wins.Count];
            if (N.IsIconic(w)) N.ShowWindow(w, 9);
            N.SetForegroundWindow(w); N.SwitchToThisWindow(w, true);
        }
    }

    // sajat "uveg": a kepernyo ablak mogotti reszet lefotozza (a dokk sajat magat nem latja: WDA_EXCLUDEFROMCAPTURE) es erosen elmossa
    static class Frost
    {
        public static string DumpDir;
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        static byte[] bufA, bufB;      // csak a UI-szálon használt, újrahasznosított pufferek
        public static BitmapSource Capture(int x, int y, int w, int h, int r)
        {
            if (w < 4 || h < 4) return null;
            r = Theme.BlurPx(r);
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                        BitBlt(dst, 0, 0, w, h, src, x, y, 0x00CC0020 | 0x40000000);      // SRCCOPY | CAPTUREBLT
                        ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst);
                    }
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int need = w * h * 4; if (bufA == null || bufA.Length < need) { bufA = new byte[need]; bufB = new byte[need]; } byte[] px = bufA, tmp = bufB; Marshal.Copy(bd.Scan0, px, 0, need); bmp.UnlockBits(bd);
                    if (r > 0) { for (int i = 0; i < 3; i++) { Pass(px, tmp, w, h, r, true); Pass(tmp, px, w, h, r, false); } if (Sharp(px, w, h)) { for (int i = 0; i < 2; i++) { Pass(px, tmp, w, h, r, true); Pass(tmp, px, w, h, r, false); } } }      // ha a kép elmosás után is éles maradt, még egyszer nekifutunk
                    Theme.Grade(px, need);
                    for (int i = 3; i < need; i += 4) px[i] = 255;
                    BitmapSource bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4); bs.Freeze();
                    if (DumpDir != null) { try { PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bs)); using (FileStream fs = File.Create(Path.Combine(DumpDir, "frost_" + w + "x" + h + ".png"))) enc.Save(fs); } catch { } }
                    return bs;
                }
            }
            catch (Exception ex) { if (DumpDir != null) { try { File.AppendAllText(Path.Combine(DumpDir, "error.log"), "capture: " + ex + Environment.NewLine); } catch { } } return null; }
        }
        // átlagos szomszéd-különbség mintavétellel: éles (nem elmosott) kép esetén nagy
        static bool Sharp(byte[] px, int w, int h)
        {
            long sum = 0; int n = 0; for (int y = 0; y < h; y += Math.Max(1, h / 24)) { int row = y * w * 4; for (int x = 0; x + 6 < w; x += 6) { sum += Math.Abs(px[row + x * 4 + 1] - px[row + (x + 1) * 4 + 1]); n++; } }
            return n > 0 && (double)sum / n > 5.0;
        }
        static void Pass(byte[] s, byte[] d, int w, int h, int r, bool horiz)
        {
            int lines = horiz ? h : w, len = horiz ? w : h, step = horiz ? 4 : w * 4, lineStep = horiz ? w * 4 : 4, div = 2 * r + 1;
            for (int l = 0; l < lines; l++)
            {
                int b = l * lineStep;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0; for (int i = -r; i <= r; i++) sum += s[b + Math.Min(len - 1, Math.Max(0, i)) * step + c];
                    for (int i = 0; i < len; i++)
                    {
                        d[b + i * step + c] = (byte)(sum / div);
                        sum += s[b + Math.Min(len - 1, i + r + 1) * step + c] - s[b + Math.Max(0, i - r) * step + c];
                    }
                }
            }
        }
    }

    // a Windows tálca Start- es ora/tray resze moge rajzolt uveg-pilula (ugyanaz a kinezet, mint a dokke)
    class Pill : Window
    {
        CanvasView view = new CanvasView(); Settings S; IntPtr hwnd; BitmapSource frost; bool excl, shown; DateTime frostAt = DateTime.MinValue; N.RECT cur; double scale = 1;
        public Action OnClick; bool click;
        public void ThemeChanged() { view.InvalidateVisual(); }
        public Pill(Settings s, bool clickable)
        {
            S = s; click = clickable; using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            AllowsTransparency = false; Background = Brushes.Transparent; Focusable = false; Content = view; view.Draw = Paint; IsHitTestVisible = clickable;
            MouseLeftButtonUp += delegate { if (click && OnClick != null) OnClick(); };
            Left = -30000; Top = -30000; Width = 50; Height = 40;
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                N.SetWindowLong(hwnd, -20, N.GetWindowLong(hwnd, -20) | 0x80 | 0x08000000 | (click ? 0 : 0x20));   // toolwindow + noactivate (+ click-through, ha nem kattinthato)
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1; N.DwmExtendFrameIntoClientArea(hwnd, ref mg); int pol = 1; N.DwmSetWindowAttribute(hwnd, 2, ref pol, 4);
                N.PlainWindow(hwnd); excl = N.Exclude(hwnd);
            };
        }
        DateTime zAt = DateTime.MinValue;
        public void Snap(N.RECT r) { BitmapSource f = Frost.Capture(r.L, r.T, r.R - r.L, r.B - r.T, 12); if (f != null) { frost = f; view.InvalidateVisual(); } }   // az ablak/tálca megjelenese ELOTT hivjuk
        public void Update(N.RECT r, IntPtr tray, bool visible, double op)
        {
            if (!visible) { if (shown) { Hide(); shown = false; } return; }
            view.Opacity = Math.Max(0, Math.Min(1, op));
            if (!shown) { Show(); shown = true; }
            if (hwnd == IntPtr.Zero) return;
            bool changed = r.L != cur.L || r.T != cur.T || r.R != cur.R || r.B != cur.B; cur = r;
            if (changed || (DateTime.Now - zAt).TotalMilliseconds > 300) { zAt = DateTime.Now; N.SetWindowPos(hwnd, tray, r.L, r.T, r.R - r.L, r.B - r.T, 0x10); }      // direkt a tálca ALATT (a Start/ora ikonok folotte maradnak)
            if (changed) { frostAt = DateTime.MinValue; view.InvalidateVisual(); }
        }
        void Paint(DrawingContext dc)
        {
            double w = (cur.R - cur.L) / scale, h = (cur.B - cur.T) / scale; if (w < 4 || h < 4) return;
            double r = Math.Min(h / 2, S.Get("round", 0.6) * h / 2); Rect full = new Rect(0.5, 0.5, w - 1, h - 1);
            bool fr = frost != null; byte ta = (byte)Math.Min(240, Math.Max(S.Get("glass", 135), fr ? 0 : 200));
            if (fr) dc.DrawRoundedRectangle(new ImageBrush(frost), null, full, r, r);
            dc.DrawRoundedRectangle(new SolidColorBrush(Theme.TintA(ta)), null, full, r, r);
            dc.DrawRoundedRectangle(Theme.Gloss(), Theme.BorderPen(), full, r, r);
            if (click) Dock.DrawStart(dc, new Rect(w / 2 - 11, h / 2 - 11, 22, 22), S.Get("mono", 1) > 0);
        }
    }

    static class Ease
    {
        public static double OutCubic(double t) { t = 1 - t; return 1 - t * t * t; }
        public static double InCubic(double t) { return t * t * t; }
        public static double OutBack(double t) { const double c1 = 1.35, c3 = c1 + 1; double u = t - 1; return 1 + c3 * u * u * u + c1 * u * u; }   // enyhe "rugos" tullendules (iOS)
    }
    class IconAnim { public double S = 1, V, T = 1; }   // ikon-rugo: S = meret, T = cel, V = sebesseg

    class Slot { public Item It; public Rect R; }

    class CanvasView : FrameworkElement
    {
        public Action<DrawingContext> Draw;
        protected override void OnRender(DrawingContext dc) { if (Draw != null) Draw(dc); }
    }

    // ---------- elo elonezet tobb ablakos programoknal (mint a Windows tálcán) ----------
    class Preview : Window
    {
        static Preview inst; public static bool Force; public static void HideNow() { if (inst != null) inst.Dismiss(); } public static bool Visible { get { return inst != null && inst.showing; } }
        public static void Test(Item it, N.RECT rect, double sc) { Force = true; if (inst == null) inst = new Preview(); inst.scale = sc; inst.DisplayFor(it, rect); }
        const double CW = 236, CH = 164, PAD = 10;
        IntPtr hwnd; double scale = 1; CanvasView view = new CanvasView(); DispatcherTimer timer = new DispatcherTimer();
        Item req, cur; N.RECT reqRect, slot; DateTime reqTime, leftAt; bool showing;
        List<IntPtr> wins = new List<IntPtr>(), thumbs = new List<IntPtr>(); List<Rect> cards = new List<Rect>(); List<string> titles = new List<string>();
        BitmapSource frost; bool excl;
        bool vis; double op; int ptgt; double p0v; Stopwatch pws = new Stopwatch(); DispatcherTimer anim = new DispatcherTimer(DispatcherPriority.Render); int bx, by;
        int hc = -1, hx = -1; double pw, ph; Dictionary<string, BitmapSource> icons = new Dictionary<string, BitmapSource>();

        Preview()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            AllowsTransparency = false; Background = Brushes.Transparent; Focusable = false; Content = view; view.Draw = Paint;
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                N.SetWindowLong(hwnd, -20, N.GetWindowLong(hwnd, -20) | 0x80 | 0x08000000);
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1; N.DwmExtendFrameIntoClientArea(hwnd, ref mg); int pol = 1; N.DwmSetWindowAttribute(hwnd, 2, ref pol, 4);
                N.PlainWindow(hwnd); excl = N.Exclude(hwnd);
            };
            MouseMove += OnMove; MouseLeftButtonUp += OnUp; MouseDown += delegate(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle && hc >= 0) CloseWin(hc); };
            MouseLeave += delegate { hc = hx = -1; view.InvalidateVisual(); };
            timer.Interval = TimeSpan.FromMilliseconds(100); timer.Tick += Tick;
            anim.Interval = TimeSpan.FromMilliseconds(8); anim.Tick += AnimStep;
        }

        public static void Request(Item it, N.RECT rect, double sc)
        {
            if (inst == null) inst = new Preview();
            inst.scale = sc; inst.req = it; inst.reqRect = rect; inst.reqTime = DateTime.Now; if (!inst.timer.IsEnabled) inst.timer.Start();
        }

        void Tick(object s, EventArgs e)
        {
            N.POINT c; N.GetCursorPos(out c); DateTime now = DateTime.Now; if (Force) { leftAt = now; return; }
            bool inReq = req != null && N.In(c, reqRect, 6);
            if (!showing)
            {
                if (!inReq) { req = null; timer.Stop(); return; }
                if ((now - reqTime).TotalMilliseconds > 280) DisplayFor(req, reqRect);
                return;
            }
            if (inReq && req.Key != cur.Key && (now - reqTime).TotalMilliseconds > 120) { DisplayFor(req, reqRect); return; }
            N.RECT pr; N.GetWindowRect(hwnd, out pr);
            if (N.In(c, pr, 3) || N.In(c, slot, 6) || inReq) leftAt = now;
            else if ((now - leftAt).TotalMilliseconds > 350) Dismiss();
        }

        BitmapSource Ic(string path)
        {
            BitmapSource b; if (path == null) return null; if (!icons.TryGetValue(path, out b)) { b = N.Icon(path, 32); icons[path] = b; } return b;
        }

        void StartAnim(int target) { ptgt = target; p0v = op; pws.Restart(); if (!anim.IsEnabled) anim.Start(); }
        void ThumbOpacity()
        {
            byte a = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, op)));
            foreach (IntPtr th in thumbs) if (th != IntPtr.Zero) { N.DWMTHUMB q = new N.DWMTHUMB(); q.Flags = 0x4; q.Opacity = a; N.DwmUpdateThumbnailProperties(th, ref q); }
        }
        void AnimStep(object s, EventArgs e)
        {
            double t = Math.Min(1.0, pws.ElapsedMilliseconds / (ptgt == 1 ? 220.0 : 140.0));
            op = p0v + (ptgt - p0v) * (ptgt == 1 ? Ease.OutCubic(t) : Ease.InCubic(t));
            view.Opacity = op;
            if (hwnd != IntPtr.Zero) N.SetWindowPos(hwnd, new IntPtr(-1), bx, by + (int)Math.Round((1 - op) * 12 * scale), 0, 0, 0x11);   // enyhen felcsuszik
            ThumbOpacity();
            if (t >= 1) { anim.Stop(); if (ptgt == 0) { ClearThumbs(); Hide(); vis = false; } }
        }

        void DisplayFor(Item it, N.RECT sl)
        {
            cur = it; slot = sl; ClearThumbs(); wins = new List<IntPtr>(it.Wins); cards.Clear(); titles.Clear();
            if (wins.Count < 2) { Dismiss(); return; }
            Forms.Screen scr = Forms.Screen.FromPoint(new System.Drawing.Point((sl.L + sl.R) / 2, (sl.T + sl.B) / 2));
            int maxCols = Math.Max(1, (int)((scr.Bounds.Width / scale - 40 - PAD) / (CW + PAD)));
            int cols = Math.Min(wins.Count, maxCols), rows = (wins.Count + cols - 1) / cols;
            for (int i = 0; i < wins.Count; i++)
            {
                cards.Add(new Rect(PAD + (i % cols) * (CW + PAD), PAD + (i / cols) * (CH + PAD), CW, CH));
                StringBuilder t = new StringBuilder(256); N.GetWindowText(wins[i], t, 256); titles.Add(t.ToString());
            }
            pw = PAD + cols * (CW + PAD); ph = PAD + rows * (CH + PAD);
            int wpx = (int)Math.Round(pw * scale), hpx = (int)Math.Round(ph * scale);
            int x = (sl.L + sl.R) / 2 - wpx / 2; x = Math.Max(scr.Bounds.X + 8, Math.Min(scr.Bounds.Right - wpx - 8, x));
            int y = sl.T - hpx - 10; if (y < scr.Bounds.Y + 4) y = sl.B + 10;
            bool first = !vis; bx = x; by = y;
            if (hwnd == IntPtr.Zero) { Left = x / scale; Top = y / scale; Width = pw; Height = ph; view.Opacity = 0; op = 0; Show(); vis = true; }
            N.SetWindowPos(hwnd, new IntPtr(-1), x, y + (int)Math.Round((1 - op) * 12 * scale), wpx, hpx, 0x10);
            if (!vis) { vis = true; op = 0; view.Opacity = 0; Show(); }
            showing = true; if (ptgt != 1 || op < 1) StartAnim(1);
            leftAt = DateTime.Now;
            if (first) { frost = null; N.RECT pr; N.GetWindowRect(hwnd, out pr); frost = Frost.Capture(pr.L, pr.T, pr.R - pr.L, pr.B - pr.T, 16); }
            RegisterThumbs(); view.InvalidateVisual();
        }

        void RegisterThumbs()
        {
            for (int i = 0; i < wins.Count; i++)
            {
                IntPtr th; if (N.DwmRegisterThumbnail(hwnd, wins[i], out th) != 0) { thumbs.Add(IntPtr.Zero); continue; }
                thumbs.Add(th); N.SIZE sz; N.DwmQueryThumbnailSourceSize(th, out sz); if (sz.W <= 0 || sz.H <= 0) { sz.W = 16; sz.H = 9; }
                Rect a = new Rect(cards[i].X + 8, cards[i].Y + 32, CW - 16, CH - 42);
                double ar = (double)sz.W / sz.H, tw = a.Width, thh = tw / ar; if (thh > a.Height) { thh = a.Height; tw = thh * ar; }
                N.DWMTHUMB p = new N.DWMTHUMB(); p.Flags = 0x1D; p.Opacity = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, op))); p.Visible = true; p.ClientOnly = false;
                p.Dest.L = (int)Math.Round((a.X + (a.Width - tw) / 2) * scale); p.Dest.T = (int)Math.Round((a.Y + (a.Height - thh) / 2) * scale);
                p.Dest.R = p.Dest.L + (int)Math.Round(tw * scale); p.Dest.B = p.Dest.T + (int)Math.Round(thh * scale);
                N.DwmUpdateThumbnailProperties(th, ref p);
            }
        }
        void ClearThumbs() { foreach (IntPtr t in thumbs) if (t != IntPtr.Zero) N.DwmUnregisterThumbnail(t); thumbs.Clear(); }
        void Dismiss() { if (vis && ptgt != 0) StartAnim(0); showing = false; req = null; cur = null; hc = hx = -1; timer.Stop(); }

        void CloseWin(int i) { N.PostMessage(wins[i], 0x10, IntPtr.Zero, IntPtr.Zero); Dismiss(); }
        void OnMove(object s, MouseEventArgs e)
        {
            Point p = e.GetPosition(this); int a = -1, b = -1;
            for (int i = 0; i < cards.Count; i++) if (cards[i].Contains(p)) { a = i; if (new Rect(cards[i].Right - 28, cards[i].Y + 5, 22, 22).Contains(p)) b = i; }
            if (a != hc || b != hx) { hc = a; hx = b; view.InvalidateVisual(); }
        }
        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (hc < 0) return; if (hx >= 0) { CloseWin(hx); return; }
            IntPtr w = wins[hc]; Dismiss();
            if (N.IsIconic(w)) N.ShowWindow(w, 9); N.SetForegroundWindow(w); N.SwitchToThisWindow(w, true);
        }

        void Paint(DrawingContext dc)
        {
            Rect pr2 = new Rect(0.5, 0.5, Math.Max(1, pw - 1), Math.Max(1, ph - 1));
            if (frost != null) dc.DrawRoundedRectangle(new ImageBrush(frost), null, pr2, 12, 12);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(frost != null ? 140 : 215), 17, 20, 26)), Theme.BorderPen(), pr2, 12, 12);
            for (int i = 0; i < cards.Count; i++)
            {
                Rect c = cards[i];
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(i == hc ? 52 : 26), 255, 255, 255)), null, c, 8, 8);
                BitmapSource ic = Ic(cur != null ? cur.IconPath : null); if (ic != null) dc.DrawImage(ic, new Rect(c.X + 9, c.Y + 8, 18, 18));
                FormattedText ft = new FormattedText(titles[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Inter, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 12, Brushes.White);
                ft.MaxTextWidth = CW - 32 - 34; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(ft, new Point(c.X + 34, c.Y + 9));
                if (i == hc)
                {
                    Rect x = new Rect(c.Right - 28, c.Y + 5, 22, 22);
                    if (i == hx) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(196, 43, 28)), null, x, 5, 5);
                    Pen xp = new Pen(Brushes.White, 1.4);
                    dc.DrawLine(xp, new Point(x.X + 7, x.Y + 7), new Point(x.Right - 7, x.Bottom - 7)); dc.DrawLine(xp, new Point(x.Right - 7, x.Y + 7), new Point(x.X + 7, x.Bottom - 7));
                }
            }
        }
    }

    class Dock : Window
    {
        public static List<N.RECT> Keep = new List<N.RECT>();   // a pilulak teruletei: itt az egerrel a dokk is marad
        public bool IsShown { get { return shown; } }
        public double AnimP { get { return Math.Max(0, Math.Min(1, p)); } }
        public int DockEdge { get { return edgeDir; } }   // 0 = alul, 1 = fent, 2 = balra, 3 = jobbra: a dokkhoz legkozelebbi kulso kepernyoszel (ide kell vinni az egeret)
        bool springs; public bool Busy { get { return animating || springs; } }
        // iOS-szeru megjelenes/eltunes: csuszas + attunes, rugos lassulassal
        double p = 1, p0 = 1, dur = 300; int tgt = 1, offX, offY, edgeDir, baseX, baseY; bool animating, visibleW = true; Stopwatch aw = new Stopwatch(), fs = Stopwatch.StartNew();
        int Slide { get { return (int)Math.Round(48 * scale); } }
        Dictionary<string, IconAnim> ia = new Dictionary<string, IconAnim>();
        IconAnim A(Item it) { string k = it.IsStart ? "start" : it.Key; IconAnim a; if (!ia.TryGetValue(k, out a)) { a = new IconAnim(); ia[k] = a; } return a; }
        bool OuterEdge(int side)       // nincs szomszedos monitor azon az oldalon
        {
            System.Drawing.Rectangle b = Scr.Bounds;
            System.Drawing.Point pt = side == 1 ? new System.Drawing.Point(b.Left + b.Width / 2, b.Top - 1) : side == 2 ? new System.Drawing.Point(b.Left - 1, b.Top + b.Height / 2) : new System.Drawing.Point(b.Right, b.Top + b.Height / 2);
            foreach (Forms.Screen sc in Forms.Screen.AllScreens) if (sc.Bounds.Contains(pt)) return false;
            return true;
        }
        int EdgeOf(int px, int py, int pw, int ph)
        {
            System.Drawing.Rectangle b = Scr.Bounds; int[] d = { b.Bottom - (py + ph), py - b.Top, px - b.Left, b.Right - (px + pw) };
            bool[] ok = { true, OuterEdge(1), OuterEdge(2), OuterEdge(3) }; int best = 0;
            for (int i = 1; i < 4; i++) if (ok[i] && d[i] < d[best]) best = i;
            return best;
        }
        void SetOff(double d)           // csuszas iranya = a dokk szelenek iranya
        {
            int v = (int)Math.Round(d); offX = edgeDir == 2 ? -v : edgeDir == 3 ? v : 0; offY = edgeDir == 0 ? v : edgeDir == 1 ? -v : 0;
        }
        void Go(bool on)
        {
            int t = on ? 1 : 0; if (t == tgt) return;
            tgt = t; shown = on; p0 = p; dur = on ? 340 : 230; aw.Restart(); animating = true;
            if (on && !visibleW) { visibleW = true; p = 0; p0 = 0; SetOff(Slide); view.Opacity = 0; Place(); frost = Frost.Capture(baseX, baseY, winW, winH, 16); needSnap = false; Show(); }
        }
        void Step()
        {
            double dt = Math.Min(0.05, fs.Elapsed.TotalSeconds); fs.Restart(); bool moved = false;
            foreach (IconAnim a in ia.Values)
                if (Math.Abs(a.S - a.T) > 0.0005 || Math.Abs(a.V) > 0.005) { a.V += (380 * (a.T - a.S) - 19 * a.V) * dt; a.S += a.V * dt; moved = true; }
            springs = moved; if (moved && visibleW) view.InvalidateVisual();
            if (!animating) return;
            double t = Math.Min(1.0, aw.ElapsedMilliseconds / dur);
            p = p0 + (tgt - p0) * (tgt == 1 ? Ease.OutBack(t) : Ease.InCubic(t));
            view.Opacity = Math.Max(0, Math.Min(1, tgt == 1 ? p * 1.6 : p)); SetOff((1 - p) * Slide);
            if (hwnd != IntPtr.Zero && visibleW) N.SetWindowPos(hwnd, IntPtr.Zero, baseX + offX, baseY + offY, 0, 0, 0x15);
            if (t >= 1) { animating = false; p = tgt; SetOff(0); view.Opacity = tgt; if (tgt == 0) { Hide(); visibleW = false; } else if (hwnd != IntPtr.Zero) N.SetWindowPos(hwnd, IntPtr.Zero, baseX, baseY, 0, 0, 0x15); }
        }
        public Forms.Screen Scr; Model M; Settings S; string K; double scale; IntPtr hwnd;
        BitmapSource frost; bool excl; DateTime frostAt = DateTime.MinValue; int snapState, winW, winH; DateTime snapAt; bool needSnap;
        CanvasView view = new CanvasView(); List<Slot> slots = new List<Slot>();
        double CX, BY, W, H, curW; bool autoW;
        int hover = -1, zone = 0, resizeZone = 0; Slot pending; bool pressed, moving, reorder, menuOpen, shown = true;
        N.POINT downCur; N.RECT downRect; double rsW, rsH, rsBY; DateTime lastIn = DateTime.Now;
        Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        public Dock(Forms.Screen scr, Model m, Settings s)
        {
            Scr = scr; M = m; S = s; K = "mon" + new string(Array.FindAll(scr.DeviceName.ToCharArray(), char.IsLetterOrDigit));
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            AllowsTransparency = false; Background = Brushes.Transparent; Focusable = false; Content = view; view.Draw = Paint;
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            ToolTipService.SetInitialShowDelay(view, 350);

            H = S.Get(K + ".h", 64); W = S.Get(K + ".w", 0); autoW = W <= 0;
            double sw = scr.Bounds.Width / scale, sx = scr.Bounds.X / scale, sb = scr.Bounds.Bottom / scale;
            CX = S.Get(K + ".cx", sx + sw / 2); BY = S.Get(K + ".by", sb - 52);
            if (CX < sx || CX > sx + sw || BY < scr.Bounds.Y / scale || BY > sb + 5) { CX = sx + sw / 2; BY = sb - 52; }

            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                N.SetWindowLong(hwnd, -20, N.GetWindowLong(hwnd, -20) | 0x80 | 0x08000000);
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1; N.DwmExtendFrameIntoClientArea(hwnd, ref mg); int pol = 1; N.DwmSetWindowAttribute(hwnd, 2, ref pol, 4);
                N.PlainWindow(hwnd); excl = N.Exclude(hwnd); if (Frost.DumpDir != null) File.AppendAllText(Path.Combine(Frost.DumpDir, "error.log"), "dock excl=" + excl + " err=" + Marshal.GetLastWin32Error() + Environment.NewLine);
            };
            MouseMove += OnMove; MouseLeave += delegate { if (!pressed) { hover = -1; view.ToolTip = null; view.InvalidateVisual(); } };
            MouseLeftButtonDown += OnDown; MouseLeftButtonUp += OnUp; MouseRightButtonUp += OnRight; MouseWheel += OnWheel;
            Place();
        }

        // ---------- elrendezes: az ikonok mindig kitoltik a dokot ----------
        double PadV(double h) { return Math.Max(6, h * 0.14); }
        double PadH(double h) { return Math.Max(10, PadV(h) * 1.15); }
        double AutoWidth(double h, int n)
        {
            double s = h - 2 * PadV(h), gap = Math.Max(Math.Max(4, s * 0.14), s * 0.28);
            return 2 * PadH(h) + n * s + Math.Max(0, n - 1) * gap;
        }
        void Layout()
        {
            slots.Clear(); List<Item> items = M.Display(); int n = items.Count; if (n == 0) return;
            double pv = PadV(H), ph = PadH(H), sH = H - 2 * pv, minGap = Math.Max(4, sH * 0.14);
            double wFit = (curW - 2 * ph - (n - 1) * minGap) / n, s = Math.Max(16, Math.Min(sH, wFit));
            double extra = curW - 2 * ph - n * s, gap = n > 1 ? Math.Min(Math.Max(minGap, extra / (n - 1)), s * 0.7) : 0;
            double x = (curW - (n * s + (n - 1) * gap)) / 2, y = (H - s) / 2 - Math.Min(3, pv * 0.25);
            foreach (Item it in items) { Slot sl = new Slot(); sl.It = it; sl.R = new Rect(x, y, s, s); slots.Add(sl); x += s + gap; }
        }
        public void Place()
        {
            int n = M.Display().Count; double maxW = Scr.Bounds.Width / scale - 20;
            curW = Math.Min(autoW ? AutoWidth(H, Math.Max(1, n)) : Math.Max(W, 100), maxW);
            int px = (int)Math.Round((CX - curW / 2) * scale), py = (int)Math.Round((BY - H) * scale), pw = (int)Math.Round(curW * scale), ph = (int)Math.Round(H * scale);
            baseX = px; baseY = py; winW = pw; winH = ph; edgeDir = EdgeOf(px, py, pw, ph);
            if (hwnd != IntPtr.Zero) N.SetWindowPos(hwnd, IntPtr.Zero, px + offX, py + offY, pw, ph, 0x14);   // egy lepesben: nincs villodzas
            else { Left = px / scale; Top = py / scale; Width = curW; Height = H; }
            Layout(); view.InvalidateVisual();
        }
        public void Refresh() { if (autoW && Math.Abs(AutoWidth(H, Math.Max(1, M.Display().Count)) - curW) > 0.5) Place(); else { Layout(); view.InvalidateVisual(); } }
        void SavePos() { needSnap = true; S.Set(K + ".cx", CX); S.Set(K + ".by", BY); S.Set(K + ".h", H); S.Set(K + ".w", autoW ? 0 : W); S.Save(); }

        // ---------- uveg + lekerekites ----------
        double Glass() { return Math.Max(0, Math.Min(240, S.Get("glass", 135))); }
        // uveg: a sajat elmosott hatterkep (Frost) + sotet, simitott szelu arnyalat; mozgatas/atmeretezes kozben csak sima arnyalat (nem akad)
        bool interactive;
        void ApplyGlass() { }
        string frostSig = "";
        public void ThemeChanged()
        {
            cache.Clear(); string sg = S.Get("blur", 16) + "|" + S.Get("frostsat", 100) + "|" + S.Get("frostbright", 100); if (sg != frostSig) { frostSig = sg; snapDue = DateTime.Now.AddMilliseconds(450); }
            if (view != null) view.InvalidateVisual();
        }
        DateTime snapDue = DateTime.MaxValue;
        void SetInteractive(bool on) { interactive = on; if (!on) needSnap = true; view.InvalidateVisual(); }
        void UpdateFrost()
        {
            if (hwnd == IntPtr.Zero || !shown || interactive || !excl) return;
            N.RECT r; N.GetWindowRect(hwnd, out r); BitmapSource f = Frost.Capture(r.L, r.T, r.R - r.L, r.B - r.T, 16);
            if (f != null) { frost = f; view.InvalidateVisual(); }
        }
        double Radius() { return Math.Min(H / 2, S.Get("round", 0.6) * H / 2); }
        void ApplyShape() { ApplyGlass(); }

        // ---------- rajzolas ----------
        BitmapSource Ic(Item it, double size)
        {
            int[] b = { 32, 48, 64, 96, 128, 256 }; int px = 256; double want = size * scale;
            foreach (int v in b) if (v >= want) { px = v; break; }
            bool mono = S.Get("mono", 1) > 0; string k = it.IconPath + "|" + px + (mono ? "|m" : ""); BitmapSource bs;
            if (!cache.TryGetValue(k, out bs)) { bs = N.Icon(it.IconPath, px); if (mono && bs != null) bs = N.Mono(bs, 52, Theme.IconGain(0.84)); cache[k] = bs; }
            return bs;
        }
        void Paint(DrawingContext dc)
        {
            double r = Radius(); Rect full = new Rect(0.5, 0.5, Math.Max(1, curW - 1), Math.Max(1, H - 1));
            bool fr = frost != null && !interactive; byte ta = (byte)Math.Min(240, Math.Max(Glass(), fr ? 0 : 200) + (interactive ? 40 : 0));
            if (fr) dc.DrawRoundedRectangle(new ImageBrush(frost), null, full, r, r);
            dc.DrawRoundedRectangle(new SolidColorBrush(Theme.TintA(ta)), null, full, r, r);
            LinearGradientBrush gloss = Theme.Gloss();
            dc.DrawRoundedRectangle(gloss, Theme.BorderPen(), full, r, r);
            for (int i = 0; i < slots.Count; i++)
            {
                Slot sl = slots[i]; Item it = sl.It; Rect rc = sl.R;
                bool tiles = S.Get("tiles", 1) > 0, mono = S.Get("mono", 1) > 0, hot = i == hover && !moving;
                if (hot && !tiles) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, Rect.Inflate(rc, rc.Width * 0.08, rc.Height * 0.08), rc.Width * 0.22, rc.Width * 0.22);
                IconAnim ian; bool sc = ia.TryGetValue(it.IsStart ? "start" : it.Key, out ian) && Math.Abs(ian.S - 1) > 0.002;
                if (sc) dc.PushTransform(new ScaleTransform(ian.S, ian.S, rc.X + rc.Width / 2, rc.Y + rc.Height / 2));
                Rect ir = tiles ? Rect.Inflate(rc, -rc.Width * 0.15, -rc.Height * 0.15) : rc;
                if (tiles) DrawTile(dc, rc, hot);
                if (it.IsStart) DrawStart(dc, ir, mono);
                else { BitmapSource bs = Ic(it, ir.Width); if (bs != null) dc.DrawImage(bs, ir); }
                if (sc) dc.Pop();
                if (it.Wins.Count > 0)
                {
                    bool act = it.Wins.Contains(M.Fg); double w = act ? rc.Width * 0.5 : rc.Width * 0.22;
                    dc.DrawRoundedRectangle(new SolidColorBrush(act ? Color.FromRgb(120, 190, 255) : Color.FromArgb(200, 230, 230, 235)), null,
                        new Rect(rc.X + (rc.Width - w) / 2, Math.Min(H - 5, rc.Bottom + Math.Max(2, PadV(H) * 0.3)), w, 2.5), 1.2, 1.2);
                    if (it.Wins.Count > 1) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 230, 230, 235)), null, new Rect(rc.Right - 4, rc.Y + 2, 5, 5), 2.5, 2.5);
                }
            }
        }
        // iOS-szeru uveg ikon-csempe: halvanyan feher, atlatszo, fenyes felso szel
        static void DrawTile(DrawingContext dc, Rect rc, bool hot)
        {
            double rr = rc.Width * Theme.TileRound / 58.0, tk = Theme.TileA / 62.0;
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb((byte)Math.Min(255, (hot ? 70 : 48) * tk), 255, 255, 255), Color.FromArgb((byte)Math.Min(255, (hot ? 34 : 16) * tk), 255, 255, 255), 90),
                new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1), rc, rr, rr);
            dc.DrawRoundedRectangle(null, new Pen(new LinearGradientBrush(Color.FromArgb(130, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), 1), Rect.Inflate(rc, -1, -1), rr - 1, rr - 1);
        }
        public static void DrawStart(DrawingContext dc, Rect r, bool mono)
        {
            double p = r.Width * 0.2, g = r.Width * 0.04, s = (r.Width - 2 * p - g) / 2; Brush b = new SolidColorBrush(mono ? Color.FromArgb(235, 240, 240, 245) : Color.FromRgb(70, 170, 255));
            for (int i = 0; i < 4; i++) dc.DrawRectangle(b, null, new Rect(r.X + p + (i % 2) * (s + g), r.Y + p + (i / 2) * (s + g), s, s));
        }

        // ---------- egerkezeles ----------
        int Hit(Point p) { for (int i = 0; i < slots.Count; i++) if (Rect.Inflate(slots[i].R, 4, 8).Contains(p)) return i; return -1; }
        int Zone(Point p) { if (p.X < 10) return 1; if (p.X > curW - 10) return 2; if (p.Y < 5) return 3; if (p.Y > H - 5) return 4; return 0; }
        N.RECT SlotScreenRect(Slot sl)
        {
            N.RECT r; N.GetWindowRect(hwnd, out r); N.RECT o = new N.RECT();
            o.L = r.L + (int)(sl.R.X * scale); o.R = r.L + (int)(sl.R.Right * scale); o.T = r.T + (int)(sl.R.Y * scale); o.B = r.T + (int)(sl.R.Bottom * scale); return o;
        }
        static ToolTip Tip(string t) { ToolTip tt = new ToolTip(); tt.Content = t; tt.FontFamily = new FontFamily("Inter, Segoe UI"); tt.FontSize = 12; return tt; }
        void OnMove(object s, MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (resizeZone != 0) { DoResize(); return; }
            if (pressed && e.LeftButton == MouseButtonState.Pressed)
            {
                N.POINT c; N.GetCursorPos(out c); int dx = c.X - downCur.X, dy = c.Y - downCur.Y; bool far = Math.Abs(dx) > 5 || Math.Abs(dy) > 5;
                if (reorder) { if (far) DoReorder(p); return; }
                if (!moving && far) { moving = true; SetInteractive(true); }
                if (moving) N.SetWindowPos(hwnd, IntPtr.Zero, downRect.L + dx, downRect.T + dy, 0, 0, 0x15);   // kozvetlen pixel-mozgatas: sima
                return;
            }
            zone = Zone(p); Cursor = zone == 1 || zone == 2 ? Cursors.SizeWE : zone != 0 ? Cursors.SizeNS : Cursors.Arrow;
            int h = zone == 0 ? Hit(p) : -1;
            if (h != hover)
            {
                hover = h; view.ToolTip = h >= 0 ? Tip(slots[h].It.IsStart ? "Start" : slots[h].It.Name) : null; view.InvalidateVisual();
                if (h >= 0 && slots[h].It.Wins.Count >= 2) Preview.Request(slots[h].It, SlotScreenRect(slots[h]), scale);
            }
        }
        void OnDown(object s, MouseButtonEventArgs e)
        {
            Preview.HideNow(); Point p = e.GetPosition(this); zone = Zone(p); N.GetCursorPos(out downCur); N.GetWindowRect(hwnd, out downRect);
            if (zone != 0) { resizeZone = zone; rsW = curW; rsH = H; rsBY = BY; CaptureMouse(); SetInteractive(true); return; }
            int h = Hit(p); pending = h >= 0 ? slots[h] : null; moving = false; pressed = true; springs = true; if (pending != null) A(pending.It).T = 0.86;   // lenyomas: az ikon kicsit osszehuzodik
            reorder = pending != null && pending.It.Pinned && Keyboard.Modifiers == ModifierKeys.Shift;   // Shift + huzas = sorrend, kulonben a dok mozog
            CaptureMouse();
        }
        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            springs = true; if (pending != null) { IconAnim a = A(pending.It); a.T = 1; if (!moving && !reorder) a.V += 3.4; }   // felengedes: rugos "pattanas"
            if (resizeZone != 0) { resizeZone = 0; SetInteractive(false); SavePos(); return; }
            if (moving)
            {
                moving = false; N.RECT r; N.GetWindowRect(hwnd, out r); CX = (r.L + r.R) / 2.0 / scale; BY = r.B / scale; SetInteractive(false); SavePos();
            }
            else if (pending != null && !reorder) M.Activate(pending.It);
            else if (reorder) M.SavePins();
            pressed = false; reorder = false; pending = null; view.InvalidateVisual();
        }
        void DoResize()
        {
            N.POINT c; N.GetCursorPos(out c); double dx = (c.X - downCur.X) / scale, dy = (c.Y - downCur.Y) / scale;
            if (resizeZone == 1) { autoW = false; W = Math.Max(100, rsW - 2 * dx); }
            else if (resizeZone == 2) { autoW = false; W = Math.Max(100, rsW + 2 * dx); }
            else if (resizeZone == 3) { H = Math.Max(36, Math.Min(160, rsH - dy)); BY = rsBY; }
            else { H = Math.Max(36, Math.Min(160, rsH + dy)); BY = rsBY + (H - rsH); }
            Place();
        }
        void DoReorder(Point p)
        {
            int from = M.Pinned.IndexOf(pending.It); if (from < 0) return; int to = from;
            for (int i = 0; i < slots.Count; i++) { Item it = slots[i].It; if (it.Pinned && p.X > slots[i].R.X && p.X < slots[i].R.Right) to = M.Pinned.IndexOf(it); }
            if (to != from) { Item it = M.Pinned[from]; M.Pinned.RemoveAt(from); M.Pinned.Insert(to, it); Layout(); for (int i = 0; i < slots.Count; i++) if (slots[i].It == it) pending = slots[i]; }
            view.InvalidateVisual();
        }
        void OnWheel(object s, MouseWheelEventArgs e)
        {
            double st = e.Delta > 0 ? 1 : -1; ModifierKeys m = Keyboard.Modifiers;
            if (m == ModifierKeys.Shift) { S.Set("glass", Math.Max(10, Math.Min(235, Glass() + st * 10))); S.Save(); view.InvalidateVisual(); return; }
            if (m == (ModifierKeys.Control | ModifierKeys.Shift)) { if (autoW) { W = curW; autoW = false; } W = Math.Max(100, W + st * 24); }
            else if (m == ModifierKeys.Control)          // nagyitas: a magassag es (fix szelessegnel) a szelesseg aranyosan valtozik
            {
                double nh = Math.Max(36, Math.Min(160, H + st * 4)); if (!autoW) W = W * nh / H; H = nh;
            }
            else return;                                  // sima gorgo nem csinal semmit (nincs veletlen atmeretezes)
            Place(); SavePos();
        }

        // ---------- menu ----------
        MenuItem Mi(string t, RoutedEventHandler h, bool check = false, bool on = false)
        {
            MenuItem m = new MenuItem(); m.Header = t; if (check) { m.IsCheckable = true; m.IsChecked = on; } if (h != null) m.Click += h; return m;
        }
        MenuItem Sub(string t, string[] names, double[] vals, string key, bool dockSize)
        {
            MenuItem m = new MenuItem(); m.Header = t;
            for (int i = 0; i < names.Length; i++)
            {
                double v = vals[i];
                m.Items.Add(Mi(names[i], delegate
                {
                    if (dockSize) { if (!autoW) W = W * v / H; H = v; Place(); SavePos(); } else { S.Set(key, v); S.Save(); ApplyGlass(); Place(); }
                }));
            }
            return m;
        }
        void OnRight(object s, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this); int h = Hit(p); ContextMenu cm = new ContextMenu(); cm.FontFamily = new FontFamily("Inter, Segoe UI");
            if (h >= 0 && !slots[h].It.IsStart)
            {
                Item it = slots[h].It;
                cm.Items.Add(Mi(it.Pinned ? Loc.T("Levétel a dokkról") : Loc.T("Kitűzés a dokkra"), delegate { if (it.Pinned) M.Unpin(it); else M.Pin(it); M.Refresh(); Place(); }));
                if (it.Wins.Count > 0) cm.Items.Add(Mi(Loc.T("Új ablak / indítás"), delegate { it.Wins = new List<IntPtr>(); M.Activate(it); }));
                cm.Items.Add(new Separator());
            }
            cm.Items.Add(Sub(Loc.T("Méret (ikonméret)"), new[] { Loc.T("Kicsi (44)"), Loc.T("Közepes (56)"), Loc.T("Nagy (68)"), Loc.T("Nagyon nagy (84)"), "Hatalmas (104)" }, new double[] { 44, 56, 68, 84, 104 }, "", true));
            MenuItem w = new MenuItem(); w.Header = Loc.T("Szélesség");
            w.Items.Add(Mi(Loc.T("Automatikus (az ikonokhoz igazodik)"), delegate { autoW = true; Place(); SavePos(); }));
            w.Items.Add(Mi(Loc.T("Szélesebb (+60)"), delegate { if (autoW) { W = curW; autoW = false; } W += 60; Place(); SavePos(); }));
            w.Items.Add(Mi("Keskenyebb (-60)", delegate { if (autoW) { W = curW; autoW = false; } W = Math.Max(100, W - 60); Place(); SavePos(); }));
            cm.Items.Add(w);
            cm.Items.Add(Sub(Loc.T("Üveghatás / sötétség"), new[] { Loc.T("Átlátszó üveg (30)"), Loc.T("Üveg (65)"), Loc.T("Sötét üveg (100)"), Loc.T("Nagyon sötét (150)"), Loc.T("Majdnem fekete (200)") }, new double[] { 30, 65, 100, 150, 200 }, "glass", false));
            cm.Items.Add(Sub(Loc.T("Lekerekítés"), new[] { Loc.T("Szögletes"), Loc.T("Kicsi"), Loc.T("Közepes"), Loc.T("Pilula") }, new double[] { 0, 0.3, 0.6, 1 }, "round", false));
            cm.Items.Add(Mi(Loc.T("Start gomb"), delegate(object o, RoutedEventArgs a) { S.Set("start", ((MenuItem)o).IsChecked ? 1 : 0); S.Save(); M.Refresh(); Place(); }, true, S.Get("start", 0) > 0));
            cm.Items.Add(Mi(Loc.T("Fekete-fehér ikonok"), delegate(object o, RoutedEventArgs a) { S.Set("mono", ((MenuItem)o).IsChecked ? 1 : 0); S.Save(); view.InvalidateVisual(); }, true, S.Get("mono", 1) > 0));
            cm.Items.Add(Mi(Loc.T("Üveg ikonháttér (csempe)"), delegate(object o, RoutedEventArgs a) { S.Set("tiles", ((MenuItem)o).IsChecked ? 1 : 0); S.Save(); view.InvalidateVisual(); }, true, S.Get("tiles", 1) > 0));
            cm.Items.Add(Mi(Loc.T("Automatikus elrejtés (egér a képernyő aljára)"), delegate(object o, RoutedEventArgs a) { S.Set("autohide", ((MenuItem)o).IsChecked ? 1 : 0); S.Save(); }, true, S.Get("autohide", 1) > 0));
            cm.Items.Add(Mi(Loc.T("Windows tálca ikonjainak elrejtése"), delegate(object o, RoutedEventArgs a) { S.Set("hidetb", ((MenuItem)o).IsChecked ? 1 : 0); S.Save(); N.HideTaskButtons(((MenuItem)o).IsChecked); }, true, S.Get("hidetb", 1) > 0));
            cm.Items.Add(Mi(Loc.T("Alkalmazás hozzáadása..."), delegate
            {
                Forms.OpenFileDialog d = new Forms.OpenFileDialog(); d.Filter = Loc.T("Programok (*.exe;*.lnk)|*.exe;*.lnk");
                if (d.ShowDialog() == Forms.DialogResult.OK) { M.AddFile(d.FileName); M.Refresh(); Place(); }
            }));
            cm.Items.Add(Mi(Loc.T("Alapértelmezett méret és hely"), delegate { S.Remove(K + "."); H = 64; autoW = true; double sb = Scr.Bounds.Bottom / scale; CX = (Scr.Bounds.X + Scr.Bounds.Width / 2.0) / scale; BY = sb - 52; Place(); SavePos(); }));
            cm.Items.Add(new Separator());
            cm.Items.Add(Mi(Loc.T("Kilépés"), delegate { Application.Current.Shutdown(); }));
            cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; };
            cm.PlacementTarget = this; cm.IsOpen = true;
        }

        // ---------- automatikus elrejtes ----------
        public void Tick()
        {
            if (hwnd == IntPtr.Zero) return;
            Step();
            if (snapDue != DateTime.MaxValue && DateTime.Now >= snapDue) { snapDue = DateTime.MaxValue; needSnap = true; }
            bool auto = S.Get("autohide", 1) > 0; N.POINT c; N.GetCursorPos(out c); N.RECT r; N.GetWindowRect(hwnd, out r);
            bool inDock = N.In(c, r, 8);
            if (edgeDir == 0) { foreach (N.RECT kr in Keep) if (N.In(c, kr, 6)) inDock = true; if (shown && N.FlyoutOpen()) inDock = true; }   // az also pilulak csak az ALUL levo dokkot tartjak nyitva
            System.Drawing.Rectangle sbn = Scr.Bounds;
            bool edge = edgeDir == 0 ? (c.Y >= sbn.Bottom - 2 && c.X >= sbn.X && c.X < sbn.Right)
                      : edgeDir == 1 ? (c.Y <= sbn.Top + 1 && c.X >= sbn.X && c.X < sbn.Right)
                      : edgeDir == 2 ? (c.X <= sbn.Left + 1 && c.Y >= sbn.Top && c.Y < sbn.Bottom)
                      : (c.X >= sbn.Right - 2 && c.Y >= sbn.Top && c.Y < sbn.Bottom);       // a dokk helyenek megfelelo kepernyoszel
            bool busy = Theme.Pinned || Preview.Force || menuOpen || IsMouseCaptured || resizeZone != 0 || moving || Preview.Visible;
            if (!auto || (shown && (inDock || busy)) || edge) lastIn = DateTime.Now;
            bool want = !auto || edge || busy || (shown && (DateTime.Now - lastIn).TotalMilliseconds < 700);
            // uj hatter-fotó (mozgatas/atmeretezes utan): ~70 ms-ra atlatszova teszi magat, lefenykepezi a hatteret, visszaall
            if (snapState == 1 && (DateTime.Now - snapAt).TotalMilliseconds >= 70)
            {
                N.RECT sr; N.GetWindowRect(hwnd, out sr); BitmapSource f = Frost.Capture(sr.L, sr.T, sr.R - sr.L, sr.B - sr.T, 16);
                if (f != null) frost = f; snapState = 0; needSnap = false; view.Opacity = 1;
            }
            else if (needSnap && snapState == 0 && shown && visibleW && !animating && !interactive && !moving && resizeZone == 0 && !pressed) { snapState = 1; snapAt = DateTime.Now; view.Opacity = 0; }
            Go(want);
        }
        public N.RECT ScreenRect { get { N.RECT r; N.GetWindowRect(hwnd, out r); return r; } }
        public void ShowFirst() { Place(); frost = Frost.Capture(baseX, baseY, winW, winH, 16); Show(); lastIn = DateTime.Now.AddSeconds(2.5); }
    }

    // ---------- asztali parancsikonok: iOS-szeru uveg csempe + fekete-feher ikon ----------
    static class ShellIcon
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImageList
        {
            [PreserveSig] int Add(IntPtr a, IntPtr b, ref int c);
            [PreserveSig] int ReplaceIcon(int i, IntPtr h, ref int c);
            [PreserveSig] int SetOverlayImage(int a, int b);
            [PreserveSig] int Replace(int i, IntPtr a, IntPtr b);
            [PreserveSig] int AddMasked(IntPtr a, int c, ref int d);
            [PreserveSig] int Draw(IntPtr p);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr icon);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string p, uint attr, ref SHFILEINFO fi, uint cb, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")] static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attr, ref SHFILEINFO fi, uint cb, uint flags);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);
        static bool IconIndex(string path, out int idx)
        {
            idx = 0; SHFILEINFO fi = new SHFILEINFO(); uint size = (uint)Marshal.SizeOf(fi);
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr pidl; uint at; if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out at) != 0 || pidl == IntPtr.Zero) return false;
                try { if (SHGetFileInfoPidl(pidl, 0, ref fi, size, 0x4008) == IntPtr.Zero) return false; } finally { CoTaskMemFree(pidl); }
            }
            else if (SHGetFileInfo(path, 0, ref fi, size, 0x4000) == IntPtr.Zero) return false;
            idx = fi.iIcon; return true;
        }
        [DllImport("shell32.dll", EntryPoint = "#727")] static extern int SHGetImageList(int list, ref Guid riid, out IImageList ppv);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        // az adott fajl (parancsikon, .url...) ikonja 256 px-en, a parancsikon-nyil nelkul
        public static BitmapSource Jumbo(string path)
        {
            int iconIdx; if (!IconIndex(path, out iconIdx)) return null;
            Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); IImageList il; if (SHGetImageList(4, ref iid, out il) != 0 || il == null) return null;
            IntPtr hi = IntPtr.Zero; if (il.GetIcon(iconIdx, 1, ref hi) != 0 || hi == IntPtr.Zero) return null;
            try
            {
                BitmapSource b = new FormatConvertedBitmap(Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(256, 256)), PixelFormats.Pbgra32, null, 0);
                int w = b.PixelWidth, h = b.PixelHeight; byte[] px = new byte[w * h * 4]; b.CopyPixels(px, w * 4, 0);
                int mx = -1, my = -1; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (px[(y * w + x) * 4 + 3] > 8) { if (x > mx) mx = x; if (y > my) my = y; }
                if (mx >= 0 && mx < 80 && my < 80) b = new CroppedBitmap(b, new Int32Rect(0, 0, mx + 1, my + 1));      // kis ikon a sarokban: kivagjuk, hogy kitoltse a csempet
                b.Freeze(); return b;
            }
            finally { DestroyIcon(hi); }
        }
        // egyszinu "lap" a hattérben (pl. UWP appok: Xbox, Store): a lapot eldobjuk, csak a jel marad, az kitolti a csempet
        public static BitmapSource StripPlate(BitmapSource src)
        {
            BitmapSource f = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0); int w = f.PixelWidth, h = f.PixelHeight; byte[] px = new byte[w * h * 4]; f.CopyPixels(px, w * 4, 0);
            int[] xs = { 1, w - 2 }, ys = { 1, h - 2 }; int cb = 0, cg = 0, cr = 0;
            foreach (int x in xs) foreach (int y in ys) { int i = (y * w + x) * 4; if (px[i + 3] < 200) return src; cb += px[i]; cg += px[i + 1]; cr += px[i + 2]; }
            cb /= 4; cg /= 4; cr /= 4; int near = 0;
            for (int i = 0; i < px.Length; i += 4) if (Math.Abs(px[i] - cb) + Math.Abs(px[i + 1] - cg) + Math.Abs(px[i + 2] - cr) < 40) near++;
            if (near < w * h * 0.45) return src;
            int mnx = w, mny = h, mxx = -1, mxy = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4; int d = Math.Abs(px[i] - cb) + Math.Abs(px[i + 1] - cg) + Math.Abs(px[i + 2] - cr); double a = d <= 36 ? 0 : d >= 80 ? 1 : (d - 36) / 44.0;
                px[i] = (byte)(px[i] * a); px[i + 1] = (byte)(px[i + 1] * a); px[i + 2] = (byte)(px[i + 2] * a); px[i + 3] = (byte)(px[i + 3] * a);
                if (a > 0.08) { if (x < mnx) mnx = x; if (x > mxx) mxx = x; if (y < mny) mny = y; if (y > mxy) mxy = y; }
            }
            if (mxx < 0) return src;
            BitmapSource all = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            int side = Math.Min(Math.Min(w, h), Math.Max(mxx - mnx + 1, mxy - mny + 1)), x0 = Math.Max(0, Math.Min(w - side, (mnx + mxx) / 2 - side / 2)), y0 = Math.Max(0, Math.Min(h - side, (mny + mxy) / 2 - side / 2));
            CroppedBitmap c = new CroppedBitmap(all, new Int32Rect(x0, y0, side, side)); c.Freeze(); return c;
        }
        // teljes (atlatszatlan, negyzetes) ikon pl. UWP-appoknal: ilyenkor maga az ikon a csempe
        public static bool FullBleed(BitmapSource b)
        {
            BitmapSource f = new FormatConvertedBitmap(b, PixelFormats.Pbgra32, null, 0); int w = f.PixelWidth, h = f.PixelHeight; byte[] px = new byte[w * h * 4]; f.CopyPixels(px, w * 4, 0);
            int[] xs = { 1, w - 2 }, ys = { 1, h - 2 }; foreach (int x in xs) foreach (int y in ys) if (px[(y * w + x) * 4 + 3] < 200) return false; return true;
        }
    }

    static class DeskTiles
    {
        static string root, bak, icoDir, logf; static bool failQuiet;
        static List<FileSystemWatcher> fws = new List<FileSystemWatcher>(); static Dictionary<string, DateTime> pending = new Dictionary<string, DateTime>(); static object lk = new object();
        static HashSet<string> failed = new HashSet<string>(); static DispatcherTimer tm;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(int e, uint f, string a, IntPtr b);

        public static void Init(string dir)
        {
            root = Path.Combine(dir, "desktop-tiles"); bak = Path.Combine(root, "backup"); icoDir = Path.Combine(root, "icons"); logf = Path.Combine(root, "tiles.log");
            Directory.CreateDirectory(bak); Directory.CreateDirectory(icoDir);
        }
        static void Log(string m) { try { LogUtil.Cap(logf); File.AppendAllText(logf, DateTime.Now.ToString("s") + " " + m + Environment.NewLine); } catch { } }
        public static string[] Dirs() { return new string[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }; }
        static string Id(string path)
        {
            using (System.Security.Cryptography.MD5 h = System.Security.Cryptography.MD5.Create())
            { byte[] b = h.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant())); StringBuilder sb = new StringBuilder(); for (int i = 0; i < 5; i++) sb.Append(b[i].ToString("x2")); return sb.ToString(); }
        }
        static string Safe(string n) { foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return n.Length > 40 ? n.Substring(0, 40) : n; }
        static object Lnk(string p)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            return t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { p });
        }
        static string LnkIcon(object sc) { return (string)sc.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null); }

        static bool Tiled(string path, string ext)
        {
            if (ext == ".lnk") { string il = LnkIcon(Lnk(path)); return il != null && il.StartsWith(icoDir, StringComparison.OrdinalIgnoreCase); }
            foreach (string l in File.ReadAllLines(path, Encoding.Default)) if (l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) && l.Substring(9).Trim().StartsWith(icoDir, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---- ikon rajzolas: sotet uveg csempe + az eredeti ikon fekete-feherben
        static byte[] RenderPng(BitmapSource glyph, int S, bool bleed)
        {
            DrawingVisual dv = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
            {
                double m = Math.Max(0.5, S / 128.0), r = S * 0.24; Rect t = new Rect(m, m, S - 2 * m, S - 2 * m);
                dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(178, 46, 52, 64), Color.FromArgb(150, 12, 14, 20), 90), new Pen(new SolidColorBrush(Color.FromArgb(125, 255, 255, 255)), Math.Max(1, S / 96.0)), t, r, r);
                dc.DrawRoundedRectangle(null, new Pen(new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), Math.Max(1, S / 128.0)), Rect.Inflate(t, -m * 1.6, -m * 1.6), r * 0.9, r * 0.9);
                if (bleed)
                {
                    dc.PushClip(new RectangleGeometry(t, r, r)); dc.DrawImage(glyph, t); dc.Pop();
                    dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(60, 60, 70, 90), Color.FromArgb(110, 8, 10, 16), 90), new Pen(new SolidColorBrush(Color.FromArgb(125, 255, 255, 255)), Math.Max(1, S / 96.0)), t, r, r);
                    dc.DrawRoundedRectangle(null, new Pen(new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), Math.Max(1, S / 128.0)), Rect.Inflate(t, -m * 1.6, -m * 1.6), r * 0.9, r * 0.9);
                }
                else { double pad = S * 0.17; dc.DrawImage(glyph, new Rect(pad, pad, S - 2 * pad, S - 2 * pad)); }
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using (MemoryStream ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
        static byte[] BuildIco(BitmapSource glyph, bool bleed)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 }; List<byte[]> png = new List<byte[]>(); foreach (int z in sizes) png.Add(RenderPng(glyph, z, bleed));
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length); int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++) { byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); bw.Write(d); bw.Write(d); bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32); bw.Write(png[i].Length); bw.Write(off); off += png[i].Length; }
                foreach (byte[] b in png) bw.Write(b);
                bw.Flush(); return ms.ToArray();
            }
        }

        static void SetLnkIcon(string path, string ico)
        {
            object sc = Lnk(path); Type t = sc.GetType();
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico + ",0" }); t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        static void SetUrlIcon(string path, string ico)
        {
            List<string> L = new List<string>(File.ReadAllLines(path, Encoding.Default));
            L.RemoveAll(delegate(string l) { return l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) || l.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase); });
            int sec = L.FindIndex(delegate(string l) { return l.Trim().Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase); });
            if (sec < 0) { L.Insert(0, "[InternetShortcut]"); sec = 0; }
            L.Insert(sec + 1, "IconFile=" + ico); L.Insert(sec + 2, "IconIndex=0");
            File.WriteAllLines(path, L.ToArray(), Encoding.Default);
        }

        public static bool Process(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant(); if (ext != ".lnk" && ext != ".url") return false;
            try
            {
                if (Tiled(path, ext)) return false;
                string id = Id(path), orig = Path.Combine(bak, id + ext);
                File.Copy(path, orig, true);                                                       // az eredeti mentese (visszaallitashoz)
                BitmapSource big = ShellIcon.Jumbo(orig); if (big != null) big = ShellIcon.StripPlate(big); if (big == null) { Log("nincs ikon: " + path); return false; }
                string ico = Path.Combine(icoDir, Safe(Path.GetFileNameWithoutExtension(path)) + "_" + id + ".ico");
                File.WriteAllBytes(ico, BuildIco(N.Mono(big, 86, 0.66), ShellIcon.FullBleed(big)));
                if (ext == ".lnk") SetLnkIcon(path, ico); else SetUrlIcon(path, ico);
                File.AppendAllText(Path.Combine(root, "map.txt"), id + "|" + path + Environment.NewLine);
                SHChangeNotify(0x2000, 0x5, path, IntPtr.Zero); Log("ok: " + path); return true;
            }
            catch (Exception ex) { lock (failed) failed.Add(path); Log("HIBA " + path + ": " + ex.Message); return false; }
        }
        public static List<string> Scan(string[] dirs)
        {
            List<string> r = new List<string>();
            foreach (string d in (dirs ?? Dirs())) if (Directory.Exists(d)) { r.AddRange(Directory.GetFiles(d, "*.lnk")); r.AddRange(Directory.GetFiles(d, "*.url")); }
            return r;
        }
        public static int RunAll(string[] dirs) { int n = 0; foreach (string f in Scan(dirs)) if (Process(f)) n++; return n; }
        public static int Restore()
        {
            int n = 0; string map = Path.Combine(root, "map.txt"); if (!File.Exists(map)) return 0;
            foreach (string l in File.ReadAllLines(map))
            {
                int i = l.IndexOf('|'); if (i < 1) continue; string id = l.Substring(0, i), path = l.Substring(i + 1), b = Path.Combine(bak, id + Path.GetExtension(path).ToLowerInvariant());
                try { if (File.Exists(b) && File.Exists(path)) { File.Copy(b, path, true); SHChangeNotify(0x2000, 0x5, path, IntPtr.Zero); n++; } } catch (Exception ex) { Log("visszaallitas HIBA " + path + ": " + ex.Message); }
            }
            return n;
        }

        // ---- fut a dokkal: minden meglevo + minden UJ parancsikon automatikusan megkapja
        static void Enq(string p, int delayMs) { string x = Path.GetExtension(p).ToLowerInvariant(); if (x != ".lnk" && x != ".url") return; lock (lk) pending[p] = DateTime.Now.AddMilliseconds(delayMs); }
        public static void Start()
        {
            foreach (string d in Dirs())
            {
                if (!Directory.Exists(d)) continue;
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(d); w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                    FileSystemEventHandler h = delegate(object o, FileSystemEventArgs e) { lock (failed) failed.Remove(e.FullPath); Enq(e.FullPath, 2500); };
                    w.Created += h; w.Changed += h; w.Renamed += delegate(object o, RenamedEventArgs e) { Enq(e.FullPath, 2500); }; w.EnableRaisingEvents = true; fws.Add(w);
                }
                catch (Exception ex) { Log("figyeles HIBA " + d + ": " + ex.Message); }
            }
            foreach (string f in Scan(null)) Enq(f, 3000);
            tm = new DispatcherTimer(); tm.Interval = TimeSpan.FromMilliseconds(300);
            tm.Tick += delegate
            {
                string next = null; lock (lk) foreach (KeyValuePair<string, DateTime> kv in pending) if (kv.Value <= DateTime.Now) { next = kv.Key; break; }
                if (next == null) return; lock (lk) pending.Remove(next);
                bool bad; lock (failed) bad = failed.Contains(next); if (!bad && File.Exists(next)) Process(next);
            };
            tm.Start();
        }
    }

    // A Windows 10 Start menu (kulso, a StartMenuExperienceHost ablaka): megnyilaskor lekerekitjuk es kicsit "lebegtetjuk"
    static class StartStyler
    {
        public static string AppDir; static IntPtr hw = IntPtr.Zero; static string last = ""; static int stable, applied; static double scale = 1; static bool init;
        static void Log(string m) { try { LogUtil.Cap(Path.Combine(AppDir, "start-styler.log")); File.AppendAllText(Path.Combine(AppDir, "start-styler.log"), DateTime.Now.ToString("s") + " " + m + Environment.NewLine); } catch { } }
        static IntPtr Find(bool onlyVisible)
        {
            IntPtr found = IntPtr.Zero;
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (onlyVisible && !N.IsWindowVisible(h)) return true;
                StringBuilder c = new StringBuilder(64); N.GetClassName(h, c, 64); if (c.ToString() != "Windows.UI.Core.CoreWindow") return true;
                int cl; N.DwmGetWindowAttribute(h, 14, out cl, 4); if (onlyVisible && cl != 0) return true;
                uint pid; N.GetWindowThreadProcessId(h, out pid); string exe = N.ExePath(pid);
                if (exe == null || !exe.EndsWith("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase)) return true;
                N.RECT r; N.GetWindowRect(h, out r); if (r.R - r.L < 300 || r.B - r.T < 300) return true;
                found = h; return false;
            }, IntPtr.Zero);
            return found;
        }
        public static void Tick(Settings s)
        {
            if (s.Get("startstyle", 0) <= 0) return;
            if (!init) { init = true; using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0; }
            IntPtr h = Find(true);
            if (h == IntPtr.Zero) { hw = IntPtr.Zero; last = ""; stable = 0; applied = 0; return; }
            N.RECT r; N.GetWindowRect(h, out r); string sg = h.ToInt64() + ":" + r.L + "," + r.T + "," + r.R + "," + r.B;
            if (sg != last) { last = sg; stable = 0; return; }        // meg mozog (animacio) vagy valtozott
            if (++stable != 3 || applied >= 3) return;                 // 3 egymas utani azonos meres = az animacio vege; legfeljebb 3x alkalmazzuk megnyitasonkent
            int w = r.R - r.L, hh = r.B - r.T, rad = (int)Math.Round(s.Get("startround", 22) * scale), gap = (int)Math.Round(s.Get("startgap", 10) * scale);
            N.SetWindowRgn(h, N.CreateRoundRectRgn(0, 0, w + 1, hh + 1, rad * 2, rad * 2), true);
            if (gap > 0) N.SetWindowPos(h, IntPtr.Zero, r.L + gap, r.T - gap, 0, 0, 0x15);
            N.RECT r2; N.GetWindowRect(h, out r2); last = h.ToInt64() + ":" + r2.L + "," + r2.T + "," + r2.R + "," + r2.B; stable = 99; applied++; hw = h;
            Log("stilus: hwnd=" + h.ToInt64() + " elotte=(" + r.L + "," + r.T + "," + w + "x" + hh + ") utana=(" + r2.L + "," + r2.T + ") lekerekites=" + rad + " tavolsag=" + gap);
        }
        public static void Reset()      // kilepeskor: a kivagas torlese
        {
            IntPtr h = Find(false); if (h != IntPtr.Zero) N.SetWindowRgn(h, IntPtr.Zero, true);
        }
    }

    // Win billentyu: a mi Start-menunk nyilik meg (a Windowsé helyett). Win+X kombinaciok, Win+E stb. valtozatlanul mukodnek.
    static class LogUtil { public static void Cap(string p) { try { FileInfo fi = new FileInfo(p); if (fi.Exists && fi.Length > 200000) File.Delete(p); } catch { } } }
    static class KeyHook
    {
        delegate IntPtr HP(int n, IntPtr w, IntPtr l);
        static HP proc = Cb; static IntPtr hh; static bool winDown, other, swallowedS, spaceSwallowed; public static Action OnWin, OnSpot;
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HP p, IntPtr m, uint t);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
        // A billentyű-horgot saját szál kezeli üzenethurokkal: a felület lassúsága (Start-lista, blur, indítás) nem késleltetheti a visszahívást,
        // különben a Windows (kb. 300 ms után) csendben leszedi a horgot, és a Win-gomb újra a gyári Start menüt nyitja.
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint t, uint m, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        static Thread ht; static uint htid; static Dispatcher disp;
        public static void Install()
        {
            if (ht != null) return; disp = Application.Current.Dispatcher;
            ht = new Thread(delegate()
            {
                htid = GetCurrentThreadId(); hh = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);
                MSG m; while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { if (m.m == 0x8001) { IntPtr old = hh; hh = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); if (hh == IntPtr.Zero) hh = old; else if (old != IntPtr.Zero) UnhookWindowsHookEx(old); } }
                if (hh != IntPtr.Zero) { UnhookWindowsHookEx(hh); hh = IntPtr.Zero; }
            });
            ht.IsBackground = true; ht.Priority = ThreadPriority.Highest; ht.Start();
        }
        public static void Rehook() { try { if (htid != 0) PostThreadMessage(htid, 0x8001, IntPtr.Zero, IntPtr.Zero); } catch { } }
        public static void Remove() { try { if (htid != 0) PostThreadMessage(htid, 0x12, IntPtr.Zero, IntPtr.Zero); else if (hh != IntPtr.Zero) { UnhookWindowsHookEx(hh); hh = IntPtr.Zero; } } catch { } }
        static IntPtr Cb(int n, IntPtr w, IntPtr l)
        {
            try {
            if (n >= 0)
            {
                int vk = Marshal.ReadInt32(l), flags = Marshal.ReadInt32(l, 8), msg = (int)w;
                if ((flags & 0x10) == 0)       // nem altalunk beinjektalt billentyu
                {
                    bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                    // ha a Win-fel-esemény kimaradt (Win+L, UAC, időtúllépés), a valós billentyű-állapothoz igazítunk: nem ragad be
                    if (winDown && vk != 0x5B && vk != 0x5C && (GetAsyncKeyState(0x5B) & 0x8000) == 0 && (GetAsyncKeyState(0x5C) & 0x8000) == 0) { winDown = false; swallowedS = false; }
                    if (vk == 0x5B || vk == 0x5C)
                    {
                        if (down) { if (!winDown) { winDown = true; other = false; swallowedS = false; } }
                        else if (up && winDown)
                        {
                            winDown = false;
                            if (!other) { N.keybd_event(0xE8, 0, 0, UIntPtr.Zero); N.keybd_event(0xE8, 0, 2, UIntPtr.Zero); if (OnWin != null) disp.BeginInvoke(OnWin); }   // 0xE8: ez "kitakarja" a Windows Start-menujet
                            else if (swallowedS) { N.keybd_event(0xE8, 0, 0, UIntPtr.Zero); N.keybd_event(0xE8, 0, 2, UIntPtr.Zero); }
                            swallowedS = false;
                        }
                    }
                    else if (vk == 0x53 && winDown && OnSpot != null && (GetAsyncKeyState(0x10) & 0x8000) == 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0)    // csak a sima Win+S: a Windows keresője helyett a Spotlight (a Win+Shift+S képernyő-kivágás érintetlen)
                    {
                        other = true;
                        if (down) { if (!swallowedS) disp.BeginInvoke(OnSpot); swallowedS = true; return (IntPtr)1; }       // az ismétlődő lenyomás nem nyitja/zárja újra
                        if (up && swallowedS) return (IntPtr)1;
                    }
                    else if (vk == 0x20 && OnSpot != null && !winDown && (((flags & 0x20) != 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0) || spaceSwallowed))   // Alt+Space
                    {
                        if (down) { if (!spaceSwallowed) disp.BeginInvoke(OnSpot); spaceSwallowed = true; return (IntPtr)1; }
                        if (up && spaceSwallowed) { spaceSwallowed = false; return (IntPtr)1; }
                    }
                    else if (down && winDown) other = true;
                }
            }
            }
            catch { }
            return CallNextHookEx(hh, n, w, l);
        }
    }

    class AppEntry { public string Name, Path; public bool Custom; public double X = -1, Y = -1; public FrameworkElement Cell; public Image Img; }

    class StartMenu : Window
    {
        static StartMenu inst; public static bool IsOpen { get { return inst != null && inst.open; } }
        public static void Toggle(Settings s, Forms.Screen scr) { if (inst == null) inst = new StartMenu(s, scr); inst.Flip(); }
        // a menü előre felépül (rejtve), így az első megnyitás azonnali és nem hiányos
        public static void PreBuild(Settings s, Forms.Screen scr) { try { if (inst == null) inst = new StartMenu(s, scr); inst.EnsureBuilt(); } catch { } }
        public static void Dump(string file) { if (inst != null) inst.DumpPng(file); }
        public static void CloseNow() { if (inst != null) inst.CloseMenu(); }
        const double W = 640, H = 720, CW = 104, CH = 110;
        Settings S; Forms.Screen Scr; double scale = 1; IntPtr hwnd; BitmapSource frost; CanvasView bg = new CanvasView(); Grid root = new Grid();
        TextBox search = new TextBox(); TextBlock hint = new TextBlock(); Canvas page = new Canvas(); Grid area = new Grid(); StackPanel dots = new StackPanel(); Border prevBtn, nextBtn; TranslateTransform slide = new TranslateTransform();
        List<AppEntry> apps = new List<AppEntry>(), vis = new List<AppEntry>(), pending = new List<AppEntry>();
        int cols = 5, rows = 5, cur, pageCount = 1, bx, by, bw, bh, atg, edgeDir; double op, a0;
        bool open, built, menuOpen, activated, flipBusy, dragging, prevDown; AppEntry drag; Point dragStart; DateTime openedAt, closedAt = DateTime.MinValue, edgeAt, wheelAt = DateTime.MinValue;
        DispatcherTimer loader = new DispatcherTimer(DispatcherPriority.Background), anim = new DispatcherTimer(DispatcherPriority.Render), watch = new DispatcherTimer(DispatcherPriority.Input); Stopwatch aw = new Stopwatch();
        static FontFamily Inter { get { return Theme.Font(); } }
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        static string F(string n) { string d = StartStyler.AppDir; if (string.IsNullOrEmpty(d)) d = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); return System.IO.Path.Combine(d, n); }
        static string[] ReadLines(string n) { try { string p = F(n); if (File.Exists(p)) return File.ReadAllLines(p, Encoding.UTF8); } catch { } return new string[0]; }
        static void WriteLines(string n, IEnumerable<string> l) { try { File.WriteAllLines(F(n), l, new UTF8Encoding(false)); } catch { } }
        static void DeleteFile(string n) { try { string p = F(n); if (File.Exists(p)) File.Delete(p); } catch { } }

        StartMenu(Settings s, Forms.Screen scr)
        {
            S = s; Scr = scr; using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; AllowsTransparency = false; Background = Brushes.Transparent; AllowDrop = true;
            Content = root; bg.Draw = PaintBg; RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.HighQuality); Width = W; Height = H; Left = -30000; Top = -30000;
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle; N.SetWindowLong(hwnd, -20, N.GetWindowLong(hwnd, -20) | 0x80);
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                N.MARGINS mg = new N.MARGINS(); mg.L = mg.R = mg.T = mg.B = -1; N.DwmExtendFrameIntoClientArea(hwnd, ref mg); int pol = 1; N.DwmSetWindowAttribute(hwnd, 2, ref pol, 4); N.PlainWindow(hwnd);
            };
            Deactivated += delegate { if (open && !menuOpen && activated && (DateTime.Now - openedAt).TotalMilliseconds > 400) CloseMenu(); };
            Activated += delegate { activated = true; };
            PreviewKeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; }
                else if (e.Key == Key.Enter) { int per = cols * rows; if (cur * per < vis.Count) Launch(vis[cur * per]); e.Handled = true; }
                else if (search.Text.Length == 0 && e.Key == Key.Right) { GoPage(cur + 1, false); e.Handled = true; }
                else if (search.Text.Length == 0 && e.Key == Key.Left) { GoPage(cur - 1, false); e.Handled = true; }
                else if (e.Key == Key.PageDown) { GoPage(cur + 1, false); e.Handled = true; }
                else if (e.Key == Key.PageUp) { GoPage(cur - 1, false); e.Handled = true; }
            };
            PreviewMouseWheel += delegate(object o, MouseWheelEventArgs e) { if ((DateTime.Now - wheelAt).TotalMilliseconds < 280) { e.Handled = true; return; } wheelAt = DateTime.Now; GoPage(cur + (e.Delta < 0 ? 1 : -1), false); e.Handled = true; };
            DragOver += delegate(object o, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object o, DragEventArgs e)
            {
                try { string[] fl = e.Data.GetData(DataFormats.FileDrop) as string[]; if (fl != null) foreach (string f in fl) AddApp(f); } catch { }
                e.Handled = true;
            };
            loader.Interval = TimeSpan.FromMilliseconds(15); loader.Tick += LoadTick; anim.Interval = TimeSpan.FromMilliseconds(8); anim.Tick += AnimStep; watch.Interval = TimeSpan.FromMilliseconds(30); watch.Tick += WatchTick;
        }

        void PaintBg(DrawingContext dc)
        {
            Rect full = new Rect(0.5, 0.5, W - 1, Math.Max(1, ActualHeight - 1)); double r = Rad;
            if (frost != null) dc.DrawRoundedRectangle(new ImageBrush(frost), null, full, r, r);
            byte ta = (byte)Math.Min(235, Math.Max(S.Get("glass", 135), frost != null ? 150 : 210));
            dc.DrawRoundedRectangle(new SolidColorBrush(Theme.TintA(ta)), null, full, r, r);
            dc.DrawRoundedRectangle(Theme.Gloss(), Theme.BorderPen(), full, r, r);
        }
        static double Rad { get { return Theme.MenuRound; } }

        Border Pillbtn(string text, MouseButtonEventHandler click)
        {
            Border b = new Border(); b.CornerRadius = new CornerRadius(12); b.Padding = new Thickness(14, 8, 14, 8); b.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)); b.BorderThickness = new Thickness(1); b.Cursor = Cursors.Hand;
            TextBlock t = new TextBlock(); t.Text = text; t.Foreground = Brushes.White; t.FontFamily = Inter; t.FontSize = Theme.FS(13); t.FontWeight = Theme.W(0); b.Child = t;
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); }; b.MouseLeave += delegate { b.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)); };
            b.MouseLeftButtonUp += click; return b;
        }

        Border NavBtn(string glyph, int dir)
        {
            Border b = new Border(); b.Width = 34; b.Height = 34; b.CornerRadius = new CornerRadius(17); b.Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)); b.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)); b.BorderThickness = new Thickness(1); b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center; b.HorizontalAlignment = dir > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            TextBlock t = new TextBlock(); t.Text = glyph; t.Foreground = Brushes.White; t.FontFamily = Inter; t.FontSize = Theme.FS(22); t.FontWeight = Theme.W(1); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, -3, 0, 0); b.Child = t;
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)); }; b.MouseLeave += delegate { b.Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)); };
            b.MouseLeftButtonUp += delegate { GoPage(cur + dir, false); }; return b;
        }

        void Build()
        {
            built = true; search = new TextBox(); hint = new TextBlock(); page = new Canvas(); area = new Grid(); dots = new StackPanel(); slide = new TranslateTransform(); root.Children.Add(bg);
            Grid g = new Grid(); g.Margin = new Thickness(22, 20, 22, 16); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border sb = new Border(); sb.CornerRadius = new CornerRadius(14); sb.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)); sb.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); sb.BorderThickness = new Thickness(1); sb.Padding = new Thickness(14, 9, 14, 9);
            Grid sg = new Grid(); search.Background = Brushes.Transparent; search.BorderThickness = new Thickness(0); search.Foreground = Brushes.White; search.CaretBrush = Brushes.White; search.FontFamily = Inter; search.FontSize = Theme.FS(15); search.FontWeight = Theme.W(0);
            hint.Text = Loc.T("Keresés"); hint.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)); hint.FontFamily = Inter; hint.FontSize = Theme.FS(15); hint.IsHitTestVisible = false; hint.Margin = new Thickness(2, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center;
            sg.Children.Add(search); sg.Children.Add(hint); sb.Child = sg; Grid.SetRow(sb, 0); g.Children.Add(sb);
            search.TextChanged += delegate { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; cur = 0; Layout(false); };

            page.RenderTransform = slide; page.HorizontalAlignment = HorizontalAlignment.Center; page.VerticalAlignment = VerticalAlignment.Center; page.ClipToBounds = false;
            prevBtn = NavBtn("‹", -1); nextBtn = NavBtn("›", 1);
            area.Margin = new Thickness(0, 14, 0, 2); area.Children.Add(page); area.Children.Add(prevBtn); area.Children.Add(nextBtn); Grid.SetRow(area, 1); g.Children.Add(area);
            area.SizeChanged += delegate { int nr = Math.Max(1, (int)(area.ActualHeight / CH)); if (nr != rows) { rows = nr; if (built) Layout(false); } };
            dots.Orientation = Orientation.Horizontal; dots.HorizontalAlignment = HorizontalAlignment.Center; dots.Height = 16; dots.Margin = new Thickness(0, 0, 0, 8); Grid.SetRow(dots, 2); g.Children.Add(dots);

            Grid bottom = new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border setBtn0 = Pillbtn("⚙", delegate { CloseMenu(); try { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); } catch { } });
            setBtn0.ToolTip = Loc.T("Windows beállítások"); setBtn0.Margin = new Thickness(0, 0, 6, 0);
            Border cmdBtn = Pillbtn("⌨  CMD", delegate { RunShell("cmd.exe", "", false); }); cmdBtn.ToolTip = Loc.T("Parancssor (jobb klikk: rendszergazdaként)"); cmdBtn.MouseRightButtonUp += delegate { ShellMenu(cmdBtn, "cmd.exe", ""); };
            cmdBtn.Margin = new Thickness(0, 0, 6, 0);
            Border psBtn = Pillbtn("❯  PS", delegate { RunShell("powershell.exe", "-NoLogo", false); }); psBtn.ToolTip = Loc.T("PowerShell (jobb klikk: rendszergazdaként)"); psBtn.MouseRightButtonUp += delegate { ShellMenu(psBtn, "powershell.exe", "-NoLogo"); };
            StackPanel setBtn = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left }; setBtn.Children.Add(setBtn0); setBtn.Children.Add(cmdBtn); setBtn.Children.Add(psBtn);
            Border addBtn = Pillbtn(Loc.T("+  Alkalmazás hozzáadása"), delegate { PickApp(); }); addBtn.ToolTip = Loc.T("Kattints: fájl kiválasztása. Vagy húzz ide egy programot/parancsikont. Jobb klikk: több lehetőség.");
            addBtn.MouseRightButtonUp += delegate { AddMenu(addBtn); };
            Border pw = Pillbtn(Loc.T("Kikapcsolás"), delegate(object o, MouseButtonEventArgs e) { PowerMenu((FrameworkElement)o); }); pw.HorizontalAlignment = HorizontalAlignment.Right;
            addBtn.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(addBtn, 1); Grid.SetColumn(pw, 2); bottom.Children.Add(setBtn); bottom.Children.Add(addBtn); bottom.Children.Add(pw); Grid.SetRow(bottom, 3); g.Children.Add(bottom); root.Children.Add(g);
            Populate();
        }

        // a telepített + hozzáadott alkalmazások listája (a Start-menü és a Spotlight közös, gyorsítótárazott listája)
        static List<AppEntry> entryCache; static DateTime entryAt = DateTime.MinValue; static int appsVersion, entryVersion = -1;
        internal static volatile bool entryDirty;
        static int invGen; internal static void Invalidate() { Interlocked.Increment(ref invGen); entryDirty = true; }
        internal static bool Ready { get { return entryCache != null; } }
        static List<AppEntry> installedCache; static DateTime installedAt = DateTime.MinValue; static int installedVer = -1;
        static List<AppEntry> Installed()
        {
            if (installedCache == null || installedVer != appsVersion || (DateTime.Now - installedAt).TotalMinutes > 20) { installedVer = appsVersion; installedCache = ListApps(); installedAt = DateTime.Now; }
            return installedCache;
        }
        internal static List<AppEntry> Entries()
        {
            if (entryCache == null || entryDirty || entryVersion != appsVersion)
            {
                if (entryCache != null) { Prefetch(); return entryCache; }          // a régi lista azonnal használható, az újat háttérben építjük (nem akasztja meg a felületet)
                entryDirty = false; entryVersion = appsVersion; entryCache = LoadEntries(); entryAt = DateTime.Now;
            }
            return entryCache;
        }
        static List<AppEntry> LoadEntries()
        {
            HashSet<string> hidden = new HashSet<string>(ReadLines("starthidden.txt"), StringComparer.OrdinalIgnoreCase);
            List<AppEntry> list = new List<AppEntry>();
            foreach (AppEntry a in Installed()) if (!hidden.Contains(a.Path)) list.Add(a);
            foreach (string ln in ReadLines("startcustom.txt"))
            {
                int i = ln.IndexOf('|'); if (i <= 0) continue;
                AppEntry a = new AppEntry(); a.Name = ln.Substring(0, i); a.Path = ln.Substring(i + 1); a.Custom = true;
                if (File.Exists(a.Path) || Directory.Exists(a.Path)) list.Add(a);
            }
            Dictionary<string, int> idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); string[] ord = ReadLines("startorder.txt");
            for (int i = 0; i < ord.Length; i++) if (!idx.ContainsKey(ord[i])) idx[ord[i]] = i;
            list.Sort(delegate(AppEntry x, AppEntry y)
            {
                int ix, iy; bool kx = idx.TryGetValue(x.Path, out ix), ky = idx.TryGetValue(y.Path, out iy);
                if (kx && ky) return ix.CompareTo(iy); if (kx != ky) return kx ? -1 : 1;
                return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }
        static List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        public static void WatchApps()
        {
            foreach (string dir in new string[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs") })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue; FileSystemWatcher w = new FileSystemWatcher(dir); w.IncludeSubdirectories = true; w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
                    FileSystemEventHandler h = delegate { appsVersion++; SchedulePrefetch(); }; w.Created += h; w.Deleted += h; w.Renamed += delegate { appsVersion++; SchedulePrefetch(); }; w.EnableRaisingEvents = true; watchers.Add(w);
                }
                catch { }
            }
        }
        static volatile bool prefetching;
        public static void Prefetch()
        {
            if (prefetching) return; prefetching = true; IcCfg();
            Thread th = new Thread(delegate() { try { int ver = appsVersion, g = invGen; List<AppEntry> l = LoadEntries(); if (g == invGen) { entryVersion = ver; entryDirty = false; entryCache = l; entryAt = DateTime.Now; } WarmIcons(l); } catch { } finally { prefetching = false; } });
            th.SetApartmentState(ApartmentState.STA); th.IsBackground = true; th.Priority = ThreadPriority.BelowNormal; th.Start();
        }
        static DispatcherTimer prefetchT;
        static void SchedulePrefetch()      // az új telepítés után néhány mp-pel, tétlenül frissítjük a listát, hogy a megnyitáskor ne akadjon
        {
            try { Application.Current.Dispatcher.BeginInvoke(new Action(delegate { if (prefetchT == null) { prefetchT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; prefetchT.Tick += delegate { prefetchT.Stop(); Prefetch(); }; } prefetchT.Stop(); prefetchT.Start(); })); } catch { }
        }

        // az alkalmazaslista felepitese: telepitettek - elrejtettek + sajat hozzaadottak, a mentett sorrendben
        void Populate()
        {
            IcCfg(); page.Children.Clear(); apps.Clear(); pending.Clear();
            List<AppEntry> list = new List<AppEntry>();
            List<AppEntry> srcL = Entries(); popSrc = srcL; foreach (AppEntry src in srcL) { AppEntry c = new AppEntry(); c.Name = src.Name; c.Path = src.Path; c.Custom = src.Custom; list.Add(c); }
            foreach (AppEntry a in list) { a.Cell = MakeCell(a); a.Cell.Visibility = Visibility.Collapsed; page.Children.Add(a.Cell); apps.Add(a); pending.Add(a); }
            if (!loader.IsEnabled) loader.Start();
            Layout(false);
        }

        void SaveOrder() { Invalidate(); List<string> l = new List<string>(); foreach (AppEntry a in apps) l.Add(a.Path); WriteLines("startorder.txt", l); }

        void PickApp()
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog(); d.Title = Loc.T("Alkalmazás hozzáadása"); d.Multiselect = true; d.DereferenceLinks = false;
            d.Filter = Loc.T("Programok és parancsikonok|*.exe;*.lnk;*.url;*.bat;*.cmd|Minden fájl|*.*");
            bool? r; menuOpen = true; try { r = d.ShowDialog(this); } finally { menuOpen = false; }
            if (r == true) foreach (string f in d.FileNames) AddApp(f);
            try { Activate(); search.Focus(); } catch { }
        }

        void AddApp(string path)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return;
            foreach (AppEntry x in apps) if (string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) { cur = 0; Layout(true); return; }
            AppEntry a = new AppEntry(); a.Name = System.IO.Path.GetFileNameWithoutExtension(path); if (a.Name.Length == 0) a.Name = System.IO.Path.GetFileName(path); a.Path = path; a.Custom = true;
            a.Cell = MakeCell(a); a.Cell.Visibility = Visibility.Collapsed; page.Children.Add(a.Cell); apps.Insert(0, a); LoadIcon(a);
            List<string> cl = new List<string>(ReadLines("startcustom.txt")); cl.Add(a.Name + "|" + a.Path); WriteLines("startcustom.txt", cl); SaveOrder();
            search.Text = ""; cur = 0; Layout(true);
        }

        void RemoveApp(AppEntry a)
        {
            apps.Remove(a); page.Children.Remove(a.Cell);
            if (a.Custom) { List<string> cl = new List<string>(); foreach (string ln in ReadLines("startcustom.txt")) if (!ln.EndsWith("|" + a.Path, StringComparison.OrdinalIgnoreCase)) cl.Add(ln); WriteLines("startcustom.txt", cl); }
            else { List<string> hl = new List<string>(ReadLines("starthidden.txt")); hl.Add(a.Path); WriteLines("starthidden.txt", hl); }
            SaveOrder(); Layout(true);
        }

        void AddMenu(FrameworkElement anchor)
        {
            ContextMenu cm = new ContextMenu(); cm.FontFamily = Inter; cm.PlacementTarget = anchor; cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            MenuItem a = new MenuItem(); a.Header = Loc.T("Elrejtett alkalmazások visszahozása"); a.Click += delegate { DeleteFile("starthidden.txt"); Invalidate(); entryCache = null; Populate(); }; cm.Items.Add(a);
            MenuItem b = new MenuItem(); b.Header = Loc.T("Sorrend visszaállítása (ABC)"); b.Click += delegate { DeleteFile("startorder.txt"); Invalidate(); entryCache = null; Populate(); }; cm.Items.Add(b);
            cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; }; cm.IsOpen = true;
        }

        void PowerMenu(FrameworkElement anchor)
        {
            ContextMenu cm = new ContextMenu(); cm.FontFamily = Inter; cm.PlacementTarget = anchor; cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            string[][] items = { new[] { Loc.T("Alvás"), "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0" }, new[] { Loc.T("Újraindítás"), "shutdown.exe", "/r /t 0" }, new[] { Loc.T("Leállítás"), "shutdown.exe", "/s /t 0" }, new[] { Loc.T("Kijelentkezés"), "shutdown.exe", "/l" } };
            foreach (string[] it in items) { string[] x = it; MenuItem mi = new MenuItem(); mi.Header = x[0]; mi.Click += delegate { CloseMenu(); try { Process.Start(new ProcessStartInfo(x[1], x[2]) { UseShellExecute = true }); } catch { } }; cm.Items.Add(mi); }
            cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; }; cm.IsOpen = true;
        }

        static List<AppEntry> ListApps()
        {
            List<AppEntry> l = new List<AppEntry>();
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application"); object sh = Activator.CreateInstance(t);
                object ns = t.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, sh, new object[] { "shell:AppsFolder" });
                object items = ns.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, ns, null);
                int n = (int)items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null);
                for (int i = 0; i < n; i++)
                {
                    object it = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                    string name = (string)it.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, it, null), path = (string)it.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, it, null);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue; AppEntry a = new AppEntry(); a.Name = name; a.Path = path; l.Add(a);
                }
            }
            catch { }
            return l;
        }

        FrameworkElement MakeCell(AppEntry a)
        {
            Border tile = new Border(); tile.Width = 58; tile.Height = 58; tile.CornerRadius = new CornerRadius(Theme.TileRound); tile.HorizontalAlignment = HorizontalAlignment.Center;
            tile.Background = new LinearGradientBrush(Color.FromArgb(Theme.TileA, 255, 255, 255), Color.FromArgb((byte)(Theme.TileA / 3), 255, 255, 255), 90); tile.BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)); tile.BorderThickness = new Thickness(1);
            Image img = new Image(); img.Width = 36; img.Height = 36; img.Stretch = Stretch.Uniform; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); tile.Child = img; a.Img = img;
            TextBlock tb = new TextBlock(); tb.Text = a.Name; tb.Foreground = Brushes.White; tb.FontFamily = Inter; tb.FontSize = Theme.FS(11); tb.FontWeight = Theme.W(0); tb.TextAlignment = TextAlignment.Center; tb.TextWrapping = TextWrapping.Wrap; tb.TextTrimming = TextTrimming.CharacterEllipsis; tb.MaxHeight = 30; tb.Width = 88; tb.Margin = new Thickness(0, 6, 0, 0);
            StackPanel sp = new StackPanel(); sp.Children.Add(tile); sp.Children.Add(tb);
            Border hv = new Border(); hv.Width = 100; hv.Height = 108; hv.Padding = new Thickness(0, 8, 0, 6); hv.CornerRadius = new CornerRadius(12); hv.Background = Brushes.Transparent; hv.Child = sp; hv.Cursor = Cursors.Hand; hv.ToolTip = a.Name;
            hv.RenderTransformOrigin = new Point(0.5, 0.5);
            hv.MouseEnter += delegate { if (drag == null) hv.Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)); }; hv.MouseLeave += delegate { if (drag != a) hv.Background = Brushes.Transparent; };
            // bal gomb: kattintas = inditas, huzas = athelyezes (elo atrendezes, a szelen 0,6 mp utan lapoz)
            hv.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { if (flipBusy) return; drag = a; dragging = false; dragStart = e.GetPosition(page); edgeDir = 0; hv.CaptureMouse(); e.Handled = true; };
            hv.MouseMove += delegate(object o, MouseEventArgs e)
            {
                if (drag != a || e.LeftButton != MouseButtonState.Pressed) return; Point p = e.GetPosition(page);
                if (!dragging)
                {
                    if (search.Text.Length > 0 || Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 9) return;
                    dragging = true; hv.Opacity = 0.7; hv.RenderTransform = new ScaleTransform(1.1, 1.1); Panel.SetZIndex(hv, 50); hv.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                }
                DragMove(p);
            };
            hv.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                if (drag != a) return; bool was = dragging; drag = null; dragging = false; hv.ReleaseMouseCapture(); EndDragVisual(hv);
                if (was) SaveOrder(); else Launch(a); e.Handled = true;
            };
            hv.LostMouseCapture += delegate { if (drag == a) { bool was = dragging; drag = null; dragging = false; EndDragVisual(hv); if (was) SaveOrder(); } };
            hv.MouseRightButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                ContextMenu cm = new ContextMenu(); cm.FontFamily = Inter; MenuItem o1 = new MenuItem(); o1.Header = Loc.T("Megnyitás"); o1.Click += delegate { Launch(a); }; cm.Items.Add(o1);
                MenuItem oa = new MenuItem(); oa.Header = Loc.T("Futtatás rendszergazdaként"); oa.Click += delegate { LaunchAdmin(a); }; cm.Items.Add(oa);
                MenuItem o2 = new MenuItem(); o2.Header = a.Custom ? Loc.T("Eltávolítás a menüből") : Loc.T("Elrejtés a menüből"); o2.Click += delegate { RemoveApp(a); }; cm.Items.Add(o2);
                cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; }; cm.PlacementTarget = hv; cm.IsOpen = true; e.Handled = true;
            };
            return hv;
        }
        void EndDragVisual(Border hv) { hv.Opacity = 1; hv.RenderTransform = null; Panel.SetZIndex(hv, 0); hv.Background = Brushes.Transparent; }

        void DragMove(Point p)
        {
            if (drag == null || p.X < 0 || p.Y < 0 || p.X >= page.Width || p.Y >= page.Height) return;
            int per = cols * rows, idx = cur * per + (int)(p.Y / CH) * cols + (int)(p.X / CW); if (idx >= apps.Count) idx = apps.Count - 1;
            int old = apps.IndexOf(drag); if (idx < 0 || idx == old) return;
            apps.RemoveAt(old); apps.Insert(idx, drag); Layout(true);
        }

        // ikon-gyorsítótár: indulás után háttérben előre elkészül (Prefetch), így az első megnyitáskor már minden ikon kész
        static Dictionary<string, BitmapSource> iconCache = new Dictionary<string, BitmapSource>(); static readonly object icLock = new object();
        static volatile bool icMono = true; static double icGain = 0.66; static string icSfx = "";
        static void IcCfg() { icMono = Theme.S.Get("mono", 1) > 0; icGain = Theme.IconGain(0.66); string sfx = icMono ? "m" + icGain.ToString("0.00") : "c"; lock (icLock) { if (sfx != icSfx) { iconCache.Clear(); icSfx = sfx; } } }
        static string IconKey(AppEntry a) { return a.Path + "|" + icSfx; }
        static BitmapSource MakeIcon(AppEntry a)
        {
            BitmapSource big = ShellIcon.Jumbo(a.Custom ? a.Path : "shell:AppsFolder\\" + a.Path); if (big == null) return null;
            BitmapSource small = new TransformedBitmap(big, new ScaleTransform(0.28, 0.28)); small.Freeze();
            BitmapSource prep = ShellIcon.StripPlate(small); BitmapSource r = icMono ? N.Mono(prep, 86, icGain) : N.Own(prep); if (r != null && r.CanFreeze && !r.IsFrozen) r.Freeze(); return r;
        }
        static void WarmIcons(List<AppEntry> list)
        {
            foreach (AppEntry a in list.ToArray())
            {
                try { string k = IconKey(a); lock (icLock) { if (iconCache.ContainsKey(k)) continue; } BitmapSource b = MakeIcon(a); if (b != null) lock (icLock) { iconCache[k] = b; } } catch { }
                System.Threading.Thread.Sleep(12);
            }
        }
        void LoadIcon(AppEntry a)
        {
            try
            {
                string k = IconKey(a); BitmapSource c; lock (icLock) { iconCache.TryGetValue(k, out c); }
                if (c == null) { c = MakeIcon(a); if (c == null) return; lock (icLock) { iconCache[k] = c; } }
                a.Img.Source = c;
            }
            catch { }
        }
        void LoadTick(object s, EventArgs e)
        {
            for (int k = 0; k < 6 && pending.Count > 0; k++) { AppEntry a = pending[0]; pending.RemoveAt(0); LoadIcon(a); }
            if (pending.Count == 0) loader.Stop();
        }

        // a latszo (szurt) lista lapokra osztva; csak az aktualis lap cellai latszanak, a mozgas animalt
        void Layout(bool animate)
        {
            string q = search.Text.Trim(); System.Globalization.CompareInfo ci = CultureInfo.CurrentCulture.CompareInfo; vis.Clear();
            foreach (AppEntry a in apps) { if (q.Length == 0 || ci.IndexOf(a.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) vis.Add(a); else a.Cell.Visibility = Visibility.Collapsed; }
            int per = Math.Max(1, cols * rows); pageCount = Math.Max(1, (vis.Count + per - 1) / per); if (cur >= pageCount) cur = pageCount - 1; if (cur < 0) cur = 0;
            page.Width = cols * CW; page.Height = rows * CH;
            for (int i = 0; i < vis.Count; i++)
            {
                AppEntry a = vis[i]; if (i / per != cur) { a.Cell.Visibility = Visibility.Collapsed; a.X = a.Y = -1; continue; }
                int k = i % per; double x = (k % cols) * CW + 2, y = (k / cols) * CH; bool was = a.Cell.Visibility == Visibility.Visible && a.X >= 0;
                a.Cell.Visibility = Visibility.Visible;
                if (animate && was && (a.X != x || a.Y != y))
                {
                    System.Windows.Media.Animation.CubicEase ez = new System.Windows.Media.Animation.CubicEase(); ez.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut;
                    System.Windows.Media.Animation.DoubleAnimation ax = new System.Windows.Media.Animation.DoubleAnimation(a.X, x, TimeSpan.FromMilliseconds(190)); ax.EasingFunction = ez;
                    System.Windows.Media.Animation.DoubleAnimation ay = new System.Windows.Media.Animation.DoubleAnimation(a.Y, y, TimeSpan.FromMilliseconds(190)); ay.EasingFunction = ez;
                    Canvas.SetLeft(a.Cell, x); Canvas.SetTop(a.Cell, y); a.Cell.BeginAnimation(Canvas.LeftProperty, ax); a.Cell.BeginAnimation(Canvas.TopProperty, ay);
                }
                else { a.Cell.BeginAnimation(Canvas.LeftProperty, null); a.Cell.BeginAnimation(Canvas.TopProperty, null); Canvas.SetLeft(a.Cell, x); Canvas.SetTop(a.Cell, y); }
                a.X = x; a.Y = y;
            }
            if (prevBtn != null)
            {
                prevBtn.Visibility = cur > 0 ? Visibility.Visible : Visibility.Hidden; nextBtn.Visibility = cur < pageCount - 1 ? Visibility.Visible : Visibility.Hidden;
                dots.Children.Clear();
                if (pageCount > 1) for (int i = 0; i < pageCount; i++)
                {
                    int pi = i; System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse(); el.Width = 7; el.Height = 7; el.Margin = new Thickness(4, 0, 4, 0); el.Fill = new SolidColorBrush(Color.FromArgb((byte)(i == cur ? 235 : 85), 255, 255, 255)); el.Cursor = Cursors.Hand;
                    el.MouseLeftButtonUp += delegate { GoPage(pi, false); }; dots.Children.Add(el);
                }
            }
        }

        // lapozas: kifut + befut animacio; dragMove: a huzott elem atkerul az uj lapra
        void GoPage(int p, bool dragMove)
        {
            if (flipBusy || p < 0 || p >= pageCount || p == cur) return;
            int dir = p > cur ? 1 : -1; flipBusy = true;
            System.Windows.Media.Animation.DoubleAnimation o = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(90));
            o.Completed += delegate
            {
                cur = p;
                if (dragMove && drag != null)
                {
                    int per = cols * rows, old = apps.IndexOf(drag); apps.RemoveAt(old); int ix = dir > 0 ? p * per : p * per + per - 1; apps.Insert(Math.Min(Math.Max(0, ix), apps.Count), drag);
                }
                Layout(false); slide.X = dir * 46;
                System.Windows.Media.Animation.CubicEase ez = new System.Windows.Media.Animation.CubicEase(); ez.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut;
                System.Windows.Media.Animation.DoubleAnimation ix2 = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(230)); ix2.EasingFunction = ez;
                System.Windows.Media.Animation.DoubleAnimation io = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(230)); io.Completed += delegate { flipBusy = false; };
                slide.BeginAnimation(TranslateTransform.XProperty, ix2); page.BeginAnimation(UIElement.OpacityProperty, io);
            };
            System.Windows.Media.Animation.DoubleAnimation ox = new System.Windows.Media.Animation.DoubleAnimation(-dir * 46, TimeSpan.FromMilliseconds(90));
            slide.BeginAnimation(TranslateTransform.XProperty, ox); page.BeginAnimation(UIElement.OpacityProperty, o);
        }

        // 30 ms-onkent, amig nyitva a menu: kattintas a menun kivul = bezaras; huzas kozben a szelen toprengve lapoz
        void WatchTick(object s, EventArgs e)
        {
            if (!open) return;
            bool down = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0; DateTime now = DateTime.Now; N.POINT c; N.GetCursorPos(out c);
            if (down && !prevDown && !menuOpen && (now - openedAt).TotalMilliseconds > 250)
            {
                if (c.X < bx || c.X >= bx + bw || c.Y < by - 4 || c.Y >= by + bh + 4) { prevDown = down; CloseMenu(); return; }
            }
            prevDown = down;
            if (dragging && drag != null && !flipBusy)
            {
                Point p = page.PointFromScreen(new Point(c.X, c.Y)); int dir = p.X < -6 ? -1 : (p.X > page.Width + 6 ? 1 : 0);
                if (dir == 0) edgeDir = 0;
                else if (dir != edgeDir) { edgeDir = dir; edgeAt = now; }
                else if ((now - edgeAt).TotalMilliseconds > 600) { edgeAt = now; GoPage(cur + dir, true); }
            }
        }

        // parancssor / PowerShell indítása (a felhasználói mappában), opcionálisan rendszergazdaként (UAC)
        void RunShell(string exe, string args, bool admin)
        {
            CloseMenu();
            try { ProcessStartInfo pi = new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }; if (admin) pi.Verb = "runas"; Process.Start(pi); } catch { }
        }
        void ShellMenu(FrameworkElement anchor, string exe, string args)
        {
            ContextMenu cm = new ContextMenu(); cm.FontFamily = Inter; MenuItem a1 = new MenuItem { Header = Loc.T("Megnyitás") }; a1.Click += delegate { RunShell(exe, args, false); }; cm.Items.Add(a1);
            MenuItem a2 = new MenuItem { Header = Loc.T("Megnyitás rendszergazdaként") }; a2.Click += delegate { RunShell(exe, args, true); }; cm.Items.Add(a2);
            cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; }; cm.PlacementTarget = anchor; cm.IsOpen = true;
        }
        void LaunchAdmin(AppEntry a)
        {
            CloseMenu();
            try { ProcessStartInfo pi = new ProcessStartInfo(a.Custom ? a.Path : "shell:AppsFolder\\" + a.Path) { UseShellExecute = true, Verb = "runas" }; if (a.Custom) { try { pi.WorkingDirectory = System.IO.Path.GetDirectoryName(a.Path); } catch { } } Process.Start(pi); } catch { }
        }
        void Launch(AppEntry a)
        {
            CloseMenu();
            try
            {
                if (a.Custom) { ProcessStartInfo pi = new ProcessStartInfo(a.Path) { UseShellExecute = true }; try { pi.WorkingDirectory = System.IO.Path.GetDirectoryName(a.Path); } catch { } Process.Start(pi); }
                else Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + a.Path) { UseShellExecute = true });
            }
            catch { }
        }

        void Flip() { if (open) CloseMenu(); else if ((DateTime.Now - closedAt).TotalMilliseconds > 300) OpenMenu(); }
        int popVersion = -1, themeVer = -1; List<AppEntry> popSrc;
        public void EnsureBuilt() { if (!built) { Build(); themeVer = S.Version; popVersion = appsVersion; } }
        void OpenMenu()
        {
            if (built && themeVer != S.Version) { root.Children.Clear(); built = false; }
            if (!built) { Build(); themeVer = S.Version; } else if (popVersion != appsVersion || popSrc != entryCache) Populate();
            popVersion = appsVersion;
            try { Scr = Forms.Screen.FromPoint(Forms.Cursor.Position); using (System.Drawing.Graphics g0 = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g0.DpiX / 96.0; } catch { }
            bw = (int)Math.Round(W * scale); bh = (int)Math.Round(Math.Min(H, Scr.Bounds.Height / scale - 120) * scale);
            bx = Scr.Bounds.X + (int)Math.Round(14 * scale); by = Scr.Bounds.Bottom - (int)Math.Round(56 * scale) - bh;
            Height = bh / scale; drag = null; dragging = false; flipBusy = false; menuOpen = false; prevDown = true;
            slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = 0; page.BeginAnimation(UIElement.OpacityProperty, null); page.Opacity = 1;
            search.Text = ""; cur = 0; Layout(false); if (pending.Count > 0 && !loader.IsEnabled) loader.Start();
            frost = Frost.Capture(bx, by, bw, bh, 16); bg.InvalidateVisual();               // a hatter-foto az ablak megjelenese elott
            root.Opacity = 0; op = 0; open = true; activated = false; openedAt = DateTime.Now; watch.Start();
            if (hwnd == IntPtr.Zero) { Left = bx / scale; Top = by / scale; Show(); }
            N.SetWindowPos(hwnd, new IntPtr(-1), bx, by + (int)Math.Round(28 * scale), bw, bh, 0x40);
            Show(); Activate(); N.SwitchToThisWindow(hwnd, true); search.Focus(); Keyboard.Focus(search); StartAnim(1);
            // a rétegzett ablak első képkockájából néha kimarad a háttér: kényszerített újrarajzolás (azonnal, majd kis késleltetéssel)
            Dispatcher.BeginInvoke(new Action(delegate { bg.InvalidateVisual(); root.InvalidateVisual(); }), DispatcherPriority.Render);
            DispatcherTimer rt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) }; rt.Tick += delegate { rt.Stop(); if (open) { bg.InvalidateVisual(); } }; rt.Start();
        }
        void CloseMenu() { if (!open) return; open = false; closedAt = DateTime.Now; watch.Stop(); StartAnim(0); }
        void StartAnim(int t) { atg = t; a0 = op; aw.Restart(); anim.Start(); }
        void AnimStep(object s, EventArgs e)
        {
            double t = Math.Min(1.0, aw.ElapsedMilliseconds / (atg == 1 ? 280.0 : 170.0) / Theme.Anim);
            op = a0 + (atg - a0) * (atg == 1 ? Ease.OutCubic(t) : Ease.InCubic(t)); root.Opacity = Math.Max(0, Math.Min(1, op));
            N.SetWindowPos(hwnd, new IntPtr(-1), bx, by + (int)Math.Round((1 - Math.Max(0, Math.Min(1, op))) * 28 * scale), 0, 0, 0x11);
            if (t >= 1) { anim.Stop(); if (atg == 0) Hide(); }
        }
        void DumpPng(string file)
        {
            root.Measure(new Size(W, Height)); root.Arrange(new Rect(0, 0, W, Height)); root.UpdateLayout(); Layout(false); root.UpdateLayout(); root.Opacity = 1;
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)(W * scale), (int)(Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); rtb.Render(root);
            PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using (FileStream fs = File.Create(file)) enc.Save(fs);
        }
    }

    // A Windows sajat felugro ablakai (naptar/ora, muveletkozpont, halozat, hangero, rejtett ikonok, ertesitesek):
    // lekerekitjuk es kicsit "lebegtetjuk" oket. Az ablak tartalmat a Windows rajzolja, ezert igazi uveg-blur nem lehetseges rajtuk.
    static class FlyoutStyler
    {
        public static string AppDir; public static System.Drawing.Rectangle primaryBounds; static double scale = 1; static bool init; static int tk;
        class St { public string Sig = ""; public int Stable, Applied; }
        static Dictionary<long, St> st = new Dictionary<long, St>(); static Dictionary<uint, string> exes = new Dictionary<uint, string>(); static HashSet<string> logged = new HashSet<string>();
        static readonly string[] Skip = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "CabinetWClass", "SysShadow", "tooltips_class32", "Windows.UI.Core.CoreWindow.Hidden", "#32770", "OperationStatusWindow" };
        static HashSet<long> touched = new HashSet<long>();
        static string HwFile { get { return Path.Combine(AppDir ?? AppDomain.CurrentDomain.BaseDirectory, "flyout-hwnds.txt"); } }
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetWindowAttribute2(IntPtr h, int a, out N.RECT v, int s);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(N.POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
        static bool DiagOn = false;
        static void Log(string m) { try { LogUtil.Cap(Path.Combine(AppDir, "flyout-styler.log")); File.AppendAllText(Path.Combine(AppDir, "flyout-styler.log"), DateTime.Now.ToString("s") + " " + m + Environment.NewLine); } catch { } }
        static DateTime exesAt = DateTime.MinValue;
        static string Exe(uint pid)
        {
            if ((DateTime.Now - exesAt).TotalSeconds > 10) { exes.Clear(); exesAt = DateTime.Now; }          // a PID-ek gyorsan újrahasznosulnak
            string e; if (exes.TryGetValue(pid, out e)) return e;
            string p = N.ExePath(pid); e = p == null ? "" : Path.GetFileName(p).ToLowerInvariant(); exes[pid] = e; return e;
        }
        public static void Tick(Settings s)
        {
            if (s.Get("flyoutstyle", 1) <= 0) { if (st.Count > 0 || touched.Count > 0) { Reset(); st.Clear(); } return; }      // kikapcsolva: a korábban kivágott ablakok eredeti alakja visszaáll
            if ((++tk & 1) != 0) return;
            if (!init) { init = true; using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0; }
            if (DiagOn) { N.POINT cp; N.GetCursorPos(out cp); IntPtr wh = WindowFromPoint(cp); if (wh != IntPtr.Zero) { IntPtr rt = GetAncestor(wh, 2); IntPtr own = GetAncestor(wh, 3); uint pp; N.GetWindowThreadProcessId(own, out pp); string ee = Exe(pp);
                if (ee != "glassdock.exe") { StringBuilder cc = new StringBuilder(80); N.GetClassName(own, cc, 80); StringBuilder c2 = new StringBuilder(80); N.GetClassName(wh, c2, 80); N.RECT rr; N.GetWindowRect(own, out rr);
                    string lg = Loc.T("unter-egér: ") + ee + "|top=" + cc + "|hit=" + c2 + "|vis=" + N.IsWindowVisible(own) + "|" + rr.L + "," + rr.T + "," + (rr.R - rr.L) + "x" + (rr.B - rr.T); if (ee != "brave.exe" && ee != "antigravity.exe" && logged.Count < 400 && logged.Add(lg)) Log(lg); } } }
            int rad = (int)Math.Round(s.Get("flyoutround", 18) * scale), gap = (int)Math.Round(s.Get("flyoutgap", 10) * scale); HashSet<long> now = new HashSet<long>();
            HashSet<long> done = new HashSet<long>();
            Func<IntPtr, bool> proc = delegate(IntPtr h)
            {
              try
              {
                if (!done.Add(h.ToInt64())) return true;
                if (!N.IsWindowVisible(h)) return true;
                N.RECT r; N.GetWindowRect(h, out r); int w = r.R - r.L, hh = r.B - r.T; if (w < 120 || hh < 50) return true;
                Forms.Screen sc = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.L, r.T, w, hh)); System.Drawing.Rectangle sb = sc.Bounds;
                if (w >= sb.Width * 0.9 && hh >= sb.Height * 0.9 && w >= 1000) return true;     // teljes kepernyos ablak (asztal, jatek...)
                bool right = r.R >= sb.Right - 60 && r.R <= sb.Right + 40, bottom = r.B >= sb.Bottom - 160 && r.B <= sb.Bottom + 40;
                int cl; N.DwmGetWindowAttribute(h, 14, out cl, 4);
                uint pid; N.GetWindowThreadProcessId(h, out pid); string exe = Exe(pid);
                if (exe == "glassdock.exe") return true;
                if (DiagOn) { StringBuilder c0 = new StringBuilder(80); N.GetClassName(h, c0, 80); string l0 = "kandidat " + exe + "|" + c0 + "|cloaked=" + cl + "|" + r.L + "," + r.T + "," + w + "x" + hh; if (w <= 700 && logged.Count < 300 && logged.Add(l0)) Log(l0); }
                if (cl != 0) return true;
                if (exe != "explorer.exe" && exe != "shellexperiencehost.exe" && exe != "searchhost.exe" && exe != "searchapp.exe") return true;
                StringBuilder c = new StringBuilder(80); N.GetClassName(h, c, 80); string cn = c.ToString(); foreach (string k in Skip) if (cn == k) return true;
                long id = h.ToInt64(); now.Add(id); St t; if (!st.TryGetValue(id, out t)) { t = new St(); st[id] = t; }
                string lg = exe + "|" + cn + "|" + r.L + "," + r.T + "," + w + "x" + hh; if (logged.Count < 200 && logged.Add(lg)) Log("lathato: " + lg);
                // az ablakot NEM mozgatjuk (a Windows maga animalja, harcolnánk vele): a kivagas hagy "levegot" a jobb/also szelen, igy lebegonek tunik
                string sz = w + "x" + hh; if (sz == t.Sig) return true; t.Sig = sz;
                int ins = Math.Max(1, (int)Math.Round(scale)); bool tall = hh > sb.Height * 0.9; int gr = right ? gap : 0, gb = bottom && !tall ? gap : 0;
                N.SetWindowRgn(h, N.CreateRoundRectRgn(ins, ins, w - gr + 1, hh - gb + 1, rad * 2, rad * 2), true); t.Applied++; if (touched.Add(id)) { try { File.AppendAllText(HwFile, id + Environment.NewLine); } catch { } }       // ins: a Windows sajat 1 px-es szogletes keretet kivagjuk
                int ncp = 1, hr1 = N.DwmSetWindowAttribute(h, 2, ref ncp, 4);        // DWMWA_NCRENDERING_POLICY = letiltva: a Windows sajat kerete ne rajzolodjon
                if (DiagOn) { N.RECT fb; DwmGetWindowAttribute2(h, 9, out fb, 16); string extra = "keret: rect=" + r.L + "," + r.T + "," + r.R + "," + r.B + " frame=" + fb.L + "," + fb.T + "," + fb.R + "," + fb.B + " exstyle=0x" + N.GetWindowLong(h, -20).ToString("X") + " style=0x" + N.GetWindowLong(h, -16).ToString("X");
                  uint pid2 = pid; N.EnumWindows(delegate(IntPtr h2, IntPtr l2) { uint p3; N.GetWindowThreadProcessId(h2, out p3); if (p3 != pid2 || h2 == h) return true; StringBuilder c3 = new StringBuilder(80); N.GetClassName(h2, c3, 80); N.RECT r3; N.GetWindowRect(h2, out r3); extra += " | " + c3 + " vis=" + N.IsWindowVisible(h2) + " " + r3.L + "," + r3.T + "," + r3.R + "," + r3.B; return true; }, IntPtr.Zero);
                  Log(extra); }
                Log("stilus: " + exe + "|" + cn + " " + r.L + "," + r.T + "," + sz + " gap=" + gr + "/" + gb); return true;
              }
              catch (Exception ex) { string m = Loc.T("hiba: ") + ex.Message; if (logged.Add(m)) Log(m + " " + ex.StackTrace); return true; }
            };
            N.EnumWindows(delegate(IntPtr h, IntPtr l) { return proc(h); }, IntPtr.Zero);
            // a felugro ablakokat az EnumWindows nem mindig adja vissza: a jobb alsó sarok néhány pontjából is megkeressük őket
            foreach (System.Drawing.Rectangle sbn in new System.Drawing.Rectangle[] { primaryBounds })
            {
                int[][] pts = { new[] { sbn.Right - 20, sbn.Bottom - 70 }, new[] { sbn.Right - 20, sbn.Bottom - 300 }, new[] { sbn.Right - 200, sbn.Bottom - 80 }, new[] { sbn.Right - 20, sbn.Bottom - 600 } };
                foreach (int[] pt in pts)
                {
                    N.POINT pp0; pp0.X = pt[0]; pp0.Y = pt[1]; IntPtr hw0 = WindowFromPoint(pp0); if (hw0 == IntPtr.Zero) continue; IntPtr top = GetAncestor(hw0, 2); if (top != IntPtr.Zero) proc(top); IntPtr ow = GetAncestor(hw0, 3); if (ow != IntPtr.Zero && ow != top) proc(ow);
                }
            }
            List<long> gone = new List<long>(); foreach (long k in st.Keys) if (!now.Contains(k)) gone.Add(k); foreach (long k in gone) st.Remove(k);   // bezarodott: legkozelebb ujra alkalmazzuk
        }
        public static void Reset()
        {
            HashSet<long> ids = new HashSet<long>(touched); foreach (long k in new List<long>(st.Keys)) ids.Add(k);
            try { if (File.Exists(HwFile)) { foreach (string l in File.ReadAllLines(HwFile)) { long v; if (long.TryParse(l, out v)) ids.Add(v); } File.Delete(HwFile); } } catch { }
            foreach (long k in ids) { try { N.SetWindowRgn(new IntPtr(k), IntPtr.Zero, true); int ncp0 = 0; N.DwmSetWindowAttribute(new IntPtr(k), 2, ref ncp0, 4); } catch { } }
            touched.Clear();
        }
    }

    static class Program
    {
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
        static void SelfTrim() { try { GC.Collect(2, GCCollectionMode.Forced, true); using (System.Diagnostics.Process me = System.Diagnostics.Process.GetCurrentProcess()) EmptyWorkingSet(me.Handle); } catch { } }
        [STAThread]
        static void Main(string[] args)
        {
            try { System.Runtime.ProfileOptimization.SetProfileRoot(AppDomain.CurrentDomain.BaseDirectory); System.Runtime.ProfileOptimization.StartProfile("dock.jit"); } catch { }      // többmagos JIT: a következő indítás gyorsabb
            // szoftveres WPF-rajzolás: ~50 MB-tal kevesebb privát memória (a GPU-illesztő foglalása elmarad); GLASSDOCK_HW=1 vagy hwrender.txt: hardveres
            if (Environment.GetEnvironmentVariable("GLASSDOCK_HW") != "1" && !File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "hwrender.txt"))) RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            if (args.Length > 0 && args[0] == "/restore") { StartStyler.Reset(); FlyoutStyler.Reset(); N.TaskbarSlide(1); N.HideTaskButtons(false); N.TaskbarPillsOff(); return; }
            N.NoExclude = Array.IndexOf(args, "/noexcl") >= 0;
            if (Array.IndexOf(args, "/dumpfrost") >= 0) Frost.DumpDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (args.Length > 0 && (args[0] == "/tiles" || args[0] == "/untile"))
            {
                string d0 = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                DeskTiles.Init(args.Length > 2 ? args[2] : d0);
                int n = args[0] == "/untile" ? DeskTiles.Restore() : DeskTiles.RunAll(args.Length > 1 ? new string[] { args[1] } : null);
                File.WriteAllText(Path.Combine(d0, "tiles-result.txt"), args[0] + ": " + n + " db" + Environment.NewLine); return;
            }
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "disabled.txt"))) return;      // "Vissza az eredetihez": kikapcsolva, az automatikus indítás sem hozza vissza
            bool created; Mutex mx = new Mutex(true, "GlassDock-ADVANCED_USE", out created); if (!created) return;
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Settings s = new Settings(Path.Combine(dir, "settings.ini")); Theme.S = s; Theme.PinFile = Path.Combine(dir, "pin.txt"); Model m = new Model(s, dir);
            Application app = new Application(); GlassTerm.GlassMenu.ApplyTheme(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string lastErr = ""; DateTime lastErrAt = DateTime.MinValue;
            app.DispatcherUnhandledException += delegate(object o, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; try { string ek = e.Exception.GetType().Name + e.Exception.Message; if (ek == lastErr && (DateTime.Now - lastErrAt).TotalSeconds < 60) return; lastErr = ek; lastErrAt = DateTime.Now; string ef = Path.Combine(dir, "error.log"); LogUtil.Cap(ef); File.AppendAllText(ef, DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine); } catch { } };
            Action<Action> safe = delegate(Action a0) { try { a0(); } catch { } };
            Action restoreAll = delegate { safe(N.TaskbarPillsOff); safe(delegate { N.TaskbarSlide(1); }); safe(delegate { N.HideTaskButtons(false); }); safe(KeyHook.Remove); safe(StartStyler.Reset); safe(FlyoutStyler.Reset); };
            int restoredI = 0; Action restoreOnce = delegate { if (Interlocked.Exchange(ref restoredI, 1) != 0) return; restoreAll(); };
            app.Exit += delegate { restoreOnce(); };
            AppDomain.CurrentDomain.ProcessExit += delegate { restoreOnce(); };
            AppDomain.CurrentDomain.UnhandledException += delegate { restoreOnce(); };
            List<Dock> docks = new List<Dock>(); m.Refresh();
            foreach (Forms.Screen sc in Forms.Screen.AllScreens) docks.Add(new Dock(sc, m, s));
            foreach (Dock d in docks) d.ShowFirst();
            bool gstart = s.Get("glassstart", 1) > 0; N.CustomStart = gstart;
            Pill pl = new Pill(s, gstart), pr = new Pill(s, false);
            DispatcherTimer t = new DispatcherTimer(); t.Interval = TimeSpan.FromMilliseconds(700);
            t.Tick += delegate { if (m.Refresh()) foreach (Dock d in docks) d.Refresh(); if (s.Get("hidetb", 1) > 0) N.HideTaskButtons(true); if (s.Get("pills", 1) > 0 && s.Get("fullwork", 0) > 0) N.FullWorkArea(); };
            t.Start();
            DispatcherTimer t2 = new DispatcherTimer(DispatcherPriority.Render); t2.Interval = TimeSpan.FromMilliseconds(8);
            Dock primary = docks.Find(delegate(Dock d) { return d.Scr.Primary; }) ?? docks[0];
            if (gstart)
            {
                Action toggle = delegate { StartMenu.Toggle(s, primary.Scr); };
                pl.OnClick = toggle; KeyHook.OnWin = toggle; Model.StartAction = toggle;
                if (s.Get("spotlight", 1) > 0) KeyHook.OnSpot = delegate { Spotlight.Toggle(s); };
                KeyHook.Install(); StartMenu.WatchApps();
                DispatcherTimer pre = new DispatcherTimer(); pre.Interval = TimeSpan.FromSeconds(6); pre.Tick += delegate { pre.Stop(); StartMenu.Prefetch(); int pbN = 0; DispatcherTimer pb = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(7) }; pb.Tick += delegate { if (!StartMenu.Ready && ++pbN < 8) return; pb.Stop(); StartMenu.PreBuild(s, primary.Scr); }; pb.Start(); }; pre.Start();      // az alkalmazás-lista előtöltése (Spotlight / Start)
                int spi = Array.IndexOf(args, "/spottest");
                if (spi >= 0)
                {
                    string sq = args.Length > spi + 1 ? args[spi + 1] : "term"; int sstep = 0; DispatcherTimer spt = new DispatcherTimer(); spt.Interval = TimeSpan.FromMilliseconds(2500);
                    spt.Tick += delegate { sstep++; if (sstep == 1) Spotlight.Toggle(s); else if (sstep == 2) Spotlight.SetQuery(sq); else if (sstep == 4) { Spotlight.Dump(Path.Combine(dir, "spotlight-test.png")); Spotlight.CloseNow(); spt.Stop(); } };
                    spt.Start();
                }
                if (Array.IndexOf(args, "/menutest") >= 0)
                {
                    DispatcherTimer mt = new DispatcherTimer(); mt.Interval = TimeSpan.FromMilliseconds(2000); int step = 0;
                    mt.Tick += delegate { step++; if (step == 1) toggle(); else if (step == 4) { StartMenu.Dump(Path.Combine(dir, "startmenu-test.png")); StartMenu.CloseNow(); mt.Stop(); } };
                    mt.Start();
                }
            }
            DispatcherTimer trimT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; trimT.Tick += delegate { SelfTrim(); trimT.Interval = TimeSpan.FromMinutes(10); }; trimT.Start();
            DispatcherTimer rhT = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) }; rhT.Tick += delegate { KeyHook.Rehook(); }; rhT.Start();      // indulás után és ritkán később a saját munkakészlet összehúzása (a Start-lista/blur ideiglenes memóriája)
            DateTime lastPoll = DateTime.MinValue; int tickN = 0; bool pillsOn = false, panim = false; int ptgt = 0; double pp = 0, pp0 = 0, pdur = 300; Stopwatch pws = new Stopwatch(); DateTime lastPill = DateTime.MinValue;
            t2.Tick += delegate
            {
                bool busy = panim; foreach (Dock d in docks) if (d.Busy) busy = true;
                int wantMs = busy ? 8 : 50; if ((int)t2.Interval.TotalMilliseconds != wantMs) t2.Interval = TimeSpan.FromMilliseconds(wantMs);      // nyugalmi allapotban ~50 ms, animacio kozben minden kepkockanal
                foreach (Dock d in docks) d.Tick();
                if ((DateTime.Now - lastPoll).TotalMilliseconds > 300) { lastPoll = DateTime.Now; Theme.PollPin(); if (s.Poll()) { foreach (Dock d in docks) d.ThemeChanged(); pl.ThemeChanged(); pr.ThemeChanged(); } }
                StartStyler.Tick(s);
                FlyoutStyler.Tick(s);
                bool on = s.Get("pills", 1) > 0;
                // Start/ora pilulak: ha a dokk ALUL van, a dokkal egyutt jelennek meg/tunnek el; ha mashol van (pl. fent), csak az alsó élre vitt egérre
                N.POINT cp; N.GetCursorPos(out cp); System.Drawing.Rectangle pb = primary.Scr.Bounds;
                bool edgeB = cp.Y >= pb.Bottom - 2 && cp.X >= pb.X && cp.X < pb.Right, inKeep = false;
                foreach (N.RECT kr in Dock.Keep) if (N.In(cp, kr, 6)) inKeep = true;
                if (edgeB || (pp > 0.001 && (inKeep || N.FlyoutOpen()))) lastPill = DateTime.Now;
                bool wantP = on && (Theme.Pinned || StartMenu.IsOpen || (primary.DockEdge == 0 && primary.IsShown) || (DateTime.Now - lastPill).TotalMilliseconds < 700);
                int tg = wantP ? 1 : 0;
                if (tg != ptgt) { ptgt = tg; pp0 = pp; pdur = tg == 1 ? 340 : 230; pws.Restart(); panim = true; }
                if (panim) { double tt = Math.Min(1.0, pws.ElapsedMilliseconds / pdur); pp = pp0 + (ptgt - pp0) * (ptgt == 1 ? Ease.OutBack(tt) : Ease.InCubic(tt)); if (tt >= 1) { panim = false; pp = ptgt; } }
                double pc = Math.Max(0, Math.Min(1, pp)); bool show = on && pc > 0.001;
                N.RECT l = new N.RECT(), r = new N.RECT(); IntPtr tray = IntPtr.Zero; bool ok = false;
                if (show && !pillsOn) { N.RECT sl, sr; if (N.PillRects(out sl, out sr)) { pl.Snap(sl); pr.Snap(sr); } }   // a tálca meg rejtett: tiszta hatter-fotó a pilulaknak
                pillsOn = show;
                if (on) N.TaskbarSlide(pc);
                if (show) ok = N.TaskbarPills((int)Math.Round(Math.Min(20, s.Get("round", 0.6) * 20)), out l, out r, out tray);
                pl.Update(l, tray, ok, pc); pr.Update(r, tray, ok, pc);
                Dock.Keep.Clear(); if (ok) { Dock.Keep.Add(l); Dock.Keep.Add(r); }
            };
            t2.Start();
            N.TaskbarAutoHide(s.Get("tbautohide", 1) > 0);   // a Windows tálca automatikus elrejtesu: nincs lefoglalt sav, a teljes kepernyore lehet maximalizalni
            if (s.Get("hidetb", 1) > 0) N.HideTaskButtons(true);
            if (Array.IndexOf(args, "/pin") >= 0) Preview.Force = true;   // hibakereseshez: a dokk nem rejtozik el
            if (Array.IndexOf(args, "/previewtest") >= 0)
            {
                DispatcherTimer tt = new DispatcherTimer(); tt.Interval = TimeSpan.FromMilliseconds(1500);
                tt.Tick += delegate
                {
                    tt.Stop(); Item best = null; foreach (Item i in m.Pinned) if (i.Wins.Count >= 2) { best = i; break; }
                    if (best == null) foreach (Item i in m.Run) if (i.Wins.Count >= 2) { best = i; break; }
                    if (best != null && docks.Count > 0) { N.RECT r = docks[0].ScreenRect; r.R = r.L + 120; Preview.Test(best, r, 1.0); }
                };
                tt.Start();
            }
            DeskTiles.Init(dir); StartStyler.AppDir = dir; FlyoutStyler.AppDir = dir; FlyoutStyler.primaryBounds = Forms.Screen.PrimaryScreen.Bounds; if (s.Get("desktoptiles", 1) > 0) DeskTiles.Start();
            app.Run(); GC.KeepAlive(mx);
        }
    }
}
