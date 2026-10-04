using System;

namespace PRStutter.RenderExperiment;

internal static class StabilizationMath
{
    // Matching integer high-resolution texel shifts avoid two resampling phases
    // fighting each other on the followed sprite. This adds <= 1/(2*scale) unit error.
    public static float Quantize(float residual, int scale)
    {
        if (!float.IsFinite(residual) || Math.Abs(residual) > .5001f || scale < 2 || scale > 8)
            throw new ArgumentOutOfRangeException(nameof(residual));
        return (float)Math.Round(residual * scale) / scale;
    }
}
