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
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.GridExperiment;

[BepInPlugin("local.prstutter.grid", "PR Stutter Grid Test", "0.8.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        if (!Matches("GameAssembly.dll", "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd") ||
            !Matches("FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat", "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd"))
        { Log.LogError("Unsupported build; grid test disabled."); return; }
        Test.Log = Log;
        ClassInjector.RegisterTypeInIl2Cpp<GridPass>();
        PixelCapture.Register();
        _driver = AddComponent<Driver>();
        ExperimentControls.Ready = true;
        Test.Note("0.8.0 ready, OFF by default. F4: automatic corrections via the timing plugin and status panel. Separate controls: F6 pixel diagnostic; F7 pacing (90s); F9 4x / F10 8x smoothing (15s). CRT OFF. F6 disabled during a combined test. Ordinary cardinal and diagonal manual walking only.");
    }
    public override bool Unload()
    {
        ExperimentControls.Ready = false;
        PixelCapture.Shutdown();
        PacingTest.Stop("unload");
        Test.Stop("unload"); Test.DisposeStopped();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
    private static bool Matches(string path, string hash)
    {
        using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, path));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() {
        try { PacingTest.Tick(); } catch (Exception e) { PacingTest.Fault(e); }
        try { Test.Tick(); } catch (Exception e) { Test.Fault(e); }
        try { PixelCapture.Tick(); } catch (Exception e) { PixelCapture.Fault(e); }
    }
    public void LateUpdate() { try { Test.Prepare(); } catch (Exception e) { Test.Fault(e); } }
    public void OnApplicationFocus(bool focused) { if (!focused) try { PacingTest.Stop("focus_lost"); } catch (Exception e) { PacingTest.Fault(e); } }
    public void OnApplicationQuit() {
        try { PixelCapture.Shutdown(); } catch (Exception e) { PixelCapture.Fault(e); }
        try { PacingTest.Stop("quit"); } catch (Exception e) { PacingTest.Fault(e); }
        try { Test.Stop("quit"); } catch (Exception e) { Test.Fault(e); }
    }
}
public sealed class GridPass : MonoBehaviour
{
    public GridPass(IntPtr pointer) : base(pointer) { }
    public void OnPreCull() { try { Test.Before(this); } catch (Exception e) { Test.Fault(e); } }
    public void OnPostRender() { try { Test.After(this); } catch (Exception e) { Test.Fault(e); } }
    public void OnDisable() { try { Test.Disabled(this); } catch (Exception e) { Test.Fault(e); } }
}
internal static class Test
{
    public static ManualLogSource? Log;
    private static Session? _session;
    private static bool _active;
    private static long _deadline;
    public static bool Active => _active;
    public static double Remaining => _active ? Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency) : 0;
    public static string LastStop { get; private set; } = "not started";
    public static string Mode => _session?.Mode ?? "";
    public static void Note(string message)
    {
        Log?.LogInfo(message);
        try {
            var directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "grid-test.log"), $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        } catch (Exception e) { Log?.LogWarning($"Evidence write failed: {e.Message}"); }
    }
    public static void Tick()
    {
        _session?.Restore(); // Recovery when a camera callback is skipped.
        DisposeStopped();
        if (_active && Expired()) Stop("timeout");
        if (ExperimentControls.SuppressKeys) return;
        bool high = Input.GetKeyDown(KeyCode.F10);
        if (!high && !Input.GetKeyDown(KeyCode.F9)) return;
        if (_active) { Stop("manual"); return; }
        Start(high ? 8 : 4);
    }
    public static void Start(int scale, long deadline = 0)
    {
        if (_active) throw new InvalidOperationException("Smoothing is already active.");
        DisposeStopped();
        _session = new Session(scale);
        _session.Initialize(); // Session owns partial initialization before this call.
        _deadline = deadline == 0 ? Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15 : deadline;
        _active = true; LastStop = "";
        Note($"GRID ON mode={_session.Mode} ({Remaining:F1}s): {_session.TargetSize} field/upper/ceiling targets. CRT OFF. Coordinated={ExperimentControls.Coordinated}.");
    }
    private static bool Expired() => Stopwatch.GetTimestamp() >= _deadline;
    public static void Prepare()
    {
        if (!_active || _session == null) return;
        if (Expired()) { Stop("timeout"); return; }
        _session.Prepare();
    }
    public static void Before(GridPass pass)
    {
        if (!_active || _session == null || !_session.Owns(pass)) return;
        if (Expired()) { Stop("timeout"); return; }
        _session.Before(pass);
    }
    public static void After(GridPass pass)
    { if (_active && _session != null && _session.Owns(pass)) _session.After(pass); }
    public static void Disabled(GridPass pass)
    { if (_active && _session != null && _session.Owns(pass)) Stop("camera_disabled"); }
    public static void Stop(string reason)
    {
        bool wasActive = _active; _active = false;
        if (wasActive) LastStop = reason;
        _session?.Restore();
        if (wasActive) Note($"GRID OFF ({reason}); {_session?.Summary}. Render bindings restored.");
    }
    public static void Fault(Exception e)
    {
        _active = false;
        LastStop = "fault: " + e.Message;
        try { _session?.Restore(); } catch (Exception restore) { Note($"RESTORE FAILED: {restore}"); }
        Note($"GRID FAULT: {e}; {_session?.Summary}");
    }
    public static void DisposeStopped()
    { if (!_active && _session != null) { _session.Dispose(); _session = null; } }
}

internal sealed class Session
{
    private readonly int Scale;
    private readonly List<Target> _targets = new();
    private readonly List<CameraBinding> _cameras = new();
    private readonly List<TextureBinding> _textures = new();
    private readonly List<GridPass> _passes = new();
    private readonly List<float> _biases = new();
    private readonly List<FieldPlayerController> _controllers = new();
    private readonly CameraFollowing _following;
    private readonly FieldPlayer _player;
    private readonly Material _compositor;
    private readonly Camera _front;
    private readonly Vector3 _playerPosition;
    private readonly Vector3 _followOffset;
    private VisualMotion? _motion;
    private readonly int _width = Screen.width, _height = Screen.height;
    private readonly float _fakeCrt;
    private GridPass? _final;
    private int _prepared = -1, _frames;
    private readonly HashSet<IntPtr> _rendered = new();
    private readonly HashSet<IntPtr> _begun = new();
    public string Mode => $"fractional-motion-{Scale}x";
    public string TargetSize => $"{320 * Scale}x{180 * Scale}";
    public string Summary => $"mode={Mode}, completedFrames={_frames}, targets={_targets.Count}, textureBindings={_textures.Count}, CRT={_fakeCrt}, {_motion?.Summary}";
    private static bool Same(Texture? a, Texture? b) => a == null ? b == null : b != null && a.Pointer == b.Pointer;
    private static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    public Session(int scale)
    {
        if (scale is not (4 or 8)) throw new ArgumentOutOfRangeException(nameof(scale));
        Scale = scale;
        if (SystemInfo.maxTextureSize < 320 * scale) throw new InvalidOperationException("Requested grid exceeds the GPU texture-size limit.");
        CameraFollowing? following = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>())
            if (f.TargetEntity != null && f.camera != null && f.camera.isActiveAndEnabled) {
                if (following != null) throw new InvalidOperationException("Multiple follow targets."); following = f;
            }
        _following = following ?? throw new InvalidOperationException("No active field follow target.");
        _player = _following.TargetEntity.TryCast<FieldPlayer>() ?? throw new InvalidOperationException("Follow target is not the player.");
        _playerPosition = _player.transform.position;
        _followOffset = _following.camera.transform.position - _playerPosition;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) _controllers.Add(c);
        _compositor = PostProcessLite.GetMaterial();
        if (_compositor == null || _compositor.shader.name != "Last/PostProcessLite") throw new InvalidOperationException("Unsupported compositor.");
        _fakeCrt = _compositor.GetFloat("_FakeCRT");
        if (_fakeCrt != 0) throw new InvalidOperationException("Turn CRT OFF in the game settings before this test.");
        Camera? front = null;
        var names = new HashSet<string>();
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>()) {
            if (!camera.isActiveAndEnabled) continue;
            if (camera.name == "CameraFrontFilter") {
                if (front != null) throw new InvalidOperationException("Multiple final cameras."); front = camera;
            }
            if (camera.name is not ("CameraUpperTransparentRT" or "CameraCeilTransparentRT" or "CameraTileMap" or "CameraFieldMain")) continue;
            if (!names.Add(camera.name) || camera.targetTexture == null || !camera.orthographic ||
                Math.Abs(camera.orthographicSize - 90) > .001f || camera.rect != new Rect(0, 0, 1, 1))
                throw new InvalidOperationException("Unsupported camera: " + camera.name);
            var original = camera.targetTexture;
            if (original.width != 320 || original.height != 180 || original.antiAliasing != 1 || !original.IsCreated())
                throw new InvalidOperationException("Expected original 320x180, single-sample targets.");
            var target = _targets.Find(t => Same(t.Original, original));
            if (target == null) { target = new Target(original); _targets.Add(target); }
            _cameras.Add(new CameraBinding(camera, target));
        }
        _front = front ?? throw new InvalidOperationException("No final compositor camera.");
        if (_cameras.Count != 4 || _targets.Count != 3 || _front.targetTexture != null || _front.depth != 99 ||
            _front.rect != new Rect(0, 0, 1, 1)) throw new InvalidOperationException("Expected four field cameras, three textures and screen compositor.");
        foreach (var c in _cameras) if (c.Camera.depth >= _front.depth) throw new InvalidOperationException("Unexpected render order.");
        var main = _cameras.Find(c => c.Camera.name == "CameraFieldMain")!;
        var tile = _cameras.Find(c => c.Camera.name == "CameraTileMap")!;
        if (!Same(main.Target.Original, tile.Target.Original) || !Same(main.Target.Original, _compositor.GetTexture("_MainGameTex")))
            throw new InvalidOperationException("Main field/compositor texture mismatch.");
        if (main.Camera.Pointer != _following.camera.Pointer || main.Camera.cullingMask != 0)
            throw new InvalidOperationException("Expected logical follow camera with no field draw layers.");
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (camera.isActiveAndEnabled && _targets.Exists(t => Same(t.Original, camera.targetTexture)) &&
                !_cameras.Exists(c => c.Camera.Pointer == camera.Pointer))
                throw new InvalidOperationException("Additional camera uses a field target: " + camera.name);
        // Enumerate existing material references without Renderer.material instantiation.
        // The inspected native AttachTransparentTexture methods bind local material textures.
        foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            foreach (string property in material.GetTexturePropertyNames()) {
                var texture = material.GetTexture(property);
                var target = _targets.Find(t => Same(t.Original, texture));
                if (target != null) _textures.Add(new TextureBinding(material, property, target));
            }
        if (!_textures.Exists(b => b.Material.Pointer == _compositor.Pointer && b.Property == "_MainGameTex"))
            throw new InvalidOperationException("Compositor texture binding was not discovered.");
        foreach (string property in new[] { "_MainGameDiffuseBias", "_OverlayDiffuseBias" }) {
            float value = _compositor.GetFloat(property);
            if (!float.IsFinite(value)) throw new InvalidOperationException("Invalid CRT bias.");
            _biases.Add(value);
        }
        ValidateScene();
    }
    public void Initialize()
    {
        var renderCameras = new List<Camera>();
        foreach (var c in _cameras) if (c.Camera.name != "CameraFieldMain") renderCameras.Add(c.Camera);
        _motion = new VisualMotion(_player, _following.camera, renderCameras, Scale);
        foreach (var target in _targets) {
            target.High = new RenderTexture(target.Original);
            target.High.name = "PRStutter.Grid." + target.Original.name;
            target.High.width = 320 * Scale; target.High.height = 180 * Scale;
            target.High.filterMode = target.Original.filterMode;
            target.High.wrapMode = target.Original.wrapMode;
            if (!target.High.Create()) throw new InvalidOperationException("Render texture allocation failed.");
            Test.Note($"Target {target.Original.name}: id={target.Original.GetInstanceID()}, 320x180 -> {target.High.width}x{target.High.height}, format={target.High.format}, filter={target.High.filterMode}, depth={target.High.depth}");
        }
        foreach (var c in _cameras) _passes.Add(c.Camera.gameObject.AddComponent<GridPass>());
        _final = _front.gameObject.AddComponent<GridPass>(); _passes.Add(_final);
        foreach (var binding in _textures) Test.Note($"Consumer {binding.Material.name}/{binding.Material.shader.name}.{binding.Property} <- {binding.Target.Original.name}");
        foreach (var target in _targets)
            Test.Note($"Target {target.Original.name}: {_textures.FindAll(b => b.Target == target).Count} discovered material texture consumers. Zero consumers means this scene may not exercise that transparency buffer.");
        Test.Note($"CRT disabled; existing shader settings retained. Screen={_width}x{_height}; mode={Mode}; max quantization error={0.5f / Scale:F5} game units. 8x has four times the target pixels of 4x; performance is unverified.");
    }
    private void ValidateScene()
    {
        if (!Application.isFocused || _player == null || _following == null || _following.TargetEntity == null ||
            _following.TargetEntity.Pointer != _player.Pointer || (int)_player.moveState != 0 ||
            _player.IsAutoMoving || _player.IsRiging ||
            !_controllers.Exists(c => c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer))
            throw new InvalidOperationException("Ordinary manual field control required; control/state changed.");
        var offset = _following.camera.transform.position - _player.transform.position;
        if ((offset - _followOffset).sqrMagnitude > .000001f)
            throw new InvalidOperationException("Camera-follow offset changed (map boundary or scripted camera).");
        var start = _player.startPos; var dest = _player.destPos; var local = _player.transform.localPosition;
        if (!MotionResidual.TryCalculate(start.x, start.y, dest.x, dest.y, local.x, local.y,
                _player.moveTimer, _player.moveTime, out _, out _))
            throw new InvalidOperationException("Only ordinary adjacent-tile walking is supported.");
        if (Time.timeScale != 1 || Screen.width != _width || Screen.height != _height || _front == null || !_front.isActiveAndEnabled ||
            _front.targetTexture != null || _compositor == null || _compositor.GetFloat("_FakeCRT") != _fakeCrt ||
            _compositor.GetFloat("_BlurMainGame") != 0 || _compositor.GetFloat("_PartialFadeOverlay") != 0)
            throw new InvalidOperationException("Display, CRT mode, camera or transition changed.");
        foreach (var c in _cameras)
            if (c.Camera == null || !c.Camera.isActiveAndEnabled ||
                (c.Camera.name != "CameraFieldMain" && !Exact(c.Camera.transform.position, c.Position + (_motion?.CameraOffset ?? Vector3.zero))) ||
                c.Camera.transform.rotation != c.Rotation || !c.Camera.orthographic || c.Camera.orthographicSize != c.Size ||
                c.Camera.rect != new Rect(0, 0, 1, 1) ||
                c.Camera.depth != c.Depth || c.Camera.cullingMask != c.Mask || c.Camera.clearFlags != c.Clear)
                throw new InvalidOperationException("Field camera moved or changed.");
    }
    public bool Owns(GridPass pass) => _passes.Exists(p => p != null && p.Pointer == pass.Pointer);
    public bool IsFinal(GridPass pass) => _final != null && _final.Pointer == pass.Pointer;
    public void Prepare()
    {
        Restore(); ValidateScene();
        foreach (var c in _cameras) if (!Same(c.Camera.targetTexture, c.Target.Original)) throw new InvalidOperationException("Camera target changed.");
        foreach (var b in _textures) if (b.Material == null || !Same(b.Material.GetTexture(b.Property), b.Target.Original)) throw new InvalidOperationException("Texture consumer changed.");
        foreach (var t in _targets) if (t.High == null || !t.High.IsCreated()) throw new InvalidOperationException("Owned target lost.");
        foreach (var c in _cameras) c.Override.Apply(c.Target.High!);
        foreach (var b in _textures) b.Override.Apply(b.Target.High!);
        _rendered.Clear(); _begun.Clear(); _prepared = Time.frameCount;
    }
    public void Before(GridPass pass)
    {
        ValidateScene();
        if (_prepared != Time.frameCount) throw new InvalidOperationException("Camera rendered before target preparation.");
        foreach (var c in _cameras) if (!Same(c.Camera.targetTexture, c.Target.High)) throw new InvalidOperationException("Render target overwritten before draw.");
        foreach (var b in _textures) if (b.Material == null || !Same(b.Material.GetTexture(b.Property), b.Target.High)) throw new InvalidOperationException("Texture binding overwritten before draw.");
        for (int i = 0; i < _biases.Count; i++) {
            string name = i == 0 ? "_MainGameDiffuseBias" : "_OverlayDiffuseBias";
            if (_compositor.GetFloat(name) != _biases[i]) throw new InvalidOperationException("Compositor settings changed before draw.");
        }
        if (!IsFinal(pass) && _begun.Count == 0) _motion?.Apply();
        _motion?.ValidateApplied();
        if (IsFinal(pass)) {
            if (_rendered.Count != 4) throw new InvalidOperationException("Incomplete field rendering before compositor.");
        } else if (!_begun.Add(pass.Pointer)) throw new InvalidOperationException("Repeated field-camera draw.");
    }
    public void After(GridPass pass)
    {
        if (_prepared != Time.frameCount) throw new InvalidOperationException("Post-render without prepared targets.");
        if (IsFinal(pass)) {
            if (_rendered.Count != 4) throw new InvalidOperationException("Post-compositor without four completed field cameras.");
            _frames++;
            Restore();
        } else if (!_begun.Contains(pass.Pointer) || !_rendered.Add(pass.Pointer))
            throw new InvalidOperationException("Unexpected field post-render callback.");
    }
    public void Restore()
    {
        Exception? error = null;
        try { _motion?.Restore(); } catch (Exception e) { error = e; }
        foreach (var b in _textures) try { b.Override.Restore(); } catch (Exception e) { error ??= e; }
        foreach (var c in _cameras) try { c.Override.Restore(); } catch (Exception e) { error ??= e; }
        _prepared = -1;
        if (error != null) throw error;
    }
    public void Dispose()
    {
        Restore(); // No release while restoration remains incomplete.
        foreach (var p in _passes) if (p != null) UnityEngine.Object.Destroy(p);
        _passes.Clear(); _final = null;
        foreach (var target in _targets) if (target.High != null) {
            target.High.Release(); UnityEngine.Object.Destroy(target.High); target.High = null;
        }
    }
    private sealed class Target
    {
        public readonly RenderTexture Original;
        public RenderTexture? High;
        public Target(RenderTexture original) { Original = original; }
    }
    private sealed class CameraBinding
    {
        public readonly Camera Camera;
        public readonly Target Target;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float Size, Depth;
        public readonly int Mask;
        public readonly CameraClearFlags Clear;
        public readonly ScopedOverride<RenderTexture> Override;
        public CameraBinding(Camera camera, Target target)
        {
            Camera = camera; Target = target; Position = camera.transform.position; Rotation = camera.transform.rotation;
            Size = camera.orthographicSize; Depth = camera.depth; Mask = camera.cullingMask; Clear = camera.clearFlags;
            Override = new ScopedOverride<RenderTexture>(() => camera == null ? target.Original : camera.targetTexture,
                t => { if (camera != null) camera.targetTexture = t; }, Same);
        }
    }
    private sealed class TextureBinding
    {
        public readonly Material Material;
        public readonly string Property;
        public readonly Target Target;
        public readonly ScopedOverride<Texture> Override;
        public TextureBinding(Material material, string property, Target target)
        {
            Material = material; Property = property; Target = target;
            Override = new ScopedOverride<Texture>(() => material == null ? target.Original : material.GetTexture(property),
                t => { if (material != null) material.SetTexture(property, t); }, Same);
        }
    }
}
