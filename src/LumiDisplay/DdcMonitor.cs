using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LumiDisplay;

/// <summary>
/// DDC/CI monitor control via Windows Low-Level Monitor Configuration API (dxva2.dll).
/// Uses standard MCCS VCP codes matching BetterDisplay's DDC controller:
///   0x10 = Luminance (Brightness)
///   0x12 = Contrast
///   0x62 = Audio Speaker Volume
///   0x8D = Audio Mute
/// </summary>
public sealed class DdcMonitor : IMonitor
{
    // --- VCP Code constants (MCCS / VESA Monitor Control Command Set) ---
    public const byte VCP_BRIGHTNESS = 0x10;
    public const byte VCP_CONTRAST   = 0x12;
    public const byte VCP_VOLUME     = 0x62;
    public const byte VCP_MUTE       = 0x8D;

    // Mute VCP values (per MCCS spec, same as BetterDisplay uses)
    public const uint MUTE_ON  = 1;
    public const uint MUTE_OFF = 2;

    private readonly IntPtr _physicalMonitor;
    private readonly string _description;
    private bool _disposed;

    public string Description => _description;
    public IntPtr Handle => _physicalMonitor;
    public bool IsBuiltIn => false;
    public bool SupportsBrightness => true;
    public bool SupportsContrast => true;
    public bool SupportsVolume => true;

    private DdcMonitor(IntPtr physicalMonitor, string description)
    {
        _physicalMonitor = physicalMonitor;
        _description = description;
    }

    /// <summary>
    /// Enumerate all DDC/CI capable physical monitors on the system.
    /// </summary>
    public static List<DdcMonitor> EnumerateMonitors()
    {
        var monitors = new List<DdcMonitor>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                    {
                        foreach (var pm in physicalMonitors)
                        {
                            monitors.Add(new DdcMonitor(pm.hPhysicalMonitor, pm.szPhysicalMonitorDescription));
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

        return monitors;
    }

    /// <summary>
    /// Get the current and maximum value for a VCP feature.
    /// Returns (currentValue, maxValue) or null if the read fails.
    /// </summary>
    public (uint current, uint max)? GetVcpValue(byte vcpCode)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DdcMonitor));

        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                _physicalMonitor, vcpCode,
                out _, out uint currentValue, out uint maxValue))
        {
            return (currentValue, maxValue);
        }
        return null;
    }

    /// <summary>
    /// Set a VCP feature to a specific value.
    /// </summary>
    public bool SetVcpValue(byte vcpCode, uint value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DdcMonitor));
        return NativeMethods.SetVCPFeature(_physicalMonitor, vcpCode, value);
    }

    // --- Convenience methods matching BetterDisplay's controls ---

    public (uint current, uint max)? GetBrightness() => GetVcpValue(VCP_BRIGHTNESS);
    public bool SetBrightness(uint value) => SetVcpValue(VCP_BRIGHTNESS, value);

    public (uint current, uint max)? GetContrast() => GetVcpValue(VCP_CONTRAST);
    public bool SetContrast(uint value) => SetVcpValue(VCP_CONTRAST, value);

    public (uint current, uint max)? GetVolume() => GetVcpValue(VCP_VOLUME);
    public bool SetVolume(uint value) => SetVcpValue(VCP_VOLUME, value);

    public bool? GetMute()
    {
        var result = GetVcpValue(VCP_MUTE);
        if (result == null) return null;
        return result.Value.current == MUTE_ON;
    }

    public bool SetMute(bool muted) => SetVcpValue(VCP_MUTE, muted ? MUTE_ON : MUTE_OFF);
    public bool ToggleMute()
    {
        var current = GetMute();
        if (current == null) return false;
        return SetMute(!current.Value);
    }

    ~DdcMonitor()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_physicalMonitor != IntPtr.Zero)
            {
                NativeMethods.DestroyPhysicalMonitor(_physicalMonitor);
            }
        }
    }

    public override string ToString() => $"{_description} (0x{_physicalMonitor:X})";
}

/// <summary>
/// P/Invoke declarations for dxva2.dll (Monitor Configuration) and user32.dll.
/// </summary>
internal static partial class NativeMethods
{
    // --- dxva2.dll: Low-Level Monitor Configuration ---

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwPhysicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr hMonitor, byte bVCPCode,
        out uint pvct, out uint pdwCurrentValue, out uint pdwMaximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetVCPFeature(
        IntPtr hMonitor, byte bVCPCode, uint dwNewValue);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    // --- user32.dll: Monitor and display enumeration ---

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // --- user32.dll: Global hotkey registration ---

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Hotkey modifier flags
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // --- user32.dll: Low-level keyboard hook (for media keys) ---

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_APPCOMMAND = 0x0319;

    // Virtual key codes for media keys
    public const uint VK_VOLUME_MUTE = 0xAD;
    public const uint VK_VOLUME_DOWN = 0xAE;
    public const uint VK_VOLUME_UP = 0xAF;
    public const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    public const uint VK_MEDIA_PREV_TRACK = 0xB1;
    public const uint VK_MEDIA_STOP = 0xB2;
    public const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    // APPCOMMAND values (shifted >> 16)
    public const int APPCOMMAND_VOLUME_MUTE = 8;
    public const int APPCOMMAND_VOLUME_DOWN = 9;
    public const int APPCOMMAND_VOLUME_UP = 10;
    public const int APPCOMMAND_BROWSER_BACKWARD = 1;
    public const int APPCOMMAND_BROWSER_FORWARD = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // --- user32.dll: Window layering for OSD ---

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_TOPMOST = 0x8;
    public const int WS_EX_NOACTIVATE = 0x8000000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
}
