using System;

namespace PRStutter.RenderExperiment;

// A short-lived override: only restore values that still equal our own writes.
internal sealed class UvOverride
{
    private readonly Func<int, float> _read;
    private readonly Action<int, float> _write;
    private readonly float[] _original, _applied = new float[4];
    private bool _pending;

    public UvOverride(float[] original, Func<int, float> read, Action<int, float> write)
    {
        _original = (float[])original.Clone();
        _read = read; _write = write;
    }

    public bool Apply(float u, float v)
    {
        Restore();
        if (!float.IsFinite(u) || !float.IsFinite(v)) return false;
        for (int i = 0; i < 4; i++)
        {
            float current = _read(i);
            if (!float.IsFinite(current) || Math.Abs(current - _original[i]) > .00001f) return false;
            _applied[i] = _original[i] + (i < 2 ? u : v);
        }
        _pending = true; // Includes recovery after a partially completed write.
        for (int i = 0; i < 4; i++) _write(i, _applied[i]);
        return true;
    }

    public void Restore()
    {
        if (!_pending) return;
        Exception? failure = null;
        for (int i = 0; i < 4; i++)
        {
            try { if (_read(i) == _applied[i]) _write(i, _original[i]); }
            catch (Exception e) { failure ??= e; }
        }
        // Keep a failed restore pending for the next Update retry.
        if (failure != null) throw failure;
        _pending = false;
    }
}
