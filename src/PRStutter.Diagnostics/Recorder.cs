using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Last.Entity.Field;
using UnityEngine;

namespace PRStutter.Diagnostics;

internal static class Recorder
{
    private static CameraFollowing? _following;
    public static bool Active => _buffer != null;
    private static CaptureBuffer? _buffer;
    private static readonly Dictionary<int, CameraDescription> Cameras = new();
    private static ManualLogSource? _log;
    private static string _output = "", _unity = "", _startedUtc = "", _stem = "";
    private static int _seconds;
    private static long _startQpc, _lastPoll;
    private static Task? _saveTask;
    private static bool _keyboardUnavailable, _faultReported;
    private static IntPtr _entityPointer;
    private static FieldPlayer? _player;
    private static FieldSpriteEntity? _spriteEntity;
    private static string? _renderInspection;

    public static void Initialize(ManualLogSource log, string output, int seconds, string unity)
    {
        _log = log; _output = output; _seconds = seconds; _unity = unity;
        Directory.CreateDirectory(output);
    }

    public static void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        bool toggle = false;
        if (!_keyboardUnavailable)
        {
            try { toggle = Input.GetKeyDown(KeyCode.F8); }
            catch (Exception error)
            {
                _keyboardUnavailable = true;
                _log?.LogWarning($"F8 unavailable: {error.Message}. Use the Start-DiagnosticCapture script instead.");
            }
        }
        // No filesystem polling during capture. The alternate trigger is an explicit request file.
        if (!Active && now - _lastPoll > Stopwatch.Frequency / 4)
        {
            _lastPoll = now;
            string request = Path.Combine(_output, "start.request");
            if (File.Exists(request)) { File.Delete(request); toggle = true; }
        }
        if (toggle)
        {
            if (Active) Stop("manual"); else Start();
        }
        if (_buffer is { } buffer && (now - _startQpc >= _seconds * Stopwatch.Frequency || buffer.Dropped > 0))
            Stop(buffer.Dropped > 0 ? "capacity" : "duration");
    }

    private static void Start()
    {
        if (_saveTask is { IsCompleted: false }) { _log?.LogWarning("Previous capture is still being saved."); return; }
        // Discover once, outside the measured interval. Never patch a game method to find it.
        CameraFollowing? selected = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<CameraFollowing>())
        {
            if (!IsUsable(candidate)) continue;
            if (selected != null)
            {
                _log?.LogWarning("Multiple active field cameras found; capture not started.");
                return;
            }
            selected = candidate;
        }
        if (selected == null)
        {
            _log?.LogWarning("No active field camera-follow target. Load a field map, then press F8 again.");
            return;
        }
        _following = selected;
        // Snapshot/formatting precedes the measured interval; a failed inspection must not
        // prevent the proven polling capture from running.
        _renderInspection = null;
        try { _renderInspection = RenderInspection.Read(selected); }
        catch (Exception error) { _log?.LogWarning($"Render inspection unavailable: {error.Message}"); }
        // Allocate once before recording. No per-sample file writes or CSV formatting.
        _buffer = new CaptureBuffer(65536);
        Cameras.Clear();
        _faultReported = false;
        _startedUtc = DateTime.UtcNow.ToString("o");
        _stem = Path.Combine(_output, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _entityPointer = IntPtr.Zero;
        _player = null;
        _spriteEntity = null;
        _startQpc = Stopwatch.GetTimestamp();
        _log?.LogInfo($"CAPTURE START ({_seconds}s): {_stem}.csv");
    }

    private static bool IsUsable(CameraFollowing? following) => following != null &&
        following.gameObject.activeInHierarchy && following.TargetEntity != null &&
        following.camera != null && following.camera.enabled && following.camera.gameObject.activeInHierarchy;

    public static void Late()
    {
        var following = _following;
        if (!IsUsable(following)) { Stop("field_camera_inactive"); return; }
        long begin = Stopwatch.GetTimestamp();
        CaptureSample sample = Sample(Phase.LateUpdate, following!.TargetEntity, following, following.camera, begin);
        Add(ref sample, begin);
    }

    private static CaptureSample Sample(Phase phase, FieldEntity entity, CameraFollowing following, Camera? camera, long begin)
    {
        var transform = entity.transform;
        var visual = entity.visualParent;
        var world = transform.position;
        if (_entityPointer != entity.Pointer)
        {
            _entityPointer = entity.Pointer;
            _player = entity.TryCast<FieldPlayer>();
            _spriteEntity = entity.TryCast<FieldSpriteEntity>();
        }
        var sprite = _spriteEntity?.spriteRenderer;
        var spriteWorld = sprite == null ? Point3.Missing : V(sprite.transform.position);
        var sample = new CaptureSample {
            Phase = phase, Frame = Time.frameCount, Qpc = begin,
            DeltaTime = Time.deltaTime, UnscaledDeltaTime = Time.unscaledDeltaTime, TimeScale = Time.timeScale,
            EntityId = entity.GetInstanceID(), ControllerId = following.GetInstanceID(),
            PlayerMoveState = _player == null ? -1 : (int)_player.moveState,
            Timer = entity.moveTimer, Duration = entity.moveTime, Start = V(entity.startPos), Destination = V(entity.destPos),
            Candidate = Point3.Missing, Result = Point3.Missing,
            Local = V(transform.localPosition), World = V(world), VisualWorld = visual == null ? Point3.Missing : V(visual.position),
            SpriteWorld = spriteWorld, SpriteScreenPoint = Point3.Missing,
            CameraWorld = Point3.Missing, CameraTarget = V(following.targetPosition), ScrollOffset = Point3.Missing, ScreenPoint = Point3.Missing,
            ScreenWidth = Screen.width, ScreenHeight = Screen.height, OrthographicSize = float.NaN
        };
        if (camera != null)
        {
            int id = camera.GetInstanceID();
            var texture = camera.targetTexture;
            if (!Cameras.ContainsKey(id)) Cameras.Add(id, new CameraDescription(id, camera.name, camera.cullingMask,
                texture == null ? "" : texture.name, texture == null ? -1 : (int)texture.filterMode));
            sample.CameraId = id;
            sample.CameraWorld = V(camera.transform.position);
            sample.ScreenPoint = V(camera.WorldToScreenPoint(world));
            if (sprite != null) sample.SpriteScreenPoint = V(camera.WorldToScreenPoint(new Vector3(spriteWorld.X, spriteWorld.Y, spriteWorld.Z)));
            sample.PixelWidth = camera.pixelWidth; sample.PixelHeight = camera.pixelHeight;
            sample.TargetWidth = texture == null ? 0 : texture.width;
            sample.TargetHeight = texture == null ? 0 : texture.height;
            sample.OrthographicSize = camera.orthographicSize;
        }
        return sample;
    }

    private static Point3 V(Vector3 v) => new(v.x, v.y, v.z);
    private static bool Add(ref CaptureSample sample, long begin)
    {
        sample.ObserverTicks = Stopwatch.GetTimestamp() - begin;
        return _buffer?.Add(sample) ?? false;
    }

    public static void Fault(Exception error)
    {
        if (_faultReported) return;
        _faultReported = true;
        _log?.LogError($"Diagnostic observer stopped after an error: {error}");
        Stop("observer_error");
    }

    private static void Stop(string reason)
    {
        CaptureBuffer? buffer = _buffer;
        if (buffer == null) return;
        _buffer = null;
        _following = null;
        var metadata = new CaptureMetadata(2, Plugin.Version, Plugin.SupportedGameHash, _unity,
            _startedUtc, Stopwatch.Frequency, _startQpc, Stopwatch.GetTimestamp(), reason,
            buffer.Count, buffer.Dropped, 0, 0, 0, Cameras.Values.ToArray(), "LateUpdatePolling");
        string stem = _stem;
        string? renderInspection = _renderInspection;
        _log?.LogInfo($"CAPTURE STOP: {reason}; {buffer.Count} rows, {buffer.Dropped} dropped. Saving in background.");
        _saveTask = Task.Run(() => {
            try {
                if (renderInspection != null) {
                    File.WriteAllText(stem + ".render.json.partial", renderInspection);
                    File.Move(stem + ".render.json.partial", stem + ".render.json");
                }
                CaptureFiles.Write(stem, buffer, metadata); _log?.LogInfo($"CAPTURE SAVED: {stem}.csv");
            }
            catch (Exception error) { _log?.LogError($"Capture save failed: {error}"); }
        });
    }

    public static void Shutdown()
    {
        Stop("shutdown");
        _saveTask?.Wait(TimeSpan.FromSeconds(3));
    }
}
