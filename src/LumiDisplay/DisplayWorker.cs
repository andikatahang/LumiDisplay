using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace LumiDisplay;

/// <summary>
/// Runs all key-driven DDC/CI work on one background thread.
///
/// DDC/CI reads and writes take ~50 ms each (more with retries). Doing them inside the
/// low-level keyboard hook or on the UI thread stalls input, and Windows silently removes a
/// keyboard hook that keeps answering late. Callers enqueue a job and return at once; the
/// worker merges presses that piled up (three "brightness up" become one jump of three
/// steps; only the latest absolute value of a slider drag is sent) and reports back to the
/// UI thread for the OSD.
/// </summary>
public sealed class DisplayWorker : IDisposable
{
    public enum Control { Brightness, Contrast, Volume }

    private abstract record Job;
    private sealed record StepJob(Control Control, IMonitor[] Targets, int StepPercent, int Steps, bool Sync, bool ShowOsd) : Job;
    private sealed record SetJob(Control Control, IMonitor[] Targets, double Percent, bool Sync, bool ShowOsd) : Job;
    private sealed record MuteJob(IMonitor[] Targets) : Job;
    private sealed record WorkJob(Action Work) : Job;

    private readonly BlockingCollection<Job> _queue = new();
    private readonly Thread _thread;
    private readonly SynchronizationContext _ui;
    private readonly Action<OsdOverlay.OsdType, double> _showOsd;
    private readonly Action<bool> _showMute;

    public DisplayWorker(SynchronizationContext ui, Action<OsdOverlay.OsdType, double> showOsd, Action<bool> showMute)
    {
        _ui = ui;
        _showOsd = showOsd;
        _showMute = showMute;
        _thread = new Thread(Loop) { IsBackground = true, Name = "LumiDisplay DDC/CI" };
        _thread.Start();
    }

    // --- Enqueue (any thread) ---

    public void Step(Control control, IReadOnlyList<IMonitor> targets, int stepPercent, int direction,
        bool syncContrast = false, bool showOsd = true) =>
        Add(new StepJob(control, Snapshot(targets), stepPercent, direction, syncContrast, showOsd));

    public void SetPercent(Control control, IReadOnlyList<IMonitor> targets, double percent,
        bool syncContrast = false, bool showOsd = true) =>
        Add(new SetJob(control, Snapshot(targets), percent, syncContrast, showOsd));

    public void ToggleMute(IReadOnlyList<IMonitor> targets) => Add(new MuteJob(Snapshot(targets)));

    /// <summary>Any other monitor work (e.g. re-enumerating displays), in order with the rest.</summary>
    public void Run(Action work) => Add(new WorkJob(work));

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it.</summary>
    public void InvokeOnUi(Action action) => _ui.Send(_ => action(), null);

    private static IMonitor[] Snapshot(IReadOnlyList<IMonitor> targets)
    {
        var copy = new IMonitor[targets.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = targets[i];
        return copy;
    }

    private void Add(Job job)
    {
        try { _queue.Add(job); }
        catch (InvalidOperationException) { } // shutting down
    }

    // --- Worker thread ---

    private void Loop()
    {
        foreach (var first in _queue.GetConsumingEnumerable())
        {
            var batch = new List<Job> { first };
            while (_queue.TryTake(out var more)) batch.Add(more);

            foreach (var job in Coalesce(batch))
            {
                try { Execute(job); }
                catch (ObjectDisposedException) { } // the monitor was unplugged/replaced meanwhile
                catch (Exception) { }               // never let one bad monitor stop the worker
            }
        }
    }

    /// <summary>Merges consecutive presses of the same control, and consecutive absolute sets.</summary>
    private static List<Job> Coalesce(List<Job> batch)
    {
        var result = new List<Job>();
        foreach (var job in batch)
        {
            var last = result.Count > 0 ? result[^1] : null;
            if (job is StepJob s && last is StepJob p && p.Control == s.Control && p.StepPercent == s.StepPercent
                && p.Sync == s.Sync && SameTargets(p.Targets, s.Targets))
            {
                result[^1] = p with { Steps = p.Steps + s.Steps, ShowOsd = p.ShowOsd || s.ShowOsd };
            }
            else if (job is SetJob set && last is SetJob prev && prev.Control == set.Control && SameTargets(prev.Targets, set.Targets))
            {
                result[^1] = set;
            }
            else
            {
                result.Add(job);
            }
        }
        return result;
    }

    private static bool SameTargets(IMonitor[] a, IMonitor[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private void Execute(Job job)
    {
        switch (job)
        {
            case StepJob s:
            {
                DisplayControl.Change? last = null;
                foreach (var m in s.Targets)
                {
                    var change = s.Control switch
                    {
                        Control.Brightness => DisplayControl.StepBrightness(m, s.StepPercent, s.Steps, s.Sync),
                        Control.Contrast => DisplayControl.StepContrast(m, s.StepPercent, s.Steps),
                        _ => DisplayControl.StepVolume(m, s.StepPercent, s.Steps)
                    };
                    last = change ?? last;
                }
                if (last != null && s.ShowOsd) ShowOsd(s.Control, last.Value.NewPercent);
                break;
            }
            case SetJob set:
            {
                DisplayControl.Change? last = null;
                foreach (var m in set.Targets)
                {
                    var change = set.Control switch
                    {
                        Control.Brightness => DisplayControl.SetBrightnessPercent(m, set.Percent, set.Sync),
                        Control.Contrast => DisplayControl.SetContrastPercent(m, set.Percent),
                        _ => DisplayControl.SetVolumePercent(m, set.Percent)
                    };
                    last = change ?? last;
                }
                if (last != null && set.ShowOsd) ShowOsd(set.Control, last.Value.NewPercent);
                break;
            }
            case MuteJob mute:
                foreach (var m in mute.Targets)
                {
                    if (!m.SupportsVolume) continue;
                    bool? wasMuted = m.GetMute();
                    if (m.ToggleMute())
                    {
                        bool nowMuted = !(wasMuted ?? false);
                        _ui.Post(_ => _showMute(nowMuted), null);
                    }
                }
                break;
            case WorkJob work:
                work.Work();
                break;
        }
    }

    private void ShowOsd(Control control, double percent)
    {
        var type = control switch
        {
            Control.Brightness => OsdOverlay.OsdType.Brightness,
            Control.Contrast => OsdOverlay.OsdType.Contrast,
            _ => OsdOverlay.OsdType.Volume
        };
        _ui.Post(_ => _showOsd(type, percent / 100.0), null);
    }

    public void Dispose()
    {
        // Let the current job finish; the queue is left for the GC because a job that is
        // still running after the timeout keeps using it.
        _queue.CompleteAdding();
        _thread.Join(1000);
    }
}
