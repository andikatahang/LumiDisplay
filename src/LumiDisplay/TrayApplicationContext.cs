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
///   LumiDisplay uses percent steps on a grid instead (DisplayControl): 10% per press,
///   2% fine, values snap to multiples of the step; one OSD segment per step.
///   Optional: contrast follows brightness changes (SyncContrastWithBrightness).
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

    // Replaced (never mutated) by RefreshMonitors, so the DDC/CI worker can read it safely
    private volatile List<IMonitor> _monitors = new();
    private readonly System.Windows.Forms.Timer _monitorRefreshTimer;
    private readonly RegisteredWaitHandle? _showWindowWait;
    private readonly DisplayWorker _worker;

    public TrayApplicationContext(bool openSettings = false, System.Threading.EventWaitHandle? showWindowEvent = null)
    {
        _settings = AppSettings.Load();
        _osd = new OsdOverlay();
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _worker = new DisplayWorker(ui, (type, fraction) => _osd.Show(type, fraction), muted => _osd.ShowMute(muted));

        // A second launch (desktop icon, Start menu) signals this event: open the window here
        if (showWindowEvent != null)
        {
            _showWindowWait = ThreadPool.RegisterWaitForSingleObject(showWindowEvent,
                (_, _) => ui.Post(_ => ShowSettingsDialog(), null), null, Timeout.Infinite, false);
        }

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

        // Enumerate monitors (both DDC/CI external and WMI built-in laptop panels) right away;
        // what each one supports is probed on the DDC/CI worker
        _monitors = EnumerateMonitors();
        var initial = _monitors;
        _worker.Run(() => ProbeAndPublish(initial, replacing: null));

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
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettingsDialog();
        };

        if (openSettings)
            ShowSettingsDialog();
        else
            ReportHotkeyFailures();
    }

    // Shortcuts Windows refused (another app already owns the combination)
    private readonly List<HotkeyManager.HotkeyAction> _hotkeyFailures = new();
    private bool _hotkeysSuspended;

    private void RegisterConfiguredHotkeys()
    {
        _hotkeyManager.UnregisterAll();
        _hotkeyFailures.Clear();
        if (_hotkeysSuspended) return;

        foreach (var (action, binding) in _settings.GetShortcutMap())
        {
            if (binding == null || !IsShortcutGroupEnabled(action)) continue;
            if (!_hotkeyManager.RegisterHotkey(binding))
                _hotkeyFailures.Add(action);
        }
    }

    private bool IsShortcutGroupEnabled(HotkeyManager.HotkeyAction action) => action switch
    {
        HotkeyManager.HotkeyAction.BrightnessUp or HotkeyManager.HotkeyAction.BrightnessDown
            or HotkeyManager.HotkeyAction.BrightnessFineUp or HotkeyManager.HotkeyAction.BrightnessFineDown
            => _settings.EnableBrightnessHotkeys,
        HotkeyManager.HotkeyAction.ContrastUp or HotkeyManager.HotkeyAction.ContrastDown
            => _settings.EnableContrastHotkeys,
        _ => _settings.EnableVolumeHotkeys
    };

    /// <summary>
    /// Released while the settings window records a new shortcut, so pressing the current
    /// combination reaches the recorder instead of adjusting the display.
    /// </summary>
    private void SetHotkeysSuspended(bool suspended)
    {
        if (_hotkeysSuspended == suspended) return;
        _hotkeysSuspended = suspended;
        RegisterConfiguredHotkeys();
    }

    private void ReportHotkeyFailures()
    {
        if (_hotkeyFailures.Count == 0) return;
        var map = _settings.GetShortcutMap();
        var lines = _hotkeyFailures.ConvertAll(a =>
            $"{HotkeyManager.ActionName(a)} ({HotkeyManager.Describe(map[a]!)})");
        _trayIcon.ShowBalloonTip(6000, "Some shortcuts are already in use",
            string.Join(", ", lines) + " — another app owns them. Pick different keys in Settings → Hotkeys.",
            ToolTipIcon.Warning);
    }

    private static List<IMonitor> EnumerateMonitors()
    {
        var list = new List<IMonitor>();

        // 1. External monitors via DDC/CI (dxva2)
        list.AddRange(DdcMonitor.EnumerateMonitors());

        // 2. Built-in displays via WMI (laptops/tablets)
        list.AddRange(WmiMonitor.EnumerateMonitors());

        return list;
    }

    /// <summary>
    /// Re-enumerates displays on the DDC/CI worker. When the same displays are still connected the
    /// current objects (and their probed capabilities) are kept, so nothing is re-probed every 10 s.
    /// </summary>
    private void RefreshMonitors() => _worker.Run(() =>
    {
        var fresh = EnumerateMonitors();
        var current = _monitors;
        if (SameMonitors(current, fresh))
        {
            foreach (var m in fresh) m.Dispose();
            return;
        }
        ProbeAndPublish(fresh, replacing: current);
    });

    private static bool SameMonitors(List<IMonitor> a, List<IMonitor> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].GetType() != b[i].GetType() || a[i].Description != b[i].Description)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Worker thread: finds out what each display supports, then makes the list current on the
    /// UI thread and releases the displays it replaces.
    /// </summary>
    private void ProbeAndPublish(List<IMonitor> list, List<IMonitor>? replacing)
    {
        foreach (var m in list)
        {
            if (m is DdcMonitor ddc)
                ddc.ProbeCapabilities();
        }

        _worker.InvokeOnUi(() => _monitors = list);

        if (replacing != null && !ReferenceEquals(replacing, list))
        {
            foreach (var m in replacing)
                m.Dispose();
        }
    }

    private List<IMonitor> GetTargetMonitors()
    {
        if (_settings.SelectedMonitorIndex >= 0 && _settings.SelectedMonitorIndex < _monitors.Count)
            return new List<IMonitor> { _monitors[_settings.SelectedMonitorIndex] };
        return _monitors;
    }

    // --- Media key handler ---

    // Volume keys, volume shortcuts and the tray Volume menu change the Windows volume of the
    // default playback device, so the taskbar slider always matches what you hear. The monitor's
    // own hardware speaker level (DDC/CI) is a separate stage, set in Settings → Monitors & Test.

    private bool OnMediaKey(MediaKeyInterceptor.MediaKeyAction action)
    {
        // Sound is playing through headphones / laptop speakers: leave those keys to Windows
        if (_settings.ForwardToNativeWhenSupported && !AudioEndpoint.DefaultOutputIsDisplay())
            return false;

        int step = _settings.AudioMediaKeyFineControlByDefault
            ? _settings.FineStepPercent
            : _settings.StepPercent;

        return action switch
        {
            MediaKeyInterceptor.MediaKeyAction.VolumeUp => StepWindowsVolume(step, +1),
            MediaKeyInterceptor.MediaKeyAction.VolumeDown => StepWindowsVolume(step, -1),
            MediaKeyInterceptor.MediaKeyAction.VolumeMute => ToggleWindowsMute(),
            _ => false
        };
    }

    /// <summary>One grid step of the Windows volume (60 → 70 → 80), with LumiDisplay's OSD.
    /// Returns false (key goes to Windows) when there is no playback device to control.</summary>
    private bool StepWindowsVolume(int stepPercent, int direction)
    {
        var current = AudioEndpoint.GetVolumePercent();
        if (current == null) return false;
        double next = DisplayControl.NextPercent(current.Value, stepPercent, direction);
        AudioEndpoint.SetVolumePercent(next);
        if (AudioEndpoint.GetMute() == true)
            AudioEndpoint.SetMute(false); // like Windows: changing the volume unmutes
        _osd.Show(OsdOverlay.OsdType.Volume, next / 100.0);
        return true;
    }

    private void SetWindowsVolumePercent(int percent)
    {
        if (!AudioEndpoint.SetVolumePercent(percent)) return;
        if (AudioEndpoint.GetMute() == true)
            AudioEndpoint.SetMute(false);
        _osd.Show(OsdOverlay.OsdType.Volume, percent / 100.0);
    }

    private bool ToggleWindowsMute()
    {
        var muted = AudioEndpoint.GetMute();
        if (muted == null) return false;
        if (!AudioEndpoint.SetMute(!muted.Value)) return false;
        if (muted.Value)
            _osd.Show(OsdOverlay.OsdType.Volume, (AudioEndpoint.GetVolumePercent() ?? 0) / 100.0);
        else
            _osd.ShowMute(true);
        return true;
    }

    // --- Hotkey handler ---

    private void OnHotkeyAction(HotkeyManager.HotkeyAction action)
    {
        // Volume shortcuts change the Windows volume and follow the volume fine-scale setting;
        // they work even when no monitor is connected
        int volumeStep = _settings.StepPercent;
        int volumeFineStep = _settings.FineStepPercent;
        if (_settings.AudioMediaKeyFineControlByDefault)
            (volumeStep, volumeFineStep) = (volumeFineStep, volumeStep);

        switch (action)
        {
            case HotkeyManager.HotkeyAction.VolumeUp: StepWindowsVolume(volumeStep, +1); return;
            case HotkeyManager.HotkeyAction.VolumeDown: StepWindowsVolume(volumeStep, -1); return;
            case HotkeyManager.HotkeyAction.VolumeFineUp: StepWindowsVolume(volumeFineStep, +1); return;
            case HotkeyManager.HotkeyAction.VolumeFineDown: StepWindowsVolume(volumeFineStep, -1); return;
            case HotkeyManager.HotkeyAction.VolumeMute: ToggleWindowsMute(); return;
        }

        var targets = GetTargetMonitors();
        if (targets.Count == 0) return;

        // Step sizes in percent; values snap to multiples of the step (60 → 70 → 80)
        int standardStep = _settings.StepPercent;
        int fineStep = _settings.FineStepPercent;

        if (_settings.VideoMediaKeyFineControlByDefault)
        {
            // Swap: standard becomes fine, fine becomes standard
            (standardStep, fineStep) = (fineStep, standardStep);
        }

        switch (action)
        {
            case HotkeyManager.HotkeyAction.BrightnessUp:
                AdjustAllBrightness(targets, standardStep, +1);
                break;
            case HotkeyManager.HotkeyAction.BrightnessDown:
                AdjustAllBrightness(targets, standardStep, -1);
                break;
            case HotkeyManager.HotkeyAction.BrightnessFineUp:
                AdjustAllBrightness(targets, fineStep, +1);
                break;
            case HotkeyManager.HotkeyAction.BrightnessFineDown:
                AdjustAllBrightness(targets, fineStep, -1);
                break;
            case HotkeyManager.HotkeyAction.ContrastUp:
                AdjustAllContrast(targets, _settings.StepPercent, +1);
                break;
            case HotkeyManager.HotkeyAction.ContrastDown:
                AdjustAllContrast(targets, _settings.StepPercent, -1);
                break;
        }
    }

    // All three queue the DDC/CI work and return at once; the worker shows the OSD when done.

    private void AdjustAllBrightness(List<IMonitor> targets, int stepPercent, int direction) =>
        _worker.Step(DisplayWorker.Control.Brightness, targets, stepPercent, direction, _settings.SyncContrastWithBrightness);

    // Manual contrast: never touches brightness, even when contrast follows brightness
    private void AdjustAllContrast(List<IMonitor> targets, int stepPercent, int direction) =>
        _worker.Step(DisplayWorker.Control.Contrast, targets, stepPercent, direction);

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
            Font = Theme.Font(9F),
            Padding = new Padding(4, 6, 4, 6),
            ShowCheckMargin = false,
            ShowImageMargin = true
        };

        // Header with the app mark
        var header = new ToolStripLabel("LumiDisplay")
        {
            Image = Logo.ToBitmap(32),
            Font = Theme.MediumFont(10F),
            ForeColor = Theme.Text,
            Padding = new Padding(0, 4, 0, 6)
        };
        menu.Items.Add(header);

        // Settings / Preferences item
        var settingsItem = new ToolStripMenuItem("Settings…", MenuGlyph(Glyph.Gear));
        settingsItem.Font = Theme.MediumFont(9F);
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
        // Presets sit on the same 10% grid the keys use
        foreach (int pct in new[] { 0, 20, 40, 60, 80, 100 })
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
        foreach (int pct in new[] { 0, 20, 40, 60, 80, 100 })
        {
            int p = pct;
            var item = new ToolStripMenuItem($"{p}%");
            item.Click += (_, _) => SetWindowsVolumePercent(p);
            volumeMenu.DropDownItems.Add(item);
        }
        volumeMenu.DropDownItems.Add(new ToolStripSeparator());
        var muteItem = new ToolStripMenuItem("Toggle Mute");
        muteItem.Click += (_, _) => ToggleWindowsMute();
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

        var syncContrastItem = new ToolStripMenuItem("Sync Contrast with Brightness")
        { Checked = _settings.SyncContrastWithBrightness, CheckOnClick = true };
        syncContrastItem.CheckedChanged += (_, _) =>
        {
            _settings.SyncContrastWithBrightness = syncContrastItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(syncContrastItem);

        // The settings window can change these too; show the current state each time the menu opens
        menu.Opening += (_, _) =>
        {
            interceptItem.Checked = _settings.InterceptMediaVolumeKeys;
            fineAudioItem.Checked = _settings.AudioMediaKeyFineControlByDefault;
            fineVideoItem.Checked = _settings.VideoMediaKeyFineControlByDefault;
            syncContrastItem.Checked = _settings.SyncContrastWithBrightness;
        };

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
            DescribeShortcuts() +
            "  Volume keys: Windows volume in even steps (when intercepted)\n\n" +
            "Controllers Supported:\n" +
            "  • DDC/CI (dxva2.dll) - VCP 0x10, 0x12, 0x62, 0x8D\n" +
            "  • WMI (root\\wmi) - Built-in laptop panels",
            "About LumiDisplay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        menu.Items.Add(aboutItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Quit LumiDisplay", MenuGlyph(Glyph.Power));
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        ApplyItemPadding(menu.Items);
        return menu;
    }

    private string DescribeShortcuts()
    {
        var text = "";
        foreach (var (action, binding) in _settings.GetShortcutMap())
        {
            if (binding != null && IsShortcutGroupEnabled(action))
                text += $"  {HotkeyManager.Describe(binding)}: {HotkeyManager.ActionName(action)}\n";
        }
        return text;
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
                    _hotkeysSuspended = false;
                    RegisterConfiguredHotkeys();
                    _mediaKeyInterceptor.Enabled = _settings.InterceptMediaVolumeKeys;
                    ReportHotkeyFailures();
                },
                getMonitors: () => _monitors.AsReadOnly(),
                showOsd: (type, frac) => _osd.Show(type, frac),
                onQuit: ExitApplication,
                getFailedHotkeys: () => _hotkeyFailures.AsReadOnly(),
                suspendHotkeys: SetHotkeysSuspended,
                worker: _worker);
            var form = _settingsForm;
            form.FormClosed += (_, _) =>
            {
                if (!form.QuitRequested) ShowTrayHintOnce();
            };
        }

        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Show();
        _settingsForm.BringToFront();
        _settingsForm.Activate();
    }

    /// <summary>
    /// Closing the window keeps LumiDisplay running; tell the user once where it went.
    /// </summary>
    private void ShowTrayHintOnce()
    {
        if (_settings.TrayHintShown || _exiting) return;
        _settings.TrayHintShown = true;
        _settings.Save();
        _trayIcon.ShowBalloonTip(4000, "LumiDisplay is still running",
            "It keeps handling your brightness and volume keys from the system tray. " +
            "Click the tray icon to reopen, or right-click it to quit.",
            ToolTipIcon.None);
    }

    private bool _exiting;

    private void ExitApplication()
    {
        _exiting = true;
        _trayIcon.Visible = false;
        Cleanup();
        Application.Exit();
    }

    private void SetBrightnessPercent(int percent) =>
        _worker.SetPercent(DisplayWorker.Control.Brightness, GetTargetMonitors(), percent, _settings.SyncContrastWithBrightness);


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
                    info += $"  Speaker (hardware): {vol.Value.current}/{vol.Value.max} " +
                            $"({vol.Value.current * 100 / Math.Max(1, vol.Value.max)}%)\n";
                else
                    info += "  Speaker (hardware): N/A (no DDC audio control)\n";

                var mute = m.GetMute();
                if (mute != null)
                    info += $"  Mute: {(mute.Value ? "On" : "Off")}\n";

                info += "\n";
            }
        }

        var winVolume = AudioEndpoint.GetVolumePercent();
        info += $"Windows volume: {(winVolume.HasValue ? $"{Math.Round(winVolume.Value)}%" : "N/A")}" +
                $"{(AudioEndpoint.GetMute() == true ? " (muted)" : "")}\n\n";

        info += $"Settings:\n" +
                $"  Step: {_settings.StepPercent}%\n" +
                $"  Fine Step: {_settings.FineStepPercent}%\n" +
                $"  Sync Contrast with Brightness: {_settings.SyncContrastWithBrightness}\n" +
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
        _showWindowWait?.Unregister(null);
        _monitorRefreshTimer.Stop();
        _monitorRefreshTimer.Dispose();
        _mediaKeyInterceptor.Dispose();
        _hotkeyManager.Dispose();
        _worker.Dispose(); // finish the DDC/CI command in flight before the monitor handles go
        _osd.Dispose();

        _settingsForm?.Dispose();

        foreach (var m in _monitors)
            m.Dispose();
        _monitors = new List<IMonitor>();

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
/// Professional color table providing LumiDisplay's dark gray palette.
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
/// a dark panel with Windows 11 rounded corners, soft hover pills, light check marks and thin separators.
/// </summary>
internal sealed class LumiDisplayMenuRenderer : ToolStripProfessionalRenderer
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public LumiDisplayMenuRenderer() : base(new LumiDisplayColorTable())
    {
        RoundedEdges = false;
    }

    // Runs for the menu and every submenu that shares this renderer
    protected override void Initialize(ToolStrip toolStrip)
    {
        base.Initialize(toolStrip);
        if (toolStrip is not ToolStripDropDown dropDown) return;
        if (dropDown.IsHandleCreated) RoundCorners(dropDown);
        dropDown.HandleCreated += (_, _) => RoundCorners(dropDown);
    }

    // shortcut: Windows 10 has no DWM corner preference and keeps square corners; fine for a Windows 11 look
    private static void RoundCorners(Control c)
    {
        try
        {
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(c.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch { }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        using var path = Theme.RoundedRect(rect, 6);
        using var brush = new SolidBrush(Theme.SurfaceAlt);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled || e.Item is ToolStripLabel ? Theme.Text : Theme.TextFaint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Selected == true ? Theme.Text : Theme.TextMuted;
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

        using var pen = new Pen(Theme.OnAccent, Math.Max(1.6f, rect.Width / 9f))
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
