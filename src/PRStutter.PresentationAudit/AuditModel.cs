using System;

namespace PRStutter.PresentationAudit;

// Immutable values only. These equations never modify or advance gameplay.
public readonly record struct Point(float X, float Y, float Z = 0);
public readonly record struct CheckResult(string Status, Point Expected, Point Observed, float Error);
public static class AuditModel
{
    public const float Tolerance = .002f;
    public static bool Finite(Point p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static float Axis(float input, float length, float view, bool loop, bool clamp, float offset)
    {
        float extent = Math.Max(0, length - view) * .5f;
        return (clamp && !loop ? Math.Clamp(input, -extent, extent) : input) + offset;
    }
    public static Point Camera(Point target, Point scroll, Point offset, float width, float height,
        float viewWidth, float viewHeight, bool loopX, bool loopY, bool clamp) => new(
        Axis(target.X + scroll.X, width, viewWidth, loopX, clamp, offset.X),
        Axis(target.Y + scroll.Y, height, viewHeight, loopY, clamp, offset.Y));
    public static float WrappedVisualAxis(float entity, float camera, float player, float length, float halfView, bool loop)
    {
        float result = entity - camera;
        float sign = player >= 0 ? 1 : -1; // Mathf.Sign(0) is +1.
        if (loop && length > halfView * 2 && Math.Abs(camera + halfView * sign - entity) > length)
            result += sign * length;
        return result;
    }
    public static Point Visual(Point entity, Point camera, Point player, float width, float height,
        float halfWidth, float halfHeight, bool loopX, bool loopY, Point scale) => new(
        WrappedVisualAxis(entity.X, camera.X, player.X, width, halfWidth, loopX) * scale.X,
        WrappedVisualAxis(entity.Y, camera.Y, player.Y, height, halfHeight, loopY) * scale.Y);
    public static CheckResult Compare(Point expected, Point observed, string scope = "")
    {
        if (!Finite(expected) || !Finite(observed)) return new("nonfinite", expected, observed, 0);
        if (scope.Length != 0) return new(scope, expected, observed, 0);
        float error = Math.Max(Math.Abs(expected.X - observed.X), Math.Abs(expected.Y - observed.Y));
        return new(error <= Tolerance ? "match" : "mismatch", expected, observed, error);
    }
}

// No catch-up bursts after a hitch; this is spatial evidence, not timing telemetry.
public sealed class AuditWindow
{
    private readonly long _frequency;
    private long _next, _end;
    public bool Active { get; private set; }
    public AuditWindow(long frequency) { if (frequency < 20) throw new ArgumentOutOfRangeException(nameof(frequency)); _frequency = frequency; }
    public void Start(long now) { Active = true; _next = now; _end = now + _frequency * 60; }
    public bool Expired(long now) => Active && now >= _end;
    public bool Take(long now)
    {
        if (!Active || now >= _end || now < _next) return false;
        _next = now + _frequency / 20; return true;
    }
    public void Stop() => Active = false;
}
