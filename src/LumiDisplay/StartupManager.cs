using System;
using Microsoft.Win32;

namespace LumiDisplay;

/// <summary>
/// Manages Windows startup registration via HKCU Run registry key.
/// </summary>
public static class StartupManager
{
    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string APP_NAME = "LumiDisplay";

    /// <summary>
    /// Passed by the Run entry so a sign-in launch can stay in the tray,
    /// while a launch from the desktop/Start menu opens the window.
    /// </summary>
    public const string AutostartArg = "--autostart";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
            return key?.GetValue(APP_NAME) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rewrites an existing Run entry from older versions (no autostart flag, or a moved exe).
    /// </summary>
    public static void RefreshStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
            var current = key?.GetValue(APP_NAME) as string;
            if (current == null) return;
            if (current != $"\"{Environment.ProcessPath}\" {AutostartArg}")
                SetStartup(true);
        }
        catch { }
    }

    public static bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(APP_NAME, $"\"{exePath}\" {AutostartArg}");
                    return true;
                }
                return false;
            }
            else
            {
                key.DeleteValue(APP_NAME, false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
