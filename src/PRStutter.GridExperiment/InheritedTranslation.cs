using System;
using System.Collections.Generic;
using PRStutter.RenderExperiment;

namespace PRStutter.GridExperiment;

// Translate a hierarchy once. Descendants provide readback only, never setters.
internal sealed class InheritedTranslation<T>
{
    private readonly ScopedOverride<T> _root;
    private readonly List<Func<T>> _reads = new();
    private readonly List<T> _originals = new();
    private readonly Func<T, T, T> _add;
    private readonly Func<T, T, bool> _equal;
    private T _offset = default!;
    public InheritedTranslation(Func<T> readRoot, Action<T> writeRoot,
        IEnumerable<Func<T>> readDescendants, Func<T, T, T> add, Func<T, T, bool> equal)
    {
        _add = add; _equal = equal;
        _root = new ScopedOverride<T>(readRoot, writeRoot, equal);
        _reads.Add(readRoot); _reads.AddRange(readDescendants);
        foreach (var read in _reads) _originals.Add(read());
    }
    public void Apply(T offset)
    {
        if (_root.Pending) throw new InvalidOperationException("Hierarchy translation is already applied.");
        for (int i = 0; i < _reads.Count; i++)
            if (!_equal(_reads[i](), _originals[i])) throw new InvalidOperationException($"Hierarchy baseline changed at index {i}.");
        _offset = offset;
        _root.Apply(_add(_originals[0], offset));
        Validate();
    }
    public void Validate()
    {
        if (!_root.Pending) return;
        for (int i = 0; i < _reads.Count; i++) {
            T actual = _reads[i](), expected = _add(_originals[i], _offset);
            if (!_equal(actual, expected))
                throw new InvalidOperationException($"Inherited translation mismatch at index {i}: expected={expected}, actual={actual}.");
        }
    }
    public void Restore() => _root.Restore();
}
