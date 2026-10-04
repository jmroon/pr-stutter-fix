using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using Last.Entity.Field;
using Last.Map;
using Last.Map.Renderer;
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.NativeScrollExperiment;

[BepInPlugin("local.prstutter.nativescroll", "PR Stutter Native Scroll Test", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        if (!Matches("GameAssembly.dll", "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd") ||
            !Matches("FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat", "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd"))
        { Log.LogError("Unsupported game build; native scroll test disabled."); return; }
        Test.Log = Log;
        ClassInjector.RegisterTypeInIl2Cpp<MapPass>();
        _driver = AddComponent<Driver>();
        Test.Note("0.1.0 ready, OFF by default. F10 tests fractional native map scrolling for 15s. Original textures, shaders, camera and gameplay positions are retained. Background-only diagnostic; NPC alignment is not corrected.");
    }
    public override bool Unload()
    {
        Test.Stop("unload"); Test.DisposeStopped();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
    private static bool Matches(string relative, string expected)
    {
        using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, relative));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() { try { Test.Tick(); } catch (Exception e) { Test.Fault(e); } }
    public void OnApplicationQuit() { try { Test.Stop("quit"); } catch (Exception e) { Test.Fault(e); } }
}
public sealed class MapPass : MonoBehaviour
{
    public MapPass(IntPtr pointer) : base(pointer) { }
    public void OnPreCull() { try { Test.Before(this); } catch (Exception e) { Test.Fault(e); } }
    public void OnPostRender() { try { Test.After(this); } catch (Exception e) { Test.Fault(e); } }
    public void OnDisable() { try { Test.Disabled(this); } catch (Exception e) { Test.Fault(e); } }
}

internal static class Test
{
    public static ManualLogSource? Log;
    private static Session? _session;
    private static bool _active;
    private static long _started;
    public static void Note(string message)
    {
        Log?.LogInfo(message);
        try {
            var directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "native-scroll.log"), $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        } catch (Exception e) { Log?.LogWarning($"Evidence write failed: {e.Message}"); }
    }
    public static void Tick()
    {
        _session?.Restore();
        DisposeStopped();
        if (_active && Expired()) Stop("timeout");
        if (!Input.GetKeyDown(KeyCode.F10)) return;
        if (_active) { Stop("manual"); return; }
        DisposeStopped();
        _session = new Session();
        _session.Initialize();
        _started = Stopwatch.GetTimestamp(); _active = true;
        Note("NATIVE SCROLL ON (15s). Fractional input to the existing map renderer; background comparison only.");
    }
    private static bool Expired() => Stopwatch.GetTimestamp() - _started >= Stopwatch.Frequency * 15;
    public static void Before(MapPass pass)
    {
        if (!_active || _session == null || !_session.Owns(pass)) return;
        if (Expired()) { Stop("timeout"); return; }
        _session.Apply();
    }
    public static void After(MapPass pass)
    { if (_session != null && _session.IsLast(pass)) _session.Restore(); }
    public static void Disabled(MapPass pass)
    { if (_active && _session != null && _session.Owns(pass)) Stop("field_camera_disabled"); }
    public static void Stop(string reason)
    {
        bool wasActive = _active; _active = false;
        _session?.Restore();
        if (wasActive) { Note($"NATIVE SCROLL OFF ({reason}); {_session?.Summary}. Native map state restored."); _session?.WriteSamples(); }
    }
    public static void DisposeStopped()
    { if (!_active && _session != null) { _session.Dispose(); _session = null; } }
    public static void Fault(Exception e)
    {
        _active = false;
        try { _session?.Restore(); } catch (Exception restore) { Note($"RESTORE FAILED: {restore}"); }
        Note($"NATIVE SCROLL FAULT: {e}");
        _session?.WriteSamples();
    }
}

internal sealed class Session
{
    private readonly CameraFollowing _following;
    private readonly FieldPlayer _player;
    private readonly MainViewMapRenderer _renderer;
    private readonly List<FieldPlayerController> _controllers = new();
    private readonly List<Camera> _cameras = new();
    private readonly List<IntPtr> _cameraTargets = new();
    private readonly List<MapPass> _passes = new();
    private readonly Material _compositor;
    private readonly Texture _fieldTexture;
    private readonly ScopedOverride<Vector3> _scroll;
    private readonly Vector3 _followOffset;
    private readonly List<string> _samples = new();
    private MapPass? _last;
    private int _lastFrame = -1, _frames, _fractionalFrames;
    private float _maxError;
    private double _maxApplyMs;
    private Vector3 _originalRoot;
    private bool _verifyRestore;
    public string Summary => $"frames={_frames}, fractionalInputs={_fractionalFrames}, maxReadbackError={_maxError:F6}, maxNativeApplyMs={_maxApplyMs:F3}";

    public Session()
    {
        CameraFollowing? following = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>())
            if (f.TargetEntity != null && f.camera != null && f.camera.isActiveAndEnabled) {
                if (following != null) throw new InvalidOperationException("Multiple follow targets."); following = f;
            }
        _following = following ?? throw new InvalidOperationException("No active field follow target.");
        _player = _following.TargetEntity.TryCast<FieldPlayer>() ?? throw new InvalidOperationException("Target is not the player.");
        MainViewMapRenderer? renderer = null;
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MainViewMapRenderer>())
            if (r.isActiveAndEnabled && r.mainBGLayer != null && r.mainBGLayer.activeInHierarchy) {
                if (renderer != null) throw new InvalidOperationException("Multiple main map renderers."); renderer = r;
            }
        _renderer = renderer ?? throw new InvalidOperationException("No main map renderer.");
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) _controllers.Add(c);
        if (!HasControl()) throw new InvalidOperationException("Ordinary manual field walking is required.");
        _compositor = PostProcessLite.GetMaterial();
        if (_compositor == null || _compositor.shader.name != "Last/PostProcessLite") throw new InvalidOperationException("Unsupported compositor.");
        _fieldTexture = _compositor.GetTexture("_MainGameTex");
        if (_fieldTexture == null || _fieldTexture.width != 320 || _fieldTexture.height != 180 ||
            _following.camera.targetTexture == null || _fieldTexture.Pointer != _following.camera.targetTexture.Pointer)
            throw new InvalidOperationException("Original 320x180 field target required.");
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (camera.isActiveAndEnabled && camera.name is "CameraUpperTransparentRT" or "CameraCeilTransparentRT" or "CameraTileMap" or "CameraFieldMain") {
                if (camera.targetTexture == null || camera.targetTexture.width != 320 || camera.targetTexture.height != 180 ||
                    !camera.orthographic || Math.Abs(camera.orthographicSize - 90) > .001f)
                    throw new InvalidOperationException($"Unexpected field camera layout: {camera.name}.");
                _cameras.Add(camera);
                _cameraTargets.Add(camera.targetTexture.Pointer);
            }
        if (_cameras.Count != 4 || _cameras.FindAll(c => c.name == "CameraFieldMain").Count != 1 ||
            _cameras.FindAll(c => c.name == "CameraTileMap").Count != 1)
            throw new InvalidOperationException("Expected the four inspected field cameras.");
        var final = _cameras.Find(c => c.name == "CameraFieldMain")!;
        foreach (var c in _cameras) if (c.Pointer != final.Pointer && c.depth >= final.depth)
            throw new InvalidOperationException("Unexpected field camera ordering.");
        _followOffset = _following.camera.transform.position - _player.transform.position;
        // The captured preCameraPosition has already had native clamp/offset applied.
        // isScreenshot=true bypasses ONLY the input clamp in this inspected method;
        // using zero offset prevents applying the camera offset a second time.
        _scroll = new ScopedOverride<Vector3>(() => _renderer == null ? Vector3.zero : _renderer.preCameraPosition,
            value => { if (_renderer != null) _renderer.UpdateMapScrollIfNeed(value, Vector3.zero, true); }, Exact);
    }
    public void Initialize()
    {
        foreach (var camera in _cameras) {
            var pass = camera.gameObject.AddComponent<MapPass>(); _passes.Add(pass);
            if (camera.name == "CameraFieldMain") _last = pass;
        }
    }
    public bool Owns(MapPass pass) => _passes.Exists(p => p != null && p.Pointer == pass.Pointer);
    public bool IsLast(MapPass pass) => _last != null && _last.Pointer == pass.Pointer;
    private bool HasControl()
    {
        if (_player == null || (int)_player.moveState != 0 || _player.IsAutoMoving || _player.IsRiging) return false;
        return _controllers.Exists(c => c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer);
    }
    private static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static float XYError(Vector3 a, Vector3 b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

    public void Apply()
    {
        if (_lastFrame == Time.frameCount) return;
        Restore();
        if (!HasControl() || _renderer == null || !_renderer.isActiveAndEnabled || _following == null ||
            _following.TargetEntity == null || _following.TargetEntity.Pointer != _player.Pointer ||
            _compositor == null || _compositor.GetFloat("_BlurMainGame") != 0 || Math.Abs(Time.timeScale - 1) > .001f || Time.unscaledDeltaTime > .1f)
            throw new InvalidOperationException("Scene or player control changed.");
        if (_compositor.GetTexture("_MainGameTex") == null || _compositor.GetTexture("_MainGameTex").Pointer != _fieldTexture.Pointer)
            throw new InvalidOperationException("Field texture changed.");
        for (int i = 0; i < _cameras.Count; i++) {
            var camera = _cameras[i];
            if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture == null ||
                camera.targetTexture.Pointer != _cameraTargets[i] || camera.targetTexture.width != 320 || camera.targetTexture.height != 180)
                throw new InvalidOperationException("Field camera/target layout changed.");
        }
        var cameraPosition = _following.camera.transform.position;
        if (XYError(cameraPosition - _player.transform.position, _followOffset) > .001f)
            throw new InvalidOperationException("Camera boundary/offset changed.");
        var original = _renderer.preCameraPosition;
        if (XYError(original, cameraPosition) > .001f)
            throw new InvalidOperationException($"Map/camera scroll mismatch; map={original}, camera={cameraPosition}.");
        var local = _player.transform.localPosition;
        var start = _player.startPos; var dest = _player.destPos;
        if (!MotionResidual.TryCalculate(start.x, start.y, dest.x, dest.y, local.x, local.y,
            _player.moveTimer, _player.moveTime, out float x, out float y)) throw new InvalidOperationException("Unsupported movement.");
        var candidate = new Vector3(original.x + x, original.y + y, original.z);
        _originalRoot = _renderer.mainBGLayer.transform.localPosition;
        _verifyRestore = true;
        long began = Stopwatch.GetTimestamp();
        _scroll.Apply(candidate);
        _maxApplyMs = Math.Max(_maxApplyMs, (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
        var actual = _renderer.preCameraPosition;
        var root = _renderer.mainBGLayer.transform.localPosition;
        float error = XYError(actual, candidate);
        _maxError = Math.Max(_maxError, error);
        if (error > .0001f || actual.z != original.z) throw new InvalidOperationException("Native scroll did not retain fractional input.");
        if (!Exact(local, _player.transform.localPosition) || !Exact(cameraPosition, _following.camera.transform.position))
            throw new InvalidOperationException("Unexpected gameplay/camera position change during native map call.");
        _frames++; _lastFrame = Time.frameCount;
        if (Math.Abs(x) > .001f || Math.Abs(y) > .001f) {
            _fractionalFrames++;
            if (_samples.Count < 8) _samples.Add($"residual=({x:F5},{y:F5}), nativeBefore={original}, nativeAfter={actual}, rootBefore={_originalRoot}, rootAfter={root}");
        }
    }
    public void Restore()
    {
        bool owned = _scroll.Pending;
        _scroll.Restore();
        if (owned && _verifyRestore && _renderer != null && _renderer.mainBGLayer != null &&
            XYError(_renderer.mainBGLayer.transform.localPosition, _originalRoot) > .0001f)
            throw new InvalidOperationException("Map root did not restore to its recorded position.");
        _verifyRestore = false;
    }
    public void Dispose()
    {
        Restore();
        var passes = _passes.ToArray(); _passes.Clear(); _last = null;
        foreach (var pass in passes) if (pass != null) UnityEngine.Object.Destroy(pass);
        WriteSamples();
    }
    public void WriteSamples()
    {
        foreach (string sample in _samples) Test.Note("SAMPLE " + sample);
        _samples.Clear();
    }
}
