using System;
using PRStutter.TimingExperiment;

internal static class DiagonalChecks
{
    private static void Check(bool pass, string reason) { if (!pass) throw new Exception(reason); }
    private static readonly float Duration = .2f * MathF.Sqrt(2);
    private static Walk Moving(float sx, float sy, float timer, int x, int y) =>
        new(sx, sy, sx + 16 * x, sy + 16 * y, timer, Duration,
            CarryPolicy.Rounded(sx, sx + 16 * x, timer, Duration),
            CarryPolicy.Rounded(sy, sy + 16 * y, timer, Duration));

    public static void Run()
    {
        int simulated = 0;
        foreach (int fps in new[] { 30, 60, 120, 144, 165, 240, 360 })
        foreach (int x in new[] { -1, 1 })
        foreach (int y in new[] { -1, 1 })
        foreach (bool jitter in new[] { false, true }) {
            float sx = 64, sy = -184, timer = 0;
            double elapsed = 0;
            int callbacks = 0;
            for (int frame = 0; frame < fps * 10; frame++) {
                float dt = (1f / fps) * (jitter ? new[] { .72f, 1.28f, .96f, 1.04f }[frame % 4] : 1);
                var before = Moving(sx, sy, timer, x, y);
                Check(CarryPolicy.Ordinary(before), "Native diagonal walk rejected");
                Check(CarryPolicy.SameInput(before, x, y, frame, frame), "Fresh diagonal input rejected");
                bool carry = CarryPolicy.TryRemainder(before, dt, out float remainder);
                timer += dt; elapsed += dt;
                if (timer >= Duration) {
                    callbacks++;
                    var after = before with { Timer = 0, Duration = 0, X = before.Dx, Y = before.Dy };
                    Check(CarryPolicy.Completed(before, after), "Diagonal completion rejected");
                    sx = before.Dx; sy = before.Dy;
                    Check(CarryPolicy.ApprovedNext(before, Moving(sx, sy, 0, x, y)), "Diagonal continuation rejected");
                    timer = carry ? remainder : 0;
                }
                double px = sx + x * 16 * (double)timer / Duration;
                double py = sy + y * 16 * (double)timer / Duration;
                Check(Math.Abs(px - (64 + x * 16 * elapsed / Duration)) < .001 &&
                    Math.Abs(py - (-184 + y * 16 * elapsed / Duration)) < .001,
                    "Diagonal walk lost time on one axis at a boundary");
                Check(Math.Abs(callbacks - Math.Floor(elapsed / Duration)) <= 1, "Diagonal callback count diverged");
                simulated++;
            }
        }

        // Exact movement observed in capture 20261004-050213-916, frame 3810.
        var observed = new Walk(64, -184, 80, -200, .0084416f, .2828427f, 64, -184);
        Check(CarryPolicy.Ordinary(observed), "Captured diagonal rejected");
        foreach (int x in new[] { -1, 1 })
        foreach (int y in new[] { -1, 1 }) {
            var before = Moving(64, -184, Duration - .001f, x, y);
            Check(CarryPolicy.TryRemainder(before, .008f, out float remainder) &&
                Math.Abs(remainder - .007f) < .000001, "Diagonal remainder incorrect");
            var next = Moving(before.Dx, before.Dy, 0, x, y);
            foreach (var axis in new[] { (0, 0), (x, 0), (0, y), (-x, y), (x, -y), (-x, -y) })
                Check(!CarryPolicy.SameInput(before, axis.Item1, axis.Item2, 10, 10), "Diagonal stop/turn/reversal accepted");
            Check(!CarryPolicy.SameInput(before, x, y, 9, 10), "Stale diagonal input accepted");
            Check(!CarryPolicy.SameInput(before, x / MathF.Sqrt(2), y / MathF.Sqrt(2), 10, 10), "Uninspected analog input accepted");
            Check(!CarryPolicy.ApprovedNext(before, next with { Dx = next.Sx, Dy = next.Sy, Duration = 0 }), "Blocked diagonal accepted");
            var slide = new Walk(next.Sx, next.Sy, next.Dx, next.Sy, 0, .2f, next.Sx, next.Sy);
            Check(CarryPolicy.Ordinary(slide) && !CarryPolicy.ApprovedNext(before, slide), "Collision slide received diagonal carry");
            Check(!CarryPolicy.ApprovedNext(before, Moving(next.Sx, next.Sy, 0, -x, y)), "Changed diagonal accepted");
            Check(!CarryPolicy.ApprovedNext(before, Moving(next.Sx + 16, next.Sy, 0, x, y)), "Diagonal teleport accepted");
            Check(!CarryPolicy.ApprovedNext(before, next with { Duration = Duration / 2 }), "Diagonal dash accepted");
            Check(!CarryPolicy.TryRemainder(before with { Dx = before.Sx + 32 * x }, .008f, out _), "Long diagonal path accepted");
            Check(!CarryPolicy.TryRemainder(before, .051f, out _), "Diagonal stall accepted");
        }
        Console.WriteLine($"PASS: {simulated} diagonal frames across four directions, steady/jittered 30-360 FPS; both axes conserve time within .001 units. Captured diagonal accepted; blocked paths, slides, turns, stale/analog input, dash and teleports refuse carry.");
    }
}
