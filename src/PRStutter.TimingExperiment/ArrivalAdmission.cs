namespace PRStutter.TimingExperiment;

// Only used after the caller verifies the exact newly queued arrival task. Keep
// ownership in the game's scheduler: yielded AND completed tasks remain on its
// running list for normal resumption/termination, including after F5 is stopped.
internal interface IArrivalAdmission
{
    void Start();
    void AddToRunning();
    void RemoveFromQueue();
    void UndoAddToRunning();
    void Step();
}

internal static class ArrivalAdmission
{
    public static void AdmitAndStep(IArrivalAdmission task)
    {
        // This build's EnumeratorTaskProcess inherits a Start that only sets
        // Running. If admission fails it remains queued; native Start can retry.
        task.Start();
        task.AddToRunning(); // Add first so allocation failure cannot lose a task.
        try { task.RemoveFromQueue(); }
        catch { task.UndoAddToRunning(); throw; }
        // Never roll back ownership after executing game code. A yielded task
        // must resume from its new state, and an ended task must not be restarted.
        task.Step();
    }
}
