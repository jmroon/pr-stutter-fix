using System;
using PRStutter.TimingExperiment;

internal static class TransitionChecks
{
    private sealed class OwnedFeature : ITestFeature
    {
        public string Name { get; }
        public bool Active { get; private set; }
        public string StopReason => "external guard";
        public int NativeValue = 7, Starts, Restores;
        private int _original;
        public bool Unsupported;
        public OwnedFeature(string name) { Name = name; }
        public void Start(long deadline)
        {
            if (Active) throw new Exception("Duplicate ownership");
            _original = NativeValue; NativeValue = 99; Active = true; Starts++;
            if (Unsupported) throw new InvalidOperationException("unknown camera layout");
        }
        public void Stop(string reason) { if (Active) { if(NativeValue == 99) NativeValue = _original; Active = false; Restores++; } }
    }
    public static void Run()
    {
        var timing = new OwnedFeature("timing"); var pacing = new OwnedFeature("pacing"); var render = new OwnedFeature("render");
        var t = new AutomaticFeature(timing); var p = new AutomaticFeature(pacing); var r = new AutomaticFeature(render);
        var observations = new ObservationChannel<int>();
        int observed = 0; using var subscription = observations.Subscribe(_ => observed++);
        for (int lap = 0; lap < 100; lap++) {
            foreach (string phase in new[] { "field", "menu", "field", "cutscene", "battle", "field", "focus lost", "field" }) {
                bool eligible = phase == "field";
                string context = phase + lap;
                t.Reconcile(context,true,eligible,phase); p.Reconcile(context,true,eligible,phase); r.Reconcile(context,true,eligible,phase);
                observations.Publish(lap); // Observation continues through all suspended phases.
                foreach (var feature in new[] { timing, pacing, render })
                    if (feature.Active != eligible || feature.NativeValue != (eligible ? 99 : 7)) throw new Exception("Transition leaked state: " + phase);
            }
        }
        render.Unsupported = true;
        t.Reconcile("unknown-layout",true,true,""); p.Reconcile("unknown-layout",true,true,""); r.Reconcile("unknown-layout",true,true,"");
        if (!t.Active || !p.Active || r.Active || render.NativeValue != 7) throw new Exception("Unsupported rendering affected independent corrections");
        pacing.NativeValue = 3; // A user/native setting change during ownership is not ours to undo.
        p.Suspend("settings changed");
        if (pacing.NativeValue != 3) throw new Exception("Restoration clobbered an independent setting change");
        t.Suspend("shutdown"); r.Suspend("shutdown");
        if (timing.NativeValue != 7 || render.NativeValue != 7 || observed != 800) throw new Exception("Shutdown or diagnostic continuity failed");
        Console.WriteLine("PASS: 800 simulated field/menu/cutscene/battle/focus transitions preserve ownership and diagnostic continuity; unknown render layouts leave timing/pacing independent. Live game transitions remain unverified.");
    }
}
