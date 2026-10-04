using System;

namespace PRStutter.TimingExperiment;

// Independent lifetime for one correction. A failed attempt is latched until
// the observed context changes; cleanup is retried before any later start.
internal sealed class AutomaticFeature
{
    private readonly ITestFeature _feature;
    private string _context = "";
    private bool _expectedActive, _blocked, _cleanupPending;
    public string Status { get; private set; } = "waiting";
    public AutomaticFeature(ITestFeature feature) { _feature = feature; }
    public bool Active => _feature.Active;
    public void Reconcile(string context, bool enabled, bool eligible, string reason)
    {
        if (_context != context) {
            Suspend("context changed");
            _context = context; _blocked = false;
        }
        if (_cleanupPending) { Suspend(Status); if (_cleanupPending) return; }
        if (!enabled || !eligible) {
            Suspend(enabled ? reason : "disabled");
            return;
        }
        if (_expectedActive && !_feature.Active) {
            _blocked = true;
            Suspend(_feature.StopReason);
        }
        if (_blocked) return;
        if (_feature.Active) { Status = "active"; return; }
        try {
            _feature.Start(long.MaxValue);
            if (!_feature.Active) throw new InvalidOperationException("component declined start");
            _expectedActive = true; Status = "active";
        } catch (Exception e) { _blocked = true; Suspend("unsupported/fault: " + e.Message); }
    }
    public void Suspend(string reason)
    {
        _expectedActive = false; Status = reason;
        try { _feature.Stop(reason); _cleanupPending = false; }
        catch (Exception e) { _cleanupPending = true; Status = "cleanup pending: " + e.Message; }
    }
}

// Require observed player/camera agreement before enabling the existing
// following-camera algorithm. Idle in a new scene is insufficient evidence.
internal sealed class FollowReadiness
{
    private bool _have;
    private float _px, _py, _cx, _cy;
    private int _matches;
    public bool Ready => _matches >= 2;
    public void Reset() { _have = false; _matches = 0; }
    public void Observe(float px, float py, float cx, float cy)
    {
        if (!float.IsFinite(px + py + cx + cy)) { Reset(); return; }
        if (_have) {
            float dx = px - _px, dy = py - _py;
            if (Math.Abs((cx - _cx) - dx) > .001f || Math.Abs((cy - _cy) - dy) > .001f) _matches = 0;
            else if (dx != 0 || dy != 0) _matches = Math.Min(2, _matches + 1);
        }
        _have = true; _px = px; _py = py; _cx = cx; _cy = cy;
    }
}
