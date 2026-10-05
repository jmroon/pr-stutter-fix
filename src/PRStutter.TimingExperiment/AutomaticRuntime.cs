using System;
using System.Diagnostics;
using BepInEx.Configuration;
using Last.Entity.Field;
using Last.Map;
using PRStutter.GridExperiment;
using UnityEngine;

namespace PRStutter.TimingExperiment;

internal static class AutomaticRuntime
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
    private static readonly AutomaticFeature TimingFeature = new(new Feature("Timing", () => Timing.Active, () => Timing.LastStop, Timing.Start, Timing.StopForRuntime));
    private static readonly AutomaticFeature PacingFeature = new(new Feature("Pacing", () => ExperimentControls.PacingActive, () => ExperimentControls.PacingStopReason, ExperimentControls.StartPacing, ExperimentControls.StopPacing));
    private static readonly AutomaticFeature SmoothingFeature = new(new Feature("Smoothing", () => ExperimentControls.SmoothingActive, () => ExperimentControls.SmoothingStopReason, ExperimentControls.StartSmoothing, ExperimentControls.StopSmoothing));
    private static readonly FollowReadiness Follow = new();
    private static ConfigEntry<bool> _enabled = null!, _timing = null!, _pacing = null!, _smoothing = null!;
    private static long _nextProbe;
    private static string _identity = "";
    private static CorrectionSnapshot _lastPublished;
    private static int _generation;
    private static bool _shutdown;
    public static bool Enabled => _enabled?.Value ?? false;
    public static void Initialize(ConfigFile config)
    {
        CorrectionStatus.Game = GameProfile.Id;
        CorrectionStatus.Available = true;
        _enabled = config.Bind("Corrections", "Enabled", true, "Automatic field corrections. F9 toggles this setting. Unsupported components suspend independently.");
        _timing = config.Bind("Corrections", "Timing", true, "Preserve tile time only for inspected manual movement.");
        _pacing = config.Bind("Corrections", "Pacing", true, "Scoped display-paced VSync during supported field control.");
        _smoothing = config.Bind("Corrections", "Smoothing", true, "8x smoothing after observing a freely following camera. CRT must be off.");
        TimingCapture.Enabled = config.Bind("Diagnostics", "LegacyTimingCsv", false, "Optional bounded timing CSV per correction session; independent of automatic correction lifetime.").Value;
        ExperimentControls.SetCoordinated(true); // Own controls for the entire runtime, including suspension.
    }
    public static void Toggle()
    {
        if (ComparisonControl.Active) { Timing.Note("F9 ignored while stock movement owns timing/pacing; Shift+F11 stops it."); return; }
        _enabled.Value = !Enabled; _generation++; _nextProbe = 0;
        if (!Enabled) Suspend("disabled by F9");
    }
    public static void Tick()
    {
        if (_shutdown) return;
        if (ComparisonControl.Active) { Publish(Stopwatch.GetTimestamp(), ComparisonControl.ContextKind + ";generation=" + ComparisonControl.Generation); return; }
        long now = Stopwatch.GetTimestamp();
        if (now < _nextProbe) { Publish(now); return; }
        _nextProbe = now + Stopwatch.Frequency / 2;
        try {
            CameraFollowing? following = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<CameraFollowing>())
                if (candidate.TargetEntity != null && candidate.camera != null && candidate.camera.isActiveAndEnabled) {
                    if (following != null) { Apply("multiple-follow", false, false, false, "multiple camera-follow targets", now); return; }
                    following = candidate;
                }
            var player = following?.TargetEntity.TryCast<FieldPlayer>();
            bool manual = false;
            FieldController? field = null;
            int controllerId = 0;
            if (player != null && player.gameObject.activeInHierarchy && (int)player.moveState == 0 &&
                !player.IsAutoMoving && !GameProfile.TransportActive(player) && !player.pauseMoving)
                foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerKeyController>())
                    if (c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == player.Pointer) {
                        manual = true; controllerId = c.GetInstanceID(); field = c.playerHandle?.TryCast<FieldController>();
                    }
            bool available = manual && Application.isFocused && Time.timeScale == 1 && ExperimentControls.Ready;
            string identity = $"follow={following?.GetInstanceID() ?? 0};player={player?.GetInstanceID() ?? 0};controller={controllerId};area={field?.currentAreaId ?? -1};mapRenderer={field?.mainViewMapRenderer?.Pointer ?? IntPtr.Zero};manual={available};display={Screen.width}x{Screen.height}";
            if (identity != _identity) { Follow.Reset(); _identity = identity; }
            if (available && player != null && following != null) {
                var p = player.transform.position; var c = following.camera.transform.position;
                Follow.Observe(p.x, p.y, c.x, c.y);
            } else Follow.Reset();
            var compositor = PostProcessLite.GetMaterial();
            bool crtOff = compositor != null && compositor.GetFloat("_FakeCRT") == 0;
            string reason = !Application.isFocused ? "unfocused" : Time.timeScale != 1 ? "paused" : "manual field control unavailable";
            Apply(identity, available, Follow.Ready, crtOff, reason, now);
        } catch (Exception e) { Apply("probe-fault", false, false, false, "scene observation failed: " + e.Message, now); }
    }
    private static void Apply(string identity, bool available, bool following, bool crtOff, string reason, long now)
    {
        string context = identity + ";generation=" + _generation;
        TimingFeature.Reconcile(context + _timing.Value, Enabled && _timing.Value, available, reason);
        PacingFeature.Reconcile(context + _pacing.Value, Enabled && _pacing.Value, available, reason);
        string renderReason = !available ? reason : !crtOff ? "CRT enabled" : "waiting for freely following camera";
        SmoothingFeature.Reconcile(context + $";follow={following};crtOff={crtOff};wanted={_smoothing.Value}",
            Enabled && _smoothing.Value, available && following && crtOff, renderReason);
        Publish(now, identity);
    }
    private static void Publish(long now, string? context = null)
    {
        var s = new CorrectionSnapshot(now, context ?? CorrectionStatus.Current.Context ?? "waiting", Enabled,
            TimingFeature.Active, PacingFeature.Active, SmoothingFeature.Active,
            ComparisonControl.Active ? ComparisonControl.TimingStatus : TimingFeature.Status,
            ComparisonControl.Active ? ComparisonControl.PacingStatus : PacingFeature.Status, SmoothingFeature.Status);
        CorrectionStatus.Current = s;
        var comparable = s with { Qpc = 0 };
        if (comparable == _lastPublished) return;
        _lastPublished = comparable;
        CorrectionStatus.Changes.Publish(s);
        Timing.Note($"AUTO STATE {s.Context}; enabled={s.Enabled}; timing={s.Timing}/{s.TimingReason}; pacing={s.Pacing}/{s.PacingReason}; smoothing={s.Smoothing}/{s.SmoothingReason}");
    }
    public static void Suspend(string reason)
    {
        ComparisonControl.Stop(reason);
        TimingFeature.Suspend(reason); PacingFeature.Suspend(reason); SmoothingFeature.Suspend(reason);
        Follow.Reset(); _identity = ""; _generation++; _nextProbe = 0;
        Publish(Stopwatch.GetTimestamp());
    }
    public static void Shutdown(string reason)
    { _shutdown = true; Suspend(reason); ExperimentControls.SetCoordinated(false); }
}
