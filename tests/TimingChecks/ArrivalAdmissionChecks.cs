using System;
using System.Collections.Generic;
using PRStutter.TimingExperiment;

internal static class ArrivalAdmissionChecks
{
    private static void Check(bool pass, string reason) { if (!pass) throw new Exception(reason); }
    public static void Run()
    {
        foreach (int waits in new[] { 0, 2 }) {
            var game = new FakeAdmission { Waits = waits };
            ArrivalAdmission.AdmitAndStep(game);
            Check(game.Starts == 1 && game.Updates == 1, "Arrival was not advanced exactly once");
            Check(game.Queued.Count == 0 && game.Running.Count == 2, "Scheduler lost or duplicated arrival ownership");
            Check(game.OtherUpdates == 0 && game.Approvals == (waits == 0 ? 1 : 0), "Wait bypassed or unrelated task advanced");
            // Stopping the experiment does nothing to admitted game tasks. The
            // normal scheduler continues through every remaining yield/cleanup.
            for (int i = 0; i < waits + 2; i++) game.NormalScheduler();
            Check(game.Approvals == 1 && game.Starts == 1 && game.Terminates == 1 &&
                !game.Running.Contains("arrival"), "Yielded task was restarted, lost, or completed more than once");
        }
        var ended = new FakeAdmission { EndImmediately = true };
        ArrivalAdmission.AdmitAndStep(ended);
        Check(ended.End && ended.Queued.Count == 0, "Completed task left queued to restart");
        ended.NormalScheduler();
        Check(ended.Starts == 1 && ended.Terminates == 1 && ended.Updates == 1, "Completed task restarted or stepped again");

        var appends = new FakeAdmission { QueueDuringStep = true };
        ArrivalAdmission.AdmitAndStep(appends);
        Check(appends.Queued.Count == 1 && appends.Queued[0] == "callback-task" &&
            appends.OtherUpdates == 0, "Callback-created task consumed or advanced");

        foreach (string failure in new[] { "start", "add", "remove", "step" }) {
            var game = new FakeAdmission { Failure = failure };
            bool threw = false;
            try { ArrivalAdmission.AdmitAndStep(game); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "Injected admission failure was swallowed");
            int owners = (game.Queued.Contains("arrival") ? 1 : 0) + (game.Running.Contains("arrival") ? 1 : 0);
            Check(owners == 1 && game.OtherUpdates == 0, "Exception lost/duplicated game task or advanced another task");
            Check(game.Running.Contains("arrival") == (failure == "step"), "Rollback crossed the game execution boundary");
            game.Failure = "";
            for (int i = 0; i < 4; i++) game.NormalScheduler();
            Check(game.Approvals == 1 && game.Terminates == 1, "Scheduler could not resume task after failed admission");
        }
        Console.WriteLine("PASS: single-task admission preserves waits, one approval, normal cleanup after stop, callback-created tasks, and scheduler ownership across start/add/remove/step failures.");
    }

    private sealed class FakeAdmission : IArrivalAdmission
    {
        public readonly List<string> Queued = new() { "arrival" };
        public readonly List<string> Running = new() { "other" };
        public int Starts, Updates, Approvals, OtherUpdates, Terminates, Waits;
        public bool EndImmediately, QueueDuringStep, End;
        public string Failure = "";
        private int _state;
        private void ThrowAt(string point) { if (Failure == point) throw new InvalidOperationException(point); }
        public void Start() { ThrowAt("start"); Starts++; End = false; }
        public void AddToRunning() { ThrowAt("add"); Running.Add("arrival"); }
        public void RemoveFromQueue() { ThrowAt("remove"); Queued.RemoveAt(0); }
        public void UndoAddToRunning() { Running.RemoveAt(Running.Count - 1); }
        public void Step()
        {
            ThrowAt("step");
            if (End) return;
            Updates++;
            if (QueueDuringStep) { Queued.Add("callback-task"); QueueDuringStep = false; }
            if (EndImmediately || _state == 1) { End = true; return; }
            if (Waits > 0) { Waits--; return; }
            Approvals++; _state = 1; // Real arrival yields once after invoking approval.
        }
        public void NormalScheduler()
        {
            if (Queued.Contains("arrival")) { Queued.Remove("arrival"); Start(); AddToRunning(); }
            OtherUpdates++;
            if (!Running.Contains("arrival")) return;
            Step();
            if (End) { Running.Remove("arrival"); Terminates++; }
        }
    }
}
