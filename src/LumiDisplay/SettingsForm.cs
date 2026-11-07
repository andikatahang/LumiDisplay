using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Settings window for LumiDisplay.
/// Allows configuring hotkeys, step sizes, startup preferences, and testing monitor adjustments.
/// Light theme: white sidebar and cards on a soft-gray page, dark blue accent.
/// </summary>
public sealed class SettingsForm : Form, IGlassHost
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    // uxtheme #135 SetPreferredAppMode (undocumented, Windows 10 1903+): 2 = ForceDark, so common controls pick their dark theme
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const int SidebarWidth = 224;
    private const int ContentWidth = 636;
    private const int HeaderHeight = 84;
    private const int FooterHeight = 68;
    private const int CardMargin = 28;

    // --- Liquid glass backdrop ---

    private readonly GlassBackdrop _backdrop = new();
    GlassBackdrop IGlassHost.Backdrop => _backdrop;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        RefreshGlass();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshGlass(); // final position is known now
    }

    // Moving or resizing ends here; the glass shows the wallpaper under the window's new spot
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        RefreshGlass();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        RefreshGlass();
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.Desktop || IsDisposed) return;
        Glass.InvalidateWallpaper();
        BeginInvoke(new Action(RefreshGlass));
    }

    private void RefreshGlass()
    {
        if (!IsHandleCreated || IsDisposed) return;
        _backdrop.Rebuild(this);

        // Windows 11: tint the title bar with the glass color so it reads as one surface
        try
        {
            int caption = ColorTranslator.ToWin32(_backdrop.TopColor);
            int text = ColorTranslator.ToWin32(Theme.Text);
            int border = ColorTranslator.ToWin32(Theme.Border);
            int dark = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch { }
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_backdrop.Base != null)
            e.Graphics.DrawImage(_backdrop.Base, 0, 0);
        else
            e.Graphics.Clear(Theme.Window);
    }

    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private readonly Func<IReadOnlyList<IMonitor>> _getMonitors;
    private readonly Action<OsdOverlay.OsdType, double> _showOsd;
    private readonly Action? _onQuit;

    /// <summary>True when the window is closing because the user chose "Quit LumiDisplay".</summary>
    public bool QuitRequested { get; private set; }

    // Navigation
    private readonly List<NavItem> _navItems = new();
    private readonly List<Panel> _pages = new();
    private readonly List<(string title, string subtitle)> _pageHeaders = new();
    private Label _lblPageTitle = null!;
    private Label _lblPageSubtitle = null!;
    private Label _lblMonitorStatus = null!;

    // Controls - General
    private ToggleSwitch _chkStartup = null!;
    private ToggleSwitch _chkStartMinimized = null!;
    private ToggleSwitch _chkInterceptVolume = null!;
    private ToggleSwitch _chkForwardNative = null!;

    // Controls - Stepping
    private Stepper _numStepPercent = null!;
    private Stepper _numFineStepPercent = null!;
    private ToggleSwitch _chkSyncContrast = null!;
    private ToggleSwitch _chkFineVolumeDefault = null!;
    private ToggleSwitch _chkFineVideoDefault = null!;
    private StepPreview _stepPreview = null!;

    // Controls - Hotkeys
    private ToggleSwitch _chkBrightnessHotkeys = null!;
    private ToggleSwitch _chkContrastHotkeys = null!;
    private ToggleSwitch _chkVolumeHotkeys = null!;
    private readonly Dictionary<HotkeyManager.HotkeyAction, ShortcutRecorder> _recorders = new();

    // Controls - Monitors & Testing
    private ComboBox _cboTargetMonitor = null!;
    private StatTile _tileType = null!;
    private StatTile _tileBrightness = null!;
    private StatTile _tileVolume = null!;
    private StatTile _tileContrast = null!;
    private SliderBar _tbTestBrightness = null!;
    private Label _lblTestBrightness = null!;
    private SliderBar _tbTestVolume = null!;
    private Label _lblTestVolume = null!;

    public SettingsForm(
        AppSettings settings,
        Action onSettingsChanged,
        Func<IReadOnlyList<IMonitor>> getMonitors,
        Action<OsdOverlay.OsdType, double> showOsd,
        Action? onQuit = null,
        Func<IReadOnlyCollection<HotkeyManager.HotkeyAction>>? getFailedHotkeys = null,
        Action<bool>? suspendHotkeys = null,
        DisplayWorker? worker = null)
    {
        _worker = worker;
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _getMonitors = getMonitors;
        _showOsd = showOsd;
        _onQuit = onQuit;
        _getFailedHotkeys = getFailedHotkeys;
        _suspendHotkeys = suspendHotkeys;

        InitializeComponent();
        LoadSettings();
        HookChangeTracking();
        _savedState = CurrentState();
        UpdateDirtyState();
        SelectPage(0);
    }

    // --- Unsaved-changes tracking ---

    private RoundedButton _btnSave = null!;
    private Label _lblSaveState = null!;
    private readonly System.Windows.Forms.Timer _savedFlashTimer = new() { Interval = 2500 };
    private string? _savedState;

    private bool IsDirty => _savedState != null && CurrentState() != _savedState;

    /// <summary>Everything Save writes, as one comparable string.</summary>
    private string CurrentState()
    {
        var parts = new List<string>
        {
            $"{_chkStartup.Checked}|{_chkStartMinimized.Checked}|{_chkInterceptVolume.Checked}|{_chkForwardNative.Checked}",
            $"{_numStepPercent.Value}|{_numFineStepPercent.Value}|{_chkSyncContrast.Checked}",
            $"{_chkFineVolumeDefault.Checked}|{_chkFineVideoDefault.Checked}",
            $"{_chkBrightnessHotkeys.Checked}|{_chkContrastHotkeys.Checked}|{_chkVolumeHotkeys.Checked}",
            $"{_cboTargetMonitor.SelectedIndex}"
        };
        foreach (var (action, recorder) in _recorders)
            parts.Add($"{action}={recorder.Modifiers}:{(int)recorder.Key}");
        return string.Join(";", parts);
    }

    private void HookChangeTracking()
    {
        foreach (var toggle in new[]
                 {
                     _chkStartup, _chkStartMinimized, _chkInterceptVolume, _chkForwardNative, _chkSyncContrast,
                     _chkFineVolumeDefault, _chkFineVideoDefault, _chkBrightnessHotkeys, _chkContrastHotkeys, _chkVolumeHotkeys
                 })
            toggle.CheckedChanged += (_, _) => UpdateDirtyState();

        _numStepPercent.ValueChanged += (_, _) => UpdateDirtyState();
        _numFineStepPercent.ValueChanged += (_, _) => UpdateDirtyState();
        _cboTargetMonitor.SelectedIndexChanged += (_, _) => UpdateDirtyState();
        foreach (var recorder in _recorders.Values)
            recorder.BindingChanged += (_, _) => UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        if (_savedState == null) return; // still loading
        bool dirty = IsDirty;
        _btnSave.Enabled = dirty;

        if (dirty)
        {
            _savedFlashTimer.Stop();
            _lblSaveState.Text = "Unsaved changes";
            _lblSaveState.ForeColor = Theme.Accent;
        }
        else if (!_savedFlashTimer.Enabled)
        {
            _lblSaveState.Text = "";
        }
    }

    /// <summary>Saves and stays open; the button disables again until the next change.</summary>
    private bool SaveChanges()
    {
        if (!IsDirty) return true;
        SaveSettings();
        _savedState = CurrentState();

        // Shortcuts another app owns show up red right away
        var failed = _getFailedHotkeys?.Invoke() ?? Array.Empty<HotkeyManager.HotkeyAction>();
        foreach (var (action, recorder) in _recorders)
            recorder.Unavailable = failed.Contains(action);

        _lblSaveState.Text = "✓ Changes saved";
        _lblSaveState.ForeColor = Theme.Success;
        _savedFlashTimer.Stop();
        _savedFlashTimer.Start();
        UpdateDirtyState();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && IsDirty)
        {
            var answer = MessageBox.Show(this,
                "You have unsaved changes. Save them before closing?",
                "LumiDisplay", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes)
                SaveChanges();
        }
        base.OnFormClosing(e);
    }

    private readonly Func<IReadOnlyCollection<HotkeyManager.HotkeyAction>>? _getFailedHotkeys;
    private readonly DisplayWorker? _worker;
    private readonly Action<bool>? _suspendHotkeys;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Never leave the global shortcuts paused if the window closes mid-recording
        _suspendHotkeys?.Invoke(false);
        _savedFlashTimer.Dispose();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _backdrop.Dispose();
        base.OnFormClosed(e);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "LumiDisplay Settings";
        ClientSize = new Size(SidebarWidth + ContentWidth, 600);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        Font = Theme.Font(9F);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // Content area (right): header, scrollable pages, footer — all on the window's glass
        var header = new GlassPanel
        {
            Location = new Point(SidebarWidth, 0),
            Size = new Size(ContentWidth, HeaderHeight)
        };
        _lblPageTitle = new Label
        {
            Location = new Point(CardMargin - 1, 22),
            AutoSize = true,
            Font = Theme.SemiboldFont(15F),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        _lblPageSubtitle = new Label
        {
            Location = new Point(CardMargin, 54),
            AutoSize = true,
            Font = Theme.Font(9F),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent
        };
        header.Controls.Add(_lblPageTitle);
        header.Controls.Add(_lblPageSubtitle);

        AddPage("General", Glyph.Gear, "General", "Startup behaviour and media key handling.", BuildGeneralTab);
        AddPage("Stepping & OSD", Glyph.Sliders, "Stepping & OSD", "How far each key press moves brightness and volume.", BuildSteppingTab);
        AddPage("Hotkeys", Glyph.Keyboard, "Hotkeys", "Global keyboard shortcuts for your displays.", BuildHotkeysTab);
        AddPage("Monitors & Test", Glyph.Monitor, "Monitors & Test", "Pick a target display and try changes live.", BuildMonitorsTab);

        var footer = BuildFooter();
        var sidebar = BuildSidebar();

        Controls.Add(header);
        foreach (var page in _pages)
            Controls.Add(page);
        Controls.Add(footer);
        Controls.Add(sidebar);

        ResumeLayout(false);
    }

    // --- Shell ---

    private Panel BuildSidebar()
    {
        // A floating glass panel, inset from the window edges
        const int inset = 12;
        int width = SidebarWidth - inset * 2;
        int height = 600 - inset * 2;
        var sidebar = new GlassPanel
        {
            IsSurface = true,
            Location = new Point(inset, inset),
            Size = new Size(width, height)
        };
        sidebar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            float s = sidebar.DeviceDpi / 96f;
            Logo.Draw(g, new RectangleF(14 * s, 14 * s, 36 * s, 36 * s));

            using var nameFont = Theme.SemiboldFont(12F);
            using var tagFont = Theme.Font(8F);
            TextRenderer.DrawText(g, "LumiDisplay", nameFont, new Point((int)(60 * s), (int)(14 * s)), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "Display & audio control", tagFont, new Point((int)(61 * s), (int)(36 * s)), Theme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        };

        var lblSection = new Label
        {
            Text = "SETTINGS",
            Location = new Point(18, 74),
            AutoSize = true,
            Font = Theme.MediumFont(7.5F),
            ForeColor = Theme.TextFaint,
            BackColor = Color.Transparent
        };
        sidebar.Controls.Add(lblSection);

        for (int i = 0; i < _navItems.Count; i++)
        {
            var nav = _navItems[i];
            nav.Location = new Point(8, 94 + i * 44);
            nav.Width = width - 16;
            sidebar.Controls.Add(nav);
        }

        // Status block at the bottom of the sidebar
        var status = new GlassPanel
        {
            Location = new Point(8, height - 8 - 60),
            Size = new Size(width - 16, 60),
            Fill = Theme.Overlay(14),
            Outline = true
        };
        status.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = status.DeviceDpi / 96f;
            using var dot = new SolidBrush(_getMonitors().Count > 0 ? Theme.Success : Theme.TextFaint);
            g.FillEllipse(dot, 14 * s, 16 * s, 8 * s, 8 * s);
        };
        _lblMonitorStatus = new Label
        {
            Location = new Point(28, 11),
            AutoSize = true,
            Font = Theme.MediumFont(8.75F),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent
        };
        var version = typeof(SettingsForm).Assembly.GetName().Version;
        var lblVersion = new Label
        {
            Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}",
            Location = new Point(28, 32),
            AutoSize = true,
            Font = Theme.Font(8F),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent
        };
        status.Controls.Add(_lblMonitorStatus);
        status.Controls.Add(lblVersion);
        sidebar.Controls.Add(status);

        return sidebar;
    }

    private Panel BuildFooter()
    {
        var footer = new GlassPanel
        {
            Location = new Point(SidebarWidth, 600 - FooterHeight),
            Size = new Size(ContentWidth, FooterHeight)
        };
        footer.Paint += (_, e) =>
        {
            float s = footer.DeviceDpi / 96f;
            using var pen = new Pen(Glass.Divider, 1f);
            e.Graphics.DrawLine(pen, CardMargin * s, 0, footer.Width - CardMargin * s, 0);
        };

        // Closing the window keeps the app in the tray; this is the explicit way out
        var btnQuit = new RoundedButton
        {
            Text = "Quit LumiDisplay",
            Size = new Size(150, 38),
            Location = new Point(CardMargin, 15),
            BackColor = Theme.Surface
        };
        btnQuit.Click += (_, _) =>
        {
            QuitRequested = true;
            Close();
            if (!IsDisposed)
            {
                // The user chose to keep editing when asked about unsaved changes
                QuitRequested = false;
                return;
            }
            _onQuit?.Invoke();
        };

        // Enabled only while there are unsaved changes; saving keeps the window open
        _btnSave = new RoundedButton
        {
            Text = "Save changes",
            Primary = true,
            Size = new Size(132, 38),
            Location = new Point(ContentWidth - CardMargin - 132, 15),
            BackColor = Theme.Surface,
            Enabled = false
        };
        _btnSave.Click += (_, _) => SaveChanges();

        // Closing with unsaved changes asks first (see OnFormClosing)
        var btnClose = new RoundedButton
        {
            Text = "Close",
            Size = new Size(100, 38),
            Location = new Point(ContentWidth - CardMargin - 132 - 10 - 100, 15),
            BackColor = Theme.Surface
        };
        btnClose.Click += (_, _) => Close();

        _lblSaveState = new Label
        {
            AutoSize = false,
            Size = new Size(150, 38),
            Location = new Point(ContentWidth - CardMargin - 132 - 10 - 100 - 12 - 150, 15),
            TextAlign = ContentAlignment.MiddleRight,
            Font = Theme.Font(8.75F),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent
        };
        _savedFlashTimer.Tick += (_, _) =>
        {
            _savedFlashTimer.Stop();
            UpdateDirtyState();
        };

        if (_onQuit != null)
            footer.Controls.Add(btnQuit);
        footer.Controls.Add(_lblSaveState);
        footer.Controls.Add(btnClose);
        footer.Controls.Add(_btnSave);
        return footer;
    }

    private void AddPage(string navText, Glyph glyph, string title, string subtitle, Action<Panel> build)
    {
        int index = _pages.Count;
        var nav = new NavItem(navText, glyph);
        nav.Click += (_, _) => SelectPage(index);
        _navItems.Add(nav);

        // Scrolls with a slim overlay scrollbar; cards go into page.Content
        var page = new GlassScrollPanel
        {
            Location = new Point(SidebarWidth, HeaderHeight),
            Size = new Size(ContentWidth, 600 - HeaderHeight - FooterHeight),
            Visible = false
        };
        page.Content.Size = page.Size;
        build(page.Content);
        // Bottom room so the last card does not touch the footer when scrolled
        page.UpdateContentHeight(20);

        _pages.Add(page);
        _pageHeaders.Add((title, subtitle));
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navItems[i].Selected = i == index;
        }
        _lblPageTitle.Text = _pageHeaders[index].title;
        _lblPageSubtitle.Text = _pageHeaders[index].subtitle;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+1..4 switch pages
        int current = _pages.FindIndex(p => p.Visible);
        if (e.Control && e.KeyCode == Keys.Tab)
        {
            int next = (current + (e.Shift ? -1 : 1) + _pages.Count) % _pages.Count;
            SelectPage(next);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode < Keys.D1 + _pages.Count)
        {
            SelectPage(e.KeyCode - Keys.D1);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            SaveChanges();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    // --- Layout helpers ---

    private static Card AddCard(Panel page, string title, string description, ref int y, int contentHeight)
    {
        var card = new Card
        {
            Title = title,
            Description = description,
            Location = new Point(CardMargin, y),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Size = new Size(ContentWidth - CardMargin * 2, card.ContentTop + contentHeight + 8);
        page.Controls.Add(card);
        y += card.Height + 16;
        return card;
    }

    private static void AddRow(Card card, Control row, ref int y)
    {
        // Inset so rows never paint over the card's border, even after DPI rounding
        row.Location = new Point(2, y);
        row.Width = card.Width - 4;
        card.Controls.Add(row);
        y += row.Height;
    }

    private static Stepper CreateNumber(int min, int max, int value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Suffix = "%"
    };

    // --- Pages ---

    private void BuildGeneralTab(Panel page)
    {
        _chkStartup = new ToggleSwitch();
        _chkStartMinimized = new ToggleSwitch();
        _chkInterceptVolume = new ToggleSwitch();
        _chkForwardNative = new ToggleSwitch();

        int y = 4;
        var startup = AddCard(page, "Startup", "", ref y, 2 * 60);
        int ry = startup.ContentTop;
        AddRow(startup, new SettingRow("Start with Windows", "Launch LumiDisplay automatically when you sign in.", _chkStartup), ref ry);
        AddRow(startup, new SettingRow("Stay in tray at sign-in", "When started with Windows, don't open this window.", _chkStartMinimized), ref ry);

        var media = AddCard(page, "Media keys",
            "Volume keys change the Windows volume, so the taskbar slider moves with them.", ref y, 2 * 60);
        ry = media.ContentTop;
        AddRow(media, new SettingRow("Intercept volume keys", "Even 10% steps and LumiDisplay's on-screen display.", _chkInterceptVolume), ref ry);
        AddRow(media, new SettingRow("Forward to system audio", "Let Windows handle the keys when sound isn't playing through a monitor.", _chkForwardNative), ref ry);

        var btnResetAll = new RoundedButton
        {
            Text = "Reset all",
            Size = new Size(110, 34),
            BackColor = Theme.Surface
        };
        btnResetAll.Click += (_, _) => ResetAllToDefaults();
        var reset = AddCard(page, "Reset", "", ref y, 60);
        ry = reset.ContentTop;
        AddRow(reset, new SettingRow("Reset to defaults",
            "Restore every option and shortcut. Nothing changes until you save.", btnResetAll), ref ry);
    }

    /// <summary>
    /// Puts every control back to the out-of-the-box values. Like any other edit in this window,
    /// it only takes effect when the user clicks Save changes.
    /// </summary>
    private void ResetAllToDefaults()
    {
        var answer = MessageBox.Show(this,
            "Reset all LumiDisplay settings and shortcuts to their defaults?\n\n" +
            "Nothing is applied until you click Save changes.",
            "Reset to defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var defaults = new AppSettings();
        ApplyToControls(defaults, defaults.StartWithWindows, Array.Empty<HotkeyManager.HotkeyAction>());
        _cboTargetMonitor.SelectedIndex = 0;
    }

    private void BuildSteppingTab(Panel page)
    {
        _numStepPercent = CreateNumber(1, 50, 10);
        _numFineStepPercent = CreateNumber(1, 10, 2);
        _numStepPercent.ValueChanged += (_, _) => UpdateStepSummary();
        _numFineStepPercent.ValueChanged += (_, _) => UpdateStepSummary();
        _chkFineVolumeDefault = new ToggleSwitch();
        _chkFineVideoDefault = new ToggleSwitch();
        _chkSyncContrast = new ToggleSwitch();
        _stepPreview = new StepPreview();

        int y = 4;
        var steps = AddCard(page, "Step size",
            "Brightness, contrast and volume move in even steps that snap to the grid.", ref y, 2 * 60 + 92);
        int ry = steps.ContentTop;
        AddRow(steps, new SettingRow("Step (%)", "Each key press, e.g. 60% → 70% → 80%.", _numStepPercent), ref ry);
        AddRow(steps, new SettingRow("Fine step (%)", "With Shift held, or the fine shortcuts.", _numFineStepPercent), ref ry);
        AddRow(steps, _stepPreview, ref ry);

        var link = AddCard(page, "Brightness & contrast", "", ref y, 60);
        ry = link.ContentTop;
        AddRow(link, new SettingRow("Sync contrast with brightness",
            "Contrast moves along with brightness. Contrast keys still work on their own.", _chkSyncContrast), ref ry);

        var fine = AddCard(page, "Fine scale by default", "", ref y, 2 * 60);
        ry = fine.ContentTop;
        AddRow(fine, new SettingRow("Volume media keys", "Use the fine step for Volume Up and Down.", _chkFineVolumeDefault), ref ry);
        AddRow(fine, new SettingRow("Brightness hotkeys", "Swap standard and fine steps for brightness.", _chkFineVideoDefault), ref ry);

        UpdateStepSummary();
    }

    private void UpdateStepSummary()
    {
        int step = (int)_numStepPercent.Value;
        _stepPreview.SetSteps(step, (int)_numFineStepPercent.Value);
        // The live test sliders follow the same grid
        if (_tbTestBrightness != null) _tbTestBrightness.Step = step;
        if (_tbTestVolume != null) _tbTestVolume.Step = step;
    }

    private void BuildHotkeysTab(Panel page)
    {
        _chkBrightnessHotkeys = new ToggleSwitch();
        _chkContrastHotkeys = new ToggleSwitch();
        _chkVolumeHotkeys = new ToggleSwitch();

        // Instructions + reset
        var lblHelp = new Label
        {
            Text = "Click a shortcut, then press new keys. Backspace clears.",
            Location = new Point(CardMargin + 2, 10),
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent
        };
        var btnReset = new RoundedButton
        {
            Text = "Reset to defaults",
            Size = new Size(150, 32),
            Location = new Point(ContentWidth - CardMargin - 150, 2),
            BackColor = Theme.Window,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnReset.Click += (_, _) => ResetShortcutsToDefaults();
        page.Controls.Add(lblHelp);
        page.Controls.Add(btnReset);

        int y = 48;
        var bright = AddCard(page, "Brightness", "", ref y, 4 * 60);
        bright.HeaderControl = _chkBrightnessHotkeys;
        int ry = bright.ContentTop;
        AddShortcutRow(bright, HotkeyManager.HotkeyAction.BrightnessUp, "One step brighter.", ref ry);
        AddShortcutRow(bright, HotkeyManager.HotkeyAction.BrightnessDown, "One step dimmer.", ref ry);
        AddShortcutRow(bright, HotkeyManager.HotkeyAction.BrightnessFineUp, "A smaller step brighter.", ref ry);
        AddShortcutRow(bright, HotkeyManager.HotkeyAction.BrightnessFineDown, "A smaller step dimmer.", ref ry);

        var volume = AddCard(page, "Volume", "Optional extra keys for the Windows volume; the volume keys already work.", ref y, 5 * 60);
        volume.HeaderControl = _chkVolumeHotkeys;
        ry = volume.ContentTop;
        AddShortcutRow(volume, HotkeyManager.HotkeyAction.VolumeUp, "One step louder.", ref ry);
        AddShortcutRow(volume, HotkeyManager.HotkeyAction.VolumeDown, "One step quieter.", ref ry);
        AddShortcutRow(volume, HotkeyManager.HotkeyAction.VolumeFineUp, "A smaller step louder.", ref ry);
        AddShortcutRow(volume, HotkeyManager.HotkeyAction.VolumeFineDown, "A smaller step quieter.", ref ry);
        AddShortcutRow(volume, HotkeyManager.HotkeyAction.VolumeMute, "Mute or unmute the sound.", ref ry);

        var contrast = AddCard(page, "Contrast", "", ref y, 2 * 60);
        contrast.HeaderControl = _chkContrastHotkeys;
        ry = contrast.ContentTop;
        AddShortcutRow(contrast, HotkeyManager.HotkeyAction.ContrastUp, "One step more contrast.", ref ry);
        AddShortcutRow(contrast, HotkeyManager.HotkeyAction.ContrastDown, "One step less contrast.", ref ry);
    }

    private void AddShortcutRow(Card card, HotkeyManager.HotkeyAction action, string description, ref int y)
    {
        var recorder = new ShortcutRecorder
        {
            Validate = (mods, key) =>
            {
                foreach (var (other, r) in _recorders)
                {
                    if (other != action && r.HasBinding && r.Modifiers == mods && r.Key == key)
                        return $"Already used by {HotkeyManager.ActionName(other)}";
                }
                return null;
            }
        };
        recorder.BindingChanged += (_, _) => recorder.Unavailable = false;
        recorder.RecordingChanged += (_, recording) => _suspendHotkeys?.Invoke(recording);
        _recorders[action] = recorder;
        AddRow(card, new SettingRow(HotkeyManager.ActionName(action), description, recorder), ref y);
    }

    private void ResetShortcutsToDefaults()
    {
        foreach (var (action, recorder) in _recorders)
        {
            var binding = Array.Find(HotkeyManager.DefaultBindings, b => b.Action == action);
            recorder.SetBinding(binding?.Modifiers ?? 0, binding?.Key ?? Keys.None);
            recorder.Unavailable = false;
        }
    }

    private void BuildMonitorsTab(Panel page)
    {
        int y = 4;
        var target = AddCard(page, "Target display", "", ref y, 40 + 60 + 12);

        // Flat ComboBox has no border of its own; host it in a 12px-rounded surface frame on the glass
        var comboHost = new GlassPanel
        {
            Location = new Point(20, target.ContentTop - 3),
            Size = new Size(target.Width - 40 - 96 - 10, 36),
            Fill = Theme.Surface,
            Outline = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        comboHost.Paint += (_, e) =>
        {
            if (!_cboTargetMonitor.Focused) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = comboHost.DeviceDpi / 96f;
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, comboHost.Width - 1.5f, comboHost.Height - 1.5f), Theme.Radius * s);
            using var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1f);
            e.Graphics.DrawPath(pen, path);
        };
        comboHost.Resize += (_, _) => comboHost.Invalidate();

        _cboTargetMonitor = new ComboBox
        {
            Location = new Point(10, 4),
            Size = new Size(comboHost.Width - 20, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Standard,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 24,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _cboTargetMonitor.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var bg = new SolidBrush(selected ? Theme.SurfaceAlt : Theme.Surface);
            e.Graphics.FillRectangle(bg, e.Bounds);
            var textRect = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, _cboTargetMonitor.Items[e.Index]?.ToString(), _cboTargetMonitor.Font, textRect,
                selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        };
        // Windows' dark combo theme: dark frame, arrow and dropdown list instead of the light flat border
        _cboTargetMonitor.HandleCreated += (_, _) =>
        {
            try
            {
                SetPreferredAppMode(2);
                SetWindowTheme(_cboTargetMonitor.Handle, "DarkMode_CFD", null);
            }
            catch { } // older Windows: keeps the light combo frame
        };
        _cboTargetMonitor.SelectedIndexChanged += (_, _) => UpdateMonitorDetails();
        _cboTargetMonitor.GotFocus += (_, _) => comboHost.Invalidate();
        _cboTargetMonitor.LostFocus += (_, _) => comboHost.Invalidate();
        comboHost.Controls.Add(_cboTargetMonitor);

        var btnRefresh = new RoundedButton
        {
            Text = "Refresh",
            Size = new Size(96, 36),
            Location = new Point(target.Width - 20 - 96, target.ContentTop - 3),
            BackColor = Theme.Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnRefresh.Click += (_, _) => PopulateMonitorsList();

        int tileGap = 10;
        int tileW = (target.Width - 40 - tileGap * 3) / 4;
        int tileY = target.ContentTop + 44;
        _tileType = new StatTile("Type") { Location = new Point(20, tileY), Width = tileW };
        _tileBrightness = new StatTile("Brightness") { Location = new Point(20 + (tileW + tileGap), tileY), Width = tileW };
        _tileVolume = new StatTile("Speaker") { Location = new Point(20 + (tileW + tileGap) * 2, tileY), Width = tileW };
        _tileContrast = new StatTile("Contrast") { Location = new Point(20 + (tileW + tileGap) * 3, tileY), Width = tileW };

        target.Controls.Add(comboHost);
        target.Controls.Add(btnRefresh);
        target.Controls.Add(_tileType);
        target.Controls.Add(_tileBrightness);
        target.Controls.Add(_tileVolume);
        target.Controls.Add(_tileContrast);

        var test = AddCard(page, "Live adjustment", "Brightness and the monitor's own speaker level (DDC/CI), in real time.", ref y, 2 * 50 + 4);
        int ry = test.ContentTop;
        (_tbTestBrightness, _lblTestBrightness) = AddSliderRow(test, Glyph.Sun, "Brightness", ref ry);
        (_tbTestVolume, _lblTestVolume) = AddSliderRow(test, Glyph.Speaker, "Speaker", ref ry);

        _tbTestBrightness.Scroll += (_, _) =>
        {
            _lblTestBrightness.Text = $"{_tbTestBrightness.Value}%";
            ApplyTestBrightness((uint)_tbTestBrightness.Value);
        };
        _tbTestVolume.Scroll += (_, _) =>
        {
            _lblTestVolume.Text = $"{_tbTestVolume.Value}%";
            ApplyTestVolume((uint)_tbTestVolume.Value);
        };
    }

    private static (SliderBar slider, Label value) AddSliderRow(Card card, Glyph glyph, string caption, ref int y)
    {
        int rowY = y;
        var icon = new GlassPanel
        {
            Location = new Point(20, rowY + 9),
            Size = new Size(32, 32),
            Radius = 10,
            Fill = Theme.Overlay(16),
            Outline = true
        };
        icon.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = icon.DeviceDpi / 96f;
            Glyphs.Draw(e.Graphics, glyph, new RectangleF(7 * s, 7 * s, 18 * s, 18 * s), Theme.Accent);
        };

        var label = new Label
        {
            Text = caption,
            Location = new Point(62, rowY + 16),
            AutoSize = true,
            Font = Theme.MediumFont(9.5F),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent
        };

        var slider = new SliderBar
        {
            Location = new Point(150, rowY + 11),
            Size = new Size(card.Width - 150 - 80, 28),
            BackColor = Theme.Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var value = new Label
        {
            Text = "50%",
            Location = new Point(card.Width - 20 - 50, rowY + 15),
            Size = new Size(50, 22),
            TextAlign = ContentAlignment.MiddleRight,
            Font = Theme.MediumFont(10F),
            ForeColor = Theme.Accent,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        card.Controls.Add(icon);
        card.Controls.Add(label);
        card.Controls.Add(slider);
        card.Controls.Add(value);
        y += 50;
        return (slider, value);
    }

    // --- Data ---

    private void LoadSettings()
    {
        ApplyToControls(_settings, StartupManager.IsStartupEnabled(),
            _getFailedHotkeys?.Invoke() ?? Array.Empty<HotkeyManager.HotkeyAction>());
        PopulateMonitorsList();
    }

    private void ApplyToControls(AppSettings s, bool startWithWindows, IReadOnlyCollection<HotkeyManager.HotkeyAction> failedHotkeys)
    {
        _chkStartup.Checked = startWithWindows;
        _chkStartMinimized.Checked = s.StartMinimized;
        _chkInterceptVolume.Checked = s.InterceptMediaVolumeKeys;
        _chkForwardNative.Checked = s.ForwardToNativeWhenSupported;

        _numStepPercent.Value = Math.Clamp(s.StepPercent, 1, 50);
        _numFineStepPercent.Value = Math.Clamp(s.FineStepPercent, 1, 10);
        _chkSyncContrast.Checked = s.SyncContrastWithBrightness;
        UpdateStepSummary();
        _chkFineVolumeDefault.Checked = s.AudioMediaKeyFineControlByDefault;
        _chkFineVideoDefault.Checked = s.VideoMediaKeyFineControlByDefault;

        _chkBrightnessHotkeys.Checked = s.EnableBrightnessHotkeys;
        _chkContrastHotkeys.Checked = s.EnableContrastHotkeys;
        _chkVolumeHotkeys.Checked = s.EnableVolumeHotkeys;

        foreach (var (action, binding) in s.GetShortcutMap())
        {
            if (!_recorders.TryGetValue(action, out var recorder)) continue;
            recorder.SetBinding(binding?.Modifiers ?? 0, binding?.Key ?? Keys.None);
            recorder.Unavailable = failedHotkeys.Contains(action);
        }
    }

    private void PopulateMonitorsList()
    {
        _cboTargetMonitor.Items.Clear();
        _cboTargetMonitor.Items.Add("All monitors (synchronized)");

        var monitors = _getMonitors();
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string type = m.IsBuiltIn ? "Built-in · WMI" : "DDC/CI";
            _cboTargetMonitor.Items.Add($"{i + 1}. {m.Description}  ({type})");
        }

        _lblMonitorStatus.Text = monitors.Count == 1 ? "1 display detected" : $"{monitors.Count} displays detected";
        _lblMonitorStatus.Parent?.Invalidate();

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
            _tileType.Value = monitors.Count == 1 ? "1 display" : $"{monitors.Count} displays";
            _tileBrightness.Value = "Synced";
            _tileVolume.Value = "Synced";
            _tileContrast.Value = "Synced";
            return;
        }

        var m = monitors[sel - 1];
        // Controls the monitor didn't answer to when probed are skipped (no slow, retried reads)
        var bright = m.SupportsBrightness ? m.GetBrightness() : null;
        var vol = m.SupportsVolume ? m.GetVolume() : null;
        var contrast = m.SupportsContrast ? m.GetContrast() : null;

        static string Pct((uint current, uint max)? v, bool supported) =>
            !supported ? "Not supported"
            : v.HasValue ? $"{Math.Round(DisplayControl.ToPercent(v.Value.current, v.Value.max))}%"
            : "No reply";

        _tileType.Value = m.IsBuiltIn ? "Built-in" : "DDC/CI";
        _tileBrightness.Value = Pct(bright, m.SupportsBrightness);
        _tileVolume.Value = Pct(vol, m.SupportsVolume);
        _tileContrast.Value = Pct(contrast, m.SupportsContrast);

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

        // Uses the toggle as currently shown, so the sync can be tried before saving.
        // Through the worker a fast drag sends only the latest value instead of stalling the window.
        if (_worker != null)
            _worker.SetPercent(DisplayWorker.Control.Brightness, targets.ToList(), percent, _chkSyncContrast.Checked, showOsd: false);
        else
            foreach (var m in targets)
                DisplayControl.SetBrightnessPercent(m, percent, _chkSyncContrast.Checked);
        _showOsd(OsdOverlay.OsdType.Brightness, percent / 100.0);
    }

    private void ApplyTestVolume(uint percent)
    {
        int sel = _cboTargetMonitor.SelectedIndex;
        var monitors = _getMonitors();

        var targets = (sel <= 0 || sel - 1 >= monitors.Count)
            ? (IEnumerable<IMonitor>)monitors
            : new[] { monitors[sel - 1] };

        if (_worker != null)
            _worker.SetPercent(DisplayWorker.Control.Volume, targets.ToList(), percent, showOsd: false);
        else
            foreach (var m in targets)
                DisplayControl.SetVolumePercent(m, percent);
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

        _settings.StepPercent = (int)_numStepPercent.Value;
        _settings.FineStepPercent = (int)_numFineStepPercent.Value;
        _settings.SyncContrastWithBrightness = _chkSyncContrast.Checked;
        _settings.AudioMediaKeyFineControlByDefault = _chkFineVolumeDefault.Checked;
        _settings.VideoMediaKeyFineControlByDefault = _chkFineVideoDefault.Checked;

        _settings.EnableBrightnessHotkeys = _chkBrightnessHotkeys.Checked;
        _settings.EnableContrastHotkeys = _chkContrastHotkeys.Checked;
        _settings.EnableVolumeHotkeys = _chkVolumeHotkeys.Checked;

        var shortcuts = _settings.GetShortcutMap();
        foreach (var (action, recorder) in _recorders)
        {
            shortcuts[action] = recorder.HasBinding
                ? new HotkeyManager.HotkeyBinding(action, recorder.Modifiers, recorder.Key)
                : null;
        }
        _settings.SetShortcutMap(shortcuts);

        int sel = _cboTargetMonitor.SelectedIndex;
        _settings.SelectedMonitorIndex = (sel > 0) ? (sel - 1) : -1;

        _settings.Save();
        _onSettingsChanged();
    }
}
