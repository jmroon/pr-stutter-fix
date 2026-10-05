using UnityEngine;

namespace PRStutter.GridExperiment;

// Narrow coordination API. Render/pacing state is still owned and restored by
// the original implementations; the timing plugin never duplicates that logic.
public static class ExperimentControls
{
    public static bool Ready { get; internal set; }
    public static bool Coordinated { get; private set; }
    private static int _suppressedFrame = -1;
    internal static bool SuppressKeys => Coordinated || _suppressedFrame == Time.frameCount;
    public static void SetCoordinated(bool value) { Coordinated = value; _suppressedFrame = Time.frameCount; }
    public static bool PacingCleanupComplete => PacingTest.CleanupComplete;
    public static void StartFieldPacing(long deadline, System.Func<bool> eligible) => PacingTest.StartField(deadline, eligible);
    public static bool PacingActive => PacingTest.Active;
    public static bool SmoothingActive => Test.Active;
    public static double PacingRemaining => PacingTest.Remaining;
    public static double SmoothingRemaining => Test.Remaining;
    public static string PacingStopReason => PacingTest.LastStop;
    public static string SmoothingStopReason => Test.LastStop;
    public static string SmoothingMode => Test.Mode;
    public static void StartPacing(long deadline) => PacingTest.Start(deadline);
    public static void StartSmoothing(long deadline) => Test.Start(8, deadline);
    public static void StopPacing(string reason) => PacingTest.Stop(reason);
    public static void StopSmoothing(string reason) { Test.Stop(reason); Test.DisposeStopped(); }
    public static void StopPixelCapture() => PixelCapture.Stop("combined_test");
}
