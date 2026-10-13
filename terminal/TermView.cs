// Lumen Terminal – a kirajzoló és bemeneti vezérlő (egy panel). C# 5, WPF.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GlassTerm
{
    public enum PasteMode { Normal, Plain, OneLine, Quoted }

    // egy parancs-blokk: prompt + parancs + kimenet (a prompt mintázatából ismeri fel a terminál; a kilépési kód OSC 133;D-ből jön)
    public class PromptMark
    {
        public long Id, OutId = -1, EndId = -1; public int CmdCol; public string Cmd = ""; public DateTime T0, T1; public int Exit = int.MinValue; public bool Started, Done;
        public double Seconds { get { return Done ? (T1 - T0).TotalSeconds : (Started ? (DateTime.Now - T0).TotalSeconds : 0); } }
    }

    public class LinkInfo { public string Kind, Text, Path; public int Line, Col; public int Row, C0, C1; public long AbsLine; }

    public class TermView : FrameworkElement
    {
        public VtScreen Scr; ConPty pty; DispatcherTimer timer, blinkTimer, bellTimer;
        GlyphTypeface gtReg, gtBold, gtObl; List<GlyphTypeface> fallbacks = new List<GlyphTypeface>();
        public double FontSizePx = 15, LineFactor = 1.2, CellW, CellH, BaseY; public Thickness Pad = new Thickness(10, 6, 10, 6);
        public int Cols = 100, Rows = 30; public Color DefFg, DefBg; public TermTheme Theme; public TermSettings St = TermSettings.I;
        public Color[] Palette = new Color[256]; Dictionary<int, SolidColorBrush> brushes = new Dictionary<int, SolidColorBrush>();
        public string CmdLine, WorkDir, FontDir; public ShellProfile Profile; public string RawLog;
        public event Action Exited, TitleChanged, GridChanged, BellRung, OutputWhileInactive, CwdChanged, FindChanged, Focused, SelectionChanged; public event Action<PromptMark> CommandDone; public event Action<LinkInfo> LinkActivated; public event Action<Point> ContextRequested; public event Action<string> UserInput, Copied, Pasted, Notified, DevUrlFound, PreviewRequested; public string LastDevUrl = "";
        public Action<string, string, Action> AskConfirm;      // (cím, szöveg, igen-művelet): a gazda mutatja a jóváhagyó ablakot
        bool hasSel, selecting, selRect; int sL, sC, eL, eC; DateTime lastClick = DateTime.MinValue; int clickCount; Point lastClickPt;
        DateTime deadSince = DateTime.MinValue, lastData = DateTime.MinValue, lastActivity = DateTime.MinValue; long trimSeen; bool altSeen, exitedRaised, blinkOn = true, focusedLast; double bellAlpha;
        public bool Dim, Broadcasting; Regex promptRx; long scanned = -1; public List<PromptMark> Marks = new List<PromptMark>();
        FileStream logStream; public bool Logging { get { return logStream != null; } }
        // keresés
        string findQ = ""; bool findCase, findRegex; List<int[]> matches = new List<int[]>(); int curMatch = -1;     // [abs sor, c0, c1]
        // link
        LinkInfo hover;

        public TermView(string fontDir)
        {
            FontDir = fontDir; Focusable = true; FocusVisualStyle = null; ClipToBounds = true; Cursor = Cursors.IBeam; AllowDrop = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale); TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Scr = new VtScreen(Cols, Rows); Scr.Reply = delegate (string s) { if (pty != null) pty.Write(s); };
            Scr.TitleChanged += delegate { if (TitleChanged != null) TitleChanged(); }; Scr.CwdChanged += delegate { if (CwdChanged != null) CwdChanged(); };
            Scr.Bell += OnBell; Scr.Notify += delegate (string m) { if (Notified != null) Notified(m); }; Scr.PreviewRequest += delegate (string u) { if (PreviewRequested != null) PreviewRequested(u); };
            Scr.ClipboardSet += delegate (string t) { try { Clipboard.SetText(t); if (Copied != null) Copied(t); } catch { } };
            ApplySettings();
            timer = new DispatcherTimer(DispatcherPriority.Normal); timer.Interval = TimeSpan.FromMilliseconds(12); timer.Tick += Tick; timer.Start();
            blinkTimer = new DispatcherTimer(); blinkTimer.Interval = TimeSpan.FromMilliseconds(530); blinkTimer.Tick += delegate { if (BlinkWanted) { blinkOn = !blinkOn; InvalidateVisual(); } else if (!blinkOn) { blinkOn = true; InvalidateVisual(); } }; blinkTimer.Start();
            bellTimer = new DispatcherTimer(); bellTimer.Interval = TimeSpan.FromMilliseconds(30); bellTimer.Tick += delegate { bellAlpha -= 0.12; if (bellAlpha <= 0) { bellAlpha = 0; bellTimer.Stop(); } InvalidateVisual(); };
            GotKeyboardFocus += delegate { blinkOn = true; if (Scr.FocusEvents) Send("\x1b[I"); if (Focused != null) Focused(); InvalidateVisual(); };
            LostKeyboardFocus += delegate { if (Scr.FocusEvents) Send("\x1b[O"); InvalidateVisual(); };
            Drop += delegate (object o, DragEventArgs e)
            {
                try { string[] f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) { StringBuilder sb = new StringBuilder(); foreach (string x in f) { if (sb.Length > 0) sb.Append(' '); sb.Append(QuotePath(x)); } Send(sb.ToString()); Focus(); } else if (e.Data.GetDataPresent(DataFormats.UnicodeText)) { PasteText((string)e.Data.GetData(DataFormats.UnicodeText)); } } catch { }
                e.Handled = true;
            };
            DragOver += delegate (object o, DragEventArgs e) { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        }

        public static string QuotePath(string p) { if (p.IndexOfAny(new char[] { '$', '`' }) >= 0) return "'" + p.Replace("'", "''") + "'"; return p.IndexOfAny(new char[] { ' ', '&', '(', ')', '[', ']', '{', '}', '^', '=', ';', '!', '\'', '+', ',', '~', '%', '#', '@', '|', '<', '>' }) >= 0 ? "\"" + p + "\"" : p; }

        // ------------------------------------------------------------------ téma, betű, beállítások
        public void ApplyTheme(TermTheme t)
        {
            Theme = t; DefFg = t.Fg; DefBg = t.Bg; for (int i = 0; i < 16; i++) Palette[i] = t.P[i];
            int[] lv = { 0, 95, 135, 175, 215, 255 }; for (int i = 0; i < 216; i++) Palette[16 + i] = Color.FromRgb((byte)lv[i / 36], (byte)lv[(i / 6) % 6], (byte)lv[i % 6]);
            for (int i = 0; i < 24; i++) { byte v = (byte)(8 + i * 10); Palette[232 + i] = Color.FromRgb(v, v, v); }
            brushes.Clear(); InvalidateVisual();
        }

        string fontLoaded = "";
        void LoadFonts(string family)
        {
            if (fontLoaded == family && gtReg != null) return; GlyphTypeface r = null, b = null, o = null;
            try
            {
                if (string.Equals(family, "DejaVu Sans Mono", StringComparison.OrdinalIgnoreCase) && Directory.Exists(FontDir))
                { r = new GlyphTypeface(new Uri(Path.Combine(FontDir, "DejaVuSansMono.ttf"))); b = new GlyphTypeface(new Uri(Path.Combine(FontDir, "DejaVuSansMono-Bold.ttf"))); o = new GlyphTypeface(new Uri(Path.Combine(FontDir, "DejaVuSansMono-Oblique.ttf"))); }
            }
            catch { r = null; }
            if (r == null)
            {
                FontFamily ff = new FontFamily(family); GlyphTypeface g;
                if (new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out g)) r = g;
                if (new Typeface(ff, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal).TryGetGlyphTypeface(out g)) b = g;
                if (new Typeface(ff, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out g)) o = g;
            }
            if (r == null) { GlyphTypeface g; if (new Typeface("Consolas").TryGetGlyphTypeface(out g)) r = g; }
            if (r == null) return;
            gtReg = r; gtBold = b ?? r; gtObl = o ?? r; fontLoaded = family; fallbacks.Clear();
            foreach (string fam in new string[] { "Consolas", "Segoe UI Symbol", "MS Gothic", "Segoe UI Emoji", "Segoe UI" }) { GlyphTypeface g; if (new Typeface(fam).TryGetGlyphTypeface(out g)) fallbacks.Add(g); }
        }

        public void ApplySettings()
        {
            ApplyTheme(St.CurTheme); LoadFonts(St.Font); FontSizePx = Math.Max(8, Math.Min(48, St.FontSize)); LineFactor = Math.Max(1.0, Math.Min(2.5, St.LineFactor)); Pad = new Thickness(St.PadX, St.PadY, St.PadX, St.PadY); Recalc(); Relayout(); Scr.SbMax = Math.Max(500, St.Scrollback); InvalidateVisual();
        }

        public void SetFontSize(double px) { FontSizePx = Math.Max(8, Math.Min(48, px)); St.FontSize = FontSizePx; Recalc(); Relayout(); InvalidateVisual(); }
        void Recalc()
        {
            if (gtReg == null) return; ushort gi; double adv = 0.602; if (gtReg.CharacterToGlyphMap.TryGetValue('M', out gi)) adv = gtReg.AdvanceWidths[gi];
            CellW = Math.Round(adv * FontSizePx * 4) / 4.0; CellH = Math.Round(FontSizePx * LineFactor);
            double asc = gtReg.Baseline * FontSizePx, desc = (gtReg.Height - gtReg.Baseline) * FontSizePx; BaseY = Math.Round((CellH - (asc + desc)) / 2 + asc);
        }

        // ------------------------------------------------------------------ shell, I/O
        public bool StartShell(ShellProfile prof, string cwd)
        {
            Profile = prof; promptRx = null; if (prof != null && !string.IsNullOrEmpty(prof.PromptRegex)) { try { promptRx = new Regex(prof.PromptRegex); } catch { } }
            return StartShell(prof != null ? prof.CmdLine : Cfg.Get("shell", "cmd.exe"), cwd);
        }
        public bool StartShell(string cmdLine, string cwd)
        {
            if (!string.IsNullOrEmpty(cwd) && !Directory.Exists(cwd)) cwd = null; try { byte[] rs = new byte[] { 0x1b, (byte)99 }; Scr.Feed(rs, 2); } catch { } CmdLine = cmdLine; WorkDir = cwd; exitedRaised = false; deadSince = DateTime.MinValue; if (pty != null) pty.Dispose(); pty = new ConPty();
            bool ok = pty.Start(cmdLine, cwd, Cols, Rows);
            if (!ok) { byte[] eb = Encoding.UTF8.GetBytes(Loc.T("Nem sikerült elindítani a shellt: ") + pty.Error + "\r\n"); Scr.Feed(eb, eb.Length); }
            return ok;
        }
        public void Send(string s) { if (pty != null) pty.Write(s); }
        void UserSend(string s) { Send(s); if (UserInput != null) UserInput(s); }
        // parancs beírása (előbb a sor törlése); futtatáskor a prompt-blokk is létrejön, mintha begépelték volna
        public void RunCommand(string cmd, bool run)
        {
            // az Esc külön érkezik (különben Alt+betűként értelmeződne), utána a szöveg, végül az Enter
            UserSend(Profile != null && (Profile.Kind == "ps" || Profile.Kind == "cmd") ? "\x1b" : "\x15");
            DispatcherTimer d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            d.Tick += delegate
            {
                d.Stop(); UserSend(cmd); if (!run) return;
                DispatcherTimer e = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) }; e.Tick += delegate { e.Stop(); CommandEntered(); UserSend("\r"); }; e.Start();
            };
            d.Start();
        }
        public bool Alive { get { return pty != null && pty.Alive; } }
        public string Cwd { get { return !string.IsNullOrEmpty(Scr.Cwd) ? Scr.Cwd : WorkDir; } }
        public string Title { get { return Scr.Title; } }
        public void Shutdown() { try { if (fsw != null) { fsw.Dispose(); fsw = null; } if (watchT != null) { watchT.Stop(); watchT = null; } } catch { } timer.Stop(); blinkTimer.Stop(); bellTimer.Stop(); StopLog(); if (pty != null) pty.Dispose(); pty = null; }

        public void StartLog(string path) { StopLog(); try { logStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read); } catch { logStream = null; } }
        public void StopLog() { if (logStream != null) { try { logStream.Dispose(); } catch { } logStream = null; } }

        void Tick(object s, EventArgs e)
        {
            if (pty == null) return;
            List<byte[]> l = pty.Drain(262144);
            if (l != null)
            {
                foreach (byte[] b in l)
                {
                    if (RawLog != null) { try { using (FileStream fs = new FileStream(RawLog, FileMode.Append)) fs.Write(b, 0, b.Length); } catch { } }
                    if (logStream != null) { try { logStream.Write(b, 0, b.Length); } catch { } }
                    Scr.Feed(b, b.Length);
                }
                lastData = DateTime.Now; if (St.ScrollOnOutput && Scr.ViewOffset > 0) Scr.ViewOffset = 0;
                if (!IsKeyboardFocusWithin && (DateTime.Now - lastActivity).TotalMilliseconds > 700) { lastActivity = DateTime.Now; if (OutputWhileInactive != null) OutputWhileInactive(); }
            }
            if (Scr.Dirty) { Scr.Dirty = false; InvalidateVisual(); }
            if (Scr.Trimmed != trimSeen)
            {
                int d = (int)(Scr.Trimmed - trimSeen); trimSeen = Scr.Trimmed;
                if (hasSel) { sL -= d; eL -= d; if (sL < 0 && eL < 0) hasSel = false; sL = Math.Max(0, sL); eL = Math.Max(0, eL); }
                for (int i = 0; i < matches.Count; i++) matches[i][0] -= d; int mBefore = matches.Count; matches.RemoveAll(delegate (int[] m) { return m[0] < 0; }); if (curMatch >= 0) curMatch = Math.Min(matches.Count - 1, Math.Max(matches.Count > 0 ? 0 : -1, curMatch - (mBefore - matches.Count)));
                long trm = Scr.Trimmed; foreach (PromptMark pm0 in Marks) if (pm0.Started && !pm0.Done && pm0.Id < trm) { pm0.Id = trm; if (pm0.OutId >= 0 && pm0.OutId < trm) pm0.OutId = trm; }
                Marks.RemoveAll(delegate (PromptMark m) { return m.Id < trm; });
            }
            if (remapAt != DateTime.MinValue && DateTime.Now >= remapAt) { remapAt = DateTime.MinValue; RemapMarks(); lastData = DateTime.Now; }
            if (Scr.Alt != altSeen) { altSeen = Scr.Alt; hasSel = false; }
            if (l == null && lastData != DateTime.MinValue && (DateTime.Now - lastData).TotalMilliseconds > 110 && !Scr.Alt && remapAt == DateTime.MinValue) { if (promptRx != null) ScanPrompts(); ScanDevUrls(); lastData = DateTime.MinValue; }
            if (!exitedRaised && pty.Started && l == null && !pty.Alive)
            {
                if (deadSince == DateTime.MinValue) deadSince = DateTime.Now;
                else if ((DateTime.Now - deadSince).TotalMilliseconds > 300) { exitedRaised = true; if (Exited != null) Exited(); }      // addig várunk, hogy az utolsó kimenet is beérkezzen
            }
            else if (l != null) deadSince = DateTime.MinValue;
        }

        // ------------------------------------------------------------------ prompt-blokkok
        DateTime remapAt = DateTime.MinValue;
        // átméretezés után a sorok máshová kerülnek: a blokkok helyét a prompt-sorok alapján újraszámoljuk (a végéről párosítva)
        void RemapMarks()
        {
            if (promptRx == null || Marks.Count == 0 || Scr.Alt) return; List<long> ids = new List<long>(); long last = Scr.LineId(Scr.Rows - 1);
            for (long id = Scr.Trimmed; id <= last; id++) { string t = Scr.TextAtId(id); if (t != null) { Match m = promptRx.Match(t); if (m.Success && m.Index == 0) ids.Add(id); } }
            int k = Math.Min(ids.Count, Marks.Count); if (k == 0) return; int off = Marks.Count - k; if (off > 0) Marks.RemoveRange(0, off);
            int idOff = ids.Count - k;
            for (int i = 0; i < Marks.Count; i++)
            {
                PromptMark m = Marks[i]; m.Id = ids[idOff + i]; if (!m.Started) { m.OutId = -1; m.EndId = -1; continue; }
                int cmdLen = m.CmdCol + m.Cmd.Length; int lines = Math.Max(1, (cmdLen + Math.Max(1, Cols) - 1) / Math.Max(1, Cols)); m.OutId = m.Id + lines;
                m.EndId = i + 1 < Marks.Count && m.Done ? ids[idOff + i + 1] : -1;
            }
            scanned = Math.Max(Scr.Trimmed, Scr.LineId(Scr.Y) - 1); InvalidateVisual();
        }
        // futó parancs kimenetében localhost-os cím (Vite, Next, Express…) → a gazda felajánlja az előnézetet
        static readonly Regex DevUrlRx = new Regex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\]):\d{2,5}(?:/[^\s'""<>)\]]*)?", RegexOptions.IgnoreCase);
        HashSet<string> announced = new HashSet<string>();
        void ScanDevUrls()
        {
            if (DevUrlFound == null) return; PromptMark pm = Marks.Count > 0 ? Marks[Marks.Count - 1] : null; int first = 0;
            if (pm != null) { if (!pm.Started || pm.Done) return; first = Math.Max(0, AbsOf(pm.OutId) - Scr.Sb.Count); }     // csak a most futó parancs kimenetét nézzük
            for (int y = first; y < Scr.Rows; y++)
            {
                string t = VtScreen.LineText(Scr.Grid[y], 0, Scr.Cols); if (t.IndexOf("://", StringComparison.Ordinal) < 0) continue; Match m = DevUrlRx.Match(t); if (!m.Success) continue;
                string u = m.Value.TrimEnd('.', ',', ';').Replace("0.0.0.0", "localhost").Replace("127.0.0.1", "localhost").Replace("[::1]", "localhost"); if (announced.Count > 200) announced.Clear(); if (!announced.Add(u)) continue; LastDevUrl = u; DevUrlFound(u); return;
            }
        }
        void ScanPrompts()
        {
            long first = Math.Max(scanned, Scr.Trimmed), last = Scr.LineId(Scr.Y); bool any = false;
            for (long id = first; id <= last; id++)
            {
                string t = Scr.TextAtId(id); if (t == null) continue; Match m = promptRx.Match(t); if (!m.Success || m.Index != 0) continue;
                PromptMark pm = Marks.Count > 0 ? Marks[Marks.Count - 1] : null;
                if (pm != null && id <= pm.Id) { if (pm.Id == id) pm.CmdCol = m.Length; continue; }
                if (pm != null && !pm.Done)      // az előző parancs véget ért: új prompt jelent meg
                {
                    pm.Done = true; pm.T1 = DateTime.Now; pm.EndId = id; if (pm.Started) { pm.Exit = (Scr.AppExit != int.MinValue && Scr.AppExitAt >= pm.T0) ? Scr.AppExit : int.MinValue; any = true; if (CommandDone != null) CommandDone(pm); }
                }
                Marks.Add(new PromptMark { Id = id, CmdCol = m.Length }); if (Marks.Count > 3000) Marks.RemoveAt(0);
            }
            scanned = Math.Max(first, last - 1); if (any) InvalidateVisual();
        }

        // Enter lenyomásakor: a parancs szövege a prompt végétől a kurzorsorig
        void CommandEntered()
        {
            if (promptRx == null || Scr.Alt || Marks.Count == 0) return; PromptMark pm = Marks[Marks.Count - 1]; if (pm.Started || pm.Done) return;
            long cur = Scr.LineId(Scr.Y); StringBuilder sb = new StringBuilder();
            for (long id = pm.Id; id <= cur; id++) { string t = Scr.TextAtId(id); if (t == null) continue; if (id == pm.Id) t = t.Length > pm.CmdCol ? t.Substring(pm.CmdCol) : ""; sb.Append(t); if (id < cur) sb.Append(' '); }
            string cmd = sb.ToString().Trim(); if (cmd.Length == 0) return;
            announced.Clear(); pm.Cmd = cmd; pm.Started = true; pm.T0 = DateTime.Now; pm.OutId = cur + 1; Scr.AppExit = int.MinValue;
        }

        public PromptMark LastMark { get { for (int i = Marks.Count - 1; i >= 0; i--) if (Marks[i].Started) return Marks[i]; return null; } }
        public bool HasMarks { get { return Marks.Count > 1; } }
        int AbsOf(long id) { return (int)(id - Scr.Trimmed); }

        public void JumpPrompt(int dir)
        {
            if (Marks.Count == 0) return; int topAbs = Scr.Sb.Count - Scr.ViewOffset; PromptMark target = null;
            if (dir < 0) { for (int i = Marks.Count - 1; i >= 0; i--) if (AbsOf(Marks[i].Id) < topAbs) { target = Marks[i]; break; } }
            else { for (int i = 0; i < Marks.Count; i++) if (AbsOf(Marks[i].Id) > topAbs) { target = Marks[i]; break; } }
            if (target == null) { if (dir > 0) { Scr.ViewOffset = 0; InvalidateVisual(); } return; }
            ScrollToAbs(AbsOf(target.Id));
        }
        void ScrollToAbs(int abs) { Scr.ViewOffset = Math.Max(0, Math.Min(Scr.Sb.Count, Scr.Sb.Count - abs)); InvalidateVisual(); }

        string OutputOf(PromptMark m, out int a, out int z)
        {
            a = AbsOf(m.OutId); int end = m.EndId >= 0 ? AbsOf(m.EndId) - 1 : Scr.Sb.Count + Scr.Y - 1; z = end;
            while (z >= a) { Cell[] r = Scr.AbsLine(z); if (r != null && VtScreen.LineText(r, 0, Cols - 1).Length > 0) break; z--; }
            if (z < a) return "";
            StringBuilder sb = new StringBuilder(); for (int i = a; i <= z; i++) { sb.Append(VtScreen.LineText(Scr.AbsLine(i), 0, int.MaxValue)); if (i < z) sb.Append("\r\n"); }
            return sb.ToString();
        }
        public string LastOutputText() { PromptMark m = LastMark; if (m == null || m.OutId < 0) return ""; int a, z; return OutputOf(m, out a, out z); }
        public void SelectLastOutput() { PromptMark m = LastMark; if (m == null || m.OutId < 0) return; int a, z; if (OutputOf(m, out a, out z).Length == 0) return; sL = a; sC = 0; eL = z; eC = Cols - 1; selRect = false; hasSel = true; InvalidateVisual(); }
        public void CopyLastOutput() { string t = LastOutputText(); if (t.Length > 0) { SetClip(t); } }
        public void CopyLastCommand() { PromptMark m = LastMark; if (m != null) SetClip(m.Cmd); }
        public void RerunLast() { PromptMark m = LastMark; if (m != null) RunCommand(m.Cmd, true); }
        // ---- run book: az utolsó n parancs-blokk önálló HTML-be (parancs, állapot, idő, kimenet)
        public string BlocksHtml(int n)
        {
            List<PromptMark> l = new List<PromptMark>(); for (int i = Marks.Count - 1; i >= 0 && l.Count < n; i--) if (Marks[i].Started) l.Insert(0, Marks[i]);
            StringBuilder sb = new StringBuilder("<!doctype html><meta charset=utf-8><title>Run book</title><body style=\"margin:0;background:#161618;color:#e6e6ea;font:14px Segoe UI,sans-serif;padding:24px\"><h2>Run book · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "</h2>");
            foreach (PromptMark m in l)
            {
                int a, z; string o = m.OutId >= 0 ? OutputOf(m, out a, out z) : ""; bool ok = m.Exit == 0 || m.Exit == int.MinValue;
                sb.Append("<div style=\"background:#202024;border-radius:12px;margin:14px 0;padding:12px 16px;border-left:4px solid " + (ok ? "#30d158" : "#ff453a") + "\"><div style=\"font:600 14px Consolas,monospace\">$ " + System.Net.WebUtility.HtmlEncode(m.Cmd) + "</div><div style=\"color:#9a9aa2;font-size:12px;margin:4px 0 8px\">" + (ok ? "✓" : Loc.T("✗ kilépési kód ") + m.Exit) + " · " + FmtDur(m.Seconds) + "</div><pre style=\"margin:0;white-space:pre-wrap;font:12.5px Consolas,monospace\">" + System.Net.WebUtility.HtmlEncode(o.Length > 40000 ? o.Substring(0, 40000) + "\n…" : o) + "</pre></div>");
            }
            return sb.ToString();
        }
        // ---- figyelő-blokk: az utolsó parancs újrafut, ha a munkamappában fájl változik
        FileSystemWatcher fsw; DispatcherTimer watchT; public bool Watching { get { return fsw != null; } }
        public bool ToggleWatch()
        {
            if (fsw != null) { try { fsw.EnableRaisingEvents = false; fsw.Dispose(); } catch { } fsw = null; if (watchT != null) { watchT.Stop(); watchT = null; } return false; }
            string cmd = LastCommand, cwd = Cwd; if (string.IsNullOrEmpty(cmd) || string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) return false;
            fsw = new FileSystemWatcher(cwd) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            watchT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) }; watchT.Tick += delegate (object s0, EventArgs e0) { ((DispatcherTimer)s0).Stop(); PromptMark lm = LastMark; if (fsw != null && lm != null && lm.Done) RunCommand(cmd, true); };
            FileSystemEventHandler h = delegate (object o, FileSystemEventArgs e) { string p = e.FullPath; if (p.IndexOf(Path.DirectorySeparatorChar + ".", StringComparison.Ordinal) >= 0 || p.IndexOf(@"\AppData\", StringComparison.OrdinalIgnoreCase) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(p, @"\\(build|out|target|__pycache__|coverage)\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase) || p.IndexOf("node_modules", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(Path.DirectorySeparatorChar + ".git", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(Path.DirectorySeparatorChar + "dist" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) return; Dispatcher.BeginInvoke(new Action(delegate { if (fsw == null || watchT == null) return; PromptMark lm = LastMark; if (lm == null || !lm.Done || (DateTime.Now - lm.T1).TotalMilliseconds < 1500) return; watchT.Stop(); watchT.Start(); })); };
            fsw.Changed += h; fsw.Created += h; fsw.Renamed += delegate (object o, RenamedEventArgs e) { h(o, e); }; fsw.EnableRaisingEvents = true; return true;
        }
        public string LastCommand { get { PromptMark m = LastMark; return m != null ? m.Cmd : ""; } }
        void SetClip(string t) { try { Clipboard.SetText(t); } catch { } if (Copied != null) Copied(t); }

        // ------------------------------------------------------------------ keresés
        public int MatchCount { get { return matches.Count; } }
        public int CurrentMatch { get { return curMatch; } }
        public string FindQuery { get { return findQ; } }
        public int Find(string q, bool matchCase, bool regex)
        {
            findQ = q ?? ""; findCase = matchCase; findRegex = regex; matches.Clear(); curMatch = -1; if (findQ.Length == 0) { InvalidateVisual(); if (FindChanged != null) FindChanged(); return 0; }
            Regex rx; try { rx = regex ? new Regex(findQ, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)) : new Regex(Regex.Escape(findQ), matchCase ? RegexOptions.None : RegexOptions.IgnoreCase); } catch { InvalidateVisual(); if (FindChanged != null) FindChanged(); return 0; }
            int total = Scr.Sb.Count + Scr.Rows;
            for (int a = 0; a < total && matches.Count < 20000; a++)
            {
                Cell[] row = Scr.AbsLine(a); if (row == null) continue; int[] cols; string t = RowString(row, out cols); if (t.Length == 0) continue;
                try { foreach (Match m in rx.Matches(t)) { if (m.Length == 0) continue; matches.Add(new int[] { a, cols[m.Index], cols[m.Index + m.Length - 1] }); if (matches.Count >= 20000) break; } } catch (RegexMatchTimeoutException) { break; }
            }
            if (matches.Count > 0) { int bottom = Scr.Sb.Count - Scr.ViewOffset + Rows; curMatch = matches.Count - 1; for (int i = matches.Count - 1; i >= 0; i--) if (matches[i][0] < bottom) { curMatch = i; break; } RevealMatch(); }
            InvalidateVisual(); if (FindChanged != null) FindChanged(); return matches.Count;
        }
        public void FindStep(int dir) { if (matches.Count == 0) return; curMatch = (curMatch + dir + matches.Count) % matches.Count; RevealMatch(); InvalidateVisual(); if (FindChanged != null) FindChanged(); }
        void RevealMatch() { if (curMatch < 0) return; int abs = matches[curMatch][0], top = Scr.Sb.Count - Scr.ViewOffset; if (abs < top || abs >= top + Rows) ScrollToAbs(Math.Max(0, abs - Rows / 3)); }
        public void ClearFind() { matches.Clear(); curMatch = -1; findQ = ""; InvalidateVisual(); if (FindChanged != null) FindChanged(); }

        static string RowString(Cell[] row, out int[] cols)
        {
            StringBuilder sb = new StringBuilder(row.Length); List<int> cl = new List<int>(row.Length);
            for (int i = 0; i < row.Length; i++) { char c = row[i].C; if (c == '\0') continue; sb.Append(c); cl.Add(i); }
            cols = cl.ToArray(); return sb.ToString().TrimEnd(' ');
        }

        // ------------------------------------------------------------------ linkek (URL, útvonal, fájl:sor:oszlop)
        static readonly Regex UrlRx = new Regex(@"(?:https?|ftp|file)://[^\s<>""'`]+|\bwww\.[^\s<>""'`]+\.[A-Za-z]{2,}[^\s<>""'`]*", RegexOptions.Compiled);
        static readonly Regex PathRx = new Regex(@"(?<![\w])[A-Za-z]:\\(?:[^\s\\/:*?""<>|]+\\)*[^\s\\/:*?""<>|]*(?::\d+(?::\d+)?)?|(?<![\w:/\\.])(?:\.{1,2}|~)[\\/][^\s""'<>|]+(?::\d+(?::\d+)?)?|(?<![\w:/\\.])[\w.@-]+(?:[\\/][\w.@-]+)+(?::\d+(?::\d+)?)?", RegexOptions.Compiled);
        static readonly Regex MailRx = new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);

        static string TrimLink(string s) { while (s.Length > 1 && ".,;:!?)]}'\"".IndexOf(s[s.Length - 1]) >= 0) { if (s[s.Length - 1] == ')' && s.IndexOf('(') >= 0) break; s = s.Substring(0, s.Length - 1); } return s; }

        public LinkInfo LinkAt(int abs, int col)
        {
            Cell[] row = Scr.AbsLine(abs); if (row == null) return null; int[] cols; string t = RowString(row, out cols); if (t.Length == 0) return null;
            foreach (Regex rx in new Regex[] { UrlRx, PathRx, MailRx })
                foreach (Match m in rx.Matches(t))
                {
                    string txt = TrimLink(m.Value); if (txt.Length == 0) continue; int i0 = m.Index, i1 = m.Index + txt.Length - 1; if (i1 >= cols.Length) i1 = cols.Length - 1;
                    if (col < cols[i0] || col > cols[i1]) continue;
                    LinkInfo li = new LinkInfo { Text = txt, AbsLine = abs, C0 = cols[i0], C1 = cols[i1] };
                    if (rx == UrlRx) { li.Kind = "url"; li.Path = txt.StartsWith("www.") ? "https://" + txt : txt; }
                    else if (rx == MailRx) { li.Kind = "url"; li.Path = "mailto:" + txt; }
                    else
                    {
                        li.Kind = "file"; string p = txt; Match lm = Regex.Match(p, @":(\d+)(?::(\d+))?$"); if (lm.Success && !(p.Length >= 2 && p[1] == ':' && lm.Index == 1)) { int.TryParse(lm.Groups[1].Value, out li.Line); if (lm.Groups[2].Success) int.TryParse(lm.Groups[2].Value, out li.Col); p = p.Substring(0, lm.Index); }
                        li.Path = p;
                    }
                    return li;
                }
            return null;
        }

        // ------------------------------------------------------------------ rajzolás
        SolidColorBrush Br(Color c) { int k = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B; SolidColorBrush b; if (!brushes.TryGetValue(k, out b)) { if (brushes.Count > 2048) brushes.Clear(); b = new SolidColorBrush(c); b.Freeze(); brushes[k] = b; } return b; }

        Color Resolve(int v, bool fg, bool bold)
        {
            if (v == 0) return fg ? DefFg : DefBg;
            if ((v & unchecked((int)0xFF000000)) == VtScreen.PAL) { int i = v & 255; if (bold && fg && i < 8) i += 8; return Palette[i]; }
            return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        bool Selected(int line, int col)
        {
            if (!hasSel) return false; int a = sL, ac = sC, b = eL, bc = eC; if (a > b || (a == b && ac > bc)) { int t = a; a = b; b = t; t = ac; ac = bc; bc = t; }
            if (line < a || line > b) return false;
            if (selRect) { int c1 = Math.Min(sC, eC), c2 = Math.Max(sC, eC); return col >= c1 && col <= c2; }
            if (line == a && col < ac) return false; if (line == b && col > bc) return false; return true;
        }

        public bool BlinkWanted { get { return IsKeyboardFocused && (Scr.CursorShape >= 0 ? Scr.CursorBlinkApp : (St.CursorBlink || Scr.CursorBlinkApp)); } }

        static readonly Regex SecretRx = new Regex(@"\b(?:sk-[A-Za-z0-9_\-]{20,}|sk_[a-z0-9]{24,}|gh[pousr]_[A-Za-z0-9]{30,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9\-]{10,}|AIza[0-9A-Za-z_\-]{35}|eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,})|(?i:api[_-]?key|secret|token|passwd|password)[""']?\s*[:=]\s*[""']?(?<v>[^\s""]{8,})", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        protected override void OnRender(DrawingContext dc)
        {
            if (gtReg == null) return; double ox = Pad.Left, oy = Pad.Top; int sbCount = Scr.Sb.Count; int topAbs = sbCount - Scr.ViewOffset;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            Color curFindBg = Color.FromArgb(190, 255, 140, 0), allFindBg = Color.FromArgb(120, 255, 210, 0);
            for (int r = 0; r < Rows; r++)
            {
                Cell[] line = Scr.LineAt(r); if (line == null) continue; int n = Math.Min(line.Length, Cols); double y = oy + r * CellH; int abs = topAbs + r;
                // keresési találatok (háttér)
                if (matches.Count > 0) for (int mi = 0; mi < matches.Count; mi++) { int[] m = matches[mi]; if (m[0] != abs) continue; dc.DrawRectangle(Br(mi == curMatch ? curFindBg : allFindBg), null, new Rect(ox + m[1] * CellW, y, (m[2] - m[1] + 1) * CellW, CellH)); }
                // háttér-sávok
                int bs = 0; Color? bcol = null;
                for (int c = 0; c <= n; c++)
                {
                    Color? cur = null;
                    if (c < n) { Cell cl = line[c]; bool inv = (cl.A & 16) != 0; if (inv && cl.Fg == 0) cur = DefFg; else if (!inv && cl.Bg == 0) cur = null; else cur = Resolve(inv ? cl.Fg : cl.Bg, inv, false); }
                    if (!Nullable.Equals(cur, bcol) || c == n) { if (bcol.HasValue) dc.DrawRectangle(Br(bcol.Value), null, new Rect(ox + bs * CellW, y, (c - bs) * CellW, CellH)); bs = c; bcol = cur; }
                }
                // szöveg-sávok
                List<ushort> gl = new List<ushort>(); List<double> ad = new List<double>(); GlyphTypeface rg = null; Color rc = DefFg; int rs = 0;
                for (int c = 0; c <= n; c++)
                {
                    GlyphTypeface gt = null; Color fc = DefFg; ushort gi = 0; double adv = CellW; bool draw = false;
                    if (c < n)
                    {
                        Cell cl = line[c]; char ch = cl.C; int cp = ch; bool pair = false;
                        if (ch >= 0xD800 && ch <= 0xDBFF && c + 1 < n && line[c + 1].C >= 0xDC00 && line[c + 1].C <= 0xDFFF) { cp = char.ConvertToUtf32(ch, line[c + 1].C); pair = true; }
                        if (ch != '\0' && ch != ' ' && (cl.A & 64) == 0 && (pair || !(ch >= 0xD800 && ch <= 0xDFFF)))
                        {
                            bool inv = (cl.A & 16) != 0; fc = inv ? (cl.Bg == 0 ? DefBg : Resolve(cl.Bg, false, false)) : Resolve(cl.Fg, true, (cl.A & 1) != 0);
                            if ((cl.A & 2) != 0) fc = Color.FromArgb(150, fc.R, fc.G, fc.B);
                            GlyphTypeface pref = (cl.A & 1) != 0 ? gtBold : ((cl.A & 4) != 0 ? gtObl : gtReg);
                            if (pref.CharacterToGlyphMap.TryGetValue(cp, out gi)) gt = pref;
                            else if (gtReg.CharacterToGlyphMap.TryGetValue(cp, out gi)) gt = gtReg;
                            else { foreach (GlyphTypeface f in fallbacks) if (f.CharacterToGlyphMap.TryGetValue(cp, out gi)) { gt = f; break; } }
                            if (gt != null) { draw = true; if (pair || (c + 1 < n && line[c + 1].C == '\0')) adv = CellW * 2; }
                        }
                    }
                    bool sameRun = gt != null && rg != null && gt == rg && fc == rc;
                    if (!sameRun || !draw || c == n)
                    {
                        if (gl.Count > 0 && rg != null) { GlyphRun run = new GlyphRun(rg, 0, false, FontSizePx, gl.ToArray(), new Point(ox + rs * CellW, y + BaseY), ad.ToArray(), null, null, null, null, null, null); dc.DrawGlyphRun(Br(rc), run); }
                        gl.Clear(); ad.Clear(); rg = null;
                    }
                    if (draw) { if (rg == null) { rg = gt; rc = fc; rs = c; } gl.Add(gi); ad.Add(adv); if (adv > CellW) c++; }
                }
                // aláhúzás, áthúzás
                for (int c = 0; c < n; c++)
                {
                    Cell cl = line[c]; if ((cl.A & (8 | 32)) == 0 || cl.C == '\0') continue; bool inv = (cl.A & 16) != 0;
                    Color fc = inv ? (cl.Bg == 0 ? DefBg : Resolve(cl.Bg, false, false)) : Resolve(cl.Fg, true, (cl.A & 1) != 0);
                    if ((cl.A & 8) != 0) dc.DrawRectangle(Br(fc), null, new Rect(ox + c * CellW, y + BaseY + 2, CellW, 1));
                    if ((cl.A & 32) != 0) dc.DrawRectangle(Br(fc), null, new Rect(ox + c * CellW, y + CellH / 2, CellW, 1));
                }
                // link-kiemelés
                if (hover != null && hover.AbsLine == abs) dc.DrawRectangle(Br(Color.FromRgb(90, 170, 255)), null, new Rect(ox + hover.C0 * CellW, y + BaseY + 2, (hover.C1 - hover.C0 + 1) * CellW, 1.4));
                // kijelölés
                if (hasSel)
                {
                    int a = -1, z = -1; for (int c = 0; c < Cols; c++) { if (Selected(abs, c)) { if (a < 0) a = c; z = c; } }
                    if (a >= 0) dc.DrawRectangle(Br(Color.FromArgb(120, Theme.Sel.R, Theme.Sel.G, Theme.Sel.B)), null, new Rect(ox + a * CellW, y, (z - a + 1) * CellW, CellH));
                }
            }
            // parancs-blokkok: bal oldali jelző a hibás parancsokon, jobb oldali jelvény (idő / kilépési kód)
            if (St.Badges && Marks.Count > 0 && !Scr.Alt)
            {
                for (int i = 0; i < Marks.Count; i++)
                {
                    PromptMark m = Marks[i]; if (!m.Started) continue; int a0 = AbsOf(m.Id), a1 = m.EndId >= 0 ? AbsOf(m.EndId) - 1 : topAbs + Rows; if (a1 < topAbs || a0 >= topAbs + Rows) continue;
                    bool fail = m.Done && m.Exit != 0 && m.Exit != int.MinValue;
                    if (fail) { double y0 = oy + Math.Max(0, a0 - topAbs) * CellH, y1 = oy + (Math.Min(a1, topAbs + Rows - 1) - topAbs + 1) * CellH; dc.DrawRectangle(Br(Color.FromArgb(200, 255, 85, 80)), null, new Rect(Math.Max(0, ox - 7), y0, 2.5, y1 - y0)); }
                    if (!m.Done || a0 < topAbs || a0 >= topAbs + Rows) continue; double sec = m.Seconds;
                    if (!fail && sec < 2.0) continue;
                    string txt = (fail ? "✗ " + m.Exit + "  " : "✓  ") + FmtDur(sec); Color bc2 = fail ? Color.FromRgb(255, 105, 97) : Color.FromRgb(120, 200, 130);
                    FormattedText ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11.5, Br(Color.FromArgb(235, bc2.R, bc2.G, bc2.B))); double tw = ft.Width + 12, ty = oy + (a0 - topAbs) * CellH + (CellH - ft.Height) / 2, tx = ActualWidth - Pad.Right - tw - 4;
                    dc.DrawRoundedRectangle(Br(Color.FromArgb(150, DefBg.R, DefBg.G, DefBg.B)), null, new Rect(tx - 2, ty - 1, tw, ft.Height + 2), 6, 6); dc.DrawText(ft, new Point(tx + 4, ty));
                }
            }
            // kurzor
            if (Scr.CursorVisible && Scr.ViewOffset == 0)
            {
                double cx = ox + Math.Min(Scr.X, Cols - 1) * CellW, cy = oy + Scr.Y * CellH; int shape = Scr.CursorShape >= 0 ? Scr.CursorShape : St.CursorStyle; Color cc = Theme.Cursor;
                if (IsKeyboardFocused)
                {
                    if (blinkOn || !BlinkWanted)
                    {
                        if (shape == 0)
                        {
                            dc.DrawRectangle(Br(Color.FromArgb(220, cc.R, cc.G, cc.B)), null, new Rect(cx, cy, CellW, CellH));
                            Cell cl = Scr.Grid[Math.Min(Scr.Y, Scr.Rows - 1)][Math.Min(Scr.X, Scr.Cols - 1)]; ushort gi;
                            if (cl.C > ' ' && gtReg.CharacterToGlyphMap.TryGetValue(cl.C, out gi)) dc.DrawGlyphRun(Br(DefBg), new GlyphRun(gtReg, 0, false, FontSizePx, new ushort[] { gi }, new Point(cx, cy + BaseY), new double[] { CellW }, null, null, null, null, null, null));
                        }
                        else if (shape == 1) dc.DrawRectangle(Br(cc), null, new Rect(cx, cy + CellH - 2.2, CellW, 2.2));
                        else dc.DrawRectangle(Br(cc), null, new Rect(cx, cy, 2.0, CellH));
                    }
                }
                else dc.DrawRectangle(null, new Pen(Br(Color.FromArgb(210, cc.R, cc.G, cc.B)), 1), new Rect(cx + 0.5, cy + 0.5, CellW - 1, CellH - 1));
            }
            // titok-őr: API-kulcsok / tokenek elmosása (az egér alatti sorban látszanak)
            if (St.SecretGuard)
            {
                try
                {
                    Point mp = Mouse.GetPosition(this); int mrow = IsMouseOver ? (int)((mp.Y - oy) / CellH) : -1; Brush mask = Br(Color.FromRgb((byte)Math.Min(255, DefBg.R + 44), (byte)Math.Min(255, DefBg.G + 44), (byte)Math.Min(255, DefBg.B + 44)));
                    for (int r = 0; r < Rows; r++)
                    {
                        if (r == mrow) continue; Cell[] ln = Scr.LineAt(r); if (ln == null) continue; int[] cs; string t = RowString(ln, out cs); if (t.Length < 14) continue;
                        foreach (Match m in SecretRx.Matches(t)) { Group gp = m.Groups["v"].Success ? m.Groups["v"] : (Group)m; double x0 = ox + cs[gp.Index] * CellW, x1 = ox + (cs[gp.Index + gp.Length - 1] + 1) * CellW; dc.DrawRoundedRectangle(mask, null, new Rect(x0, oy + r * CellH + 1, x1 - x0, CellH - 2), 3, 3); }
                    }
                }
                catch { }
            }
            // görgetősáv-jelző
            if (Scr.ViewOffset > 0 && sbCount > 0) { double total = sbCount + Rows, th = Math.Max(24, ActualHeight * Rows / total), ty = (ActualHeight - th) * (sbCount - Scr.ViewOffset) / sbCount; dc.DrawRoundedRectangle(Br(Color.FromArgb(120, 200, 200, 200)), null, new Rect(ActualWidth - 7, ty, 4, th), 2, 2); }
            if (bellAlpha > 0) dc.DrawRectangle(Br(Color.FromArgb((byte)(80 * bellAlpha), 255, 255, 255)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (Dim && St.DimInactive && !IsKeyboardFocusWithin) dc.DrawRectangle(Br(Color.FromArgb(70, DefBg.R, DefBg.G, DefBg.B)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        static string FmtDur(double s) { if (s < 1) return ((int)(s * 1000)) + " ms"; if (s < 60) return s.ToString("0.0", CultureInfo.InvariantCulture) + " s"; if (s < 3600) return ((int)(s / 60)) + " p " + ((int)(s % 60)) + " mp"; return ((int)(s / 3600)) + Loc.T(" ó ") + ((int)((s % 3600) / 60)) + " p"; }

        protected override void OnRenderSizeChanged(SizeChangedInfo i) { base.OnRenderSizeChanged(i); Relayout(); }
        void Relayout()
        {
            if (CellW <= 0) return; double w = ActualWidth - Pad.Left - Pad.Right, h = ActualHeight - Pad.Top - Pad.Bottom; if (w < 20 || h < 10) return;
            int c = Math.Max(10, (int)(w / CellW)), r = Math.Max(3, (int)(h / CellH));
            if (c != Cols || r != Rows) { Cols = c; Rows = r; Scr.Resize(c, r); if (pty != null) pty.Resize(c, r); remapAt = DateTime.Now.AddMilliseconds(450); if (GridChanged != null) GridChanged(); }
        }

        void OnBell()
        {
            if (St.VisualBell) { bellAlpha = 1; if (!bellTimer.IsEnabled) bellTimer.Start(); InvalidateVisual(); }
            if (St.AudibleBell) { try { System.Media.SystemSounds.Beep.Play(); } catch { } }
            if (BellRung != null) BellRung();
        }

        // ------------------------------------------------------------------ bemenet: billentyűk
        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            string t = e.Text; if (string.IsNullOrEmpty(t)) return; StringBuilder sb = new StringBuilder();
            foreach (char c in t) if (c >= 0x20 && c != 0x7F) sb.Append(c);
            if (sb.Length > 0) { TypedInput(); UserSend(sb.ToString()); e.Handled = true; }
        }
        void TypedInput() { if (St.ScrollOnKey) Scr.ViewOffset = 0; if (hasSel) { hasSel = false; InvalidateVisual(); } }
        static string Mod(int m) { return m > 1 ? ";" + m : ""; }

        // a gazda ezt hívja, ha a billentyűt a terminál kapja (a gazda saját gyorsbillentyűi előbb futnak)
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : (e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key);
            ModifierKeys mk = Keyboard.Modifiers; bool ctrl = (mk & ModifierKeys.Control) != 0, alt = (mk & ModifierKeys.Alt) != 0, shift = (mk & ModifierKeys.Shift) != 0;
            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
            if (alt && !ctrl && k == Key.F4) return;
            if (ctrl && alt) return;                                                           // AltGr: a szöveg-bemenet kezeli (magyar billentyűzet)
            int m = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
            if (ctrl && !alt && (k == Key.OemPlus || k == Key.Add)) { SetFontSize(FontSizePx + 1); e.Handled = true; return; }
            if (ctrl && !alt && (k == Key.OemMinus || k == Key.Subtract)) { SetFontSize(FontSizePx - 1); e.Handled = true; return; }
            if (ctrl && !alt && !shift && (k == Key.D0 || k == Key.NumPad0)) { SetFontSize(15); e.Handled = true; return; }
            if ((ctrl && shift && k == Key.C) || (ctrl && !shift && !alt && k == Key.C && hasSel) || (ctrl && k == Key.Insert)) { Copy(); e.Handled = true; return; }
            if ((ctrl && k == Key.V) || (shift && k == Key.Insert)) { Paste(shift && ctrl ? PasteMode.Plain : PasteMode.Normal); e.Handled = true; return; }
            if (shift && !ctrl && k == Key.PageUp) { Scr.ViewOffset = Math.Min(Scr.Sb.Count, Scr.ViewOffset + Rows - 1); InvalidateVisual(); e.Handled = true; return; }
            if (shift && !ctrl && k == Key.PageDown) { Scr.ViewOffset = Math.Max(0, Scr.ViewOffset - (Rows - 1)); InvalidateVisual(); e.Handled = true; return; }
            if (ctrl && !alt && !shift && k == Key.Home && Scr.ViewOffset >= 0 && Scr.Sb.Count > 0 && !Scr.AppCursor) { /* a Ctrl+Home a shellnek marad */ }
            string s = null; bool ac = Scr.AppCursor && m == 1;
            switch (k)
            {
                case Key.Enter: s = alt ? "\x1b\r" : "\r"; if (!alt) CommandEntered(); break;
                case Key.Back: s = ctrl ? "\x08" : (alt ? "\x1b\x7f" : "\x7f"); break;
                case Key.Tab: s = shift ? "\x1b[Z" : "\t"; break;
                case Key.Escape: s = "\x1b"; break;
                case Key.Up: s = ac ? "\x1bOA" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "A"; break;
                case Key.Down: s = ac ? "\x1bOB" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "B"; break;
                case Key.Right: s = ac ? "\x1bOC" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "C"; break;
                case Key.Left: s = ac ? "\x1bOD" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "D"; break;
                case Key.Home: s = ac ? "\x1bOH" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "H"; break;
                case Key.End: s = ac ? "\x1bOF" : "\x1b[" + (m > 1 ? "1" + Mod(m) : "") + "F"; break;
                case Key.Insert: s = "\x1b[2~"; break;
                case Key.Delete: s = "\x1b[3" + Mod(m) + "~"; break;
                case Key.PageUp: s = "\x1b[5" + Mod(m) + "~"; break;
                case Key.PageDown: s = "\x1b[6" + Mod(m) + "~"; break;
                case Key.F1: s = "\x1bOP"; break; case Key.F2: s = "\x1bOQ"; break; case Key.F3: s = "\x1bOR"; break; case Key.F4: s = "\x1bOS"; break;
                case Key.F5: s = "\x1b[15~"; break; case Key.F6: s = "\x1b[17~"; break; case Key.F7: s = "\x1b[18~"; break; case Key.F8: s = "\x1b[19~"; break;
                case Key.F9: s = "\x1b[20~"; break; case Key.F10: s = "\x1b[21~"; break; case Key.F11: s = "\x1b[23~"; break; case Key.F12: s = "\x1b[24~"; break;
            }
            if (s == null && ctrl && !alt) { if (k >= Key.A && k <= Key.Z) s = ((char)(k - Key.A + 1)).ToString(); else if (k == Key.Space || k == Key.D2) s = "\0"; }
            else if (s == null && alt && !ctrl)
            {
                if (k >= Key.A && k <= Key.Z) { char c = (char)('a' + (k - Key.A)); if (shift) c = char.ToUpper(c); s = "\x1b" + c; }
                else if (k >= Key.D0 && k <= Key.D9 && !shift) s = "\x1b" + (char)('0' + (k - Key.D0));
            }
            if (s != null) { TypedInput(); UserSend(s); e.Handled = true; }
        }

        // ------------------------------------------------------------------ vágólap
        public bool HasSelection { get { return hasSel; } }
        public void Copy()
        {
            string t = SelectedText(); if (!string.IsNullOrEmpty(t)) SetClip(t);
            hasSel = false; InvalidateVisual();
        }

        static readonly Regex DangerRx = new Regex(@"(?i)(\brm\s+-[a-z]*r[a-z]*f?[a-z]*\s+(/|~|\*)|remove-item[^\n]*-recurse[^\n]*-force[^\n]*(\s[a-z]:\\?(\s|$)|\\\*)|\bformat\s+[a-z]:|\bdel\s+/[sq][^\n]*\s[a-z]:\\|\bdd\s+if=|:\(\)\s*\{|\|\s*(sh|bash|iex|invoke-expression)\b|\b(iwr|irm|curl|wget)\b[^\n|]*\|\s*(iex|sh|bash)|drop\s+(table|database)\b|\bshutdown\s+/[sr])", RegexOptions.None, TimeSpan.FromMilliseconds(250));

        public static string Sanitize(string t)
        {
            return t.Replace('\u201C', '"').Replace('\u201D', '"').Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u00A0', ' ').Replace("\u200B", "").Replace("\uFEFF", "");
        }

        public void Paste() { Paste(PasteMode.Normal); }
        public void Paste(PasteMode mode)
        {
            string t = null;
            try
            {
                if (mode == PasteMode.Quoted && Clipboard.ContainsFileDropList()) { StringBuilder sb = new StringBuilder(); foreach (string f in Clipboard.GetFileDropList()) { if (sb.Length > 0) sb.Append(' '); sb.Append(QuotePath(f)); } t = sb.ToString(); }
                else if (Clipboard.ContainsFileDropList() && !Clipboard.ContainsText()) { StringBuilder sb = new StringBuilder(); foreach (string f in Clipboard.GetFileDropList()) { if (sb.Length > 0) sb.Append(' '); sb.Append(QuotePath(f)); } t = sb.ToString(); }
                else if (Clipboard.ContainsText()) t = Clipboard.GetText();
            }
            catch { }
            if (string.IsNullOrEmpty(t)) return; if (St.SmartPaste || mode == PasteMode.Plain || mode == PasteMode.OneLine) t = Sanitize(t);
            if (mode == PasteMode.Quoted && !t.Contains("\"") && t.IndexOf(' ') >= 0 && t.IndexOf('\n') < 0) t = "\"" + t + "\"";
            if (mode == PasteMode.OneLine) t = Regex.Replace(t.Trim(), @"\s*[\r\n]+\s*", " ");
            string norm = t.Replace("\r\n", "\r").Replace('\n', '\r'); bool multi = norm.IndexOf('\r') >= 0; bool danger = false; try { danger = t.Length > 200000 || DangerRx.IsMatch(t); } catch (RegexMatchTimeoutException) { danger = true; }
            Action go = delegate { DoPaste(norm); };
            if (AskConfirm != null && ((St.PasteConfirm && multi && !Scr.BracketedPaste) || danger))
            {
                string prev = t.Length > 600 ? t.Substring(0, 600) + "\n…" : t; int lines = t.Split('\n').Length;
                AskConfirm(danger ? Loc.T("Veszélyesnek tűnő parancs beillesztése") : Loc.T("Többsoros beillesztés"), (danger ? Loc.T("A beillesztendő szöveg veszélyes parancsot tartalmazhat.\n\n") : lines + Loc.T(" sor kerül beillesztésre, és mindegyik lefuthat.\n\n")) + prev, go);
            }
            else go();
        }
        static readonly Regex CtrlRx = new Regex("[\\x00-\\x08\\x0b\\x0c\\x0e-\\x1a\\x1c-\\x1f\\x7f\\u0080-\\u009f]");
        void DoPaste(string norm) { norm = CtrlRx.Replace(norm, ""); TypedInput(); UserSend(Scr.BracketedPaste ? "\x1b[200~" + norm + "\x1b[201~" : norm); if (Pasted != null) Pasted(norm); }
        public void PasteText(string t) { string norm = CtrlRx.Replace(t, "").Replace("\r\n", "\r").Replace('\n', '\r'); if (norm.IndexOf('\r') >= 0 && !Scr.BracketedPaste && St.PasteConfirm && AskConfirm != null) { AskConfirm(Loc.T("Többsoros beillesztés"), norm.Split('\r').Length + Loc.T(" sor kerül beillesztésre, és mindegyik lefuthat.\n\n") + (norm.Length > 600 ? norm.Substring(0, 600) + "\n…" : norm), delegate { DoPaste(norm); }); } else DoPaste(norm); }

        // ------------------------------------------------------------------ kijelölés
        string SelectedText()
        {
            if (!hasSel) return ""; int a = sL, ac = sC, b = eL, bc = eC; if (a > b || (a == b && ac > bc)) { int t = a; a = b; b = t; t = ac; ac = bc; bc = t; }
            StringBuilder sb = new StringBuilder();
            for (int l = a; l <= b; l++)
            {
                Cell[] row = Scr.AbsLine(l); int c1, c2; if (selRect) { c1 = Math.Min(sC, eC); c2 = Math.Max(sC, eC); } else { c1 = l == a ? ac : 0; c2 = l == b ? bc : int.MaxValue; }
                string line = VtScreen.LineText(row, c1, c2); if (!St.TrimCopy) { } sb.Append(line); if (l < b) sb.Append("\r\n");
            }
            return sb.ToString();
        }
        public string SelectionText { get { return SelectedText(); } }
        public void SelectAll() { sL = 0; sC = 0; eL = Scr.Sb.Count + Scr.Rows - 1; eC = Math.Max(0, Scr.Cols - 1); selRect = false; hasSel = true; InvalidateVisual(); if (SelectionChanged != null) SelectionChanged(); }
        public void ClearSelection() { hasSel = false; InvalidateVisual(); }
        public void SelectLineAt(int abs) { sL = eL = abs; sC = 0; eC = Cols - 1; selRect = false; hasSel = true; InvalidateVisual(); }
        public void SelectCurrentLine() { SelectLineAt(Scr.Sb.Count + Scr.Y); }
        public void SelectLink(LinkInfo li) { if (li == null) return; sL = eL = (int)li.AbsLine; sC = li.C0; eC = li.C1; selRect = false; hasSel = true; InvalidateVisual(); }

        void SelectWord(int l, int c)
        {
            Cell[] row = Scr.AbsLine(l); if (row == null) return; int a = c, z = c;
            Func<int, bool> isw = delegate (int i) { char ch = row[i].C; return ch != ' ' && ch != '\0' && "()[]{}<>\"'`,;|&".IndexOf(ch) < 0; };
            if (c >= row.Length || !isw(c)) return; while (a > 0 && isw(a - 1)) a--; while (z < row.Length - 1 && isw(z + 1)) z++;
            sL = eL = l; sC = a; eC = z; selRect = false; hasSel = true;
        }

        // ------------------------------------------------------------------ egér
        void CellAt(Point p, out int line, out int col, out int row)
        {
            int r = (int)((p.Y - Pad.Top) / CellH), c = (int)((p.X - Pad.Left) / CellW); r = Math.Max(0, Math.Min(Rows - 1, r)); c = Math.Max(0, Math.Min(Cols - 1, c));
            line = Scr.Sb.Count - Scr.ViewOffset + r; col = c; row = r;
        }

        bool Reporting { get { return Scr.MouseMode > 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0; } }
        void Report(int btn, bool release, Point p, bool motion)
        {
            int l, c, r; CellAt(p, out l, out c, out r); int b = btn; ModifierKeys mk = Keyboard.Modifiers; if ((mk & ModifierKeys.Alt) != 0) b += 8; if ((mk & ModifierKeys.Control) != 0) b += 16; if (motion) b += 32;
            if (Scr.MouseSgr) Send("\x1b[<" + b + ";" + (c + 1) + ";" + (r + 1) + (release ? "m" : "M"));
            else { if (release) b = (b & ~3) | 3; if (c + 1 > 222 || r + 1 > 222) return; Send("\x1b[M" + (char)(32 + b) + (char)(32 + c + 1) + (char)(32 + r + 1)); }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus(); Keyboard.Focus(this); Point p = e.GetPosition(this);
            if (Reporting) { Report(0, false, p, false); CaptureMouse(); e.Handled = true; return; }
            int l, c, rw; CellAt(p, out l, out c, out rw);
            ModifierKeys mk = Keyboard.Modifiers;
            if ((mk & ModifierKeys.Control) != 0 || !St.LinkNeedsCtrl) { LinkInfo li = LinkAt(l, c); if (li != null && (mk & ModifierKeys.Control) == 0 && li.Kind != "url") li = null; if (li != null) { if (LinkActivated != null) LinkActivated(li); e.Handled = true; return; } }
            DateTime now = DateTime.Now; if ((now - lastClick).TotalMilliseconds < 400 && Math.Abs(p.X - lastClickPt.X) < 5 && Math.Abs(p.Y - lastClickPt.Y) < 5) clickCount++; else clickCount = 1; lastClick = now; lastClickPt = p;
            if ((mk & ModifierKeys.Shift) != 0 && hasSel) { eL = l; eC = c; selecting = true; CaptureMouse(); }
            else if (clickCount == 2) { SelectWord(l, c); if (St.AutoCopy) AutoCopy(); }
            else if (clickCount >= 3) { sL = l; sC = 0; eL = l; eC = Cols - 1; selRect = false; hasSel = true; if (St.AutoCopy) AutoCopy(); }
            else { sL = eL = l; sC = eC = c; hasSel = false; selecting = true; selRect = (mk & ModifierKeys.Alt) != 0; CaptureMouse(); }
            InvalidateVisual(); e.Handled = true;
        }
        void AutoCopy() { string t = SelectedText(); if (!string.IsNullOrEmpty(t)) { try { Clipboard.SetText(t); } catch { } } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (St.SecretGuard) { int rr0 = (int)((p.Y - Pad.Top) / CellH); if (rr0 != secretRow) { secretRow = rr0; InvalidateVisual(); } }
            if (Reporting && IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && Scr.MouseMode >= 1002) { Report(0, false, p, true); return; }
            if (Reporting && Scr.MouseMode == 1003 && !IsMouseCaptured) { Report(3, false, p, true); return; }
            if (!selecting)
            {
                bool want = (Keyboard.Modifiers & ModifierKeys.Control) != 0 || !St.LinkNeedsCtrl; LinkInfo li = null; if (want) { int l, c, r; CellAt(p, out l, out c, out r); li = LinkAt(l, c); if (li != null && (Keyboard.Modifiers & ModifierKeys.Control) == 0 && li.Kind != "url") li = null; }
                if ((li == null) != (hover == null) || (li != null && hover != null && (li.AbsLine != hover.AbsLine || li.C0 != hover.C0))) { hover = li; Cursor = hover != null ? Cursors.Hand : Cursors.IBeam; InvalidateVisual(); }
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) { selecting = false; ReleaseMouseCapture(); return; }
            int l2, c2, r2; CellAt(p, out l2, out c2, out r2);
            if (l2 != eL || c2 != eC) { eL = l2; eC = c2; hasSel = !(sL == eL && sC == eC); InvalidateVisual(); if (SelectionChanged != null) SelectionChanged(); }
            if (p.Y < 0) { Scr.ViewOffset = Math.Min(Scr.Sb.Count, Scr.ViewOffset + 1); InvalidateVisual(); } else if (p.Y > ActualHeight) { Scr.ViewOffset = Math.Max(0, Scr.ViewOffset - 1); InvalidateVisual(); }
        }
        int secretRow = -2;
        protected override void OnMouseLeave(MouseEventArgs e) { if (St.SecretGuard && secretRow != -2) { secretRow = -2; InvalidateVisual(); } if (hover != null) { hover = null; Cursor = Cursors.IBeam; InvalidateVisual(); } }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (Reporting || (IsMouseCaptured && Scr.MouseMode > 0 && !selecting)) { Report(0, true, e.GetPosition(this), false); ReleaseMouseCapture(); return; }
            if (selecting) { selecting = false; if (hasSel && St.AutoCopy) AutoCopy(); }
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { if (Reporting) { Focus(); Report(2, false, e.GetPosition(this), false); e.Handled = true; } }
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            if (Reporting) { Report(2, true, e.GetPosition(this), false); e.Handled = true; return; }
            Focus(); if (ContextRequested != null) ContextRequested(e.GetPosition(this)); e.Handled = true;
        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle) { Focus(); if (Reporting) { Report(1, false, e.GetPosition(this), false); } else if (St.MiddlePaste) { if (hasSel) { string t = SelectedText(); if (t.Length > 0) PasteText(t); } else Paste(); } e.Handled = true; }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle && Reporting) { Report(1, true, e.GetPosition(this), false); e.Handled = true; } }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Point p = e.GetPosition(this); int notches = Math.Max(1, Math.Abs(e.Delta) / 120);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SetFontSize(FontSizePx + (e.Delta > 0 ? 1 : -1)); e.Handled = true; return; }
            if (Reporting) { for (int i = 0; i < notches; i++) Report(e.Delta > 0 ? 64 : 65, false, p, false); e.Handled = true; return; }
            if (Scr.Alt) { string key = e.Delta > 0 ? (Scr.AppCursor ? "\x1bOA" : "\x1b[A") : (Scr.AppCursor ? "\x1bOB" : "\x1b[B"); for (int i = 0; i < notches * 3; i++) Send(key); }
            else { Scr.ViewOffset = Math.Max(0, Math.Min(Scr.Sb.Count, Scr.ViewOffset + (e.Delta > 0 ? 1 : -1) * notches * Math.Max(1, St.WheelLines))); InvalidateVisual(); }
            e.Handled = true;
        }

        // ------------------------------------------------------------------ görgetés, törlés
        public void ScrollTop() { Scr.ViewOffset = Scr.Sb.Count; InvalidateVisual(); }
        public void ScrollBottom() { Scr.ViewOffset = 0; InvalidateVisual(); }
        public void ScrollPage(int dir) { Scr.ViewOffset = Math.Max(0, Math.Min(Scr.Sb.Count, Scr.ViewOffset + dir * (Rows - 1))); InvalidateVisual(); }
        public void ScrollLines(int n) { Scr.ViewOffset = Math.Max(0, Math.Min(Scr.Sb.Count, Scr.ViewOffset + n)); InvalidateVisual(); }
        public void ClearScreen() { Send("\x0c"); }
        public void ClearScrollback() { hasSel = false; Scr.Trimmed += Scr.Sb.Count; Scr.Sb.Clear(); Scr.ViewOffset = 0; matches.Clear(); InvalidateVisual(); }
        public void ClearAll() { ClearScrollback(); Send("\x0c"); }
        public void ResetTerminal() { Scr.Feed(Encoding.UTF8.GetBytes("\x1b" + "c"), 2); InvalidateVisual(); }

        // ------------------------------------------------------------------ export
        public string AllText(bool includeScrollback)
        {
            StringBuilder sb = new StringBuilder(); int a = includeScrollback ? 0 : Scr.Sb.Count, total = Scr.Sb.Count + Scr.Rows; int last = total - 1;
            while (last > a && VtScreen.LineText(Scr.AbsLine(last), 0, Cols - 1).Length == 0) last--;
            for (int i = a; i <= last; i++) { sb.Append(VtScreen.LineText(Scr.AbsLine(i), 0, int.MaxValue)); if (i < last) sb.Append("\r\n"); }
            return sb.ToString();
        }

        string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public string AllHtml(bool selectionOnly)
        {
            int a = 0, z = Scr.Sb.Count + Scr.Rows - 1; int sa = 0, sz = 0;
            if (selectionOnly && hasSel) { a = Math.Min(sL, eL); z = Math.Max(sL, eL); sa = a; sz = z; } else if (selectionOnly) return "";
            while (z > a && VtScreen.LineText(Scr.AbsLine(z), 0, Cols - 1).Length == 0) z--;
            StringBuilder sb = new StringBuilder(); sb.Append("<pre style=\"background:" + Hex(DefBg) + ";color:" + Hex(DefFg) + ";font-family:'" + St.Font + "',Consolas,monospace;font-size:13px;padding:10px;border-radius:8px\">");
            for (int i = a; i <= z; i++)
            {
                Cell[] row = Scr.AbsLine(i); if (row == null) { sb.Append('\n'); continue; } int end = row.Length - 1; while (end >= 0 && (row[end].C == ' ' || row[end].C == '\0') && row[end].Bg == 0) end--;
                int ps = -1; string pc = null;
                for (int c = 0; c <= end; c++)
                {
                    Cell cl = row[c]; if (cl.C == '\0') continue; bool inv = (cl.A & 16) != 0; Color fg = inv ? (cl.Bg == 0 ? DefBg : Resolve(cl.Bg, false, false)) : Resolve(cl.Fg, true, (cl.A & 1) != 0); Color bg = inv ? (cl.Fg == 0 ? DefFg : Resolve(cl.Fg, true, false)) : (cl.Bg == 0 ? DefBg : Resolve(cl.Bg, false, false));
                    string css = "color:" + Hex(fg) + (cl.Bg != 0 || inv ? ";background:" + Hex(bg) : "") + ((cl.A & 1) != 0 ? ";font-weight:bold" : "") + ((cl.A & 4) != 0 ? ";font-style:italic" : "") + ((cl.A & 8) != 0 ? ";text-decoration:underline" : "");
                    if (css != pc) { if (ps >= 0) sb.Append("</span>"); sb.Append("<span style=\"" + css + "\">"); pc = css; ps = c; }
                    sb.Append(System.Net.WebUtility.HtmlEncode(cl.C.ToString()));
                }
                if (ps >= 0) sb.Append("</span>"); if (i < z) sb.Append('\n');
            }
            sb.Append("</pre>"); return sb.ToString();
        }
        public void CopyAsHtml(bool selectionOnly)
        {
            string html = AllHtml(selectionOnly); if (html.Length == 0) return; string text = selectionOnly ? SelectedText() : AllText(true);
            try { DataObject d = new DataObject(); d.SetText(text); d.SetData(DataFormats.Html, HtmlClip(html)); Clipboard.SetDataObject(d, true); if (Copied != null) Copied(text); } catch { }
        }
        static string HtmlClip(string html)
        {
            string head = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n"; string pre = "<html><body><!--StartFragment-->", post = "<!--EndFragment--></body></html>";
            int hl = string.Format(head, 0, 0, 0, 0).Length; int sh = hl, sf = sh + pre.Length, ef = sf + Encoding.UTF8.GetByteCount(html), eh = ef + post.Length;
            return string.Format(head, sh, eh, sf, ef) + pre + html + post;
        }
        public void SaveText(string path, bool includeScrollback) { File.WriteAllText(path, AllText(includeScrollback), new UTF8Encoding(true)); }
        public void SaveHtml(string path) { File.WriteAllText(path, "<!doctype html><meta charset=utf-8><title>Lumen Terminal</title><body style=\"margin:0;background:" + Hex(DefBg) + "\">" + AllHtml(false), new UTF8Encoding(false)); }
        public int ScrollbackLines { get { return Scr.Sb.Count; } }
    }
}
