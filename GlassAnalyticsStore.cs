// GlassControl – analitika tár: kétnaponkénti napló-fájlok, heti rövid összegzők, havi összegzők és a "memory" (tanult tudás).
//
//   analytics\
//     1-2_day\  3-4_day\  5-6-7_day\   ← minden ciklus-szakasz egy txt-je (részletes tartalom + átlagok)
//     summa_month\
//         summa_1 .. summa_4\          ← heti mappák, bennük egy txt a 3 szakasz rövid, tömör összegzőjével
//         mont_1\ mont_2\ ...          ← 4 hét után: a heti összegzők összegzése (ezek maradnak); a summa_1..4 törlődik
//         months.txt                   ← az összes hónap egy nagy txt-ben
//     memory\memory.txt                ← egy nagy, soha nem törlődő txt: a tanult tudás (szakaszokra bontva)
//     raw\minutes.csv, raw\wiper.csv   ← percenkénti nyers mérés (35 napig)
//
// Naponta egyszer minden txt-t beolvas és tanul belőlük (memory.txt). A memory-ból dolgozik az okos seprés.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GlassControlWpf
{
    public class ProgProfile
    {
        public string Name; public double AvgMb, MaxMb, TrendMbH, FreedSumMb; public long PresentMin, FgMin; public int Sessions, SweepCount; public double[] StartHours = new double[24], ActiveMin = new double[24], FgMinH = new double[24];
        public DateTime LastSeen;
        public double AvgSessionMin { get { return Sessions > 0 ? (double)PresentMin / Sessions : 0; } }
        public double FgShare { get { return PresentMin > 0 ? (double)FgMin / PresentMin : 0; } }
        public double AvgFreedMb { get { return SweepCount > 0 ? FreedSumMb / SweepCount : 0; } }
    }

    public class MemModel
    {
        public int Days; public long Minutes; public double AvgPct, MaxPct; public double[] HourAvg = new double[24], HourN = new double[24], WeekdayAvg = new double[7], WeekdayN = new double[7];
        public Dictionary<string, ProgProfile> Progs = new Dictionary<string, ProgProfile>(); public HashSet<string> Processed = new HashSet<string>(); public DateTime Updated;
        public double HourPct(int h) { return HourN[h] > 0 ? HourAvg[h] : AvgPct; }
    }

    class RawMin { public DateTime T; public double Pct, UsedGb, TotalGb; public string Fg; public List<KeyValuePair<string, double>> Top = new List<KeyValuePair<string, double>>(); }
    class RawWipe { public DateTime T; public int Count; public double FreedMb, Before, After; public List<KeyValuePair<string, double>> Top = new List<KeyValuePair<string, double>>(); }

    public class AnalyticsStore
    {
        public string Root, RawDir, RawFile, WipeFile, MemoryDir, MemoryFile, MonthDir, StateFile; public MemModel Model = new MemModel();
        static readonly string[] ChunkNames = { "1-2_day", "3-4_day", "5-6-7_day" }; static readonly int[] CS = { 0, 2, 4 }, CE = { 1, 3, 6 };
        static readonly CultureInfo IC = CultureInfo.InvariantCulture; readonly object io = new object(); Dictionary<string, string> state = new Dictionary<string, string>();

        public AnalyticsStore(string appDir)
        {
            Root = Path.Combine(appDir, "analytics"); RawDir = Path.Combine(Root, "raw"); RawFile = Path.Combine(RawDir, "minutes.csv"); WipeFile = Path.Combine(RawDir, "wiper.csv");
            MemoryDir = Path.Combine(Root, "memory"); MemoryFile = Path.Combine(MemoryDir, "memory.txt"); MonthDir = Path.Combine(Root, "summa_month"); StateFile = Path.Combine(Root, "state.ini");
            try { Directory.CreateDirectory(RawDir); Directory.CreateDirectory(MemoryDir); Directory.CreateDirectory(MonthDir); foreach (string c in ChunkNames) Directory.CreateDirectory(Path.Combine(Root, c)); } catch { }
            // régi helyről áthozás (egyszeri)
            try { string o1 = Path.Combine(appDir, "analytics-log.csv"), o2 = Path.Combine(appDir, "analytics-wiper.csv"); if (File.Exists(o1) && !File.Exists(RawFile)) File.Move(o1, RawFile); if (File.Exists(o2) && !File.Exists(WipeFile)) File.Move(o2, WipeFile); } catch { }
            LoadState(); if (!state.ContainsKey("cycleStart")) { state["cycleStart"] = DateTime.Today.ToString("yyyy-MM-dd", IC); SaveState(); }
            LoadModel();
        }

        void LoadState() { state.Clear(); try { if (File.Exists(StateFile)) foreach (string l in File.ReadAllLines(StateFile)) { int i = l.IndexOf('='); if (i > 0) state[l.Substring(0, i)] = l.Substring(i + 1); } } catch { } }
        void SaveState() { try { List<string> l = new List<string>(); foreach (KeyValuePair<string, string> kv in state) l.Add(kv.Key + "=" + kv.Value); WriteAtomic(StateFile, string.Join(Environment.NewLine, l.ToArray()) + Environment.NewLine, false); } catch { } }
        DateTime CycleStart { get { DateTime d; return DateTime.TryParseExact(state["cycleStart"], "yyyy-MM-dd", IC, DateTimeStyles.None, out d) ? d : DateTime.Today; } }
        int StateInt(string k, int def) { string s; int v; return state.TryGetValue(k, out s) && int.TryParse(s, out v) ? v : def; }

        // ---------------------------------------------------------------- nyers napló
        public void AppendMinute(DateTime t, double pct, double usedGb, double totalGb, string fg, string top)
        {
            try { lock (io) File.AppendAllText(RawFile, t.ToString("yyyy-MM-dd HH:mm", IC) + "," + pct.ToString("0.0", IC) + "," + usedGb.ToString("0.00", IC) + "," + totalGb.ToString("0.0", IC) + "," + top + "," + fg + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        public void AppendWipe(DateTime t, int count, double freedMb, double before, double after, string top)
        {
            try { lock (io) File.AppendAllText(WipeFile, t.ToString("yyyy-MM-dd HH:mm:ss", IC) + "," + count + "," + freedMb.ToString("0.0", IC) + "," + before.ToString("0.00", IC) + "," + after.ToString("0.00", IC) + "," + top + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        // atomi írás: ideiglenes fájl, majd csere (összeomlás/áramszünet esetén sem marad félbehagyott fájl)
        public static string Clean(string s) { return (s ?? "").Replace(',', '_').Replace('|', '_').Replace(':', '_').Replace(';', '_').Replace('=', '_').Replace('#', '_').Replace('\r', ' ').Replace('\n', ' '); }
        public static void WriteAtomic(string path, string text) { WriteAtomic(path, text, true); }
        public static void WriteAtomic(string path, string text, bool bom)
        {
            string dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp"; File.WriteAllText(tmp, text, new UTF8Encoding(bom));
            for (int i = 0; i < 6; i++)      // ha egy másik program épp olvassa a fájlt, röviden várunk és újrapróbáljuk
            {
                try { if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); return; }
                catch (IOException) { System.Threading.Thread.Sleep(40); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(40); }
            }
            File.Copy(tmp, path, true); try { File.Delete(tmp); } catch { }
        }

        // 35 napnál régebbi nyers sorok törlése (naponta egyszer; a percnapló és a seprés-napló is)
        DateTime lastTrim = DateTime.MinValue;
        public void TrimRaw()
        {
            if ((DateTime.Now - lastTrim).TotalHours < 20) return; lastTrim = DateTime.Now;
            try { lock (io) { DateTime cut = DateTime.Now.AddDays(-35); TrimFile(RawFile, cut, "yyyy-MM-dd HH:mm", 16); TrimFile(WipeFile, cut, "yyyy-MM-dd HH:mm:ss", 19); } } catch { }
        }
        void TrimFile(string f, DateTime cut, string fmt, int len)
        {
            if (!File.Exists(f)) return; List<string> keep = new List<string>(); bool ch = false;
            foreach (string l in File.ReadAllLines(f, Encoding.UTF8)) { DateTime t; if (l.Length >= len && DateTime.TryParseExact(l.Substring(0, len), fmt, IC, DateTimeStyles.None, out t) && t < cut) { ch = true; continue; } keep.Add(l); }
            if (ch) WriteAtomic(f, string.Join(Environment.NewLine, keep.ToArray()) + Environment.NewLine);
        }

        static List<KeyValuePair<string, double>> ParseTop(string s)
        {
            List<KeyValuePair<string, double>> r = new List<KeyValuePair<string, double>>(); if (string.IsNullOrEmpty(s)) return r;
            foreach (string p in s.Split('|')) { int c = p.LastIndexOf(':'); double mb; if (c > 0 && double.TryParse(p.Substring(c + 1), NumberStyles.Float, IC, out mb)) r.Add(new KeyValuePair<string, double>(p.Substring(0, c), mb)); }
            return r;
        }

        List<RawMin> ReadRaw(DateTime from, DateTime to)
        {
            List<RawMin> l = new List<RawMin>(); try
            {
                if (!File.Exists(RawFile)) return l;
                foreach (string line in File.ReadAllLines(RawFile, Encoding.UTF8))
                {
                    string[] p = line.Split(','); DateTime t; if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", IC, DateTimeStyles.None, out t) || t < from || t >= to) continue;
                    RawMin m = new RawMin { T = t, Fg = p.Length > 5 ? p[5].Trim() : "" }; double d; if (!double.TryParse(p[1], NumberStyles.Float, IC, out d)) continue; m.Pct = d; double.TryParse(p[2], NumberStyles.Float, IC, out m.UsedGb); double.TryParse(p[3], NumberStyles.Float, IC, out m.TotalGb); m.Top = ParseTop(p[4]); l.Add(m);
                }
            }
            catch { }
            return l;
        }
        List<RawWipe> ReadWipes(DateTime from, DateTime to)
        {
            List<RawWipe> l = new List<RawWipe>(); try
            {
                if (!File.Exists(WipeFile)) return l;
                foreach (string line in File.ReadAllLines(WipeFile, Encoding.UTF8))
                {
                    string[] p = line.Split(','); DateTime t; int c; double f, b, a; if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", IC, DateTimeStyles.None, out t) || t < from || t >= to) continue;
                    if (int.TryParse(p[1], out c) && double.TryParse(p[2], NumberStyles.Float, IC, out f) && double.TryParse(p[3], NumberStyles.Float, IC, out b) && double.TryParse(p[4], NumberStyles.Float, IC, out a)) l.Add(new RawWipe { T = t, Count = c, FreedMb = f, Before = b, After = a, Top = p.Length > 5 ? ParseTop(p[5]) : new List<KeyValuePair<string, double>>() });
                }
            }
            catch { }
            return l;
        }

        // ---------------------------------------------------------------- karbantartás: szakaszok, összegzők, hónap, tanulás
        public string Maintain()
        {
            lock (io)
            {
                StringBuilder log = new StringBuilder(); DateTime cs = CycleStart; int todayIdx = (DateTime.Today - cs).Days; if (todayIdx < 0) { state["cycleStart"] = DateTime.Today.ToString("yyyy-MM-dd", IC); state["chunksDone"] = "0"; SaveState(); cs = DateTime.Today; todayIdx = 0; }
                // a legutóbbi lezárult szakasz sorszáma
                int y = todayIdx - 1, latest = -1; if (y >= 0) { int gw = y / 7, dim = y % 7, c = dim < 2 ? 0 : (dim < 4 ? 1 : 2); latest = (dim == CE[c]) ? gw * 3 + c : gw * 3 + c - 1; }
                int done = StateInt("chunksDone", 0);
                for (int o = done; o <= latest; o++) { try { BuildChunk(o, cs); log.Append(Loc.T("szakasz kész: ")).Append(o).Append("; "); } catch (Exception ex) { log.Append(Loc.T("hiba: ")).Append(ex.Message); break; } done = o + 1; state["chunksDone"] = done.ToString(); SaveState(); }       // hiba esetén legközelebb újrapróbáljuk
                string today = DateTime.Today.ToString("yyyy-MM-dd", IC);
                if (!state.ContainsKey("lastLearn") || state["lastLearn"] != today || latest >= 0 && !File.Exists(MemoryFile)) { try { Learn(); state["lastLearn"] = today; SaveState(); log.Append(Loc.T("tanulás kész; ")); } catch (Exception ex) { log.Append(Loc.T("tanulási hiba: ")).Append(ex.Message); } }
                return log.ToString();
            }
        }

        string ChunkFile(int o, DateTime cs)
        {
            int gw = o / 3, c = o % 3; DateTime a = cs.AddDays(gw * 7 + CS[c]), b = cs.AddDays(gw * 7 + CE[c]);
            return Path.Combine(Root, ChunkNames[c], "W" + (gw + 1).ToString("00") + "_" + a.ToString("yyyy-MM-dd") + "_" + b.ToString("yyyy-MM-dd") + ".txt");
        }

        class PAgg { public double SumMb, MaxMb, TrendNum; public int N, Sessions, FgMin; public DateTime MaxAt, LastT = DateTime.MinValue; public double[] Start = new double[24], ActiveH = new double[24], FgH = new double[24]; public List<double> Series = new List<double>(); public double SessionMin; }

        void BuildChunk(int o, DateTime cs)
        {
            int gw = o / 3, c = o % 3; DateTime a = cs.AddDays(gw * 7 + CS[c]), b = cs.AddDays(gw * 7 + CE[c]), end = b.AddDays(1);
            string file = ChunkFile(o, cs); List<RawMin> mins = ReadRaw(a, end); List<RawWipe> wipes = ReadWipes(a, end); int totalMinutes = (int)(end - a).TotalMinutes;
            StringBuilder sb = new StringBuilder(); string wim = ((gw % 4) + 1).ToString(), month = (gw / 4 + 1).ToString();
            sb.AppendLine("=== ANALITIKA SZAKASZ ===").AppendLine("chunk=" + ChunkNames[c]).AppendLine("week=" + (gw + 1) + " weekInMonth=" + wim + " month=" + month).AppendLine("from=" + a.ToString("yyyy-MM-dd") + " to=" + b.ToString("yyyy-MM-dd"));
            string shortLine;
            if (mins.Count == 0)
            {
                sb.AppendLine("minutes=0").AppendLine(Loc.T("megjegyzes=Nincs mérés ebben az időszakban (a GlassControl nem futott)."));
                shortLine = "[" + ChunkNames[c] + "] " + a.ToString("MM.dd") + "–" + b.ToString("MM.dd") + Loc.T(" | nincs mérés"); WriteWeekLine(gw, c, shortLine + " ## none=1"); WriteAtomic(file, sb.ToString(), true); AfterChunk(gw, c); return;
            }
            double sum = 0, mx = 0, mn = 999; DateTime mxAt = a, mnAt = a; double[] hs = new double[24], hn = new double[24], ws = new double[7], wn = new double[7]; Dictionary<string, PAgg> pa = new Dictionary<string, PAgg>(); Dictionary<string, int> fgm = new Dictionary<string, int>();
            foreach (RawMin m in mins)
            {
                sum += m.Pct; if (m.Pct >= mx) { mx = m.Pct; mxAt = m.T; } if (m.Pct < mn) { mn = m.Pct; mnAt = m.T; } hs[m.T.Hour] += m.Pct; hn[m.T.Hour]++; int wd = ((int)m.T.DayOfWeek + 6) % 7; ws[wd] += m.Pct; wn[wd]++;
                foreach (KeyValuePair<string, double> kv in m.Top)
                {
                    PAgg g; if (!pa.TryGetValue(kv.Key, out g)) { g = new PAgg(); pa[kv.Key] = g; }
                    if (g.LastT == DateTime.MinValue || (m.T - g.LastT).TotalMinutes > 5) { g.Sessions++; g.Start[m.T.Hour]++; }          // új munkamenet, ha 5 percnél hosszabb szünet volt
                    g.SessionMin += g.LastT == DateTime.MinValue || (m.T - g.LastT).TotalMinutes > 5 ? 1 : Math.Max(1, (m.T - g.LastT).TotalMinutes);
                    g.LastT = m.T; g.SumMb += kv.Value; g.N++; g.ActiveH[m.T.Hour]++; g.Series.Add(kv.Value); if (kv.Value >= g.MaxMb) { g.MaxMb = kv.Value; g.MaxAt = m.T; }
                    if (m.Fg == kv.Key) { g.FgMin++; g.FgH[m.T.Hour]++; }
                }
            }
            double avg = sum / mins.Count; double cov = 100.0 * mins.Count / totalMinutes;
            sb.AppendLine("minutes=" + mins.Count + " coverage=" + cov.ToString("0.0", IC));
            sb.AppendLine("sys_avg=" + avg.ToString("0.0", IC) + " sys_max=" + mx.ToString("0.0", IC) + " sys_max_at=" + mxAt.ToString("yyyy-MM-dd HH:mm") + " sys_min=" + mn.ToString("0.0", IC) + " sys_min_at=" + mnAt.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("=== HOURS ===").AppendLine("# hour avg_pct minutes");
            for (int h = 0; h < 24; h++) if (hn[h] > 0) sb.AppendLine("h|" + h + "|" + (hs[h] / hn[h]).ToString("0.0", IC) + "|" + hn[h]);
            sb.AppendLine("=== WEEKDAYS ===").AppendLine(Loc.T("# weekday(0=hétfő) avg_pct minutes"));
            for (int d = 0; d < 7; d++) if (wn[d] > 0) sb.AppendLine("w|" + d + "|" + (ws[d] / wn[d]).ToString("0.0", IC) + "|" + wn[d]);
            sb.AppendLine("=== PROGRAMS ===").AppendLine(Loc.T("# p|név|átlagMB|csúcsMB|csúcs ideje|jelen_perc|munkamenet|átlag_munkamenet_perc|előtérben_perc|trend_MB_óra|indulási órák(óra:db)|aktív órák(óra:perc)|előtér órák(óra:perc)"));
            List<KeyValuePair<string, PAgg>> pl = new List<KeyValuePair<string, PAgg>>(pa); pl.Sort(delegate (KeyValuePair<string, PAgg> x, KeyValuePair<string, PAgg> y) { return (y.Value.SumMb / y.Value.N * Math.Sqrt(y.Value.N)).CompareTo(x.Value.SumMb / x.Value.N * Math.Sqrt(x.Value.N)); });
            foreach (KeyValuePair<string, PAgg> kv in pl)
            {
                PAgg g = kv.Value; double pavg = g.SumMb / g.N, trend = 0; if (g.Series.Count >= 30) { int k = Math.Max(5, g.Series.Count / 5); double s1 = 0, s2 = 0; for (int i = 0; i < k; i++) { s1 += g.Series[i]; s2 += g.Series[g.Series.Count - 1 - i]; } trend = (s2 - s1) / k / Math.Max(0.1, g.Series.Count * 0.8 / 60.0); }
                sb.AppendLine("p|" + kv.Key + "|" + pavg.ToString("0", IC) + "|" + g.MaxMb.ToString("0", IC) + "|" + g.MaxAt.ToString("MM-dd HH:mm") + "|" + g.N + "|" + g.Sessions + "|" + (g.Sessions > 0 ? g.SessionMin / g.Sessions : 0).ToString("0", IC) + "|" + g.FgMin + "|" + trend.ToString("0", IC) + "|" + Hist(g.Start) + "|" + Hist(g.ActiveH) + "|" + Hist(g.FgH));
            }
            sb.AppendLine("=== WIPER ===");
            double fsum = 0; Dictionary<string, double[]> pf = new Dictionary<string, double[]>(); foreach (RawWipe w in wipes) { fsum += w.FreedMb; sb.AppendLine("s|" + w.T.ToString("yyyy-MM-dd HH:mm:ss") + "|" + w.Count + "|" + w.FreedMb.ToString("0", IC) + "|" + w.Before.ToString("0.0", IC) + "|" + w.After.ToString("0.0", IC)); foreach (KeyValuePair<string, double> t in w.Top) { double[] f; if (!pf.TryGetValue(t.Key, out f)) { f = new double[2]; pf[t.Key] = f; } f[0] += t.Value; f[1]++; } }
            foreach (KeyValuePair<string, double[]> kv in pf) sb.AppendLine("f|" + kv.Key + "|" + kv.Value[0].ToString("0", IC) + "|" + kv.Value[1].ToString("0", IC));
            sb.AppendLine("sweeps=" + wipes.Count + " freed_mb=" + fsum.ToString("0", IC));
            // rövid összefoglaló (olvasható + gépi rész)
            StringBuilder topTxt = new StringBuilder(), topMach = new StringBuilder(); int shown = 0;
            foreach (KeyValuePair<string, PAgg> kv in pl) { PAgg g = kv.Value; if (shown >= 5) break; double hrs = g.N / 60.0, pavg = g.SumMb / g.N; if (topTxt.Length > 0) { topTxt.Append(", "); topMach.Append('|'); } topTxt.Append(kv.Key).Append(' ').Append(pavg >= 1024 ? (pavg / 1024).ToString("0.0", IC) + "GB" : pavg.ToString("0", IC) + "MB").Append('/').Append(hrs.ToString("0.#", IC)).Append(Loc.T("ó")); topMach.Append(kv.Key).Append(':').Append(pavg.ToString("0", IC)).Append(':').Append(hrs.ToString("0.0", IC)); shown++; }
            shortLine = "[" + ChunkNames[c] + "] " + a.ToString("MM.dd") + "–" + b.ToString("MM.dd") + Loc.T(" | átlag ") + avg.ToString("0", IC) + Loc.T("% csúcs ") + mx.ToString("0", IC) + "% (" + mxAt.ToString("HH:mm") + ") | " + topTxt + " | wiper " + wipes.Count + "×/" + (fsum >= 1024 ? (fsum / 1024).ToString("0.0", IC) + "GB" : fsum.ToString("0", IC) + "MB") + Loc.T(" | mérés ") + cov.ToString("0", IC) + "%";
            sb.AppendLine("=== SUMMARY ===").AppendLine(shortLine);
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            WriteWeekLine(gw, c, shortLine + " ## avg=" + avg.ToString("0.0", IC) + ";max=" + mx.ToString("0", IC) + ";min=" + mn.ToString("0", IC) + ";wipe=" + wipes.Count + "," + fsum.ToString("0", IC) + ";cov=" + cov.ToString("0", IC) + ";n=" + mins.Count + ";top=" + topMach);
            AfterChunk(gw, c);
        }

        static string Hist(double[] h) { StringBuilder s = new StringBuilder(); for (int i = 0; i < 24; i++) if (h[i] > 0) { if (s.Length > 0) s.Append(';'); s.Append(i).Append(':').Append(h[i].ToString("0", IC)); } return s.ToString(); }

        // a heti összegző txt: egy szakasz = egy sor (újraírva, ha már volt)
        void WriteWeekLine(int gw, int c, string line)
        {
            string wim = ((gw % 4) + 1).ToString(), dir = Path.Combine(MonthDir, "summa_" + wim), f = Path.Combine(dir, "summa_" + wim + ".txt"); Directory.CreateDirectory(dir);
            List<string> lines = new List<string>(); if (File.Exists(f)) lines.AddRange(File.ReadAllLines(f, Encoding.UTF8)); if (lines.Count == 0) { lines.Add("SUMMA – " + (gw + 1) + Loc.T(". hét (a hónap ") + wim + ". hete)"); }
            string key = "[" + ChunkNames[c] + "]"; lines.RemoveAll(delegate (string s) { return s.StartsWith(key); }); lines.Add(line);
            // rendezés: fejléc, majd a szakaszok sorrendben
            List<string> sorted = new List<string>(); sorted.Add(lines[0]); foreach (string n in ChunkNames) foreach (string s in lines) if (s.StartsWith("[" + n + "]")) sorted.Add(s);
            WriteAtomic(f, string.Join(Environment.NewLine, sorted.ToArray()) + Environment.NewLine, true);
        }

        void AfterChunk(int gw, int c) { if (c == 2 && gw % 4 == 3) { try { MonthRollup(gw / 4 + 1); } catch { } } }

        // 4 hét után: a summa_1..4 összegzése a mont_N-be, utána a summa mappák törlése
        void MonthRollup(int m)
        {
            Dictionary<string, double[]> prog = new Dictionary<string, double[]>(); double avgSum = 0, mx = 0; int avgN = 0, wipes = 0; double freed = 0; List<string> weeks = new List<string>();
            for (int w = 1; w <= 4; w++)
            {
                string f = Path.Combine(MonthDir, "summa_" + w, "summa_" + w + ".txt"); if (!File.Exists(f)) continue; weeks.Add("--- summa_" + w + " ---"); weeks.AddRange(File.ReadAllLines(f, Encoding.UTF8));
                foreach (string l in File.ReadAllLines(f, Encoding.UTF8))
                {
                    int i = l.IndexOf("##"); if (i < 0) continue; Dictionary<string, string> kv = new Dictionary<string, string>(); foreach (string p in l.Substring(i + 2).Trim().Split(';')) { int e = p.IndexOf('='); if (e > 0) kv[p.Substring(0, e)] = p.Substring(e + 1); }
                    double d; double wgt = 1; if (kv.ContainsKey("n")) double.TryParse(kv["n"], NumberStyles.Float, IC, out wgt); if (wgt <= 0) wgt = 1;
                    if (kv.ContainsKey("avg") && double.TryParse(kv["avg"], NumberStyles.Float, IC, out d)) { avgSum += d * wgt; avgN += (int)wgt; } if (kv.ContainsKey("max") && double.TryParse(kv["max"], NumberStyles.Float, IC, out d)) mx = Math.Max(mx, d);
                    if (kv.ContainsKey("wipe")) { string[] wp = kv["wipe"].Split(','); int wc; double wf; if (wp.Length == 2 && int.TryParse(wp[0], out wc) && double.TryParse(wp[1], NumberStyles.Float, IC, out wf)) { wipes += wc; freed += wf; } }
                    if (kv.ContainsKey("top")) foreach (string t in kv["top"].Split('|')) { string[] tp = t.Split(':'); double mb, hrs; if (tp.Length == 3 && double.TryParse(tp[1], NumberStyles.Float, IC, out mb) && double.TryParse(tp[2], NumberStyles.Float, IC, out hrs)) { double[] a; if (!prog.TryGetValue(tp[0], out a)) { a = new double[2]; prog[tp[0]] = a; } a[0] += mb * hrs; a[1] += hrs; } }
                }
            }
            StringBuilder sb = new StringBuilder(); double overall = avgN > 0 ? avgSum / avgN : 0;
            sb.AppendLine("MONT_" + m + Loc.T(" – hónap összegzés (4 hét)")).AppendLine(Loc.T("készült: ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine(Loc.T("memória átlag: ") + overall.ToString("0.0", IC) + Loc.T("%  csúcs: ") + mx.ToString("0", IC) + "%").AppendLine("wiper: " + wipes + Loc.T(" tisztítás, ") + freed.ToString("0", IC) + Loc.T(" MB felszabadítva"));
            List<KeyValuePair<string, double[]>> pl = new List<KeyValuePair<string, double[]>>(prog); pl.Sort(delegate (KeyValuePair<string, double[]> x, KeyValuePair<string, double[]> y) { return y.Value[1].CompareTo(x.Value[1]); });
            sb.AppendLine(Loc.T("legtöbbet futó/fogyasztó programok (átlag MB, összes futásidő órában):")); for (int i = 0; i < pl.Count && i < 12; i++) sb.AppendLine("  " + pl[i].Key + ": " + (pl[i].Value[0] / Math.Max(0.01, pl[i].Value[1])).ToString("0", IC) + " MB, " + pl[i].Value[1].ToString("0.0", IC) + Loc.T(" óra"));
            // összevetés az előző hónappal
            double prev = double.NaN, pv; string pm = Path.Combine(MonthDir, "months.txt"); if (File.Exists(pm)) { foreach (string l in File.ReadAllLines(pm, Encoding.UTF8)) { if (l.StartsWith(Loc.T("memória átlag: ")) && double.TryParse(l.Substring(15).Split('%')[0].Trim(), NumberStyles.Float, IC, out pv)) prev = pv; } }
            if (!double.IsNaN(prev)) sb.AppendLine(Loc.T("előző hónaphoz képest: ") + (overall - prev >= 0 ? "+" : "") + (overall - prev).ToString("0.0", IC) + Loc.T(" százalékpont (előző hónap: ") + prev.ToString("0.0", IC) + "%)");
            sb.AppendLine().AppendLine(Loc.T("A 4 hét heti összegzői:")); foreach (string w in weeks) sb.AppendLine(w);
            string md = Path.Combine(MonthDir, "mont_" + m); Directory.CreateDirectory(md); File.WriteAllText(Path.Combine(md, "mont_" + m + ".txt"), sb.ToString(), Encoding.UTF8);
            File.AppendAllText(pm, "=== mont_" + m + " ===" + Environment.NewLine + sb + Environment.NewLine, Encoding.UTF8);
            for (int w = 1; w <= 4; w++) { string d = Path.Combine(MonthDir, "summa_" + w); try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }
        }

        // ---------------------------------------------------------------- tanulás: minden txt beolvasása -> memory.txt
        public void Learn() { lock (io) { LearnLocked(); } }
        void LearnLocked()
        {
            MemModel mo = Model; List<string> files = new List<string>(); foreach (string c in ChunkNames) { string d = Path.Combine(Root, c); if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.txt")); }
            files.Sort(StringComparer.OrdinalIgnoreCase); int newFiles = 0;
            foreach (string f in files)
            {
                string key = Path.GetFileName(Path.GetDirectoryName(f)) + "/" + Path.GetFileName(f); if (mo.Processed.Contains(key)) continue; try { MergeChunk(mo, f); mo.Processed.Add(key); newFiles++; } catch { }
            }
            mo.Updated = DateTime.Now; WriteModel(mo, newFiles);
        }

        void MergeChunk(MemModel mo, string file)
        {
            string section = ""; int minutes = 0, span = 2; double sysAvg = 0, sysMax = 0; DateTime from = DateTime.MinValue;
            double[] hAvg = new double[24], hN = new double[24], wAvg = new double[7], wN = new double[7]; List<string[]> progs = new List<string[]>(), freed = new List<string[]>();
            foreach (string l in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (l.StartsWith("=== ")) { section = l; continue; }
                if (l.StartsWith("from=")) { DateTime.TryParseExact(l.Substring(5, 10), "yyyy-MM-dd", IC, DateTimeStyles.None, out from); int ti = l.IndexOf(" to="); DateTime tt; if (ti > 0 && DateTime.TryParseExact(l.Substring(ti + 4, 10), "yyyy-MM-dd", IC, DateTimeStyles.None, out tt)) span = (tt - from).Days + 1; continue; }
                if (l.StartsWith("minutes=")) { string m = l.Substring(8).Split(' ')[0]; int.TryParse(m, out minutes); continue; }
                if (l.StartsWith("sys_avg=")) { foreach (string p in l.Split(' ')) { int e = p.IndexOf('='); if (e < 0) continue; double d; if (p.StartsWith("sys_avg=") && double.TryParse(p.Substring(e + 1), NumberStyles.Float, IC, out d)) sysAvg = d; if (p.StartsWith("sys_max=") && double.TryParse(p.Substring(e + 1), NumberStyles.Float, IC, out d)) sysMax = d; } continue; }
                string[] t = l.Split('|'); double v, n; int idx;
                if (l.StartsWith("h|") && t.Length >= 4 && int.TryParse(t[1], out idx) && double.TryParse(t[2], NumberStyles.Float, IC, out v) && double.TryParse(t[3], NumberStyles.Float, IC, out n) && idx >= 0 && idx < 24) { hAvg[idx] = v; hN[idx] = n; }
                else if (l.StartsWith("w|") && t.Length >= 4 && int.TryParse(t[1], out idx) && double.TryParse(t[2], NumberStyles.Float, IC, out v) && double.TryParse(t[3], NumberStyles.Float, IC, out n) && idx >= 0 && idx < 7) { wAvg[idx] = v; wN[idx] = n; }
                else if (l.StartsWith("p|") && t.Length >= 13) progs.Add(t);
                else if (l.StartsWith("f|") && t.Length >= 4) freed.Add(t);
            }
            if (minutes <= 0) return;
            mo.AvgPct = (mo.AvgPct * mo.Minutes + sysAvg * minutes) / (mo.Minutes + minutes); mo.Minutes += minutes; mo.Days += span; mo.MaxPct = Math.Max(mo.MaxPct, sysMax);
            for (int h = 0; h < 24; h++) if (hN[h] > 0) { mo.HourAvg[h] = (mo.HourAvg[h] * mo.HourN[h] + hAvg[h] * hN[h]) / (mo.HourN[h] + hN[h]); mo.HourN[h] += hN[h]; }
            for (int d = 0; d < 7; d++) if (wN[d] > 0) { mo.WeekdayAvg[d] = (mo.WeekdayAvg[d] * mo.WeekdayN[d] + wAvg[d] * wN[d]) / (mo.WeekdayN[d] + wN[d]); mo.WeekdayN[d] += wN[d]; }
            foreach (string[] t in progs)
            {
                ProgProfile p; if (!mo.Progs.TryGetValue(t[1], out p)) { p = new ProgProfile { Name = t[1] }; mo.Progs[t[1]] = p; }
                double avg, max, pm, trend, fg; int ses; double.TryParse(t[2], NumberStyles.Float, IC, out avg); double.TryParse(t[3], NumberStyles.Float, IC, out max); double.TryParse(t[5], NumberStyles.Float, IC, out pm); int.TryParse(t[6], out ses); double.TryParse(t[8], NumberStyles.Float, IC, out fg); double.TryParse(t[9], NumberStyles.Float, IC, out trend);
                p.AvgMb = (p.AvgMb * p.PresentMin + avg * pm) / Math.Max(1, p.PresentMin + pm); p.TrendMbH = (p.TrendMbH * p.PresentMin + trend * pm) / Math.Max(1, p.PresentMin + pm); p.MaxMb = Math.Max(p.MaxMb, max); p.PresentMin += (long)pm; p.Sessions += ses; p.FgMin += (long)fg; if (from > p.LastSeen) p.LastSeen = from;
                AddHist(p.StartHours, t[10]); AddHist(p.ActiveMin, t[11]); AddHist(p.FgMinH, t[12]);
            }
            foreach (string[] t in freed) { ProgProfile p; if (!mo.Progs.TryGetValue(t[1], out p)) { p = new ProgProfile { Name = t[1] }; mo.Progs[t[1]] = p; } double mb, cnt; double.TryParse(t[2], NumberStyles.Float, IC, out mb); double.TryParse(t[3], NumberStyles.Float, IC, out cnt); p.FreedSumMb += mb; p.SweepCount += (int)cnt; }
        }
        static void AddHist(double[] h, string s) { if (string.IsNullOrEmpty(s)) return; foreach (string p in s.Split(';')) { string[] kv = p.Split(':'); int i; double v; if (kv.Length == 2 && int.TryParse(kv[0], out i) && double.TryParse(kv[1], NumberStyles.Float, IC, out v) && i >= 0 && i < 24) h[i] += v; } }

        // ---- memory.txt: a tanult tudás (szakaszokra bontva)
        void WriteModel(MemModel mo, int newFiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Loc.T("=== MEMORY – tanult tudás (soha nem törlődik; naponta frissül a mappa összes txt-jéből) ==="));
            sb.AppendLine("updated=" + mo.Updated.ToString("yyyy-MM-dd HH:mm")).AppendLine("learned_files=" + mo.Processed.Count + " new_today=" + newFiles);
            sb.AppendLine().AppendLine("=== GLOBAL ===").AppendLine("days=" + mo.Days).AppendLine("minutes=" + mo.Minutes).AppendLine("avg_pct=" + mo.AvgPct.ToString("0.0", IC)).AppendLine("max_pct=" + mo.MaxPct.ToString("0", IC));
            sb.Append("hour_avg="); for (int h = 0; h < 24; h++) { if (h > 0) sb.Append(';'); sb.Append(h).Append(':').Append(mo.HourAvg[h].ToString("0.0", IC)).Append(':').Append(mo.HourN[h].ToString("0", IC)); } sb.AppendLine();
            sb.Append("weekday_avg="); for (int d = 0; d < 7; d++) { if (d > 0) sb.Append(';'); sb.Append(d).Append(':').Append(mo.WeekdayAvg[d].ToString("0.0", IC)).Append(':').Append(mo.WeekdayN[d].ToString("0", IC)); } sb.AppendLine();
            List<ProgProfile> list = new List<ProgProfile>(mo.Progs.Values); list.Sort(delegate (ProgProfile x, ProgProfile y) { return (y.AvgMb * Math.Sqrt(y.PresentMin)).CompareTo(x.AvgMb * Math.Sqrt(x.PresentMin)); });
            foreach (ProgProfile p in list)
            {
                sb.AppendLine().AppendLine("=== PROGRAM " + p.Name + " ===");
                sb.AppendLine("avg_mb=" + p.AvgMb.ToString("0", IC)).AppendLine("max_mb=" + p.MaxMb.ToString("0", IC)).AppendLine("present_min=" + p.PresentMin).AppendLine("sessions=" + p.Sessions).AppendLine("avg_session_min=" + p.AvgSessionMin.ToString("0", IC)).AppendLine("fg_min=" + p.FgMin).AppendLine("fg_share=" + (p.FgShare * 100).ToString("0", IC)).AppendLine("trend_mb_h=" + p.TrendMbH.ToString("0", IC));
                sb.AppendLine("sweep_count=" + p.SweepCount).AppendLine("sweep_freed_mb=" + p.FreedSumMb.ToString("0", IC)).AppendLine("sweep_avg_freed_mb=" + p.AvgFreedMb.ToString("0", IC)).AppendLine("last_seen=" + p.LastSeen.ToString("yyyy-MM-dd"));
                sb.AppendLine("start_hours=" + Hist(p.StartHours)).AppendLine("active_hours=" + Hist(p.ActiveMin)).AppendLine("fg_hours=" + Hist(p.FgMinH));
            }
            // tanult szabályok
            sb.AppendLine().AppendLine(Loc.T("=== SWEEP-POLICY (tanult seprési szabályok) ==="));
            double trig = Math.Max(50, Math.Min(85, Math.Round(mo.AvgPct + 10))); sb.AppendLine("suggested_trigger_pct=" + trig.ToString("0", IC));
            StringBuilder heavy = new StringBuilder(); for (int h = 0; h < 24; h++) if (mo.HourN[h] > 0 && mo.HourAvg[h] >= mo.AvgPct + 7) { if (heavy.Length > 0) heavy.Append(','); heavy.Append(h); } sb.AppendLine("heavy_hours=" + heavy);
            StringBuilder first = new StringBuilder(), skip = new StringBuilder(), prot = new StringBuilder();
            foreach (ProgProfile p in list)
            {
                if (p.PresentMin >= 30 && p.AvgMb >= 150 && p.FgShare < 0.12 && p.AvgFreedMb >= 0 && !(p.SweepCount >= 3 && p.AvgFreedMb < 15)) { if (first.Length > 0) first.Append(','); first.Append(p.Name); }
                if (p.SweepCount >= 3 && p.AvgFreedMb < 15) { if (skip.Length > 0) skip.Append(','); skip.Append(p.Name); }
                if (p.PresentMin >= 60 && p.FgShare >= 0.35) { if (prot.Length > 0) prot.Append(','); prot.Append(p.Name); }
            }
            sb.AppendLine("sweep_first=" + first).AppendLine("skip_low_gain=" + skip).AppendLine("often_in_use=" + prot);
            // előrejelzés a következő órákra
            sb.AppendLine().AppendLine(Loc.T("=== PREDICTION (várható terhelés, óránként) ==="));
            for (int k = 0; k < 6; k++) { int h = (DateTime.Now.Hour + k) % 24; sb.AppendLine("hour_" + h + "=" + mo.HourPct(h).ToString("0", IC) + "%"); }
            sb.AppendLine().AppendLine("=== PROCESSED FILES ==="); foreach (string f in mo.Processed) sb.AppendLine(f); sb.AppendLine("=== END ===");
            try { Directory.CreateDirectory(MemoryDir); WriteAtomic(MemoryFile, sb.ToString()); } catch { }
        }

        void LoadModel()
        {
            MemModel mo = new MemModel();
            try
            {
                if (File.Exists(MemoryFile) && File.ReadAllText(MemoryFile, Encoding.UTF8).Contains("=== END ==="))      // csonka fájlból nem tanulunk tovább: újraépül az összes szakasz-fájlból
                {
                    string sec = ""; ProgProfile cur = null;
                    foreach (string l in File.ReadAllLines(MemoryFile, Encoding.UTF8))
                    {
                        if (l.StartsWith("=== ")) { sec = l; cur = null; if (l.StartsWith("=== PROGRAM ")) { string nm = l.Substring(12).Replace(" ===", "").Trim(); cur = new ProgProfile { Name = nm }; mo.Progs[nm] = cur; } continue; }
                        if (sec.StartsWith("=== PROCESSED")) { if (l.Trim().Length > 0) mo.Processed.Add(l.Trim()); continue; }
                        int e = l.IndexOf('='); if (e <= 0) continue; string k = l.Substring(0, e), v = l.Substring(e + 1); double d;
                        if (sec.StartsWith("=== GLOBAL"))
                        {
                            if (k == "days") int.TryParse(v, out mo.Days); else if (k == "minutes") long.TryParse(v, out mo.Minutes); else if (k == "avg_pct" && double.TryParse(v, NumberStyles.Float, IC, out d)) mo.AvgPct = d; else if (k == "max_pct" && double.TryParse(v, NumberStyles.Float, IC, out d)) mo.MaxPct = d;
                            else if (k == "hour_avg" || k == "weekday_avg") foreach (string p in v.Split(';')) { string[] t = p.Split(':'); int i; double a, n; if (t.Length == 3 && int.TryParse(t[0], out i) && double.TryParse(t[1], NumberStyles.Float, IC, out a) && double.TryParse(t[2], NumberStyles.Float, IC, out n)) { if (k == "hour_avg" && i >= 0 && i < 24) { mo.HourAvg[i] = a; mo.HourN[i] = n; } else if (k == "weekday_avg" && i >= 0 && i < 7) { mo.WeekdayAvg[i] = a; mo.WeekdayN[i] = n; } } }
                        }
                        else if (cur != null)
                        {
                            if (k == "avg_mb" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.AvgMb = d; else if (k == "max_mb" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.MaxMb = d; else if (k == "present_min" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.PresentMin = (long)d;
                            else if (k == "sessions" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.Sessions = (int)d; else if (k == "fg_min" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.FgMin = (long)d; else if (k == "trend_mb_h" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.TrendMbH = d;
                            else if (k == "sweep_count" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.SweepCount = (int)d; else if (k == "sweep_freed_mb" && double.TryParse(v, NumberStyles.Float, IC, out d)) cur.FreedSumMb = d;
                            else if (k == "last_seen") { DateTime ls; if (DateTime.TryParseExact(v, "yyyy-MM-dd", IC, DateTimeStyles.None, out ls)) cur.LastSeen = ls; } else if (k == "start_hours") AddHist(cur.StartHours, v); else if (k == "active_hours") AddHist(cur.ActiveMin, v); else if (k == "fg_hours") AddHist(cur.FgMinH, v);
                        }
                    }
                }
            }
            catch { }
            Model = mo;
        }

        // ---- az okos seprés tanult szabályai
        public volatile HashSet<string> SweepFirst = new HashSet<string>(), SkipLowGain = new HashSet<string>(), OftenInUse = new HashSet<string>();
        public void ReloadPolicy()
        {
            HashSet<string> a = new HashSet<string>(), b = new HashSet<string>(), c = new HashSet<string>();
            try
            {
                if (!File.Exists(MemoryFile)) return;
                foreach (string l in File.ReadAllLines(MemoryFile, Encoding.UTF8))
                {
                    HashSet<string> t = l.StartsWith("sweep_first=") ? a : (l.StartsWith("skip_low_gain=") ? b : (l.StartsWith("often_in_use=") ? c : null)); if (t == null) continue;
                    foreach (string s in l.Substring(l.IndexOf('=') + 1).Split(',')) if (s.Trim().Length > 0) t.Add(s.Trim());
                }
                SweepFirst = a; SkipLowGain = b; OftenInUse = c;
            }
            catch { }
        }
        public string Describe()
        {
            return Model.Processed.Count + Loc.T(" fájlból tanult, ") + Model.Progs.Count + Loc.T(" program, ") + Model.Days + Loc.T(" nap");
        }
    }
}
