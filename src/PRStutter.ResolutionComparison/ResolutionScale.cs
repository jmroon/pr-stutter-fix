using System;

namespace PRStutter.UnroundedExperiment;

internal static class ResolutionScale
{
    public static int Next(int scale) => scale switch { 1 => 4, 4 => 8, 8 => 1, _ => throw new ArgumentOutOfRangeException(nameof(scale)) };
    public static int Width(int scale) => scale is 4 or 8 ? 320 * scale : throw new ArgumentOutOfRangeException(nameof(scale));
    public static int Height(int scale) => scale is 4 or 8 ? 180 * scale : throw new ArgumentOutOfRangeException(nameof(scale));
    public static string Caption(int scale) => scale switch {
        1 => "A: STOCK 320x180", 4 => "B: 4x 1280x720", 8 => "C: 8x 2560x1440",
        _ => throw new ArgumentOutOfRangeException(nameof(scale))
    };
}
