using System;
using System.Collections.Generic;

namespace PRStutter.TimingExperiment;

internal interface ITestFeature
{
    string Name { get; }
    bool Active { get; }
    string StopReason { get; }
    void Start(long deadline);
    void Stop(string reason);
}

// The real coordinator and offline checks use this same ownership/timeout policy.
internal sealed class CombinedRun
{
    private readonly ITestFeature[] _features;
    public bool Active { get; private set; }
    public long Deadline { get; private set; }
    public string LastStop { get; private set; } = "Ready; CRT must be off";
    public CombinedRun(params ITestFeature[] features) { _features = features; }
    public void Start(long now, long duration)
    {
        if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        Stop("reset for combined test");
        Deadline = checked(now + duration);
        try {
            foreach (var feature in _features) {
                feature.Start(Deadline);
                if (!feature.Active) throw new InvalidOperationException(feature.Name + " did not start");
            }
            Active = true; LastStop = "";
        } catch (Exception e) {
            Stop("start failed: " + e.Message);
            throw;
        }
    }
    public void Poll(long now)
    {
        if (!Active) return;
        if (now >= Deadline) { Stop("15-second test complete"); return; }
        foreach (var feature in _features)
            if (!feature.Active) { Stop(feature.Name + " stopped: " + feature.StopReason); return; }
    }
    public void Stop(string reason)
    {
        Active = false; LastStop = reason;
        List<Exception>? errors = null;
        // Attempt every restoration even if an earlier one fails.
        foreach (var feature in _features) {
            try { feature.Stop(reason); }
            catch (Exception e) { (errors ??= new()).Add(e); }
        }
        if (errors != null) {
            LastStop = reason + "; cleanup failed (see log)";
            throw new AggregateException(LastStop, errors);
        }
    }
}
