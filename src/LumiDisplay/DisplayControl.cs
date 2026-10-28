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

    /// <summary>One step of a VCP-like value (brightness, contrast, volume). Null when unsupported or unchanged.</summary>
    private static Change? Step(Func<(uint current, uint max)?> get, Func<uint, bool> set, double stepPercent, int direction)
    {
        var cur = get();
        if (cur == null || cur.Value.max == 0) return null;
        double oldPct = ToPercent(cur.Value.current, cur.Value.max);
        double newPct = NextPercent(oldPct, stepPercent, direction);
        uint raw = ToRaw(newPct, cur.Value.max);
        if (raw == cur.Value.current) return new Change(oldPct, oldPct);
        return set(raw) ? new Change(oldPct, ToPercent(raw, cur.Value.max)) : null;
    }

    private static Change? SetPercent(Func<(uint current, uint max)?> get, Func<uint, bool> set, double percent)
    {
        var cur = get();
        if (cur == null || cur.Value.max == 0) return null;
        double oldPct = ToPercent(cur.Value.current, cur.Value.max);
        uint raw = ToRaw(percent, cur.Value.max);
        return set(raw) ? new Change(oldPct, ToPercent(raw, cur.Value.max)) : null;
    }

    public static Change? StepBrightness(IMonitor m, double stepPercent, int direction, bool syncContrast)
    {
        var change = Step(m.GetBrightness, m.SetBrightness, stepPercent, direction);
        if (change != null && syncContrast) FollowWithContrast(m, change.Value);
        return change;
    }

    public static Change? SetBrightnessPercent(IMonitor m, double percent, bool syncContrast)
    {
        var change = SetPercent(m.GetBrightness, m.SetBrightness, percent);
        if (change != null && syncContrast) FollowWithContrast(m, change.Value);
        return change;
    }

    public static Change? StepContrast(IMonitor m, double stepPercent, int direction) =>
        Step(m.GetContrast, m.SetContrast, stepPercent, direction);

    public static Change? StepVolume(IMonitor m, double stepPercent, int direction) =>
        Step(m.GetVolume, m.SetVolume, stepPercent, direction);

    public static Change? SetVolumePercent(IMonitor m, double percent) =>
        SetPercent(m.GetVolume, m.SetVolume, percent);

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
