using System;

namespace PRStutter.GridExperiment;

// LateUpdate execution order is unspecified between the automatic controller and
// render driver. A newly attached pass leaves stock rendering alone until the
// first successful Prepare. After arming, missing preparation remains a fault.
internal sealed class RenderFrameGate
{
    public bool Armed { get; private set; }
    public int PreparedFrame { get; private set; } = -1;
    public void Prepared(int frame) { PreparedFrame = frame; Armed = true; }
    public void Restored() => PreparedFrame = -1;
    public bool AcceptCallback(int frame)
    {
        if (!Armed) return false;
        if (PreparedFrame != frame) throw new InvalidOperationException(
            $"Camera callback without prepared targets: currentFrame={frame}, preparedFrame={PreparedFrame}.");
        return true;
    }
}
