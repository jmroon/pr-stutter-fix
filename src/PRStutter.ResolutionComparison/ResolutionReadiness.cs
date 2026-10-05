using System.Collections.Generic;

namespace PRStutter.UnroundedExperiment;

// One attempt after a stable eligible interval. A failed topology stays blocked
// until the context/settings change or eligibility is lost and reacquired.
internal sealed class ResolutionReadiness<TKey> where TKey : notnull
{
    private TKey _key = default!;
    private bool _hasKey;
    private double _since;
    private int _firstFrame;
    private bool _attempted;
    public void Reset() { _hasKey = false; _attempted = false; }
    public bool ShouldStart(TKey key, bool eligible, double now, int frame)
    {
        if (!eligible) { Reset(); return false; }
        if (!_hasKey || !EqualityComparer<TKey>.Default.Equals(_key, key)) {
            _hasKey = true; _key = key; _since = now; _firstFrame = frame; _attempted = false; return false;
        }
        if (_attempted || frame <= _firstFrame || now - _since < .25) return false;
        _attempted = true; return true;
    }
}
