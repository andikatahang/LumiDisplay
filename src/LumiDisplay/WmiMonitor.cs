using System;
using System.Collections.Generic;
using System.Management;

namespace LumiDisplay;

/// <summary>
/// Controls built-in laptop panel brightness via Windows Management Instrumentation (WMI).
/// Queries root\wmi: WmiMonitorBrightness and calls WmiMonitorBrightnessMethods.WmiSetBrightness.
/// 
/// This corresponds to BetterDisplay's AppleBuiltInDirectController / DisplayServices path
/// used for Apple Silicon / Intel MacBook built-in screens.
/// </summary>
public sealed class WmiMonitor : IMonitor
{
    private readonly string _instanceName;
    private readonly string _friendlyName;
    private bool _disposed;

    public string Description => _friendlyName;
    public bool IsBuiltIn => true;
    public bool SupportsBrightness => true;
    public bool SupportsContrast => false;
    public bool SupportsVolume => false;

    private WmiMonitor(string instanceName)
    {
        _instanceName = instanceName;
        _friendlyName = FormatFriendlyName(instanceName);
    }

    private static string FormatFriendlyName(string instanceName)
    {
        // Example instance name: DISPLAY\LEN40A0\4&28f24b0c&0&UID265988_0
        if (string.IsNullOrWhiteSpace(instanceName))
            return "Built-in Display";

        var parts = instanceName.Split('\\');
        if (parts.Length >= 2)
            return $"Built-in Display ({parts[1]})";

        return "Built-in Display";
    }

    /// <summary>
    /// Enumerate all WMI-capable built-in displays (e.g. laptop screens).
    /// Returns an empty list on desktops without internal displays.
    /// </summary>
    public static List<WmiMonitor> EnumerateMonitors()
    {
        var monitors = new List<WmiMonitor>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\wmi"),
                new SelectQuery("WmiMonitorBrightness"));

            using var collection = searcher.Get();
            foreach (ManagementObject mo in collection)
            {
                using (mo)
                {
                    var inst = mo["InstanceName"]?.ToString();
                    if (!string.IsNullOrEmpty(inst))
                    {
                        monitors.Add(new WmiMonitor(inst));
                    }
                }
            }
        }
        catch
        {
            // Expected on desktop PCs without WMI brightness support
        }
        return monitors;
    }

    public (uint current, uint max)? GetBrightness()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WmiMonitor));
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\wmi"),
                new SelectQuery("WmiMonitorBrightness"));

            using var collection = searcher.Get();
            foreach (ManagementObject mo in collection)
            {
                using (mo)
                {
                    if (mo["InstanceName"]?.ToString() == _instanceName)
                    {
                        uint cur = Convert.ToUInt32(mo["CurrentBrightness"]);
                        return (cur, 100);
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public bool SetBrightness(uint value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WmiMonitor));
        try
        {
            value = Math.Clamp(value, 0, 100);
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\wmi"),
                new SelectQuery("WmiMonitorBrightnessMethods"));

            using var collection = searcher.Get();
            foreach (ManagementObject mo in collection)
            {
                using (mo)
                {
                    if (mo["InstanceName"]?.ToString() == _instanceName)
                    {
                        // WmiSetBrightness(uint32 Timeout, uint8 Brightness)
                        mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)value });
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    // Built-in displays do not support hardware contrast via WMI
    public (uint current, uint max)? GetContrast() => null;
    public bool SetContrast(uint value) => false;

    // Built-in displays route audio to the OS default audio device
    public (uint current, uint max)? GetVolume() => null;
    public bool SetVolume(uint value) => false;
    public bool? GetMute() => null;
    public bool SetMute(bool muted) => false;
    public bool ToggleMute() => false;

    public void Dispose()
    {
        _disposed = true;
    }

    public override string ToString() => $"{_friendlyName} [WMI]";
}
