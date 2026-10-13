// GlassControl – "iOS Glass UI" fül: az üveghatás minden eleme csúszkákkal, színválasztóval, betűtípus-listával.
// A beállításokat a dock\settings.ini-be írja; a GlassDock ~0,3 mp-en belül élőben átveszi (dokk, pilulák, Start-menü, Spotlight).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using GlassTerm;

namespace GlassControlWpf
{
    public class GlassUiPanel : ScrollViewer
    {
        string path; Action<string> runPs; string dockExe;
        Dictionary<string, string> vals = new Dictionary<string, string>(), pending = new Dictionary<string, string>();
        List<Action> refreshers = new List<Action>(); bool updating;
        DispatcherTimer writeTimer = new DispatcherTimer(), previewTimer = new DispatcherTimer();
        List<KeyValuePair<string, Dictionary<string, double>>> presets = new List<KeyValuePair<string, Dictionary<string, double>>>();
        Style sliderStyle; Canvas backdrop; Grid overlay; TextBlock fontNow;
        static readonly Dictionary<string, double> Def = new Dictionary<string, double> {
            { "glass", 135 }, { "tintr", 17 }, { "tintg", 20 }, { "tintb", 26 }, { "blur", 16 }, { "frostsat", 100 }, { "frostbright", 100 }, { "shine", 34 }, { "borderalpha", 80 },
            { "borderr", 255 }, { "borderg", 255 }, { "borderb", 255 }, { "round", 0.6 }, { "menuround", 26 }, { "tileround", 15 }, { "flyoutround", 8 }, { "flyoutgap", 10 },
            { "ufontscale", 1 }, { "ufontweight", 1 }, { "tilealpha", 62 }, { "iconbright", 100 }, { "animspeed", 1 }, { "mono", 1 }, { "tiles", 1 }, { "autohide", 1 }, { "pills", 1 },
            { "start", 0 }, { "glassstart", 1 }, { "spotlight", 1 }, { "flyoutstyle", 1 }, { "desktoptiles", 1 } };

        internal static readonly string SliderXaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='GSlider' TargetType='Slider'>
    <Setter Property='Height' Value='24'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='#40FFFFFF' VerticalAlignment='Center'/>
            <Track x:Name='PART_Track' VerticalAlignment='Center'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
                  <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='#FF0A84FF' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>
                  <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Focusable='False' Width='20' Height='20'>
                  <Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='White' Stroke='#30000000' StrokeThickness='0.5'><Ellipse.Effect><DropShadowEffect BlurRadius='4' ShadowDepth='1' Opacity='0.45'/></Ellipse.Effect></Ellipse></ControlTemplate></Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        static SolidColorBrush Br(byte a, byte r, byte g, byte b) { SolidColorBrush s = new SolidColorBrush(Color.FromArgb(a, r, g, b)); s.Freeze(); return s; }
        static readonly Brush Muted = Br(255, 156, 163, 175), CardBg = Br(150, 54, 54, 58), CardBd = Br(110, 92, 92, 98), White = Brushes.White;

        public GlassUiPanel(string settingsPath, Action<string> runPowerShell, string dockExePath)
        {
            path = settingsPath; runPs = runPowerShell; dockExe = dockExePath;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Focusable = false;
            try { sliderStyle = (Style)((ResourceDictionary)XamlReader.Parse(SliderXaml))["GSlider"]; } catch { }
            Load();
            writeTimer.Interval = TimeSpan.FromMilliseconds(120); writeTimer.Tick += delegate { writeTimer.Stop(); Flush(); };
            previewTimer.Interval = TimeSpan.FromMilliseconds(70); previewTimer.Tick += delegate { previewTimer.Stop(); BuildPreview(); };
            Build(); Loaded += delegate { BuildPreview(); };
        }

        // ------------------------------------------------------------ értékek
        void Load()
        {
            vals.Clear();
            try { if (File.Exists(path)) foreach (string l in File.ReadAllLines(path)) { int i = l.IndexOf('='); if (i > 0) vals[l.Substring(0, i)] = l.Substring(i + 1); } } catch { }
        }
        double Num(string k) { string s; double v, d; if (vals.TryGetValue(k, out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v; return Def.TryGetValue(k, out d) ? d : 0; }
        string Str(string k, string def) { string s; return vals.TryGetValue(k, out s) && s.Length > 0 ? s : def; }
        void Set(string k, double v) { SetRaw(k, v.ToString("0.###", CultureInfo.InvariantCulture)); }
        void SetRaw(string k, string v) { vals[k] = v; pending[k] = v; writeTimer.Stop(); writeTimer.Start(); previewTimer.Stop(); previewTimer.Start(); }
        void Flush()
        {
            if (pending.Count == 0) return;
            try
            {
                List<string> lines = new List<string>(); HashSet<string> done = new HashSet<string>();
                if (File.Exists(path)) foreach (string l in File.ReadAllLines(path)) { int i = l.IndexOf('='); if (i > 0) { string k = l.Substring(0, i); string nv; if (pending.TryGetValue(k, out nv)) { lines.Add(k + "=" + nv); done.Add(k); } else lines.Add(l); } else lines.Add(l); }
                foreach (KeyValuePair<string, string> kv in pending) if (!done.Contains(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
                AnalyticsStore.WriteAtomic(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine, false); pending.Clear();
            }
            catch { }
        }
        void SetMany(Dictionary<string, double> d) { foreach (KeyValuePair<string, double> kv in d) Set(kv.Key, kv.Value); Refresh(); }
        void Refresh() { updating = true; foreach (Action a in refreshers) a(); updating = false; }
        static string F(double v, string fmt) { return v.ToString(fmt, CultureInfo.CurrentCulture); }

        // ------------------------------------------------------------ építőelemek
        Border Card(string title, UIElement body)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = White, FontWeight = FontWeights.Bold, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(body);
            return new Border { Background = CardBg, BorderBrush = CardBd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 13, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = sp };
        }

        void AddSlider(Panel parent, string label, string key, double min, double max, double step, string fmt, string unit)
        {
            Grid head = new Grid { Margin = new Thickness(0, 8, 0, 0) }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock lb = new TextBlock { Text = label, Foreground = Br(255, 214, 214, 220), FontSize = 12.5, ToolTip = Loc.T("Dupla kattintás = alapérték") };
            TextBlock val = new TextBlock { Foreground = Br(255, 120, 180, 255), FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas") }; Grid.SetColumn(val, 1);
            head.Children.Add(lb); head.Children.Add(val);
            Slider sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, IsMoveToPointEnabled = true, Focusable = false };
            if (sliderStyle != null) sl.Style = sliderStyle; if (step > 0) { sl.TickFrequency = step; sl.IsSnapToTickEnabled = true; }
            Action show = delegate { val.Text = F(sl.Value, fmt) + unit; };
            sl.ValueChanged += delegate { show(); if (!updating) Set(key, sl.Value); };
            lb.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { if (e.ClickCount == 2) { sl.Value = Def[key]; } };
            refreshers.Add(delegate { sl.Value = Math.Max(min, Math.Min(max, Num(key))); show(); });
            updating = true; sl.Value = Math.Max(min, Math.Min(max, Num(key))); show(); updating = false;
            parent.Children.Add(head); parent.Children.Add(sl);
        }

        void AddToggle(Panel parent, string label, string key, string note)
        {
            Grid row = new Grid { Margin = new Thickness(0, 7, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = label, Foreground = Br(255, 214, 214, 220), FontSize = 12.5 });
            if (note != null) tx.Children.Add(new TextBlock { Text = note, Foreground = Muted, FontSize = 10.5 });
            Border track = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand }; Grid.SetColumn(track, 1);
            Ellipse knob = new Ellipse { Width = 22, Height = 22, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.35 } };
            track.Child = knob;
            Action paint = delegate { bool on = Num(key) > 0.5; track.Background = on ? Br(255, 48, 209, 88) : Br(110, 120, 120, 128); knob.Margin = new Thickness(on ? 22 : 2, 0, 0, 0); };
            track.MouseLeftButtonUp += delegate { Set(key, Num(key) > 0.5 ? 0 : 1); paint(); };
            refreshers.Add(paint); paint();
            row.Children.Add(tx); row.Children.Add(track); parent.Children.Add(row);
        }

        Border PillBtn(string text, Action click, bool accent)
        {
            Border b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = accent ? Br(255, 10, 132, 255) : Br(70, 255, 255, 255) };
            b.Child = new TextBlock { Text = text, Foreground = White, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            b.MouseEnter += delegate { b.Opacity = 0.82; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        static Color Hsv(double h, double s, double v)
        {
            s /= 100; v /= 100; double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0; int i = (int)(h / 60) % 6;
            if (i == 0) { r = c; g = x; } else if (i == 1) { r = x; g = c; } else if (i == 2) { g = c; b = x; } else if (i == 3) { g = x; b = c; } else if (i == 4) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
        static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            h = d == 0 ? 0 : (mx == r ? 60 * (((g - b) / d) % 6) : (mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4))); if (h < 0) h += 360; s = mx == 0 ? 0 : d / mx * 100; v = mx * 100;
        }
        Color TintColor() { return Color.FromRgb((byte)Num("tintr"), (byte)Num("tintg"), (byte)Num("tintb")); }
        void SetTint(Color c) { Set("tintr", c.R); Set("tintg", c.G); Set("tintb", c.B); }

        // ------------------------------------------------------------ az egész fül
        void Build()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            // iOS-kinézet ki/be: kikapcsolva a dokk nem fut, a tálca, a Start, a felugró ablakok és az ikonok eredetiek (az ikonok elrendezése marad)
            string dockDir0 = System.IO.Path.GetDirectoryName(dockExe), offMark = System.IO.Path.Combine(dockDir0, "disabled.txt");
            Border offCard = new Border { Background = Br(255, 44, 44, 50), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10) };
            DockPanel offRow = new DockPanel(); Border onBtn = PillBtn(Loc.T("Bekapcsolás"), delegate { try { System.IO.File.Delete(offMark); } catch { } offCard.Visibility = Visibility.Collapsed; Flush(); runPs("Start-Process explorer.exe '" + dockExe + "'"); }, true);
            DockPanel.SetDock(onBtn, Dock.Right); offRow.Children.Add(onBtn);
            offRow.Children.Add(new TextBlock { Text = Loc.T("Az iOS kinézet ki van kapcsolva – minden az eredeti Windows-kinézetben van."), Foreground = White, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            offCard.Child = offRow; offCard.Visibility = System.IO.File.Exists(offMark) ? Visibility.Visible : Visibility.Collapsed; root.Children.Add(offCard);
            root.Children.Add(new TextBlock { Text = Loc.T("Minden változtatás élőben látszik a dokkon, a pilulákon, a Start-menün és a Spotlighton (kb. 0,3 mp). Dupla kattintás egy csúszka nevén = alapérték."), Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) });

            // előnézet
            Border pv = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Height = 262, Margin = new Thickness(0, 0, 0, 12), BorderBrush = CardBd, BorderThickness = new Thickness(1) };
            Grid pg = new Grid(); backdrop = new Canvas { Width = 900, Height = 262, HorizontalAlignment = HorizontalAlignment.Center }; overlay = new Grid { Width = 900, Height = 262, HorizontalAlignment = HorizontalAlignment.Center };
            BuildBackdrop(); pg.Children.Add(backdrop); pg.Children.Add(overlay); pv.Child = pg; root.Children.Add(pv);

            // előbeállítások
            WrapPanel pre = new WrapPanel();
            Action<string, Dictionary<string, double>> addPre = delegate (string n, Dictionary<string, double> d) { presets.Add(new KeyValuePair<string, Dictionary<string, double>>(n, d)); pre.Children.Add(PillBtn(n, delegate { SetMany(d); }, false)); };
            addPre(Loc.T("iOS alap"), new Dictionary<string, double> { { "glass", 135 }, { "tintr", 17 }, { "tintg", 20 }, { "tintb", 26 }, { "blur", 16 }, { "frostsat", 100 }, { "frostbright", 100 }, { "shine", 34 }, { "borderalpha", 80 }, { "borderr", 255 }, { "borderg", 255 }, { "borderb", 255 } });
            addPre(Loc.T("Üvegtiszta"), new Dictionary<string, double> { { "glass", 38 }, { "tintr", 20 }, { "tintg", 28 }, { "tintb", 40 }, { "blur", 24 }, { "frostsat", 140 }, { "frostbright", 112 }, { "shine", 64 }, { "borderalpha", 150 } });
            addPre(Loc.T("Mély éjszaka"), new Dictionary<string, double> { { "glass", 195 }, { "tintr", 6 }, { "tintg", 8 }, { "tintb", 14 }, { "blur", 14 }, { "frostsat", 90 }, { "frostbright", 70 }, { "shine", 16 }, { "borderalpha", 46 } });
            addPre(Loc.T("Jég"), new Dictionary<string, double> { { "glass", 100 }, { "tintr", 190 }, { "tintg", 215 }, { "tintb", 255 }, { "blur", 22 }, { "frostsat", 125 }, { "frostbright", 130 }, { "shine", 90 }, { "borderalpha", 170 } });
            addPre(Loc.T("Rózsa"), new Dictionary<string, double> { { "glass", 96 }, { "tintr", 255 }, { "tintg", 150 }, { "tintb", 190 }, { "blur", 20 }, { "frostsat", 130 }, { "frostbright", 115 }, { "shine", 80 }, { "borderalpha", 160 } });
            addPre("Matrix", new Dictionary<string, double> { { "glass", 160 }, { "tintr", 0 }, { "tintg", 36 }, { "tintb", 10 }, { "blur", 12 }, { "frostsat", 90 }, { "frostbright", 75 }, { "shine", 20 }, { "borderalpha", 150 }, { "borderr", 0 }, { "borderg", 255 }, { "borderb", 90 } });
            addPre(Loc.T("Átlátszó"), new Dictionary<string, double> { { "glass", 0 }, { "blur", 0 }, { "shine", 22 }, { "borderalpha", 100 } });
            addPre(Loc.T("Nagyon kerek"), new Dictionary<string, double> { { "round", 1 }, { "menuround", 40 }, { "tileround", 28 }, { "flyoutround", 20 } });
            addPre(Loc.T("Éles sarkok"), new Dictionary<string, double> { { "round", 0.12 }, { "menuround", 6 }, { "tileround", 4 }, { "flyoutround", 2 } });
            root.Children.Add(Card(Loc.T("Előbeállítások"), pre));

            // háttér (Wallpaper Engine) kezelése
            StackPanel weBox = new StackPanel(); WrapPanel wpb = new WrapPanel(); TextBlock wst = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 168)), FontSize = 12, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Text = Loc.T("Ha eltűnt a háttér: a Wallpaper Engine újraindítása visszahozza (kb. 8 mp).") };
            wpb.Children.Add(PillBtn(Loc.T("🔄 Háttér újraindítása"), delegate { wst.Text = Loc.T("Újraindítás…"); FocusMode.RestartWallpaper(delegate (string m) { Dispatcher.BeginInvoke(new Action(delegate { wst.Text = m; })); }); }, true));
            wpb.Children.Add(PillBtn(Loc.T("▶ Lejátszás"), delegate { FocusMode.Control("play"); wst.Text = Loc.T("Lejátszás kérve."); }, false)); wpb.Children.Add(PillBtn(Loc.T("⏸ Szünet"), delegate { FocusMode.Control("pause"); wst.Text = Loc.T("Szünet kérve."); }, false));
            weBox.Children.Add(wpb); weBox.Children.Add(wst); root.Children.Add(Card(Loc.T("Háttér (Wallpaper Engine)"), weBox));

            // két oszlop
            Grid cols = new Grid(); cols.ColumnDefinitions.Add(new ColumnDefinition()); cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); cols.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel left = new StackPanel(), right = new StackPanel(); Grid.SetColumn(right, 2); cols.Children.Add(left); cols.Children.Add(right);

            StackPanel glass = new StackPanel();
            AddSlider(glass, Loc.T("Üveg sűrűsége (0 = tiszta, 240 = tömör)"), "glass", 0, 240, 1, "0", "");
            AddSlider(glass, Loc.T("Elmosás ereje"), "blur", 0, 40, 1, "0", " px");
            AddSlider(glass, Loc.T("Háttér telítettsége"), "frostsat", 0, 250, 5, "0", " %");
            AddSlider(glass, Loc.T("Háttér fényereje"), "frostbright", 30, 220, 5, "0", " %");
            AddSlider(glass, Loc.T("Fényes felső sáv"), "shine", 0, 160, 1, "0", "");
            AddSlider(glass, Loc.T("Keret erőssége"), "borderalpha", 0, 255, 1, "0", "");
            left.Children.Add(Card(Loc.T("Üveg"), glass));

            // szín
            StackPanel col = new StackPanel();
            Border swatch = new Border { Height = 30, CornerRadius = new CornerRadius(8), BorderBrush = Br(120, 255, 255, 255), BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 4) };
            TextBox hex = new TextBox { Width = 90, Background = Br(255, 40, 40, 43), Foreground = White, BorderBrush = CardBd, CaretBrush = White, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Padding = new Thickness(5, 3, 5, 3), HorizontalAlignment = HorizontalAlignment.Left };
            double hh0, ss0, vv0; ToHsv(TintColor(), out hh0, out ss0, out vv0); double[] hsv = { hh0, ss0, vv0 };
            Slider[] hs = new Slider[3]; TextBlock[] hv = new TextBlock[3]; string[] hn = { Loc.T("Árnyalat"), Loc.T("Telítettség"), Loc.T("Világosság") }; double[] hmax = { 360, 100, 100 }; string[] hu = { " °", " %", " %" };
            Action paintSw = delegate { Color c = TintColor(); swatch.Background = new SolidColorBrush(c); if (!hex.IsKeyboardFocused) { bool u0 = updating; updating = true; hex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); updating = u0; } };
            col.Children.Add(swatch);
            for (int i = 0; i < 3; i++)
            {
                int idx = i; Grid hd = new Grid { Margin = new Thickness(0, 6, 0, 0) }; hd.ColumnDefinitions.Add(new ColumnDefinition()); hd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hd.Children.Add(new TextBlock { Text = hn[i], Foreground = Br(255, 214, 214, 220), FontSize = 12.5 });
                hv[i] = new TextBlock { Foreground = Br(255, 120, 180, 255), FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas") }; Grid.SetColumn(hv[i], 1); hd.Children.Add(hv[i]);
                hs[i] = new Slider { Minimum = 0, Maximum = hmax[i], IsMoveToPointEnabled = true, Focusable = false, Value = hsv[i] }; if (sliderStyle != null) hs[i].Style = sliderStyle;
                hs[i].ValueChanged += delegate { hv[idx].Text = F(hs[idx].Value, "0") + hu[idx]; if (updating) return; SetTint(Hsv(hs[0].Value, hs[1].Value, hs[2].Value)); paintSw(); };
                hv[i].Text = F(hsv[i], "0") + hu[i]; col.Children.Add(hd); col.Children.Add(hs[i]);
            }
            hex.TextChanged += delegate
            {
                if (updating) return;
                string t = hex.Text.Trim().TrimStart('#'); int n; if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                { Color c = Color.FromRgb((byte)(n >> 16), (byte)(n >> 8), (byte)n); SetTint(c); double a, b, d; ToHsv(c, out a, out b, out d); updating = true; hs[0].Value = a; hs[1].Value = b; hs[2].Value = d; updating = false; swatch.Background = new SolidColorBrush(c); }
            };
            refreshers.Add(delegate { double a, b, d; ToHsv(TintColor(), out a, out b, out d); hs[0].Value = a; hs[1].Value = b; hs[2].Value = d; paintSw(); });
            col.Children.Add(new TextBlock { Text = Loc.T("Hexa kód"), Foreground = Br(255, 214, 214, 220), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 3) }); col.Children.Add(hex);
            WrapPanel sw = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            int[][] sws = { new[] { 17, 20, 26 }, new[] { 28, 28, 30 }, new[] { 0, 0, 0 }, new[] { 14, 26, 54 }, new[] { 14, 40, 30 }, new[] { 50, 16, 28 }, new[] { 38, 22, 66 }, new[] { 80, 50, 14 }, new[] { 205, 225, 255 }, new[] { 255, 205, 220 }, new[] { 245, 245, 247 } };
            foreach (int[] c3 in sws) { int[] cc = c3; Border dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb((byte)cc[0], (byte)cc[1], (byte)cc[2])), BorderBrush = Br(140, 255, 255, 255), BorderThickness = new Thickness(1) }; dot.MouseLeftButtonUp += delegate { SetTint(Color.FromRgb((byte)cc[0], (byte)cc[1], (byte)cc[2])); Refresh(); }; sw.Children.Add(dot); }
            col.Children.Add(sw);
            col.Children.Add(new TextBlock { Text = Loc.T("Keret színe"), Foreground = Br(255, 214, 214, 220), FontSize = 12.5, Margin = new Thickness(0, 4, 0, 4) });
            WrapPanel bw = new WrapPanel();
            int[][] bws = { new[] { 255, 255, 255 }, new[] { 120, 190, 255 }, new[] { 90, 255, 150 }, new[] { 255, 150, 200 }, new[] { 255, 190, 90 }, new[] { 190, 140, 255 } };
            foreach (int[] c3 in bws) { int[] cc = c3; Border dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb((byte)cc[0], (byte)cc[1], (byte)cc[2])), BorderBrush = Br(140, 255, 255, 255), BorderThickness = new Thickness(1) }; dot.MouseLeftButtonUp += delegate { Set("borderr", cc[0]); Set("borderg", cc[1]); Set("borderb", cc[2]); }; bw.Children.Add(dot); }
            col.Children.Add(bw);
            right.Children.Add(Card(Loc.T("Színárnyalat"), col));
            paintSw();

            StackPanel rnd = new StackPanel();
            AddSlider(rnd, Loc.T("Dokk és pilulák (0 = szögletes, 1 = kerek)"), "round", 0, 1, 0.05, "0.00", "");
            AddSlider(rnd, Loc.T("Start-menü és Spotlight"), "menuround", 0, 44, 1, "0", " px");
            AddSlider(rnd, Loc.T("Ikon-csempék (dokk, menü)"), "tileround", 0, 29, 1, "0", " px");
            AddSlider(rnd, Loc.T("Felugró ablakok (naptár, hangerő…)"), "flyoutround", 0, 24, 1, "0", " px");
            AddSlider(rnd, Loc.T("Felugró ablakok távolsága a széltől"), "flyoutgap", 0, 30, 1, "0", " px");
            left.Children.Add(Card(Loc.T("Kerekítés"), rnd));

            StackPanel ico = new StackPanel();
            AddSlider(ico, Loc.T("Csempe-üveg erőssége"), "tilealpha", 0, 140, 1, "0", "");
            AddSlider(ico, Loc.T("Ikonok fényereje"), "iconbright", 30, 170, 1, "0", " %");
            AddToggle(ico, Loc.T("Fekete-fehér ikonok"), "mono", null);
            AddToggle(ico, Loc.T("Üveg csempék a dokkban"), "tiles", null);
            AddSlider(ico, Loc.T("Animációk sebessége"), "animspeed", 0.4, 2.5, 0.1, "0.0", "×");
            right.Children.Add(Card(Loc.T("Ikonok és mozgás"), ico));

            // betűtípus
            StackPanel fnt = new StackPanel();
            fontNow = new TextBlock { Foreground = Br(255, 120, 180, 255), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) };
            TextBox fs = new TextBox { Background = Br(255, 40, 40, 43), Foreground = White, BorderBrush = CardBd, CaretBrush = White, FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6), ToolTip = Loc.T("Keresés a betűtípusok között") };
            ListBox fsv = new ListBox { Height = 150, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = White };
            VirtualizingStackPanel.SetIsVirtualizing(fsv, true); VirtualizingStackPanel.SetVirtualizationMode(fsv, VirtualizationMode.Recycling); ScrollViewer.SetHorizontalScrollBarVisibility(fsv, ScrollBarVisibility.Disabled);
            FrameworkElementFactory ftf = new FrameworkElementFactory(typeof(TextBlock)); ftf.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); ftf.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding()); ftf.SetValue(TextBlock.FontSizeProperty, 14.0); ftf.SetValue(TextBlock.ForegroundProperty, White); ftf.SetValue(TextBlock.MarginProperty, new Thickness(4, 1, 4, 1));
            fsv.ItemTemplate = new DataTemplate { VisualTree = ftf };
            List<string> fams = null;
            Action<string> setFont = delegate (string name) { SetRaw("ufont", name); fontNow.Text = Loc.T("Aktuális betű: ") + name; };
            bool fontSync = false;
            Action markFont = delegate { string cur = Str("ufont", "Inter"); fontNow.Text = Loc.T("Aktuális betű: ") + cur; if (fams != null) { fontSync = true; fsv.SelectedItem = fams.Contains(cur) ? cur : null; fontSync = false; } };
            fsv.SelectionChanged += delegate { string n = fsv.SelectedItem as string; if (!fontSync && n != null) setFont(n); };
            fsv.Loaded += delegate { if (fams != null) return; fams = new List<string>(); foreach (FontFamily ff in Fonts.SystemFontFamilies) fams.Add(ff.Source); fams.Sort(StringComparer.CurrentCultureIgnoreCase); fsv.ItemsSource = fams; markFont(); };
            refreshers.Add(markFont); markFont();
            fs.TextChanged += delegate { if (fams == null) return; string q = fs.Text.Trim().ToLowerInvariant(); System.ComponentModel.ICollectionView cv = System.Windows.Data.CollectionViewSource.GetDefaultView(fams); cv.Filter = q.Length == 0 ? null : (Predicate<object>)delegate (object o) { return ((string)o).ToLowerInvariant().Contains(q); }; };
            fnt.Children.Add(fontNow); fnt.Children.Add(fs); fnt.Children.Add(fsv);
            AddSlider(fnt, Loc.T("Betűméret (menük, Spotlight)"), "ufontscale", 0.7, 1.6, 0.05, "0.00", "×");
            fnt.Children.Add(new TextBlock { Text = Loc.T("Vastagság"), Foreground = Br(255, 214, 214, 220), FontSize = 12.5, Margin = new Thickness(0, 10, 0, 4) });
            WrapPanel wp = new WrapPanel(); string[] wn = { Loc.T("Normál"), Loc.T("Közepes"), Loc.T("Félkövér"), Loc.T("Kövér") }; List<Border> wb = new List<Border>();
            Action paintW = delegate { for (int i = 0; i < wb.Count; i++) wb[i].Background = Math.Round(Num("ufontweight")) == i ? Br(255, 10, 132, 255) : Br(70, 255, 255, 255); };
            for (int i = 0; i < 4; i++) { int w = i; Border b = PillBtn(wn[i], delegate { Set("ufontweight", w); paintW(); }, false); wb.Add(b); wp.Children.Add(b); }
            refreshers.Add(paintW); paintW(); fnt.Children.Add(wp);
            left.Children.Add(Card(Loc.T("Betűtípus"), fnt));

            StackPanel sw2 = new StackPanel();
            AddToggle(sw2, Loc.T("Dokk automatikus elrejtése"), "autohide", null);
            AddToggle(sw2, Loc.T("Tálca-pilulák (Start, óra)"), "pills", null);
            AddToggle(sw2, Loc.T("Start gomb a dokkban"), "start", null);
            AddToggle(sw2, Loc.T("Saját Start-menü"), "glassstart", Loc.T("újraindítás kell hozzá"));
            AddToggle(sw2, "Spotlight (Alt+Space)", "spotlight", Loc.T("újraindítás kell hozzá"));
            AddToggle(sw2, Loc.T("Felugró ablakok lekerekítése"), "flyoutstyle", Loc.T("újraindítás kell hozzá"));
            AddToggle(sw2, Loc.T("Asztali ikonok üveg csempéi"), "desktoptiles", Loc.T("újraindítás kell hozzá"));
            right.Children.Add(Card(Loc.T("Kapcsolók"), sw2));

            root.Children.Add(cols);

            WrapPanel act = new WrapPanel();
            act.Children.Add(PillBtn(Loc.T("Dokk újraindítása"), delegate { Flush(); runPs("Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1; Start-Process explorer.exe '" + dockExe + "'"); }, true));
            act.Children.Add(PillBtn(Loc.T("Vissza az eredetihez"), delegate
            {
                if (MessageBox.Show(Loc.T("Leveszi az iOS kinézetet mindenhonnan (dokk, tálca, Start menü, felugró ablakok, asztali ikonok képe). Az ikonok elrendezése nem változik. Később a „Bekapcsolás” gombbal visszakapcsolható."), "Glass UI", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                try { System.IO.File.WriteAllText(offMark, DateTime.Now.ToString("s")); } catch { } offCard.Visibility = Visibility.Visible;
                runPs("Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 600; $d='" + dockDir0 + "'; Start-Process (Join-Path $d 'GlassDock.exe') '/restore' -Wait; Start-Process (Join-Path $d 'GlassDock.exe') '/untile' -Wait; if (Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons' -Name 29 -ErrorAction SilentlyContinue) { & (Join-Path $d 'Restore-ShortcutOverlays.ps1') }");
            }, false));
            act.Children.Add(PillBtn(Loc.T("Minden alapértékre"), delegate { Dictionary<string, double> d = new Dictionary<string, double>(Def); SetMany(d); SetRaw("ufont", "Inter"); Refresh(); }, false));
            act.Children.Add(PillBtn(Loc.T("Asztali ikonok elrendezésének visszaállítása"), delegate { runPs("powershell -ExecutionPolicy Bypass -File '" + System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dockExe), "Restore-DesktopLayout.ps1") + "' -Apply"); }, false));
            act.Children.Add(PillBtn(Loc.T("Parancsikon nyilak és pajzsok elrejtése"), delegate { runPs("powershell -ExecutionPolicy Bypass -File '" + System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dockExe), "Hide-ShortcutOverlays.ps1") + "'"); }, false));
            root.Children.Add(Card(Loc.T("Műveletek"), act));
            Content = root;
            GlassMenu.Attach(this, delegate
            {
                List<MenuEntry> pm = new List<MenuEntry>(); foreach (KeyValuePair<string, Dictionary<string, double>> pr in presets) { Dictionary<string, double> d = pr.Value; pm.Add(GlassMenu.Item(pr.Key, delegate { SetMany(d); })); }
                return new MenuEntry[] {
                    GlassMenu.Menu(Loc.T("Előbeállítás"), pm.ToArray()),
                    GlassMenu.Item(Loc.T("Minden alapértékre"), delegate { SetMany(new Dictionary<string, double>(Def)); SetRaw("ufont", "Inter"); Refresh(); }),
                    GlassMenu.Sep(),
                    GlassMenu.Item(Loc.T("Dokk újraindítása"), delegate { Flush(); runPs("Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1; Start-Process explorer.exe '" + dockExe + "'"); }),
                    GlassMenu.Item(Loc.T("Beállításfájl megnyitása (settings.ini)"), delegate { try { Flush(); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }),
                    GlassMenu.Item(Loc.T("Dokk mappa megnyitása"), delegate { try { System.Diagnostics.Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(path)); } catch { } })
                };
            });
        }

        // ------------------------------------------------------------ élő előnézet (a valódi hatás a dokkon látszik; ez közelítés)
        void BuildBackdrop()
        {
            backdrop.Children.Clear();
            Rectangle bg = new Rectangle { Width = 900, Height = 262, Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromRgb(36, 52, 120), 0), new GradientStop(Color.FromRgb(120, 56, 150), 0.5), new GradientStop(Color.FromRgb(230, 120, 80), 1) }, new Point(0, 0), new Point(1, 1)) };
            backdrop.Children.Add(bg);
            int[][] blobs = { new[] { 120, 60, 130, 255, 90, 90 }, new[] { 330, 150, 150, 80, 220, 255 }, new[] { 560, 40, 120, 255, 220, 90 }, new[] { 700, 160, 150, 120, 255, 160 }, new[] { 450, 190, 90, 255, 120, 220 }, new[] { 40, 170, 100, 90, 140, 255 } };
            foreach (int[] b in blobs) { Ellipse e = new Ellipse { Width = b[2], Height = b[2], Fill = new SolidColorBrush(Color.FromRgb((byte)b[3], (byte)b[4], (byte)b[5])) }; Canvas.SetLeft(e, b[0]); Canvas.SetTop(e, b[1]); backdrop.Children.Add(e); }
            for (int i = 0; i < 9; i++) { Rectangle st = new Rectangle { Width = 14, Height = 262, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) }; Canvas.SetLeft(st, 40 + i * 100); backdrop.Children.Add(st); }
        }

        void Glass(double x, double y, double w, double h, double rad, out Grid inner)
        {
            double blur = Num("blur"), pad = Math.Max(2, blur * 2); Color tint = TintColor(); double gl = Num("glass"), br = Num("frostbright") / 100.0;
            Grid host = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0), Clip = new RectangleGeometry(new Rect(0, 0, w, h), rad, rad) };
            Rectangle copy = new Rectangle { Width = w + 2 * pad, Height = h + 2 * pad, Margin = new Thickness(-pad), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Fill = new VisualBrush(backdrop) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(x - pad + (900 - 900) / 2.0, y - pad, w + 2 * pad, h + 2 * pad), Stretch = Stretch.Fill } };
            if (blur > 0) copy.Effect = new BlurEffect { Radius = blur * 1.1, KernelType = KernelType.Gaussian };
            host.Children.Add(copy);
            if (Math.Abs(br - 1) > 0.02) host.Children.Add(new Rectangle { Fill = br < 1 ? Br((byte)Math.Min(255, (1 - br) * 255), 0, 0, 0) : Br((byte)Math.Min(255, (br - 1) * 160), 255, 255, 255) });
            host.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Max(0, Math.Min(240, gl)), tint.R, tint.G, tint.B)) });
            Color bc = Color.FromRgb((byte)Num("borderr"), (byte)Num("borderg"), (byte)Num("borderb")); byte sh = (byte)Math.Max(0, Math.Min(255, Num("shine")));
            host.Children.Add(new Rectangle { RadiusX = rad, RadiusY = rad, Margin = new Thickness(0.5), Fill = new LinearGradientBrush(Color.FromArgb(sh, 255, 255, 255), Color.FromArgb((byte)(sh / 17), 255, 255, 255), 90), Stroke = new SolidColorBrush(Color.FromArgb((byte)Num("borderalpha"), bc.R, bc.G, bc.B)), StrokeThickness = 1 });
            overlay.Children.Add(host); inner = host;
        }

        Border Tile(double size, string glyph)
        {
            byte ta = (byte)Math.Max(0, Math.Min(255, Num("tilealpha"))); double rr = Num("tileround") * size / 58.0;
            Border t = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(rr), BorderBrush = Br(85, 255, 255, 255), BorderThickness = new Thickness(1), Background = new LinearGradientBrush(Color.FromArgb(ta, 255, 255, 255), Color.FromArgb((byte)(ta / 3), 255, 255, 255), 90), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            double g = Num("iconbright") / 100.0; byte gv = (byte)Math.Max(60, Math.Min(255, 235 * g));
            t.Child = new TextBlock { Text = glyph, Foreground = new SolidColorBrush(Color.FromRgb(gv, gv, gv)), FontSize = size * 0.42, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Symbol") };
            return t;
        }

        void BuildPreview()
        {
            if (overlay == null) return; overlay.Children.Clear();
            FontFamily ff = new FontFamily(Str("ufont", "Inter") + ", Inter, Segoe UI"); double fsz = Num("ufontscale"); int wi = (int)Math.Round(Num("ufontweight"));
            FontWeight w0 = wi == 0 ? FontWeights.Normal : (wi == 1 ? FontWeights.Medium : (wi == 2 ? FontWeights.SemiBold : FontWeights.Bold)), w1 = wi <= 0 ? FontWeights.Medium : (wi == 1 ? FontWeights.SemiBold : FontWeights.Bold);
            // dokk
            double dh = 58, dw = 360, dr = Math.Min(dh / 2, Num("round") * dh / 2); Grid dk;
            Glass(270, 188, dw, dh, dr, out dk);
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            string[] gl = { "▶", "★", "⚙", "✉", "♫" }; foreach (string g in gl) { Border t = Tile(40, g); t.Margin = new Thickness(6, 0, 6, 0); sp.Children.Add(t); }
            dk.Children.Add(sp);
            // Start-menü / Spotlight jellegű kártya
            double mw = 360, mh = 150, mr = Math.Min(Num("menuround"), mh / 2); Grid mc; Glass(40, 22, mw, mh, mr, out mc);
            StackPanel ms = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
            Border sbx = new Border { CornerRadius = new CornerRadius(Math.Min(14, Num("menuround") * 0.55)), Background = Br(40, 255, 255, 255), BorderBrush = Br(70, 255, 255, 255), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6) };
            sbx.Child = new TextBlock { Text = Loc.T("Keresés"), Foreground = Br(170, 255, 255, 255), FontFamily = ff, FontSize = 15 * fsz, FontWeight = w0 };
            ms.Children.Add(sbx);
            for (int i = 0; i < 2; i++)
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) }; Border t = Tile(34, i == 0 ? "▶" : "⚙"); row.Children.Add(t);
                StackPanel tx = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(new TextBlock { Text = i == 0 ? "Terminal" : Loc.T("Beállítások"), Foreground = White, FontFamily = ff, FontSize = 14 * fsz, FontWeight = w1 });
                tx.Children.Add(new TextBlock { Text = Loc.T("Alkalmazás"), Foreground = Br(125, 255, 255, 255), FontFamily = ff, FontSize = 10.5 * fsz, FontWeight = w0 });
                row.Children.Add(tx); ms.Children.Add(row);
            }
            mc.Children.Add(ms);
            // felugró ablak (naptár-szerű)
            double fw = 200, fh = 120, fr = Math.Min(Num("flyoutround"), 24); Grid fl; Glass(660, 70, fw, fh, fr, out fl);
            StackPanel fp = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
            fp.Children.Add(new TextBlock { Text = "19:24:34", Foreground = White, FontFamily = ff, FontSize = 24 * fsz, FontWeight = w0 }); fp.Children.Add(new TextBlock { Text = Loc.T("2026. október 10., szombat"), Foreground = Br(255, 120, 180, 255), FontFamily = ff, FontSize = 11 * fsz, FontWeight = w0, Margin = new Thickness(0, 4, 0, 0) });
            fl.Children.Add(fp);
        }
    }

    // iOS-szerű, vékony, áttetsző görgetősáv az egész alkalmazásban (nyilak és szürke sáv nélkül)
    public static class ScrollTheme
    {
        const string Xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='SBThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='B' CornerRadius='4' Background='#59FFFFFF' Margin='3'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#99FFFFFF'/></Trigger>
            <Trigger Property='IsDragging' Value='True'><Setter TargetName='B' Property='Background' Value='#CCFFFFFF'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='SBPage' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <ControlTemplate x:Key='SBV' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Style='{StaticResource SBPage}'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Style='{StaticResource SBPage}'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource SBThumb}'/></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <ControlTemplate x:Key='SBH' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track'>
        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Style='{StaticResource SBPage}'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Style='{StaticResource SBPage}'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource SBThumb}'/></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='13'/>
    <Setter Property='MinWidth' Value='13'/>
    <Setter Property='Template' Value='{StaticResource SBV}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='13'/>
        <Setter Property='Template' Value='{StaticResource SBH}'/>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
        public static void Apply(FrameworkElement fe)
        {
            try { ResourceDictionary rd = (ResourceDictionary)XamlReader.Parse(Xaml); fe.Resources.MergedDictionaries.Add(rd); } catch { }
        }
    }
}
