using System;

namespace LumiDisplay;

/// <summary>
/// Percentage-based stepping shared by the keyboard, tray menu and settings window.
///
/// Every press moves to the next multiple of the step on a 0–100% grid, so with the default
/// 10% step brightness goes 60 → 70 → 80 rather than drifting through odd values; a value that is
/// off the grid (say 63% set on the monitor itself) snaps to 70% going up or 60% going down.
///
/// With "Sync contrast with brightness" on, contrast follows every brightness change by the same
/// number of percentage points, so a manual contrast offset is kept.
/// </summary>
internal static class DisplayControl
{
    /// <summary>Synced contrast never goes below this, so a dark screen stays readable.</summary>
    public const double MinSyncedContrastPercent = 10;

    public static double ToPercent(uint raw, uint max) => max == 0 ? 0 : raw * 100.0 / max;

    public static uint ToRaw(double percent, uint max) =>
        (uint)Math.Clamp(Math.Round(percent * max / 100.0), 0, max);

    /// <summary>The next grid value from <paramref name="currentPercent"/> in the given direction.</summary>
    public static double NextPercent(double currentPercent, double stepPercent, int direction)
    {
        if (stepPercent <= 0) return currentPercent;
        double position = currentPercent / stepPercent;
        double next = direction > 0
            ? Math.Floor(position + 1e-6) + 1
            : Math.Ceiling(position - 1e-6) - 1;
        return Math.Clamp(next * stepPercent, 0, 100);
    }

    public readonly record struct Change(double OldPercent, double NewPercent);

    /// <summary>
    /// Moves a VCP-like value (brightness, contrast, volume) by |<paramref name="steps"/>| grid steps
    /// in the sign's direction, with one read and one write. Several queued key presses arrive here
    /// as one call. Null when unsupported.
    /// <paramref name="announce"/> gets the new percent before the (slow) write, so the OSD can show
    /// it at once; if the write then fails it gets the old percent back.
    /// </summary>
    private static Change? Step(Func<(uint current, uint max)?> get, Func<uint, bool> set, double stepPercent, int steps,
        Action<double>? announce)
    {
        if (steps == 0) return null;
        var cur = get();
        if (cur == null || cur.Value.max == 0) return null;
        double oldPct = ToPercent(cur.Value.current, cur.Value.max);
        double newPct = oldPct;
        for (int i = 0; i < Math.Abs(steps); i++)
            newPct = NextPercent(newPct, stepPercent, Math.Sign(steps));
        uint raw = ToRaw(newPct, cur.Value.max);
        if (raw == cur.Value.current)
        {
            announce?.Invoke(oldPct);
            return new Change(oldPct, oldPct);
        }
        return Write(set, raw, cur.Value.max, oldPct, announce);
    }

    private static Change? SetPercent(Func<(uint current, uint max)?> get, Func<uint, bool> set, double percent,
        Action<double>? announce)
    {
        var cur = get();
        if (cur == null || cur.Value.max == 0) return null;
        double oldPct = ToPercent(cur.Value.current, cur.Value.max);
        return Write(set, ToRaw(percent, cur.Value.max), cur.Value.max, oldPct, announce);
    }

    private static Change? Write(Func<uint, bool> set, uint raw, uint max, double oldPct, Action<double>? announce)
    {
        double newPct = ToPercent(raw, max);
        announce?.Invoke(newPct);
        if (set(raw)) return new Change(oldPct, newPct);
        announce?.Invoke(oldPct);
        return null;
    }

    // "steps" is signed: +1 = one step up, -3 = three steps down.

    public static Change? StepBrightness(IMonitor m, double stepPercent, int steps, bool syncContrast,
        Action<double>? announce = null)
    {
        if (!m.SupportsBrightness) return null;
        var change = Step(m.GetBrightness, m.SetBrightness, stepPercent, steps, announce);
        if (change != null && syncContrast) FollowWithContrast(m, change.Value);
        return change;
    }

    public static Change? SetBrightnessPercent(IMonitor m, double percent, bool syncContrast,
        Action<double>? announce = null)
    {
        if (!m.SupportsBrightness) return null;
        var change = SetPercent(m.GetBrightness, m.SetBrightness, percent, announce);
        if (change != null && syncContrast) FollowWithContrast(m, change.Value);
        return change;
    }

    public static Change? StepContrast(IMonitor m, double stepPercent, int steps, Action<double>? announce = null) =>
        m.SupportsContrast ? Step(m.GetContrast, m.SetContrast, stepPercent, steps, announce) : null;

    public static Change? SetContrastPercent(IMonitor m, double percent, Action<double>? announce = null) =>
        m.SupportsContrast ? SetPercent(m.GetContrast, m.SetContrast, percent, announce) : null;

    public static Change? StepVolume(IMonitor m, double stepPercent, int steps, Action<double>? announce = null) =>
        m.SupportsVolume ? Step(m.GetVolume, m.SetVolume, stepPercent, steps, announce) : null;

    public static Change? SetVolumePercent(IMonitor m, double percent, Action<double>? announce = null) =>
        m.SupportsVolume ? SetPercent(m.GetVolume, m.SetVolume, percent, announce) : null;

    /// <summary>Moves contrast by the same number of percentage points brightness just moved.</summary>
    private static void FollowWithContrast(IMonitor m, Change brightness)
    {
        double delta = brightness.NewPercent - brightness.OldPercent;
        if (Math.Abs(delta) < 0.01 || !m.SupportsContrast) return;

        var cur = m.GetContrast();
        if (cur == null || cur.Value.max == 0) return;
        double oldPct = ToPercent(cur.Value.current, cur.Value.max);
        double target = Math.Clamp(oldPct + delta, Math.Min(oldPct, MinSyncedContrastPercent), 100);
        uint raw = ToRaw(target, cur.Value.max);
        if (raw != cur.Value.current)
            m.SetContrast(raw);
    }
}
