using System;

namespace PRStutter.RenderExperiment;

public static class MotionResidual
{
    // Ordinary adjacent-tile walking: 16 units on one or both axes.
    // Each axis keeps its own half-unit rounding bound, including diagonals.
    public static bool TryCalculate(float sx, float sy, float dx, float dy,
        float x, float y, float timer, float duration, out float rx, out float ry)
    {
        rx = ry = 0;
        if (!float.IsFinite(sx) || !float.IsFinite(sy) || !float.IsFinite(dx) || !float.IsFinite(dy) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(timer) || !float.IsFinite(duration)) return false;
        if (duration == 0) return true;
        if (duration < .05f || duration > 1 || timer < 0 || timer > duration) return false;
        float deltaX = dx - sx, deltaY = dy - sy;
        bool horizontal = Math.Abs(Math.Abs(deltaX) - 16) < .0001f && Math.Abs(deltaY) < .0001f;
        bool vertical = Math.Abs(Math.Abs(deltaY) - 16) < .0001f && Math.Abs(deltaX) < .0001f;
        bool diagonal = Math.Abs(Math.Abs(deltaX) - 16) < .0001f && Math.Abs(Math.Abs(deltaY) - 16) < .0001f;
        if (!horizontal && !vertical && !diagonal) return false;
        float t = timer / duration;
        float cx = sx + deltaX * t, cy = sy + deltaY * t;
        if (Math.Abs((float)Math.Round(cx) - x) > .0001f || Math.Abs((float)Math.Round(cy) - y) > .0001f) return false;
        rx = cx - x; ry = cy - y;
        return Math.Abs(rx) <= .5001f && Math.Abs(ry) <= .5001f;
    }
}
