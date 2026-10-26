using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Main application context: system tray icon, hotkey processing, DDC control orchestration.
/// 
/// Architecture mirrors BetterDisplay's AppCore:
///   1. CGEventTap for media keys  →  MediaKeyInterceptor (WH_KEYBOARD_LL)
///   2. RegisterEventHotKey        →  HotkeyManager (RegisterHotKey)
///   3. DDCController + IOI2C      →  DdcMonitor (dxva2.dll SetVCPFeature/GetVCPFeature)
///   4. GlassOSD / AppOSD          →  OsdOverlay (layered WinForms)
///   5. Per-display settings       →  AppSettings (JSON)
///
/// Key mapping from BetterDisplay (macOS) to LumiDisplay (Windows):
///   NX_KEYTYPE_SOUND_UP (0)       →  VK_VOLUME_UP (0xAF)
///   NX_KEYTYPE_SOUND_DOWN (1)     →  VK_VOLUME_DOWN (0xAE)
///   NX_KEYTYPE_MUTE (7)           →  VK_VOLUME_MUTE (0xAD)
///   NX_KEYTYPE_BRIGHTNESS_UP (2)  →  Ctrl+Alt+Up (custom hotkey)
///   NX_KEYTYPE_BRIGHTNESS_DOWN (3)→  Ctrl+Alt+Down (custom hotkey)
///
/// DDC VCP codes (identical on both platforms - MCCS standard):
///   0x10 = Luminance (Brightness)
///   0x12 = Contrast
///   0x62 = Audio Speaker Volume
///   0x8D = Audio Mute
///
/// Step sizes (from BetterDisplay's keyboardSteps configuration):
///   Total range: 64 logical steps (configurable)
///   Standard step: 4 logical steps = 1 OSD chiclet (16 chiclets total)
///   Fine step: 1 logical step = 1/4 chiclet
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly HiddenHotkeyWindow _hotkeyWindow;
    private readonly HotkeyManager _hotkeyManager;
    private readonly MediaKeyInterceptor _mediaKeyInterceptor;
    private readonly OsdOverlay _osd;
    private readonly AppSettings _settings;
    private SettingsForm? _settingsForm;

    private List<IMonitor> _monitors = new();
    private readonly System.Windows.Forms.Timer _monitorRefreshTimer;

    public TrayApplicationContext(bool openSettings = false)
    {
        _settings = AppSettings.Load();
        _osd = new OsdOverlay();

        // Create hidden window for hotkey messages
        _hotkeyWindow = new HiddenHotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyMessage;

        // Set up hotkey manager
        _hotkeyManager = new HotkeyManager(_hotkeyWindow.Handle, OnHotkeyAction);
        RegisterConfiguredHotkeys();

        // Set up media key interceptor
        _mediaKeyInterceptor = new MediaKeyInterceptor(OnMediaKey);
        _mediaKeyInterceptor.Enabled = _settings.InterceptMediaVolumeKeys;
        _mediaKeyInterceptor.Install();

        // Enumerate monitors (both DDC/CI external and WMI built-in laptop panels)
        RefreshMonitors();

        // Refresh monitor list periodically (monitors can be connected/disconnected)
        _monitorRefreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        _monitorRefreshTimer.Tick += (_, _) => RefreshMonitors();
        _monitorRefreshTimer.Start();

        // System tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = LoadOrGenerateTrayIcon(),
            Visible = true,
            Text = "LumiDisplay — Display & Audio Control",
            ContextMenuStrip = CreateContextMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettingsDialog();

        if (openSettings)
            ShowSettingsDialog();
    }

    private void RegisterConfiguredHotkeys()
    {
        _hotkeyManager.UnregisterAll();

        if (_settings.EnableBrightnessHotkeys)
        {
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.BrightnessUp,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Up));
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.BrightnessDown,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Down));
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.BrightnessFineUp,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, Keys.Up));
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.BrightnessFineDown,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, Keys.Down));
        }

        if (_settings.EnableContrastHotkeys)
        {
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.ContrastUp,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Right));
            _hotkeyManager.RegisterHotkey(new HotkeyManager.HotkeyBinding(
                HotkeyManager.HotkeyAction.ContrastDown,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Left));
        }
    }

    private void RefreshMonitors()
    {
        // Dispose old monitors
        foreach (var m in _monitors)
            m.Dispose();

        var list = new List<IMonitor>();

        // 1. External monitors via DDC/CI (dxva2)
        list.AddRange(DdcMonitor.EnumerateMonitors());

        // 2. Built-in displays via WMI (laptops/tablets)
        list.AddRange(WmiMonitor.EnumerateMonitors());

        _monitors = list;
    }

    private List<IMonitor> GetTargetMonitors()
    {
        if (_settings.SelectedMonitorIndex >= 0 && _settings.SelectedMonitorIndex < _monitors.Count)
            return new List<IMonitor> { _monitors[_settings.SelectedMonitorIndex] };
        return _monitors;
    }

    // --- Media key handler ---

    private bool OnMediaKey(MediaKeyInterceptor.MediaKeyAction action)
    {
        var targets = GetTargetMonitors();
        if (targets.Count == 0)
        {
            // No DDC monitors - let system handle the keys
            return false;
        }

        int step = _settings.AudioMediaKeyFineControlByDefault
            ? _settings.FineStep
            : _settings.StandardStep;

        switch (action)
        {
            case MediaKeyInterceptor.MediaKeyAction.VolumeUp:
                foreach (var m in targets)
                {
                    var result = m.AdjustVolume(step, _settings.TotalSteps);
                    if (result != null)
                        _osd.Show(OsdOverlay.OsdType.Volume,
                            (double)result.Value.newValue / result.Value.maxValue);
                }
                return true;

            case MediaKeyInterceptor.MediaKeyAction.VolumeDown:
                foreach (var m in targets)
                {
                    var result = m.AdjustVolume(-step, _settings.TotalSteps);
                    if (result != null)
                        _osd.Show(OsdOverlay.OsdType.Volume,
                            (double)result.Value.newValue / result.Value.maxValue);
                }
                return true;

            case MediaKeyInterceptor.MediaKeyAction.VolumeMute:
                foreach (var m in targets)
                {
                    bool? wasMuted = m.GetMute();
                    if (m.ToggleMute())
                    {
                        bool nowMuted = !(wasMuted ?? false);
                        _osd.ShowMute(nowMuted);
                    }
                }
                return true;
        }

        return false;
    }

    // --- Hotkey handler ---

    private void OnHotkeyAction(HotkeyManager.HotkeyAction action)
    {
        var targets = GetTargetMonitors();
        if (targets.Count == 0) return;

        int standardStep = _settings.StandardStep;
        int fineStep = _settings.FineStep;

        if (_settings.VideoMediaKeyFineControlByDefault)
        {
            // Swap: standard becomes fine, fine becomes standard
            (standardStep, fineStep) = (fineStep, standardStep);
        }

        switch (action)
        {
            case HotkeyManager.HotkeyAction.BrightnessUp:
                AdjustAllBrightness(targets, standardStep);
                break;
            case HotkeyManager.HotkeyAction.BrightnessDown:
                AdjustAllBrightness(targets, -standardStep);
                break;
            case HotkeyManager.HotkeyAction.BrightnessFineUp:
                AdjustAllBrightness(targets, fineStep);
                break;
            case HotkeyManager.HotkeyAction.BrightnessFineDown:
                AdjustAllBrightness(targets, -fineStep);
                break;
            case HotkeyManager.HotkeyAction.VolumeUp:
                AdjustAllVolume(targets, standardStep);
                break;
            case HotkeyManager.HotkeyAction.VolumeDown:
                AdjustAllVolume(targets, -standardStep);
                break;
            case HotkeyManager.HotkeyAction.VolumeFineUp:
                AdjustAllVolume(targets, fineStep);
                break;
            case HotkeyManager.HotkeyAction.VolumeFineDown:
                AdjustAllVolume(targets, -fineStep);
                break;
            case HotkeyManager.HotkeyAction.VolumeMute:
                foreach (var m in targets)
                {
                    bool? wasMuted = m.GetMute();
                    if (m.ToggleMute())
                        _osd.ShowMute(!(wasMuted ?? false));
                }
                break;
            case HotkeyManager.HotkeyAction.ContrastUp:
                AdjustAllContrast(targets, standardStep);
                break;
            case HotkeyManager.HotkeyAction.ContrastDown:
                AdjustAllContrast(targets, -standardStep);
                break;
        }
    }

    private void AdjustAllBrightness(List<IMonitor> targets, int step)
    {
        foreach (var m in targets)
        {
            var result = m.AdjustBrightness(step, _settings.TotalSteps);
            if (result != null)
                _osd.Show(OsdOverlay.OsdType.Brightness,
                    (double)result.Value.newValue / result.Value.maxValue);
        }
    }

    private void AdjustAllVolume(List<IMonitor> targets, int step)
    {
        foreach (var m in targets)
        {
            var result = m.AdjustVolume(step, _settings.TotalSteps);
            if (result != null)
                _osd.Show(OsdOverlay.OsdType.Volume,
                    (double)result.Value.newValue / result.Value.maxValue);
        }
    }

    private void AdjustAllContrast(List<IMonitor> targets, int step)
    {
        foreach (var m in targets)
        {
            var cur = m.GetContrast();
            if (cur == null) continue;

            uint max = cur.Value.max;
            if (max == 0) continue;

            double stepSize = (double)max / _settings.TotalSteps;
            int newVal = (int)Math.Round(cur.Value.current + step * stepSize);
            newVal = Math.Clamp(newVal, 0, (int)max);

            if (m.SetContrast((uint)newVal))
                _osd.Show(OsdOverlay.OsdType.Contrast, (double)newVal / max);
        }
    }

    // --- Hotkey window message relay ---

    private void OnHotkeyMessage(int hotkeyId)
    {
        _hotkeyManager.ProcessHotkey(hotkeyId);
    }

    // --- Tray context menu ---

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new LumiDisplayMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = new Font(Theme.FontFamily, 9F),
            Padding = new Padding(4, 6, 4, 6),
            ShowCheckMargin = false,
            ShowImageMargin = true
        };

        // Header with the app mark
        var header = new ToolStripLabel("LumiDisplay")
        {
            Image = Logo.ToBitmap(32),
            Font = new Font(Theme.FontFamilySemibold, 10F),
            ForeColor = Theme.Accent,
            Padding = new Padding(0, 4, 0, 6)
        };
        menu.Items.Add(header);

        // Settings / Preferences item
        var settingsItem = new ToolStripMenuItem("Settings…", MenuGlyph(Glyph.Gear));
        settingsItem.Font = new Font(Theme.FontFamilySemibold, 9F);
        settingsItem.Click += (_, _) => ShowSettingsDialog();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // Monitor list submenu
        var monitorsMenu = new ToolStripMenuItem("Monitors", MenuGlyph(Glyph.Monitor));
        monitorsMenu.DropDown.Renderer = menu.Renderer;
        monitorsMenu.DropDownOpening += (_, _) =>
        {
            monitorsMenu.DropDownItems.Clear();

            var allItem = new ToolStripMenuItem("All Monitors (Synchronized)")
            { Checked = _settings.SelectedMonitorIndex == -1 };
            allItem.Click += (_, _) =>
            {
                _settings.SelectedMonitorIndex = -1;
                _settings.Save();
            };
            monitorsMenu.DropDownItems.Add(allItem);
            monitorsMenu.DropDownItems.Add(new ToolStripSeparator());

            for (int i = 0; i < _monitors.Count; i++)
            {
                int idx = i;
                var mon = _monitors[i];
                string typeTag = mon.IsBuiltIn ? " [WMI]" : " [DDC/CI]";
                var item = new ToolStripMenuItem($"{i + 1}. {mon.Description}{typeTag}")
                { Checked = _settings.SelectedMonitorIndex == idx };
                item.Click += (_, _) =>
                {
                    _settings.SelectedMonitorIndex = idx;
                    _settings.Save();
                };
                monitorsMenu.DropDownItems.Add(item);
            }

            if (_monitors.Count == 0)
            {
                monitorsMenu.DropDownItems.Add(
                    new ToolStripMenuItem("(No monitors detected)") { Enabled = false });
            }

            monitorsMenu.DropDownItems.Add(new ToolStripSeparator());
            var refreshItem = new ToolStripMenuItem("Refresh");
            refreshItem.Click += (_, _) => RefreshMonitors();
            monitorsMenu.DropDownItems.Add(refreshItem);
            ApplyItemPadding(monitorsMenu.DropDownItems);
        };
        // Placeholder so the submenu arrow shows before the first open
        monitorsMenu.DropDownItems.Add(new ToolStripMenuItem("(Loading…)") { Enabled = false });
        menu.Items.Add(monitorsMenu);

        menu.Items.Add(new ToolStripSeparator());

        // Quick adjust submenu
        var brightnessMenu = new ToolStripMenuItem("Brightness", MenuGlyph(Glyph.Sun));
        brightnessMenu.DropDown.Renderer = menu.Renderer;
        foreach (int pct in new[] { 0, 25, 50, 75, 100 })
        {
            int p = pct;
            var item = new ToolStripMenuItem($"{p}%");
            item.Click += (_, _) => SetBrightnessPercent(p);
            brightnessMenu.DropDownItems.Add(item);
        }
        ApplyItemPadding(brightnessMenu.DropDownItems);
        menu.Items.Add(brightnessMenu);

        var volumeMenu = new ToolStripMenuItem("Volume", MenuGlyph(Glyph.Speaker));
        volumeMenu.DropDown.Renderer = menu.Renderer;
        foreach (int pct in new[] { 0, 25, 50, 75, 100 })
        {
            int p = pct;
            var item = new ToolStripMenuItem($"{p}%");
            item.Click += (_, _) => SetVolumePercent(p);
            volumeMenu.DropDownItems.Add(item);
        }
        volumeMenu.DropDownItems.Add(new ToolStripSeparator());
        var muteItem = new ToolStripMenuItem("Toggle Mute");
        muteItem.Click += (_, _) =>
        {
            foreach (var m in GetTargetMonitors())
            {
                bool? wasMuted = m.GetMute();
                if (m.ToggleMute())
                    _osd.ShowMute(!(wasMuted ?? false));
            }
        };
        volumeMenu.DropDownItems.Add(muteItem);
        ApplyItemPadding(volumeMenu.DropDownItems);
        menu.Items.Add(volumeMenu);

        menu.Items.Add(new ToolStripSeparator());

        // Quick toggles
        var interceptItem = new ToolStripMenuItem("Intercept Media Volume Keys")
        { Checked = _settings.InterceptMediaVolumeKeys, CheckOnClick = true };
        interceptItem.CheckedChanged += (_, _) =>
        {
            _settings.InterceptMediaVolumeKeys = interceptItem.Checked;
            _mediaKeyInterceptor.Enabled = interceptItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(interceptItem);

        var fineAudioItem = new ToolStripMenuItem("Fine Scale Default (Volume)")
        { Checked = _settings.AudioMediaKeyFineControlByDefault, CheckOnClick = true };
        fineAudioItem.CheckedChanged += (_, _) =>
        {
            _settings.AudioMediaKeyFineControlByDefault = fineAudioItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(fineAudioItem);

        var fineVideoItem = new ToolStripMenuItem("Fine Scale Default (Brightness)")
        { Checked = _settings.VideoMediaKeyFineControlByDefault, CheckOnClick = true };
        fineVideoItem.CheckedChanged += (_, _) =>
        {
            _settings.VideoMediaKeyFineControlByDefault = fineVideoItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(fineVideoItem);

        menu.Items.Add(new ToolStripSeparator());

        // Info
        var infoItem = new ToolStripMenuItem("Monitor Info…", MenuGlyph(Glyph.Info));
        infoItem.Click += (_, _) => ShowSettingsInfo();
        menu.Items.Add(infoItem);

        // About
        var aboutItem = new ToolStripMenuItem("About LumiDisplay", MenuGlyph(Glyph.Monitor));
        aboutItem.Click += (_, _) => MessageBox.Show(
            "LumiDisplay v1.0\n\n" +
            "Intelligent external display & laptop brightness and audio control.\n" +
            "Native Windows equivalent of BetterDisplay's display controllers.\n\n" +
            "Keyboard Shortcuts:\n" +
            "  Ctrl+Alt+Up/Down: Brightness ±1 chiclet (6.25%)\n" +
            "  Ctrl+Alt+Shift+Up/Down: Fine brightness (1.56%)\n" +
            "  Ctrl+Alt+Left/Right: Contrast\n" +
            "  Volume keys: Monitor audio (when intercepted)\n\n" +
            "Controllers Supported:\n" +
            "  • DDC/CI (dxva2.dll) - VCP 0x10, 0x12, 0x62, 0x8D\n" +
            "  • WMI (root\\wmi) - Built-in laptop panels",
            "About LumiDisplay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        menu.Items.Add(aboutItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit", MenuGlyph(Glyph.Power));
        exitItem.Click += (_, _) =>
        {
            _trayIcon.Visible = false;
            Cleanup();
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        ApplyItemPadding(menu.Items);
        return menu;
    }

    private static Bitmap MenuGlyph(Glyph glyph) => Glyphs.ToBitmap(glyph, 32, Theme.TextMuted);

    private static void ApplyItemPadding(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem)
                item.Padding = new Padding(2, 5, 2, 5);
        }
    }

    private void ShowSettingsDialog()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(
                _settings,
                onSettingsChanged: () =>
                {
                    RegisterConfiguredHotkeys();
                    _mediaKeyInterceptor.Enabled = _settings.InterceptMediaVolumeKeys;
                },
                getMonitors: () => _monitors.AsReadOnly(),
                showOsd: (type, frac) => _osd.Show(type, frac));
        }

        _settingsForm.Show();
        _settingsForm.BringToFront();
        _settingsForm.Activate();
    }

    private void SetBrightnessPercent(int percent)
    {
        foreach (var m in GetTargetMonitors())
        {
            var cur = m.GetBrightness();
            if (cur != null)
            {
                uint val = (uint)(cur.Value.max * percent / 100);
                if (m.SetBrightness(val))
                    _osd.Show(OsdOverlay.OsdType.Brightness, percent / 100.0);
            }
        }
    }

    private void SetVolumePercent(int percent)
    {
        foreach (var m in GetTargetMonitors())
        {
            var cur = m.GetVolume();
            if (cur != null)
            {
                uint val = (uint)(cur.Value.max * percent / 100);
                if (m.SetVolume(val))
                    _osd.Show(OsdOverlay.OsdType.Volume, percent / 100.0);
            }
        }
    }

    private void ShowSettingsInfo()
    {
        var info = "LumiDisplay — Detected Monitors\n\n";

        if (_monitors.Count == 0)
        {
            info += "No DDC/CI capable monitors detected.\n\n" +
                    "Make sure your monitor supports DDC/CI and is connected\n" +
                    "via a compatible port (HDMI, DisplayPort, DVI, USB-C).\n" +
                    "VGA connections may also work but are less reliable.";
        }
        else
        {
            for (int i = 0; i < _monitors.Count; i++)
            {
                var m = _monitors[i];
                info += $"Monitor {i + 1}: {m.Description}\n";

                var bright = m.GetBrightness();
                if (bright != null)
                    info += $"  Brightness: {bright.Value.current}/{bright.Value.max} " +
                            $"({bright.Value.current * 100 / Math.Max(1, bright.Value.max)}%)\n";
                else
                    info += "  Brightness: N/A\n";

                var contrast = m.GetContrast();
                if (contrast != null)
                    info += $"  Contrast: {contrast.Value.current}/{contrast.Value.max}\n";

                var vol = m.GetVolume();
                if (vol != null)
                    info += $"  Volume: {vol.Value.current}/{vol.Value.max} " +
                            $"({vol.Value.current * 100 / Math.Max(1, vol.Value.max)}%)\n";
                else
                    info += "  Volume: N/A (no DDC audio control)\n";

                var mute = m.GetMute();
                if (mute != null)
                    info += $"  Mute: {(mute.Value ? "On" : "Off")}\n";

                info += "\n";
            }
        }

        info += $"Settings:\n" +
                $"  Total Steps: {_settings.TotalSteps}\n" +
                $"  Standard Step: {_settings.StandardStep} ({_settings.StandardStep * 100.0 / _settings.TotalSteps:F1}%)\n" +
                $"  Fine Step: {_settings.FineStep} ({_settings.FineStep * 100.0 / _settings.TotalSteps:F1}%)\n" +
                $"  Intercept Volume Keys: {_settings.InterceptMediaVolumeKeys}\n";

        MessageBox.Show(info, "Monitor Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Icon LoadOrGenerateTrayIcon()
    {
        try
        {
            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lumidisplay.ico");
            if (System.IO.File.Exists(icoPath))
            {
                // Ask for the small-icon size so the tray gets the crisp hand-tuned frame
                return new Icon(icoPath, SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return CreateTrayIcon();
    }

    private static Icon CreateTrayIcon()
    {
        // Programmatic fallback icon: same mark as lumidisplay.ico
        using var bmp = Logo.ToBitmap(SystemInformation.SmallIconSize.Width);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void Cleanup()
    {
        _monitorRefreshTimer.Stop();
        _monitorRefreshTimer.Dispose();
        _mediaKeyInterceptor.Dispose();
        _hotkeyManager.Dispose();
        _osd.Dispose();

        _settingsForm?.Dispose();

        foreach (var m in _monitors)
            m.Dispose();
        _monitors.Clear();

        _trayIcon.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Hidden window that receives WM_HOTKEY messages.
/// Equivalent to the hidden NSWindow/NSApp delegate in BetterDisplay's hotkey system.
/// </summary>
internal sealed class HiddenHotkeyWindow : NativeWindow, IDisposable
{
    public event Action<int>? HotkeyPressed;

    public HiddenHotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "LumiDisplay_HotkeyReceiver",
            Style = 0,
            ExStyle = 0,
            Parent = IntPtr.Zero
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}

/// <summary>
/// Professional color table providing LumiDisplay's light palette (white, soft gray, dark blue).
/// </summary>
internal sealed class LumiDisplayColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color MenuBorder => Theme.BorderStrong;
    public override Color MenuItemBorder => Theme.AccentSoft;
    public override Color MenuItemSelected => Theme.AccentSoft;
    public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
    public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;
    public override Color MenuItemPressedGradientBegin => Theme.AccentSoft;
    public override Color MenuItemPressedGradientEnd => Theme.AccentSoft;
    public override Color CheckBackground => Theme.Surface;
    public override Color CheckSelectedBackground => Theme.AccentSoft;
    public override Color CheckPressedBackground => Theme.AccentSoft;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Surface;
}

/// <summary>
/// Custom ToolStripProfessionalRenderer applying LumiDisplay styling to tray context menu:
/// rounded dark-blue-tinted hover pills, dark blue check marks and thin separators.
/// </summary>
internal sealed class LumiDisplayMenuRenderer : ToolStripProfessionalRenderer
{
    public LumiDisplayMenuRenderer() : base(new LumiDisplayColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        using var path = Theme.RoundedRect(rect, 6);
        using var brush = new SolidBrush(Theme.AccentSoft);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripLabel)
            e.TextColor = Theme.Accent;
        else if (!e.Item.Enabled)
            e.TextColor = Theme.TextFaint;
        else
            e.TextColor = e.Item.Selected ? Theme.Accent : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Selected == true ? Theme.Accent : Theme.TextMuted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        var rect = e.ImageRectangle;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var box = new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
        using (var path = Theme.RoundedRect(box, 4))
        using (var brush = new SolidBrush(Theme.Accent))
            g.FillPath(brush, path);

        using var pen = new Pen(Color.White, Math.Max(1.6f, rect.Width / 9f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        g.DrawLines(pen, new[]
        {
            new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
            new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
            new PointF(box.X + box.Width * 0.76f, box.Y + box.Height * 0.32f)
        });
    }
}
