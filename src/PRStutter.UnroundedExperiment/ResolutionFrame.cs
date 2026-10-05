using System;
using System.Collections.Generic;
using PRStutter.GridExperiment;

namespace PRStutter.UnroundedExperiment;

internal sealed class ResolutionFrame
{
    private readonly RenderFrameGate _gate = new();
    private readonly HashSet<long> _begun = new(), _rendered = new();
    private readonly int _cameraCount;
    private bool _finalBegun;
    public int CompletedFrames { get; private set; }
    public ResolutionFrame(int cameraCount)
    {
        if (cameraCount <= 0) throw new ArgumentOutOfRangeException(nameof(cameraCount));
        _cameraCount = cameraCount;
    }
    public bool Ready(int frame) => _gate.AcceptCallback(frame);
    public void Prepared(int frame)
    {
        _begun.Clear(); _rendered.Clear(); _finalBegun = false; _gate.Prepared(frame);
    }
    public void Restored() => _gate.Restored();
    public void Before(int frame, long pass, bool final)
    {
        if (!Ready(frame)) return;
        if (final) {
            if (_finalBegun || _rendered.Count != _cameraCount) throw new InvalidOperationException("Incomplete or repeated compositor draw");
            _finalBegun = true;
        } else if (_finalBegun || !_begun.Add(pass) || _begun.Count > _cameraCount) throw new InvalidOperationException("Unexpected/repeated field draw");
    }
    public bool After(int frame, long pass, bool final)
    {
        if (!Ready(frame)) return false;
        if (final) {
            if (!_finalBegun || _rendered.Count != _cameraCount) throw new InvalidOperationException("Compositor post-render without complete preparation");
            _finalBegun = false; CompletedFrames++; return true;
        }
        if (_finalBegun || !_begun.Contains(pass) || !_rendered.Add(pass)) throw new InvalidOperationException("Unexpected field post-render");
        return false;
    }
}
