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
    ///   (Volume/mute handled by media key interceptor by default)
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
}
