using System;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Manages custom keyboard shortcuts using Windows global hotkeys.
/// Mirrors BetterDisplay's RegisterEventHotKey / UnregisterEventHotKey usage.
/// 
/// BetterDisplay registers hotkeys for:
///   - Brightness up/down (with fine-scale variants)
///   - Volume up/down (with fine-scale variants)
///   - Mute toggle
///   - Contrast up/down
///   - Input source switching
///   - And many more display controls
/// 
/// We implement the core brightness/volume/mute shortcuts.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public enum HotkeyAction
    {
        BrightnessUp,
        BrightnessDown,
        BrightnessFineUp,
        BrightnessFineDown,
        VolumeUp,
        VolumeDown,
        VolumeFineUp,
        VolumeFineDown,
        VolumeMute,
        ContrastUp,
        ContrastDown,
    }

    /// <summary>
    /// Hotkey definition with modifier and key.
    /// </summary>
    public record HotkeyBinding(HotkeyAction Action, uint Modifiers, Keys Key);

    public delegate void HotkeyPressedHandler(HotkeyAction action);

    private readonly IntPtr _windowHandle;
    private readonly HotkeyPressedHandler _handler;
    private readonly Dictionary<int, HotkeyAction> _registeredHotkeys = new();
    private int _nextId = 0x0100; // Start from a safe range
    private bool _disposed;

    /// <summary>
    /// Default hotkey bindings matching BetterDisplay's typical configuration.
    /// BetterDisplay uses:
    ///   - Standard step: one OSD chiclet worth (4 logical steps of 64 total)
    ///   - Fine step: Shift+Option modifier for 1 logical step
    /// 
    /// Windows equivalents:
    ///   Ctrl+Alt+Up/Down for brightness
    ///   Ctrl+Alt+Shift+Up/Down for fine brightness
    ///   (Volume/mute handled by media key interceptor by default; volume shortcuts start unset)
    /// Users can change any of these in Settings → Hotkeys (stored in AppSettings.Shortcuts).
    /// </summary>
    public static readonly HotkeyBinding[] DefaultBindings = new[]
    {
        new HotkeyBinding(HotkeyAction.BrightnessUp,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Up),
        new HotkeyBinding(HotkeyAction.BrightnessDown,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Down),
        new HotkeyBinding(HotkeyAction.BrightnessFineUp,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, Keys.Up),
        new HotkeyBinding(HotkeyAction.BrightnessFineDown,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, Keys.Down),
        new HotkeyBinding(HotkeyAction.ContrastUp,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Right),
        new HotkeyBinding(HotkeyAction.ContrastDown,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.Left),
    };

    public HotkeyManager(IntPtr windowHandle, HotkeyPressedHandler handler)
    {
        _windowHandle = windowHandle;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public bool RegisterHotkey(HotkeyBinding binding)
    {
        int id = _nextId++;
        if (NativeMethods.RegisterHotKey(_windowHandle, id, binding.Modifiers, (uint)binding.Key))
        {
            _registeredHotkeys[id] = binding.Action;
            return true;
        }
        return false;
    }

    public void RegisterAll(IEnumerable<HotkeyBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            RegisterHotkey(binding);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredHotkeys.Keys)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
        }
        _registeredHotkeys.Clear();
    }

    /// <summary>
    /// Call from WndProc to handle WM_HOTKEY messages.
    /// </summary>
    public bool ProcessHotkey(int hotkeyId)
    {
        if (_registeredHotkeys.TryGetValue(hotkeyId, out var action))
        {
            _handler(action);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            UnregisterAll();
        }
    }

    // --- Display helpers ---

    public static string ActionName(HotkeyAction action) => action switch
    {
        HotkeyAction.BrightnessUp => "Brightness up",
        HotkeyAction.BrightnessDown => "Brightness down",
        HotkeyAction.BrightnessFineUp => "Brightness up (fine)",
        HotkeyAction.BrightnessFineDown => "Brightness down (fine)",
        HotkeyAction.VolumeUp => "Volume up",
        HotkeyAction.VolumeDown => "Volume down",
        HotkeyAction.VolumeFineUp => "Volume up (fine)",
        HotkeyAction.VolumeFineDown => "Volume down (fine)",
        HotkeyAction.VolumeMute => "Mute",
        HotkeyAction.ContrastUp => "Contrast up",
        HotkeyAction.ContrastDown => "Contrast down",
        _ => action.ToString()
    };

    /// <summary>Key cap labels for a binding, modifiers first, e.g. ["Ctrl", "Alt", "↑"].</summary>
    public static string[] DescribeKeys(uint modifiers, Keys key)
    {
        var caps = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) caps.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) caps.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) caps.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) caps.Add("Win");
        if (key != Keys.None) caps.Add(KeyName(key));
        return caps.ToArray();
    }

    public static string Describe(HotkeyBinding binding) =>
        string.Join(" + ", DescribeKeys(binding.Modifiers, binding.Key));

    public static string KeyName(Keys key)
    {
        switch (key)
        {
            case Keys.Up: return "↑";
            case Keys.Down: return "↓";
            case Keys.Left: return "←";
            case Keys.Right: return "→";
            case Keys.PageUp: return "PgUp";
            case Keys.PageDown: return "PgDn";
            case Keys.Insert: return "Ins";
            case Keys.Delete: return "Del";
            case Keys.Enter: return "Enter";
            case Keys.Back: return "Backspace";
            case Keys.Add: return "Num +";
            case Keys.Subtract: return "Num −";
            case Keys.Multiply: return "Num *";
            case Keys.Divide: return "Num /";
            case Keys.Decimal: return "Num .";
        }
        if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return $"Num {key - Keys.NumPad0}";
        if (key >= Keys.Oem1 && key <= Keys.Oem102 || key is Keys.Oemplus or Keys.Oemcomma or Keys.OemMinus or Keys.OemPeriod)
        {
            // Punctuation keys: ask the active keyboard layout which character they produce
            uint ch = MapVirtualKey((uint)key, MAPVK_VK_TO_CHAR) & 0x7FFF;
            if (ch != 0) return char.ToUpperInvariant((char)ch).ToString();
        }
        return key.ToString();
    }

    private const uint MAPVK_VK_TO_CHAR = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
