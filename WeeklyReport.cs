// Heti riport: az elmúlt 7 nap memória-naplójából egy önálló HTML kártya (napi átlag/csúcs, legnagyobb fogyasztók, seprések).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace GlassControlWpf
{
    // olvasás úgy, hogy közben a naplóíró hozzáírhasson / cserélhesse a fájlt
    static class ShareRead
    {
        public static IEnumerable<string> Lines(string f) { using (StreamReader r = new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8)) { string l; while ((l = r.ReadLine()) != null) yield return l; } }
    }
    public static class WeeklyReport
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public static string Generate(AnalyticsStore st)
        {
            DateTime cut = DateTime.Now.Date.AddDays(-6); string cutS = cut.ToString("yyyy-MM-dd HH:mm", IC);
            Dictionary<string, double[]> days = new Dictionary<string, double[]>(); Dictionary<string, double[]> progs = new Dictionary<string, double[]>(); double allSum = 0; int allN = 0, peakH = -1; double peak = 0; int[] hourN = new int[24]; double[] hourSum = new double[24];
            try
            {
                if (File.Exists(st.RawFile)) foreach (string l in ShareRead.Lines(st.RawFile))
                {
                    if (l.Length < 16 || string.CompareOrdinal(l, 0, cutS, 0, 16) < 0) continue; string[] p = l.Split(','); DateTime t; double pct; if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", IC, DateTimeStyles.None, out t) || !double.TryParse(p[1], NumberStyles.Float, IC, out pct)) continue;
                    string d = t.ToString("yyyy-MM-dd", IC); double[] a; if (!days.TryGetValue(d, out a)) { a = new double[3]; days[d] = a; } a[0] += pct; a[1]++; if (pct > a[2]) a[2] = pct; allSum += pct; allN++; hourSum[t.Hour] += pct; hourN[t.Hour]++; if (pct > peak) { peak = pct; peakH = t.Hour; }
                    foreach (string part in p[4].Split('|')) { int c = part.LastIndexOf(':'); double mb; if (c > 0 && double.TryParse(part.Substring(c + 1), NumberStyles.Float, IC, out mb)) { string nm = part.Substring(0, c); double[] g; if (!progs.TryGetValue(nm, out g)) { g = new double[2]; progs[nm] = g; } g[0] += mb; g[1]++; } }
                }
            }
            catch { }
            int wipes = 0; double freed = 0; try { if (File.Exists(st.WipeFile)) foreach (string l in ShareRead.Lines(st.WipeFile)) { string[] p = l.Split(','); DateTime t; double f; if (p.Length >= 3 && DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", IC, DateTimeStyles.None, out t) && t >= cut && double.TryParse(p[2], NumberStyles.Float, IC, out f)) { wipes++; freed += f; } } } catch { }
            List<KeyValuePair<string, double[]>> top = new List<KeyValuePair<string, double[]>>(progs); top.Sort(delegate (KeyValuePair<string, double[]> x, KeyValuePair<string, double[]> y) { return (y.Value[0] / Math.Max(1, y.Value[1])).CompareTo(x.Value[0] / Math.Max(1, x.Value[1])); });
            StringBuilder sb = new StringBuilder("<!doctype html><meta charset=utf-8><title>Heti riport</title><body style=\"margin:0;background:#141416;color:#eee;font:14px Segoe UI,sans-serif;display:grid;place-items:center;min-height:100vh\"><div style=\"width:640px;background:#202024;border-radius:20px;padding:26px 30px;margin:24px\">");
            sb.Append(Loc.T("<h2 style=\"margin:0 0 4px\">📈 Mi húzza le a gépet?</h2><div style=\"color:#9a9aa2;margin-bottom:18px\">") + cut.ToString("yyyy.MM.dd") + " – " + DateTime.Now.ToString("yyyy.MM.dd") + " · " + allN + Loc.T(" mérés</div>"));
            if (allN == 0) { sb.Append(Loc.T("<p>Még nincs elég mérés ehhez a riporthoz.</p></div>")); return sb.ToString(); }
            sb.Append("<div style=\"display:flex;gap:12px;margin-bottom:18px\">" + Card(Loc.T("Átlag"), (allSum / allN).ToString("0") + "%") + Card(Loc.T("Csúcs"), peak.ToString("0") + "% (" + peakH + Loc.T(" óra)")) + Card(Loc.T("Seprés"), wipes + " db") + Card(Loc.T("Felszabadított"), (freed / 1024).ToString("0.0") + " GB") + "</div>");
            sb.Append(Loc.T("<h3 style=\"margin:10px 0 8px\">Napi átlag / csúcs</h3>")); List<string> keys = new List<string>(days.Keys); keys.Sort();
            foreach (string k in keys) { double[] a = days[k]; double avg = a[0] / a[1]; sb.Append("<div style=\"display:flex;align-items:center;margin:4px 0\"><span style=\"width:50px;color:#9a9aa2\">" + k.Substring(5).Replace('-', '.') + "</span><div style=\"flex:1;background:#2c2c31;border-radius:6px;height:12px\"><div style=\"width:" + avg.ToString("0", IC) + "%;background:#0a84ff;height:12px;border-radius:6px\"></div></div><span style=\"width:110px;text-align:right\">" + avg.ToString("0") + Loc.T("% · csúcs ") + a[2].ToString("0") + "%</span></div>"); }
            sb.Append(Loc.T("<h3 style=\"margin:18px 0 8px\">Legnagyobb fogyasztók (átlag)</h3>"));
            for (int i = 0; i < top.Count && i < 8; i++) { double avgMb = top[i].Value[0] / Math.Max(1, top[i].Value[1]); sb.Append("<div style=\"display:flex;justify-content:space-between;margin:4px 0\"><span>" + System.Net.WebUtility.HtmlEncode(top[i].Key) + "</span><b>" + (avgMb >= 1024 ? (avgMb / 1024).ToString("0.0") + " GB" : avgMb.ToString("0") + " MB") + "</b></div>"); }
            double worst = 0; int wh = -1; for (int h = 0; h < 24; h++) if (hourN[h] > 3 && hourSum[h] / hourN[h] > worst) { worst = hourSum[h] / hourN[h]; wh = h; }
            if (wh >= 0) sb.Append(Loc.T("<p style=\"color:#9a9aa2;margin-top:18px\">💡 Általában ") + wh + Loc.T(":00 körül a legnehezebb a gép (átlag ") + worst.ToString("0") + "%).</p>");
            sb.Append("</div>"); return sb.ToString();
        }
        static string Card(string t, string v) { return "<div style=\"flex:1;background:#2a2a30;border-radius:12px;padding:10px 12px\"><div style=\"color:#9a9aa2;font-size:12px\">" + t + "</div><div style=\"font-size:18px;font-weight:600\">" + v + "</div></div>"; }
        public static void Open(AnalyticsStore st)
        {
            try { string f = Path.Combine(st.Root, "heti-riport.html"); File.WriteAllText(f, Generate(st), new UTF8Encoding(false)); using (Process.Start(new ProcessStartInfo(f) { UseShellExecute = true })) { } } catch { }
        }
    }
}
