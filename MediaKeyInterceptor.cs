using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumiDisplay;

/// <summary>
/// Intercepts media keys (volume up/down/mute) via a low-level keyboard hook.
/// Mirrors BetterDisplay's CGEventTap that captures NX_KEYTYPE_SOUND_UP (0),
/// NX_KEYTYPE_SOUND_DOWN (1), NX_KEYTYPE_MUTE (7) before the system handles them.
/// 
/// On Windows, we use WH_KEYBOARD_LL to intercept VK_VOLUME_UP (0xAF),
/// VK_VOLUME_DOWN (0xAE), and VK_VOLUME_MUTE (0xAD).
/// </summary>
public sealed class MediaKeyInterceptor : IDisposable
{
    public enum MediaKeyAction
    {
        VolumeUp,
        VolumeDown,
        VolumeMute,
    }

    public delegate bool MediaKeyHandler(MediaKeyAction action);

    private IntPtr _hookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly MediaKeyHandler _handler;
    private bool _enabled;
    private bool _disposed;

    /// <summary>
    /// When true, media keys are intercepted and redirected to the handler.
    /// When false, media keys pass through to the system as normal.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public MediaKeyInterceptor(MediaKeyHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _hookProc = HookCallback; // prevent GC
        _enabled = true;
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;

        // Use GetModuleHandle(null) for global LL hook within the current process
        IntPtr hMod = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            hMod,
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook: {Marshal.GetLastWin32Error()}");
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled &&
            (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var hookData = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            MediaKeyAction? action = hookData.vkCode switch
            {
                NativeMethods.VK_VOLUME_UP => MediaKeyAction.VolumeUp,
                NativeMethods.VK_VOLUME_DOWN => MediaKeyAction.VolumeDown,
                NativeMethods.VK_VOLUME_MUTE => MediaKeyAction.VolumeMute,
                _ => null
            };

            if (action != null)
            {
                // If handler returns true, we consumed the key (suppress system handling).
                // Wrapped in try/catch to ensure hook callback never throws into the Win32 message loop.
                try
                {
                    if (_handler(action.Value))
                    {
                        return (IntPtr)1; // Suppress the key
                    }
                }
                catch
                {
                    // Fallthrough to pass key to system on unexpected handler errors
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Uninstall();
        }
    }
}
