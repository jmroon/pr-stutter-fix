using System;
using System.Diagnostics;
using PRStutter.GridExperiment;

namespace PRStutter.TimingExperiment;

// Explicit, temporary owner for the A/B experiment. No automatic restarts.
// Reflection keeps the disposable experiment out of the runtime dependency graph.
public static class ComparisonControl
{
    public static bool Active { get; private set; }
    public static bool UnroundedCarry { get; set; }
    public static bool TimingActive => Timing.Active;
    public static bool PacingActive => ExperimentControls.PacingActive;
    public static bool SmoothingActive => ExperimentControls.SmoothingActive;
    public static int CarriedTiles => Timing.Carried;
    public static bool Healthy => Active && !AutomaticRuntime.Enabled && TimingActive && PacingActive && !SmoothingActive;
    public static string Status => $"timing={TimingActive} ({Timing.LastStop}); pacing={PacingActive} ({ExperimentControls.PacingStopReason}); compensation={SmoothingActive}; carried={CarriedTiles}";
    public static void Start(long deadline)
    {
        if (Active || AutomaticRuntime.Enabled || !ExperimentControls.Ready || !Timing.CleanupComplete ||
            TimingActive || PacingActive || SmoothingActive || deadline <= Stopwatch.GetTimestamp())
            throw new InvalidOperationException("Comparison requires idle corrections, F9 OFF and ready timing/grid plugins.");
        Active = true; UnroundedCarry = false;
        try {
            Timing.Start(deadline);
            ExperimentControls.StartPacing(deadline);
            if (!Healthy) throw new InvalidOperationException("Timing/pacing did not both start.");
            Timing.Note("COMPARISON START: " + Status);
        } catch { Stop("comparison startup failed"); throw; }
    }
    public static void Stop(string reason)
    {
        if (!Active) return;
        // Attempt all cleanup even if one component fails. Retain the lease on
        // failure so the automatic supervisor cannot take over owned resources.
        Exception? failure = null;
        try { Timing.StopForRuntime(reason); } catch (Exception e) { failure = e; }
        try { ExperimentControls.StopPacing(reason); } catch (Exception e) { failure ??= e; }
        try { ExperimentControls.StopSmoothing(reason); } catch (Exception e) { failure ??= e; }
        UnroundedCarry = false;
        if (failure != null) throw failure;
        Active = false;
        Timing.Note("COMPARISON STOP: " + reason + "; " + Status);
    }
}
