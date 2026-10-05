using System;

namespace PRStutter.TimingExperiment;

public readonly record struct Walk(float Sx, float Sy, float Dx, float Dy, float Timer, float Duration, float X, float Y);

// No saved remainder: a budget may be used once, in the frame that produced it.
public static class CarryPolicy
{
    public static bool ReadyBeforeCamera(int completionFrame, int currentFrame, int cameraFrame,
        int footFinishedFrame, bool footAllowsNext, bool operationAllowed) =>
        completionFrame == currentFrame && cameraFrame != currentFrame &&
        footFinishedFrame == currentFrame && footAllowsNext && operationAllowed;

    public static bool Ordinary(Walk s) => Ordinary(s, false);
    public static bool Ordinary(Walk s, bool unrounded)
    {
        if (!float.IsFinite(s.Sx) || !float.IsFinite(s.Sy) || !float.IsFinite(s.Dx) || !float.IsFinite(s.Dy) ||
            !float.IsFinite(s.Timer) || !float.IsFinite(s.Duration) || !float.IsFinite(s.X) || !float.IsFinite(s.Y)) return false;
        float dx = s.Dx - s.Sx, dy = s.Dy - s.Sy;
        bool cardinal = (Math.Abs(dx) == 16 && dy == 0) || (Math.Abs(dy) == 16 && dx == 0);
        bool diagonal = Math.Abs(dx) == 16 && Math.Abs(dy) == 16;
        if (!cardinal && !diagonal) return false;
        // Native FieldEntity.MoveTo multiplies the base duration by sqrt(2)
        // when both displacement axes are nonzero. Never write the duration.
        float expectedDuration = diagonal ? .2f * MathF.Sqrt(2) : .2f;
        if (Math.Abs(s.Duration - expectedDuration) > .000001f || s.Timer < 0 || s.Timer >= s.Duration) return false;
        if (unrounded) return Math.Abs(s.X - Position(s.Sx, s.Dx, s.Timer, s.Duration, true)) <= .002f &&
            Math.Abs(s.Y - Position(s.Sy, s.Dy, s.Timer, s.Duration, true)) <= .002f;
        return s.X == Rounded(s.Sx, s.Dx, s.Timer, s.Duration) && s.Y == Rounded(s.Sy, s.Dy, s.Timer, s.Duration);
    }

    public static float Position(float start, float dest, float timer, float duration, bool unrounded) =>
        unrounded ? start + (dest - start) * (timer / duration) : Rounded(start, dest, timer, duration);

    public static float Rounded(float start, float dest, float timer, float duration) =>
        (float)Math.Round(start + (dest - start) * (timer / duration));

    public static bool TryRemainder(Walk before, float dt, out float remainder) => TryRemainder(before, dt, out remainder, false);
    public static bool TryRemainder(Walk before, float dt, out float remainder, bool unrounded)
    {
        remainder = 0;
        if (!Ordinary(before, unrounded) || !float.IsFinite(dt) || dt <= 0 || dt > .05f) return false;
        float elapsed = before.Timer + dt; // Match the native float addition/comparison.
        if (elapsed < before.Duration) return false;
        remainder = elapsed - before.Duration;
        return remainder > 0 && remainder < before.Duration && remainder <= dt + .000001f;
    }

    public static bool Completed(Walk before, Walk after) =>
        after.Timer == 0 && after.Duration == 0 && before.Sx == after.Sx && before.Sy == after.Sy &&
        before.Dx == after.Dx && before.Dy == after.Dy && after.X == before.Dx && after.Y == before.Dy;

    public static bool SameInput(Walk before, float axisX, float axisY, int inputFrame, int frame) =>
        inputFrame == frame && axisX * 16 == before.Dx - before.Sx && axisY * 16 == before.Dy - before.Sy;

    public static bool ApprovedNext(Walk before, Walk next) => Ordinary(next) && next.Timer == 0 &&
        next.Sx == before.Dx && next.Sy == before.Dy && next.Duration == before.Duration &&
        next.Dx - next.Sx == before.Dx - before.Sx && next.Dy - next.Sy == before.Dy - before.Sy;
}
