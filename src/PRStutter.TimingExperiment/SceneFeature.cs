using System;

namespace PRStutter.TimingExperiment;

// Expected context loss is resumable; unexpected stops/start/cleanup failures
// latch until an explicit new run. A context change alone never clears a fault.
internal sealed class SceneFeature
{
    private readonly ITestFeature _feature;
    private readonly Func<string, bool> _expectedStop;
    private string _identity = "";
    private bool _expectedActive;
    public bool Faulted { get; private set; }
    public string Status { get; private set; } = "waiting";
    public bool Active => _feature.Active;
    public SceneFeature(ITestFeature feature, Func<string, bool> expectedStop)
    { _feature = feature; _expectedStop = expectedStop; }
    public void Reconcile(string identity, bool eligible, string reason)
    {
        if (Faulted) return;
        if (_expectedActive && !_feature.Active) {
            _expectedActive = false;
            if (!_expectedStop(_feature.StopReason)) { Fail("unexpected stop: " + _feature.StopReason); return; }
            Suspend("context unavailable");
            if (Faulted) return;
        }
        if (_identity != identity || !eligible) {
            if (_expectedActive || _feature.Active) Suspend("context changed");
            _identity = identity;
            if (Faulted) return;
        }
        if (!eligible) { Status = "suspended: " + reason; return; }
        if (_feature.Active) { Status = "active"; return; }
        try {
            _feature.Start(long.MaxValue);
            if (!_feature.Active) throw new InvalidOperationException("component declined start");
            _expectedActive = true; Status = "active";
        } catch (Exception e) { Fail("startup failed: " + e.Message); }
    }
    public void Suspend(string reason)
    {
        _expectedActive = false;
        try { _feature.Stop(reason); if (!Faulted) Status = "suspended: " + reason; }
        catch (Exception e) { Faulted = true; Status = "cleanup failed: " + e.Message; }
    }
    private void Fail(string reason)
    {
        Faulted = true; Status = reason;
        Suspend(reason); // Own and clean partial startup even when Active is false.
    }
}
