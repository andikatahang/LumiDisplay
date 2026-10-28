using System;

namespace LumiDisplay;

/// <summary>
/// Common interface for display controllers.
/// Unifies external DDC/CI monitors (DdcMonitor) and built-in laptop panels (WmiMonitor),
/// matching BetterDisplay's dual controller architecture (AppleBuiltInDirectController vs DDCController).
/// </summary>
public interface IMonitor : IDisposable
{
    /// <summary>
    /// User-friendly description/name of the monitor.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// True if this is a built-in display (e.g. laptop panel via WMI).
    /// </summary>
    bool IsBuiltIn { get; }

    /// <summary>
    /// Whether this display supports hardware brightness control.
    /// </summary>
    bool SupportsBrightness { get; }

    /// <summary>
    /// Whether this display supports hardware contrast control.
    /// </summary>
    bool SupportsContrast { get; }

    /// <summary>
    /// Whether this display supports hardware speaker volume control.
    /// </summary>
    bool SupportsVolume { get; }

    (uint current, uint max)? GetBrightness();
    bool SetBrightness(uint value);

    (uint current, uint max)? GetContrast();
    bool SetContrast(uint value);

    (uint current, uint max)? GetVolume();
    bool SetVolume(uint value);

    bool? GetMute();
    bool SetMute(bool muted);
    bool ToggleMute();

    // Stepping (10% grid, contrast sync) lives in DisplayControl, on top of Get/Set above.
}
