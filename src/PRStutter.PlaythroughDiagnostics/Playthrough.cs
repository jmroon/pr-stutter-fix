using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Last.Entity.Field;
using PRStutter.TimingExperiment;
using UnityEngine;

namespace PRStutter.PlaythroughDiagnostics;

internal static class Playthrough
{
    private static ConfigEntry<bool> _enabled = null!;
    private static IDisposable? _movementSubscription, _statusSubscription;
    private static RecentBuffer<MotionSample>? _motion;
    private static RecentBuffer<MovementObservation>? _movement;
    private static RecentBuffer<FrameCost>? _costs;
    private static RecentBuffer<CorrectionSnapshot>? _states;
    private static IncidentGate _incidents = new(Stopwatch.Frequency * 30, Stopwatch.Frequency * 3);
    private static readonly CaptureWriter Writer = new(Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter/playthrough"));
    private static readonly List<Camera> Cameras = new();
    private static readonly List<FieldEntity> Entities = new();
    private static readonly Dictionary<int, CameraInfo> CameraDescriptions = new();
    private static readonly Dictionary<int, EntityInfo> EntityDescriptions = new();
    private static readonly Dictionary<string, long> Coverage = new();
    private static long _nextDiscovery, _previousFrame, _started, _nextUi;
    private static string _context = "", _status = "Diagnostics OFF | F10 enable | F11 mark";
    private static long _discoveryTicks, _maxObserverTicks, _totalObserverTicks, _frames;
    private static long _callbackTicks, _initializationTicks;
    private static int _untrackedCameras, _untrackedEntities, _coverageOverflow;
    private static string _coverageKey = "waiting";
    private static CorrectionSnapshot _coverageState;
    private static bool _recording, _faulted;
    public static string Status => _status;
    public static void Initialize(ConfigFile config)
    {
        _enabled = config.Bind("Debug", "Enabled", false, "F10 toggles bounded playthrough recording independently of corrections. F11 saves surrounding context. No GPU readback.");
        _movementSubscription = CorrectionEvents.Movement.Subscribe(row => { try { OnMovement(row); } catch (Exception e) { Fault(e); } });
        _statusSubscription = CorrectionStatus.Changes.Subscribe(state => { try { OnState(state); } catch (Exception e) { Fault(e); } });
        if (_enabled.Value) Begin();
    }
    public static void Keys()
    {
        bool toggle = Input.GetKeyDown(KeyCode.F10);
        if (_faulted && !toggle) return;
        if (toggle) { _faulted = false; _enabled.Value = !_enabled.Value; }
        if (_enabled.Value && !_recording) Begin();
        if (!_enabled.Value && _recording) End();
        if (_recording && Input.GetKeyDown(KeyCode.F11)) _incidents.Trigger("manual-marker", Stopwatch.GetTimestamp(), true);
    }
    private static void Begin()
    {
        long initializeBegin = Stopwatch.GetTimestamp();
        _motion = new RecentBuffer<MotionSample>(65536);
        _movement = new RecentBuffer<MovementObservation>(8192);
        _costs = new RecentBuffer<FrameCost>(8192);
        _states = new RecentBuffer<CorrectionSnapshot>(256);
        _incidents = new IncidentGate(Stopwatch.Frequency * 30, Stopwatch.Frequency * 3);
        Cameras.Clear(); Entities.Clear(); CameraDescriptions.Clear(); EntityDescriptions.Clear(); Coverage.Clear();
        _started = Stopwatch.GetTimestamp(); _nextDiscovery = _previousFrame = _nextUi = 0;
        _discoveryTicks = _maxObserverTicks = _totalObserverTicks = _frames = 0;
        _untrackedCameras = _untrackedEntities = _coverageOverflow = 0;
        _context = ""; _recording = true;
        _coverageState = default; _coverageKey = "waiting"; _callbackTicks = 0;
        OnState(CorrectionStatus.Current);
        _initializationTicks = Stopwatch.GetTimestamp() - initializeBegin;
    }
    private static void OnMovement(MovementObservation row)
    {
        if (!_recording) return;
        long observerBegin = Stopwatch.GetTimestamp();
        _movement!.Add(row.Sample.Qpc, row);
        if (row.Action == "carried") {
            var b = row.Sample.Before; var n = row.Final;
            if (b.Duration > 0 && n.Duration > 0) {
                double ex = (n.Sx + (n.Dx - n.Sx) * (double)n.Timer / n.Duration) - (b.Sx + (b.Dx - b.Sx) * (double)b.Timer / b.Duration) - (b.Dx - b.Sx) * (double)row.Sample.Delta / b.Duration;
                double ey = (n.Sy + (n.Dy - n.Sy) * (double)n.Timer / n.Duration) - (b.Sy + (b.Dy - b.Sy) * (double)b.Timer / b.Duration) - (b.Dy - b.Sy) * (double)row.Sample.Delta / b.Duration;
                if (Math.Max(Math.Abs(ex), Math.Abs(ey)) > .001) _incidents.Trigger("movement-conservation-candidate", row.Sample.Qpc);
            }
        }
        _callbackTicks += Stopwatch.GetTimestamp() - observerBegin;
    }
    private static void OnState(CorrectionSnapshot state)
    {
        if (!_recording) return;
        long now = Stopwatch.GetTimestamp();
        _states!.Add(now, state with { Qpc = now });
        if (_context != state.Context) { _context = state.Context ?? "waiting"; _nextDiscovery = 0; }
        if (state.Enabled && (!state.Timing || !state.Pacing || !state.Smoothing))
            _incidents.Trigger("fallback: " + _context + " | " + state.TimingReason + " | " + state.PacingReason + " | " + state.SmoothingReason, now);
        _callbackTicks += Stopwatch.GetTimestamp() - now;
    }
    public static void Sample()
    {
        if (!_recording) return;
        long begin = Stopwatch.GetTimestamp();
        if (begin >= _nextDiscovery) Discover(begin);
        int frame = Time.frameCount;
        foreach (var camera in Cameras) {
            if (camera == null || !camera.isActiveAndEnabled) continue;
            var cp = camera.transform.position;
            foreach (var entity in Entities) {
                if (entity == null || !entity.gameObject.activeInHierarchy) continue;
                var position = entity.transform.position;
                var projected = camera.WorldToScreenPoint(position);
                var start = entity.startPos; var dest = entity.destPos;
                _motion!.Add(begin, new MotionSample(frame, begin, Time.deltaTime, camera.GetInstanceID(), entity.GetInstanceID(),
                    position.x, position.y, cp.x, cp.y, projected.x, projected.y,
                    start.x, start.y, dest.x, dest.y, entity.moveTimer, entity.moveTime,
                    projected.z, camera.orthographicSize, camera.pixelWidth, camera.pixelHeight));
            }
            // Camera motion remains observable when there are no field entities (e.g. battle).
            _motion!.Add(begin, new MotionSample(frame, begin, Time.deltaTime, camera.GetInstanceID(), 0,
                0, 0, cp.x, cp.y, 0, 0, 0, 0, 0, 0, 0, 0,
                0, camera.orthographicSize, camera.pixelWidth, camera.pixelHeight));
        }
        long wall = _previousFrame == 0 ? 0 : begin - _previousFrame; _previousFrame = begin;
        var status = CorrectionStatus.Current;
        if (Coverage.TryGetValue(_coverageKey, out long ticks)) Coverage[_coverageKey] = ticks + wall;
        else if (Coverage.Count < 128) Coverage[_coverageKey] = wall; else _coverageOverflow++;
        var comparable = status with { Qpc = 0 };
        if (comparable != _coverageState) {
            _coverageState = comparable;
            _coverageKey = $"{status.Context}|T={status.Timing},P={status.Pacing},S={status.Smoothing}";
        }
        if (Time.unscaledDeltaTime > .05f) _incidents.Trigger("long-update-candidate", begin);
        if (_incidents.Reason != null && begin >= _incidents.DueQpc) Save(begin);
        if (begin >= _nextUi) {
            _status = $"Diagnostics ON | F10 off | F11 mark | {(_incidents.Reason != null ? "collecting incident" : Writer.Status)} | observer max {_maxObserverTicks * 1000.0 / Stopwatch.Frequency:F3} ms";
            _nextUi = begin + Stopwatch.Frequency;
        }
        long cost = Stopwatch.GetTimestamp() - begin;
        _costs!.Add(begin, new FrameCost(frame, begin, cost, wall, status.Timing, status.Pacing, status.Smoothing));
        _maxObserverTicks = Math.Max(_maxObserverTicks, cost); _totalObserverTicks += cost; _frames++;
    }
    private static void Discover(long now)
    {
        long begin = Stopwatch.GetTimestamp();
        Cameras.Clear(); Entities.Clear();
        // Discovery is capped at 1 Hz; steady-frame sampling uses cached references.
        _nextDiscovery = now + Stopwatch.Frequency;
        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
        // Prefer actual field views over the final fullscreen compositor.
        foreach (var camera in cameras)
            if (camera.isActiveAndEnabled && camera.name == "CameraFieldMain") AddCamera(camera);
        foreach (var camera in cameras)
            if (camera.isActiveAndEnabled && camera.name != "CameraFrontFilter" && camera.name != "CameraFieldMain") AddCamera(camera);
        var entities = UnityEngine.Object.FindObjectsOfType<FieldEntity>();
        foreach (var entity in entities) if (entity.TryCast<FieldPlayer>() != null) AddEntity(entity);
        foreach (var entity in entities) if (entity.TryCast<FieldPlayer>() == null) AddEntity(entity);
        _discoveryTicks += Stopwatch.GetTimestamp() - begin;
    }
    private static void AddCamera(Camera camera)
    {
        int id = camera.GetInstanceID();
        if (CameraDescriptions.Count < 256 || CameraDescriptions.ContainsKey(id)) {
            var target = camera.targetTexture;
            CameraDescriptions[id] = new CameraInfo(id, camera.name, camera.gameObject.scene.name, target?.GetInstanceID() ?? 0,
                target?.width ?? camera.pixelWidth, target?.height ?? camera.pixelHeight,
                camera.orthographic, camera.orthographicSize, camera.depth, camera.cullingMask);
        }
        if (Cameras.Count < 2) Cameras.Add(camera); else _untrackedCameras++;
    }
    private static void AddEntity(FieldEntity entity)
    {
        if (!entity.gameObject.activeInHierarchy) return;
        int id = entity.GetInstanceID();
        if (EntityDescriptions.Count < 512 || EntityDescriptions.ContainsKey(id)) EntityDescriptions[id] = new EntityInfo(id, entity.name, entity.gameObject.scene.name, entity.TryCast<FieldPlayer>() != null);
        if (Entities.Count < 8) Entities.Add(entity); else _untrackedEntities++;
    }
    private static void Save(long now)
    {
        if (Writer.Busy) { Writer.RecordBusyDrop(); _incidents.Complete(); return; }
        long since = now - Stopwatch.Frequency * 18;
        Writer.TrySave(new {
            SchemaVersion = 1, Game = CorrectionStatus.Game, PluginVersion = "0.2.0", QpcFrequency = Stopwatch.Frequency,
            Display = new { Width = Screen.width, Height = Screen.height, RefreshRate = Screen.currentResolution.refreshRate,
                Vsync = QualitySettings.vSyncCount, TargetFrameRate = Application.targetFrameRate },
            StartedQpc = _started, SavedQpc = now, TriggerQpc = _incidents.TriggerQpc, Reason = _incidents.Reason,
            Phase = "LateUpdatePolling", Limitation = "Logical entity/camera projection before final rendering. Not final pixels, GPU timing or physical display cadence. Candidate incidents are not proof of jitter.",
            Motion = _motion!.Snapshot(since), Movement = _movement!.Snapshot(since), Frames = _costs!.Snapshot(since), States = _states!.Snapshot(since),
            CurrentState = CorrectionStatus.Current, Cameras = new List<CameraInfo>(CameraDescriptions.Values), Entities = new List<EntityInfo>(EntityDescriptions.Values),
            CoverageQpcTicks = new Dictionary<string, long>(Coverage),
            Loss = new { MotionOverwritten = _motion.Overwritten, MovementOverwritten = _movement.Overwritten,
                FramesOverwritten = _costs.Overwritten, StatesOverwritten = _states.Overwritten,
                SuppressedIncidents = _incidents.Suppressed, BusyWrites = Writer.Dropped, UntrackedCameras = _untrackedCameras, UntrackedEntities = _untrackedEntities, CoverageOverflow = _coverageOverflow },
            Overhead = new { Frames = _frames, TotalObserverTicks = _totalObserverTicks, MaxObserverTicks = _maxObserverTicks, DiscoveryTicks = _discoveryTicks,
                CallbackTicks = _callbackTicks, InitializationTicks = _initializationTicks,
                Limitation = "LateUpdate wall cost includes discovery and prior capture snapshots; callback and startup costs separate. Background writer CPU/GPU impact requires debug-off comparison." }
        });
        _incidents.Complete();
    }
    private static void End()
    {
        if (!_recording) return;
        if (_incidents.Reason == null) _incidents.Trigger("recording-ended", Stopwatch.GetTimestamp(), true);
        Save(Stopwatch.GetTimestamp()); _recording = false;
        _motion = null; _movement = null; _costs = null; _states = null;
        Cameras.Clear(); Entities.Clear();
        _status = "Diagnostics OFF | F10 enable | F11 mark";
    }
    public static void Fault(Exception e)
    {
        _recording = false; _faulted = true;
        _status = "Diagnostics stopped: " + e.Message;
        try { _enabled.Value = false; } catch { } // Even a read-only config directory cannot escape into gameplay.
        _motion = null; _movement = null; _costs = null; _states = null;
        Cameras.Clear(); Entities.Clear();
    }
    public static void Shutdown()
    {
        try { End(); } catch (Exception e) { Fault(e); }
        _movementSubscription?.Dispose(); _statusSubscription?.Dispose(); Writer.Wait();
    }
}
