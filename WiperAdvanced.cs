// GlassControl – Wise Memory Wiper "Haladó" beállítások: mikor, mit és hogyan seperjen automatikusan (akár nyitott ablak nélkül is).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GlassTerm;

namespace GlassControlWpf
{
    // az automatikus seprés összes beállítása (analytics\autosweep.ini)
    public class SweepConfig
    {
        public bool Auto = true, Smart = true, ProtectFg = true, IdleOnly, Notify, StartupSweep, CloseToTray = true, StartHidden = true;
        public int Threshold = 50, Cooldown = 10, IntervalMin, HourFrom, HourTo = 24, IdleSec = 60, MinProcMb, StartupDelaySec = 60; public double AvailBelowGb; public string Excludes = "";
        public string Path; public event Action Changed;
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public void Load(string path)
        {
            Path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string l in File.ReadAllLines(path))
                {
                    int i = l.IndexOf('='); if (i <= 0) continue; string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim(); int n; double d; bool isInt = int.TryParse(v, out n); bool isD = double.TryParse(v, NumberStyles.Float, IC, out d);
                    switch (k)
                    {
                        case "auto": Auto = isInt && n > 0; break; case "smart": Smart = isInt && n > 0; break; case "protectfg": ProtectFg = isInt && n > 0; break; case "idleonly": IdleOnly = isInt && n > 0; break;
                        case "notify": Notify = isInt && n > 0; break; case "startupsweep": StartupSweep = isInt && n > 0; break; case "closetotray": CloseToTray = isInt && n > 0; break; case "starthidden": StartHidden = isInt && n > 0; break;
                        case "threshold": if (isInt) Threshold = n; break; case "cooldown": if (isInt) Cooldown = Math.Max(1, n); break; case "interval": if (isInt) IntervalMin = n; break;
                        case "hourfrom": if (isInt) HourFrom = n; break; case "hourto": if (isInt) HourTo = n; break; case "idlesec": if (isInt) IdleSec = n; break; case "minprocmb": if (isInt) MinProcMb = n; break;
                        case "startupdelay": if (isInt) StartupDelaySec = n; break; case "availbelowgb": if (isD) AvailBelowGb = Convert.ToDouble(v, IC); break; case "excludes": Excludes = v; break;
                    }
                }
            }
            catch { }
            Clamp();
        }
        void Clamp()
        {
            Threshold = Math.Max(0, Math.Min(100, Threshold)); HourFrom = Math.Max(0, Math.Min(23, HourFrom)); HourTo = Math.Max(1, Math.Min(24, HourTo)); IdleSec = Math.Max(10, IdleSec); Cooldown = Math.Max(1, Math.Min(1440, Cooldown)); IntervalMin = Math.Max(0, IntervalMin); MinProcMb = Math.Max(0, MinProcMb); StartupDelaySec = Math.Max(10, Math.Min(3600, StartupDelaySec));
            if (double.IsNaN(AvailBelowGb) || double.IsInfinity(AvailBelowGb) || AvailBelowGb < 0) AvailBelowGb = 0;
        }

        public void Save()
        {
            try
            {
                List<string> l = new List<string> { "auto=" + B(Auto), "smart=" + B(Smart), "protectfg=" + B(ProtectFg), "idleonly=" + B(IdleOnly), "notify=" + B(Notify), "startupsweep=" + B(StartupSweep), "closetotray=" + B(CloseToTray), "starthidden=" + B(StartHidden),
                    "threshold=" + Threshold, "cooldown=" + Cooldown, "interval=" + IntervalMin, "hourfrom=" + HourFrom, "hourto=" + HourTo, "idlesec=" + IdleSec, "minprocmb=" + MinProcMb, "startupdelay=" + StartupDelaySec, "availbelowgb=" + AvailBelowGb.ToString("0.#", IC), "excludes=" + Excludes };
                if (Path != null) AnalyticsStore.WriteAtomic(Path, string.Join(Environment.NewLine, l.ToArray()) + Environment.NewLine);
            }
            catch { }
            if (Changed != null) Changed();
        }
        static string B(bool b) { return b ? "1" : "0"; }

        public bool InHours(int hour) { if (HourFrom == HourTo || (HourFrom <= 0 && HourTo >= 24)) return true; if (HourFrom < HourTo) return hour >= HourFrom && hour < HourTo; return hour >= HourFrom || hour < HourTo; }
        public bool IsExcluded(string name) { if (string.IsNullOrEmpty(Excludes)) return false; foreach (string p in Excludes.Split(',', ';')) { string x = p.Trim(); if (x.Length == 0) continue; if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) x = x.Substring(0, x.Length - 4); if (string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) return true; } return false; }

        public string Summary()
        {
            if (!Auto) return Loc.T("Automatikus seprés KI");
            List<string> p = new List<string>(); if (Threshold > 0) p.Add("RAM " + Threshold + Loc.T("% felett")); if (AvailBelowGb > 0) p.Add(Loc.T("szabad < ") + AvailBelowGb.ToString("0.#") + " GB"); if (IntervalMin > 0) p.Add("" + IntervalMin + Loc.T(" percenként"));
            string s = Loc.T("Automatikus seprés BE · ") + (p.Count > 0 ? string.Join(" / ", p.ToArray()) : Loc.T("nincs feltétel")) + Loc.T(" · legkorábban ") + Cooldown + Loc.T(" percenként");
            if (!(HourFrom <= 0 && HourTo >= 24)) s += " · " + HourFrom + "–" + HourTo + Loc.T(" óra között"); if (IdleOnly) s += Loc.T(" · csak tétlenül"); if (Smart) s += Loc.T(" · okos mód");
            return s;
        }
    }

    // közös kis vezérlők (csúszka, kapcsoló, kártya)
    public static class UiKit
    {
        static Style slider; static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Txt = new SolidColorBrush(Color.FromRgb(214, 214, 220)), Val = new SolidColorBrush(Color.FromRgb(120, 180, 255));
        static readonly Brush CardBg = new SolidColorBrush(Color.FromArgb(150, 54, 54, 58)), CardBd = new SolidColorBrush(Color.FromArgb(110, 92, 92, 98)), Blue = new SolidColorBrush(Color.FromRgb(10, 132, 255)), Green = new SolidColorBrush(Color.FromRgb(48, 209, 88)), Off = new SolidColorBrush(Color.FromArgb(110, 120, 120, 128));
        public static Style SliderStyle { get { if (slider == null) { try { slider = (Style)((ResourceDictionary)XamlReader.Parse(GlassUiPanel.SliderXaml))["GSlider"]; } catch { } } return slider; } }

        public static Border Card(string title, UIElement body, string sub)
        {
            StackPanel sp = new StackPanel(); Grid h = new Grid(); h.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13.5 });
            if (sub != null) h.Children.Add(new TextBlock { Text = sub, Foreground = Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(h); sp.Children.Add(body); ((FrameworkElement)body).Margin = new Thickness(0, 6, 0, 0);
            return new Border { Background = CardBg, BorderBrush = CardBd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = sp };
        }

        public static void Slider(Panel parent, string label, double min, double max, double step, string fmt, string unit, string zeroText, Func<double> get, Action<double> set, List<Action> refresh)
        {
            Grid head = new Grid { Margin = new Thickness(0, 8, 0, 0) }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock lb = new TextBlock { Text = label, Foreground = Txt, FontSize = 12.5 }; TextBlock val = new TextBlock { Foreground = Val, FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas") }; Grid.SetColumn(val, 1);
            head.Children.Add(lb); head.Children.Add(val);
            Slider sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, IsMoveToPointEnabled = true, Focusable = false, TickFrequency = step, IsSnapToTickEnabled = true }; if (SliderStyle != null) sl.Style = SliderStyle;
            bool upd = false; Action show = delegate { val.Text = (zeroText != null && sl.Value <= min + 1e-9) ? zeroText : sl.Value.ToString(fmt, CultureInfo.CurrentCulture) + unit; };
            sl.ValueChanged += delegate { show(); if (!upd) set(sl.Value); };
            refresh.Add(delegate { upd = true; sl.Value = Math.Max(min, Math.Min(max, get())); show(); upd = false; });
            upd = true; sl.Value = Math.Max(min, Math.Min(max, get())); show(); upd = false; parent.Children.Add(head); parent.Children.Add(sl);
        }

        public static void Toggle(Panel parent, string label, string note, Func<bool> get, Action<bool> set, List<Action> refresh)
        {
            Grid row = new Grid { Margin = new Thickness(0, 7, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; tx.Children.Add(new TextBlock { Text = label, Foreground = Txt, FontSize = 12.5 }); if (note != null) tx.Children.Add(new TextBlock { Text = note, Foreground = Muted, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left });
            Border track = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(track, 1);
            Ellipse knob = new Ellipse { Width = 22, Height = 22, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.35 } }; track.Child = knob;
            Action paint = delegate { bool on = get(); track.Background = on ? Green : Off; knob.Margin = new Thickness(on ? 22 : 2, 0, 0, 0); };
            track.MouseLeftButtonUp += delegate { set(!get()); paint(); }; refresh.Add(paint); paint(); row.Children.Add(tx); row.Children.Add(track); parent.Children.Add(row);
        }

        public static Border Chip(string text, Action click, bool accent)
        {
            Border b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = accent ? Blue : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) };
            b.Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold }; b.MouseEnter += delegate { b.Opacity = 0.82; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.MouseLeftButtonUp += delegate { click(); }; return b;
        }
    }

    // a Haladó panel
    public class WiperAdvanced : StackPanel
    {
        SweepConfig c; List<Action> refresh = new List<Action>(); TextBlock status = new TextBlock();
        public Func<string> LastInfo;

        public WiperAdvanced(SweepConfig cfg, Func<bool> getAutostart, Action<bool> setAutostart, Action sweepNow, Action openMemory)
        {
            c = cfg; System.Windows.Threading.DispatcherTimer saveT = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; saveT.Tick += delegate { saveT.Stop(); c.Save(); };
            Action save = delegate { saveT.Stop(); saveT.Start(); };      // gépelés/húzás közben nem minden mozdulatra írunk fájlt
            // előbeállítások
            WrapPanel pre = new WrapPanel();
            Action<string, Action> addPre = delegate (string n, Action a) { pre.Children.Add(UiKit.Chip(n, delegate { a(); c.Save(); foreach (Action r in refresh) r(); }, false)); };
            addPre(Loc.T("Óvatos"), delegate { c.Auto = true; c.Threshold = 75; c.AvailBelowGb = 0; c.IntervalMin = 0; c.Cooldown = 20; c.Smart = true; c.ProtectFg = true; c.MinProcMb = 80; c.IdleOnly = false; });
            addPre(Loc.T("Kiegyensúlyozott"), delegate { c.Auto = true; c.Threshold = 60; c.AvailBelowGb = 0; c.IntervalMin = 0; c.Cooldown = 10; c.Smart = true; c.ProtectFg = true; c.MinProcMb = 40; c.IdleOnly = false; });
            addPre(Loc.T("Agresszív (50%)"), delegate { c.Auto = true; c.Threshold = 50; c.AvailBelowGb = 0; c.IntervalMin = 0; c.Cooldown = 5; c.Smart = true; c.ProtectFg = true; c.MinProcMb = 0; c.IdleOnly = false; });
            addPre(Loc.T("Csak időzítve"), delegate { c.Auto = true; c.Threshold = 0; c.AvailBelowGb = 0; c.IntervalMin = 30; c.Cooldown = 10; c.Smart = true; c.IdleOnly = false; });
            addPre(Loc.T("Játék / munka közben csendben"), delegate { c.Auto = true; c.Threshold = 80; c.Cooldown = 15; c.IdleOnly = true; c.IdleSec = 120; c.Smart = true; c.ProtectFg = true; });
            addPre("Ki", delegate { c.Auto = false; });
            Children.Add(UiKit.Card(Loc.T("Előbeállítások"), pre, null));

            StackPanel when = new StackPanel();
            UiKit.Toggle(when, Loc.T("Automatikus seprés"), Loc.T("A GlassControl a háttérben (akár zárt ablakkal, tálca ikonként) méri a memóriát és sepri, ha a feltételek teljesülnek."), delegate { return c.Auto; }, delegate (bool v) { c.Auto = v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("Induljon, ha a használt memória eléri"), 0, 95, 5, "0", " %", "ki", delegate { return c.Threshold; }, delegate (double v) { c.Threshold = (int)v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("…vagy ha a szabad memória ez alá esik"), 0, 8, 0.5, "0.0", " GB", "ki", delegate { return c.AvailBelowGb; }, delegate (double v) { c.AvailBelowGb = v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("…vagy időzítve, ennyi percenként"), 0, 240, 5, "0", Loc.T(" perc"), "ki", delegate { return c.IntervalMin; }, delegate (double v) { c.IntervalMin = (int)v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("Két seprés között legalább"), 1, 120, 1, "0", Loc.T(" perc"), null, delegate { return c.Cooldown; }, delegate (double v) { c.Cooldown = (int)v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("Ettől az órától (naponta)"), 0, 23, 1, "0", Loc.T(" ó"), null, delegate { return c.HourFrom; }, delegate (double v) { c.HourFrom = (int)v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("Eddig az óráig"), 1, 24, 1, "0", Loc.T(" ó"), null, delegate { return c.HourTo; }, delegate (double v) { c.HourTo = (int)v; save(); }, refresh);
            UiKit.Toggle(when, Loc.T("Csak ha nem használod a gépet"), Loc.T("Csak akkor seper, ha ennyi ideje nem nyúltál az egérhez/billentyűzethez."), delegate { return c.IdleOnly; }, delegate (bool v) { c.IdleOnly = v; save(); }, refresh);
            UiKit.Slider(when, Loc.T("Tétlenség ideje"), 10, 900, 10, "0", " mp", null, delegate { return c.IdleSec; }, delegate (double v) { c.IdleSec = (int)v; save(); }, refresh);
            Children.Add(UiKit.Card(Loc.T("Mikor induljon"), when, null));

            StackPanel what = new StackPanel();
            UiKit.Toggle(what, Loc.T("Okos mód (a tanult szabályokból)"), Loc.T("Az analytics\\memory\\memory.txt alapján: a háttérben felfúvódó programokkal kezd, kihagyja ahol alig van nyereség, és ha az adott óra általában nehéz, előbb indul."), delegate { return c.Smart; }, delegate (bool v) { c.Smart = v; save(); }, refresh);
            UiKit.Toggle(what, Loc.T("Az előtérben lévő program védelme"), Loc.T("Amit éppen használsz, ahhoz nem nyúl (nincs belassulás)."), delegate { return c.ProtectFg; }, delegate (bool v) { c.ProtectFg = v; save(); }, refresh);
            UiKit.Slider(what, Loc.T("Csak az ennél nagyobb folyamatokat ürítse"), 0, 500, 10, "0", " MB", "mind", delegate { return c.MinProcMb; }, delegate (double v) { c.MinProcMb = (int)v; save(); }, refresh);
            what.Children.Add(new TextBlock { Text = Loc.T("Kizárt programok (soha ne ürítse; vesszővel elválasztva, pl. brave, obs64)"), Foreground = new SolidColorBrush(Color.FromRgb(214, 214, 220)), FontSize = 12.5, Margin = new Thickness(0, 10, 0, 4) });
            TextBox ex = new TextBox { Text = c.Excludes, Background = new SolidColorBrush(Color.FromRgb(40, 40, 43)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromArgb(110, 92, 92, 98)), CaretBrush = Brushes.White, Padding = new Thickness(7, 5, 7, 5), FontFamily = new FontFamily("Consolas"), FontSize = 12.5 };
            ex.TextChanged += delegate { c.Excludes = ex.Text; save(); }; GlassMenu.ForTextBox(ex); refresh.Add(delegate { if (!ex.IsKeyboardFocused) ex.Text = c.Excludes; }); what.Children.Add(ex);
            Children.Add(UiKit.Card(Loc.T("Mit seperjen"), what, null));

            StackPanel bg = new StackPanel();
            UiKit.Toggle(bg, Loc.T("Indítás a Windows-szal"), Loc.T("A bejelentkezéskor elindul és a háttérben figyel (nem kell megnyitni)."), getAutostart, setAutostart, refresh);
            UiKit.Toggle(bg, Loc.T("Indításkor rejtve (csak tálca ikon)"), Loc.T("Az automatikus indításkor nem jelenik meg az ablak."), delegate { return c.StartHidden; }, delegate (bool v) { c.StartHidden = v; save(); setAutostart(getAutostart()); }, refresh);
            UiKit.Toggle(bg, Loc.T("A piros gomb háttérbe küld"), Loc.T("Bezáráskor nem lép ki: a mérés és az automatikus seprés tovább fut a tálcán. (Kilépés: jobb klikk a fejlécen.)"), delegate { return c.CloseToTray; }, delegate (bool v) { c.CloseToTray = v; save(); }, refresh);
            UiKit.Toggle(bg, Loc.T("Seprés az indítás után"), Loc.T("Egyszer, a megadott késleltetéssel (a bejelentkezéskor felfújt memória rendbetételére)."), delegate { return c.StartupSweep; }, delegate (bool v) { c.StartupSweep = v; save(); }, refresh);
            UiKit.Slider(bg, Loc.T("Késleltetés az indítás után"), 10, 600, 10, "0", " mp", null, delegate { return c.StartupDelaySec; }, delegate (double v) { c.StartupDelaySec = (int)v; save(); }, refresh);
            UiKit.Toggle(bg, Loc.T("Értesítés seprés után"), Loc.T("Tálca-buborék: mennyi memória szabadult fel."), delegate { return c.Notify; }, delegate (bool v) { c.Notify = v; save(); }, refresh);
            Children.Add(UiKit.Card(Loc.T("Háttérben és indításkor"), bg, null));

            WrapPanel act = new WrapPanel(); act.Children.Add(UiKit.Chip(Loc.T("Seprés most (okos)"), delegate { sweepNow(); }, true)); act.Children.Add(UiKit.Chip(Loc.T("Tanult tudás megnyitása"), delegate { openMemory(); }, false));
            status.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)); status.FontSize = 11.5; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0);
            StackPanel st = new StackPanel(); st.Children.Add(act); st.Children.Add(status); Children.Add(UiKit.Card(Loc.T("Állapot"), st, null));
            UpdateStatus();
        }

        public void UpdateStatus() { status.Text = c.Summary() + (LastInfo != null ? "\n" + LastInfo() : ""); }
        public void RefreshAll() { foreach (Action r in refresh) r(); UpdateStatus(); }
    }
}
