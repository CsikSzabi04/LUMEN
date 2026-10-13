using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GlassControlSuite
{
    public class MainWindow : Form
    {
        // Win32 API for Working Set Trimming
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        // Controls
        private TabControl tabControl;
        private TabPage tabWiper;
        private TabPage tabProcesses;
        private TabPage tabGlassUI;
        private TabPage tabDevTools;
        private TabPage tabTerminal;

        // Wiper Controls
        private Label lblRamPercent;
        private Label lblRamUsed;
        private Label lblRamFree;
        private Label lblRamStatus;
        private Button btnOptimize;
        private Panel circlePanel;
        private System.Windows.Forms.Timer updateTimer;

        // Process List Controls
        private ListView listViewProcs;
        private TextBox txtSearchProc;
        private Button btnKillProc;
        private Button btnTrimProc;

        // DevTools Controls
        private ListView listViewDevTools;
        private RichTextBox txtDevLog;

        // Terminal Controls
        private RichTextBox txtTerminalOutput;
        private TextBox txtTerminalInput;
        private Button btnRunCmd;

        // Stat adatok
        private float currentRamPercent = 0;
        private double usedGb = 0;
        private double freeGb = 0;
        private double totalGb = 0;
        private double lastFreedMb = 0;
        private string lastOptimizeTime = Loc.T("Még nem futott");

        public MainWindow()
        {
            InitializeComponent();
            RefreshMemoryStats();

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 2000;
            updateTimer.Tick += (s, e) => {
                RefreshMemoryStats();
                if (tabControl.SelectedTab == tabWiper) circlePanel.Invalidate();
                if (tabControl.SelectedTab == tabProcesses) RefreshProcessList();
            };
            updateTimer.Start();
        }

        private void InitializeComponent()
        {
            this.Text = "Wise Memory Optimizer & GlassControl Suite // Native App";
            this.Size = new Size(950, 680);
            this.MinimumSize = new Size(800, 550);
            this.BackColor = Color.FromArgb(9, 13, 22); // Obsidian Dark
            this.ForeColor = Color.FromArgb(240, 246, 252);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;

            // Custom TabControl
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            tabControl.Padding = new Point(16, 8);
            tabControl.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Tab Pages
            tabWiper = new TabPage("🧹 Wise Memory Wiper");
            tabProcesses = new TabPage(Loc.T("📊 Feladatkezelő & RAM"));
            tabGlassUI = new TabPage(Loc.T("🍏 iOS Glass UI Vezérlő"));
            tabDevTools = new TabPage(Loc.T("🛠️ Fejlesztői Hub"));
            tabTerminal = new TabPage(Loc.T("💻 Terminál"));

            foreach (TabPage tp in new TabPage[] { tabWiper, tabProcesses, tabGlassUI, tabDevTools, tabTerminal })
            {
                tp.BackColor = Color.FromArgb(13, 17, 23);
                tp.ForeColor = Color.FromArgb(240, 246, 252);
                tabControl.TabPages.Add(tp);
            }

            SetupWiperTab();
            SetupProcessesTab();
            SetupGlassUITab();
            SetupDevToolsTab();
            SetupTerminalTab();

            this.Controls.Add(tabControl);
        }

        #region Wiper Tab (Wise Memory Optimizer Native UI)
        private void SetupWiperTab()
        {
            // Circular RAM Meter Panel
            circlePanel = new Panel { Size = new Size(220, 220), Location = new Point(350, 20) };
            circlePanel.Paint += CirclePanel_Paint;

            lblRamPercent = new Label {
                Size = new Size(220, 40),
                Location = new Point(350, 110),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };

            // Stat Boxes (Használatban & Elérhető)
            Panel boxUsed = new Panel {
                Size = new Size(380, 45),
                Location = new Point(270, 260),
                BackColor = Color.FromArgb(254, 235, 200)
            };
            Label lblUsedTitle = new Label {
                Text = Loc.T("Használatban: "),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                AutoSize = true,
                Location = new Point(20, 12)
            };
            lblRamUsed = new Label {
                Text = "0.0 GB (0%)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                AutoSize = true,
                Location = new Point(140, 12)
            };
            boxUsed.Controls.Add(lblUsedTitle);
            boxUsed.Controls.Add(lblRamUsed);

            Panel boxFree = new Panel {
                Size = new Size(380, 45),
                Location = new Point(270, 315),
                BackColor = Color.FromArgb(220, 252, 231)
            };
            Label lblFreeTitle = new Label {
                Text = Loc.T("Elérhető: "),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(21, 128, 61),
                AutoSize = true,
                Location = new Point(20, 12)
            };
            lblRamFree = new Label {
                Text = "0.0 GB (0%)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(21, 128, 61),
                AutoSize = true,
                Location = new Point(140, 12)
            };
            boxFree.Controls.Add(lblFreeTitle);
            boxFree.Controls.Add(lblRamFree);

            // Status Label
            lblRamStatus = new Label {
                Text = Loc.T("Felszabadítás: Még nem futott"),
                Location = new Point(270, 370),
                Size = new Size(380, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(139, 148, 158)
            };

            // Big Green Optimize Button
            btnOptimize = new Button {
                Text = Loc.T("🔀  Optimalizálás / AZONNALI SEPERGETÉS"),
                Size = new Size(380, 55),
                Location = new Point(270, 410),
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOptimize.FlatAppearance.BorderSize = 0;
            btnOptimize.Click += (s, e) => PerformMemoryOptimization();

            tabWiper.Controls.Add(circlePanel);
            tabWiper.Controls.Add(lblRamPercent);
            tabWiper.Controls.Add(boxUsed);
            tabWiper.Controls.Add(boxFree);
            tabWiper.Controls.Add(lblRamStatus);
            tabWiper.Controls.Add(btnOptimize);
        }

        private void CirclePanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int width = circlePanel.Width;
            int height = circlePanel.Height;
            Rectangle rect = new Rectangle(10, 10, width - 20, height - 20);

            float sweepAngle = (currentRamPercent / 100f) * 360f;

            // Green base background (Available RAM)
            using (SolidBrush greenBrush = new SolidBrush(Color.FromArgb(34, 197, 94)))
            {
                e.Graphics.FillEllipse(greenBrush, rect);
            }

            // Orange slice (Used RAM)
            using (SolidBrush orangeBrush = new SolidBrush(Color.FromArgb(245, 158, 11)))
            {
                e.Graphics.FillPie(orangeBrush, rect, -90, sweepAngle);
            }
        }

        private void RefreshMemoryStats()
        {
            try
            {
                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    freeGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    usedGb = totalGb - freeGb;
                    currentRamPercent = (float)memStatus.dwMemoryLoad;

                    lblRamPercent.Text = string.Format("{0:F0}%", currentRamPercent);
                    lblRamUsed.Text = string.Format("{0:F2} GB ({1:F0}%)", usedGb, currentRamPercent);
                    lblRamFree.Text = string.Format("{0:F2} GB ({1:F0}%)", freeGb, 100 - currentRamPercent);
                }
            }
            catch { }
        }

        private void PerformMemoryOptimization()
        {
            btnOptimize.Enabled = false;
            btnOptimize.Text = Loc.T("Optimalizálás folyamatban...");
            Application.DoEvents();

            int count = 0;
            long freedTotalBytes = 0;

            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                try
                {
                    string name = proc.ProcessName.ToLower();
                    if (name == "system" || name == "idle" || name == "dwm" || name == "explorer" || name == "csrss")
                        continue;

                    IntPtr hProc = OpenProcess(0x0100 | 0x1000, false, proc.Id);
                    if (hProc != IntPtr.Zero)
                    {
                        long before = proc.WorkingSet64;
                        if (EmptyWorkingSet(hProc))
                        {
                            long after = proc.WorkingSet64;
                            freedTotalBytes += Math.Max(0, before - after);
                            count++;
                        }
                        CloseHandle(hProc);
                    }
                }
                catch { }
            }

            lastFreedMb = freedTotalBytes / (1024.0 * 1024.0);
            lastOptimizeTime = DateTime.Now.ToString("HH:mm:ss");

            RefreshMemoryStats();
            circlePanel.Invalidate();

            lblRamStatus.Text = string.Format(Loc.T("Felszabadítás: {0:F2} GB | Időpont: {1}"), lastFreedMb / 1024.0, lastOptimizeTime);
            btnOptimize.Text = Loc.T("🔀  Optimalizálás / AZONNALI SEPERGETÉS");
            btnOptimize.Enabled = true;

            MessageBox.Show(string.Format(Loc.T("Memóriatisztítás sikeres!\n\nÜrített folyamatok: {0} db\nFelszabadítva: ~{1:F1} MB"), count, lastFreedMb), "Wise Memory Wiper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        #region Process Tab
        private void SetupProcessesTab()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
            txtSearchProc = new TextBox { Width = 250, Location = new Point(10, 12), Font = new Font("Segoe UI", 10F) };
            txtSearchProc.TextChanged += (s, e) => RefreshProcessList();

            btnTrimProc = new Button { Text = Loc.T("Munkakészlet Ürítés"), Location = new Point(270, 10), Size = new Size(140, 30), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnTrimProc.Click += (s, e) => TrimSelectedProcess();

            btnKillProc = new Button { Text = Loc.T("Feladat Befejezése"), Location = new Point(420, 10), Size = new Size(140, 30), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnKillProc.Click += (s, e) => KillSelectedProcess();

            topPanel.Controls.Add(txtSearchProc);
            topPanel.Controls.Add(btnTrimProc);
            topPanel.Controls.Add(btnKillProc);

            listViewProcs = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5F)
            };
            listViewProcs.Columns.Add("PID", 70);
            listViewProcs.Columns.Add(Loc.T("Név"), 180);
            listViewProcs.Columns.Add("RAM (MB)", 100);
            listViewProcs.Columns.Add(Loc.T("Útvonal"), 400);

            tabProcesses.Controls.Add(listViewProcs);
            tabProcesses.Controls.Add(topPanel);
            RefreshProcessList();
        }

        private void RefreshProcessList()
        {
            string query = txtSearchProc.Text.ToLower();
            listViewProcs.Items.Clear();

            Process[] procs = Process.GetProcesses();
            var sorted = procs.OrderByDescending(p => {
                try { return p.WorkingSet64; } catch { return 0; }
            }).Take(100);

            foreach (Process p in sorted)
            {
                try
                {
                    if (!string.IsNullOrEmpty(query) && !p.ProcessName.ToLower().Contains(query) && !p.Id.ToString().Contains(query))
                        continue;

                    double mb = p.WorkingSet64 / (1024.0 * 1024.0);
                    string exe = Loc.T("(nincs hozzáférés)");
                    try { exe = p.MainModule.FileName; } catch { }

                    ListViewItem item = new ListViewItem(p.Id.ToString());
                    item.SubItems.Add(p.ProcessName);
                    item.SubItems.Add(string.Format("{0:F1} MB", mb));
                    item.SubItems.Add(exe);
                    item.Tag = p.Id;

                    listViewProcs.Items.Add(item);
                }
                catch { }
            }
        }

        private void TrimSelectedProcess()
        {
            if (listViewProcs.SelectedItems.Count == 0) return;
            int pid = (int)listViewProcs.SelectedItems[0].Tag;
            try
            {
                IntPtr hProc = OpenProcess(0x0100 | 0x1000, false, pid);
                if (hProc != IntPtr.Zero)
                {
                    EmptyWorkingSet(hProc);
                    CloseHandle(hProc);
                    MessageBox.Show(string.Format(Loc.T("PID {0} munkakészlete kiürítve!"), pid), Loc.T("Siker"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshProcessList();
                }
            }
            catch (Exception ex) { MessageBox.Show(Loc.T("Hiba: ") + ex.Message); }
        }

        private void KillSelectedProcess()
        {
            if (listViewProcs.SelectedItems.Count == 0) return;
            int pid = (int)listViewProcs.SelectedItems[0].Tag;
            if (MessageBox.Show(string.Format(Loc.T("Biztosan leállítod a PID {0} folyamatot?"), pid), Loc.T("Megerősítés"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    Process.GetProcessById(pid).Kill();
                    RefreshProcessList();
                }
                catch (Exception ex) { MessageBox.Show(Loc.T("Hiba: ") + ex.Message); }
            }
        }
        #endregion

        #region Glass UI Tab
        private void SetupGlassUITab()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30) };

            Button btnRestartDock = new Button { Text = Loc.T("🔄 GlassDock Újraindítása"), Location = new Point(50, 40), Size = new Size(300, 45), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            btnRestartDock.Click += (s, e) => RunPowerShellCommand("Get-Process GlassDock -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Process d:\\ADVANCED_USE\\dock\\GlassDock.exe");

            Button btnRestoreLayout = new Button { Text = Loc.T("📌 Asztali Ikonok Helyének Visszaállítása"), Location = new Point(50, 100), Size = new Size(300, 45), BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            btnRestoreLayout.Click += (s, e) => RunPowerShellCommand("powershell -ExecutionPolicy Bypass -File d:\\ADVANCED_USE\\dock\\Restore-DesktopLayout.ps1 -Apply");

            Button btnHideOverlays = new Button { Text = Loc.T("🚫 Parancsikon Nyilak & Pajzsok Elrejtése"), Location = new Point(50, 160), Size = new Size(300, 45), BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            btnHideOverlays.Click += (s, e) => RunPowerShellCommand("powershell -ExecutionPolicy Bypass -File d:\\ADVANCED_USE\\dock\\Hide-ShortcutOverlays.ps1");

            p.Controls.Add(btnRestartDock);
            p.Controls.Add(btnRestoreLayout);
            p.Controls.Add(btnHideOverlays);
            tabGlassUI.Controls.Add(p);
        }
        #endregion

        #region DevTools Tab
        private void SetupDevToolsTab()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            listViewDevTools = new ListView { Location = new Point(20, 20), Size = new Size(880, 220), View = View.Details, FullRowSelect = true, GridLines = true, BackColor = Color.FromArgb(13, 17, 23), ForeColor = Color.White, Font = new Font("Segoe UI", 10F) };
            listViewDevTools.Columns.Add(Loc.T("Eszköz"), 150);
            listViewDevTools.Columns.Add(Loc.T("Állapot"), 150);
            listViewDevTools.Columns.Add(Loc.T("Telepítés Parancs"), 500);

            Button btnInstallPython = new Button { Text = Loc.T("🐍 Python Telepítése (Winget)"), Location = new Point(20, 260), Size = new Size(220, 40), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnInstallPython.Click += (s, e) => InstallWingetPackage("Python.Python.3.12");

            Button btnInstallNode = new Button { Text = Loc.T("⚡ Node.js Telepítése (Winget)"), Location = new Point(250, 260), Size = new Size(220, 40), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnInstallNode.Click += (s, e) => InstallWingetPackage("OpenJS.NodeJS");

            Button btnInstallGit = new Button { Text = Loc.T("🐙 Git Telepítése (Winget)"), Location = new Point(480, 260), Size = new Size(220, 40), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnInstallGit.Click += (s, e) => InstallWingetPackage("Git.Git");

            txtDevLog = new RichTextBox { Location = new Point(20, 320), Size = new Size(880, 200), BackColor = Color.Black, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("Consolas", 9.5F) };

            p.Controls.Add(listViewDevTools);
            p.Controls.Add(btnInstallPython);
            p.Controls.Add(btnInstallNode);
            p.Controls.Add(btnInstallGit);
            p.Controls.Add(txtDevLog);
            tabDevTools.Controls.Add(p);

            CheckDevToolsStatus();
        }

        private void CheckDevToolsStatus()
        {
            listViewDevTools.Items.Clear();
            string[] tools = new string[] { "python --version", "node -v", "pnpm -v", "java -version", "git --version" };
            foreach (string t in tools)
            {
                string name = t.Split(' ')[0];
                bool ok = false;
                try
                {
                    Process proc = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + t) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                    proc.WaitForExit(1500);
                    ok = (proc.ExitCode == 0);
                }
                catch { }

                ListViewItem item = new ListViewItem(name.ToUpper());
                item.SubItems.Add(ok ? Loc.T("✓ Telepítve") : Loc.T("✗ Hiányzik"));
                item.SubItems.Add("winget install --id " + name);
                item.ForeColor = ok ? Color.LightGreen : Color.Pink;
                listViewDevTools.Items.Add(item);
            }
        }

        private void InstallWingetPackage(string packageId)
        {
            txtDevLog.AppendText(string.Format(Loc.T("\n[TELEPÍTÉS] Winget csomag: {0}...\n"), packageId));
            Task.Run(() => {
                try
                {
                    Process proc = Process.Start(new ProcessStartInfo("cmd.exe", string.Format("/c winget install --id {0} -e --silent --accept-package-agreements --accept-source-agreements", packageId)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                    string outStr = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    this.Invoke(new Action(() => {
                        txtDevLog.AppendText(outStr + Loc.T("\n[KÉSZ]\n"));
                        CheckDevToolsStatus();
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => txtDevLog.AppendText(Loc.T("Hiba: ") + ex.Message + "\n")));
                }
            });
        }
        #endregion

        #region Terminal Tab
        private void SetupTerminalTab()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            txtTerminalOutput = new RichTextBox { Location = new Point(15, 15), Size = new Size(890, 460), BackColor = Color.Black, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("Consolas", 10F) };
            txtTerminalOutput.AppendText(Loc.T("GlassControl Console v1.0.0 [Native Win32 Admin App]\nÍrj be egy PowerShell / CMD parancsot alább...\n\n"));

            txtTerminalInput = new TextBox { Location = new Point(15, 490), Size = new Size(760, 30), Font = new Font("Consolas", 10F), BackColor = Color.FromArgb(22, 27, 34), ForeColor = Color.White };
            txtTerminalInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) RunTerminalCmd(); };

            btnRunCmd = new Button { Text = Loc.T("Futtatás"), Location = new Point(785, 488), Size = new Size(120, 32), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnRunCmd.Click += (s, e) => RunTerminalCmd();

            p.Controls.Add(txtTerminalOutput);
            p.Controls.Add(txtTerminalInput);
            p.Controls.Add(btnRunCmd);
            tabTerminal.Controls.Add(p);
        }

        private void RunTerminalCmd()
        {
            string cmd = txtTerminalInput.Text;
            if (string.IsNullOrWhiteSpace(cmd)) return;

            txtTerminalOutput.AppendText(string.Format("> {0}\n", cmd));
            txtTerminalInput.Clear();

            Task.Run(() => {
                string res = RunPowerShellCommand(cmd);
                this.Invoke(new Action(() => {
                    txtTerminalOutput.AppendText(res + "\n");
                    txtTerminalOutput.SelectionStart = txtTerminalOutput.Text.Length;
                    txtTerminalOutput.ScrollToCaret();
                }));
            });
        }

        private string RunPowerShellCommand(string cmd)
        {
            try
            {
                Process proc = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command " + cmd) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                string outStr = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                return outStr;
            }
            catch (Exception ex) { return Loc.T("Hiba: ") + ex.Message; }
        }
        #endregion

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }
    }
}
