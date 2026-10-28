using System;
using System.IO;
using System.Text.Json;

namespace LumiDisplay;

/// <summary>
/// Persisted application settings.
/// Mirrors BetterDisplay's per-display configuration model with:
///   - Keyboard step sizes (standard vs fine)
///   - Media key interception toggles
///   - Fine control default toggle
///   - Custom hotkey bindings
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Total logical steps for the full range.
    /// BetterDisplay default: 64 (16 chiclets × 4 steps per chiclet).
    /// </summary>
    public int TotalSteps { get; set; } = 64;

    /// <summary>
    /// Number of logical steps per standard keyboard press.
    /// BetterDisplay default: 4 (one OSD chiclet).
    /// </summary>
    public int StandardStep { get; set; } = 4;

    /// <summary>
    /// Number of logical steps per fine-scale keyboard press.
    /// BetterDisplay default: 1 (one-quarter of a chiclet).
    /// </summary>
    public int FineStep { get; set; } = 1;

    /// <summary>
    /// Whether to intercept native media volume keys and redirect to DDC.
    /// Maps to BetterDisplay's "Let the app handle native audio (volume, mute) keys".
    /// </summary>
    public bool InterceptMediaVolumeKeys { get; set; } = true;

    /// <summary>
    /// Whether fine scale is the default for volume media keys.
    /// BetterDisplay: "Make fine scale default for volume keys"
    /// </summary>
    public bool AudioMediaKeyFineControlByDefault { get; set; } = false;

    /// <summary>
    /// Whether fine scale is the default for brightness hotkeys.
    /// BetterDisplay: videoMediaKeyFineControlByDefault
    /// </summary>
    public bool VideoMediaKeyFineControlByDefault { get; set; } = false;

    /// <summary>
    /// When the audio device supports native volume control, forward to system instead.
    /// BetterDisplay: "When the app detects that the active audio device supports
    /// native volume control in macOS while it listens to media keys, it forwards
    /// the appropriate operations to the operating system."
    /// </summary>
    public bool ForwardToNativeWhenSupported { get; set; } = true;

    /// <summary>
    /// Enable brightness hotkeys (Ctrl+Alt+Up/Down).
    /// </summary>
    public bool EnableBrightnessHotkeys { get; set; } = true;

    /// <summary>
    /// Enable contrast hotkeys (Ctrl+Alt+Left/Right).
    /// </summary>
    public bool EnableContrastHotkeys { get; set; } = true;

    /// <summary>
    /// Enable the custom volume shortcuts (none are assigned by default; the keyboard's
    /// volume keys are handled separately by the media key interceptor).
    /// </summary>
    public bool EnableVolumeHotkeys { get; set; } = true;

    /// <summary>
    /// User-customized shortcuts. Actions missing from this list use
    /// <see cref="HotkeyManager.DefaultBindings"/>; an entry with Key = 0 means "not set".
    /// </summary>
    public List<ShortcutSetting> Shortcuts { get; set; } = new();

    public sealed class ShortcutSetting
    {
        public string Action { get; set; } = "";
        public uint Modifiers { get; set; }
        public int Key { get; set; }
    }

    /// <summary>
    /// The effective shortcut for every action (null = not set), merging saved values with defaults.
    /// </summary>
    public Dictionary<HotkeyManager.HotkeyAction, HotkeyManager.HotkeyBinding?> GetShortcutMap()
    {
        var map = new Dictionary<HotkeyManager.HotkeyAction, HotkeyManager.HotkeyBinding?>();
        foreach (var action in Enum.GetValues<HotkeyManager.HotkeyAction>())
            map[action] = Array.Find(HotkeyManager.DefaultBindings, b => b.Action == action);

        foreach (var saved in Shortcuts)
        {
            if (!Enum.TryParse<HotkeyManager.HotkeyAction>(saved.Action, out var action)) continue;
            map[action] = saved.Key == 0
                ? null
                : new HotkeyManager.HotkeyBinding(action, saved.Modifiers, (System.Windows.Forms.Keys)saved.Key);
        }
        return map;
    }

    /// <summary>
    /// Stores every action's shortcut explicitly, so later changes to the defaults don't move them.
    /// </summary>
    public void SetShortcutMap(IReadOnlyDictionary<HotkeyManager.HotkeyAction, HotkeyManager.HotkeyBinding?> map)
    {
        Shortcuts = new List<ShortcutSetting>();
        foreach (var (action, binding) in map)
        {
            Shortcuts.Add(new ShortcutSetting
            {
                Action = action.ToString(),
                Modifiers = binding?.Modifiers ?? 0,
                Key = binding == null ? 0 : (int)binding.Key
            });
        }
    }

    /// <summary>
    /// Start minimized to system tray.
    /// </summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>
    /// Start with Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Index of the currently selected monitor (0-based).
    /// -1 means control all monitors.
    /// </summary>
    public int SelectedMonitorIndex { get; set; } = -1;

    /// <summary>
    /// Whether the "still running in the tray" notice was already shown after closing the window.
    /// </summary>
    public bool TrayHintShown { get; set; } = false;

    // --- Persistence ---

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumiDisplay", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore corrupt settings
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best effort
        }
    }
}
