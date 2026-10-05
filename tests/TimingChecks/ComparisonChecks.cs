using System;
using PRStutter.TimingExperiment;
using PRStutter.GridExperiment;

internal static class ComparisonChecks
{
    public static void Run()
    {
        static void Check(bool ok) { if (!ok) throw new Exception("Comparison lifecycle check failed"); }
        static void Throws(Action a) { try { a(); } catch { return; } throw new Exception("Expected comparison refusal"); }
        AutomaticRuntime.Enabled = true;
        Throws(() => ComparisonControl.Start(long.MaxValue));
        Check(!Timing.Active && !ExperimentControls.PacingActive && !ComparisonControl.Active);
        AutomaticRuntime.Enabled = false;
        ExperimentControls.FailStart = true;
        Throws(() => ComparisonControl.Start(long.MaxValue));
        Check(!Timing.Active && !ExperimentControls.PacingActive && !ComparisonControl.Active);
        ExperimentControls.FailStart = false;
        ComparisonControl.Start(long.MaxValue);
        Check(ComparisonControl.Healthy);
        int starts = Timing.Starts;
        ComparisonControl.UnroundedCarry = true;
        ComparisonControl.UnroundedCarry = false;
        Check(ComparisonControl.Healthy && Timing.Starts == starts);
        Throws(() => ComparisonControl.Start(long.MaxValue));
        Check(ComparisonControl.Healthy); // Duplicate start cannot clean up someone else's lease.
        Timing.Active = false;
        Check(!ComparisonControl.Healthy && ComparisonControl.Active);
        ComparisonControl.Stop("guard stopped");
        Check(!ExperimentControls.PacingActive && !ComparisonControl.Active);
        ComparisonControl.Start(long.MaxValue);
        ExperimentControls.SmoothingActive = true;
        Check(!ComparisonControl.Healthy);
        Timing.FailStop = true;
        Throws(() => ComparisonControl.Stop("injected cleanup failure"));
        Check(ComparisonControl.Active && !ExperimentControls.PacingActive && !ExperimentControls.SmoothingActive);
        Timing.FailStop = false;
        ComparisonControl.Stop("cleanup retry");
        Check(!ComparisonControl.Active && !Timing.Active && !ComparisonControl.UnroundedCarry);
        Console.WriteLine("PASS: comparison startup rollback, actual-state health, stable A/B timing session, duplicate refusal and independent cleanup with retained lease on failure.");
    }
}

namespace PRStutter.TimingExperiment
{
    internal static class AutomaticRuntime { public static bool Enabled; }
    internal static class Timing
    {
        public static bool Active, FailStop;
        public static bool CleanupComplete => !Active;
        public static int Carried => 0;
        public static int Starts;
        public static string LastStop => "test";
        public static void Start(long deadline) { Active = true; Starts++; }
        public static void StopForRuntime(string reason) { if (FailStop) throw new Exception("stop"); Active = false; }
        public static void Note(string text) { }
    }
}
namespace PRStutter.GridExperiment
{
    internal static class ExperimentControls
    {
        public static bool Ready => true;
        public static bool PacingActive, SmoothingActive, FailStart;
        public static string PacingStopReason => "test";
        public static void StartPacing(long deadline) { PacingActive = true; if (FailStart) throw new Exception("start"); }
        public static void StopPacing(string reason) => PacingActive = false;
        public static void StopSmoothing(string reason) => SmoothingActive = false;
    }
}
