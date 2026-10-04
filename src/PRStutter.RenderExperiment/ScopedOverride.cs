using System;
using System.Collections.Generic;

namespace PRStutter.RenderExperiment;

// Retain ownership until restoration succeeds, including a setter that writes then throws.
internal sealed class ScopedOverride<T>
{
    private readonly Func<T> _read;
    private readonly Action<T> _write;
    private readonly Func<T, T, bool> _equal;
    private T _original = default!, _applied = default!;
    public bool Pending { get; private set; }
    public ScopedOverride(Func<T> read, Action<T> write, Func<T, T, bool>? equal = null)
    { _read = read; _write = write; _equal = equal ?? EqualityComparer<T>.Default.Equals; }
    public void Apply(T value)
    {
        Restore();
        _original = _read(); _applied = value; Pending = true;
        _write(value);
    }
    public void Restore()
    {
        if (!Pending) return;
        if (_equal(_read(), _applied)) _write(_original);
        Pending = false;
    }
}
