// Dev Hub – Projektek (felismerés, git, futtatás, tisztítás) és Új projekt (sablonok).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassTerm;
using Forms = System.Windows.Forms;

namespace GlassControlWpf
{
    public partial class DevHub
    {
        class Proj
        {
            public string Path, Name, Pm = "", Branch = "", Remote = "", Alias = ""; public List<string> Kinds = new List<string>(), Tags = new List<string>(); public int Dirty, Ahead, Behind; public bool IsGit, Loaded, Loading, Missing; public HashSet<string> Tracked = new HashSet<string>();
            public List<string[]> Scripts = new List<string[]>(); public List<string> Commits = new List<string>(); public string Main = "";
            public List<Border> Items = new List<Border>(); public List<TextBlock> Subs = new List<TextBlock>(), Icons = new List<TextBlock>();      // a listában egy projekt több címke alatt is megjelenhet
        }
        // címke = virtuális mappa: csak a Dev Hubban rendszerez, a lemezen semmi nem változik (devhub\projtags.txt)
        class HubTag { public string Id, Name, Color; public bool Collapsed; }
        List<Proj> projects = new List<Proj>(); List<HubTag> tags = new List<HubTag>(); List<Proj> navOrder = new List<Proj>(); bool projLoaded, untagCollapsed; Proj curProj; StackPanel projList, projDetail; TextBox projFilter; bool scanning;
        string ProjFile { get { return System.IO.Path.Combine(dataDir, "projects.txt"); } }

        // ---------------------------------------------------------------- felépítés
        FrameworkElement BuildProjects()
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid left = new Grid(); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WrapPanel top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; projFilter = Field("", 262); FrameworkElement fh = Hinted(projFilter, "Keresés (név, címke, típus)…"); fh.Margin = new Thickness(0, 0, 0, 6); projFilter.TextChanged += delegate { RefreshProjList(); };
            projFilter.PreviewKeyDown += delegate (object o, KeyEventArgs e)      // ↑/↓: léptetés, Enter: VS Code, Esc: keresés törlése
            {
                if ((e.Key == Key.Down || e.Key == Key.Up) && navOrder.Count > 0) { int i = curProj != null ? navOrder.IndexOf(curProj) : -1; i = e.Key == Key.Down ? Math.Min(navOrder.Count - 1, i + 1) : Math.Max(0, i - 1); SelectProj(navOrder[i]); if (curProj.Items.Count > 0) curProj.Items[0].BringIntoView(); e.Handled = true; }
                else if (e.Key == Key.Enter && curProj != null && !curProj.Missing) { if (!OpenInEditor("code", curProj.Path)) Toast("A VS Code nem található"); e.Handled = true; }
                else if (e.Key == Key.Escape && projFilter.Text.Length > 0) { projFilter.Clear(); e.Handled = true; }
            };
            top.Children.Add(fh); top.Children.Add(Chip("+ Mappa", delegate { AddProjectFolder(); }, 1)); top.Children.Add(Chip("🔍 Keresés a gépen", delegate { ScanProjects(true); })); top.Children.Add(Chip("↻ Állapotok", delegate { foreach (Proj p in projects) { p.Loaded = false; } if (curProj != null) LoadProj(curProj); gitDirty = true; LoadAllGit(); }));
            top.Children.Add(Chip("🏷 Új címke", delegate { EditTag(null, null); }));
            Grid.SetRow(top, 0); left.Children.Add(top); projList = new StackPanel(); ScrollViewer sv = Scroll(projList); sv.AllowDrop = true;
            sv.DragOver += delegate (object o, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            sv.Drop += delegate (object o, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Handled = true; HandleDrop(e, null); } };      // mappák behúzása az Intézőből
            Grid.SetRow(sv, 1); left.Children.Add(sv); Grid.SetColumn(left, 0); g.Children.Add(left);
            projDetail = new StackPanel(); ScrollViewer dv = Scroll(projDetail); Grid.SetColumn(dv, 2); g.Children.Add(dv);
            EnsureProjects(); RefreshProjList();
            if (projects.Count == 0 && !File.Exists(ProjFile)) ScanProjects(false);
            Proj lp = FindProj(LoadStr("lastproj", "")); if (lp != null) SelectProj(lp); else if (navOrder.Count > 0) SelectProj(navOrder[0]); else ShowEmptyProj();
            LoadAllGit(); return g;
        }
        void ShowEmptyProj() { projDetail.Children.Clear(); projDetail.Children.Add(new TextBlock { Text = projects.Count > 0 ? "Válassz egy projektet a bal oldali listából." : "Még nincs projekt a listában.\n\nA „🔍 Keresés a gépen” megkeresi a mappákat (git / Node / Python / .NET / Rust / Go…), a „+ Mappa” gombbal te is felveheted őket, vagy húzd be a mappát az Intézőből.", Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 0, 0) }); }

        void EnsureProjects() { if (!projLoaded) LoadProjectList(); }
        void LoadProjectList()
        {
            projLoaded = true; projects.Clear(); try { if (File.Exists(ProjFile)) foreach (string l in File.ReadAllLines(ProjFile)) { string p = l.Trim(); if (p.Length > 0 && FindProj(p) == null) projects.Add(MakeProj(p)); } } catch { }      // az elérhetetlen mappák is maradnak (pl. lecsatolt meghajtó): „hiányzik” jelzést kapnak, nem tűnnek el végleg
            LoadMeta();
        }
        static Proj MakeProj(string p) { return new Proj { Path = p, Name = System.IO.Path.GetFileName(p.TrimEnd('\\')).Length > 0 ? System.IO.Path.GetFileName(p.TrimEnd('\\')) : p }; }
        void SaveProjectList() { try { List<string> l = new List<string>(); foreach (Proj p in projects) l.Add(p.Path); File.WriteAllLines(ProjFile, l.ToArray(), new UTF8Encoding(false)); } catch { } }

        void AddProjectFolder()
        {
            using (Forms.FolderBrowserDialog dlg = new Forms.FolderBrowserDialog { Description = "Válaszd ki a projekt mappáját" })
            { if (dlg.ShowDialog() != Forms.DialogResult.OK) return; AddProject(dlg.SelectedPath, true); }
        }
        Proj AddProject(string path, bool select)
        {
            EnsureProjects();
            Proj ex = projects.Find(delegate (Proj x) { return string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase); });
            if (ex == null) { ex = MakeProj(path); projects.Add(ex); projects.Sort(delegate (Proj a, Proj b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); }); SaveProjectList(); RefreshProjList(); }
            if (select) SelectProj(ex); return ex;
        }

        // ---------------------------------------------------------------- keresés a gépen
        static readonly string[] SkipDirs = { "node_modules", ".git", "bin", "obj", "venv", ".venv", "windows", "program files", "program files (x86)", "$recycle.bin", "system volume information", "appdata", "programdata", "dist", "build", "target", "__pycache__", ".next", ".cache", "packages" };
        static bool IsProjectDir(string dir)
        {
            try
            {
                if (Directory.Exists(System.IO.Path.Combine(dir, ".git"))) return true;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string n = System.IO.Path.GetFileName(f).ToLowerInvariant(), e = System.IO.Path.GetExtension(n);
                    if (n == "package.json" || n == "pyproject.toml" || n == "requirements.txt" || n == "cargo.toml" || n == "go.mod" || n == "pom.xml" || n == "build.gradle" || n == "build.gradle.kts" || n == "composer.json" || e == ".sln" || e == ".csproj") return true;
                }
            }
            catch { }
            return false;
        }
        void ScanProjects(bool announce)
        {
            if (scanning) return; EnsureProjects(); scanning = true; if (announce) Toast("Projektek keresése…");
            Bg(delegate
            {
                List<string> found = new List<string>(); string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); List<string> roots = new List<string>();
                roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                foreach (string s in new string[] { @"source\repos", "Projects", "projects", "dev", "code", "repos", "Source", "workspace", "git", "GitHub" }) roots.Add(System.IO.Path.Combine(home, s));
                foreach (DriveInfo di in DriveInfo.GetDrives()) { try { if (di.DriveType == DriveType.Fixed && di.IsReady) roots.Add(di.RootDirectory.FullName); } catch { } }
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string r in roots) { if (!Directory.Exists(r)) continue; ScanDir(r, 0, found, seen); if (found.Count > 250) break; }
                return found;
            }, delegate (List<string> found)
            {
                scanning = false; int n = 0; if (found != null) foreach (string f in found) { if (!projects.Exists(delegate (Proj x) { return string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase); })) { projects.Add(MakeProj(f)); n++; } }
                projects.Sort(delegate (Proj a, Proj b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); }); SaveProjectList(); RefreshProjList(); LoadAllGit();
                if (announce || n > 0) Toast(n + " új projekt a listában (" + projects.Count + " összesen)"); if (curProj == null && projects.Count > 0) SelectProj(projects[0]);
            });
        }
        static void ScanDir(string dir, int depth, List<string> found, HashSet<string> seen)
        {
            if (depth > 2 || found.Count > 250) return;
            string[] subs; try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (string s in subs)
            {
                string name = System.IO.Path.GetFileName(s).ToLowerInvariant(); if (Array.IndexOf(SkipDirs, name) >= 0 || name.StartsWith(".")) continue;
                try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                if (IsProjectDir(s)) { if (seen.Add(s)) found.Add(s); continue; }
                ScanDir(s, depth + 1, found, seen);
            }
        }

        // ---------------------------------------------------------------- lista (címkék = virtuális mappák szerint csoportosítva)
        static string KindIcon(Proj p)
        {
            if (p.Missing) return "⚠"; if (p.Kinds.Contains("Next.js")) return "▲"; if (p.Kinds.Contains("React")) return "⚛"; if (p.Kinds.Contains("Node")) return "⬢"; if (p.Kinds.Contains("Python")) return "🐍"; if (p.Kinds.Contains(".NET")) return "🟣"; if (p.Kinds.Contains("Rust")) return "🦀"; if (p.Kinds.Contains("Go")) return "🐹"; if (p.Kinds.Contains("Java")) return "☕"; if (p.IsGit) return "🌿"; return "📁";
        }
        static string DName(Proj p) { return p.Alias.Length > 0 ? p.Alias : p.Name; }
        static string ParentName(string path) { try { return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path.TrimEnd('\\')) ?? "") ?? ""; } catch { return ""; } }
        static readonly Brush SelBg = Br(255, 255, 255, 36);
        Proj FindProj(string path) { return projects.Find(delegate (Proj x) { return string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase); }); }
        HubTag FindTag(string id) { return tags.Find(delegate (HubTag t) { return t.Id == id; }); }
        bool HasAnyTag(Proj p) { foreach (string id in p.Tags) if (FindTag(id) != null) return true; return false; }
        bool Matches(Proj p, string q)
        {
            if (q.Length == 0 || DName(p).ToLowerInvariant().Contains(q) || p.Path.ToLowerInvariant().Contains(q)) return true;
            foreach (string k in p.Kinds) if (k.ToLowerInvariant().Contains(q)) return true;
            foreach (string id in p.Tags) { HubTag t = FindTag(id); if (t != null && t.Name.ToLowerInvariant().Contains(q)) return true; }
            return false;
        }
        void RefreshProjList()
        {
            if (projList == null) return; string q = projFilter != null ? projFilter.Text.Trim().ToLowerInvariant() : ""; projList.Children.Clear(); navOrder.Clear();
            Dictionary<string, int> names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); int missing = 0;
            foreach (Proj p in projects) { p.Items.Clear(); p.Subs.Clear(); p.Icons.Clear(); int c; names.TryGetValue(DName(p), out c); names[DName(p)] = c + 1; if (p.Missing) missing++; }
            List<Proj> vis = projects.FindAll(delegate (Proj p) { return Matches(p, q); }); vis.Sort(delegate (Proj a, Proj b) { return string.Compare(DName(a), DName(b), StringComparison.OrdinalIgnoreCase); });
            if (missing > 0) projList.Children.Add(Chip("⚠ " + missing + " mappa nem található – eltávolítás…", delegate { RemoveMissing(); }, 2));
            if (tags.Count == 0) foreach (Proj p in vis) AddProjItem(p, null, names);
            else
            {
                foreach (HubTag t in tags)
                {
                    HubTag tt = t; List<Proj> m = vis.FindAll(delegate (Proj p) { return p.Tags.Contains(tt.Id); }); if (q.Length > 0 && m.Count == 0) continue;
                    bool open = !t.Collapsed || q.Length > 0; projList.Children.Add(TagHeader(t, m.Count, open)); if (open) foreach (Proj p in m) AddProjItem(p, t, names);
                }
                List<Proj> rest = vis.FindAll(delegate (Proj p) { return !HasAnyTag(p); });
                if (rest.Count > 0) { bool open = !untagCollapsed || q.Length > 0; projList.Children.Add(TagHeader(null, rest.Count, open)); if (open) foreach (Proj p in rest) AddProjItem(p, null, names); }
            }
            if (vis.Count == 0) projList.Children.Add(new TextBlock { Text = projects.Count == 0 ? "A lista üres – húzz ide egy mappát" : "Nincs találat", Foreground = Muted, Margin = new Thickness(8, 6, 0, 0) });
        }
        void AddProjItem(Proj p, HubTag group, Dictionary<string, int> names)
        {
            Proj pp = p; if (!navOrder.Contains(p)) navOrder.Add(p);
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock ic = Txt(KindIcon(p), 16, Brushes.White, false); ic.HorizontalAlignment = HorizontalAlignment.Left; p.Icons.Add(ic); StackPanel sp = new StackPanel(); Grid.SetColumn(sp, 1);
            TextBlock nm = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis }; nm.Inlines.Add(new System.Windows.Documents.Run(DName(p)));
            int dup; if (names.TryGetValue(DName(p), out dup) && dup > 1) nm.Inlines.Add(new System.Windows.Documents.Run("  ‹" + ParentName(p.Path) + "›") { FontSize = 11, FontWeight = FontWeights.Normal, Foreground = Muted });      // azonos nevű projektek megkülönböztetése
            sp.Children.Add(nm); TextBlock sub = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis }; sp.Children.Add(sub); p.Subs.Add(sub);
            StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(dots, 2);
            foreach (string id in p.Tags) { HubTag t = FindTag(id); if (t != null && t != group) dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = HexBr(t.Color, 255), Margin = new Thickness(4, 0, 0, 0), ToolTip = t.Name }); }
            g.Children.Add(ic); g.Children.Add(sp); g.Children.Add(dots);
            Border it = new Border { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(tags.Count > 0 ? 10 : 0, 0, 0, 0), CornerRadius = new CornerRadius(9), Child = g, Cursor = Cursors.Hand, Background = p == curProj ? SelBg : Brushes.Transparent, ToolTip = p.Path }; p.Items.Add(it); SetProjSub(p);
            it.MouseEnter += delegate { if (pp != curProj) it.Background = Faint; }; it.MouseLeave += delegate { if (pp != curProj) it.Background = Brushes.Transparent; };
            // kattintás: kiválasztás · húzás egy címke fejlécére: áthelyezés (Ctrl: hozzáadás, az eredeti helyén is marad)
            Point down = new Point(); bool pressed = false; string from = group != null ? group.Id : "";
            it.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { down = e.GetPosition(projList); pressed = true; };
            it.MouseLeftButtonUp += delegate { if (pressed) { pressed = false; SelectProj(pp); } };
            it.MouseMove += delegate (object o, MouseEventArgs e)
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed || tags.Count == 0) return; Point pt = e.GetPosition(projList);
                if (Math.Abs(pt.X - down.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pt.Y - down.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressed = false; try { DragDrop.DoDragDrop(it, new DataObject(DragFmt, pp.Path + "\n" + from), DragDropEffects.Copy | DragDropEffects.Move); } catch { }
            };
            GlassMenu.Attach(it, delegate { return ProjMenu(pp, group); });
            projList.Children.Add(it);
        }
        MenuEntry[] ProjMenu(Proj p, HubTag group)
        {
            List<MenuEntry> m = new List<MenuEntry>();
            if (!p.Missing) { m.Add(GlassMenu.Item("Megnyitás VS Code-ban", delegate { if (!OpenInEditor("code", p.Path)) Toast("A VS Code nem található"); })); m.Add(GlassMenu.Item("Megnyitás Antigravityben", delegate { if (!OpenInEditor("antigravity", p.Path)) Toast("Az Antigravity nem található"); })); m.Add(GlassMenu.Item("Terminál itt", delegate { Term(DName(p), p.Path, null); })); m.Add(GlassMenu.Item("Megnyitás az Intézőben", delegate { OpenFolder(p.Path); })); m.Add(GlassMenu.Sep()); }
            m.Add(GlassMenu.Menu("🏷 Címkék", TagMenu(p))); if (group != null) m.Add(GlassMenu.Item("Kivétel innen: " + group.Name, delegate { ToggleTag(p, group); }));
            m.Add(GlassMenu.Item("✎ Átnevezés (csak itt)…", delegate { RenameProj(p); })); if (p.Alias.Length > 0) m.Add(GlassMenu.Item("Eredeti név visszaállítása (" + p.Name + ")", delegate { p.Alias = ""; ProjChanged(p); }));
            m.Add(GlassMenu.Sep()); m.Add(GlassMenu.Item("Útvonal másolása", delegate { Copy(p.Path, "Útvonal másolva"); })); if (p.Missing) m.Add(GlassMenu.Item("📂 Új hely kiválasztása…", delegate { RelocateProj(p); }));
            m.Add(GlassMenu.Item("Eltávolítás a listából", delegate { RemoveProj(p); }));
            return m.ToArray();
        }
        void SetProjSub(Proj p)
        {
            string s = p.Kinds.Count > 0 ? string.Join(" · ", p.Kinds.ToArray()) : p.Path; Brush fg = p.Dirty > 0 ? Orange : Muted;
            if (p.Missing) { s = "a mappa nem található"; fg = Orange; }
            else if (p.Loaded && p.IsGit && p.Branch.Length > 0) s = "⎇ " + p.Branch + (p.Dirty > 0 ? "  ✎" + p.Dirty : "") + (p.Ahead > 0 ? "  ↑" + p.Ahead : "") + (p.Behind > 0 ? "  ↓" + p.Behind : "") + (p.Kinds.Count > 0 ? "  ·  " + p.Kinds[0] : "");
            foreach (TextBlock t in p.Subs) { t.Text = s; t.Foreground = fg; } foreach (TextBlock t in p.Icons) t.Text = KindIcon(p);
        }

        void SelectProj(Proj p)
        {
            curProj = p; SaveStr("lastproj", p.Path); foreach (Proj x in projects) foreach (Border b in x.Items) b.Background = x == p ? SelBg : Brushes.Transparent;
            ShowDetail(p); if (!p.Loaded) LoadProj(p);
        }
        void ProjChanged(Proj p) { SaveMeta(); RefreshProjList(); if (p == curProj) ShowDetail(p); }
        void RemoveProj(Proj p) { int i = navOrder.IndexOf(p); projects.Remove(p); AfterRemove(p == curProj, i); Toast(DName(p) + " kikerült a listából (a mappa nem változott)"); }
        void RemoveMissing()
        {
            List<Proj> miss = projects.FindAll(delegate (Proj p) { return p.Missing; }); if (miss.Count == 0) return; StringBuilder sb = new StringBuilder(); for (int i = 0; i < miss.Count && i < 12; i++) sb.Append("• " + DName(miss[i]) + "  –  " + miss[i].Path + "\n"); if (miss.Count > 12) sb.Append("… és még " + (miss.Count - 12) + "\n");
            Confirm("Hiányzó mappák eltávolítása", "Ezek a mappák nem találhatók (törölve, áthelyezve vagy lecsatolt meghajtón):\n\n" + sb + "\nCsak a listából kerülnek ki, a címkéikkel együtt. Ha csak áthelyezted, inkább jobb klikk → „Új hely kiválasztása…”.", "Eltávolít", delegate { bool cg = miss.Contains(curProj); foreach (Proj p in miss) projects.Remove(p); AfterRemove(cg, 0); });
        }
        void AfterRemove(bool curGone, int idx) { SaveProjectList(); SaveMeta(); RefreshProjList(); if (curGone) { curProj = null; if (navOrder.Count > 0) SelectProj(navOrder[Math.Max(0, Math.Min(idx, navOrder.Count - 1))]); else ShowEmptyProj(); } }
        void RelocateProj(Proj p)
        {
            using (Forms.FolderBrowserDialog dlg = new Forms.FolderBrowserDialog { Description = "Hol van most a(z) „" + DName(p) + "” mappája?" })
            {
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return; Proj ex = FindProj(dlg.SelectedPath); if (ex != null && ex != p) { Toast("Ez a mappa már a listában van: " + DName(ex)); return; }
                p.Path = dlg.SelectedPath; p.Name = MakeProj(p.Path).Name; p.Missing = false; p.Loaded = false; SaveProjectList(); SaveMeta(); RefreshProjList(); SelectProj(p);
            }
        }
        // új projekt sablonból: a mappa csak a terminálban futó parancs után jön létre – addig figyeljük (legfeljebb 5 percig)
        void WatchCreated(Proj p)
        {
            int n = 0; System.Windows.Threading.DispatcherTimer t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += delegate { n++; bool ok = Directory.Exists(p.Path) && IsProjectDir(p.Path); if (ok || n > 100 || !projects.Contains(p)) { t.Stop(); if (ok) { p.Loaded = false; LoadProj(p); } } }; t.Start();
        }

        // git-állapot az összes projektre (egymás után, kis terheléssel); egy projektet egyszerre csak egy szál vizsgál
        bool gitLoading, gitDirty;
        static bool Claim(Proj p) { lock (p) { if (p.Loaded || p.Loading) return false; p.Loading = true; return true; } }
        void LoadAllGit()
        {
            if (gitLoading) { gitDirty = true; return; } gitLoading = true; gitDirty = false; List<Proj> list = new List<Proj>(projects);
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (Proj p in list) { if (!Claim(p)) continue; Proj res = null; try { res = Inspect(p); } catch { } Proj pp = p, rr = res; Dispatcher.BeginInvoke(new Action(delegate { Finish(pp, rr); })); Thread.Sleep(40); }
                Dispatcher.BeginInvoke(new Action(delegate { gitLoading = false; if (gitDirty) LoadAllGit(); }));
            });
        }
        void LoadProj(Proj p) { if (!Claim(p)) return; Bg(delegate { return Inspect(p); }, delegate (Proj r) { Finish(p, r); }); }
        void Finish(Proj p, Proj r)
        {
            lock (p) { p.Loading = false; p.Loaded = true; }
            if (r != null && !string.Equals(r.Path, p.Path, StringComparison.OrdinalIgnoreCase)) { lock (p) { p.Loaded = false; } LoadProj(p); return; }      // közben áthelyezték: újra
            bool wasMissing = p.Missing; if (r != null) ApplyInspect(p, r); SetProjSub(p); if (wasMissing != p.Missing) RefreshProjList(); if (p == curProj) ShowDetail(p);
        }
        void ApplyInspect(Proj p, Proj r) { p.Kinds = r.Kinds; p.Scripts = r.Scripts; p.Pm = r.Pm; p.Main = r.Main; p.IsGit = r.IsGit; p.Branch = r.Branch; p.Remote = r.Remote; p.Dirty = r.Dirty; p.Ahead = r.Ahead; p.Behind = r.Behind; p.Tracked = r.Tracked; p.Commits = r.Commits; p.Missing = r.Missing; }

        // ---------------------------------------------------------------- címkék (virtuális mappák)
        const string DragFmt = "LumenDevHubProject";
        static readonly string[] TagColors = { "#0A84FF", "#30D158", "#FF9F0A", "#FF453A", "#BF5AF2", "#FF375F", "#64D2FF", "#FFD60A", "#5E5CE6", "#66D4CF", "#AC8E68", "#8E8E93" };
        string MetaFile { get { return System.IO.Path.Combine(dataDir, "projtags.txt"); } }
        static Color HexColor(string h) { try { return (Color)ColorConverter.ConvertFromString(h); } catch { return Color.FromRgb(10, 132, 255); } }
        static Brush HexBr(string h, byte a) { Color c = HexColor(h); return Br(c.R, c.G, c.B, a); }
        static Brush HexFg(string h) { Color c = HexColor(h); return Br((byte)(c.R + (255 - c.R) * 0.4), (byte)(c.G + (255 - c.G) * 0.4), (byte)(c.B + (255 - c.B) * 0.4), 255); }      // sötét háttéren is olvasható árnyalat
        static string OneLine(string s) { return Regex.Replace(s ?? "", @"[\t\r\n]+", " ").Trim(); }

        // formátum (tabulátorral tagolt): tag <id> <név> <#szín> <összecsukva 0/1>  ·  proj <útvonal> <címke-id,…> <megjelenített név>
        void LoadMeta()
        {
            tags.Clear(); Dictionary<string, string[]> pm = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(MetaFile)) foreach (string l in File.ReadAllLines(MetaFile)) { string[] c = l.Split('\t'); if (c.Length >= 4 && c[0] == "tag" && c[1].Length > 0 && c[2].Trim().Length > 0 && FindTag(c[1]) == null) tags.Add(new HubTag { Id = c[1], Name = c[2].Trim(), Color = c[3], Collapsed = c.Length > 4 && c[4] == "1" }); else if (c.Length >= 3 && c[0] == "proj") pm[c[1]] = c; } } catch { }
            foreach (Proj p in projects) { string[] c; if (!pm.TryGetValue(p.Path, out c)) continue; p.Tags = new List<string>(); foreach (string id in c[2].Split(',')) if (id.Length > 0 && FindTag(id) != null && !p.Tags.Contains(id)) p.Tags.Add(id); p.Alias = c.Length > 3 ? c[3].Trim() : ""; }
            untagCollapsed = LoadStr("untagcol", "0") == "1";
        }
        void SaveMeta()
        {
            try
            {
                List<string> l = new List<string>(); l.Add("# Lumen Dev Hub – címkék és megjelenített nevek (csak a Dev Hubban látszanak, a mappákat nem érintik)");
                foreach (HubTag t in tags) l.Add("tag\t" + t.Id + "\t" + OneLine(t.Name) + "\t" + t.Color + "\t" + (t.Collapsed ? "1" : "0"));
                foreach (Proj p in projects) { List<string> ids = p.Tags.FindAll(delegate (string id) { return FindTag(id) != null; }); if (ids.Count == 0 && p.Alias.Length == 0) continue; l.Add("proj\t" + p.Path + "\t" + string.Join(",", ids.ToArray()) + "\t" + OneLine(p.Alias)); }
                string tmp = MetaFile + ".tmp"; File.WriteAllLines(tmp, l.ToArray(), new UTF8Encoding(false)); if (File.Exists(MetaFile)) File.Replace(tmp, MetaFile, null); else File.Move(tmp, MetaFile);      // félbeszakadt írás ne rontsa el a fájlt
            }
            catch (Exception ex) { Toast("A címkék mentése nem sikerült: " + ex.Message); }
        }

        Border TagHeader(HubTag t, int count, bool open)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(19) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock ch = Txt(open ? "▾" : "▸", 12, Muted, false);
            Border sw = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = t != null ? HexBr(t.Color, 255) : Brushes.Transparent, BorderBrush = Muted, BorderThickness = new Thickness(t != null ? 0 : 1.2) }; Grid.SetColumn(sw, 1);
            TextBlock nm = Txt(t != null ? t.Name : "Címke nélkül", 12.5, t != null ? HexFg(t.Color) : Muted, true); nm.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(nm, 2);
            TextBlock cn = Txt(count.ToString(), 11, Muted, false); Grid.SetColumn(cn, 3); g.Children.Add(ch); g.Children.Add(sw); g.Children.Add(nm); g.Children.Add(cn);
            Border h = new Border { Padding = new Thickness(6, 6, 8, 5), Margin = new Thickness(0, 4, 0, 1), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g, AllowDrop = true, ToolTip = t != null ? "Kattints: kinyitás / becsukás · jobb klikk: szerkesztés\nHúzz ide projektet (Ctrl: az eredeti címkéjében is marad)" : "Húzz ide projektet: kikerül a címkéjéből" };
            h.MouseEnter += delegate { h.Background = Faint; }; h.MouseLeave += delegate { h.Background = Brushes.Transparent; };
            h.MouseLeftButtonUp += delegate { if (t != null) { t.Collapsed = !t.Collapsed; SaveMeta(); } else { untagCollapsed = !untagCollapsed; SaveStr("untagcol", untagCollapsed ? "1" : "0"); } RefreshProjList(); };
            h.DragOver += delegate (object o, DragEventArgs e) { e.Effects = DropEffect(e, t); e.Handled = true; h.Background = e.Effects == DragDropEffects.None ? Brushes.Transparent : t != null ? HexBr(t.Color, 60) : Br(255, 255, 255, 34); };
            h.DragLeave += delegate { h.Background = Brushes.Transparent; };
            h.Drop += delegate (object o, DragEventArgs e) { h.Background = Brushes.Transparent; e.Handled = true; HandleDrop(e, t); };
            GlassMenu.Attach(h, delegate
            {
                if (t == null) return new MenuEntry[] { GlassMenu.Item("🏷 Új címke…", delegate { EditTag(null, null); }) };
                int i = tags.IndexOf(t);
                return new MenuEntry[] { GlassMenu.Item("✎ Szerkesztés (név, szín, projektek)…", delegate { EditTag(t, null); }), GlassMenu.Sep(), i > 0 ? GlassMenu.Item("↑ Feljebb", delegate { MoveTag(t, -1); }) : GlassMenu.Off("↑ Feljebb"), i < tags.Count - 1 ? GlassMenu.Item("↓ Lejjebb", delegate { MoveTag(t, 1); }) : GlassMenu.Off("↓ Lejjebb"), GlassMenu.Sep(), GlassMenu.Item("🗑 Címke törlése…", delegate { DeleteTag(t); }) };
            });
            return h;
        }
        static DragDropEffects DropEffect(DragEventArgs e, HubTag t)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.Copy; if (!e.Data.GetDataPresent(DragFmt)) return DragDropEffects.None;
            string[] d = ((e.Data.GetData(DragFmt) as string) ?? "").Split('\n'); string from = d.Length > 1 ? d[1] : "";
            if (t == null) return from.Length > 0 ? DragDropEffects.Move : DragDropEffects.None; if (from == t.Id) return DragDropEffects.None;
            return (e.KeyStates & DragDropKeyStates.ControlKey) != 0 || from.Length == 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        // t == null: „Címke nélkül” csoport / üres lista-terület
        void HandleDrop(DragEventArgs e, HubTag t)
        {
            DragDropEffects fx = DropEffect(e, t); e.Effects = fx; if (fx == DragDropEffects.None) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] fs = e.Data.GetData(DataFormats.FileDrop) as string[]; List<string> dirs = new List<string>(); if (fs != null) foreach (string f in fs) if (Directory.Exists(f)) dirs.Add(f);
                if (dirs.Count == 0) { Toast("Mappát húzz ide (fájlt nem)"); return; }
                Dispatch(delegate { Proj last = null; foreach (string f in dirs) { last = AddProject(f, false); if (t != null && !last.Tags.Contains(t.Id)) last.Tags.Add(t.Id); } SaveMeta(); RefreshProjList(); LoadAllGit(); SelectProj(last); Toast((dirs.Count == 1 ? "Hozzáadva: " + DName(last) : dirs.Count + " mappa hozzáadva") + (t != null ? " → " + t.Name : "")); });
                return;
            }
            string[] d = ((e.Data.GetData(DragFmt) as string) ?? "").Split('\n'); Proj p = FindProj(d[0]); string from = d.Length > 1 ? d[1] : ""; if (p == null) return;
            Dispatch(delegate      // a húzás forráseleme ilyenkor még él: a listát csak utána építjük újra
            {
                if (t != null && !p.Tags.Contains(t.Id)) p.Tags.Add(t.Id); if (fx == DragDropEffects.Move && from.Length > 0) p.Tags.Remove(from);
                ProjChanged(p); Toast(t == null ? DName(p) + " kikerült a címkéből" : DName(p) + " → " + t.Name + (fx == DragDropEffects.Copy && from.Length > 0 ? " (az eredeti helyén is maradt)" : ""));
            });
        }
        MenuEntry[] TagMenu(Proj p)
        {
            List<MenuEntry> m = new List<MenuEntry>(); foreach (HubTag t in tags) { HubTag tt = t; m.Add(GlassMenu.Check(t.Name, p.Tags.Contains(t.Id), delegate { ToggleTag(p, tt); })); }
            if (m.Count > 0) m.Add(GlassMenu.Sep()); m.Add(GlassMenu.Item("🏷 Új címke…", delegate { EditTag(null, p); })); return m.ToArray();
        }
        void ToggleTag(Proj p, HubTag t) { if (!p.Tags.Remove(t.Id)) p.Tags.Add(t.Id); ProjChanged(p); }
        void RenameProj(Proj p) { Ask("Megjelenített név – csak a Dev Hubban (üresen: " + p.Name + ")", DName(p), "Mentés", delegate (string n) { n = OneLine(n); p.Alias = n == p.Name ? "" : n; ProjChanged(p); }); }
        void MoveTag(HubTag t, int dir) { int i = tags.IndexOf(t), j = i + dir; if (i < 0 || j < 0 || j >= tags.Count) return; tags[i] = tags[j]; tags[j] = t; SaveMeta(); RefreshProjList(); }
        void DeleteTag(HubTag t)
        {
            int n = projects.FindAll(delegate (Proj p) { return p.Tags.Contains(t.Id); }).Count;
            Confirm("Címke törlése: " + t.Name, "A címke megszűnik" + (n > 0 ? "; a benne lévő " + n + " projekt a többi címkéjében, vagy a „Címke nélkül” csoportban marad" : "") + ".\nA projektek és a mappák nem változnak.", "Töröl", delegate { tags.Remove(t); foreach (Proj p in projects) p.Tags.Remove(t.Id); SaveMeta(); RefreshProjList(); if (curProj != null) ShowDetail(curProj); });
        }
        Border TagPill(Proj p, HubTag t)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal }; TextBlock x = Txt("  ✕", 10.5, Muted, true); x.Cursor = Cursors.Hand; x.ToolTip = "Kivétel ebből a címkéből";
            x.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ToggleTag(p, t); }; sp.Children.Add(Txt(t.Name, 12, HexFg(t.Color), true)); sp.Children.Add(x);
            Border b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 9, 4), Margin = new Thickness(0, 0, 6, 6), Background = HexBr(t.Color, 50), BorderBrush = HexBr(t.Color, 120), BorderThickness = new Thickness(1), Child = sp, Cursor = Cursors.Hand, ToolTip = "Kattints: a címke szerkesztése" };
            b.MouseLeftButtonUp += delegate { EditTag(t, null); }; return b;
        }

        // címke létrehozása / szerkesztése: név, szín (minta vagy #RRGGBB) és a benne lévő projektek egy helyen
        void EditTag(HubTag t, Proj with)
        {
            bool isNew = t == null; string color = isNew ? TagColors[tags.Count % TagColors.Length] : t.Color;
            StackPanel sp = new StackPanel(); sp.Children.Add(Txt(isNew ? "🏷 Új címke" : "🏷 Címke szerkesztése", 15, Brushes.White, true));
            sp.Children.Add(new TextBlock { Text = "Csak itt, a Dev Hubban rendszerez – a mappák a lemezen nem mozdulnak.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 10) });
            TextBox name = Field(isNew ? "" : t.Name, 0); name.FontSize = 14; name.MaxLength = 40; sp.Children.Add(Hinted(name, "Név, pl. backendek"));
            sp.Children.Add(Head("Szín")); WrapPanel sw = new WrapPanel(); List<Border> sws = new List<Border>(); TextBox hex = Field(color, 90); hex.FontFamily = Mono; hex.FontSize = 12; hex.MaxLength = 7; hex.VerticalAlignment = VerticalAlignment.Top; hex.ToolTip = "Saját szín: #RRGGBB";
            Action mark = delegate { foreach (Border b in sws) b.BorderBrush = string.Equals((string)b.Tag, color, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent; };
            foreach (string c in TagColors) { string cc = c; Border b = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 7), Background = HexBr(c, 255), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = c }; b.MouseLeftButtonUp += delegate { hex.Text = cc; }; sws.Add(b); sw.Children.Add(b); }
            hex.TextChanged += delegate { string v = hex.Text.Trim(); if (!v.StartsWith("#")) v = "#" + v; bool ok = Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$"); hex.BorderBrush = ok ? CardBorder : Red; if (ok) { color = v.ToUpperInvariant(); mark(); } };
            sw.Children.Add(hex); sp.Children.Add(sw); mark();
            sp.Children.Add(Head("Projektek ebben a címkében")); TextBox pf = Field("", 0); FrameworkElement pfh = Hinted(pf, "Szűrés…"); pfh.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(pfh);
            StackPanel pl = new StackPanel(); List<KeyValuePair<Proj, CheckBox>> checks = new List<KeyValuePair<Proj, CheckBox>>(); List<Proj> all = new List<Proj>(projects); all.Sort(delegate (Proj a, Proj b) { return string.Compare(DName(a), DName(b), StringComparison.OrdinalIgnoreCase); });
            foreach (Proj p in all)
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Txt(KindIcon(p) + "  " + DName(p), 12.5, Brushes.White, false)); row.Children.Add(Txt("   " + p.Path, 11, Muted, false));
                CheckBox cb = new CheckBox { Content = row, IsChecked = p == with || (!isNew && p.Tags.Contains(t.Id)), Foreground = Brushes.White, Margin = new Thickness(2, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = p.Path }; checks.Add(new KeyValuePair<Proj, CheckBox>(p, cb)); pl.Children.Add(cb);
            }
            if (all.Count == 0) pl.Children.Add(Txt("Még nincs projekt a listában.", 12, Muted, false));
            pf.TextChanged += delegate { string q = pf.Text.Trim().ToLowerInvariant(); foreach (KeyValuePair<Proj, CheckBox> kv in checks) kv.Value.Visibility = Matches(kv.Key, q) ? Visibility.Visible : Visibility.Collapsed; };
            ScrollViewer psv = Scroll(pl); psv.MaxHeight = 200; sp.Children.Add(new Border { Background = Br(0, 0, 0, 70), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 4, 4), Child = psv });
            Grid bar = new Grid { Margin = new Thickness(0, 14, 0, 0) }; bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (!isNew) { HubTag old = t; Border del = Chip("🗑 Törlés", delegate { HideModal(); DeleteTag(old); }, 2); del.HorizontalAlignment = HorizontalAlignment.Left; bar.Children.Add(del); }
            Action save = delegate
            {
                string n = OneLine(name.Text); if (n.Length == 0) { Toast("Adj nevet a címkének"); name.Focus(); return; }
                HubTag me = t; if (tags.Exists(delegate (HubTag x) { return x != me && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase); })) { Toast("Már van „" + n + "” nevű címke"); name.Focus(); return; }
                if (me == null) { me = new HubTag { Id = Guid.NewGuid().ToString("N").Substring(0, 8) }; tags.Add(me); }
                me.Name = n; me.Color = color; int cnt = 0;
                foreach (KeyValuePair<Proj, CheckBox> kv in checks) { if (kv.Value.IsChecked == true) { cnt++; if (!kv.Key.Tags.Contains(me.Id)) kv.Key.Tags.Add(me.Id); } else kv.Key.Tags.Remove(me.Id); }
                HideModal(); SaveMeta(); RefreshProjList(); if (curProj != null) ShowDetail(curProj); Toast("🏷 " + n + " – " + cnt + " projekt");
            };
            StackPanel btn = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(btn, 1); btn.Children.Add(Chip("Mégse", delegate { HideModal(); })); btn.Children.Add(Chip(isNew ? "Létrehozás" : "Mentés", save, 1)); bar.Children.Add(btn); sp.Children.Add(bar);
            name.PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Enter) { save(); e.Handled = true; } };
            ShowModal(ModalPanel(sp, 520)); Dispatcher.BeginInvoke(new Action(delegate { name.Focus(); Keyboard.Focus(name); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // a mappa tartalmából felismeri a típust, a csomagkezelőt, a futtatható parancsokat és a git-állapotot (háttérszálon fut)
        Proj Inspect(Proj src)
        {
            Proj p = new Proj { Path = src.Path, Name = src.Name }; string d = p.Path; Func<string, bool> has = delegate (string n) { return File.Exists(System.IO.Path.Combine(d, n)) || Directory.Exists(System.IO.Path.Combine(d, n)); };
            if (!Directory.Exists(d)) { p.Missing = true; return p; }
            List<string> kinds = new List<string>(); List<string[]> sc = new List<string[]>(); string pm = "";
            if (has("package.json"))
            {
                kinds.Add("Node"); pm = has("pnpm-lock.yaml") ? "pnpm" : has("yarn.lock") ? "yarn" : has("bun.lockb") || has("bun.lock") ? "bun" : "npm";
                string json = ""; try { json = File.ReadAllText(System.IO.Path.Combine(d, "package.json")); } catch { }
                if (Regex.IsMatch(json, "\"next\"\\s*:")) kinds.Add("Next.js"); if (Regex.IsMatch(json, "\"react\"\\s*:")) kinds.Add("React"); if (Regex.IsMatch(json, "\"vue\"\\s*:")) kinds.Add("Vue"); if (Regex.IsMatch(json, "\"svelte\"\\s*:")) kinds.Add("Svelte"); if (Regex.IsMatch(json, "\"vite\"\\s*:")) kinds.Add("Vite"); if (Regex.IsMatch(json, "\"express\"\\s*:")) kinds.Add("Express"); if (Regex.IsMatch(json, "\"electron\"\\s*:")) kinds.Add("Electron"); if (has("tsconfig.json")) kinds.Add("TypeScript");
                Match m = Regex.Match(json, "\"scripts\"\\s*:\\s*\\{((?:[^\"{}]|\"(?:[^\"\\\\]|\\\\.)*\")*)\\}", RegexOptions.Singleline);
                if (m.Success) foreach (Match s in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")) { string n = s.Groups[1].Value; if (!Regex.IsMatch(n, @"^[A-Za-z0-9:_.\-/@]+$")) continue; sc.Add(new string[] { n, pm == "yarn" ? "yarn " + n : pm + " run " + n }); }
            }
            if (has("pyproject.toml") || has("requirements.txt") || has("setup.py") || has("Pipfile") || has("manage.py")) kinds.Add("Python");
            if (has("Cargo.toml")) kinds.Add("Rust"); if (has("go.mod")) kinds.Add("Go"); if (has("pom.xml")) kinds.Add("Java"); if (has("build.gradle") || has("build.gradle.kts")) kinds.Add("Java"); if (has("composer.json")) kinds.Add("PHP");
            try { string[] top = Directory.GetFiles(d); foreach (string f in top) { string e = System.IO.Path.GetExtension(f).ToLowerInvariant(); if (e == ".sln" || e == ".csproj") { if (!kinds.Contains(".NET")) kinds.Add(".NET"); } } } catch { }
            if (has("Dockerfile") || has("docker-compose.yml") || has("compose.yaml") || has("docker-compose.yaml")) kinds.Add("Docker");
            p.Main = has("main.py") ? "main.py" : has("app.py") ? "app.py" : has("manage.py") ? "manage.py" : "";
            p.Kinds = kinds; p.Scripts = sc; p.Pm = pm;
            p.IsGit = has(".git"); p.Commits = new List<string>(); p.Branch = ""; p.Remote = ""; p.Dirty = p.Ahead = p.Behind = 0;
            if (p.IsGit && FindExe("git") != null)
            {
                int ex; string st = Git(d, "--no-optional-locks -c core.fsmonitor=false status --porcelain=v1 -b", 8000, out ex);
                if (ex == 0)
                {
                    string[] lines = st.Replace("\r", "").Split('\n'); if (lines.Length > 0 && lines[0].StartsWith("## "))
                    {
                        string h = lines[0].Substring(3); if (h.StartsWith("No commits yet on ")) h = h.Substring(18); else if (h.StartsWith("Initial commit on ")) h = h.Substring(18); Match ab = Regex.Match(h, @"\[(.*)\]"); string br = h; int dots = h.IndexOf("..."); if (dots > 0) br = h.Substring(0, dots); else { int sp = h.IndexOf(' '); if (sp > 0) br = h.Substring(0, sp); }
                        p.Branch = br;
                        if (ab.Success) { Match a1 = Regex.Match(ab.Groups[1].Value, @"ahead (\d+)"), b1 = Regex.Match(ab.Groups[1].Value, @"behind (\d+)"); if (a1.Success) p.Ahead = int.Parse(a1.Groups[1].Value); if (b1.Success) p.Behind = int.Parse(b1.Groups[1].Value); }
                    }
                    for (int i = 1; i < lines.Length; i++) if (lines[i].Trim().Length > 0) p.Dirty++;
                }
                string lg = Git(d, "log -6 --pretty=format:%h\u001f%s\u001f%cr", 8000, out ex); if (ex == 0) foreach (string l in lg.Replace("\r", "").Split('\n')) if (l.Trim().Length > 0) p.Commits.Add(l);
                string rm = Git(d, "remote get-url origin", 5000, out ex); if (ex == 0) p.Remote = FirstLine(rm);
                foreach (string j in new string[] { "dist", "build", "out", "target", "bin", "obj", "coverage" }) { if (!Directory.Exists(System.IO.Path.Combine(d, j))) continue; string tr = Git(d, "ls-files -- \"" + j + "\"", 5000, out ex); if (ex == 0 && tr.Trim().Length > 0) p.Tracked.Add(j); }      // verziókezelt mappát nem ajánlunk fel törlésre
            }
            return p;
        }

        // ---------------------------------------------------------------- részletek
        void RunIn(Proj p, string title, string cmd) { if (!Directory.Exists(p.Path)) { Toast("A mappa nem található: " + p.Path); return; } Term(DName(p) + ": " + title, p.Path, cmd); }
        static string WebUrl(string remote)
        {
            if (string.IsNullOrEmpty(remote)) return null; string u = remote.Trim(); if (u.StartsWith("git@")) { u = u.Substring(4).Replace(":", "/"); u = "https://" + u; } if (u.EndsWith(".git")) u = u.Substring(0, u.Length - 4); Uri uu; if (!Uri.TryCreate(u, UriKind.Absolute, out uu) || (uu.Scheme != "http" && uu.Scheme != "https")) return null; return uu.GetLeftPart(UriPartial.Path);
        }

        void ShowDetail(Proj p)
        {
            projDetail.Children.Clear(); StackPanel d = projDetail;
            d.Children.Add(new TextBlock { Text = KindIcon(p) + "  " + DName(p), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock path = Txt((p.Alias.Length > 0 ? p.Name + "  ·  " : "") + p.Path, 11.5, Muted, false); path.TextTrimming = TextTrimming.CharacterEllipsis; path.Cursor = Cursors.Hand; path.ToolTip = "Kattints: útvonal másolása"; path.MouseLeftButtonUp += delegate { Copy(p.Path, "Útvonal másolva"); }; path.Margin = new Thickness(0, 2, 0, 8); d.Children.Add(path);
            WrapPanel tr = new WrapPanel(); foreach (string id in p.Tags) { HubTag t = FindTag(id); if (t != null) tr.Children.Add(TagPill(p, t)); }
            Border addTag = null; addTag = Chip(HasAnyTag(p) ? "+ Címke ▾" : "🏷 Címke hozzáadása ▾", delegate { GlassMenu.Show(addTag, TagMenu(p)); }); addTag.ToolTip = "A címkék csak itt rendszereznek, a mappát nem érintik"; tr.Children.Add(addTag); tr.Children.Add(Chip("✎ Átnevezés", delegate { RenameProj(p); })); d.Children.Add(tr);
            if (!p.Loaded) { d.Children.Add(Txt("… betöltés", 12, Muted, false)); return; }
            if (p.Missing)
            {
                d.Children.Add(new TextBlock { Text = "⚠ A mappa nem található ezen az útvonalon. Áthelyezted vagy átnevezted, esetleg le van csatolva a meghajtó? A címkéi és a megjelenített neve megmaradnak, amíg el nem távolítod.", Foreground = Orange, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
                WrapPanel mw = new WrapPanel(); mw.Children.Add(Chip("📂 Új hely kiválasztása…", delegate { RelocateProj(p); }, 1)); mw.Children.Add(Chip("↻ Újraellenőrzés", delegate { p.Loaded = false; ShowDetail(p); LoadProj(p); })); mw.Children.Add(Chip("Eltávolítás a listából", delegate { RemoveProj(p); }, 2)); d.Children.Add(mw); return;
            }
            if (p.Kinds.Count > 0) { WrapPanel tags = new WrapPanel(); foreach (string k in p.Kinds) tags.Children.Add(new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 5, 5), Background = Br(10, 132, 255, 50), Child = Txt(k, 11, Br(120, 190, 255, 255), true) }); if (p.Pm.Length > 0) tags.Children.Add(new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 5, 5), Background = Br(255, 255, 255, 24), Child = Txt(p.Pm, 11, Muted, true) }); d.Children.Add(tags); }
            if (p.IsGit && p.Branch.Length > 0)
            {
                string gl = "⎇ " + p.Branch + (p.Dirty > 0 ? "   ✎ " + p.Dirty + " módosított fájl" : "   ✓ tiszta") + (p.Ahead > 0 ? "   ↑" + p.Ahead + " push-ra vár" : "") + (p.Behind > 0 ? "   ↓" + p.Behind + " letöltendő" : "");
                TextBlock t = Txt(gl, 12.5, p.Dirty > 0 || p.Behind > 0 ? Orange : Green, true); t.Margin = new Thickness(0, 2, 0, 0); d.Children.Add(t);
            }
            // --- megnyitás
            d.Children.Add(Head("Megnyitás")); WrapPanel o = new WrapPanel();
            o.Children.Add(Chip("💠 VS Code", delegate { if (!OpenInEditor("code", p.Path)) Toast("A VS Code nem található (Eszközök → VS Code)"); }, 1)); o.Children.Add(Chip("🚀 Antigravity", delegate { if (!OpenInEditor("antigravity", p.Path)) Toast("Az Antigravity nem található"); }));
            o.Children.Add(Chip("⌨ Terminál itt", delegate { Term(p.Name, p.Path, null); })); o.Children.Add(Chip("📂 Intéző", delegate { OpenFolder(p.Path); })); string web = WebUrl(p.Remote);
            if (web != null) o.Children.Add(Chip("🐙 Repo a böngészőben", delegate { OpenUrl(web); })); o.Children.Add(Chip("📋 Útvonal", delegate { Copy(p.Path, "Útvonal másolva"); })); d.Children.Add(o);
            // --- futtatás
            WrapPanel run = new WrapPanel(); string[] fav = { "dev", "start", "build", "test", "lint", "preview" };
            foreach (string[] s in p.Scripts) { string[] ss = s; run.Children.Add(Chip("▶ " + s[0], delegate { RunIn(p, ss[0], ss[1]); }, Array.IndexOf(fav, s[0]) >= 0 ? 1 : 0)); }
            string vd = Directory.Exists(System.IO.Path.Combine(p.Path, ".venv")) ? ".venv" : Directory.Exists(System.IO.Path.Combine(p.Path, "venv")) ? "venv" : null; string py = vd != null ? @".\" + vd + @"\Scripts\python" : "python";
            if (p.Kinds.Contains("Python"))
            {
                if (p.Main.Length > 0) run.Children.Add(Chip("▶ " + (p.Main == "manage.py" ? "runserver" : p.Main), delegate { RunIn(p, p.Main, p.Main == "manage.py" ? py + " manage.py runserver" : py + " " + p.Main); }, 1));
                if (!Directory.Exists(System.IO.Path.Combine(p.Path, ".venv")) && !Directory.Exists(System.IO.Path.Combine(p.Path, "venv"))) run.Children.Add(Chip("🧪 venv létrehozása", delegate { RunIn(p, "venv", "python -m venv .venv"); })); else run.Children.Add(Chip("🧪 venv aktiválása", delegate { RunIn(p, "venv", @".\" + vd + @"\Scripts\Activate.ps1"); }));
                if (File.Exists(System.IO.Path.Combine(p.Path, "requirements.txt"))) run.Children.Add(Chip("⬇ pip install -r", delegate { RunIn(p, "pip", py + " -m pip install -r requirements.txt"); })); else if (File.Exists(System.IO.Path.Combine(p.Path, "pyproject.toml"))) run.Children.Add(Chip("⬇ pip install -e .", delegate { RunIn(p, "pip", py + " -m pip install -e ."); }));
                run.Children.Add(Chip("🧪 pytest", delegate { RunIn(p, "pytest", py + " -m pytest"); }));
            }
            if (p.Kinds.Contains(".NET")) { run.Children.Add(Chip("▶ dotnet run", delegate { RunIn(p, "run", "dotnet run"); }, 1)); run.Children.Add(Chip("dotnet watch", delegate { RunIn(p, "watch", "dotnet watch run"); })); run.Children.Add(Chip("dotnet build", delegate { RunIn(p, "build", "dotnet build"); })); run.Children.Add(Chip("dotnet test", delegate { RunIn(p, "test", "dotnet test"); })); run.Children.Add(Chip("dotnet restore", delegate { RunIn(p, "restore", "dotnet restore"); })); }
            if (p.Kinds.Contains("Rust")) { run.Children.Add(Chip("▶ cargo run", delegate { RunIn(p, "run", "cargo run"); }, 1)); run.Children.Add(Chip("cargo build --release", delegate { RunIn(p, "build", "cargo build --release"); })); run.Children.Add(Chip("cargo test", delegate { RunIn(p, "test", "cargo test"); })); run.Children.Add(Chip("cargo clippy", delegate { RunIn(p, "clippy", "cargo clippy"); })); }
            if (p.Kinds.Contains("Go")) { run.Children.Add(Chip("▶ go run .", delegate { RunIn(p, "run", "go run ."); }, 1)); run.Children.Add(Chip("go build ./...", delegate { RunIn(p, "build", "go build ./..."); })); run.Children.Add(Chip("go test ./...", delegate { RunIn(p, "test", "go test ./..."); })); run.Children.Add(Chip("go mod tidy", delegate { RunIn(p, "tidy", "go mod tidy"); })); }
            if (File.Exists(System.IO.Path.Combine(p.Path, "pom.xml"))) { run.Children.Add(Chip("mvn package", delegate { RunIn(p, "package", "mvn package"); }, 1)); run.Children.Add(Chip("mvn test", delegate { RunIn(p, "test", "mvn test"); })); }
            if (File.Exists(System.IO.Path.Combine(p.Path, "gradlew.bat"))) { run.Children.Add(Chip("gradlew build", delegate { RunIn(p, "build", @".\gradlew.bat build"); }, 1)); run.Children.Add(Chip("gradlew test", delegate { RunIn(p, "test", @".\gradlew.bat test"); })); }
            if (File.Exists(System.IO.Path.Combine(p.Path, "Makefile"))) run.Children.Add(Chip("make", delegate { RunIn(p, "make", "make"); }));
            if (File.Exists(System.IO.Path.Combine(p.Path, "docker-compose.yml")) || File.Exists(System.IO.Path.Combine(p.Path, "compose.yaml")) || File.Exists(System.IO.Path.Combine(p.Path, "docker-compose.yaml"))) { run.Children.Add(Chip("🐳 compose up -d", delegate { RunIn(p, "compose up", "docker compose up -d"); }, 1)); run.Children.Add(Chip("compose down", delegate { RunIn(p, "compose down", "docker compose down"); })); run.Children.Add(Chip("compose logs -f", delegate { RunIn(p, "compose logs", "docker compose logs -f --tail 100"); })); }
            else if (File.Exists(System.IO.Path.Combine(p.Path, "Dockerfile"))) run.Children.Add(Chip("🐳 docker build", delegate { RunIn(p, "docker build", "docker build -t " + PsQuote(DockerTag(p.Name)) + " ."); }));
            if (run.Children.Count > 0) { d.Children.Add(Head("Futtatás")); d.Children.Add(run); }
            // --- függőségek
            if (p.Pm.Length > 0)
            {
                d.Children.Add(Head("Függőségek (" + p.Pm + ")")); WrapPanel dep = new WrapPanel(); string inst = p.Pm == "yarn" ? "yarn install" : p.Pm + " install";
                dep.Children.Add(Chip("⬇ " + inst, delegate { RunIn(p, "install", inst); }, 1)); dep.Children.Add(Chip("📊 Elavult csomagok", delegate { RunIn(p, "outdated", p.Pm == "yarn" ? "yarn outdated" : p.Pm + " outdated"); })); if (p.Pm == "npm" || p.Pm == "pnpm") dep.Children.Add(Chip("🛡 Biztonsági audit", delegate { RunIn(p, "audit", p.Pm + " audit"); }));
                dep.Children.Add(Chip("📦 Csomag hozzáadása…", delegate { Ask("Csomag neve (pl. axios, zod@latest)", "", "Hozzáad", delegate (string n) { if (n.Trim().Length > 0) RunIn(p, "add " + n.Trim(), (p.Pm == "npm" ? "npm install " : p.Pm + " add ") + n.Trim()); }); })); d.Children.Add(dep);
            }
            // --- git
            d.Children.Add(Head("Git")); WrapPanel gw = new WrapPanel();
            if (!p.IsGit) gw.Children.Add(Chip("🌿 git init", delegate { RunIn(p, "git init", "git init"); }, 1));
            else
            {
                gw.Children.Add(Chip("⬇ Pull", delegate { RunIn(p, "pull", "git pull"); }, p.Behind > 0 ? 1 : 0)); gw.Children.Add(Chip("↻ Fetch", delegate { RunIn(p, "fetch", "git fetch --all --prune"); })); gw.Children.Add(Chip("⬆ Push", delegate { RunIn(p, "push", "git push"); }, p.Ahead > 0 ? 1 : 0));
                gw.Children.Add(Chip("📄 Status", delegate { RunIn(p, "status", "git status"); })); gw.Children.Add(Chip("📈 Napló", delegate { RunIn(p, "log", "git log --oneline --graph --decorate -n 30"); })); gw.Children.Add(Chip("± Diff", delegate { RunIn(p, "diff", "git diff --stat; git diff"); }));
                gw.Children.Add(Chip("✔ Commit mindent…", delegate { Ask("Commit üzenet", "", "Commit", delegate (string m) { if (m.Trim().Length > 0) RunIn(p, "commit", "git add -A; git commit -m " + PsQuote(m.Trim())); }); }, p.Dirty > 0 ? 1 : 0));
                gw.Children.Add(Chip("🌱 Új ág…", delegate { Ask("Új ág neve", "feature/", "Létrehoz", delegate (string b) { if (b.Trim().Length > 0) RunIn(p, "ág: " + b.Trim(), "git checkout -b " + PsQuote(b.Trim())); }); }));
                Border bchip = null; bchip = Chip("⎇ Ág váltása ▾", delegate { BranchMenu(p, bchip); }); gw.Children.Add(bchip);
                gw.Children.Add(Chip("🧳 Stash", delegate { RunIn(p, "stash", "git stash"); })); gw.Children.Add(Chip("🧳 Stash pop", delegate { RunIn(p, "stash pop", "git stash pop"); }));
                gw.Children.Add(Chip("↩ Utolsó commit visszavonása", delegate { Confirm("Utolsó commit visszavonása", "A legutóbbi commit megszűnik, de a változtatások megmaradnak a munkamappában (git reset --soft HEAD~1).", "Visszavon", delegate { RunIn(p, "reset", "git reset --soft HEAD~1"); }); }, 2));
                gw.Children.Add(Chip("🧹 Nem követett fájlok törlése…", delegate { Confirm("git clean", "A nem követett (untracked) fájlok és mappák törlődnek (git clean -fd). Ezt nem lehet visszavonni.", "Töröl", delegate { RunIn(p, "clean", "git clean -fdn; Read-Host 'A fenti lista törlődik – Enter: törlés, Ctrl+C: mégse'; git clean -fd"); }); }, 2));
            }
            d.Children.Add(gw);
            if (p.Commits.Count > 0)
            {
                d.Children.Add(Head("Legutóbbi commitok")); StackPanel cm = new StackPanel(); foreach (string l in p.Commits) { string[] c = l.Split('\u001f'); if (c.Length < 3) continue; Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock h = new TextBlock { Text = c[0], FontFamily = Mono, FontSize = 11.5, Foreground = Accent }; TextBlock s = new TextBlock { Text = c[1], FontSize = 12, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(s, 1); TextBlock w = Txt(c[2], 11, Muted, false); w.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(w, 2); g.Children.Add(h); g.Children.Add(s); g.Children.Add(w); g.Margin = new Thickness(2, 2, 2, 2); string hash = c[0]; g.Cursor = Cursors.Hand; g.ToolTip = "Kattints: hash másolása"; g.MouseLeftButtonUp += delegate { Copy(hash, "Hash másolva"); }; cm.Children.Add(g); }
                d.Children.Add(CardBox(cm));
            }
            // --- tisztítás
            d.Children.Add(Head("Tisztítás")); WrapPanel cl = new WrapPanel(); d.Children.Add(cl); string[] junk = { "node_modules", ".next", ".nuxt", "dist", "build", "out", "target", "bin", "obj", "__pycache__", ".venv", "venv", "coverage", ".turbo", ".parcel-cache" };
            foreach (string j in junk)
            {
                if ((j == "bin" || j == "obj") && !p.Kinds.Contains(".NET")) continue; if (p.Tracked.Contains(j)) continue;
                string full = System.IO.Path.Combine(p.Path, j); if (!Directory.Exists(full)) continue; string jj = j; Border chip = null; chip = Chip("🗑 " + j + " …", delegate { DeleteDir(p, full, jj); }, 2); cl.Children.Add(chip);
                SizeAsync(full, delegate (string sz) { SetChipText(chip, "🗑 " + jj + " (" + sz + ")"); });
            }
            if (cl.Children.Count == 0) cl.Children.Add(Txt("Nincs törölhető build-/függőség mappa.", 12, Muted, false));
            if (p.Pm.Length > 0 && Directory.Exists(System.IO.Path.Combine(p.Path, "node_modules"))) cl.Children.Add(Chip("♻ Újratelepítés (node_modules törlése + install)", delegate { Confirm("Újratelepítés", "A node_modules mappa törlődik, majd lefut a(z) " + p.Pm + " install.", "Mehet", delegate { RunIn(p, "újratelepítés", "cmd /c rmdir /s /q " + PsQuote(LongPath(System.IO.Path.Combine(p.Path, "node_modules"))) + "; " + p.Pm + " install"); }); }));
        }

        void BranchMenu(Proj p, FrameworkElement anchor)
        {
            Bg(delegate { int ex; string o = Git(p.Path, "for-each-ref refs/heads refs/remotes --format=%(refname:short)", 8000, out ex); return ex == 0 ? o : ""; }, delegate (string o)
            {
                List<MenuEntry> m = new List<MenuEntry>(); foreach (string b0 in (o ?? "").Replace("\r", "").Split('\n')) { string b = b0.Trim(); if (b.Length == 0 || b.EndsWith("/HEAD") || b == "origin") continue; string bb = b.StartsWith("origin/") ? b.Substring(7) : b; if (m.Count > 24) break; bool cur = bb == p.Branch; m.Add(GlassMenu.Check(b, cur, delegate { RunIn(p, "ág: " + bb, "git checkout " + PsQuote(bb)); })); }
                if (m.Count == 0) m.Add(GlassMenu.Off("Nincs ág")); GlassMenu.Show(anchor, m.ToArray());
            });
        }

        void DeleteDir(Proj p, string full, string name)
        {
            Confirm("Törlés: " + name, full + "\n\nA mappa véglegesen törlődik. " + (name == "node_modules" || name == ".venv" || name == "venv" ? "Újra létrehozható (install / venv)." : "A build újra előállítható."), "Töröl", delegate
            {
                Toast("Törlés folyamatban: " + name + "…");
                Bg(delegate { int ex; RunCmd("rmdir /s /q \"" + LongPath(full) + "\"", 600000, null, out ex); return !Directory.Exists(full); }, delegate (bool ok) { Toast(ok ? "✓ Törölve: " + name : "Nem sikerült teljesen törölni (használatban lévő fájl?)"); if (curProj == p) ShowDetail(p); });
            });
        }

        static string DockerTag(string n) { string t = Regex.Replace((n ?? "").ToLowerInvariant(), @"[^a-z0-9_.\-]+", "-").Trim('-', '.'); return t.Length > 0 ? t : "app"; }
        // hosszú útvonalak (260 karakter felett) törléséhez: \\?\ előtag
        internal static string LongPath(string p) { if (p != null && p.Length > 2 && p[1] == ':' && !p.StartsWith(@"\\?\")) return @"\\?\" + p; return p; }
        // mappaméretek: egyetlen sorban futó munkás + 60 mp-es gyorsítótár (nem árasztja el a szálkészletet)
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> sizeCache = new Dictionary<string, KeyValuePair<DateTime, string>>(); static readonly Queue<KeyValuePair<string, Action<string>>> sizeQ = new Queue<KeyValuePair<string, Action<string>>>(); static bool sizeWorker;
        void SizeAsync(string full, Action<string> done)
        {
            lock (sizeCache) { KeyValuePair<DateTime, string> kv; if (sizeCache.TryGetValue(full, out kv) && (DateTime.Now - kv.Key).TotalSeconds < 60) { done(kv.Value); return; } }
            lock (sizeQ) { sizeQ.Enqueue(new KeyValuePair<string, Action<string>>(full, done)); if (sizeWorker) return; sizeWorker = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    KeyValuePair<string, Action<string>> it; lock (sizeQ) { if (sizeQ.Count == 0) { sizeWorker = false; return; } it = sizeQ.Dequeue(); }
                    string r; try { bool cap; r = FmtSize(DirSize(it.Key, 6000, out cap)) + (cap ? "+" : ""); } catch { r = "?"; }
                    lock (sizeCache) { sizeCache[it.Key] = new KeyValuePair<DateTime, string>(DateTime.Now, r); }
                    Action<string> cb = it.Value; string rr = r; try { Dispatcher.BeginInvoke(new Action(delegate { try { cb(rr); } catch { } })); } catch { }
                }
            });
        }

        // mappaméret (a szimbolikus linkeket nem követi); időkorlát után "legalább" értéket ad
        internal static long DirSize(string path, int capMs, out bool capped)
        {
            capped = false; long total = 0; DateTime end = DateTime.Now.AddMilliseconds(capMs); Stack<string> st = new Stack<string>(); st.Push(path);
            while (st.Count > 0)
            {
                if (DateTime.Now > end) { capped = true; break; } string dir = st.Pop();
                try { DirectoryInfo di = new DirectoryInfo(dir); foreach (FileInfo f in di.GetFiles()) total += f.Length; foreach (DirectoryInfo s in di.GetDirectories()) { if ((s.Attributes & FileAttributes.ReparsePoint) == 0) st.Push(s.FullName); } } catch { }
            }
            return total;
        }

        // ================================================================== ÚJ PROJEKT (sablonok)
        class Tpl { public string Icon, Name, Desc, Id; }
        static readonly Tpl[] Templates = {
            new Tpl { Icon = "⚛", Name = "React + Vite", Desc = "JavaScript", Id = "vite-react" }, new Tpl { Icon = "⚛", Name = "React + Vite (TypeScript)", Desc = "TypeScript", Id = "vite-react-ts" }, new Tpl { Icon = "▲", Name = "Next.js", Desc = "kérdéseket tesz fel", Id = "next" },
            new Tpl { Icon = "🟩", Name = "Vue", Desc = "create-vue", Id = "vue" }, new Tpl { Icon = "🔶", Name = "Svelte + Vite", Desc = "JavaScript", Id = "vite-svelte" }, new Tpl { Icon = "🌐", Name = "Egyszerű weboldal", Desc = "HTML + CSS + JS", Id = "static" },
            new Tpl { Icon = "🟢", Name = "Node.js + Express API", Desc = "package.json + index.js", Id = "express" }, new Tpl { Icon = "🐍", Name = "Python projekt", Desc = "venv + main.py", Id = "python" }, new Tpl { Icon = "⚡", Name = "FastAPI", Desc = "Python webszerver", Id = "fastapi" },
            new Tpl { Icon = "🟣", Name = ".NET konzolalkalmazás", Desc = "dotnet new console", Id = "dotnet-console" }, new Tpl { Icon = "🟣", Name = ".NET Web API", Desc = "dotnet new webapi", Id = "dotnet-webapi" }, new Tpl { Icon = "🦀", Name = "Rust", Desc = "cargo new", Id = "rust" }, new Tpl { Icon = "🐹", Name = "Go modul", Desc = "go mod init + main.go", Id = "go" }
        };
        TextBox npName, npDir; CheckBox npGit, npEditor; Tpl npSel; List<Border> npCards = new List<Border>();

        FrameworkElement BuildNewProject()
        {
            StackPanel root = new StackPanel(); Grid form = new Grid(); form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            npName = Field("", 0); npName.Margin = new Thickness(0, 0, 8, 0); npDir = Field(LoadStr("newdir", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects")), 0); npDir.Margin = new Thickness(0, 0, 8, 0);
            FrameworkElement nh = Hinted(npName, "Projekt neve (pl. my-app)"); nh.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(nh, 0); Grid.SetColumn(npDir, 1); Border br = Chip("Tallózás…", delegate { using (Forms.FolderBrowserDialog dlg = new Forms.FolderBrowserDialog { Description = "Hová jöjjön létre a projekt?" }) { if (dlg.ShowDialog() == Forms.DialogResult.OK) npDir.Text = dlg.SelectedPath; } }); br.Margin = new Thickness(0); Grid.SetColumn(br, 2);
            form.Children.Add(nh); form.Children.Add(npDir); form.Children.Add(br); root.Children.Add(CardBox(form));
            WrapPanel opts = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) }; npGit = new CheckBox { Content = "git init", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0, 0, 18, 0), VerticalContentAlignment = VerticalAlignment.Center }; npEditor = new CheckBox { Content = "Megnyitás VS Code-ban a végén", IsChecked = true, Foreground = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center }; opts.Children.Add(npGit); opts.Children.Add(npEditor); root.Children.Add(opts);
            root.Children.Add(Head("Sablon (kattints, majd „Létrehozás”)")); WrapPanel cards = new WrapPanel();
            foreach (Tpl t in Templates)
            {
                Tpl tt = t; StackPanel sp = new StackPanel { Width = 188 }; sp.Children.Add(Txt(t.Icon + "  " + t.Name, 13, Brushes.White, true)); sp.Children.Add(Txt(t.Desc, 11, Muted, false));
                Border c = new Border { Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 8), Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = sp };
                c.MouseLeftButtonUp += delegate { npSel = tt; foreach (Border x in npCards) { x.Background = CardBg; x.BorderBrush = CardBorder; } c.Background = Br(10, 132, 255, 60); c.BorderBrush = Accent; };
                npCards.Add(c); cards.Children.Add(c);
            }
            root.Children.Add(cards); root.Children.Add(new Border { Height = 4 }); WrapPanel go = new WrapPanel(); go.Children.Add(Chip("✨ Létrehozás", delegate { CreateProject(); }, 1)); root.Children.Add(go);
            return Scroll(root);
        }

        static void Write(string dir, string rel, string content) { string p = System.IO.Path.Combine(dir, rel); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)); File.WriteAllText(p, content, new UTF8Encoding(false)); }

        void CreateProject()
        {
            string name = npName.Text.Trim(), parent = npDir.Text.Trim();
            if (npSel == null) { Toast("Válassz egy sablont"); return; } if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._\-]*$")) { Toast("A projekt neve: betűk, számok, - _ . (szóköz nélkül)"); return; }
            if (parent.Length == 0) { Toast("Add meg a célmappát"); return; } string dir; try { parent = System.IO.Path.GetFullPath(parent); dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(parent, name)); } catch (Exception ex0) { Toast("Érvénytelen útvonal: " + ex0.Message); return; } if (Regex.IsMatch(name, @"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$|\.$", RegexOptions.IgnoreCase)) { Toast("Ez a név Windowson nem használható"); return; } if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0) { Toast("A(z) " + dir + " már létezik és nem üres"); return; }
            try { Directory.CreateDirectory(parent); } catch (Exception ex) { Toast("A célmappa nem hozható létre: " + ex.Message); return; }
            SaveStr("newdir", parent); bool git = npGit.IsChecked == true, edit = npEditor.IsChecked == true; string id = npSel.Id; string cmd; string cwd = parent; string q = PsQuote(name); bool inPlace = false; string after = "";
            string py = @".\.venv\Scripts\python";
            try
            {
                switch (id)
                {
                    case "vite-react": cmd = "npm create vite@latest " + q + " -- --template react"; after = "npm install"; break;
                    case "vite-react-ts": cmd = "npm create vite@latest " + q + " -- --template react-ts"; after = "npm install"; break;
                    case "vite-svelte": cmd = "npm create vite@latest " + q + " -- --template svelte"; after = "npm install"; break;
                    case "next": cmd = "npx create-next-app@latest " + q; break;
                    case "vue": cmd = "npm create vue@latest " + q; after = "npm install"; break;
                    case "dotnet-console": cmd = "dotnet new console -n " + q; break;
                    case "dotnet-webapi": cmd = "dotnet new webapi -n " + q; break;
                    case "rust": cmd = "cargo new " + q; break;
                    case "static":
                        Write(dir, "index.html", "<!doctype html>\n<html lang=\"hu\">\n<head>\n  <meta charset=\"utf-8\">\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n  <title>" + name + "</title>\n  <link rel=\"stylesheet\" href=\"style.css\">\n</head>\n<body>\n  <main>\n    <h1>" + name + "</h1>\n    <p>Szia! Ez egy induló oldal.</p>\n    <button id=\"btn\">Kattints</button>\n  </main>\n  <script src=\"script.js\"></script>\n</body>\n</html>\n");
                        Write(dir, "style.css", "body { margin: 0; font-family: system-ui, sans-serif; background: #111; color: #fff; display: grid; place-items: center; min-height: 100vh; }\nmain { text-align: center; }\nbutton { padding: .6rem 1.2rem; border: 0; border-radius: 999px; background: #0a84ff; color: #fff; font-size: 1rem; cursor: pointer; }\n");
                        Write(dir, "script.js", "document.getElementById('btn').addEventListener('click', () => alert('Működik!'));\n"); cmd = ""; inPlace = true; break;
                    case "express":
                        Write(dir, "package.json", "{\n  \"name\": \"" + name.ToLowerInvariant() + "\",\n  \"version\": \"1.0.0\",\n  \"main\": \"index.js\",\n  \"scripts\": {\n    \"start\": \"node index.js\",\n    \"dev\": \"node --watch index.js\"\n  }\n}\n");
                        Write(dir, "index.js", "const express = require('express');\nconst app = express();\nconst port = process.env.PORT || 3000;\n\napp.use(express.json());\napp.get('/', (req, res) => res.json({ ok: true, message: 'Szia!' }));\n\napp.listen(port, () => console.log('http://localhost:' + port));\n");
                        Write(dir, ".gitignore", "node_modules\n.env\n"); cmd = "npm install express"; inPlace = true; break;
                    case "python":
                        Write(dir, "main.py", "def main():\n    print('Szia, világ!')\n\n\nif __name__ == '__main__':\n    main()\n"); Write(dir, "requirements.txt", ""); Write(dir, ".gitignore", ".venv/\n__pycache__/\n*.pyc\n.env\n"); cmd = "python -m venv .venv"; inPlace = true; break;
                    case "fastapi":
                        Write(dir, "main.py", "from fastapi import FastAPI\n\napp = FastAPI()\n\n\n@app.get('/')\ndef root():\n    return {'ok': True, 'message': 'Szia!'}\n"); Write(dir, "requirements.txt", "fastapi\nuvicorn[standard]\n"); Write(dir, ".gitignore", ".venv/\n__pycache__/\n*.pyc\n.env\n");
                        cmd = "python -m venv .venv; " + py + " -m pip install -r requirements.txt; Write-Host ''; Write-Host 'Indítás: .\\.venv\\Scripts\\uvicorn main:app --reload' -ForegroundColor Cyan"; inPlace = true; break;
                    case "go":
                        Write(dir, "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tfmt.Println(\"Szia, világ!\")\n}\n"); cmd = "go mod init " + q; inPlace = true; break;
                    default: Toast("Ismeretlen sablon"); return;
                }
            }
            catch (Exception ex) { Toast("A fájlok létrehozása nem sikerült: " + ex.Message); return; }
            if (inPlace) { cwd = dir; }
            // a végén: git, szerkesztő
            List<string> tail = new List<string>(); if (git) tail.Add(inPlace ? "git init" : "if (Test-Path .git) { } else { git init }");
            string editor = FindExe("code") != null ? "code" : (FindExe("antigravity") != null ? "antigravity" : null);
            if (edit && editor != null) tail.Add(editor + " .");
            string full = cmd;
            if (!inPlace) { List<string> inner = new List<string>(); if (after.Length > 0) inner.Add(after); inner.AddRange(tail); full = cmd + "; if (Test-Path -LiteralPath " + q + ") { Set-Location -LiteralPath " + q + (inner.Count > 0 ? "; " + string.Join("; ", inner.ToArray()) : "") + " }"; }      // csak sikeres létrehozás után lépünk a mappába (különben a szülőmappában futna tovább)
            else if (tail.Count > 0) full = (cmd.Length > 0 ? cmd + "; " : "") + string.Join("; ", tail.ToArray());
            if (inPlace && cmd.Length == 0 && tail.Count == 0) { full = ""; }
            Term("Új: " + name, cwd, full.Length > 0 ? full : null);
            Proj pr = AddProject(dir, false); pr.Loaded = false; if (!Directory.Exists(dir) || !IsProjectDir(dir)) WatchCreated(pr);
            Toast("Projekt létrehozva: " + dir); Dispatch(delegate { Select(PgProj); SelectProj(pr); });
        }
        void Dispatch(Action a) { Dispatcher.BeginInvoke(a); }
    }
}
