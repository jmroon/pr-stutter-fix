using System;
using PRStutter.TimingExperiment;

internal static class AutomaticChecks
{
    private sealed class Fake : ITestFeature
    {
        public string Name => "fake";
        public bool Active { get; set; }
        public string StopReason => "guard stopped";
        public int Starts, Stops;
        public bool FailStart, FailStop;
        public void Start(long deadline) { Starts++; Active = true; if (FailStart) throw new Exception("partial start"); if (deadline != long.MaxValue) throw new Exception("finite lifetime"); }
        public void Stop(string reason) { Stops++; if (FailStop) throw new Exception("restore failed"); Active = false; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var native = new Fake(); var auto = new AutomaticFeature(native);
        auto.Reconcile("field", true, true, "");
        for (int i = 0; i < 20000; i++) auto.Reconcile("field", true, true, "");
        Check(native.Starts == 1 && auto.Active, "Automatic correction timed out or restarted");
        foreach (string phase in new[] { "menu", "cutscene", "battle", "focus lost" }) {
            auto.Reconcile(phase, true, false, phase);
            Check(!auto.Active, "Unsupported phase left correction active");
            auto.Reconcile("field", true, true, "");
            Check(auto.Active, "Field correction did not resume");
        }
        native.Active = false;
        auto.Reconcile("field", true, true, ""); int starts = native.Starts;
        auto.Reconcile("field", true, true, "");
        Check(!auto.Active && native.Starts == starts, "Guard fault retried in identical context");
        auto.Reconcile("another field", true, true, "");
        Check(auto.Active, "Scene change did not release fault latch");
        native.FailStop = true;
        auto.Reconcile("battle", true, false, "battle");
        auto.Reconcile("next field", true, true, "");
        Check(native.Starts == starts + 1, "Started before restoring previous ownership");
        native.FailStop = false; auto.Reconcile("next field", true, true, "");
        Check(auto.Active, "Restoration retry did not recover");
        var broken = new Fake { FailStart = true }; var isolated = new AutomaticFeature(broken);
        isolated.Reconcile("unsupported layout", true, true, "");
        for (int i = 0; i < 100; i++) isolated.Reconcile("unsupported layout", true, true, "");
        Check(!broken.Active && broken.Starts == 1 && auto.Active, "Partial startup leaked or stopped independent feature");
        auto.Reconcile("disabled", false, true, ""); Check(!auto.Active, "Disable did not restore");
        var follow = new FollowReadiness();
        follow.Observe(0,0,0,0); follow.Observe(0,0,0,0); Check(!follow.Ready,"Idle proves free follow");
        follow.Observe(1,1,1,1); follow.Observe(2,2,2,2); Check(follow.Ready,"Free diagonal follow not recognized");
        follow.Observe(3,3,2,2); Check(!follow.Ready,"Static/clamped camera accepted");
        follow.Observe(4,4,3,3); follow.Observe(5,5,4,4); Check(follow.Ready,"Follow did not recover");
        follow.Reset(); Check(!follow.Ready,"Scene reset retained follow proof");
        Console.WriteLine("PASS: automatic independent lifetimes, menu/cutscene/battle/focus suspension/resumption, fault latching, partial-start rollback, cleanup retry, disable and clamped-camera readiness.");
    }
}
