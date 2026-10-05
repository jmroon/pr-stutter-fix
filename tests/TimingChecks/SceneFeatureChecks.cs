using System;
using PRStutter.TimingExperiment;

internal static class SceneFeatureChecks
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var state in new[] { "Player", "Event", "Message", "Gimmick" })
            Check(SceneEligibility.FieldState(state), "Supported field category rejected");
        foreach (var state in new[] { "Menu", "Battle", "Transport", "ChangeMap", "FieldReady", "Init", "MiniGame", "FutureUnknown" })
            Check(!SceneEligibility.FieldState(state), "Uninspected state admitted");
        var timing = new Fake(); var pace = new Fake();
        var t = new SceneFeature(timing, SceneEligibility.ExpectedTimingStop);
        var p = new SceneFeature(pace, SceneEligibility.ExpectedPacingStop);
        for (int run = 0; run < 100; run++) {
            string map = "map" + run;
            t.Reconcile(map + ":player", true, ""); p.Reconcile(map, true, "");
            int starts = pace.Starts;
            t.Reconcile(map + ":player", false, "script owns control"); p.Reconcile(map, true, "");
            Check(!timing.Active && pace.Active && pace.Starts == starts, "Script stopped/restarted pacing");
            t.Reconcile(map + ":player", true, "");
            Check(timing.Active && !t.Faulted, "Same-identity manual control did not resume");
            timing.Active = false; timing.StopReason = "control_changed";
            t.Reconcile(map + ":player", false, "script"); t.Reconcile(map + ":player", true, "");
            Check(timing.Active, "Native safety suspension did not recover");
            t.Reconcile("menu", false, "menu"); p.Reconcile("menu", false, "menu");
            Check(!timing.Active && !pace.Active, "Unsupported context retained component");
        }
        p.Reconcile("field", true, "");
        pace.Active = false; pace.StopReason = "settings_changed";
        p.Reconcile("field", true, "");
        int attempts = pace.Starts;
        p.Reconcile("another-field", true, "");
        Check(p.Faulted && pace.Starts == attempts && !pace.Active, "External setting change retried automatically");
        var partial = new Fake { FailStart = true };
        var f = new SceneFeature(partial, _ => true);
        f.Reconcile("field", true, "");
        Check(f.Faulted && !partial.Active && partial.Stops == 1, "Partial startup not cleaned");
        partial.FailStart = false;
        f.Reconcile("new", true, "");
        Check(partial.Starts == 1, "Fault cleared by context replacement");
        var cleanup = new Fake(); var c = new SceneFeature(cleanup, _ => true);
        c.Reconcile("a", true, ""); cleanup.FailStop = true;
        c.Reconcile("b", false, "change");
        Check(c.Faulted && cleanup.Active, "Cleanup failure lost active ownership");
        int stops = cleanup.Stops; c.Reconcile("c", true, "");
        Check(cleanup.Stops == stops, "Cleanup failure causes per-frame retry storm");
        cleanup.FailStop = false; c.Suspend("explicit shutdown retry");
        Check(!cleanup.Active && c.Faulted, "Explicit cleanup lost fault record");
        Console.WriteLine("PASS: 100 manual/script/menu/replacement cycles; pacing retained through scripts, timing resumes with same identity, unsupported states excluded, external changes/start/cleanup faults latch without retry storms.");
    }
    private sealed class Fake : ITestFeature
    {
        public string Name => "fake";
        public bool Active { get; set; }
        public string StopReason { get; set; } = "";
        public int Starts, Stops;
        public bool FailStart, FailStop;
        public void Start(long deadline) { Starts++; Active = true; if (FailStart) throw new Exception("start"); }
        public void Stop(string reason) { Stops++; if (FailStop) throw new Exception("restore"); Active = false; StopReason = reason; }
    }
}
