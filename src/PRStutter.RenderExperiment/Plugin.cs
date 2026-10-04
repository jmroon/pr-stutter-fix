using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using Last.Entity.Field;
using UnityEngine;

namespace PRStutter.RenderExperiment;

[BepInPlugin("local.prstutter.renderexperiment", "PR Stutter Render Experiment", "0.2.1")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        if (!Matches(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"), "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd") ||
            !Matches(Path.Combine(Paths.GameRootPath, "FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat"), "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd"))
        { Log.LogError("Unsupported game build; experiment disabled."); return; }
        Experiment.Log = Log;
        ClassInjector.RegisterTypeInIl2Cpp<CompositorPass>();
        ClassInjector.RegisterTypeInIl2Cpp<FieldPass>();
        _driver = AddComponent<Driver>();
        Experiment.Note("0.2.1 ready, OFF by default. F9: background-only; F10: larger field buffer plus player stabilization. Either key stops an active test. Each test lasts at most 15s. Gameplay movement is unchanged.");
    }
    public override bool Unload()
    {
        Experiment.Stop("unload");
        Experiment.DisposeStopped();
        Experiment.RemovePass();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
    private static bool Matches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() { try { Experiment.Tick(); } catch (Exception e) { Experiment.Fault(e); } }
    public void LateUpdate() { try { Experiment.PrepareField(); } catch (Exception e) { Experiment.Fault(e); } }
    public void OnApplicationQuit() => Experiment.Stop("quit");
}

// Unity callbacks on the final compositing camera, not detours on game methods.
public sealed class CompositorPass : MonoBehaviour
{
    public CompositorPass(IntPtr pointer) : base(pointer) { }
    public void OnPreCull() { try { Experiment.Apply(); } catch (Exception e) { Experiment.Fault(e); } }
    public void OnPostRender() { try { Experiment.RestoreAll(); } catch (Exception e) { Experiment.Fault(e); } }
    public void OnDisable() { try { Experiment.Disabled(this); } catch (Exception e) { Experiment.Fault(e); } }
}

internal static class Experiment
{
    public static ManualLogSource? Log;
    private static readonly string[] Bounds = { "_MainGameUL", "_MainGameUH", "_MainGameVL", "_MainGameVH" };
    private static UvOverride? _uv;
    private static CameraFollowing? _following;
    private static FieldPlayer? _player;
    private static Camera? _front;
    private static Material? _material;
    private static Texture? _texture;
    private static CompositorPass? _pass;
    private static Vector3 _followOffset;
    private static long _started;
    private static bool _active;
    private static int _frames, _lastFrame = -1;
    private static float _maxResidual;
    private static PlayerStabilizer? _stabilizer;
    private static string _mode = "background";

    public static void Note(string message)
    {
        Log?.LogInfo(message);
        try {
            var directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "render-experiment.log"), $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        } catch (Exception e) { Log?.LogWarning($"Could not write experiment evidence: {e.Message}"); }
    }

    public static void Tick()
    {
        RestoreAll(); // Also recover if OnPostRender was skipped.
        DisposeStopped();
        if (_active && Stopwatch.GetTimestamp() - _started >= Stopwatch.Frequency * 15) Stop("timeout");
        bool stable = Input.GetKeyDown(KeyCode.F10);
        if (stable || Input.GetKeyDown(KeyCode.F9)) { if (_active) Stop("manual"); else Start(stable); }
    }

    private static void Start(bool stabilize)
    {
        CameraFollowing? found = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>())
        {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (found != null) { Log?.LogWarning("Multiple field targets; experiment refused."); return; }
            found = f;
        }
        var player = found?.TargetEntity?.TryCast<FieldPlayer>();
        var material = PostProcessLite.GetMaterial();
        if (found == null || player == null || material == null || material.shader.name != "Last/PostProcessLite")
        { Log?.LogWarning("No supported field scene; experiment refused."); return; }
        foreach (var name in Bounds) if (!material.HasProperty(name)) return;
        var texture = material.GetTexture("_MainGameTex");
        if (texture == null || texture.width != 320 || texture.height != 180 ||
            found.camera.targetTexture == null || texture.Pointer != found.camera.targetTexture.Pointer ||
            Math.Abs(found.camera.orthographicSize - 90) > .001f || !found.camera.orthographic)
        { Log?.LogWarning("Render layout differs from inspected scene; experiment refused."); return; }
        Camera? front = null;
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (camera.name == "CameraFrontFilter" && camera.isActiveAndEnabled)
            { if (front != null) return; front = camera; }
        if (front == null) return;
        if (stabilize && (front.targetTexture != null || front.depth != 99 || front.rect != new Rect(0, 0, 1, 1)))
        { Note("F10 refused: output camera differs from inspected scene."); return; }
        var original = new float[4];
        for (int i = 0; i < Bounds.Length; i++)
        {
            original[i] = material.GetFloat(Bounds[i]);
            if (!float.IsFinite(original[i]) || Math.Abs(original[i] - (i % 2)) > .0001f)
            { Log?.LogWarning("Nonstandard field UV range; experiment refused."); return; }
        }
        bool reusePass = _pass != null && _front != null && _front.Pointer == front.Pointer;
        if (!reusePass) RemovePass();
        _following = found; _player = player; _front = front; _material = material; _texture = texture;
        _uv = new UvOverride(original, i => material.GetFloat(Bounds[i]), (i, value) => material.SetFloat(Bounds[i], value));
        _followOffset = found.camera.transform.position - player.transform.position;
        if (!reusePass) _pass = front.gameObject.AddComponent<CompositorPass>();
        _mode = stabilize ? "player-stabilized" : "background";
        if (stabilize) {
            var rt = texture.TryCast<RenderTexture>() ?? throw new InvalidOperationException("Field texture is not a render target.");
            _stabilizer = new PlayerStabilizer(player, found.camera, rt, material);
            _stabilizer.Initialize();
        }
        _frames = 0; _lastFrame = -1; _maxResidual = 0;
        _started = Stopwatch.GetTimestamp(); _active = true;
        Note($"EXPERIMENT ON mode={_mode} (15s). F9/F10 stops. Field target scale={(stabilize ? PlayerStabilizer.Scale : 1)}.");
    }

    private static bool ReadMotion(out float rx, out float ry)
    {
        rx = ry = 0;
        if (!_active) return false;
        if (Stopwatch.GetTimestamp() - _started >= Stopwatch.Frequency * 15) { Stop("timeout"); return false; }
        if (_following == null || _player == null || _front == null || _material == null || _texture == null ||
            _following.TargetEntity == null || _following.TargetEntity.Pointer != _player.Pointer ||
            _following.camera == null || !_following.camera.isActiveAndEnabled || !_front.isActiveAndEnabled ||
            (int)_player.moveState != 0 || Math.Abs(Time.timeScale - 1) > .001f || Time.unscaledDeltaTime > .1f ||
            _material.GetFloat("_BlurMainGame") != 0 || _texture.width != 320 || _texture.height != 180)
        { Stop("scene_changed"); return false; }
        var currentTexture = _material.GetTexture("_MainGameTex");
        if (currentTexture == null || currentTexture.Pointer != _texture.Pointer) { Stop("texture_changed"); return false; }
        var offset = _following.camera.transform.position - _player.transform.position;
        if (Math.Abs(offset.x - _followOffset.x) > .001f || Math.Abs(offset.y - _followOffset.y) > .001f)
        { Stop("camera_boundary_or_offset_changed"); return false; }
        var s = _player.startPos; var d = _player.destPos; var p = _player.transform.localPosition;
        if (!MotionResidual.TryCalculate(s.x, s.y, d.x, d.y, p.x, p.y,
            _player.moveTimer, _player.moveTime, out rx, out ry))
        { Stop("unsupported_motion"); return false; }
        return true;
    }

    public static void PrepareField()
    {
        if (_stabilizer != null && ReadMotion(out _, out _)) _stabilizer.Prepare();
    }
    public static void FieldPreCull(FieldPass pass)
    {
        if (_stabilizer?.Pass == null || _stabilizer.Pass.Pointer != pass.Pointer) return;
        if (ReadMotion(out float x, out float y)) _stabilizer.OffsetVisuals(x, y);
    }
    public static void FieldPostRender(FieldPass pass)
    {
        if (_stabilizer?.Pass != null && _stabilizer.Pass.Pointer == pass.Pointer) _stabilizer.AfterField();
    }
    public static void FieldDisabled(FieldPass pass)
    {
        if (_stabilizer?.Pass != null && _stabilizer.Pass.Pointer == pass.Pointer) Stop("field_camera_disabled");
    }

    public static void Apply()
    {
        Restore();
        if (!ReadMotion(out float rx, out float ry)) return;
        if (_stabilizer != null) {
            _stabilizer.Present();
            rx = _stabilizer.X; ry = _stabilizer.Y;
        }
        // Shift sampling by the lost fraction; do not touch overlay UVs or texture data.
        if (_uv == null || !_uv.Apply(rx / 320, ry / 180)) { Stop("game_uv_changed"); return; }
        _maxResidual = Math.Max(_maxResidual, Math.Max(Math.Abs(rx), Math.Abs(ry)));
        if (Time.frameCount != _lastFrame) { _frames++; _lastFrame = Time.frameCount; }
    }

    public static void Restore()
    {
        if (_material != null) _uv?.Restore();
        else _uv = null; // A destroyed material cannot retain an override.
    }
    public static void RestoreAll()
    {
        Exception? error = null;
        try { Restore(); } catch (Exception e) { error = e; }
        try { _stabilizer?.Restore(); } catch (Exception e) { error ??= e; }
        if (error != null) throw error;
    }
    public static void Stop(string reason)
    {
        bool wasActive = _active; _active = false;
        RestoreAll();
        if (wasActive) Note($"EXPERIMENT OFF mode={_mode} ({reason}); {_frames} render frames; max correction {_maxResidual:F4} game units. Rendering overrides restored.");
    }
    public static void DisposeStopped()
    {
        // Release in Update, outside a rendering callback that may still hold the target.
        if (!_active && _stabilizer != null) { _stabilizer.Dispose(); _stabilizer = null; }
    }
    public static void RemovePass()
    {
        var old = _pass; _pass = null;
        if (old != null) UnityEngine.Object.Destroy(old);
    }
    public static void Disabled(CompositorPass pass)
    {
        if (_pass != null && _pass.Pointer == pass.Pointer) Stop("compositor_disabled");
    }
    public static void Fault(Exception e)
    {
        _active = false;
        try { RestoreAll(); } catch (Exception restoreError) { Log?.LogError($"Render restore failed: {restoreError.Message}"); }
        Note($"EXPERIMENT FAULT mode={_mode}: {e}");
    }
}
