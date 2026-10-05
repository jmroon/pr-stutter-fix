using System;
using PRStutter.TimingExperiment;
using PRStutter.GridExperiment;

internal static class ComparisonChecks
{
    public static void Run()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        static void Throws(Action a) { try { a(); } catch { return; } throw new Exception("Expected refusal"); }
        void Context(string id, string kind, bool manual, bool eligible = true) {
            FieldContext.Current = new(id,id + (manual ? ":player" : ":script"),kind,eligible,eligible,manual,kind);
            FieldContext.Frame++;
            ComparisonControl.Refresh();
        }
        AutomaticRuntime.Enabled = true;
        Throws(() => ComparisonControl.Start(long.MaxValue));
        Check(!ComparisonControl.Active, "Conflicting owner accepted");
        AutomaticRuntime.Enabled = false;
        ComparisonControl.Start(long.MaxValue);
        Check(ComparisonControl.Healthy && !ComparisonControl.PrecisionAllowed && !Timing.Active, "Start requires or modifies unavailable field");
        Context("map1", "field-manual", true);
        ComparisonControl.Refresh();
        Check(!Timing.Active && !ComparisonControl.PrecisionAllowed, "Repeated reads armed a single frame");
        Context("map1", "field-manual", true);
        Check(Timing.Active && ExperimentControls.PacingActive && ComparisonControl.PrecisionAllowed, "Stable field did not activate");
        int paceStarts = ExperimentControls.Starts;
        Context("map1", "field-event", false);
        Check(!Timing.Active && ExperimentControls.PacingActive && ExperimentControls.Starts == paceStarts && ComparisonControl.PrecisionAllowed, "Cinematic lost pacing/precision");
        Context("map1", "field-manual", true);
        Check(Timing.Active, "Control return did not resume");
        int generation = ComparisonControl.Generation;
        Context("map2", "field-manual", true);
        Check(!Timing.Active && !ExperimentControls.PacingActive && !ComparisonControl.PrecisionAllowed, "New map used old context");
        Context("map2", "field-manual", true);
        Check(Timing.Active && ComparisonControl.Generation > generation, "Replacement did not reacquire");
        ComparisonControl.Suspend("focus lost");
        Check(ComparisonControl.Active && !Timing.Active && !ExperimentControls.PacingActive && !ComparisonControl.PrecisionAllowed, "Focus loss forgot intent or retained changes");
        Context("map2", "field-manual", true); Context("map2", "field-manual", true);
        Check(Timing.Active, "Focus return did not resume");
        Context("Battle", "Battle", false, false);
        Check(!Timing.Active && !ExperimentControls.PacingActive && ComparisonControl.Active, "Battle did not suspend while preserving intent");
        Context("map3", "field-manual", true); Context("map3", "field-manual", true);
        ExperimentControls.PacingActive = false; ExperimentControls.PacingStopReason = "settings_changed";
        Context("map3", "field-manual", true);
        Check(ComparisonControl.Faulted && !Timing.Active && !ComparisonControl.PrecisionAllowed, "External settings not faulted/cleaned");
        int attempts = ExperimentControls.Starts;
        Context("map4", "field-manual", true); Context("map4", "field-manual", true);
        Check(ExperimentControls.Starts == attempts, "Fault retried on new map");
        ComparisonControl.Stop("explicit off");
        Check(!ComparisonControl.Active && !FieldContext.Installed, "Observer lease leaked");
        ExperimentControls.FailStart = true;
        ComparisonControl.Start(long.MaxValue);
        Context("map5", "field-manual", true); Context("map5", "field-manual", true);
        Check(ComparisonControl.Faulted && !ExperimentControls.PacingActive && !Timing.Active, "Partial startup was not rolled back");
        ComparisonControl.Stop("explicit off"); ExperimentControls.FailStart = false;
        ComparisonControl.Start(long.MaxValue);
        Context("map6", "field-manual", true); Context("map6", "field-manual", true);
        Timing.FailStop = true;
        Throws(() => ComparisonControl.Stop("cleanup failure"));
        Check(ComparisonControl.Active && !ExperimentControls.PacingActive && !FieldContext.Installed, "Failed cleanup lost ownership or blocked independent cleanup");
        Timing.FailStop = false; ComparisonControl.Stop("explicit retry");
        Console.WriteLine("PASS: actual coordinator waits for fresh context, preserves enabled intent, keeps cinematic pacing/precision, reacquires map/controller, resumes after focus/battle and latches faults with independent cleanup.");
    }
}
namespace PRStutter.TimingExperiment
{
    internal static class AutomaticRuntime { public static bool Enabled; }
    internal static class FieldContext
    {
        public static FieldContextSnapshot Current = new("none","none","waiting",false,false,false,"waiting");
        public static int Frame;
        public static bool Installed;
        public static void Attach() { Installed = true; Clear(); }
        public static void Detach() { Installed = false; Clear(); }
        public static void Clear() { Current = new("none","none","waiting",false,false,false,"waiting"); Frame++; }
        public static FieldContextSnapshot Read() => Current;
    }
    internal static class Timing
    {
        public static bool Active, FailStop;
        public static bool CleanupComplete => !Active;
        public static int TotalCarried => 0;
        public static int Session => Starts;
        public static int Starts;
        public static string LastStop => "control_changed";
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
        public static bool PacingActive, FailStart;
        public static bool SmoothingActive => false;
        public static bool PacingCleanupComplete => !PacingActive;
        public static string PacingStopReason = "context_changed";
        public static int Starts;
        public static void StartFieldPacing(long deadline, Func<bool> eligible) { Starts++; PacingActive = true; if (!eligible() || FailStart) throw new Exception("start"); }
        public static void StopPacing(string reason) => PacingActive = false;
        public static void StopSmoothing(string reason) { }
    }
}
