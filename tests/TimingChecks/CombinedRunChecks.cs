using System;
using PRStutter.TimingExperiment;

internal static class CombinedRunChecks
{
    private static void Check(bool pass, string why) { if (!pass) throw new Exception(why); }
    public static void Run()
    {
        var features = new[] { new Fake("Timing"), new Fake("Pacing"), new Fake("Smoothing") };
        var run = new CombinedRun(features);
        foreach (var feature in features) feature.Active = true; // Existing separate tests.
        run.Start(1000, 15000);
        Check(run.Active && run.Deadline == 16000, "Combined test did not start");
        foreach (var feature in features)
            Check(feature.Active && feature.Deadline == 16000 && feature.Stops == 1, "Independent state not reset or deadlines differ");
        run.Poll(15999);
        Check(run.Active, "Shared countdown ended early");
        run.Poll(16000);
        Check(!run.Active, "Shared deadline ignored");
        foreach (var feature in features) Check(!feature.Active, "Timeout left a partial combination running");
        run.Start(20000, 15000);
        features[2].Active = false; features[2].StopReason = "camera offset changed";
        run.Poll(20001);
        Check(!run.Active && run.LastStop.Contains("camera offset changed"), "Safety reason not preserved");
        foreach (var feature in features) Check(!feature.Active, "Component stop did not stop the remaining features");

        for (int failed = 0; failed < 3; failed++)
        foreach (bool silently in new[] { false, true }) {
            var parts = new[] { new Fake("Timing"), new Fake("Pacing"), new Fake("Smoothing") };
            parts[failed].FailStart = !silently; parts[failed].DeclineStart = silently;
            var test = new CombinedRun(parts);
            bool threw = false;
            try { test.Start(0, 15); } catch (InvalidOperationException) { threw = true; }
            Check(threw && !test.Active, "Partial startup reported success");
            foreach (var part in parts) Check(!part.Active, "Startup failure leaked active state");
            for (int i = failed + 1; i < parts.Length; i++) Check(parts[i].Starts == 0, "Startup continued after failure");
        }
        run.Start(30000, 15000);
        features[0].FailStop = true;
        try { run.Stop("focus lost"); throw new Exception("Cleanup error hidden"); } catch (AggregateException) { }
        Check(!run.Active && !features[1].Active && !features[2].Active, "Cleanup error prevented later restorations");
        Check(run.LastStop.Contains("cleanup failed"), "Cleanup failure not visible");
        features[0].FailStop = false;
        run.Stop("retry cleanup");
        Check(!features[0].Active, "Failed cleanup could not retry");
        Console.WriteLine("PASS: shared deadline, replacement of separate tests, timeout/manual/focus cleanup, component safety stops, partial/declined startup, and restoration failures never report an intact combined test.");
    }
    private sealed class Fake : ITestFeature
    {
        public string Name { get; }
        public bool Active { get; set; }
        public string StopReason { get; set; } = "";
        public long Deadline;
        public int Starts, Stops;
        public bool FailStart, DeclineStart, FailStop;
        public Fake(string name) { Name = name; }
        public void Start(long deadline)
        {
            Starts++; Deadline = deadline;
            if (DeclineStart) return;
            Active = true;
            if (FailStart) throw new InvalidOperationException(Name + " startup failed");
        }
        public void Stop(string reason)
        {
            Stops++;
            if (FailStop) throw new InvalidOperationException(Name + " restore failed");
            Active = false; StopReason = reason;
        }
    }
}
