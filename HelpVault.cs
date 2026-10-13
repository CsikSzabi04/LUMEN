// Lumen – Súgó: fa-nézetű "vault" (mind-map): kártyák, összecsukható ágak, nagyítás/húzás, sárga jegyzetek (összeköthetők), saját jegyzetek és ötletek.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using GlassTerm;

namespace GlassControlWpf
{
    public class HelpItem
    {
        public string Id, Title, Status = "", Desc = "", Icon = "", Action; public int Depth; public bool Collapsed, Custom; public List<HelpItem> Kids = new List<HelpItem>(); public HelpItem Parent; public Border Card; public Rect Box;
        public string Cat { get { HelpItem p = this; while (p.Parent != null) p = p.Parent; return p.Title; } }
    }
    class HelpNote { public string Id, Link = ""; public double X, Y, W = 170, H = 110; public string Text = ""; public Border El; public TextBox Tb; public Shapes.Path Curve; }

    public class HelpVault : Grid
    {
        public Action<string> Navigate;
        string dir, file; List<HelpItem> roots = new List<HelpItem>(); Dictionary<string, HelpItem> byId = new Dictionary<string, HelpItem>(); List<HelpNote> notes = new List<HelpNote>(); Dictionary<string, string> personal = new Dictionary<string, string>();
        Canvas world = new Canvas(); Border viewport; ScaleTransform st = new ScaleTransform(1, 1); TranslateTransform tt = new TranslateTransform(); Grid body; ScrollViewer listView; StackPanel listPanel; Border detail; TextBlock zoomTxt; TextBox search;
        int mode; string statusFilter = ""; HelpItem sel; DispatcherTimer saveT; bool fitted; Point panStart, panOrig; bool panning; List<Border> tabBtns = new List<Border>(); int idSeq = 1;
        static Brush Br(byte r, byte g, byte b, byte a) { SolidColorBrush s = new SolidColorBrush(Color.FromArgb(a, r, g, b)); s.Freeze(); return s; }
        static readonly Brush Muted = Br(150, 150, 158, 255), Card = Br(37, 37, 39, 255), CardBd = Br(255, 255, 255, 22), Orange = Br(245, 166, 35, 255), Blue = Br(10, 132, 255, 255), Green = Br(48, 209, 88, 255), Teal = Br(64, 200, 190, 255);

        static Brush StatusBr(string s) { return s == Loc.T("Kész") ? Green : s == Loc.T("Folyamatban") ? Orange : s == Loc.T("Terv") ? Blue : Muted; }

        public HelpVault(string appDir)
        {
            dir = System.IO.Path.Combine(appDir, "help"); try { Directory.CreateDirectory(dir); } catch { } file = System.IO.Path.Combine(dir, "notes.txt"); ScrollTheme.Apply(this);
            HelpContent.Build(roots, byId); Load();
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // fejléc
            Grid head = new Grid { Margin = new Thickness(6, 2, 6, 8) }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = Loc.T("📖  Súgó"), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            StackPanel hr = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(hr, 1);
            search = new TextBox { Width = 230, Background = Br(255, 255, 255, 12), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = CardBd, Padding = new Thickness(8, 4, 8, 4), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0) }; GlassMenu.ForTextBox(search); search.ToolTip = Loc.T("Keresés a súgóban");
            search.TextChanged += delegate { Refresh(); }; hr.Children.Add(search);
            Border add = Pill(Loc.T("+ Új ▾"), true); add.MouseLeftButtonUp += delegate { GlassMenu.Show(add, GlassMenu.Item(Loc.T("Új sárga jegyzet"), delegate { AddNote(); }), GlassMenu.Item(Loc.T("Új saját ötlet / bejegyzés"), delegate { AddCustom(); })); }; hr.Children.Add(add); head.Children.Add(hr); Grid.SetRow(head, 0); Children.Add(head);
            Border lng = Pill("🌐 " + Loc.LangName + " ▾", false); lng.Margin = new Thickness(0, 0, 8, 0); hr.Children.Insert(1, lng);
            lng.MouseLeftButtonUp += delegate
            {
                List<MenuEntry> li = new List<MenuEntry>();
                for (int i = 0; i < Loc.Codes.Length; i++) { string c = Loc.Codes[i]; li.Add(GlassMenu.Item((c == Loc.Lang ? "✓  " : "     ") + Loc.Names[i], delegate { ChangeLang(c); })); }
                GlassMenu.Show(lng, li.ToArray());
            };
            // fülek
            StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) }; string[] tn = { Loc.T("🌳 Fa"), Loc.T("☰ Lista"), Loc.T("💡 Ötletek"), Loc.T("📝 Jegyzetek") };
            for (int i = 0; i < tn.Length; i++) { int k = i; Border b = Pill(tn[i], false); b.MouseLeftButtonUp += delegate { SetMode(k); }; tabBtns.Add(b); tabs.Children.Add(b); }
            Grid.SetRow(tabs, 1); Children.Add(tabs);
            // törzs
            body = new Grid(); Grid.SetRow(body, 2); Children.Add(body);
            viewport = new Border { ClipToBounds = true, Background = Dots(), CornerRadius = new CornerRadius(10), BorderBrush = CardBd, BorderThickness = new Thickness(1), Child = world };
            TransformGroup tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tt); world.RenderTransform = tg; world.Background = Brushes.Transparent; world.HorizontalAlignment = HorizontalAlignment.Left; world.VerticalAlignment = VerticalAlignment.Top;
            viewport.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { if (e.Handled) return; panning = true; panStart = e.GetPosition(viewport); panOrig = new Point(tt.X, tt.Y); viewport.CaptureMouse(); };
            viewport.MouseMove += delegate (object o, MouseEventArgs e) { if (!panning) return; Point p = e.GetPosition(viewport); tt.X = panOrig.X + p.X - panStart.X; tt.Y = panOrig.Y + p.Y - panStart.Y; };
            viewport.MouseLeftButtonUp += delegate { panning = false; viewport.ReleaseMouseCapture(); };
            viewport.MouseWheel += delegate (object o, MouseWheelEventArgs e) { Point m = e.GetPosition(viewport); Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12, m); e.Handled = true; };
            viewport.SizeChanged += delegate { if (!fitted && viewport.ActualWidth > 100 && mode == 0) { fitted = true; Fit(); } };
            listPanel = new StackPanel { Margin = new Thickness(2, 0, 10, 10) }; listView = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listPanel, Visibility = Visibility.Collapsed };
            body.Children.Add(viewport); body.Children.Add(listView);
            // nagyítás gombok
            StackPanel zc = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 14) };
            Border zo = Pill("−", false), zi = Pill("+", false), fit = Pill("⤢", false); zoomTxt = new TextBlock { Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Width = 38, TextAlignment = TextAlignment.Center };
            zo.MouseLeftButtonUp += delegate { Zoom(1 / 1.2, Center()); }; zi.MouseLeftButtonUp += delegate { Zoom(1.2, Center()); }; fit.MouseLeftButtonUp += delegate { Fit(); };
            zc.Children.Add(zo); zc.Children.Add(zoomTxt); zc.Children.Add(zi); zc.Children.Add(fit); body.Children.Add(zc);
            detail = new Border { Width = 360, HorizontalAlignment = HorizontalAlignment.Right, Background = Br(30, 30, 33, 250), BorderBrush = CardBd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 8, 8, 8), Padding = new Thickness(16, 14, 16, 14), Visibility = Visibility.Collapsed }; body.Children.Add(detail);
            saveT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; saveT.Tick += delegate { saveT.Stop(); Save(); };
            SetMode(0); Unloaded += delegate { Save(); };
        }
        // Nyelvváltás: a beállítás azonnal mentődik (a dokk ~1 mp-en belül átveszi); a GlassControl ablakai újraindítás után váltanak.
        void ChangeLang(string code)
        {
            if (code == Loc.Lang) return;
            Loc.SetLang(code);
            if (MessageBox.Show(Loc.T("A nyelv megváltozott. Újraindítod most a Lumen-t, hogy mindenhol érvényesüljön?"), Loc.T("Nyelv"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                Save(); string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & start \"\" \"" + exe + "\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
            Environment.Exit(0);
        }
        Point Center() { return new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2); }
        static Brush Dots()
        {
            DrawingGroup g = new DrawingGroup(); g.Children.Add(new GeometryDrawing(Br(20, 20, 21, 255), null, new RectangleGeometry(new Rect(0, 0, 22, 22)))); g.Children.Add(new GeometryDrawing(Br(255, 255, 255, 22), null, new EllipseGeometry(new Point(2, 2), 1, 1)));
            DrawingBrush b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 22, 22), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 22, 22), ViewboxUnits = BrushMappingMode.Absolute }; b.Freeze(); return b;
        }
        static Border Pill(string t, bool accent)
        {
            Border b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Background = accent ? Blue : Brushes.Transparent, Child = new TextBlock { Text = t, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = accent ? Brushes.White : Muted } };
            b.MouseEnter += delegate { if (!accent) b.Background = Br(255, 255, 255, 18); }; b.MouseLeave += delegate { if (!accent) b.Background = b.Tag as Brush ?? Brushes.Transparent; }; return b;
        }
        void SetMode(int m)
        {
            mode = m; for (int i = 0; i < tabBtns.Count; i++) { Brush bg = i == m ? Br(255, 255, 255, 36) : Brushes.Transparent; tabBtns[i].Tag = bg; tabBtns[i].Background = bg; ((TextBlock)tabBtns[i].Child).Foreground = i == m ? Brushes.White : Muted; }
            viewport.Visibility = m == 0 ? Visibility.Visible : Visibility.Collapsed; listView.Visibility = m == 0 ? Visibility.Collapsed : Visibility.Visible; Refresh();
        }
        void Refresh() { if (mode == 0) Relayout(); else BuildList(); }

        // ---------------------------------------------------------------- fa
        string Q { get { return search != null ? search.Text.Trim().ToLowerInvariant() : ""; } }
        bool Hit(HelpItem it) { string q = Q; return q.Length == 0 || (it.Title + " " + it.Desc).ToLowerInvariant().Contains(q); }
        bool AnyHit(HelpItem it) { if (Hit(it)) return true; foreach (HelpItem k in it.Kids) if (AnyHit(k)) return true; return false; }
        void Relayout()
        {
            world.Children.Clear(); foreach (HelpNote n0 in notes) n0.Curve = null; double x = 20; string q = Q;
            foreach (HelpItem r in roots)
            {
                double y = 20; List<HelpItem> vis = new List<HelpItem>(); Collect(r, vis, q.Length > 0);
                foreach (HelpItem it in vis) { double w = it.Depth == 0 ? 252 : 236 - it.Depth * 6; double h = it.Depth == 0 ? 46 : 40; it.Box = new Rect(x + it.Depth * 24, y, w, h); y += h + 8; }
                foreach (HelpItem it in vis) { it.Card = MakeCard(it, q.Length > 0 && !Hit(it)); Canvas.SetLeft(it.Card, it.Box.X); Canvas.SetTop(it.Card, it.Box.Y); world.Children.Add(it.Card); }
                foreach (HelpItem it in vis) Connectors(it, vis);
                x += 300;
            }
            foreach (HelpNote n in notes) PlaceNote(n); UpdateLinks(); zoomTxt.Text = (int)Math.Round(st.ScaleX * 100) + "%";
        }
        void Collect(HelpItem it, List<HelpItem> vis, bool filter) { if (filter && !AnyHit(it)) return; vis.Add(it); if (!it.Collapsed || filter) foreach (HelpItem k in it.Kids) Collect(k, vis, filter); }
        void Connectors(HelpItem p, List<HelpItem> vis)
        {
            List<HelpItem> kids = vis.FindAll(delegate (HelpItem k) { return k.Parent == p; }); if (kids.Count == 0) return; double vx = p.Box.X + 14; PathGeometry g = new PathGeometry();
            double lastMid = kids[kids.Count - 1].Box.Y + kids[kids.Count - 1].Box.Height / 2; PathFigure f0 = new PathFigure { StartPoint = new Point(vx, p.Box.Bottom) }; f0.Segments.Add(new LineSegment(new Point(vx, lastMid - 8), true)); g.Figures.Add(f0);
            foreach (HelpItem k in kids) { double my = k.Box.Y + k.Box.Height / 2; PathFigure f = new PathFigure { StartPoint = new Point(vx, my - 8) }; f.Segments.Add(new ArcSegment(new Point(vx + 8, my), new Size(8, 8), 0, false, SweepDirection.Counterclockwise, true)); f.Segments.Add(new LineSegment(new Point(k.Box.X, my), true)); g.Figures.Add(f); }
            Shapes.Path path = new Shapes.Path { Data = g, Stroke = Br(255, 255, 255, 40), StrokeThickness = 1.2, IsHitTestVisible = false }; Canvas.SetZIndex(path, -1); world.Children.Add(path);
        }
        Border MakeCard(HelpItem it, bool dim)
        {
            Brush stripe = it.Depth == 0 ? Orange : it.Kids.Count > 0 ? Br(200, 150, 60, 255) : StatusBr(it.Status); bool hit = Q.Length > 0 && Hit(it);
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border bar = new Border { Background = stripe, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 6, 0, 6) }; g.Children.Add(bar);
            if (it.Kids.Count > 0) { TextBlock ch = new TextBlock { Text = it.Collapsed ? "›" : "⌄", Foreground = Muted, FontSize = 14, Margin = new Thickness(6, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Width = 14 }; Grid.SetColumn(ch, 1); ch.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { it.Collapsed = !it.Collapsed; Relayout(); QueueSave(); e.Handled = true; }; ch.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; }; g.Children.Add(ch); }
            TextBlock ic = new TextBlock { Text = it.Icon, FontSize = 14, Margin = new Thickness(6, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White }; Grid.SetColumn(ic, 2); g.Children.Add(ic);
            StackPanel tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tx, 3); tx.Children.Add(new TextBlock { Text = it.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            tx.Children.Add(new TextBlock { Text = it.Depth == 0 ? it.Kids.Count + Loc.T(" elem") : (it.Status.Length > 0 ? it.Status : Loc.T("csoport")) + (it.Kids.Count > 0 ? " · " + it.Kids.Count + Loc.T(" elem") : ""), FontSize = 10, Foreground = it.Status == Loc.T("Kész") || it.Status == Loc.T("Folyamatban") || it.Status == Loc.T("Terv") ? StatusBr(it.Status) : Muted }); g.Children.Add(tx);
            Border open = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Margin = new Thickness(4, 0, 6, 0), Background = Br(255, 255, 255, 14), Child = new TextBlock { Text = "⧉", Foreground = Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Cursor = Cursors.Hand, ToolTip = Loc.T("Megnyitás") }; Grid.SetColumn(open, 4); g.Children.Add(open);
            Border c = new Border { Width = it.Box.Width, Height = it.Box.Height, CornerRadius = new CornerRadius(9), Background = it == sel ? Br(46, 56, 74, 255) : Card, BorderBrush = it == sel || hit ? Blue : CardBd, BorderThickness = new Thickness(1), Child = g, Opacity = dim ? 0.25 : 1, Cursor = Cursors.Hand };
            c.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; }; c.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { Open(it); e.Handled = true; }; return c;
        }

        // ---------------------------------------------------------------- nagyítás
        void Zoom(double f, Point m) { double os = st.ScaleX, ns = Math.Max(0.25, Math.Min(2.5, os * f)); tt.X = m.X - (m.X - tt.X) * (ns / os); tt.Y = m.Y - (m.Y - tt.Y) * (ns / os); st.ScaleX = st.ScaleY = ns; zoomTxt.Text = (int)Math.Round(ns * 100) + "%"; }
        void Fit()
        {
            if (roots.Count == 0 || viewport.ActualWidth < 50) return; double w = roots.Count * 300 + 20, h = 0;
            foreach (HelpItem r in roots) { double hh = 40; List<HelpItem> v = new List<HelpItem>(); Collect(r, v, false); hh += v.Count * 48; h = Math.Max(h, hh); }
            double s = Math.Min(1.0, Math.Min(viewport.ActualWidth / w, viewport.ActualHeight / Math.Max(h, 1))); s = Math.Max(0.5, s); st.ScaleX = st.ScaleY = s; tt.X = 12; tt.Y = 8; zoomTxt.Text = (int)Math.Round(s * 100) + "%";
        }

        // ---------------------------------------------------------------- jegyzetek
        void AddNote()
        {
            if (mode != 0) SetMode(0); HelpNote n = new HelpNote { Id = "n" + DateTime.Now.Ticks.ToString("x") + (idSeq++) }; Point c = new Point((Center().X - tt.X) / st.ScaleX - 80, (Center().Y - tt.Y) / st.ScaleY - 50); n.X = c.X; n.Y = c.Y; notes.Add(n); PlaceNote(n); QueueSave(); n.Tb.Focus();
        }
        void PlaceNote(HelpNote n)
        {
            Border top = new Border { Height = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeAll }; Grid hg = new Grid(); TextBlock x = new TextBlock { Text = "×", FontSize = 11, Foreground = Br(120, 90, 20, 255), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 22, 0), Cursor = Cursors.Hand, ToolTip = Loc.T("Törlés") };
            Border dot = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Background = Br(240, 140, 30, 255), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Cross, ToolTip = Loc.T("Húzd egy kártyára az összekötéshez") };
            hg.Children.Add(x); hg.Children.Add(dot); top.Child = hg;
            n.Tb = new TextBox { Text = n.Text, Background = Brushes.Transparent, BorderThickness = new Thickness(0), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Br(60, 45, 10, 255), CaretBrush = Brushes.Black, Padding = new Thickness(8, 0, 8, 8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; GlassMenu.ForTextBox(n.Tb);
            n.Tb.TextChanged += delegate { n.Text = n.Tb.Text; QueueSave(); };
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(n.Tb, 1); g.Children.Add(top); g.Children.Add(n.Tb);
            Border grip = new Border { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent, Child = new TextBlock { Text = "◢", FontSize = 9, Foreground = Br(150, 110, 30, 255), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom } }; Grid.SetRowSpan(grip, 2); g.Children.Add(grip);
            LinearGradientBrush lg = new LinearGradientBrush(Color.FromRgb(248, 232, 130), Color.FromRgb(238, 211, 106), 90);
            n.El = new Border { Width = n.W, Height = n.H, Background = lg, CornerRadius = new CornerRadius(6), Child = g, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4, Direction = 270 } }; Canvas.SetLeft(n.El, n.X); Canvas.SetTop(n.El, n.Y); Canvas.SetZIndex(n.El, 5); world.Children.Add(n.El);
            x.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { notes.Remove(n); world.Children.Remove(n.El); if (n.Curve != null) world.Children.Remove(n.Curve); QueueSave(); e.Handled = true; }; x.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            Point off = new Point(); bool drag = false;
            top.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { drag = true; Point p = e.GetPosition(world); off = new Point(p.X - n.X, p.Y - n.Y); top.CaptureMouse(); e.Handled = true; };
            top.MouseMove += delegate (object o, MouseEventArgs e) { if (!drag) return; Point p = e.GetPosition(world); n.X = p.X - off.X; n.Y = p.Y - off.Y; Canvas.SetLeft(n.El, n.X); Canvas.SetTop(n.El, n.Y); UpdateLinks(); };
            top.MouseLeftButtonUp += delegate { if (drag) { drag = false; top.ReleaseMouseCapture(); QueueSave(); } };
            bool rs = false; Point rp = new Point(); grip.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { rs = true; rp = e.GetPosition(world); grip.CaptureMouse(); e.Handled = true; };
            grip.MouseMove += delegate (object o, MouseEventArgs e) { if (!rs) return; Point p = e.GetPosition(world); n.W = Math.Max(110, n.W + p.X - rp.X); n.H = Math.Max(70, n.H + p.Y - rp.Y); rp = p; n.El.Width = n.W; n.El.Height = n.H; UpdateLinks(); };
            grip.MouseLeftButtonUp += delegate { if (rs) { rs = false; grip.ReleaseMouseCapture(); QueueSave(); } };
            dot.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { dot.CaptureMouse(); e.Handled = true; };
            dot.MouseMove += delegate (object o, MouseEventArgs e) { if (!dot.IsMouseCaptured) return; Point p = e.GetPosition(world); DrawCurve(n, new Point(n.X + n.W - 10, n.Y + 9), p, true); };
            dot.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { if (!dot.IsMouseCaptured) return; dot.ReleaseMouseCapture(); Point p = e.GetPosition(world); HelpItem hit = null; foreach (KeyValuePair<string, HelpItem> kv in byId) if (kv.Value.Card != null && kv.Value.Box.Contains(p)) hit = kv.Value; n.Link = hit != null ? hit.Id : ""; UpdateLinks(); QueueSave(); };
        }
        void DrawCurve(HelpNote n, Point a, Point b, bool temp)
        {
            if (n.Curve == null) { n.Curve = new Shapes.Path { Stroke = Br(245, 200, 60, 255), StrokeThickness = 1.6, IsHitTestVisible = false }; Canvas.SetZIndex(n.Curve, 4); world.Children.Add(n.Curve); }
            double dx = Math.Max(40, Math.Abs(b.X - a.X) / 2); PathFigure f = new PathFigure { StartPoint = a }; f.Segments.Add(new BezierSegment(new Point(a.X + dx, a.Y), new Point(b.X - dx * Math.Sign(b.X - a.X == 0 ? 1 : b.X - a.X), b.Y), b, true)); n.Curve.Data = new PathGeometry(new PathFigure[] { f });
        }
        void UpdateLinks()
        {
            foreach (HelpNote n in notes)
            {
                HelpItem it; if (n.Link.Length == 0 || !byId.TryGetValue(n.Link, out it) || it.Card == null || !world.Children.Contains(it.Card)) { if (n.Curve != null) { world.Children.Remove(n.Curve); n.Curve = null; } continue; }
                Point a = new Point(n.X + n.W - 10, n.Y + 9); bool right = a.X > it.Box.X + it.Box.Width / 2; Point b = new Point(right ? it.Box.Right : it.Box.X, it.Box.Y + it.Box.Height / 2); DrawCurve(n, a, b, false);
            }
        }

        // ---------------------------------------------------------------- részletek
        void Open(HelpItem it)
        {
            sel = it; if (mode == 0) Relayout(); detail.Visibility = Visibility.Visible; StackPanel sp = new StackPanel();
            Grid hd = new Grid(); hd.ColumnDefinitions.Add(new ColumnDefinition()); hd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hd.Children.Add(new TextBlock { Text = it.Icon + "  " + it.Title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            TextBlock cl = new TextBlock { Text = "✕", Foreground = Muted, FontSize = 14, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top }; Grid.SetColumn(cl, 1); cl.MouseLeftButtonUp += delegate { detail.Visibility = Visibility.Collapsed; sel = null; if (mode == 0) Relayout(); }; hd.Children.Add(cl); sp.Children.Add(hd);
            sp.Children.Add(new TextBlock { Text = it.Cat + (it.Parent != null && it.Parent.Parent != null ? "  ›  " + it.Parent.Title : ""), Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) });
            if (it.Status.Length > 0) { Border sc = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, Background = Br(255, 255, 255, 22), Child = new TextBlock { Text = it.Status, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = StatusBr(it.Status) }, Margin = new Thickness(0, 0, 0, 10) }; if (it.Custom) { sc.Cursor = Cursors.Hand; sc.ToolTip = Loc.T("Kattints: állapot váltása"); sc.MouseLeftButtonUp += delegate { it.Status = it.Status == Loc.T("Terv") ? Loc.T("Folyamatban") : it.Status == Loc.T("Folyamatban") ? Loc.T("Kész") : Loc.T("Terv"); QueueSave(); Open(it); }; } sp.Children.Add(sc); }
            if (it.Custom)
            {
                TextBox tt2 = new TextBox { Text = it.Title, Background = Br(255, 255, 255, 12), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = CardBd, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8), FontSize = 13 }; GlassMenu.ForTextBox(tt2); tt2.TextChanged += delegate { it.Title = tt2.Text; QueueSave(); }; sp.Children.Add(tt2);
                TextBox dd = new TextBox { Text = it.Desc, Background = Br(255, 255, 255, 12), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = CardBd, Padding = new Thickness(6, 4, 6, 4), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontSize = 12.5 }; GlassMenu.ForTextBox(dd); dd.TextChanged += delegate { it.Desc = dd.Text; QueueSave(); }; sp.Children.Add(dd);
                Border del = Pill(Loc.T("🗑 Törlés"), false); del.HorizontalAlignment = HorizontalAlignment.Left; del.Margin = new Thickness(0, 8, 0, 0); del.MouseLeftButtonUp += delegate { it.Parent.Kids.Remove(it); byId.Remove(it.Id); detail.Visibility = Visibility.Collapsed; sel = null; Refresh(); QueueSave(); }; sp.Children.Add(del);
            }
            else sp.Children.Add(new TextBlock { Text = it.Desc, Foreground = Br(222, 222, 228, 255), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
            if (!string.IsNullOrEmpty(it.Action) && Navigate != null) { Border go = Pill(Loc.T("▶ Megnyitás a Lumenben"), true); go.HorizontalAlignment = HorizontalAlignment.Left; go.Margin = new Thickness(0, 12, 0, 0); string act = it.Action; go.MouseLeftButtonUp += delegate { Navigate(act); }; sp.Children.Add(go); }
            sp.Children.Add(new TextBlock { Text = Loc.T("JEGYZETEIM"), Foreground = Muted, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 5) });
            string pv; personal.TryGetValue(it.Id, out pv); TextBox nb = new TextBox { Text = pv ?? "", Background = Br(255, 255, 255, 12), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = CardBd, Padding = new Thickness(7, 5, 7, 5), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; GlassMenu.ForTextBox(nb);
            nb.TextChanged += delegate { if (nb.Text.Length == 0) personal.Remove(it.Id); else personal[it.Id] = nb.Text; QueueSave(); }; sp.Children.Add(nb);
            detail.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
        }

        // ---------------------------------------------------------------- lista / ötletek / jegyzetek
        void BuildList()
        {
            listPanel.Children.Clear(); string q = Q;
            if (mode == 3)
            {
                foreach (HelpNote n in notes) { HelpNote nn = n; if (q.Length > 0 && !n.Text.ToLowerInvariant().Contains(q)) continue; listPanel.Children.Add(Row("🟨", n.Text.Length > 0 ? n.Text.Split('\n')[0] : Loc.T("(üres jegyzet)"), Loc.T("sárga jegyzet") + (n.Link.Length > 0 && byId.ContainsKey(n.Link) ? " → " + byId[n.Link].Title : ""), "", delegate { SetMode(0); tt.X = 40 - nn.X * st.ScaleX; tt.Y = 40 - nn.Y * st.ScaleY; })); }
                foreach (KeyValuePair<string, string> kv in personal) { HelpItem it; if (!byId.TryGetValue(kv.Key, out it)) continue; if (q.Length > 0 && !(kv.Value + it.Title).ToLowerInvariant().Contains(q)) continue; HelpItem i2 = it; listPanel.Children.Add(Row("📝", it.Title, kv.Value.Replace("\n", " "), "", delegate { Open(i2); })); }
                if (listPanel.Children.Count == 0) listPanel.Children.Add(new TextBlock { Text = Loc.T("Még nincs jegyzeted. A fában a „+ Új” menüvel sárga jegyzetet tehetsz le, egy kártya megnyitásakor pedig a „Jegyzeteim” mezőbe írhatsz."), Foreground = Muted, Margin = new Thickness(8, 12, 8, 0), TextWrapping = TextWrapping.Wrap });
                return;
            }
            WrapPanel fl = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; foreach (string s in new string[] { "", Loc.T("Kész"), Loc.T("Folyamatban"), Loc.T("Terv") }) { string ss = s; Border p = Pill(s.Length == 0 ? Loc.T("Mind") : s, false); p.Tag = ss == statusFilter ? Br(255, 255, 255, 36) : Brushes.Transparent; p.Background = (Brush)p.Tag; p.MouseLeftButtonUp += delegate { statusFilter = ss; BuildList(); }; fl.Children.Add(p); } listPanel.Children.Add(fl);
            foreach (HelpItem r in roots)
            {
                if (mode == 2 && !r.Title.Contains(Loc.T("Ötlet")) && !r.Title.Contains(Loc.T("Saját"))) continue; List<HelpItem> all = new List<HelpItem>(); Flat(r, all); bool head = false;
                foreach (HelpItem it in all) { if (it.Kids.Count > 0 || it.Depth == 0) continue; if (statusFilter.Length > 0 && it.Status != statusFilter) continue; if (!Hit(it)) continue; if (!head) { head = true; listPanel.Children.Add(new TextBlock { Text = r.Icon + "  " + r.Title.ToUpperInvariant(), Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 5) }); } HelpItem i2 = it; listPanel.Children.Add(Row(it.Icon, it.Title, it.Desc, it.Status, delegate { Open(i2); })); }
            }
        }
        void Flat(HelpItem it, List<HelpItem> l) { l.Add(it); foreach (HelpItem k in it.Kids) Flat(k, l); }
        Border Row(string icon, string title, string sub, string status, Action click)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = icon, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White }); StackPanel sp = new StackPanel(); Grid.SetColumn(sp, 1);
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }); sp.Children.Add(new TextBlock { Text = sub, Foreground = Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }); g.Children.Add(sp);
            if (status.Length > 0) { TextBlock st2 = new TextBlock { Text = status, Foreground = StatusBr(status), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }; Grid.SetColumn(st2, 2); g.Children.Add(st2); }
            Border b = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Child = g, Cursor = Cursors.Hand }; b.MouseEnter += delegate { b.Background = Br(255, 255, 255, 14); }; b.MouseLeave += delegate { b.Background = Brushes.Transparent; }; b.MouseLeftButtonUp += delegate { click(); }; return b;
        }

        // ---------------------------------------------------------------- saját ötlet
        void AddCustom()
        {
            HelpItem mine = roots.Find(delegate (HelpItem r) { return r.Id == "mine"; }); if (mine == null) return; HelpItem it = new HelpItem { Id = "c" + DateTime.Now.Ticks.ToString("x") + (idSeq++), Title = Loc.T("Új ötlet"), Status = Loc.T("Terv"), Icon = "💭", Parent = mine, Depth = 1, Custom = true }; mine.Kids.Add(it); byId[it.Id] = it; mine.Collapsed = false;
            QueueSave(); Refresh(); Open(it);
        }

        // ---------------------------------------------------------------- mentés / betöltés
        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace("|", "\\p"); }
        static string Unesc(string s) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < s.Length; i++) { if (s[i] == '\\' && i + 1 < s.Length) { char c = s[++i]; sb.Append(c == 'n' ? '\n' : c == 'p' ? '|' : c); } else sb.Append(s[i]); } return sb.ToString(); }
        void QueueSave() { if (saveT != null) { saveT.Stop(); saveT.Start(); } }
        void Save()
        {
            try
            {
                List<string> l = new List<string>(); foreach (HelpNote n in notes) l.Add("N|" + n.Id + "|" + n.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "|" + n.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "|" + n.W.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "|" + n.H.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "|" + n.Link + "|" + Esc(n.Text));
                foreach (KeyValuePair<string, string> kv in personal) l.Add("P|" + kv.Key + "|" + Esc(kv.Value));
                foreach (KeyValuePair<string, HelpItem> kv in byId) { if (kv.Value.Custom) l.Add("C|" + kv.Key + "|" + Esc(kv.Value.Title) + "|" + kv.Value.Status + "|" + Esc(kv.Value.Desc)); if (kv.Value.Collapsed) l.Add("S|" + kv.Key); }
                AnalyticsStore.WriteAtomic(file, string.Join(Environment.NewLine, l.ToArray()) + Environment.NewLine, false);
            }
            catch { }
        }
        void Load()
        {
            try
            {
                if (!File.Exists(file)) return; HelpItem mine = roots.Find(delegate (HelpItem r) { return r.Id == "mine"; });
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] p = line.Split('|'); if (p.Length < 2) continue;
                    if (p[0] == "N" && p.Length >= 8) { HelpNote n = new HelpNote { Id = p[1] }; double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n.X); double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n.Y); double.TryParse(p[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n.W); double.TryParse(p[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n.H); n.Link = p[6]; n.Text = Unesc(p[7]); notes.Add(n); }
                    else if (p[0] == "P" && p.Length >= 3) personal[p[1]] = Unesc(p[2]);
                    else if (p[0] == "C" && p.Length >= 5 && mine != null) { HelpItem it = new HelpItem { Id = p[1], Title = Unesc(p[2]), Status = p[3], Desc = Unesc(p[4]), Icon = "💭", Parent = mine, Depth = 1, Custom = true }; mine.Kids.Add(it); byId[it.Id] = it; }
                    else if (p[0] == "S") { HelpItem it; if (byId.TryGetValue(p[1], out it)) it.Collapsed = true; }
                }
            }
            catch { }
        }
    }
}
