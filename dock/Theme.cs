// GlassDock téma: az üveghatás összes állítható értéke egy helyen (settings.ini kulcsok), élő újratöltéssel.
// A GlassControl "iOS Glass UI" fülje ezeket a kulcsokat írja; a dokk ~0,3 mp-en belül átveszi.
using System;
using System.Windows;
using System.Windows.Media;

namespace GlassDock
{
    static class Theme
    {
        public static Settings S; public static bool Pinned; public static string PinFile;
        // a GlassControl iOS Glass UI füle közben a dokk és a pilulák látszanak (a pin.txt-t 1,5 mp-enként frissíti)
        public static void PollPin() { try { Pinned = PinFile != null && System.IO.File.Exists(PinFile) && (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(PinFile)).TotalSeconds < 5; } catch { Pinned = false; } }
        static double Num(string k, double d) { return S != null ? S.Get(k, d) : d; }
        static byte B(string k, double d) { return (byte)Math.Max(0, Math.Min(255, Math.Round(Num(k, d)))); }

        // --- üveg
        public static Color Tint { get { return Color.FromRgb(B("tintr", 17), B("tintg", 20), B("tintb", 26)); } }
        public static Color TintA(byte a) { Color c = Tint; return Color.FromArgb(a, c.R, c.G, c.B); }
        public static byte Shine { get { return B("shine", 34); } }
        public static byte BorderA { get { return B("borderalpha", 80); } }
        public static Color BorderC { get { return Color.FromRgb(B("borderr", 255), B("borderg", 255), B("borderb", 255)); } }
        public static LinearGradientBrush Gloss() { Color w = Color.FromRgb(255, 255, 255); return new LinearGradientBrush(Color.FromArgb(Shine, w.R, w.G, w.B), Color.FromArgb((byte)(Shine / 17), w.R, w.G, w.B), 90); }
        public static Pen BorderPen() { Color c = BorderC; return new Pen(new SolidColorBrush(Color.FromArgb(BorderA, c.R, c.G, c.B)), 1); }
        public static int BlurPx(int baseR) { double k = Num("blur", 16) / 16.0; return Math.Max(0, (int)Math.Round(baseR * k)); }

        // az elmosott háttér színe: telítettség és fényerő (iOS-es "vibrancy")
        public static void Grade(byte[] px) { Grade(px, px.Length); }
        public static void Grade(byte[] px, int len)
        {
            double sat = Num("frostsat", 100) / 100.0, br = Num("frostbright", 100) / 100.0; if (Math.Abs(sat - 1) < 0.01 && Math.Abs(br - 1) < 0.01) return;
            for (int i = 0; i + 3 < len; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2], l = 0.299 * r + 0.587 * g + 0.114 * b;
                r = (l + (r - l) * sat) * br; g = (l + (g - l) * sat) * br; b = (l + (b - l) * sat) * br;
                px[i] = (byte)Math.Max(0, Math.Min(255, b)); px[i + 1] = (byte)Math.Max(0, Math.Min(255, g)); px[i + 2] = (byte)Math.Max(0, Math.Min(255, r));
            }
        }

        // --- kerekítés
        public static double MenuRound { get { return Math.Max(0, Num("menuround", 26)); } }
        public static double TileRound { get { return Math.Max(0, Num("tileround", 15)); } }
        public static byte TileA { get { return B("tilealpha", 62); } }

        // --- betű
        public static string FontName { get { return S != null ? S.GetStr("ufont", "Inter") : "Inter"; } }
        public static FontFamily Font() { return new FontFamily(FontName + ", Inter, Segoe UI"); }
        public static double FontScale { get { return Math.Max(0.6, Math.Min(2, Num("ufontscale", 1))); } }
        public static double FS(double n) { return n * FontScale; }
        // 0 vékony, 1 normál/közepes, 2 félkövér, 3 kövér; a "step" a szöveg fontossága (cím = +1)
        public static FontWeight W(int step)
        {
            int i = (int)Math.Round(Num("ufontweight", 1)) + step; if (i < 0) i = 0; if (i > 3) i = 3;
            return i == 0 ? FontWeights.Normal : (i == 1 ? FontWeights.Medium : (i == 2 ? FontWeights.SemiBold : FontWeights.Bold));
        }

        // --- ikonok, animáció
        public static double IconGain(double baseGain) { return baseGain * Math.Max(0.2, Num("iconbright", 100) / 100.0); }
        public static double Anim { get { return Math.Max(0.3, Math.Min(3, Num("animspeed", 1))); } }
    }
}
