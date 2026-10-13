// Lumen – nyelvkezelés (English / Magyar / Deutsch). A forráskód szövegei magyar kulcsok, a fordítások a beágyazott lang.tsv-ben vannak
// (hu <TAB> en <TAB> de, \n \t \\ escape-ekkel). Az aktuális nyelv a dock\language.txt fájlban él (en | hu | de, alapból en);
// a GlassControl, a GlassDock és a Terminal ugyanazt használja, a fájl módosítását ~1 mp-en belül észreveszik.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

public static class Loc
{
    public static readonly string[] Codes = { "en", "hu", "de" };
    public static readonly string[] Names = { "English", "Magyar", "Deutsch" };

    static readonly object gate = new object();
    static Dictionary<string, string[]> map;
    static string lang = "en", path;
    static DateTime stamp = DateTime.MinValue;
    static int checkedAt = Environment.TickCount - 100000;

    public static string Lang { get { Refresh(); return lang; } }

    public static string LangName { get { string c = Lang; int i = Array.IndexOf(Codes, c); return Names[i < 0 ? 0 : i]; } }

    /// <summary>A magyar kulcs fordítása az aktuális nyelvre (ismeretlen kulcs vagy magyar nyelv: változatlan).</summary>
    public static string T(string hu)
    {
        if (string.IsNullOrEmpty(hu)) return hu;
        Refresh();
        if (lang == "hu") return hu;
        lock (gate)
        {
            string[] v;
            if (map != null && map.TryGetValue(hu, out v)) { string s = lang == "de" ? v[1] : v[0]; return string.IsNullOrEmpty(s) ? hu : s; }
        }
        return hu;
    }

    /// <summary>Nyelv mentése (a többi futó Lumen-program ~1 mp-en belül átveszi).</summary>
    public static void SetLang(string code)
    {
        if (Array.IndexOf(Codes, code) < 0) return;
        try
        {
            string p = FilePath(); string tmp = p + ".tmp";
            File.WriteAllText(tmp, code, new UTF8Encoding(false));
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
            stamp = File.GetLastWriteTimeUtc(p);
        }
        catch { }
        lang = code;
    }

    static string FilePath()
    {
        if (path != null) return path;
        string b = AppDomain.CurrentDomain.BaseDirectory;
        string d = Path.Combine(b, "dock");
        if (!Directory.Exists(d))
        {
            if (File.Exists(Path.Combine(b, "settings.ini"))) d = b;
            else { string up = Path.GetFullPath(Path.Combine(b, "..", "dock")); d = Directory.Exists(up) ? up : b; }
        }
        path = Path.Combine(d, "language.txt");
        return path;
    }

    static void Refresh()
    {
        int now = Environment.TickCount;
        if (map != null && Math.Abs(now - checkedAt) < 1000) return;
        checkedAt = now;
        try
        {
            if (map == null) LoadMap();
            string p = FilePath();
            DateTime w = File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
            if (w == stamp && stamp != DateTime.MinValue) return;
            stamp = w;
            string c = w == DateTime.MinValue ? "en" : File.ReadAllText(p).Trim().ToLowerInvariant();
            lang = Array.IndexOf(Codes, c) >= 0 ? c : "en";
        }
        catch { if (map == null) map = new Dictionary<string, string[]>(); }
    }

    static void LoadMap()
    {
        Dictionary<string, string[]> m = new Dictionary<string, string[]>();
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang.tsv"))
            {
                if (s != null)
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            string[] f = line.Split('\t');
                            if (f.Length < 3) continue;
                            m[Un(f[0])] = new string[] { Un(f[1]), Un(f[2]) };
                        }
                    }
            }
        }
        catch { }
        lock (gate) map = m;
    }

    static string Un(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                b.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n == 'r' ? '\r' : n);
            }
            else b.Append(c);
        }
        return b.ToString();
    }
}
