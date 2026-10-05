namespace PRStutter.TimingExperiment;

// Two distinct fresh field-update frames establish a context. Repeated reads in
// one frame cannot arm it. An unsupported gap clears readiness immediately.
internal sealed class ContextReadiness
{
    private string _identity = "";
    private int _lastFrame = -1, _count;
    public bool Ready => _count >= 2;
    public void Observe(string identity, int fieldFrame, bool eligible)
    {
        if (!eligible) { Reset(); return; }
        if (_identity != identity || fieldFrame < _lastFrame) { Reset(); _identity = identity; }
        if (fieldFrame == _lastFrame) return;
        _lastFrame = fieldFrame; _count = System.Math.Min(2, _count + 1);
    }
    public void Reset() { _identity = ""; _lastFrame = -1; _count = 0; }
}
