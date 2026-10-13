// GlassTerm mag: VT-emulátor (VtScreen), ConPTY-kapcsolat (ConPty) és a kirajzoló vezérlő (TermView).
// C# 5, WPF, csak a gépen levő .NET Framework fordítóval fordul.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GlassTerm
{
    public struct Cell { public char C; public int Fg, Bg; public byte A; }   // A: 1 félkövér, 2 halvány, 4 dőlt, 8 aláhúzott, 16 fordított, 32 áthúzott, 64 rejtett

    // ------------------------------------------------------------------------------------------ VT képernyő
    public class VtScreen
    {
        public const int PAL = 0x01000000;
        public string Cwd; public int MouseMode; public bool MouseSgr, FocusEvents, CursorBlinkApp; public int CursorShape = -1; public int AppExit = int.MinValue; public DateTime AppExitAt = DateTime.MinValue;
        public event Action Bell, CwdChanged; public event Action<string> Notify, ClipboardSet, PreviewRequest; char inter;
        public long LineId(int row) { return Trimmed + Sb.Count + row; }
        public string TextAtId(long id) { int abs = (int)(id - Trimmed); Cell[] r = AbsLine(abs); return r == null ? null : LineText(r, 0, Cols - 1); }
        public int Cols, Rows; public Cell[][] Grid; Cell[][] mainGrid; public bool Alt;
        public long Trimmed; public List<Cell[]> Sb = new List<Cell[]>(); public int SbMax = 10000; public int ViewOffset;
        public int X, Y; public bool WrapNext;
        public int CurFg, CurBg; public byte CurA;
        int top, bot, savX, savY, savFg, savBg; byte savA; bool savWrap;
        public bool AppCursor, BracketedPaste, CursorVisible = true, AutoWrap = true, Origin, Insert;
        public string Title = ""; public event Action TitleChanged; public Action<string> Reply;
        public bool Dirty; bool[] tabs;
        Decoder dec = new UTF8Encoding(false).GetDecoder();
        enum St { Ground, Esc, EscInt, Csi, Osc, OscEsc, Str, StrEsc }
        bool[] isSub = new bool[33]; bool nextSub;
        St st; int[] pars = new int[32]; int np; bool haveNum; int cur; char priv; StringBuilder osc = new StringBuilder();

        public VtScreen(int cols, int rows) { Cols = cols; Rows = rows; Grid = NewGrid(cols, rows); top = 0; bot = rows - 1; ResetTabs(); }
        static Cell[][] NewGrid(int c, int r) { Cell[][] g = new Cell[r][]; for (int i = 0; i < r; i++) g[i] = BlankRow(c, 0); return g; }
        static Cell[] BlankRow(int c, int bg) { Cell[] row = new Cell[c]; Cell b = new Cell(); b.C = ' '; b.Bg = bg; for (int i = 0; i < c; i++) row[i] = b; return row; }
        void ResetTabs() { tabs = new bool[Cols]; for (int i = 0; i < Cols; i += 8) tabs[i] = true; }

        public void Resize(int c, int r)
        {
            if (c < 2) c = 2; if (r < 1) r = 1; if (c == Cols && r == Rows) return;
            int shift = 0; if (!Alt && r < Rows) shift = Math.Max(0, Y - (r - 1));
            for (int i = 0; i < shift; i++) AddSb(Grid[i]);
            Grid = ResizeGrid(Grid, Cols, Rows, c, r, shift);
            if (Alt && mainGrid != null)         // a mentett fő képernyő is követi a méretet; ami alul kicsúszik (a prompt!), azt a görgetősávba toljuk
            {
                int sh2 = r < Rows ? Math.Max(0, savY - (r - 1)) : 0; for (int i = 0; i < sh2; i++) AddSb(mainGrid[i]);
                mainGrid = ResizeGrid(mainGrid, Cols, Rows, c, r, sh2); savY = Math.Max(0, savY - sh2);
            }
            Y -= shift; Cols = c; Rows = r; X = Math.Min(X, c - 1); Y = Math.Max(0, Math.Min(Y, r - 1)); top = 0; bot = r - 1; WrapNext = false; ResetTabs();
            ViewOffset = Math.Min(ViewOffset, Sb.Count); Dirty = true;
        }
        static Cell[][] ResizeGrid(Cell[][] g, int oc, int or, int c, int r, int shift)
        {
            Cell[][] ng = new Cell[r][];
            for (int i = 0; i < r; i++)
            {
                ng[i] = BlankRow(c, 0); int src = i + shift;
                if (src >= 0 && src < or) Array.Copy(g[src], ng[i], Math.Min(c, oc));
            }
            return ng;
        }
        void AddSb(Cell[] row) { Sb.Add(row); if (Sb.Count > SbMax + 256) { Sb.RemoveRange(0, 256); Trimmed += 256; } if (ViewOffset > 0) ViewOffset = Math.Min(Sb.Count, ViewOffset + 1); }

        public Cell[] LineAt(int viewRow)
        {
            int idx = Sb.Count - ViewOffset + viewRow; if (idx < 0) return null;
            if (idx < Sb.Count) return Sb[idx]; idx -= Sb.Count; return idx < Rows ? Grid[idx] : null;
        }
        public Cell[] AbsLine(int abs) { if (abs < 0) return null; if (abs < Sb.Count) return Sb[abs]; abs -= Sb.Count; return abs < Rows ? Grid[abs] : null; }

        // ---- bejövő bájtok
        public void Feed(byte[] b, int n)
        {
            char[] cs = new char[n + 8]; int cnt = dec.GetChars(b, 0, n, cs, 0);
            for (int i = 0; i < cnt; i++) Process(cs[i]);
            Dirty = true;
        }

        void Process(char c)
        {
            switch (st)
            {
                case St.Osc:
                    if (c == 0x07) { EndOsc(); st = St.Ground; } else if (c == 0x1B) st = St.OscEsc; else if (c == 0x18 || c == 0x1A) st = St.Ground; else if (osc.Length < 262144) osc.Append(c);
                    return;
                case St.OscEsc: if (c == '\\') EndOsc(); st = St.Ground; return;
                case St.Str: if (c == 0x1B) st = St.StrEsc; else if (c == 0x18 || c == 0x1A) st = St.Ground; return;
                case St.StrEsc: st = St.Ground; return;
                case St.EscInt: st = St.Ground; return;
            }
            if (c < 0x20)
            {
                switch ((int)c)
                {
                    case 0x1B: st = St.Esc; return;
                    case 0x18: case 0x1A: st = St.Ground; return;
                    case 0x07: if (Bell != null) Bell(); return;
                    case 0x08: if (X > 0) X--; WrapNext = false; return;
                    case 0x09: TabFwd(1); return;
                    case 0x0A: case 0x0B: case 0x0C: WrapNext = false; LineFeed(); return;
                    case 0x0D: X = 0; WrapNext = false; return;
                }
                return;
            }
            switch (st)
            {
                case St.Ground: Print(c); return;
                case St.Esc: EscCmd(c); return;
                case St.Csi: CsiChar(c); return;
            }
        }

        void EscCmd(char c)
        {
            st = St.Ground;
            switch (c)
            {
                case '[': st = St.Csi; np = 0; haveNum = false; cur = 0; priv = '\0'; nextSub = false; inter = '\0'; break;
                case ']': st = St.Osc; osc.Length = 0; break;
                case 'P': case 'X': case '^': case '_': st = St.Str; break;
                case '(': case ')': case '*': case '+': case '#': case '%': case ' ': st = St.EscInt; break;
                case '7': SaveCursor(); break;
                case '8': RestoreCursor(); break;
                case 'D': LineFeed(); break;
                case 'E': X = 0; LineFeed(); break;
                case 'M': RevIndex(); break;
                case 'H': if (X < tabs.Length) tabs[X] = true; break;
                case 'c': FullReset(); break;
            }
        }

        void CsiChar(char c)
        {
            if (c >= '0' && c <= '9') { cur = Math.Min(cur * 10 + (c - '0'), 1000000); haveNum = true; return; }
            if (c == ';' || c == ':') { if (np < pars.Length) { isSub[np] = nextSub; pars[np++] = haveNum ? cur : 0; } cur = 0; haveNum = false; nextSub = c == ':'; return; }
            if (c >= '<' && c <= '?') { priv = c; return; }
            if (c >= ' ' && c <= '/') { inter = c; return; }                // közbenső karakterek (pl. DECSCUSR szóköze) – a végző karakter dönt
            if (haveNum || np > 0) { if (np < pars.Length) { isSub[np] = nextSub; pars[np++] = haveNum ? cur : 0; } }
            st = St.Ground; ExecCsi(c); nextSub = false; inter = '\0';
        }

        int P(int i, int def) { return i < np && pars[i] > 0 ? pars[i] : def; }

        void ExecCsi(char f)
        {
            switch (f)
            {
                case 'A': { int n = P(0, 1); int lim = Y >= top ? top : 0; Y = Math.Max(lim, Y - n); WrapNext = false; break; }
                case 'B': case 'e': { int n = P(0, 1); int lim = Y <= bot ? bot : Rows - 1; Y = Math.Min(lim, Y + n); WrapNext = false; break; }
                case 'C': X = Math.Min(Cols - 1, X + P(0, 1)); WrapNext = false; break;
                case 'D': X = Math.Max(0, X - P(0, 1)); WrapNext = false; break;
                case 'E': Y = Math.Min(Rows - 1, Y + P(0, 1)); X = 0; WrapNext = false; break;
                case 'F': Y = Math.Max(0, Y - P(0, 1)); X = 0; WrapNext = false; break;
                case 'G': case '`': X = Math.Max(0, Math.Min(Cols - 1, P(0, 1) - 1)); WrapNext = false; break;
                case 'H': case 'f': { int r = P(0, 1) - 1, c = P(1, 1) - 1; if (Origin) { r += top; Y = Math.Min(bot, r); } else Y = Math.Min(Rows - 1, r); X = Math.Min(Cols - 1, c); WrapNext = false; break; }
                case 'd': { int r = P(0, 1) - 1; Y = Origin ? Math.Min(bot, r + top) : Math.Min(Rows - 1, r); WrapNext = false; break; }
                case 'J': EraseDisplay(np > 0 ? pars[0] : 0); break;
                case 'K': EraseLine(np > 0 ? pars[0] : 0); break;
                case 'L': InsertLines(P(0, 1)); break;
                case 'M': DeleteLines(P(0, 1)); break;
                case '@': InsertChars(P(0, 1)); break;
                case 'P': DeleteChars(P(0, 1)); break;
                case 'X': { int n = Math.Min(P(0, 1), Cols - X); Cell b = Blank(); for (int i = 0; i < n; i++) Grid[Y][X + i] = b; break; }
                case 'S': for (int i = 0, n = Math.Min(P(0, 1), bot - top + 1); i < n; i++) ScrollUp(); break;
                case 'T': for (int i = 0, n = Math.Min(P(0, 1), bot - top + 1); i < n; i++) ScrollDown(); break;
                case 'Z': TabBack(P(0, 1)); break;
                case 'g': { int m = np > 0 ? pars[0] : 0; if (m == 3) { for (int i = 0; i < tabs.Length; i++) tabs[i] = false; } else if (X < tabs.Length) tabs[X] = false; break; }
                case 'm': if (priv == '\0') Sgr(); break;
                case 'r': { if (priv != '\0' || inter != '\0') break; int t = P(0, 1) - 1, b = P(1, Rows) - 1; if (b > Rows - 1) b = Rows - 1; if (t < b) { top = t; bot = b; X = 0; Y = Origin ? top : 0; WrapNext = false; } break; }
                case 's': if (priv == '\0') SaveCursor(); break;
                case 'u': if (priv == '\0') RestoreCursor(); break;
                case 'h': SetMode(true); break;
                case 'l': SetMode(false); break;
                case 'n':
                    {
                        int m = np > 0 ? pars[0] : 0;
                        if (m == 5) Send("\x1b[0n"); else if (m == 6) Send("\x1b[" + ((Origin ? Y - top : Y) + 1) + ";" + (X + 1) + "R");
                        break;
                    }
                case 'c': if (priv == '>') Send("\x1b[>0;10;1c"); else if (priv == '\0') Send("\x1b[?1;2c"); break;
                case 'q': if (inter == ' ') { if (np == 0 || pars[0] == 0) { CursorShape = -1; CursorBlinkApp = false; } else { int n = pars[0]; CursorShape = Math.Min(2, (n - 1) / 2); CursorBlinkApp = (n % 2) == 1; } } break;
                case 'p': if (inter == '!') { /* DECSTR */ CurFg = CurBg = 0; CurA = 0; AutoWrap = true; Origin = false; Insert = false; top = 0; bot = Rows - 1; } break;
            }
        }

        void Send(string s) { if (Reply != null) Reply(s); }

        void SetMode(bool on)
        {
            for (int i = 0; i < np; i++)
            {
                int m = pars[i];
                if (priv == '?')
                {
                    switch (m)
                    {
                        case 1: AppCursor = on; break;
                        case 6: Origin = on; X = 0; Y = on ? top : 0; break;
                        case 7: AutoWrap = on; break;
                        case 25: CursorVisible = on; break;
                        case 12: CursorBlinkApp = on; break;
                        case 1000: case 1002: case 1003: if (on) MouseMode = m; else if (MouseMode == m) MouseMode = 0; break;
                        case 1006: MouseSgr = on; break;
                        case 1004: FocusEvents = on; break;
                        case 47: case 1047: if (on) EnterAlt(false); else ExitAlt(false); break;
                        case 1049: if (on) EnterAlt(true); else ExitAlt(true); break;
                        case 2004: BracketedPaste = on; break;
                    }
                }
                else if (m == 4) Insert = on;
            }
        }

        void EnterAlt(bool save)
        {
            if (Alt) return; if (save) SaveCursor();
            mainGrid = Grid; Grid = NewGrid(Cols, Rows); Alt = true; ViewOffset = 0; if (save) { X = 0; Y = 0; WrapNext = false; }
        }
        void ExitAlt(bool restore)
        {
            if (!Alt) return; Grid = mainGrid; mainGrid = null; Alt = false; if (restore) RestoreCursor(); WrapNext = false;
        }

        void SaveCursor() { savX = X; savY = Y; savFg = CurFg; savBg = CurBg; savA = CurA; savWrap = WrapNext; }
        void RestoreCursor() { X = Math.Min(savX, Cols - 1); Y = Math.Min(savY, Rows - 1); CurFg = savFg; CurBg = savBg; CurA = savA; WrapNext = savWrap; }

        void FullReset()
        {
            CursorShape = -1; MouseMode = 0; MouseSgr = false; FocusEvents = false; CurFg = CurBg = 0; CurA = 0; AppCursor = false; BracketedPaste = false; CursorVisible = true; AutoWrap = true; Origin = false; Insert = false;
            if (Alt) ExitAlt(false); top = 0; bot = Rows - 1; X = Y = 0; WrapNext = false; Grid = NewGrid(Cols, Rows); ResetTabs();
        }

        DateTime lastNote = DateTime.MinValue; bool NotifyOk() { DateTime n = DateTime.Now; if ((n - lastNote).TotalMilliseconds < 500) return false; lastNote = n; return true; }
        public bool AllowOsc52;
        void EndOsc()
        {
            string s = osc.ToString(); int i = s.IndexOf(';'); if (i <= 0) return; string n = s.Substring(0, i), rest = s.Substring(i + 1);
            if (n == "0" || n == "2") { Title = rest; if (TitleChanged != null) TitleChanged(); }
            else if (n == "7")        // munkamappa: file://gép/C:/mappa
            {
                string r = rest; if (r.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) { r = r.Substring(7); int sl = r.IndexOf('/'); if (sl >= 0) r = r.Substring(sl + 1); }
                try { r = Uri.UnescapeDataString(r).Replace('/', '\\'); } catch { } if (r.Length > 1 && r[1] == ':' && r.Length < 260 && r.IndexOfAny(new char[] { '"', '<', '>', '|', '*', '?', '\0', '\r', '\n', '\t' }) < 0) { Cwd = r; if (CwdChanged != null) CwdChanged(); }
            }
            else if (n == "9") { if (!System.Text.RegularExpressions.Regex.IsMatch(rest, @"^\d+(;|$)") && Notify != null && NotifyOk()) Notify(rest.Length > 300 ? rest.Substring(0, 300) : rest); }      // iTerm2-stílusú értesítés
            else if (n == "777") { string[] p = rest.Split(';'); if (p.Length >= 1 && p[0] == "preview") { if (PreviewRequest != null && NotifyOk()) PreviewRequest(p.Length > 1 && p[1].Length < 2000 ? p[1] : ""); } else if (p.Length >= 2 && p[0] == "notify" && Notify != null && NotifyOk()) Notify(p.Length > 2 ? p[1] + ": " + p[2] : p[1]); }
            else if (n == "52")       // vágólap írása (a lekérdezést nem támogatjuk)
            {
                int k = rest.LastIndexOf(';'); string b64 = k >= 0 ? rest.Substring(k + 1) : rest;
                if (AllowOsc52 && b64 != "?" && b64.Length > 0 && b64.Length < 200000 && ClipboardSet != null) { try { ClipboardSet(Encoding.UTF8.GetString(Convert.FromBase64String(b64))); } catch { } }
            }
            else if (n == "133") { string[] p = rest.Split(';'); if (p.Length > 0 && p[0] == "D") { int ec = 0; AppExit = p.Length > 1 && int.TryParse(p[1], out ec) ? ec : 0; AppExitAt = DateTime.Now; } }      // az előző parancs kilépési kódja
        }

        // ---- SGR
        void Sgr()
        {
            if (np == 0) { CurFg = CurBg = 0; CurA = 0; return; }
            for (int i = 0; i < np; i++)
            {
                if (isSub[i]) continue;                                   // az alparamétereket a szülő kód kezeli
                int p = pars[i];
                if ((p == 38 || p == 48) && i + 1 < np && isSub[i + 1])    // kettőspontos forma: 38:5:n  vagy  38:2:[színtér]:r:g:b
                {
                    int j = i + 1, cnt = 0; while (j + cnt < np && isSub[j + cnt]) cnt++;
                    int mode = pars[j], col = 0; bool ok = false;
                    if (mode == 5 && cnt >= 2) { col = PAL | (pars[j + 1] & 255); ok = true; }
                    else if (mode == 2 && cnt >= 4) { col = unchecked((int)0xFF000000) | ((pars[j + cnt - 3] & 255) << 16) | ((pars[j + cnt - 2] & 255) << 8) | (pars[j + cnt - 1] & 255); ok = true; }
                    if (ok) { if (p == 38) CurFg = col; else CurBg = col; }
                    i = j + cnt - 1; continue;
                }
                if (p == 58) { if (i + 1 < np && pars[i + 1] == 2) i += 4; else if (i + 1 < np && pars[i + 1] == 5) i += 2; continue; }   // aláhúzás-szín: nem támogatott, de ne zavarjon
                if (p == 0) { CurFg = CurBg = 0; CurA = 0; }
                else if (p == 1) CurA |= 1; else if (p == 2) CurA |= 2; else if (p == 3) CurA |= 4; else if (p == 4 || p == 21) CurA |= 8;
                else if (p == 7) CurA |= 16; else if (p == 8) CurA |= 64; else if (p == 9) CurA |= 32;
                else if (p == 22) CurA = (byte)(CurA & ~3); else if (p == 23) CurA = (byte)(CurA & ~4); else if (p == 24) CurA = (byte)(CurA & ~8);
                else if (p == 27) CurA = (byte)(CurA & ~16); else if (p == 28) CurA = (byte)(CurA & ~64); else if (p == 29) CurA = (byte)(CurA & ~32);
                else if (p >= 30 && p <= 37) CurFg = PAL | (p - 30);
                else if (p == 39) CurFg = 0;
                else if (p >= 40 && p <= 47) CurBg = PAL | (p - 40);
                else if (p == 49) CurBg = 0;
                else if (p >= 90 && p <= 97) CurFg = PAL | (p - 90 + 8);
                else if (p >= 100 && p <= 107) CurBg = PAL | (p - 100 + 8);
                else if (p == 38 || p == 48)
                {
                    int col = 0; bool ok = false;
                    if (i + 2 < np && pars[i + 1] == 5) { col = PAL | (pars[i + 2] & 255); i += 2; ok = true; }
                    else if (i + 4 < np && pars[i + 1] == 2) { col = unchecked((int)0xFF000000) | ((pars[i + 2] & 255) << 16) | ((pars[i + 3] & 255) << 8) | (pars[i + 4] & 255); i += 4; ok = true; }
                    if (ok) { if (p == 38) CurFg = col; else CurBg = col; }
                }
            }
        }

        // ---- szöveg és mozgás
        static int Width(char c)
        {
            if (c < 0x300) return 1;
            if (c <= 0x36F || (c >= 0x200B && c <= 0x200F) || (c >= 0xFE00 && c <= 0xFE0F) || (c >= 0x2060 && c <= 0x2064) || (c >= 0xDC00 && c <= 0xDFFF)) return 0;
            if ((c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) || (c >= 0xD800 && c <= 0xDBFF)) return 2;
            return 1;
        }

        Cell Blank() { Cell b = new Cell(); b.C = ' '; b.Bg = CurBg; return b; }

        int lastHiX = -1, lastHiY = -1;
        void Print(char c)
        {
            if (c >= 0xDC00 && c <= 0xDFFF)         // alsó félkód: az előző felső félkód (2 cellás) második cellájába kerül
            {
                if (lastHiY >= 0 && lastHiY < Rows && lastHiX + 1 < Cols) { Cell lo = Grid[lastHiY][lastHiX + 1]; lo.C = c; Grid[lastHiY][lastHiX + 1] = lo; }
                lastHiY = -1; return;
            }
            int w = Width(c); if (w == 0) return;
            if (WrapNext && AutoWrap) { X = 0; WrapNext = false; LineFeed(); }
            if (w == 2 && X >= Cols - 1) { if (AutoWrap) { Grid[Y][X] = Blank(); X = 0; LineFeed(); } else return; }
            if (Insert) InsertChars(w);
            Cell cl = new Cell(); cl.C = c; cl.Fg = CurFg; cl.Bg = CurBg; cl.A = CurA; Grid[Y][X] = cl; if (c >= 0xD800 && c <= 0xDBFF) { lastHiX = X; lastHiY = Y; }
            if (w == 2) { Cell c2 = cl; c2.C = '\0'; Grid[Y][X + 1] = c2; }
            X += w; if (X >= Cols) { X = Cols - 1; WrapNext = true; }
        }

        void LineFeed() { if (Y == bot) ScrollUp(); else if (Y < Rows - 1) Y++; }
        void RevIndex() { if (Y == top) ScrollDown(); else if (Y > 0) Y--; }

        void ScrollUp()
        {
            Cell[] removed = Grid[top]; for (int r = top; r < bot; r++) Grid[r] = Grid[r + 1]; Grid[bot] = BlankRow(Cols, CurBg);
            if (top == 0 && !Alt) AddSb(removed);
        }
        void ScrollDown() { for (int r = bot; r > top; r--) Grid[r] = Grid[r - 1]; Grid[top] = BlankRow(Cols, CurBg); }

        void InsertLines(int n) { if (Y < top || Y > bot) return; n = Math.Min(n, bot - Y + 1); for (int i = 0; i < n; i++) { for (int r = bot; r > Y; r--) Grid[r] = Grid[r - 1]; Grid[Y] = BlankRow(Cols, CurBg); } X = 0; WrapNext = false; }
        void DeleteLines(int n) { if (Y < top || Y > bot) return; n = Math.Min(n, bot - Y + 1); for (int i = 0; i < n; i++) { for (int r = Y; r < bot; r++) Grid[r] = Grid[r + 1]; Grid[bot] = BlankRow(Cols, CurBg); } X = 0; WrapNext = false; }
        void InsertChars(int n) { n = Math.Min(n, Cols - X); Cell[] row = Grid[Y]; for (int i = Cols - 1; i >= X + n; i--) row[i] = row[i - n]; Cell b = Blank(); for (int i = 0; i < n; i++) row[X + i] = b; }
        void DeleteChars(int n) { n = Math.Min(n, Cols - X); Cell[] row = Grid[Y]; for (int i = X; i < Cols - n; i++) row[i] = row[i + n]; Cell b = Blank(); for (int i = Cols - n; i < Cols; i++) row[i] = b; }

        void EraseLine(int m)
        {
            Cell b = Blank(); Cell[] row = Grid[Y]; int a = 0, z = Cols - 1;
            if (m == 0) a = X; else if (m == 1) z = X;
            for (int i = a; i <= z; i++) row[i] = b; if (m != 1) WrapNext = false;
        }
        void EraseDisplay(int m)
        {
            if (m == 3) { Trimmed += Sb.Count; Sb.Clear(); ViewOffset = 0; return; }
            Cell b = Blank();
            if (m == 0) { EraseLine(0); for (int r = Y + 1; r < Rows; r++) Grid[r] = BlankRow(Cols, CurBg); }
            else if (m == 1) { for (int r = 0; r < Y; r++) Grid[r] = BlankRow(Cols, CurBg); EraseLine(1); }
            else if (m == 2) { for (int r = 0; r < Rows; r++) Grid[r] = BlankRow(Cols, CurBg); }
        }

        void TabFwd(int n) { for (int k = 0; k < n; k++) { int x = X + 1; while (x < Cols - 1 && !tabs[x]) x++; X = Math.Min(x, Cols - 1); } WrapNext = false; }
        void TabBack(int n) { for (int k = 0; k < n; k++) { int x = X - 1; while (x > 0 && !tabs[x]) x--; X = Math.Max(x, 0); } WrapNext = false; }

        public static string LineText(Cell[] row, int a, int z)
        {
            if (row == null) return ""; StringBuilder sb = new StringBuilder(); z = Math.Min(z, row.Length - 1);
            for (int i = Math.Max(0, a); i <= z; i++) { char c = row[i].C; if (c == '\0') continue; sb.Append(c); }
            return sb.ToString().TrimEnd(' ');
        }
    }

    // ------------------------------------------------------------------------------------------ ConPTY
    public class ConPty : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out IntPtr r, out IntPtr w, IntPtr attrs, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(COORD size, IntPtr hIn, IntPtr hOut, uint flags, out IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr ret);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, byte[] buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, byte[] buf, int n, out int written, IntPtr ov);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")] static extern int WaitForSingleObject(IntPtr h, int ms);

        IntPtr hPC, inW, outR, hProc, attrList; Thread reader; readonly object ql = new object(); List<byte[]> queue = new List<byte[]>(); public volatile bool Exited; bool disposed;
        public string Error; public bool Started; Thread writer; readonly List<byte[]> wq = new List<byte[]>(); readonly AutoResetEvent wev = new AutoResetEvent(false);

        bool Fail(string err, params IntPtr[] extra)
        {
            Error = err; foreach (IntPtr h in extra) if (h != IntPtr.Zero) CloseHandle(h);
            if (hPC != IntPtr.Zero) { ClosePseudoConsole(hPC); hPC = IntPtr.Zero; }
            if (inW != IntPtr.Zero) { CloseHandle(inW); inW = IntPtr.Zero; } if (outR != IntPtr.Zero) { CloseHandle(outR); outR = IntPtr.Zero; }
            if (attrList != IntPtr.Zero) { DeleteProcThreadAttributeList(attrList); Marshal.FreeHGlobal(attrList); attrList = IntPtr.Zero; }
            return false;
        }

        public bool Start(string cmdLine, string cwd, int cols, int rows)
        {
            IntPtr inR, outW;
            outW = IntPtr.Zero; if (!CreatePipe(out inR, out inW, IntPtr.Zero, 0)) return Fail("pipe", inR); if (!CreatePipe(out outR, out outW, IntPtr.Zero, 0)) return Fail("pipe", inR, outW);
            COORD sz = new COORD(); sz.X = (short)cols; sz.Y = (short)rows;
            int hr = CreatePseudoConsole(sz, inR, outW, 0, out hPC); if (hr != 0) return Fail("CreatePseudoConsole 0x" + hr.ToString("X"), inR, outW);
            CloseHandle(inR); CloseHandle(outW);
            STARTUPINFOEX si = new STARTUPINFOEX(); si.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            IntPtr size = IntPtr.Zero; InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); attrList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size)) return Fail("InitAttr");
            if (!UpdateProcThreadAttribute(attrList, 0, (IntPtr)0x00020016, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) return Fail("UpdateAttr");
            si.lpAttributeList = attrList; PROCESS_INFORMATION pi;
            if (!CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, string.IsNullOrEmpty(cwd) ? null : cwd, ref si, out pi)) return Fail("CreateProcess err " + Marshal.GetLastWin32Error());
            hProc = pi.hProcess; CloseHandle(pi.hThread); Started = true;
            DeleteProcThreadAttributeList(attrList); Marshal.FreeHGlobal(attrList); attrList = IntPtr.Zero;
            reader = new Thread(ReadLoop); reader.IsBackground = true; reader.Start();
            writer = new Thread(WriteLoop); writer.IsBackground = true; writer.Start(); return true;
        }

        void ReadLoop()
        {
            byte[] buf = new byte[16384];
            while (true)
            {
                int n; if (!ReadFile(outR, buf, buf.Length, out n, IntPtr.Zero) || n <= 0) break;
                byte[] c = new byte[n]; Buffer.BlockCopy(buf, 0, c, 0, n); lock (ql) { while (qBytes > 4000000 && !disposed) Monitor.Wait(ql, 50); queue.Add(c); qBytes += n; }
            }
            Exited = true;
        }

        long qBytes;
        // legfeljebb ~max bájtot ad vissza hívásonként (a felület nem fagy le hatalmas kimenetnél); a többi a sorban marad
        public List<byte[]> Drain(int max) { lock (ql) { if (queue.Count == 0) return null; int i = 0; long got = 0; while (i < queue.Count && (got < max || i == 0)) { got += queue[i].Length; i++; } List<byte[]> l = queue.GetRange(0, i); queue.RemoveRange(0, i); qBytes -= got; Monitor.PulseAll(ql); return l; } }
        public List<byte[]> Drain() { return Drain(int.MaxValue); }

        public void Write(string s)
        {
            if (disposed || inW == IntPtr.Zero || s == null || s.Length == 0) return;
            byte[] b = new UTF8Encoding(false).GetBytes(s); lock (wq) wq.Add(b); wev.Set();        // külön szál ír: a beillesztés/gépelés sosem fagyasztja a felületet
        }

        void WriteLoop()
        {
            while (!disposed)
            {
                wev.WaitOne(400); List<byte[]> batch = null; lock (wq) { if (wq.Count > 0) { batch = new List<byte[]>(wq); wq.Clear(); } }
                if (batch != null) foreach (byte[] b in batch) { int w; IntPtr h = inW; if (h == IntPtr.Zero || disposed) break; WriteFile(h, b, b.Length, out w, IntPtr.Zero); }
            }
        }

        public void Resize(int cols, int rows) { if (hPC == IntPtr.Zero || disposed) return; COORD sz = new COORD(); sz.X = (short)Math.Max(2, cols); sz.Y = (short)Math.Max(1, rows); ResizePseudoConsole(hPC, sz); }

        public bool Alive { get { return hProc != IntPtr.Zero && !disposed && WaitForSingleObject(hProc, 0) == 0x102; } }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { if (hProc != IntPtr.Zero) TerminateProcess(hProc, 0); } catch { }
            IntPtr pc = hPC, ow = inW, orr = outR, hp = hProc; hPC = IntPtr.Zero;
            Thread t = new Thread(delegate ()
            {
                try { if (pc != IntPtr.Zero) ClosePseudoConsole(pc); } catch { }
                try { if (reader != null) reader.Join(800); } catch { }
                try { if (ow != IntPtr.Zero) CloseHandle(ow); } catch { }
                try { if (orr != IntPtr.Zero) CloseHandle(orr); } catch { }
                try { if (hp != IntPtr.Zero) CloseHandle(hp); } catch { }
            });
            t.IsBackground = true; t.Start();
        }
    }
}
