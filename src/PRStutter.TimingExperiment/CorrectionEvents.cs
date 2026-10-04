using System;

namespace PRStutter.TimingExperiment;

// Value-only observation API. Subscribers cannot authorize or cancel corrections.
public readonly record struct MovementSample(bool Valid, int Frame, long Qpc, float Delta,
    Walk Before, float AxisX, float AxisY, int InputFrame);
public readonly record struct MovementObservation(MovementSample Sample, Walk After, Walk Final,
    float Remainder, float Applied, string Action, long Ticks, bool FootBusy, int FootFrame,
    bool FootAllowsNext, int CameraFrame, string ArrivalAction = "none", int ArrivalState = -1,
    int RequestCount = -1, int RunningCount = -1);

public static class CorrectionEvents
{
    public static ObservationChannel<MovementObservation> Movement { get; } = new();
}

// Main-thread subscriptions and publication. Fixed subscriber count; no per-event
// invocation-list allocations. A broken observer detaches without affecting peers.
public sealed class ObservationChannel<T>
{
    private readonly Action<T>?[] _observers = new Action<T>?[4];
    public int Faults { get; private set; }
    public IDisposable Subscribe(Action<T> observer)
    {
        for (int i = 0; i < _observers.Length; i++)
            if (_observers[i] == null) { _observers[i] = observer; return new Subscription(this, i, observer); }
        throw new InvalidOperationException("Observer slots full");
    }
    public void Publish(T value)
    {
        for (int i = 0; i < _observers.Length; i++) {
            var observer = _observers[i];
            if (observer == null) continue;
            try { observer(value); }
            catch { _observers[i] = null; Faults++; }
        }
    }
    private sealed class Subscription : IDisposable
    {
        private readonly ObservationChannel<T> _owner;
        private readonly int _index;
        private readonly Action<T> _observer;
        public Subscription(ObservationChannel<T> owner, int index, Action<T> observer)
        { _owner = owner; _index = index; _observer = observer; }
        public void Dispose() { if (ReferenceEquals(_owner._observers[_index], _observer)) _owner._observers[_index] = null; }
    }
}

public readonly record struct CorrectionSnapshot(long Qpc, string Context, bool Enabled,
    bool Timing, bool Pacing, bool Smoothing, string TimingReason, string PacingReason, string SmoothingReason);

// Public read-only status and value events for optional diagnostics. No telemetry dependency.
public static class CorrectionStatus
{
    public static string Game { get; internal set; } = "unknown";
    public static bool Available { get; internal set; }
    public static CorrectionSnapshot Current { get; internal set; }
    public static ObservationChannel<CorrectionSnapshot> Changes { get; } = new();
}
