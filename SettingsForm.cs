using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Settings window for LumiDisplay.
/// Allows configuring hotkeys, step sizes, startup preferences, and testing monitor adjustments.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Color ThemeBg = Color.FromArgb(20, 18, 30);
    private static readonly Color ThemeSurface = Color.FromArgb(28, 25, 42);
    private static readonly Color ThemeControl = Color.FromArgb(40, 36, 60);
    private static readonly Color ThemeBorder = Color.FromArgb(60, 54, 85);
    private static readonly Color ThemeText = Color.FromArgb(235, 240, 250);
    private static readonly Color ThemeTextMuted = Color.FromArgb(150, 155, 178);
    private static readonly Color AccentCyan = Color.FromArgb(6, 182, 212);
    private static readonly Color AccentGold = Color.FromArgb(245, 158, 11);
    private static readonly Color AccentViolet = Color.FromArgb(168, 85, 247);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int useDarkMode = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch { }
    }

    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private readonly Func<IReadOnlyList<IMonitor>> _getMonitors;
    private readonly Action<OsdOverlay.OsdType, double> _showOsd;

    // Controls - General
    private CheckBox _chkStartup = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkInterceptVolume = null!;
    private CheckBox _chkForwardNative = null!;

    // Controls - Stepping
    private NumericUpDown _numTotalSteps = null!;
    private NumericUpDown _numStandardStep = null!;
    private NumericUpDown _numFineStep = null!;
    private CheckBox _chkFineVolumeDefault = null!;
    private CheckBox _chkFineVideoDefault = null!;
    private Label _lblStepSummary = null!;

    // Controls - Hotkeys
    private CheckBox _chkBrightnessHotkeys = null!;
    private CheckBox _chkContrastHotkeys = null!;

    // Controls - Monitors & Testing
    private ComboBox _cboTargetMonitor = null!;
    private Label _lblMonitorDetails = null!;
    private TrackBar _tbTestBrightness = null!;
    private Label _lblTestBrightness = null!;
    private TrackBar _tbTestVolume = null!;
    private Label _lblTestVolume = null!;

    public SettingsForm(
        AppSettings settings,
        Action onSettingsChanged,
        Func<IReadOnlyList<IMonitor>> getMonitors,
        Action<OsdOverlay.OsdType, double> showOsd)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _getMonitors = getMonitors;
        _showOsd = showOsd;

        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "LumiDisplay — Display & Audio Settings";
        Size = new Size(560, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        BackColor = ThemeBg;
        ForeColor = ThemeText;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Top,
            Height = 380,
            Padding = new Point(14, 6),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(125, 32)
        };

        tabControl.DrawItem += (s, e) =>
        {
            var g = e.Graphics;
            var tabRect = tabControl.GetTabRect(e.Index);
            bool isSelected = tabControl.SelectedIndex == e.Index;

            using var bgBrush = new SolidBrush(isSelected ? ThemeSurface : ThemeBg);
            g.FillRectangle(bgBrush, tabRect);

            if (isSelected)
            {
                using var activePen = new Pen(AccentCyan, 2.5f);
                g.DrawLine(activePen, tabRect.Left + 6, tabRect.Bottom - 2, tabRect.Right - 6, tabRect.Bottom - 2);
            }

            string title = tabControl.TabPages[e.Index].Text;
            Color textColor = isSelected ? AccentCyan : ThemeTextMuted;
            TextRenderer.DrawText(g, title, tabControl.Font, tabRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // Tab 1: General
        var tabGeneral = new TabPage("General") { BackColor = ThemeSurface, ForeColor = ThemeText };
        BuildGeneralTab(tabGeneral);
        tabControl.TabPages.Add(tabGeneral);

        // Tab 2: Stepping & OSD
        var tabStepping = new TabPage("Stepping & OSD") { BackColor = ThemeSurface, ForeColor = ThemeText };
        BuildSteppingTab(tabStepping);
        tabControl.TabPages.Add(tabStepping);

        // Tab 3: Hotkeys
        var tabHotkeys = new TabPage("Hotkeys") { BackColor = ThemeSurface, ForeColor = ThemeText };
        BuildHotkeysTab(tabHotkeys);
        tabControl.TabPages.Add(tabHotkeys);

        // Tab 4: Monitors & Test
        var tabMonitors = new TabPage("Monitors & Test") { BackColor = ThemeSurface, ForeColor = ThemeText };
        BuildMonitorsTab(tabMonitors);
        tabControl.TabPages.Add(tabMonitors);

        // Bottom panel for buttons
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12),
            BackColor = ThemeBg
        };

        var btnSave = new Button
        {
            Text = "Save & Apply",
            DialogResult = DialogResult.OK,
            Size = new Size(120, 32),
            Location = new Point(Width - 250, 10),
            BackColor = AccentCyan,
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font(Font, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (_, _) =>
        {
            SaveSettings();
            Close();
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(95, 32),
            Location = new Point(Width - 118, 10),
            BackColor = ThemeControl,
            ForeColor = ThemeText,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderColor = ThemeBorder;
        btnCancel.Click += (_, _) => Close();

        bottomPanel.Controls.Add(btnSave);
        bottomPanel.Controls.Add(btnCancel);

        Controls.Add(tabControl);
        Controls.Add(bottomPanel);
    }

    private void BuildGeneralTab(TabPage page)
    {
        page.Padding = new Padding(16);

        _chkStartup = new CheckBox
        {
            Text = "Start with Windows (Launch automatically on login)",
            AutoSize = true,
            Location = new Point(20, 20),
            ForeColor = ThemeText
        };

        _chkStartMinimized = new CheckBox
        {
            Text = "Start minimized to system tray",
            AutoSize = true,
            Location = new Point(20, 55),
            ForeColor = ThemeText
        };

        var grpMedia = new GroupBox
        {
            Text = "Media Keys Interception",
            Location = new Point(15, 95),
            Size = new Size(500, 120),
            Padding = new Padding(12),
            ForeColor = AccentGold,
            BackColor = ThemeSurface
        };

        _chkInterceptVolume = new CheckBox
        {
            Text = "Intercept native media volume keys (Volume Up, Down, Mute)",
            AutoSize = true,
            Location = new Point(15, 25),
            ForeColor = ThemeText
        };

        _chkForwardNative = new CheckBox
        {
            Text = "Forward to system audio when active device supports native volume",
            AutoSize = true,
            Location = new Point(15, 55),
            ForeColor = ThemeText
        };

        var lblNote = new Label
        {
            Text = "When enabled, pressing keyboard volume keys will adjust external monitor speaker volume via DDC/CI (VCP 0x62) instead of Windows system volume.",
            ForeColor = ThemeTextMuted,
            Location = new Point(15, 82),
            Size = new Size(470, 32)
        };

        grpMedia.Controls.Add(_chkInterceptVolume);
        grpMedia.Controls.Add(_chkForwardNative);
        grpMedia.Controls.Add(lblNote);

        page.Controls.Add(_chkStartup);
        page.Controls.Add(_chkStartMinimized);
        page.Controls.Add(grpMedia);
    }

    private void BuildSteppingTab(TabPage page)
    {
        page.Padding = new Padding(16);

        var lblTotal = new Label { Text = "Total logical steps:", Location = new Point(20, 22), AutoSize = true, ForeColor = ThemeText };
        _numTotalSteps = new NumericUpDown
        {
            Location = new Point(220, 20),
            Size = new Size(70, 25),
            Minimum = 16,
            Maximum = 256,
            Value = 64,
            BackColor = ThemeControl,
            ForeColor = ThemeText
        };
        _numTotalSteps.ValueChanged += (_, _) => UpdateStepSummary();

        var lblStandard = new Label { Text = "Standard step size (steps):", Location = new Point(20, 57), AutoSize = true, ForeColor = ThemeText };
        _numStandardStep = new NumericUpDown
        {
            Location = new Point(220, 55),
            Size = new Size(70, 25),
            Minimum = 1,
            Maximum = 64,
            Value = 4,
            BackColor = ThemeControl,
            ForeColor = ThemeText
        };
        _numStandardStep.ValueChanged += (_, _) => UpdateStepSummary();

        var lblFine = new Label { Text = "Fine step size (steps):", Location = new Point(20, 92), AutoSize = true, ForeColor = ThemeText };
        _numFineStep = new NumericUpDown
        {
            Location = new Point(220, 90),
            Size = new Size(70, 25),
            Minimum = 1,
            Maximum = 16,
            Value = 1,
            BackColor = ThemeControl,
            ForeColor = ThemeText
        };
        _numFineStep.ValueChanged += (_, _) => UpdateStepSummary();

        _chkFineVolumeDefault = new CheckBox
        {
            Text = "Use fine scale by default for volume media keys",
            AutoSize = true,
            Location = new Point(20, 130),
            ForeColor = ThemeText
        };

        _chkFineVideoDefault = new CheckBox
        {
            Text = "Use fine scale by default for brightness hotkeys",
            AutoSize = true,
            Location = new Point(20, 160),
            ForeColor = ThemeText
        };

        _lblStepSummary = new Label
        {
            Location = new Point(20, 200),
            Size = new Size(480, 85),
            ForeColor = AccentCyan,
            BackColor = ThemeControl,
            Padding = new Padding(8)
        };

        page.Controls.Add(lblTotal);
        page.Controls.Add(_numTotalSteps);
        page.Controls.Add(lblStandard);
        page.Controls.Add(_numStandardStep);
        page.Controls.Add(lblFine);
        page.Controls.Add(_numFineStep);
        page.Controls.Add(_chkFineVolumeDefault);
        page.Controls.Add(_chkFineVideoDefault);
        page.Controls.Add(_lblStepSummary);

        UpdateStepSummary();
    }

    private void UpdateStepSummary()
    {
        int total = (int)_numTotalSteps.Value;
        int standard = (int)_numStandardStep.Value;
        int fine = (int)_numFineStep.Value;

        double stdPct = (double)standard / total * 100.0;
        double finePct = (double)fine / total * 100.0;
        int chiclets = total / 4;

        _lblStepSummary.Text =
            $"Configuration Summary:\n" +
            $"• {total} total steps (simulates {chiclets} OSD chiclets with {total / Math.Max(1, chiclets)} steps each)\n" +
            $"• Standard step: {standard} logical steps = {stdPct:F2}% per press (1 chiclet)\n" +
            $"• Fine step: {fine} logical steps = {finePct:F2}% per press (0.25 chiclet)\n" +
            $"• Matches BetterDisplay's native macOS stepping behaviour.";
    }

    private void BuildHotkeysTab(TabPage page)
    {
        page.Padding = new Padding(16);

        _chkBrightnessHotkeys = new CheckBox
        {
            Text = "Enable Brightness Hotkeys",
            AutoSize = true,
            Location = new Point(20, 20),
            ForeColor = ThemeText
        };

        var lblBrightKeys = new Label
        {
            Text = "• Ctrl + Alt + Up: Brightness Up (Standard Step)\n" +
                   "• Ctrl + Alt + Down: Brightness Down (Standard Step)\n" +
                   "• Ctrl + Alt + Shift + Up: Brightness Up (Fine Step)\n" +
                   "• Ctrl + Alt + Shift + Down: Brightness Down (Fine Step)",
            Location = new Point(40, 48),
            Size = new Size(460, 75),
            ForeColor = ThemeTextMuted
        };

        _chkContrastHotkeys = new CheckBox
        {
            Text = "Enable Contrast Hotkeys",
            AutoSize = true,
            Location = new Point(20, 135),
            ForeColor = ThemeText
        };

        var lblContrastKeys = new Label
        {
            Text = "• Ctrl + Alt + Right: Contrast Up\n" +
                   "• Ctrl + Alt + Left: Contrast Down",
            Location = new Point(40, 163),
            Size = new Size(460, 45),
            ForeColor = ThemeTextMuted
        };

        var lblVolumeKeys = new Label
        {
            Text = "Media Keys (when Intercept Volume Keys is enabled):\n" +
                   "• Volume Up: Speaker Volume Up\n" +
                   "• Volume Down: Speaker Volume Down\n" +
                   "• Volume Mute: Speaker Mute Toggle",
            Location = new Point(20, 220),
            Size = new Size(480, 60),
            ForeColor = AccentCyan
        };

        page.Controls.Add(_chkBrightnessHotkeys);
        page.Controls.Add(lblBrightKeys);
        page.Controls.Add(_chkContrastHotkeys);
        page.Controls.Add(lblContrastKeys);
        page.Controls.Add(lblVolumeKeys);
    }

    private void BuildMonitorsTab(TabPage page)
    {
        page.Padding = new Padding(16);

        var lblTarget = new Label { Text = "Target Monitor:", Location = new Point(20, 18), AutoSize = true, ForeColor = ThemeText };
        _cboTargetMonitor = new ComboBox
        {
            Location = new Point(130, 15),
            Size = new Size(360, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeControl,
            ForeColor = ThemeText,
            FlatStyle = FlatStyle.Flat
        };
        _cboTargetMonitor.SelectedIndexChanged += (_, _) => UpdateMonitorDetails();

        _lblMonitorDetails = new Label
        {
            Location = new Point(20, 50),
            Size = new Size(470, 70),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            BackColor = ThemeControl,
            ForeColor = ThemeText
        };

        var grpTest = new GroupBox
        {
            Text = "Live Adjustment Test",
            Location = new Point(15, 130),
            Size = new Size(485, 140),
            Padding = new Padding(12),
            ForeColor = AccentGold,
            BackColor = ThemeSurface
        };

        var lblB = new Label { Text = "Brightness:", Location = new Point(15, 25), AutoSize = true, ForeColor = ThemeText };
        _tbTestBrightness = new TrackBar
        {
            Location = new Point(90, 20),
            Size = new Size(320, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            BackColor = ThemeSurface
        };
        _lblTestBrightness = new Label { Text = "50%", Location = new Point(420, 25), AutoSize = true, ForeColor = AccentGold };
        _tbTestBrightness.Scroll += (_, _) =>
        {
            _lblTestBrightness.Text = $"{_tbTestBrightness.Value}%";
            ApplyTestBrightness((uint)_tbTestBrightness.Value);
        };

        var lblV = new Label { Text = "Volume:", Location = new Point(15, 75), AutoSize = true, ForeColor = ThemeText };
        _tbTestVolume = new TrackBar
        {
            Location = new Point(90, 70),
            Size = new Size(320, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            BackColor = ThemeSurface
        };
        _lblTestVolume = new Label { Text = "50%", Location = new Point(420, 75), AutoSize = true, ForeColor = AccentCyan };
        _tbTestVolume.Scroll += (_, _) =>
        {
            _lblTestVolume.Text = $"{_tbTestVolume.Value}%";
            ApplyTestVolume((uint)_tbTestVolume.Value);
        };

        grpTest.Controls.Add(lblB);
        grpTest.Controls.Add(_tbTestBrightness);
        grpTest.Controls.Add(_lblTestBrightness);
        grpTest.Controls.Add(lblV);
        grpTest.Controls.Add(_tbTestVolume);
        grpTest.Controls.Add(_lblTestVolume);

        page.Controls.Add(lblTarget);
        page.Controls.Add(_cboTargetMonitor);
        page.Controls.Add(_lblMonitorDetails);
        page.Controls.Add(grpTest);
    }

    private void LoadSettings()
    {
        _chkStartup.Checked = StartupManager.IsStartupEnabled();
        _chkStartMinimized.Checked = _settings.StartMinimized;
        _chkInterceptVolume.Checked = _settings.InterceptMediaVolumeKeys;
        _chkForwardNative.Checked = _settings.ForwardToNativeWhenSupported;

        _numTotalSteps.Value = Math.Clamp(_settings.TotalSteps, 16, 256);
        _numStandardStep.Value = Math.Clamp(_settings.StandardStep, 1, 64);
        _numFineStep.Value = Math.Clamp(_settings.FineStep, 1, 16);
        _chkFineVolumeDefault.Checked = _settings.AudioMediaKeyFineControlByDefault;
        _chkFineVideoDefault.Checked = _settings.VideoMediaKeyFineControlByDefault;

        _chkBrightnessHotkeys.Checked = _settings.EnableBrightnessHotkeys;
        _chkContrastHotkeys.Checked = _settings.EnableContrastHotkeys;

        PopulateMonitorsList();
    }

    private void PopulateMonitorsList()
    {
        _cboTargetMonitor.Items.Clear();
        _cboTargetMonitor.Items.Add("All Monitors (Synchronized)");

        var monitors = _getMonitors();
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string type = m.IsBuiltIn ? "[WMI Built-in]" : "[DDC/CI]";
            _cboTargetMonitor.Items.Add($"{i + 1}. {m.Description} {type}");
        }

        if (_settings.SelectedMonitorIndex >= 0 && _settings.SelectedMonitorIndex < monitors.Count)
            _cboTargetMonitor.SelectedIndex = _settings.SelectedMonitorIndex + 1;
        else
            _cboTargetMonitor.SelectedIndex = 0;

        UpdateMonitorDetails();
    }

    private void UpdateMonitorDetails()
    {
        int sel = _cboTargetMonitor.SelectedIndex;
        var monitors = _getMonitors();

        if (sel <= 0 || sel - 1 >= monitors.Count)
        {
            _lblMonitorDetails.Text =
                $"Controlling {monitors.Count} connected monitor(s).\n" +
                $"Adjustments will be synchronized across all capable displays.";
            return;
        }

        var m = monitors[sel - 1];
        var bright = m.GetBrightness();
        var vol = m.GetVolume();
        var contrast = m.GetContrast();

        string brightStr = bright.HasValue ? $"{bright.Value.current}/{bright.Value.max} ({bright.Value.current * 100 / Math.Max(1, bright.Value.max)}%)" : "Unsupported";
        string volStr = vol.HasValue ? $"{vol.Value.current}/{vol.Value.max} ({vol.Value.current * 100 / Math.Max(1, vol.Value.max)}%)" : "Unsupported";
        string contrastStr = contrast.HasValue ? $"{contrast.Value.current}/{contrast.Value.max}" : "Unsupported";

        _lblMonitorDetails.Text =
            $"Name: {m.Description}\n" +
            $"Type: {(m.IsBuiltIn ? "Built-in panel (WMI)" : "External monitor (DDC/CI)")}\n" +
            $"Brightness: {brightStr} | Volume: {volStr} | Contrast: {contrastStr}";

        if (bright.HasValue && bright.Value.max > 0)
        {
            _tbTestBrightness.Value = (int)(bright.Value.current * 100 / bright.Value.max);
            _lblTestBrightness.Text = $"{_tbTestBrightness.Value}%";
        }
        if (vol.HasValue && vol.Value.max > 0)
        {
            _tbTestVolume.Value = (int)(vol.Value.current * 100 / vol.Value.max);
            _lblTestVolume.Text = $"{_tbTestVolume.Value}%";
        }
    }

    private void ApplyTestBrightness(uint percent)
    {
        int sel = _cboTargetMonitor.SelectedIndex;
        var monitors = _getMonitors();

        var targets = (sel <= 0 || sel - 1 >= monitors.Count)
            ? (IEnumerable<IMonitor>)monitors
            : new[] { monitors[sel - 1] };

        foreach (var m in targets)
        {
            var cur = m.GetBrightness();
            if (cur.HasValue)
            {
                uint val = (uint)(cur.Value.max * percent / 100);
                m.SetBrightness(val);
            }
        }
        _showOsd(OsdOverlay.OsdType.Brightness, percent / 100.0);
    }

    private void ApplyTestVolume(uint percent)
    {
        int sel = _cboTargetMonitor.SelectedIndex;
        var monitors = _getMonitors();

        var targets = (sel <= 0 || sel - 1 >= monitors.Count)
            ? (IEnumerable<IMonitor>)monitors
            : new[] { monitors[sel - 1] };

        foreach (var m in targets)
        {
            var cur = m.GetVolume();
            if (cur.HasValue)
            {
                uint val = (uint)(cur.Value.max * percent / 100);
                m.SetVolume(val);
            }
        }
        _showOsd(OsdOverlay.OsdType.Volume, percent / 100.0);
    }

    private void SaveSettings()
    {
        // Startup registry
        if (_chkStartup.Checked != StartupManager.IsStartupEnabled())
        {
            StartupManager.SetStartup(_chkStartup.Checked);
        }

        _settings.StartWithWindows = _chkStartup.Checked;
        _settings.StartMinimized = _chkStartMinimized.Checked;
        _settings.InterceptMediaVolumeKeys = _chkInterceptVolume.Checked;
        _settings.ForwardToNativeWhenSupported = _chkForwardNative.Checked;

        _settings.TotalSteps = (int)_numTotalSteps.Value;
        _settings.StandardStep = (int)_numStandardStep.Value;
        _settings.FineStep = (int)_numFineStep.Value;
        _settings.AudioMediaKeyFineControlByDefault = _chkFineVolumeDefault.Checked;
        _settings.VideoMediaKeyFineControlByDefault = _chkFineVideoDefault.Checked;

        _settings.EnableBrightnessHotkeys = _chkBrightnessHotkeys.Checked;
        _settings.EnableContrastHotkeys = _chkContrastHotkeys.Checked;

        int sel = _cboTargetMonitor.SelectedIndex;
        _settings.SelectedMonitorIndex = (sel > 0) ? (sel - 1) : -1;

        _settings.Save();
        _onSettingsChanged();
    }
}
