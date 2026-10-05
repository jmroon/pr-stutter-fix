using System;
using System.Diagnostics;
using PRStutter.GridExperiment;

namespace PRStutter.TimingExperiment;

// Persistent user intent with independently eligible components. Retains the
// public bridge name so stock-mode UI and diagnostics stay loosely coupled.
public static class ComparisonControl
{
    private sealed class Feature : ITestFeature
    {
        public string Name { get; }
        private readonly Func<bool> _active;
        private readonly Func<string> _reason;
        private readonly Action<long> _start;
        private readonly Action<string> _stop;
        public Feature(string name, Func<bool> active, Func<string> reason, Action<long> start, Action<string> stop)
        { Name = name; _active = active; _reason = reason; _start = start; _stop = stop; }
        public bool Active => _active();
        public string StopReason => _reason();
        public void Start(long deadline) => _start(deadline);
        public void Stop(string reason) => _stop(reason);
    }
    private static SceneFeature? _timing, _pacing;
    private static readonly ContextReadiness Readiness = new();
    private static FieldContextSnapshot _context = new("none", "none", "waiting", false, false, false, "waiting for field");
    private static bool _fault;
    private static string _faultReason = "", _lastLog = "";
    private static string _pacingIdentity = "";
    public static bool Active { get; private set; }
    public static bool UnroundedCarry { get; set; }
    public static bool TimingActive => Timing.Active;
    public static bool PacingActive => ExperimentControls.PacingActive;
    public static bool SmoothingActive => ExperimentControls.SmoothingActive;
    public static int CarriedTiles => Timing.TotalCarried;
    public static int TimingSession => Timing.Session;
    public static int Generation { get; private set; }
    public static string ContextKind => _context.Kind;
    public static string ContextIdentity => _context.Identity;
    public static string TimingStatus => _timing?.Status ?? "off";
    public static string PacingStatus => _pacing?.Status ?? "off";
    public static bool Faulted => _fault || _timing?.Faulted == true || _pacing?.Faulted == true;
    public static bool Healthy => Active && !Faulted && !AutomaticRuntime.Enabled && !SmoothingActive && ExperimentControls.Ready;
    public static bool PrecisionAllowed => Healthy && Readiness.Ready && _context.Precision;
    public static string Status => $"{ContextKind} [generation {Generation}] | precision={(PrecisionAllowed ? "eligible" : "suspended")}\nTiming: {TimingStatus} | pacing: {PacingStatus} | carried={CarriedTiles}" + (Faulted ? "\nFAULT: " + _faultReason : "");
    public static void Start(long deadline)
    {
        if (Active || AutomaticRuntime.Enabled || !ExperimentControls.Ready || !Timing.CleanupComplete ||
            !ExperimentControls.PacingCleanupComplete || TimingActive || PacingActive || SmoothingActive)
            throw new InvalidOperationException("Stock mode requires idle corrections, F9 OFF and clean component ownership.");
        _fault = false; _faultReason = ""; _lastLog = ""; Generation = 0; Readiness.Reset();
        _context = new("none", "none", "waiting", false, false, false, "waiting for fresh field");
        _timing = new(new Feature("Timing", () => TimingActive, () => Timing.LastStop, Timing.Start, Timing.StopForRuntime), SceneEligibility.ExpectedTimingStop);
        _pacing = new(new Feature("Pacing", () => PacingActive, () => ExperimentControls.PacingStopReason,
            d => ExperimentControls.StartFieldPacing(d, PacingContextValid), ExperimentControls.StopPacing), SceneEligibility.ExpectedPacingStop);
        Active = true; UnroundedCarry = false;
        try { FieldContext.Attach(); }
        catch { Stop("context startup failed"); throw; }
        Timing.Note("STOCK RUNTIME ENABLED; waiting for eligible field; diagnostics optional.");
    }
    private static bool PacingContextValid()
    {
        var live = FieldContext.Read();
        return Active && !Faulted && live.Pacing && live.Identity == _pacingIdentity;
    }
    public static void Refresh()
    {
        if (!Active || Faulted) return;
        try {
            if (AutomaticRuntime.Enabled || SmoothingActive || !ExperimentControls.Ready)
                throw new InvalidOperationException("Conflicting correction owner or missing grid runtime");
            var context = FieldContext.Read();
            if (context.Identity != _context.Identity || context.TimingIdentity != _context.TimingIdentity) Generation++;
            _context = context;
            Readiness.Observe(context.Identity, FieldContext.Frame, context.Precision);
            bool ready = Readiness.Ready;
            _pacingIdentity = context.Identity;
            _pacing!.Reconcile(context.Identity, ready && context.Pacing, context.Reason);
            if (_pacing.Faulted) { Fail("pacing=" + PacingStatus); return; }
            _timing!.Reconcile(context.TimingIdentity, ready && context.Manual, context.Reason);
            if (_timing.Faulted || _pacing.Faulted) {
                Fail("timing=" + TimingStatus + "; pacing=" + PacingStatus); return;
            }
            string note = $"{Generation}:{context.Identity}:{context.Kind}; precision={PrecisionAllowed}; timing={TimingActive}/{TimingStatus}; pacing={PacingActive}/{PacingStatus}";
            if (note != _lastLog) { _lastLog = note; Timing.Note("STOCK STATE " + note); }
        } catch (Exception e) { Fail("context evaluation failed: " + e.Message); }
    }
    public static void Suspend(string reason)
    {
        if (!Active) return;
        _timing?.Suspend(reason); _pacing?.Suspend(reason);
        Readiness.Reset(); FieldContext.Clear();
        _context = new(reason, reason, reason, false, false, false, reason);
        UnroundedCarry = false;
        if (_timing?.Faulted == true || _pacing?.Faulted == true) { _fault = true; _faultReason = "cleanup failed: " + TimingStatus + "; " + PacingStatus; }
    }
    public static void Fail(string reason)
    {
        _fault = true; _faultReason = reason;
        Suspend(reason);
        Timing.Note("STOCK RUNTIME FAULT (latched): " + reason);
    }
    public static void Stop(string reason)
    {
        if (!Active) return;
        Exception? failure = null;
        try { Timing.StopForRuntime(reason); } catch (Exception e) { failure = e; }
        try { ExperimentControls.StopPacing(reason); } catch (Exception e) { failure ??= e; }
        try { ExperimentControls.StopSmoothing(reason); } catch (Exception e) { failure ??= e; }
        try { FieldContext.Detach(); } catch (Exception e) { failure ??= e; }
        UnroundedCarry = false; Readiness.Reset();
        if (failure != null) { _fault = true; _faultReason = failure.Message; throw failure; }
        Active = false;
        Timing.Note("STOCK RUNTIME DISABLED: " + reason);
    }
}
