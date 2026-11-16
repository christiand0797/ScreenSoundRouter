using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenSoundRouter;

public class MainForm : Form
{
    private readonly RoutingEngine _engine;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly NotifyIcon _trayIcon;
    private readonly Panel _mappingsPanel;
    private readonly ListView _appsListView;
    private readonly ListBox _logListBox;
    private readonly CheckBox _chkAutoRoute;
    private readonly CheckBox _chkStartWithWindows;
    private readonly bool _startMinimized;
    private bool _allowExit;

    private record AudioComboItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public MainForm(RoutingEngine engine, bool startMinimized)
    {
        _engine = engine;
        _startMinimized = startMinimized;

        Text = "ScreenSoundRouter — Per-Monitor Automatic Audio Router";
        Size = new Size(880, 680);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.FromArgb(235, 238, 245);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CreateSpeakerIcon();

        // System Tray Icon
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open ScreenSoundRouter", null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add("Re-apply Audio Routing Now", null, (_, _) => _engine.Tick(forceReapply: true));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "ScreenSoundRouter (Active)",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        // Main Layout
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header & controls
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Monitor -> Audio mappings
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f)); // Active applications table
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f)); // Activity log
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Footer buttons

        // 1. Header Bar
        var headerPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var lblTitle = new Label
        {
            Text = "ScreenSoundRouter",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(96, 175, 255),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var lblSubtitle = new Label
        {
            Text = "Automatically routes each app or game's sound to the speakers of the monitor it is currently on.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(170, 178, 195),
            AutoSize = true,
            Location = new Point(2, 32)
        };

        var togglesFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(2, 60),
            Margin = new Padding(0)
        };

        _chkAutoRoute = new CheckBox
        {
            Text = "Enable Automatic Monitor-to-Speaker Routing",
            Checked = _engine.Config.AutoRoutingEnabled,
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 230, 160),
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Margin = new Padding(0, 0, 24, 0)
        };
        _chkAutoRoute.CheckedChanged += (_, _) =>
        {
            _engine.Config.AutoRoutingEnabled = _chkAutoRoute.Checked;
            _engine.Config.Save();
            _engine.Log(_chkAutoRoute.Checked ? "Automatic monitor audio routing ENABLED." : "Automatic monitor audio routing PAUSED.");
            if (_chkAutoRoute.Checked)
                _engine.Tick(forceReapply: true);
        };

        _chkStartWithWindows = new CheckBox
        {
            Text = "Start Automatically with Windows (Tray)",
            Checked = RouterConfig.IsStartWithWindowsEnabled(),
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 215, 225),
            Margin = new Padding(0, 2, 0, 0)
        };
        _chkStartWithWindows.CheckedChanged += (_, _) =>
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            RouterConfig.SetStartWithWindows(_chkStartWithWindows.Checked, exePath);
            _engine.Log(_chkStartWithWindows.Checked ? "Enabled 'Start with Windows'." : "Disabled 'Start with Windows'.");
        };

        togglesFlow.Controls.Add(_chkAutoRoute);
        togglesFlow.Controls.Add(_chkStartWithWindows);

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);
        headerPanel.Controls.Add(togglesFlow);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // 2. Monitor Mappings Group
        var grpMappings = new GroupBox
        {
            Text = "Monitor → Audio Output Device Mapping",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 210, 230),
            Padding = new Padding(12, 18, 12, 12),
            Margin = new Padding(0, 0, 0, 12)
        };
        _mappingsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true
        };
        grpMappings.Controls.Add(_mappingsPanel);
        mainLayout.Controls.Add(grpMappings, 0, 1);

        // 3. Active Audio Applications ListView
        var grpApps = new GroupBox
        {
            Text = "Live Audio Applications & Detected Screen Location",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 210, 230),
            Padding = new Padding(10, 18, 10, 10),
            Margin = new Padding(0, 0, 0, 12)
        };
        _appsListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = Color.FromArgb(32, 35, 44),
            ForeColor = Color.FromArgb(235, 238, 245),
            BorderStyle = BorderStyle.FixedSingle
        };
        _appsListView.Columns.Add("Process", 135);
        _appsListView.Columns.Add("PID", 65);
        _appsListView.Columns.Add("Window Title", 220);
        _appsListView.Columns.Add("Current Monitor", 175);
        _appsListView.Columns.Add("Routed Audio Output", 215);
        grpApps.Controls.Add(_appsListView);
        mainLayout.Controls.Add(grpApps, 0, 2);

        // 4. Activity Log
        var grpLog = new GroupBox
        {
            Text = "Routing Event Log",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 210, 230),
            Padding = new Padding(10, 18, 10, 10),
            Margin = new Padding(0, 0, 0, 12)
        };
        _logListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 20, 26),
            ForeColor = Color.FromArgb(165, 215, 255),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        grpLog.Controls.Add(_logListBox);
        mainLayout.Controls.Add(grpLog, 0, 3);

        // 5. Footer Action Buttons
        var footerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = new Padding(0)
        };

        var btnRefreshDevices = CreateButton("Refresh Monitors && Speakers", Color.FromArgb(48, 82, 138));
        btnRefreshDevices.Click += (_, _) =>
        {
            _engine.RefreshHardware();
            RebuildMonitorMappingsUI();
            _engine.Tick(forceReapply: true);
            _engine.Log("Refreshed monitors and audio endpoints.");
        };

        var btnForceSync = CreateButton("Force Re-Route All Now", Color.FromArgb(42, 115, 82));
        btnForceSync.Click += (_, _) =>
        {
            _engine.Tick(forceReapply: true);
            _engine.Log("Forced audio routing sync for all active apps.");
        };

        var btnResetDefaults = CreateButton("Reset All Apps to Windows Default", Color.FromArgb(110, 55, 55));
        btnResetDefaults.Click += (_, _) =>
        {
            if (AudioRouter.ClearAllPersistedAudioEndpoints())
            {
                _engine.Log("Cleared all per-app audio overrides (reset to Windows default).");
            }
        };

        var btnMinimizeTray = CreateButton("Minimize to System Tray", Color.FromArgb(62, 66, 80));
        btnMinimizeTray.Click += (_, _) => Hide();

        footerFlow.Controls.Add(btnRefreshDevices);
        footerFlow.Controls.Add(btnForceSync);
        footerFlow.Controls.Add(btnResetDefaults);
        footerFlow.Controls.Add(btnMinimizeTray);
        mainLayout.Controls.Add(footerFlow, 0, 4);

        Controls.Add(mainLayout);

        // Wire engine events
        _engine.OnLogMessage += line =>
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => AddLogLine(line));
            }
            else
            {
                AddLogLine(line);
            }
        };

        _engine.OnStatusUpdated += () =>
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(UpdateAppsListView);
            }
            else
            {
                UpdateAppsListView();
            }
        };

        RebuildMonitorMappingsUI();
        _engine.Log("ScreenSoundRouter initialized and monitoring window locations.");

        // Start polling timer
        _pollTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(250, _engine.Config.PollIntervalMs)
        };
        _pollTimer.Tick += (_, _) => _engine.Tick();
        _pollTimer.Start();

        // Perform initial tick right away
        _engine.Tick(forceReapply: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startMinimized)
        {
            Hide();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(
                2000,
                "ScreenSoundRouter is still running",
                "Audio routing continues in the background. Double-click the tray icon to open.",
                ToolTipIcon.Info);
            return;
        }
        _pollTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _allowExit = true;
        Close();
        Application.Exit();
    }

    private void RebuildMonitorMappingsUI()
    {
        _mappingsPanel.SuspendLayout();
        _mappingsPanel.Controls.Clear();

        foreach (var mon in _engine.Monitors)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 4, 0, 4),
                WrapContents = false
            };

            string primaryTag = mon.IsPrimary ? " [Primary]" : "";
            var lblMon = new Label
            {
                Text = $"Monitor {mon.GdiDeviceName.Replace(@"\\.\DISPLAY", "#")}: {mon.FriendlyName} ({mon.Bounds.Width}×{mon.Bounds.Height}){primaryTag}",
                Width = 340,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(235, 238, 245),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Margin = new Padding(0, 5, 12, 0)
            };

            var cmbAudio = new ComboBox
            {
                Width = 420,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(38, 42, 52),
                ForeColor = Color.FromArgb(235, 238, 245),
                FlatStyle = FlatStyle.Flat
            };

            cmbAudio.Items.Add(new AudioComboItem("", "— Do Not Route on This Monitor —"));
            int selectedIndex = 0;

            _engine.Config.MonitorToAudioEndpoint.TryGetValue(mon.GdiDeviceName, out string? configuredId);

            for (int i = 0; i < _engine.AudioDevices.Count; i++)
            {
                var dev = _engine.AudioDevices[i];
                cmbAudio.Items.Add(new AudioComboItem(dev.Id, dev.FriendlyName));
                if (!string.IsNullOrWhiteSpace(configuredId) &&
                    dev.Id.Equals(configuredId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i + 1;
                }
            }

            cmbAudio.SelectedIndex = selectedIndex;
            string gdiKey = mon.GdiDeviceName;
            string monLabel = mon.FriendlyName;

            cmbAudio.SelectedIndexChanged += (_, _) =>
            {
                if (cmbAudio.SelectedItem is AudioComboItem item)
                {
                    _engine.Config.MonitorToAudioEndpoint[gdiKey] = item.Id;
                    _engine.Config.Save();
                    _engine.Log($"Mapped monitor '{monLabel}' ({gdiKey}) -> {item.DisplayName}");
                    _engine.Tick(forceReapply: true);
                }
            };

            row.Controls.Add(lblMon);
            row.Controls.Add(cmbAudio);
            _mappingsPanel.Controls.Add(row);
        }

        _mappingsPanel.ResumeLayout();
    }

    private void UpdateAppsListView()
    {
        var statuses = _engine.CurrentStatuses;

        _appsListView.BeginUpdate();
        _appsListView.Items.Clear();

        foreach (var st in statuses)
        {
            var item = new ListViewItem(st.ProcessName);
            item.SubItems.Add(st.ProcessId.ToString());
            item.SubItems.Add(st.WindowTitle);
            item.SubItems.Add(st.MonitorFriendlyName);
            item.SubItems.Add(st.AudioEndpointName);

            if (st.IsActiveAudio)
            {
                item.ForeColor = Color.FromArgb(130, 240, 170);
            }
            else
            {
                item.ForeColor = Color.FromArgb(180, 186, 200);
            }

            _appsListView.Items.Add(item);
        }

        _appsListView.EndUpdate();
    }

    private void AddLogLine(string line)
    {
        _logListBox.Items.Add(line);
        while (_logListBox.Items.Count > 200)
        {
            _logListBox.Items.RemoveAt(0);
        }
        _logListBox.TopIndex = _logListBox.Items.Count - 1;
    }

    private static Button CreateButton(string text, Color backColor)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static Icon CreateSpeakerIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bgBrush = new SolidBrush(Color.FromArgb(35, 115, 225));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        Point[] speakerCone =
        {
            new(8, 13),
            new(13, 13),
            new(19, 8),
            new(19, 24),
            new(13, 19),
            new(8, 19)
        };
        g.FillPolygon(Brushes.White, speakerCone);

        using var wavePen = new Pen(Color.White, 2.2f);
        g.DrawArc(wavePen, 18, 10, 6, 12, -55, 110);
        g.DrawArc(wavePen, 20, 7, 9, 18, -55, 110);

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
