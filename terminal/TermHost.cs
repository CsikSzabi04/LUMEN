// Lumen Terminal – gazda: lapok, osztott panelek, parancspaletta, keresősáv, előzmény-kereső, snippetek, vágólap-előzmények,
// profilok, értesítések, munkamenet-visszaállítás. A Terminal.exe ablaka és a GlassControl Terminál füle is ezt használja.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassTerm
{
    public class TermAction { public string Id, Title, Cat, Key; public Action Run; public Func<bool> Enabled, Checked; }

    // az ablak-szintű műveletek: a Terminal.exe ablaka tölti ki; a beágyazott (GlassControl) módban a hiányzók le vannak tiltva
    public class HostWin
    {
        public Action NewWindow, ToggleMax, Minimize, CloseWindow, ToggleTopmost, Quake, FlashWindow; public Func<bool> IsTopmost, IsMaxed, WindowActive; public Action<double> SetOpacity; public Action<string, string> Balloon; public Action<bool> SetTabsBar;
    }

    public class PaneNode { public TermView Leaf; public PaneNode A, B, Parent; public bool Vertical; public double Ratio = 0.5; }
    public class TermTab
    {
        public ShellProfile Prof; public string Custom; public PaneNode Root; public TermView Focus; public Border Chip, Host; public Grid Outer; public UIElement TreeEl; public WebPreview Preview; public GridSplitter PrevSplit; public TextBlock Label, Dot; public bool Activity, Bell, Zoomed, Broadcast; public Color? Tag; public string LastLabel = "";
        public List<TermView> Leaves() { List<TermView> l = new List<TermView>(); Walk(Root, l); return l; }
        static void Walk(PaneNode n, List<TermView> l) { if (n == null) return; if (n.Leaf != null) l.Add(n.Leaf); else { Walk(n.A, l); Walk(n.B, l); } }
    }

    public class TermHost : Grid
    {
        public static bool NoSession; public HostWin Win = new HostWin(); public TermSettings St = TermSettings.I; public string FontDir; public bool Embedded;
        List<TermTab> tabs = new List<TermTab>(); TermTab cur; Grid contentGrid = new Grid(); Border tabBar; StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal }; Grid overlay = new Grid();
        List<TermAction> actions = new List<TermAction>(); Dictionary<string, TermAction> keyMap = new Dictionary<string, TermAction>(); Stack<string[]> closedTabs = new Stack<string[]>(); List<string> clipHist = new List<string>(); string lastToast = "";
        Border findBar; TextBox findBox; TextBlock findCount; bool findCase, findRegex; Border toast; DispatcherTimer toastT, labelT; Border modalPanel, scrim; Border dragChip;
        public event Action AllTabsClosed, TitleChanged, GridChanged;
        static readonly FontFamily UiFont = new FontFamily("Inter, Segoe UI"); static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(160, 160, 168));

        public TermHost(string fontDir, bool embedded)
        {
            FontDir = fontDir; Embedded = embedded; St.Load(); WebPreview.Dir = Path.Combine(Path.GetDirectoryName(fontDir.TrimEnd((char)92)) ?? "", "webview2"); if (!Directory.Exists(WebPreview.Dir)) WebPreview.Dir = Path.Combine(fontDir, "..", "webview2"); SnapsToDevicePixels = true;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            tabBar = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 20, 20, 22)), Padding = new Thickness(8, 4, 8, 4), Child = chips, Visibility = Visibility.Collapsed, BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetRow(tabBar, 0); Children.Add(tabBar); Grid.SetRow(contentGrid, 1); Children.Add(contentGrid);
            Grid.SetRow(overlay, 0); Grid.SetRowSpan(overlay, 2); Children.Add(overlay);
            St.Changed += ApplyAll; BuildActions(); LoadKeys();
            labelT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) }; labelT.Tick += delegate { UpdateLabels(); }; labelT.Start();
            AllowDrop = true;
        }

        public TermView CurrentView { get { return cur != null ? cur.Focus : null; } }
        public int TabCount { get { return tabs.Count; } }
        public string DebugChips() { StringBuilder sb = new StringBuilder(); foreach (UIElement c in chips.Children) { Border b = c as Border; sb.Append("[" + (b != null ? b.ActualWidth.ToString("0") : "?") + ":" + (b != null && b.Child is Grid ? ((Grid)b.Child).Children.Count + "ch" : "x") + "]"); } return sb.ToString() + " tabs=" + tabs.Count; }
        public List<TermView> AllViews() { List<TermView> l = new List<TermView>(); foreach (TermTab t in tabs) l.AddRange(t.Leaves()); return l; }

        // ================================================================== lapok
        public void OpenFirst(bool restore)
        {
            if (restore && !NoSession && St.RestoreSession && LoadSession()) return;
            NewTab(ShellProfiles.Default(), Cfg.Get("startdir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }

        TermView MakeView(ShellProfile prof, string cwd)
        {
            TermView v = new TermView(FontDir); v.Cols = 100; v.Rows = 28; v.Scr.Resize(100, 28); v.Scr.AllowOsc52 = Cfg.GetBool("osc52", false);
            v.Exited += delegate { OnExited(v); }; v.TitleChanged += delegate { UpdateLabels(); };
            v.GridChanged += delegate { if (CurrentView == v && GridChanged != null) GridChanged(); };
            v.BellRung += delegate { TermTab t = TabOf(v); if (t != null && t != cur) { t.Bell = true; RefreshChips(); } };
            v.OutputWhileInactive += delegate { TermTab t = TabOf(v); if (t != null && t != cur && !t.Activity) { t.Activity = true; RefreshChips(); } };
            v.CommandDone += delegate (PromptMark m) { OnCommandDone(v, m); };
            v.LinkActivated += OpenLink; v.DevUrlFound += delegate (string u) { OnDevUrl(v, u); }; v.PreviewRequested += delegate (string u) { if (string.IsNullOrEmpty(u) || LoopbackHttp(u)) OpenPreviewFor(v, u); };      // a shellből csak helyi (localhost) cím kérhető előnézetre
            v.ContextRequested += delegate (Point p) { ShowContext(v, p); };
            v.Copied += delegate (string t) { AddClip(t); Toast(Loc.T("Másolva · ") + Math.Min(t.Length, 9999) + Loc.T(" karakter")); }; v.Pasted += delegate (string t) { AddClip(t); };
            v.Notified += delegate (string m) { Toast(m); if (Win.WindowActive != null && !Win.WindowActive() && Win.Balloon != null) Win.Balloon("Terminal", m); };
            v.UserInput += delegate (string s) { TermTab t = TabOf(v); if (t != null && t.Broadcast) foreach (TermView o in t.Leaves()) if (o != v) o.Send(s); };
            v.AskConfirm = ShowConfirm;
            v.Focused += delegate { TermTab t = TabOf(v); if (t != null) { t.Focus = v; if (t == cur) { RefreshDim(t); if (TitleChanged != null) TitleChanged(); } } };
            v.StartShell(prof, cwd); return v;
        }

        TermTab TabOf(TermView v) { foreach (TermTab t in tabs) foreach (TermView x in t.Leaves()) if (x == v) return t; return null; }

        public TermTab NewTab(ShellProfile prof, string cwd)
        {
            if (prof == null) prof = ShellProfiles.Default(); TermTab t = new TermTab { Prof = prof.Clone() }; TermView v = MakeView(prof, cwd);
            t.Root = new PaneNode { Leaf = v }; t.Focus = v; t.Host = new Border { Visibility = Visibility.Collapsed }; t.Outer = new Grid(); t.Outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 }); t.Outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) }); t.Outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) }); t.Host.Child = t.Outer; contentGrid.Children.Add(t.Host);
            tabs.Add(t); BuildChip(t); RebuildTab(t); SelectTab(t); RefreshChips(); return t;
        }

        void BuildChip(TermTab t)
        {
            Border b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 6, 4), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, MinWidth = 90, MaxWidth = 220, Background = Brushes.Transparent };
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock icon = new TextBlock { Text = t.Prof.Icon, FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.Bold };
            t.Label = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t.Label, 1);
            t.Dot = new TextBlock { Text = "●", FontSize = 9, Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed }; Grid.SetColumn(t.Dot, 2);
            TextBlock x = new TextBlock { Text = "×", FontSize = 15, Foreground = Muted, Margin = new Thickness(4, -2, 2, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Arrow, FontWeight = FontWeights.Bold }; Grid.SetColumn(x, 3);
            x.MouseEnter += delegate { x.Foreground = Brushes.White; }; x.MouseLeave += delegate { x.Foreground = Muted; }; x.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { CloseTab(t); e.Handled = true; }; x.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            g.Children.Add(icon); g.Children.Add(t.Label); g.Children.Add(t.Dot); g.Children.Add(x); b.Child = g; t.Chip = b;
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { SelectTab(t); dragChip = b; b.CaptureMouse(); };
            b.MouseMove += delegate (object o, MouseEventArgs e) { if (dragChip == b && b.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) DragChip(t, e.GetPosition(chips)); };
            b.MouseLeftButtonUp += delegate { if (dragChip == b) { dragChip = null; b.ReleaseMouseCapture(); } };
            b.MouseDown += delegate (object o, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle) { CloseTab(t); e.Handled = true; } };
            b.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { TabMenu(t); e.Handled = true; };
            b.MouseEnter += delegate { if (t != cur) b.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)); }; b.MouseLeave += delegate { if (t != cur) b.Background = Brushes.Transparent; };
        }

        void DragChip(TermTab t, Point p)
        {
            int idx = tabs.IndexOf(t); double x = 0; int target = tabs.Count - 1;
            for (int i = 0; i < tabs.Count; i++) { double w = tabs[i].Chip.ActualWidth + 4; if (p.X < x + w / 2 + (i > idx ? 0 : 0)) { target = i; break; } x += w; }
            if (target != idx && target >= 0 && target < tabs.Count) { tabs.RemoveAt(idx); tabs.Insert(target, t); RefreshChips(); }
        }

        void RefreshChips()
        {
            chips.Children.Clear();
            foreach (TermTab t in tabs)
            {
                chips.Children.Add(t.Chip); bool sel = t == cur; t.Chip.Background = sel ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : Brushes.Transparent;
                t.Chip.BorderThickness = new Thickness(0, 2, 0, 0); t.Chip.BorderBrush = t.Tag.HasValue ? (Brush)new SolidColorBrush(t.Tag.Value) : Brushes.Transparent;
                t.Dot.Visibility = (t.Activity || t.Bell) && !sel ? Visibility.Visible : Visibility.Collapsed; t.Dot.Foreground = t.Bell ? new SolidColorBrush(Color.FromRgb(255, 159, 10)) : new SolidColorBrush(Color.FromRgb(10, 132, 255));
            }
            Border plus = new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = new TextBlock { Text = "+", FontSize = 17, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) }, ToolTip = Loc.T("Új lap (Ctrl+Shift+T)") };
            plus.MouseEnter += delegate { plus.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); }; plus.MouseLeave += delegate { plus.Background = Brushes.Transparent; }; plus.MouseLeftButtonUp += delegate { NewTab(ShellProfiles.Default(), CurrentView != null ? CurrentView.Cwd : null); };
            Border arrow = new Border { Width = 20, Height = 24, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = new TextBlock { Text = "⌄", FontSize = 13, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) }, ToolTip = Loc.T("Profilok") };
            arrow.MouseEnter += delegate { arrow.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); }; arrow.MouseLeave += delegate { arrow.Background = Brushes.Transparent; }; arrow.MouseLeftButtonUp += delegate { ProfileMenu(arrow); };
            chips.Children.Add(plus); chips.Children.Add(arrow);
            bool show = tabs.Count > 1 || St.ShowTabsAlways; tabBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed; UpdateLabels();
        }

        string LabelFor(TermTab t)
        {
            if (!string.IsNullOrEmpty(t.Custom)) return t.Custom; TermView v = t.Focus; if (v == null) return t.Prof.Name;
            PromptMark m = v.LastMark; if (m != null && m.Started && !m.Done) { string c = m.Cmd; int sp = c.IndexOf(' '); return "▸ " + (sp > 0 ? c.Substring(0, sp) : c); }
            string cwd = v.Cwd; if (!string.IsNullOrEmpty(cwd)) { string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); if (string.Equals(cwd.TrimEnd('\\'), home, StringComparison.OrdinalIgnoreCase)) return "~"; string leaf = Path.GetFileName(cwd.TrimEnd('\\')); if (!string.IsNullOrEmpty(leaf)) return leaf; return cwd; }
            return t.Prof.Name;
        }

        void UpdateLabels()
        {
            foreach (TermTab t in tabs) { try { string l = LabelFor(t); if (l != t.LastLabel) { t.LastLabel = l; t.Label.Text = l; t.Chip.ToolTip = l; } } catch { } }
            if (TitleChanged != null) TitleChanged();
        }

        public string TitleText { get { TermView v = CurrentView; string label = cur != null ? LabelFor(cur) : "terminal"; string sh = cur != null ? cur.Prof.Name : "shell"; return Environment.UserName.ToLower() + " — " + sh + " — " + (v != null ? v.Cols + "×" + v.Rows : "") + (label != sh && label != "~" ? "   · " + label : ""); } }

        public void SelectTab(TermTab t)
        {
            if (t == null) return; cur = t; foreach (TermTab x in tabs) x.Host.Visibility = x == t ? Visibility.Visible : Visibility.Collapsed; t.Activity = false; t.Bell = false; RefreshChips(); RefreshDim(t);
            Dispatcher.BeginInvoke(new Action(delegate { FocusCurrent(); if (GridChanged != null) GridChanged(); if (TitleChanged != null) TitleChanged(); }), DispatcherPriority.Input);
            ApplyBg();
        }
        public void SelectIndex(int i) { if (i >= 0 && i < tabs.Count) SelectTab(tabs[i]); }
        void CycleTab(int d) { if (tabs.Count < 2) return; int i = tabs.IndexOf(cur); SelectTab(tabs[(i + d + tabs.Count) % tabs.Count]); }
        void MoveTab(int d) { int i = tabs.IndexOf(cur), j = i + d; if (i < 0 || j < 0 || j >= tabs.Count) return; TermTab t = tabs[i]; tabs.RemoveAt(i); tabs.Insert(j, t); RefreshChips(); }
        public void FocusCurrent() { TermView v = CurrentView; if (v != null) { v.Focus(); Keyboard.Focus(v); } }

        void CloseTab(TermTab t)
        {
            if (St.ConfirmClose && t.Leaves().Exists(delegate (TermView v) { return v.Alive; })) { ShowConfirm(Loc.T("Lap bezárása"), Loc.T("A lapban még fut egy shell. Bezárod?"), delegate { DoCloseTab(t); }); return; }
            DoCloseTab(t);
        }
        void DoCloseTab(TermTab t)
        {
            int i = tabs.IndexOf(t); if (i < 0) return; TermView f = t.Focus; closedTabs.Push(new string[] { t.Prof.Name, f != null ? f.Cwd : "" });
            if (t.Preview != null) { t.Preview.Shutdown(); t.Preview = null; } foreach (TermView v in t.Leaves()) v.Shutdown(); contentGrid.Children.Remove(t.Host); tabs.RemoveAt(i);
            if (tabs.Count == 0) { cur = null; RefreshChips(); if (AllTabsClosed != null) AllTabsClosed(); return; }
            if (cur == t) SelectTab(tabs[Math.Min(i, tabs.Count - 1)]); else RefreshChips();
        }
        void OnExited(TermView v)
        {
            TermTab t = TabOf(v); if (t == null) return;
            if (Cfg.GetBool("closeonexit", true)) ClosePane(t, v);
            else { try { byte[] rs = new byte[] { 0x1b, (byte)99 }; v.Scr.Feed(rs, 2); } catch { } byte[] m = Encoding.UTF8.GetBytes(Loc.T("\r\n[a shell leállt – Ctrl+Shift+W: bezárás, vagy parancspaletta → Shell újraindítása]\r\n")); v.Scr.Feed(m, m.Length); }
        }

        // ================================================================== panelek
        void Detach(UIElement e)
        {
            DependencyObject p = LogicalTreeHelper.GetParent(e); if (p is Panel) ((Panel)p).Children.Remove(e); else if (p is Decorator) ((Decorator)p).Child = null; else if (p is ContentControl) ((ContentControl)p).Content = null;
        }
        UIElement Build(PaneNode n)
        {
            if (n.Leaf != null) { Detach(n.Leaf); return n.Leaf; }
            Grid g = new Grid(); UIElement a = Build(n.A), b = Build(n.B); GridSplitter sp = new GridSplitter { Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ShowsPreview = false };
            if (n.Vertical)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n.Ratio, GridUnitType.Star), MinWidth = 120 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - n.Ratio, GridUnitType.Star), MinWidth = 120 });
                Grid.SetColumn(a, 0); Grid.SetColumn(sp, 1); Grid.SetColumn(b, 2); sp.ResizeDirection = GridResizeDirection.Columns; sp.Width = 4; sp.Cursor = Cursors.SizeWE;
            }
            else
            {
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(n.Ratio, GridUnitType.Star), MinHeight = 60 }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - n.Ratio, GridUnitType.Star), MinHeight = 60 });
                Grid.SetRow(a, 0); Grid.SetRow(sp, 1); Grid.SetRow(b, 2); sp.ResizeDirection = GridResizeDirection.Rows; sp.Height = 4; sp.Cursor = Cursors.SizeNS;
            }
            g.Children.Add(a); g.Children.Add(sp); g.Children.Add(b); return g;
        }
        void RebuildTab(TermTab t)
        {
            foreach (TermView v in t.Leaves()) Detach(v); if (t.TreeEl != null) t.Outer.Children.Remove(t.TreeEl); UIElement tree = (t.Zoomed && t.Focus != null) ? (UIElement)t.Focus : Build(t.Root);
            if (t.Zoomed && t.Focus != null) Detach(t.Focus); t.TreeEl = tree; Grid.SetRow(tree, 0); t.Outer.Children.Add(tree); RefreshDim(t);
        }
        void RefreshDim(TermTab t) { List<TermView> l = t.Leaves(); foreach (TermView v in l) v.Dim = l.Count > 1 && !t.Zoomed; }

        PaneNode NodeOf(PaneNode n, TermView v) { if (n == null) return null; if (n.Leaf == v) return n; PaneNode r = NodeOf(n.A, v); return r ?? NodeOf(n.B, v); }

        void Split(bool vertical)
        {
            if (cur == null || cur.Focus == null) return; TermView f = cur.Focus; PaneNode node = NodeOf(cur.Root, f); if (node == null) return;
            TermView nv = MakeView(cur.Prof, f.Cwd); PaneNode a = new PaneNode { Leaf = f, Parent = node }, b = new PaneNode { Leaf = nv, Parent = node }; node.Leaf = null; node.A = a; node.B = b; node.Vertical = vertical; node.Ratio = 0.5;
            cur.Zoomed = false; cur.Focus = nv; RebuildTab(cur); Dispatcher.BeginInvoke(new Action(FocusCurrent), DispatcherPriority.Input);
        }
        void ClosePane(TermTab t, TermView v)
        {
            PaneNode node = NodeOf(t.Root, v); if (node == null) return; if (node.Parent == null) { DoCloseTab(t); return; }
            PaneNode p = node.Parent, sib = p.A == node ? p.B : p.A; v.Shutdown();
            p.Leaf = sib.Leaf; p.A = sib.A; p.B = sib.B; p.Vertical = sib.Vertical; p.Ratio = sib.Ratio; if (p.A != null) p.A.Parent = p; if (p.B != null) p.B.Parent = p;
            List<TermView> leaves = t.Leaves(); if (t.Focus == v || !leaves.Contains(t.Focus)) t.Focus = leaves[0]; t.Zoomed = false; RebuildTab(t); if (t == cur) Dispatcher.BeginInvoke(new Action(FocusCurrent), DispatcherPriority.Input);
        }
        void CloseFocusedPane() { if (cur == null || cur.Focus == null) return; TermTab t = cur; if (t.Leaves().Count == 1) CloseTab(t); else ClosePane(t, t.Focus); }
        void CyclePane(int d) { if (cur == null) return; List<TermView> l = cur.Leaves(); if (l.Count < 2) return; int i = l.IndexOf(cur.Focus); cur.Focus = l[(i + d + l.Count) % l.Count]; if (cur.Zoomed) RebuildTab(cur); FocusCurrent(); }
        void ToggleZoom() { if (cur == null || cur.Leaves().Count < 2) return; cur.Zoomed = !cur.Zoomed; RebuildTab(cur); FocusCurrent(); Toast(cur.Zoomed ? Loc.T("Panel kinagyítva (újra: visszaállítás)") : Loc.T("Panelek visszaállítva")); }
        void Equalize() { if (cur == null) return; EqualizeNode(cur.Root); RebuildTab(cur); }
        void EqualizeNode(PaneNode n) { if (n == null || n.Leaf != null) return; n.Ratio = 0.5; EqualizeNode(n.A); EqualizeNode(n.B); }

        // ================================================================== beállítások, megjelenés
        void ApplyAll()
        {
            foreach (TermTab t in tabs) foreach (TermView v in t.Leaves()) v.ApplySettings(); ApplyBg(); RefreshChips(); if (Win.SetOpacity != null) Win.SetOpacity(St.Opacity);
            if (GridChanged != null) GridChanged();
        }
        void ApplyBg() { Color bg = St.CurTheme.Bg; contentGrid.Background = new SolidColorBrush(bg); }
        void Setting(Action change) { change(); St.Save(); St.Apply(); }

        // ================================================================== jelzések, értesítések
        public void Toast(string text)
        {
            lastToast = text; if (toast != null) overlay.Children.Remove(toast);
            toast = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(235, 34, 34, 38)), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1), Padding = new Thickness(16, 8, 16, 9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = (cur != null && cur.Preview != null) ? VerticalAlignment.Top : VerticalAlignment.Bottom, Margin = (cur != null && cur.Preview != null) ? new Thickness(0, 50, 0, 0) : new Thickness(0, 0, 0, 22), IsHitTestVisible = false, Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium }, Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45, Direction = 270 } };
            overlay.Children.Add(toast); Border me = toast; DispatcherTimer tt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2300) }; tt.Tick += delegate { tt.Stop(); overlay.Children.Remove(me); }; tt.Start();
        }

        void OnCommandDone(TermView v, PromptMark m)
        {
            if (!St.Notify || m.Seconds < St.NotifySec) return; bool ok = m.Exit == 0 || m.Exit == int.MinValue; TermTab t = TabOf(v); bool visible = t == cur && (Win.WindowActive == null || Win.WindowActive()) && v.IsKeyboardFocusWithin;
            string msg = (ok ? "✓ " : "✗ " + m.Exit + " · ") + Trim(m.Cmd, 48) + " · " + (m.Seconds < 60 ? m.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" : ((int)(m.Seconds / 60)) + " p " + ((int)(m.Seconds % 60)) + " mp");
            if (visible) return;
            if (t != cur && t != null) { t.Activity = true; RefreshChips(); }
            if (Win.WindowActive != null && !Win.WindowActive()) { if (Win.FlashWindow != null) Win.FlashWindow(); if (Win.Balloon != null) Win.Balloon(ok ? Loc.T("Parancs kész") : Loc.T("Parancs hibával fejeződött be"), msg); }
            else Toast(msg);
        }
        static string Trim(string s, int n) { return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

        void AddClip(string t) { if (string.IsNullOrEmpty(t) || t.Length > 20000) return; clipHist.Remove(t); clipHist.Insert(0, t); if (clipHist.Count > 40) clipHist.RemoveAt(clipHist.Count - 1); }

        // ================================================================== élő előnézet (localhost) a terminál alatt
        Border banner; DispatcherTimer bannerT;
        TermTab PreviewTab(TermView v) { TermTab t = v != null ? TabOf(v) : null; return t ?? cur; }
        public bool PreviewAvailable { get { return WebPreview.WhyNot() == null; } }

        public void OpenPreview(string url) { OpenPreviewFor(CurrentView, url); }
        void OpenPreviewFor(TermView v, string url)
        {
            TermTab t = PreviewTab(v); if (t == null) return; HideBanner();
            if (!string.IsNullOrEmpty(url) && !SafeHttp(url)) { Toast(Loc.T("Előnézethez csak http/https cím adható meg")); return; }
            if (string.IsNullOrEmpty(url)) url = v != null ? v.LastDevUrl : "";
            if (string.IsNullOrEmpty(url)) { AskText(Loc.T("Előnézet – cím vagy port (pl. 5173)"), "http://localhost:5173", delegate (string s) { s = s.Trim(); int p; if (int.TryParse(s, out p)) s = "http://localhost:" + p; else if (s.Length > 0 && s.IndexOf("://") < 0) s = "http://" + s; if (s.Length > 0) OpenPreviewFor(v, s); }); return; }
            if (t != cur) SelectTab(t);
            string why = WebPreview.WhyNot(); if (why != null) { Toast(why + Loc.T(" Megnyitom a böngészőben.")); OpenExternal(url); return; }
            if (t.Preview == null)
            {
                t.Preview = new WebPreview(); TermTab tt = t; t.Preview.CloseRequested += delegate { ClosePreview(tt); }; t.Preview.ExternalRequested += delegate (string u) { OpenExternal(u); };
                t.PrevSplit = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), ShowsPreview = false, ResizeDirection = GridResizeDirection.Rows, Cursor = Cursors.SizeNS };
                Grid.SetRow(t.PrevSplit, 1); Grid.SetRow(t.Preview, 2); t.Outer.Children.Add(t.PrevSplit); t.Outer.Children.Add(t.Preview);
                t.Outer.RowDefinitions[1].Height = new GridLength(5); t.Outer.RowDefinitions[2].Height = new GridLength(0.95, GridUnitType.Star); t.Outer.RowDefinitions[2].MinHeight = 150;
            }
            t.Preview.Navigate(url); Dispatcher.BeginInvoke(new Action(FocusCurrent), DispatcherPriority.Input);
        }
        void ClosePreview(TermTab t)
        {
            if (t == null || t.Preview == null) return; t.Preview.Shutdown(); t.Outer.Children.Remove(t.Preview); t.Outer.Children.Remove(t.PrevSplit); t.Preview = null; t.PrevSplit = null;
            t.Outer.RowDefinitions[1].Height = new GridLength(0); t.Outer.RowDefinitions[2].Height = new GridLength(0); t.Outer.RowDefinitions[2].MinHeight = 0; t.Outer.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); FocusCurrent();
        }
        static bool SafeHttp(string s) { Uri u; return Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps); }
        static bool LoopbackHttp(string s) { Uri u; return Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.IsLoopback; }
        void OpenExternal(string url) { try { if (!string.IsNullOrEmpty(url) && SafeHttp(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        // a futó parancs kimenetében localhost-os cím jelent meg: felajánljuk (vagy ha az előnézet már nyitva van, követjük)
        void OnDevUrl(TermView v, string url)
        {
            TermTab t = TabOf(v); if (t == null) return; v.LastDevUrl = url;
            if (t.Preview != null) { Uri a, b; if (!(Uri.TryCreate(url, UriKind.Absolute, out a) && Uri.TryCreate(t.Preview.CurrentUrl, UriKind.Absolute, out b) && a.Authority == b.Authority)) t.Preview.Navigate(url); return; }
            if (t != cur) return; HideBanner(); string shown = url.Replace("http://", "").Replace("https://", "").TrimEnd('/');
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = "🌐  " + shown, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            Func<string, bool, Action, Border> chip = delegate (string text, bool accent, Action click) { Border b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Background = accent ? (Brush)new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold } }; b.MouseEnter += delegate { b.Opacity = 0.8; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); }; return b; };
            sp.Children.Add(chip(Loc.T("📺 Előnézet alul"), true, delegate { OpenPreviewFor(v, url); })); sp.Children.Add(chip(Loc.T("↗ Böngészőben"), false, delegate { HideBanner(); OpenExternal(url); })); sp.Children.Add(new TextBlock { Text = "×", Foreground = Muted, FontSize = 17, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, -3, 0, 0) });
            ((TextBlock)sp.Children[sp.Children.Count - 1]).MouseLeftButtonUp += delegate { HideBanner(); };
            banner = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(240, 34, 34, 38)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 16), Child = sp, Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.5, Direction = 270 } };
            overlay.Children.Add(banner); if (bannerT == null) { bannerT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; bannerT.Tick += delegate { HideBanner(); }; } bannerT.Stop(); bannerT.Start();
        }
        void HideBanner() { if (banner != null) overlay.Children.Remove(banner); banner = null; if (bannerT != null) bannerT.Stop(); }

        // ================================================================== linkek
        static readonly string[] ExecExt = { ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".wsc", ".sct", ".hta", ".msi", ".msp", ".msc", ".scr", ".lnk", ".cpl", ".jar", ".reg", ".pif", ".url", ".appref-ms", ".application", ".inf", ".py", ".pyw", ".pyz", ".sh", ".scf", ".chm", ".website", ".settingcontent-ms", ".appx", ".msix", ".appxbundle", ".msixbundle" };
        void OpenLink(LinkInfo li)
        {
            try
            {
                if (li.Kind == "url") { string uu = li.Path; if (uu.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) uu = "https://" + uu; Uri pu; if (Uri.TryCreate(uu, UriKind.Absolute, out pu) && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps || pu.Scheme == Uri.UriSchemeMailto)) Process.Start(new ProcessStartInfo(pu.AbsoluteUri) { UseShellExecute = true }); else Toast(Loc.T("Ezt a linktípust nem nyitom meg (csak http/https/mailto)")); return; }
                string p = li.Path; TermView v = CurrentView; string cwd = v != null ? v.Cwd : null;
                if (p.StartsWith("~")) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.Substring(1).TrimStart('\\', '/'));
                else if (!Path.IsPathRooted(p) && !string.IsNullOrEmpty(cwd)) p = Path.Combine(cwd, p);
                p = p.Replace('/', '\\');
                if (Directory.Exists(p)) { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = true }); return; }
                if (!File.Exists(p)) { Toast(Loc.T("Nem található: ") + Trim(p, 60)); return; }
                if (Array.IndexOf(ExecExt, Path.GetExtension(p).ToLowerInvariant()) >= 0) { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + p + "\"") { UseShellExecute = true }); Toast(Loc.T("Futtatható fájlt nem indítok el kattintásra – megmutattam az Intézőben")); return; }
                if (li.Line > 0 && p.IndexOfAny(new char[] { '%', '&', '^', '!' }) < 0) { string ed = FindEditor(); if (ed != null) { Process.Start(new ProcessStartInfo(ed, "-g \"" + p + ":" + li.Line + (li.Col > 0 ? ":" + li.Col : "") + "\"") { UseShellExecute = false, CreateNoWindow = true }); return; } }
                Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
            }
            catch (Exception ex) { Toast(Loc.T("Megnyitás sikertelen: ") + ex.Message); }
        }
        static string FindEditor()
        {
            foreach (string n in new string[] { "code.cmd", "antigravity.cmd", "code.exe", "antigravity.exe" }) foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) { try { if (d.Trim().Length > 0 && File.Exists(Path.Combine(d.Trim(), n))) return Path.Combine(d.Trim(), n); } catch { } }
            return null;
        }

        // ================================================================== előugró (modális) felületek
        Border Panel(UIElement child, double width)
        {
            return new Border { Width = width, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(244, 28, 28, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1), Child = child, Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.55, Direction = 270 } };
        }
        void ShowModal(Border panel, double top)
        {
            HideModal(); scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }; scrim.MouseLeftButtonDown += delegate { HideModal(); }; overlay.Children.Add(scrim);
            modalPanel = panel; panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Top; panel.Margin = new Thickness(0, top, 0, 0); overlay.Children.Add(panel);
        }
        public void HideModal() { if (scrim != null) overlay.Children.Remove(scrim); if (modalPanel != null) overlay.Children.Remove(modalPanel); scrim = null; modalPanel = null; FocusCurrent(); }
        bool ModalOpen { get { return modalPanel != null; } }

        TextBox Box(string hint)
        {
            TextBox t = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 17, FontWeight = FontWeights.Medium, Padding = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center, Tag = hint };
            GlassMenu.ForTextBox(t); return t;
        }

        // ---- jóváhagyás
        public void ShowConfirm(string title, string text, Action yes)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold });
            sp.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 216)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14), FontFamily = new FontFamily("Consolas"), MaxHeight = 220 });
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Border no = Btn(Loc.T("Mégse"), false, delegate { HideModal(); }), ok = Btn(Loc.T("Beillesztés / igen"), true, delegate { HideModal(); yes(); }); btns.Children.Add(no); btns.Children.Add(ok); sp.Children.Add(btns);
            Border p = Panel(sp, 560); p.Focusable = true; p.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Escape || e.Key == Key.N) { HideModal(); e.Handled = true; } else if (e.Key == Key.Y || (e.Key == Key.Enter && !title.StartsWith(Loc.T("Veszélyes")))) { HideModal(); yes(); e.Handled = true; } };
            ShowModal(p, 90); p.Focus(); Keyboard.Focus(p);
        }
        Border Btn(string text, bool accent, Action click)
        {
            Border b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Background = accent ? (Brush)new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Child = new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12.5 } };
            b.MouseEnter += delegate { b.Opacity = 0.85; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.MouseLeftButtonUp += delegate { click(); }; return b;
        }

        // ---- szöveg bekérése (átnevezés, snippet neve, változók)
        void AskText(string title, string initial, Action<string> done)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) }; sp.Children.Add(new TextBlock { Text = title, Foreground = Muted, FontSize = 12 });
            TextBox tb = Box(""); tb.Text = initial ?? ""; tb.FontSize = 17; sp.Children.Add(tb); Border p = Panel(sp, 480);
            tb.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Escape) { HideModal(); e.Handled = true; } else if (e.Key == Key.Enter) { string v = tb.Text; HideModal(); done(v); e.Handled = true; } };
            ShowModal(p, 110); tb.Focus(); tb.SelectAll(); Keyboard.Focus(tb);
        }

        // ---- lista-választó (parancspaletta, előzmények, vágólap, snippetek közös motorja)
        class Item { public string Title, Sub, Right; public Action Run, RunAlt; public bool Dim; public string Search; public int Ord; }
        void ShowPicker(string hint, List<Item> items, string initial, bool fuzzy, int maxRows)
        {
            StackPanel root = new StackPanel(); Border boxRow = new Border { Padding = new Thickness(18, 10, 18, 10), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid bg = new Grid(); TextBox tb = Box(hint); TextBlock ph = new TextBlock { Text = hint, Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), FontSize = 17, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium }; bg.Children.Add(tb); bg.Children.Add(ph); boxRow.Child = bg; root.Children.Add(boxRow);
            StackPanel list = new StackPanel { Margin = new Thickness(8, 6, 8, 8) }; root.Children.Add(list); Border panel = Panel(root, 660);
            List<Item> shown = new List<Item>(); int sel = 0; List<Border> rows = new List<Border>();
            Action select = delegate { for (int i = 0; i < rows.Count; i++) rows[i].Background = i == sel ? (Brush)new SolidColorBrush(Color.FromArgb(70, 10, 132, 255)) : Brushes.Transparent; };
            Action rebuild = delegate
            {
                string q = tb.Text.Trim().ToLowerInvariant(); ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; shown.Clear(); List<KeyValuePair<double, Item>> sc = new List<KeyValuePair<double, Item>>();
                int oi = 0; foreach (Item it in items) { it.Ord = oi++; double s = q.Length == 0 ? 0 : (fuzzy ? Score(it.Search ?? it.Title.ToLowerInvariant(), q) : (it.Title.ToLowerInvariant().Contains(q) ? 0 : -1)); if (s >= 0) sc.Add(new KeyValuePair<double, Item>(s, it)); }
                if (q.Length > 0) sc.Sort(delegate (KeyValuePair<double, Item> x, KeyValuePair<double, Item> y) { int cc = x.Key.CompareTo(y.Key); return cc != 0 ? cc : x.Value.Ord.CompareTo(y.Value.Ord); });
                for (int i = 0; i < sc.Count && i < maxRows; i++) shown.Add(sc[i].Value);
                list.Children.Clear(); rows.Clear(); sel = Math.Min(sel, Math.Max(0, shown.Count - 1));
                if (shown.Count == 0) list.Children.Add(new TextBlock { Text = Loc.T("Nincs találat"), Foreground = Muted, Margin = new Thickness(12, 10, 12, 10), FontSize = 13 });
                for (int i = 0; i < shown.Count; i++)
                {
                    Item it = shown[i]; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    StackPanel left = new StackPanel(); left.Children.Add(new TextBlock { Text = it.Title, Foreground = it.Dim ? Muted : Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = it.Sub == "mono" ? new FontFamily("Consolas") : UiFont });
                    if (!string.IsNullOrEmpty(it.Sub) && it.Sub != "mono") left.Children.Add(new TextBlock { Text = it.Sub, Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                    g.Children.Add(left); if (!string.IsNullOrEmpty(it.Right)) { TextBlock r = new TextBlock { Text = it.Right, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }; Grid.SetColumn(r, 1); g.Children.Add(r); }
                    Border row = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6), Child = g, Cursor = Cursors.Hand }; int idx = i;
                    row.MouseEnter += delegate { sel = idx; select(); }; row.MouseLeftButtonUp += delegate { HideModal(); if (it.Run != null) it.Run(); }; rows.Add(row); list.Children.Add(row);
                }
                select();
            };
            tb.TextChanged += delegate { sel = 0; rebuild(); };
            tb.PreviewKeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { HideModal(); e.Handled = true; }
                else if (e.Key == Key.Down) { if (shown.Count > 0) { sel = (sel + 1) % shown.Count; select(); } e.Handled = true; }
                else if (e.Key == Key.Up) { if (shown.Count > 0) { sel = (sel - 1 + shown.Count) % shown.Count; select(); } e.Handled = true; }
                else if (e.Key == Key.Enter && shown.Count > 0) { Item it = shown[sel]; bool alt = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && it.RunAlt != null; HideModal(); if (alt) it.RunAlt(); else if (it.Run != null) it.Run(); e.Handled = true; }
            };
            tb.Text = initial ?? ""; ShowModal(panel, 70); rebuild(); tb.Focus(); tb.CaretIndex = tb.Text.Length; Keyboard.Focus(tb);
        }

        static double Score(string text, string q)
        {
            int i = text.IndexOf(q, StringComparison.Ordinal); if (i == 0) return 0; if (i > 0) { char pc = text[i - 1]; return (pc == ' ' || pc == ':' || pc == '-' || pc == '.' || pc == '/') ? 30 + i * 0.1 : 90 + i * 0.5; }
            int p = 0, gaps = 0, last = -2; for (int k = 0; k < text.Length && p < q.Length; k++) if (text[k] == q[p]) { if (last >= 0 && k != last + 1) gaps++; last = k; p++; }
            return p == q.Length ? 300 + gaps * 6 + text.Length * 0.2 : -1;
        }

        // ================================================================== parancspaletta, előzmények, vágólap, snippetek
        public void ShowPalette(string initial)
        {
            List<Item> items = new List<Item>();
            foreach (TermAction a in actions)
            {
                TermAction act = a; bool en = a.Enabled == null || a.Enabled(); string ck = a.Checked != null ? (a.Checked() ? "✓ " : "  ") : "";
                items.Add(new Item { Title = ck + a.Title, Right = (a.Cat ?? "") + (string.IsNullOrEmpty(a.Key) ? "" : "   " + a.Key), Dim = !en, Search = (a.Cat + " " + a.Title).ToLowerInvariant(), Run = delegate { if (act.Enabled == null || act.Enabled()) act.Run(); } });
            }
            ShowPicker(Loc.T("Parancs keresése…  (Ctrl+Shift+P)"), items, initial, true, 11);
        }

        public void ShowTabSwitcher()
        {
            List<Item> items = new List<Item>(); foreach (TermTab t in tabs) { TermTab tt = t; items.Add(new Item { Title = LabelFor(t), Sub = (t.Focus != null ? t.Focus.Cwd : "") + "  ·  " + t.Prof.Name, Right = (tabs.IndexOf(t) + 1).ToString(), Run = delegate { SelectTab(tt); } }); }
            ShowPicker(Loc.T("Ugrás lapra…"), items, "", true, 12);
        }

        static string HistoryFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt"); } }
        public List<string> History()
        {
            List<string> r = new List<string>(); HashSet<string> seen = new HashSet<string>();
            foreach (TermTab t in tabs) foreach (TermView v in t.Leaves()) for (int i = v.Marks.Count - 1; i >= 0; i--) { string c = v.Marks[i].Cmd; if (v.Marks[i].Started && c.Length > 0 && seen.Add(c)) r.Add(c); }
            try
            {
                string f = HistoryFile; if (File.Exists(f))
                {
                    string[] lines; using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) lines = sr.ReadToEnd().Split('\n');
                    for (int i = lines.Length - 1; i >= 0 && r.Count < 3000; i--) { string c = lines[i].TrimEnd('\r'); if (c.Length > 0 && seen.Add(c)) r.Add(c); }
                }
            }
            catch { }
            return r;
        }
        public void ShowHistory()
        {
            List<Item> items = new List<Item>(); foreach (string c in History()) { string cmd = c; items.Add(new Item { Title = c, Sub = "mono", Run = delegate { InsertCommand(cmd, false); }, RunAlt = delegate { InsertCommand(cmd, true); } }); }
            ShowPicker(Loc.T("Előzmények (Enter: beírás, Shift+Enter: futtatás)"), items, "", true, 10);
        }
        void InsertCommand(string cmd, bool run) { TermView v = CurrentView; if (v == null) return; FocusCurrent(); v.RunCommand(cmd, run); }

        public void ShowClipHistory()
        {
            List<Item> items = new List<Item>(); foreach (string c in clipHist) { string t = c; string one = t.Replace("\r", " ").Replace("\n", " ↵ "); items.Add(new Item { Title = Trim(one, 120), Right = t.Length + Loc.T(" kar."), Sub = "mono", Run = delegate { TermView v = CurrentView; if (v != null) v.PasteText(t); } }); }
            if (items.Count == 0) { Toast(Loc.T("A vágólap-előzmény még üres (másolj valamit a terminálban)")); return; }
            ShowPicker(Loc.T("Vágólap-előzmény (Enter: beillesztés)"), items, "", true, 10);
        }

        // ---- snippetek: name|parancs ; a "!" előtag azonnal futtat; változók: {clip} {cwd} {date} {time} {user} {host} {lastcmd} {ask:Címke}
        string SnippetFile { get { return Path.Combine(Cfg.Dir, "snippets.txt"); } }
        public List<string[]> Snippets()
        {
            string f = SnippetFile; if (!File.Exists(f)) { try { File.WriteAllLines(f, new string[] { Loc.T("# név|parancs   (a parancs elé tett ! azonnal le is futtatja; változók: {clip} {cwd} {date} {time} {user} {host} {lastcmd} {ask:Címke})"),
                Loc.T("git állapot|!git status -sb"), Loc.T("git napló (gráf)|!git log --oneline --graph --decorate -n 25"), Loc.T("git commit mindenre|git add -A; git commit -m \"{ask:Commit üzenet}\""), Loc.T("git ág váltás|git checkout {ask:Ág neve}"), "git push|!git push",
                Loc.T("pnpm telepítés|!pnpm install"), "pnpm dev szerver|!pnpm run dev", Loc.T("npm telepítés|!npm install"), Loc.T("port használata|netstat -ano | findstr :{ask:Port}"), Loc.T("port felszabadítása|killport {ask:Port}"),
                Loc.T("legnagyobb fájlok|!Get-ChildItem -Recurse -File | Sort-Object Length -Descending | Select-Object -First 15 FullName, @{n='MB';e={[int]($_.Length/1MB)}}"), Loc.T("mappa méretei|!size"), Loc.T("folyamatok memória szerint|!Get-Process | Sort-Object WS -Descending | Select-Object -First 15 Name, Id, @{n='MB';e={[int]($_.WS/1MB)}}"),
                Loc.T("IP-címek|!myip"), Loc.T("DNS ürítés|!ipconfig /flushdns"), Loc.T("hálózat tesztelése|!ping 1.1.1.1 -n 4"), Loc.T("fájl keresése név szerint|ff {ask:Név}"), Loc.T("szöveg keresése fájlokban|grep \"{ask:Keresett szöveg}\" . -i"),
                "python szerver (8000)|!serve 8000", Loc.T("venv létrehozása|python -m venv .venv; .\\.venv\\Scripts\\Activate.ps1"), Loc.T("projekt megnyitása szerkesztőben|!edit ."), Loc.T("mappa megnyitása Intézőben|!open ."), Loc.T("JSON szépítés vágólapról|{clip} | json"), Loc.T("idő most|!now"), "UUID|!uuid" }, new UTF8Encoding(false)); } catch { } }
            List<string[]> l = new List<string[]>(); try { foreach (string line in File.ReadAllLines(f)) { if (line.TrimStart().StartsWith("#") || line.Trim().Length == 0) continue; int i = line.IndexOf('|'); if (i > 0) l.Add(new string[] { line.Substring(0, i).Trim(), line.Substring(i + 1) }); } } catch { }
            return l;
        }
        void RunSnippet(string name, string body)
        {
            bool run = body.StartsWith("!"); if (run) body = body.Substring(1); List<string> asks = new List<string>(); int pos = 0;
            while (true) { int a = body.IndexOf("{ask:", pos, StringComparison.Ordinal); if (a < 0) break; int b = body.IndexOf('}', a); if (b < 0) break; string label = body.Substring(a + 5, b - a - 5); if (!asks.Contains(label)) asks.Add(label); pos = b + 1; }
            Dictionary<string, string> vals = new Dictionary<string, string>(); Action<int> step = null;
            step = delegate (int i)
            {
                if (i >= asks.Count) { FinishSnippet(body, vals, run); return; }
                AskText(name + " – " + asks[i], "", delegate (string v) { vals[asks[i]] = v; step(i + 1); });
            };
            step(0);
        }
        void FinishSnippet(string body, Dictionary<string, string> vals, bool run)
        {
            foreach (KeyValuePair<string, string> kv in vals) body = body.Replace("{ask:" + kv.Key + "}", kv.Value);
            TermView v = CurrentView; string clip = ""; try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
            body = body.Replace("{clip}", clip).Replace("{cwd}", v != null ? v.Cwd ?? "" : "").Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd")).Replace("{time}", DateTime.Now.ToString("HH:mm:ss")).Replace("{user}", Environment.UserName).Replace("{host}", Environment.MachineName.ToLower()).Replace("{lastcmd}", v != null ? v.LastCommand : "");
            InsertCommand(body, run);
        }
        public void ShowSnippets()
        {
            List<Item> items = new List<Item>(); foreach (string[] s in Snippets()) { string n = s[0], b = s[1]; items.Add(new Item { Title = n, Sub = "mono", Right = b.StartsWith("!") ? "futtat" : Loc.T("beír"), Search = (n + " " + b).ToLowerInvariant(), Run = delegate { RunSnippet(n, b); } }); }
            ShowPicker(Loc.T("Snippetek…  (a snippets.txt fájlban szerkeszthetők)"), items, "", true, 10);
        }
        void SaveSnippetFromLast()
        {
            TermView v = CurrentView; string last = v != null ? v.LastCommand : ""; if (string.IsNullOrEmpty(last)) { Toast(Loc.T("Még nincs végrehajtott parancs")); return; }
            AskText("Snippet neve (parancs: " + Trim(last, 50) + ")", "", delegate (string name) { if (name.Trim().Length == 0) return; try { File.AppendAllText(SnippetFile, name.Trim() + "|" + last + Environment.NewLine, new UTF8Encoding(false)); Toast(Loc.T("Snippet mentve: ") + name.Trim()); } catch { } });
        }

        // ================================================================== keresősáv
        public void ShowFind()
        {
            if (findBar == null) BuildFindBar(); if (!overlay.Children.Contains(findBar)) overlay.Children.Add(findBar);
            findBar.Margin = new Thickness(0, (tabBar.Visibility == Visibility.Visible ? tabBar.ActualHeight : 0) + 8, 22, 0);
            TermView v = CurrentView; if (v != null && v.HasSelection && v.SelectionText.Length > 0 && v.SelectionText.IndexOf('\n') < 0) findBox.Text = v.SelectionText; findBox.Focus(); findBox.SelectAll(); Keyboard.Focus(findBox); DoFind();
        }
        void HideFind() { if (findBar != null) overlay.Children.Remove(findBar); TermView v = CurrentView; if (v != null) v.ClearFind(); FocusCurrent(); }
        void BuildFindBar()
        {
            findBox = new TextBox { Width = 190, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }; GlassMenu.ForTextBox(findBox);
            findCount = new TextBlock { Foreground = Muted, FontSize = 11.5, Width = 62, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            Func<string, string, Action, Border> tb = delegate (string txt, string tip, Action click) { Border b = new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand, Child = new TextBlock { Text = txt, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, ToolTip = tip, Background = Brushes.Transparent }; b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); }; b.MouseLeave += delegate { b.Background = Brushes.Transparent; }; b.MouseLeftButtonUp += delegate { click(); }; return b; };
            Border caseB = null, rxB = null;
            caseB = tb("Aa", Loc.T("Kis/nagybetű számít"), delegate { findCase = !findCase; caseB.Child.SetValue(TextBlock.ForegroundProperty, findCase ? new SolidColorBrush(Color.FromRgb(90, 190, 255)) : Brushes.White); DoFind(); });
            rxB = tb(".*", Loc.T("Reguláris kifejezés"), delegate { findRegex = !findRegex; rxB.Child.SetValue(TextBlock.ForegroundProperty, findRegex ? new SolidColorBrush(Color.FromRgb(90, 190, 255)) : Brushes.White); DoFind(); });
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 8, 5) };
            sp.Children.Add(new TextBlock { Text = "⌕", Foreground = Muted, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 6, 0) }); sp.Children.Add(findBox); sp.Children.Add(findCount); sp.Children.Add(caseB); sp.Children.Add(rxB);
            sp.Children.Add(tb("↑", Loc.T("Előző (Shift+Enter)"), delegate { FindStep(-1); })); sp.Children.Add(tb("↓", Loc.T("Következő (Enter)"), delegate { FindStep(1); })); sp.Children.Add(tb("×", Loc.T("Bezár (Esc)"), HideFind));
            findBar = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(244, 30, 30, 34)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = sp, Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.5, Direction = 270 } };
            findBox.TextChanged += delegate { DoFind(); };
            findBox.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Escape) { HideFind(); e.Handled = true; } else if (e.Key == Key.Enter) { FindStep((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; } };
        }
        void DoFind() { TermView v = CurrentView; if (v == null || findBox == null) return; int n = v.Find(findBox.Text, findCase, findRegex); UpdateFindCount(); }
        void FindStep(int d) { TermView v = CurrentView; if (v == null) return; v.FindStep(d); UpdateFindCount(); }
        void UpdateFindCount() { TermView v = CurrentView; if (v == null) return; findCount.Text = v.MatchCount == 0 ? (findBox.Text.Length > 0 ? "nincs" : "") : (v.CurrentMatch + 1) + "/" + v.MatchCount; }
        bool FindOpen { get { return findBar != null && overlay.Children.Contains(findBar); } }

        // ================================================================== menük
        void TabMenu(TermTab t)
        {
            Func<string, Color, MenuEntry> col = delegate (string n, Color c) { return GlassMenu.Item("● " + n, delegate { t.Tag = c; RefreshChips(); }); };
            GlassMenu.Show(t.Chip, GlassMenu.Item(Loc.T("Átnevezés…"), delegate { AskText(Loc.T("Lap neve (üresen: automatikus)"), t.Custom ?? "", delegate (string s) { t.Custom = s.Trim().Length > 0 ? s.Trim() : null; UpdateLabels(); }); }),
                GlassMenu.Item(Loc.T("Másolás új lapra (ugyanott)"), delegate { NewTab(t.Prof, t.Focus != null ? t.Focus.Cwd : null); }), GlassMenu.Sep(),
                GlassMenu.Menu(Loc.T("Szín"), col(Loc.T("Piros"), Color.FromRgb(255, 95, 87)), col(Loc.T("Narancs"), Color.FromRgb(255, 159, 10)), col(Loc.T("Sárga"), Color.FromRgb(254, 188, 46)), col(Loc.T("Zöld"), Color.FromRgb(48, 209, 88)), col(Loc.T("Kék"), Color.FromRgb(10, 132, 255)), col(Loc.T("Lila"), Color.FromRgb(191, 90, 242)), GlassMenu.Item(Loc.T("Nincs"), delegate { t.Tag = null; RefreshChips(); })),
                GlassMenu.Item(Loc.T("Balra mozgat"), delegate { SelectTab(t); MoveTab(-1); }), GlassMenu.Item(Loc.T("Jobbra mozgat"), delegate { SelectTab(t); MoveTab(1); }), GlassMenu.Sep(),
                GlassMenu.Item(Loc.T("Bezárás"), delegate { CloseTab(t); }), GlassMenu.Item(Loc.T("Többi lap bezárása"), delegate { foreach (TermTab x in new List<TermTab>(tabs)) if (x != t) DoCloseTab(x); }),
                GlassMenu.Item(Loc.T("Jobbra lévők bezárása"), delegate { int i = tabs.IndexOf(t); foreach (TermTab x in new List<TermTab>(tabs)) if (tabs.IndexOf(x) > i) DoCloseTab(x); }));
        }

        void ProfileMenu(FrameworkElement anchor)
        {
            List<MenuEntry> l = new List<MenuEntry>(); foreach (ShellProfile p in ShellProfiles.Detect()) { ShellProfile pp = p; l.Add(GlassMenu.Item(p.Name, delegate { NewTab(pp, CurrentView != null ? CurrentView.Cwd : null); })); }
            l.Add(GlassMenu.Sep()); l.Add(GlassMenu.Item(Loc.T("Profilok beállítása (term.ini)"), delegate { OpenFile(Path.Combine(Cfg.Dir, "term.ini")); })); GlassMenu.Show(anchor, l.ToArray());
        }

        void ShowContext(TermView v, Point p)
        {
            LinkInfo li = null; try { double cw = v.CellW, ch = v.CellH; int r = (int)((p.Y - v.Pad.Top) / ch), c = (int)((p.X - v.Pad.Left) / cw); int abs = v.Scr.Sb.Count - v.Scr.ViewOffset + Math.Max(0, Math.Min(v.Rows - 1, r)); li = v.LinkAt(abs, Math.Max(0, Math.Min(v.Cols - 1, c))); } catch { }
            bool sel = v.HasSelection; List<MenuEntry> m = new List<MenuEntry>();
            if (li != null) { LinkInfo link = li; m.Add(GlassMenu.Item(li.Kind == "url" ? Loc.T("Link megnyitása") : Loc.T("Megnyitás: ") + Trim(li.Text, 34), delegate { OpenLink(link); })); if (li.Kind == "url") m.Add(GlassMenu.Item(Loc.T("📺 Előnézet alul (a terminálban)"), delegate { OpenPreviewFor(v, link.Path); })); m.Add(GlassMenu.Item(Loc.T("Link másolása"), delegate { try { Clipboard.SetText(link.Text); } catch { } })); m.Add(GlassMenu.Sep()); }
            m.Add(sel ? GlassMenu.Item(Loc.T("Másolás"), "Ctrl+Shift+C", delegate { v.Copy(); }) : GlassMenu.Off(Loc.T("Másolás")));
            m.Add(sel ? GlassMenu.Item(Loc.T("Másolás HTML-ként (színekkel)"), delegate { v.CopyAsHtml(true); }) : GlassMenu.Off(Loc.T("Másolás HTML-ként")));
            m.Add(GlassMenu.Item(Loc.T("Beillesztés"), "Ctrl+V", delegate { v.Paste(); }));
            m.Add(GlassMenu.Menu(Loc.T("Beillesztés másképp"), GlassMenu.Item(Loc.T("Formázás nélkül"), "Ctrl+Shift+V", delegate { v.Paste(PasteMode.Plain); }), GlassMenu.Item(Loc.T("Egy sorba"), delegate { v.Paste(PasteMode.OneLine); }), GlassMenu.Item(Loc.T("Idézőjelezett útvonalként"), delegate { v.Paste(PasteMode.Quoted); }), GlassMenu.Item(Loc.T("Vágólap-előzmény…"), "Ctrl+Shift+H", ShowClipHistory)));
            m.Add(GlassMenu.Item(Loc.T("Összes kijelölése"), "Ctrl+Shift+A", delegate { v.SelectAll(); })); m.Add(GlassMenu.Sep());
            m.Add(GlassMenu.Item(Loc.T("Keresés…"), "Ctrl+F", ShowFind)); m.Add(GlassMenu.Item(Loc.T("Előzmények…"), "Ctrl+R", ShowHistory)); m.Add(GlassMenu.Item(Loc.T("Snippetek…"), "Ctrl+Shift+S", ShowSnippets)); m.Add(GlassMenu.Sep());
            PromptMark lm = v.LastMark; m.Add(lm != null ? GlassMenu.Item(Loc.T("Utolsó kimenet másolása"), "Ctrl+Shift+O", delegate { v.CopyLastOutput(); }) : GlassMenu.Off(Loc.T("Utolsó kimenet másolása"))); m.Add(lm != null ? GlassMenu.Item(Loc.T("Utolsó parancs újrafuttatása"), delegate { v.RerunLast(); }) : GlassMenu.Off(Loc.T("Utolsó parancs újrafuttatása"))); m.Add(GlassMenu.Sep());
            m.Add(GlassMenu.Item(Loc.T("Osztás jobbra"), "Ctrl+Shift+D", delegate { Split(true); })); m.Add(GlassMenu.Item(Loc.T("Osztás lefelé"), "Ctrl+Shift+E", delegate { Split(false); })); m.Add(GlassMenu.Item(Loc.T("Panel bezárása"), "Ctrl+Shift+W", CloseFocusedPane)); m.Add(GlassMenu.Sep());
            List<MenuEntry> th = new List<MenuEntry>(); foreach (TermTheme t in TermTheme.All) { string n = t.Name; th.Add(GlassMenu.Check(n, St.Theme == n, delegate { Setting(delegate { St.Theme = n; }); })); }
            m.Add(GlassMenu.Menu(Loc.T("Téma"), th.ToArray())); m.Add(GlassMenu.Item(Loc.T("Parancspaletta…"), "Ctrl+Shift+P", delegate { ShowPalette(""); }));
            GlassMenu.Show(v, m.ToArray());
        }

        static void OpenFile(string path) { try { if (!File.Exists(path)) File.WriteAllText(path, ""); string ex2 = Path.GetExtension(path).ToLowerInvariant(); if (ex2 == ".ps1" || ex2 == ".ini" || ex2 == ".txt") Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false }); else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }
        static void OpenFolder(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); } catch { } }

        // ================================================================== munkamenet
        string SessionFile { get { return Path.Combine(Cfg.Dir, "session.ini"); } }
        public void SaveSession()
        {
            try
            {
                if (NoSession || !St.RestoreSession || Embedded) return; List<string> l = new List<string>(); foreach (TermTab t in tabs) l.Add(t.Prof.Name + "|" + (t.Focus != null ? t.Focus.Cwd : "") + "|" + (t.Custom ?? ""));
                File.WriteAllLines(SessionFile, l.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
        bool LoadSession()
        {
            try
            {
                if (Embedded || !File.Exists(SessionFile)) return false; int n = 0;
                foreach (string line in File.ReadAllLines(SessionFile)) { string[] p = line.Split('|'); if (p.Length < 2) continue; ShellProfile prof = null; foreach (ShellProfile x in ShellProfiles.Detect()) if (x.Name == p[0]) prof = x; if (prof == null) prof = ShellProfiles.Default(); string cwd = p[1].Length > 0 && Directory.Exists(p[1]) ? p[1] : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); TermTab t = NewTab(prof, cwd); if (p.Length > 2 && p[2].Length > 0) t.Custom = p[2]; n++; if (n >= 12) break; }
                if (n > 0) { SelectTab(tabs[0]); return true; }
            }
            catch { }
            return false;
        }
        public void Shutdown() { try { labelT.Stop(); St.Changed -= ApplyAll; if (bannerT != null) bannerT.Stop(); } catch { } SaveSession(); foreach (TermTab pt in tabs) if (pt.Preview != null) { pt.Preview.Shutdown(); pt.Preview = null; } foreach (TermTab t in new List<TermTab>(tabs)) foreach (TermView v in t.Leaves()) v.Shutdown(); }

        // ================================================================== gyorsbillentyűk
        static string KeyName(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return k.ToString(); if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString(); if (k >= Key.F1 && k <= Key.F12) return k.ToString();
            switch (k) { case Key.OemPlus: return "Plus"; case Key.OemMinus: return "Minus"; case Key.OemComma: return "Comma"; case Key.OemPeriod: return "Period"; case Key.Oem3: return "Backtick"; case Key.OemOpenBrackets: return "LBracket"; case Key.OemCloseBrackets: return "RBracket"; }
            return k.ToString();
        }
        void LoadKeys()
        {
            keyMap.Clear(); Dictionary<string, string> over = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { string f = Path.Combine(Cfg.Dir, "keys.ini"); if (File.Exists(f)) foreach (string l in File.ReadAllLines(f)) { int i = l.IndexOf('='); if (i > 0 && !l.TrimStart().StartsWith("#")) over[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim(); } } catch { }
            foreach (TermAction a in actions) { string key; if (over.TryGetValue(a.Id, out key)) a.Key = key; if (!string.IsNullOrEmpty(a.Key) && !keyMap.ContainsKey(a.Key.ToLowerInvariant())) keyMap[a.Key.ToLowerInvariant()] = a; }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (ModalOpen || Keyboard.FocusedElement is TextBox) return;       // a keresősáv / felugró mezők maguk kezelik a billentyűket
            Key k = e.Key == Key.System ? e.SystemKey : e.Key; if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftShift || k == Key.RightShift || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LWin || k == Key.RWin) return;
            ModifierKeys mk = Keyboard.Modifiers; bool ctrl = (mk & ModifierKeys.Control) != 0, alt = (mk & ModifierKeys.Alt) != 0, shift = (mk & ModifierKeys.Shift) != 0; if (ctrl && alt) return;
            if (ctrl && k == Key.Tab) { CycleTab(shift ? -1 : 1); e.Handled = true; return; }
            TermView v = CurrentView; bool multiPane = cur != null && cur.Leaves().Count > 1;
            if (alt && !ctrl && !shift && multiPane && (k == Key.Left || k == Key.Up)) { CyclePane(-1); e.Handled = true; return; }
            if (alt && !ctrl && !shift && multiPane && (k == Key.Right || k == Key.Down)) { CyclePane(1); e.Handled = true; return; }
            if (ctrl && !shift && !alt && v != null && v.HasMarks && (k == Key.Up || k == Key.Down)) { v.JumpPrompt(k == Key.Up ? -1 : 1); e.Handled = true; return; }
            if (FindOpen && k == Key.F3) { FindStep(shift ? -1 : 1); e.Handled = true; return; }
            if (v != null && v.Scr.Alt && ctrl && !shift && !alt) return;       // vim, less, htop… saját Ctrl+billentyűit nem vesszük el
            string name = (ctrl ? "ctrl+" : "") + (alt ? "alt+" : "") + (shift ? "shift+" : "") + KeyName(k).ToLowerInvariant(); TermAction a;
            if (keyMap.TryGetValue(name, out a)) { if (a.Enabled == null || a.Enabled()) { a.Run(); e.Handled = true; } }
        }

        // ================================================================== a műveletek jegyzéke (a parancspaletta és a gyorsbillentyűk forrása)
        void Add(string id, string cat, string title, string key, Action run) { actions.Add(new TermAction { Id = id, Cat = cat, Title = title, Key = key, Run = run }); }
        void AddT(string id, string cat, string title, string key, Func<bool> get, Action<bool> set) { actions.Add(new TermAction { Id = id, Cat = cat, Title = title, Key = key, Checked = get, Run = delegate { Setting(delegate { set(!get()); }); } }); }
        void AddV(string id, string cat, string title, string key, Action<TermView> run) { actions.Add(new TermAction { Id = id, Cat = cat, Title = title, Key = key, Enabled = delegate { return CurrentView != null; }, Run = delegate { TermView v = CurrentView; if (v != null) run(v); } }); }
        void AddW(string id, string cat, string title, string key, Func<Action> pick) { actions.Add(new TermAction { Id = id, Cat = cat, Title = title, Key = key, Enabled = delegate { return pick() != null; }, Run = delegate { Action a = pick(); if (a != null) a(); } }); }

        void BuildActions()
        {
            string T = Loc.T("Lapok"), P = Loc.T("Panelek"), E = Loc.T("Szerkesztés"), S = Loc.T("Keresés"), V = Loc.T("Nézet"), TH = Loc.T("Téma"), N = Loc.T("Navigáció"), B = Loc.T("Parancs-blokk"), X = "Export", SH = "Shell", O = Loc.T("Megnyitás"), C = Loc.T("Beállítás"), W = Loc.T("Ablak"), H = Loc.T("Súgó");
            // ---- lapok
            Add("tab.new", T, Loc.T("Új lap"), "Ctrl+Shift+T", delegate { NewTab(ShellProfiles.Default(), CurrentView != null ? CurrentView.Cwd : null); });
            Add("tab.duplicate", T, Loc.T("Lap duplikálása (ugyanabban a mappában)"), "", delegate { if (cur != null) NewTab(cur.Prof, cur.Focus != null ? cur.Focus.Cwd : null); });
            Add("tab.close", T, Loc.T("Lap bezárása"), "", delegate { if (cur != null) CloseTab(cur); });
            Add("tab.closeothers", T, Loc.T("Többi lap bezárása"), "", delegate { TermTab c = cur; foreach (TermTab x in new List<TermTab>(tabs)) if (x != c) DoCloseTab(x); });
            Add("tab.closeright", T, Loc.T("Jobbra lévő lapok bezárása"), "", delegate { int i = tabs.IndexOf(cur); foreach (TermTab x in new List<TermTab>(tabs)) if (tabs.IndexOf(x) > i) DoCloseTab(x); });
            Add("tab.reopen", T, Loc.T("Bezárt lap újranyitása"), "Ctrl+Shift+U", delegate { if (closedTabs.Count == 0) { Toast(Loc.T("Nincs újranyitható lap")); return; } string[] c = closedTabs.Pop(); ShellProfile pr = null; foreach (ShellProfile x in ShellProfiles.Detect()) if (x.Name == c[0]) pr = x; NewTab(pr ?? ShellProfiles.Default(), c[1].Length > 0 && Directory.Exists(c[1]) ? c[1] : null); });
            Add("tab.next", T, Loc.T("Következő lap"), "Ctrl+PageDown", delegate { CycleTab(1); }); Add("tab.prev", T, Loc.T("Előző lap"), "Ctrl+PageUp", delegate { CycleTab(-1); });
            for (int i = 1; i <= 9; i++) { int idx = i - 1; Add("tab.select" + i, T, Loc.T("Ugrás a(z) ") + i + Loc.T(". lapra"), "Ctrl+" + i, delegate { SelectIndex(idx); }); }
            Add("tab.moveleft", T, Loc.T("Lap mozgatása balra"), "Ctrl+Shift+PageUp", delegate { MoveTab(-1); }); Add("tab.moveright", T, Loc.T("Lap mozgatása jobbra"), "Ctrl+Shift+PageDown", delegate { MoveTab(1); });
            Add("tab.rename", T, Loc.T("Lap átnevezése…"), "", delegate { if (cur != null) { TermTab t = cur; AskText(Loc.T("Lap neve (üresen: automatikus)"), t.Custom ?? "", delegate (string s) { t.Custom = s.Trim().Length > 0 ? s.Trim() : null; UpdateLabels(); }); } });
            Add("tab.switch", T, Loc.T("Ugrás lapra… (lista)"), "Ctrl+Shift+G", ShowTabSwitcher);
            string[] cn = { Loc.T("Piros"), Loc.T("Narancs"), Loc.T("Sárga"), Loc.T("Zöld"), Loc.T("Kék"), Loc.T("Lila") }; Color[] cc = { Color.FromRgb(255, 95, 87), Color.FromRgb(255, 159, 10), Color.FromRgb(254, 188, 46), Color.FromRgb(48, 209, 88), Color.FromRgb(10, 132, 255), Color.FromRgb(191, 90, 242) };
            for (int i = 0; i < cn.Length; i++) { Color c = cc[i]; Add("tab.color." + i, T, Loc.T("Lap színe: ") + cn[i], "", delegate { if (cur != null) { cur.Tag = c; RefreshChips(); } }); }
            Add("tab.color.none", T, Loc.T("Lap színe: nincs"), "", delegate { if (cur != null) { cur.Tag = null; RefreshChips(); } });
            AddT("tab.always", T, Loc.T("Lapsáv mindig látszik"), "", delegate { return St.ShowTabsAlways; }, delegate (bool b) { St.ShowTabsAlways = b; });
            foreach (ShellProfile pr in ShellProfiles.Detect()) { ShellProfile pp = pr; Add("profile." + pr.Name, Loc.T("Profil"), Loc.T("Új lap: ") + pr.Name, "", delegate { NewTab(pp, CurrentView != null ? CurrentView.Cwd : null); }); }
            // ---- panelek
            Add("pane.right", P, Loc.T("Osztás jobbra (új panel)"), "Ctrl+Shift+D", delegate { Split(true); }); Add("pane.down", P, Loc.T("Osztás lefelé (új panel)"), "Ctrl+Shift+E", delegate { Split(false); });
            Add("pane.close", P, Loc.T("Panel bezárása"), "Ctrl+Shift+W", CloseFocusedPane); Add("pane.next", P, Loc.T("Következő panel"), "", delegate { CyclePane(1); }); Add("pane.prev", P, Loc.T("Előző panel"), "", delegate { CyclePane(-1); });
            Add("pane.zoom", P, Loc.T("Panel kinagyítása / visszaállítása"), "Ctrl+Shift+Z", ToggleZoom); Add("pane.equal", P, Loc.T("Panelek egyenlő méretűre"), "", Equalize);
            actions.Add(new TermAction { Id = "pane.broadcast", Cat = P, Title = Loc.T("Bevitel küldése minden panelre (broadcast)"), Key = "Ctrl+Shift+B", Checked = delegate { return cur != null && cur.Broadcast; }, Run = delegate { if (cur != null) { cur.Broadcast = !cur.Broadcast; Toast(cur.Broadcast ? Loc.T("Broadcast BE: a gépelés minden panelre megy") : Loc.T("Broadcast KI")); } } });
            // ---- szerkesztés
            AddV("edit.copy", E, Loc.T("Másolás"), "Ctrl+Shift+C", delegate (TermView v) { v.Copy(); }); AddV("edit.copyhtml", E, Loc.T("Másolás HTML-ként (színekkel)"), "", delegate (TermView v) { v.CopyAsHtml(true); });
            AddV("edit.copyall", E, Loc.T("Teljes kimenet másolása"), "", delegate (TermView v) { try { Clipboard.SetText(v.AllText(true)); Toast(Loc.T("Teljes kimenet másolva")); } catch { } });
            AddV("edit.copycwd", E, Loc.T("Munkamappa másolása"), "", delegate (TermView v) { try { Clipboard.SetText(v.Cwd ?? ""); Toast(Loc.T("Másolva: ") + v.Cwd); } catch { } });
            AddV("edit.paste", E, Loc.T("Beillesztés"), "Ctrl+V", delegate (TermView v) { v.Paste(); }); AddV("edit.paste.plain", E, Loc.T("Beillesztés formázás nélkül (okos idézőjelek javítva)"), "Ctrl+Shift+V", delegate (TermView v) { v.Paste(PasteMode.Plain); });
            AddV("edit.paste.oneline", E, Loc.T("Beillesztés egy sorba"), "", delegate (TermView v) { v.Paste(PasteMode.OneLine); }); AddV("edit.paste.quoted", E, Loc.T("Beillesztés idézőjelezett útvonalként"), "", delegate (TermView v) { v.Paste(PasteMode.Quoted); });
            AddV("edit.selectall", E, Loc.T("Összes kijelölése"), "Ctrl+Shift+A", delegate (TermView v) { v.SelectAll(); }); AddV("edit.selectline", E, Loc.T("Aktuális sor kijelölése"), "", delegate (TermView v) { v.SelectCurrentLine(); });
            AddV("edit.deselect", E, Loc.T("Kijelölés megszüntetése"), "", delegate (TermView v) { v.ClearSelection(); });
            Add("edit.cliphistory", E, Loc.T("Vágólap-előzmény…"), "Ctrl+Shift+H", ShowClipHistory);
            Add("edit.history", E, Loc.T("Parancs-előzmények keresése…"), "Ctrl+R", ShowHistory); Add("edit.snippets", E, Loc.T("Snippetek…"), "Ctrl+Shift+S", ShowSnippets); Add("edit.snippet.save", E, Loc.T("Utolsó parancs mentése snippetként…"), "", SaveSnippetFromLast);
            // ---- keresés
            Add("find.open", S, Loc.T("Keresés a kimenetben…"), "Ctrl+F", ShowFind); Add("find.next", S, Loc.T("Következő találat"), "", delegate { if (FindOpen) FindStep(1); else ShowFind(); }); Add("find.prev", S, Loc.T("Előző találat"), "", delegate { if (FindOpen) FindStep(-1); }); Add("find.close", S, Loc.T("Keresősáv bezárása"), "", HideFind);
            // ---- nézet
            AddV("view.zoomin", V, Loc.T("Betű nagyítása"), "Ctrl+Plus", delegate (TermView v) { Setting(delegate { St.FontSize = Math.Min(48, St.FontSize + 1); }); }); AddV("view.zoomout", V, Loc.T("Betű kicsinyítése"), "Ctrl+Minus", delegate (TermView v) { Setting(delegate { St.FontSize = Math.Max(8, St.FontSize - 1); }); });
            AddV("view.zoomreset", V, Loc.T("Betűméret alaphelyzet"), "Ctrl+0", delegate (TermView v) { Setting(delegate { St.FontSize = 15; }); });
            foreach (string f in new string[] { "DejaVu Sans Mono", "Consolas", "Cascadia Mono", "Courier New", "Lucida Console", "Segoe UI Mono" }) { string ff = f; actions.Add(new TermAction { Id = "font." + f, Cat = V, Title = Loc.T("Betűtípus: ") + f, Checked = delegate { return St.Font == ff; }, Run = delegate { Setting(delegate { St.Font = ff; }); } }); }
            Add("view.ls.up", V, Loc.T("Sortávolság növelése"), "", delegate { Setting(delegate { St.LineFactor = Math.Min(2.0, St.LineFactor + 0.05); }); }); Add("view.ls.down", V, Loc.T("Sortávolság csökkentése"), "", delegate { Setting(delegate { St.LineFactor = Math.Max(1.0, St.LineFactor - 0.05); }); });
            Add("view.pad.up", V, Loc.T("Belső margó növelése"), "", delegate { Setting(delegate { St.PadX = Math.Min(40, St.PadX + 2); St.PadY = Math.Min(30, St.PadY + 2); }); }); Add("view.pad.down", V, Loc.T("Belső margó csökkentése"), "", delegate { Setting(delegate { St.PadX = Math.Max(0, St.PadX - 2); St.PadY = Math.Max(0, St.PadY - 2); }); });
            Add("view.op.down", V, Loc.T("Ablak átlátszóbb"), "", delegate { Setting(delegate { St.Opacity = Math.Max(0.35, St.Opacity - 0.05); }); }); Add("view.op.up", V, Loc.T("Ablak áttetszőbb → tömörebb"), "", delegate { Setting(delegate { St.Opacity = Math.Min(1.0, St.Opacity + 0.05); }); }); Add("view.op.reset", V, Loc.T("Ablak átlátszóság alaphelyzet"), "", delegate { Setting(delegate { St.Opacity = 1.0; }); });
            string[] cs = { Loc.T("Téglalap"), Loc.T("Aláhúzás"), Loc.T("Függőleges vonal") }; for (int i = 0; i < 3; i++) { int ci = i; actions.Add(new TermAction { Id = "cursor." + i, Cat = V, Title = Loc.T("Kurzor: ") + cs[i], Checked = delegate { return St.CursorStyle == ci; }, Run = delegate { Setting(delegate { St.CursorStyle = ci; }); } }); }
            AddT("cursor.blink", V, Loc.T("Kurzor villogása"), "", delegate { return St.CursorBlink; }, delegate (bool b) { St.CursorBlink = b; });
            AddT("view.secret", V, Loc.T("Titok-őr: API-kulcsok elmosása a kimenetben (egérrel fölé víve látszik)"), "", delegate { return St.SecretGuard; }, delegate (bool b) { St.SecretGuard = b; });
            AddT("view.dim", V, Loc.T("Nem aktív panelek halványítása"), "", delegate { return St.DimInactive; }, delegate (bool b) { St.DimInactive = b; });
            AddT("view.badges", V, Loc.T("Parancs-jelvények (idő, kilépési kód)"), "", delegate { return St.Badges; }, delegate (bool b) { St.Badges = b; });
            Add("view.fullscreen", V, Loc.T("Teljes méret / visszaállítás"), "F11", delegate { if (Win.ToggleMax != null) Win.ToggleMax(); }); actions[actions.Count - 1].Enabled = delegate { return Win.ToggleMax != null; };
            actions.Add(new TermAction { Id = "view.top", Cat = V, Title = Loc.T("Mindig felül"), Checked = delegate { return Win.IsTopmost != null && Win.IsTopmost(); }, Enabled = delegate { return Win.ToggleTopmost != null; }, Run = delegate { if (Win.ToggleTopmost != null) Win.ToggleTopmost(); } });
            // ---- témák
            foreach (TermTheme t in TermTheme.All) { string n = t.Name; actions.Add(new TermAction { Id = "theme." + n, Cat = TH, Title = Loc.T("Téma: ") + n, Checked = delegate { return St.Theme == n; }, Run = delegate { Setting(delegate { St.Theme = n; }); } }); }
            // ---- navigáció
            AddV("nav.top", N, Loc.T("Görgetés az elejére"), "Ctrl+Shift+Home", delegate (TermView v) { v.ScrollTop(); }); AddV("nav.bottom", N, Loc.T("Görgetés a végére"), "Ctrl+Shift+End", delegate (TermView v) { v.ScrollBottom(); });
            AddV("nav.pgup", N, Loc.T("Egy oldal fel"), "Shift+PageUp", delegate (TermView v) { v.ScrollPage(1); }); AddV("nav.pgdn", N, Loc.T("Egy oldal le"), "Shift+PageDown", delegate (TermView v) { v.ScrollPage(-1); });
            AddV("nav.lineup", N, Loc.T("Egy sor fel"), "", delegate (TermView v) { v.ScrollLines(1); }); AddV("nav.linedown", N, Loc.T("Egy sor le"), "", delegate (TermView v) { v.ScrollLines(-1); });
            AddV("nav.prompt.prev", N, Loc.T("Előző parancs (prompt) a görgetősávban"), "Ctrl+Up", delegate (TermView v) { v.JumpPrompt(-1); }); AddV("nav.prompt.next", N, Loc.T("Következő parancs (prompt)"), "Ctrl+Down", delegate (TermView v) { v.JumpPrompt(1); });
            // ---- parancs-blokkok
            AddV("block.copyout", B, Loc.T("Utolsó parancs kimenetének másolása"), "Ctrl+Shift+O", delegate (TermView v) { if (v.LastMark == null) Toast(Loc.T("Még nincs végrehajtott parancs")); else v.CopyLastOutput(); });
            AddV("block.selout", B, Loc.T("Utolsó parancs kimenetének kijelölése"), "", delegate (TermView v) { v.SelectLastOutput(); }); AddV("block.copycmd", B, Loc.T("Utolsó parancs szövegének másolása"), "Ctrl+Shift+Y", delegate (TermView v) { v.CopyLastCommand(); });
            AddV("block.runbook", B, Loc.T("Run book: az utolsó 10 parancs-blokk mentése HTML-be (asztalra)"), "", delegate (TermView v) { try { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "runbook-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html"); File.WriteAllText(f, v.BlocksHtml(10), new UTF8Encoding(false)); Toast(Loc.T("Mentve: ") + f); } catch (Exception ex) { Toast(Loc.T("Nem sikerült: ") + ex.Message); } });
            actions.Add(new TermAction { Id = "block.watch", Cat = B, Title = Loc.T("Figyelő-blokk: az utolsó parancs újrafut, ha a mappában fájl változik"), Checked = delegate { return CurrentView != null && CurrentView.Watching; }, Enabled = delegate { return CurrentView != null; }, Run = delegate { TermView v = CurrentView; if (v == null) return; bool on = v.ToggleWatch(); Toast(on ? Loc.T("👁 Figyelés BE: ") + Trim(v.LastCommand, 40) : Loc.T("Figyelés KI")); } });
            AddV("block.rerun", B, Loc.T("Utolsó parancs újrafuttatása"), "", delegate (TermView v) { if (v.LastMark == null) Toast(Loc.T("Még nincs végrehajtott parancs")); else v.RerunLast(); });
            // ---- export
            AddV("export.text", X, Loc.T("Teljes kimenet mentése szövegként…"), "", delegate (TermView v) { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "terminal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"); try { v.SaveText(f, true); Toast(Loc.T("Mentve: ") + f); } catch { } });
            AddV("export.html", X, Loc.T("Teljes kimenet mentése HTML-ként (színekkel)…"), "", delegate (TermView v) { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "terminal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html"); try { v.SaveHtml(f); Toast(Loc.T("Mentve: ") + f); } catch { } });
            AddV("export.visible", X, Loc.T("Látható képernyő mentése szövegként…"), "", delegate (TermView v) { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "terminal-kep-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"); try { v.SaveText(f, false); Toast(Loc.T("Mentve: ") + f); } catch { } });
            AddV("export.png", X, Loc.T("Képernyőkép a terminálról (PNG az asztalra)"), "", delegate (TermView v) { try { string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "terminal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"); SavePng(v, f); Toast(Loc.T("Mentve: ") + f); } catch { } });
            AddV("export.pngclip", X, Loc.T("Képernyőkép vágólapra"), "", delegate (TermView v) { try { RenderTargetBitmap b = Render(v); Clipboard.SetImage(b); Toast(Loc.T("Kép a vágólapon")); } catch { } });
            actions.Add(new TermAction { Id = "log.toggle", Cat = X, Title = Loc.T("Munkamenet-napló rögzítése (nyers kimenet fájlba)"), Checked = delegate { return CurrentView != null && CurrentView.Logging; }, Enabled = delegate { return CurrentView != null; }, Run = delegate { TermView v = CurrentView; if (v == null) return; if (v.Logging) { v.StopLog(); Toast(Loc.T("Napló leállítva")); } else { string d = Path.Combine(Cfg.Dir, "logs"); try { Directory.CreateDirectory(d); } catch { } string f = Path.Combine(d, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"); v.StartLog(f); Toast(Loc.T("Napló: ") + f); } } });
            Add("log.open", X, Loc.T("Naplók mappája"), "", delegate { string d = Path.Combine(Cfg.Dir, "logs"); try { Directory.CreateDirectory(d); } catch { } OpenFolder(d); });
            // ---- shell
            AddV("sh.clear", SH, Loc.T("Képernyő törlése"), "", delegate (TermView v) { v.ClearScreen(); }); AddV("sh.clearsb", SH, Loc.T("Görgetősáv törlése"), "", delegate (TermView v) { v.ClearScrollback(); }); AddV("sh.clearall", SH, Loc.T("Minden törlése (képernyő + görgetősáv)"), "Ctrl+Shift+K", delegate (TermView v) { v.ClearAll(); });
            AddV("sh.reset", SH, Loc.T("Terminál alaphelyzetbe (reset)"), "", delegate (TermView v) { v.ResetTerminal(); }); AddV("sh.ctrlc", SH, Loc.T("Ctrl+C küldése (megszakítás)"), "", delegate (TermView v) { v.Send("\x03"); }); AddV("sh.ctrld", SH, Loc.T("Ctrl+D küldése (EOF)"), "", delegate (TermView v) { v.Send("\x04"); });
            AddV("sh.ctrlz", SH, Loc.T("Ctrl+Z küldése"), "", delegate (TermView v) { v.Send("\x1a"); }); AddV("sh.esc", SH, Loc.T("Esc küldése"), "", delegate (TermView v) { v.Send("\x1b"); });
            Add("sh.restart", SH, Loc.T("Shell újraindítása ebben a panelben"), "", delegate { TermView v = CurrentView; if (v != null) v.StartShell(cur.Prof, v.Cwd); });
            Add("sh.admin", SH, Loc.T("Rendszergazdai PowerShell megnyitása (UAC)"), "", delegate { try { Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -Command \"Set-Location '" + (CurrentView != null && !string.IsNullOrEmpty(CurrentView.Cwd) && Directory.Exists(CurrentView.Cwd) ? CurrentView.Cwd.Replace("'", "''").Replace("\"", "") : "~") + "'\"") { Verb = "runas", UseShellExecute = true }); } catch { } });
            AddV("sh.cwdexplorer", SH, Loc.T("Munkamappa megnyitása az Intézőben"), "", delegate (TermView v) { if (!string.IsNullOrEmpty(v.Cwd)) OpenFolder(v.Cwd); });
            AddV("sh.cwdcode", SH, Loc.T("Munkamappa megnyitása szerkesztőben (VS Code / Antigravity)"), "", delegate (TermView v) { string ed = FindEditor(); if (ed != null && !string.IsNullOrEmpty(v.Cwd) && Directory.Exists(v.Cwd) && v.Cwd.IndexOfAny(new char[] { '%', '&', '^', '!', '"' }) < 0) { try { Process.Start(new ProcessStartInfo(ed, "\"" + v.Cwd + "\"") { UseShellExecute = false, CreateNoWindow = true }); } catch { } } else Toast(Loc.T("Nem található VS Code / Antigravity a PATH-on")); });
                // ---- előnézet
            string PV = Loc.T("Előnézet");
            Add("preview.open", PV, Loc.T("Előnézet megnyitása alul… (localhost cím / port)"), "Ctrl+Shift+J", delegate { OpenPreviewFor(CurrentView, ""); });
            AddW("preview.last", PV, Loc.T("Előnézet: utolsó észlelt dev-szerver cím"), "", delegate { TermView v = CurrentView; return v != null && !string.IsNullOrEmpty(v.LastDevUrl) ? (Action)delegate { OpenPreviewFor(v, v.LastDevUrl); } : null; });
            AddW("preview.close", PV, Loc.T("Előnézet bezárása"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { ClosePreview(cur); } : null; });
            AddW("preview.reload", PV, Loc.T("Előnézet újratöltése"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { cur.Preview.Reload(); } : null; });
            AddW("preview.desktop", PV, Loc.T("Előnézet: teljes szélesség"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { cur.Preview.SetMode(0); } : null; });
            AddW("preview.tablet", PV, Loc.T("Előnézet: tablet (820 px)"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { cur.Preview.SetMode(1); } : null; });
            AddW("preview.phone", PV, Loc.T("Előnézet: telefon (390 px)"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { cur.Preview.SetMode(2); } : null; });
            AddW("preview.devtools", PV, Loc.T("Előnézet: fejlesztői eszközök (DevTools)"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { cur.Preview.OpenDevTools(); } : null; });
            AddW("preview.external", PV, Loc.T("Előnézet megnyitása a böngészőben"), "", delegate { return cur != null && cur.Preview != null ? (Action)delegate { OpenExternal(cur.Preview.CurrentUrl); } : null; });
        // ---- megnyitás / fájlok
            Add("open.ini", O, Loc.T("Beállítások fájl (term.ini)"), "", delegate { OpenFile(Path.Combine(Cfg.Dir, "term.ini")); }); Add("open.keys", O, Loc.T("Gyorsbillentyű-felülírások (keys.ini)"), "", delegate { OpenFile(Path.Combine(Cfg.Dir, "keys.ini")); });
            Add("open.profile", O, Loc.T("PowerShell-integráció (lumen-profile.ps1)"), "", delegate { ShellProfiles.EnsureProfileScript(); OpenFile(Path.Combine(Cfg.Dir, "lumen-profile.ps1")); }); Add("open.user", O, Loc.T("Saját PowerShell-kiegészítések (lumen-user.ps1)"), "", delegate { OpenFile(Path.Combine(Cfg.Dir, "lumen-user.ps1")); });
            Add("open.snippets", O, Loc.T("Snippet-fájl (snippets.txt)"), "", delegate { Snippets(); OpenFile(SnippetFile); }); Add("open.history", O, Loc.T("PSReadLine előzmény-fájl"), "", delegate { OpenFile(HistoryFile); }); Add("open.folder", O, Loc.T("A Terminal mappája"), "", delegate { OpenFolder(Cfg.Dir); });
            // ---- beállítások (kapcsolók)
            AddT("set.autocopy", C, Loc.T("Kijelölés automatikus másolása"), "", delegate { return St.AutoCopy; }, delegate (bool b) { St.AutoCopy = b; }); AddT("set.middle", C, Loc.T("Középső gomb = beillesztés"), "", delegate { return St.MiddlePaste; }, delegate (bool b) { St.MiddlePaste = b; });
            AddT("set.pasteconfirm", C, Loc.T("Többsoros beillesztés jóváhagyása"), "", delegate { return St.PasteConfirm; }, delegate (bool b) { St.PasteConfirm = b; }); AddT("set.smartpaste", C, Loc.T("Okos beillesztés (idézőjelek, kötőjelek javítása)"), "", delegate { return St.SmartPaste; }, delegate (bool b) { St.SmartPaste = b; });
            AddT("set.linkctrl", C, Loc.T("Linkek megnyitása csak Ctrl+kattintással"), "", delegate { return St.LinkNeedsCtrl; }, delegate (bool b) { St.LinkNeedsCtrl = b; }); AddT("set.notify", C, Loc.T("Értesítés hosszú parancs végén"), "", delegate { return St.Notify; }, delegate (bool b) { St.Notify = b; });
            AddT("set.visualbell", C, Loc.T("Vizuális csengő (villanás)"), "", delegate { return St.VisualBell; }, delegate (bool b) { St.VisualBell = b; }); AddT("set.audiblebell", C, Loc.T("Hangos csengő"), "", delegate { return St.AudibleBell; }, delegate (bool b) { St.AudibleBell = b; });
            AddT("set.scrollout", C, Loc.T("Görgetés a végére új kimenetnél"), "", delegate { return St.ScrollOnOutput; }, delegate (bool b) { St.ScrollOnOutput = b; }); AddT("set.scrollkey", C, Loc.T("Görgetés a végére gépeléskor"), "", delegate { return St.ScrollOnKey; }, delegate (bool b) { St.ScrollOnKey = b; });
            AddT("set.restore", C, Loc.T("Lapok visszaállítása induláskor"), "", delegate { return St.RestoreSession; }, delegate (bool b) { St.RestoreSession = b; }); AddT("set.confirmclose", C, Loc.T("Lap bezárásának megerősítése, ha fut benne valami"), "", delegate { return St.ConfirmClose; }, delegate (bool b) { St.ConfirmClose = b; });
            foreach (int n in new int[] { 5000, 20000, 100000 }) { int sn = n; actions.Add(new TermAction { Id = "set.sb." + n, Cat = C, Title = Loc.T("Görgetősáv hossza: ") + n + Loc.T(" sor"), Checked = delegate { return St.Scrollback == sn; }, Run = delegate { Setting(delegate { St.Scrollback = sn; }); } }); }
            Add("set.wheel.up", C, Loc.T("Görgetési sebesség növelése"), "", delegate { Setting(delegate { St.WheelLines = Math.Min(15, St.WheelLines + 1); }); }); Add("set.wheel.down", C, Loc.T("Görgetési sebesség csökkentése"), "", delegate { Setting(delegate { St.WheelLines = Math.Max(1, St.WheelLines - 1); }); });
            Add("set.notify.sec", C, Loc.T("Értesítési küszöb: +5 mp"), "", delegate { Setting(delegate { St.NotifySec = Math.Min(300, St.NotifySec + 5); }); Toast(Loc.T("Értesítés ") + St.NotifySec + Loc.T(" mp felett")); });
            // ---- ablak
            Add("win.new", W, Loc.T("Új ablak"), "Ctrl+Shift+N", delegate { if (Win.NewWindow != null) Win.NewWindow(); }); actions[actions.Count - 1].Enabled = delegate { return Win.NewWindow != null; };
            Add("win.min", W, Loc.T("Ablak minimalizálása"), "", delegate { if (Win.Minimize != null) Win.Minimize(); }); actions[actions.Count - 1].Enabled = delegate { return Win.Minimize != null; };
            Add("win.close", W, Loc.T("Ablak bezárása"), "", delegate { if (Win.CloseWindow != null) Win.CloseWindow(); }); actions[actions.Count - 1].Enabled = delegate { return Win.CloseWindow != null; };
            Add("win.quake", W, Loc.T("Lenyíló (quake) mód váltása"), "", delegate { if (Win.Quake != null) Win.Quake(); }); actions[actions.Count - 1].Enabled = delegate { return Win.Quake != null; };
            // ---- súgó
            Add("help.keys", H, Loc.T("Gyorsbillentyűk és funkciók listája"), "Ctrl+Shift+F1", ShowCheatSheet); Add("help.lumen", H, Loc.T("PowerShell segédparancsok (lumen help)"), "", delegate { TermView v = CurrentView; if (v != null) v.Send("\x1blumen help\r"); });
            Add("help.palette", H, Loc.T("Parancspaletta"), "Ctrl+Shift+P", delegate { ShowPalette(""); });
        }

        // az utolsó (dinamikus) elem: snippetek és a paletta is látja őket
        public void ShowCheatSheet()
        {
            StringBuilder sb = new StringBuilder(); string cat = ""; List<TermAction> sorted = new List<TermAction>(actions);
            foreach (TermAction a in sorted) { if (a.Cat != cat) { cat = a.Cat; sb.Append("\n").Append(cat.ToUpperInvariant()).Append("\n"); } if (!string.IsNullOrEmpty(a.Key)) sb.Append("  ").Append((a.Key + "                         ").Substring(0, Math.Max(a.Key.Length, 22))).Append(a.Title).Append("\n"); }
            string path = Path.Combine(Cfg.Dir, "FEATURES-shortcuts.txt"); try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); OpenFile(path); } catch { }
        }

        // új lap megnyitása adott mappában, opcionálisan parancs futtatásával (a Dev Hub és más hívók használják)
        public void RunInNewTab(string title, string cwd, string cmd)
        {
            TermTab t = NewTab(ShellProfiles.Default(), !string.IsNullOrEmpty(cwd) && Directory.Exists(cwd) ? cwd : null); if (!string.IsNullOrEmpty(title)) { t.Custom = title; UpdateLabels(); }
            if (string.IsNullOrEmpty(cmd)) return; TermView v = t.Focus; int tries = 0; DispatcherTimer d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            d.Tick += delegate { if (++tries > 48 || v.Marks.Count > 0) { d.Stop(); v.RunCommand(cmd, true); } };      // megvárjuk az első promptot
            d.Start();
        }
        public int ActionCount { get { return actions.Count; } }
        public List<TermAction> AllActions { get { return actions; } }
        public void RunAction(string id) { foreach (TermAction a in actions) if (a.Id == id) { a.Run(); return; } }

        // ---- képernyőkép
        RenderTargetBitmap Render(FrameworkElement v) { v.UpdateLayout(); int w = Math.Max(1, (int)v.ActualWidth), h = Math.Max(1, (int)v.ActualHeight); DrawingVisual dv = new DrawingVisual(); using (DrawingContext dc = dv.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(St.CurTheme.Bg), null, new Rect(0, 0, w, h)); dc.DrawRectangle(new VisualBrush(v), null, new Rect(0, 0, w, h)); } RenderTargetBitmap b = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); b.Render(dv); return b; }
        void SavePng(FrameworkElement v, string path) { RenderTargetBitmap b = Render(v); PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b)); using (FileStream fs = File.Create(path)) enc.Save(fs); }

        // ---- bővítés: a snippetek a palettában is megjelennek
        public void Refresh() { }
    }
}
