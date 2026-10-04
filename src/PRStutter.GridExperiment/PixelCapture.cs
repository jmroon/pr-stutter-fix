using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BepInEx;
using Il2CppInterop.Runtime.Injection;
using Last.Entity.Field;
using Last.Map;
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.GridExperiment;

public sealed class PixelPass : MonoBehaviour
{
    public PixelPass(IntPtr pointer) : base(pointer) { }
    public void OnPostRender() { try { PixelCapture.After(this); } catch (Exception e) { PixelCapture.Fault(e); } }
    public void OnDisable() { try { PixelCapture.Disabled(this); } catch (Exception e) { PixelCapture.Fault(e); } }
}

// Diagnostic only: synchronous, cropped readback of the field target before final
// composition. No claim that these samples measure physical display cadence.
internal static class PixelCapture
{
    private const int Width = 128, Height = 64, BytesPerFrame = Width * 2 * Height * 4, Capacity = 1024;
    private static Camera? _camera;
    private static CameraFollowing? _following;
    private static FieldPlayer? _player;
    private static readonly List<FieldPlayerController> Controllers = new();
    private static PixelPass? _pass;
    private static Texture2D? _cpu;
    private static byte[]? _pixels;
    private static Sample[]? _samples;
    private static Task? _save;
    private static int _count, _lastFrame, _sourceWidth, _sourceHeight, _x0, _x1, _y, _vsync;
    private static long _started;
    private static string _directory = "", _utc = "";
    private static readonly ScopedOverride<RenderTexture?> ActiveTarget = new(
        () => RenderTexture.active, value => RenderTexture.active = value,
        (a, b) => a == null ? b == null : b != null && a.Pointer == b.Pointer);

    public static void Register() => ClassInjector.RegisterTypeInIl2Cpp<PixelPass>();
    public static void Disabled(PixelPass pass) { if (_pass != null && _pass.Pointer == pass.Pointer) Stop("camera_disabled"); }
    public static void Tick()
    {
        ActiveTarget.Restore();
        if (_save is { IsCompleted: true }) {
            Test.Note(_save.IsFaulted ? $"PIXEL SAVE FAILED: {_save.Exception}" : "PIXEL SAVED: " + _directory);
            _save = null;
        }
        if (_pixels != null && (!Valid() || Stopwatch.GetTimestamp() - _started > Stopwatch.Frequency * 7))
            Stop("control_changed_or_timeout");
        if (ExperimentControls.SuppressKeys || !Input.GetKeyDown(KeyCode.F6)) return;
        if (_pixels != null) Stop("manual"); else Start();
    }
    private static void Start()
    {
        if (_save != null) { Test.Note("Previous pixel capture still saving."); return; }
        _camera = null; _following = null; Controllers.Clear();
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>()) {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (_following != null) throw new InvalidOperationException("Multiple pixel-capture follow targets.");
            _following = f;
        }
        _player = _following?.TargetEntity.TryCast<FieldPlayer>();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (c.name == "CameraTileMap" && c.isActiveAndEnabled) {
                if (_camera != null) throw new InvalidOperationException("Multiple tile cameras."); _camera = c;
            }
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (_player != null && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) Controllers.Add(c);
        _vsync = QualitySettings.vSyncCount;
        if (!Valid()) throw new InvalidOperationException("Pixel capture requires CRT off and ordinary manual field control.");
        _count = 0; _lastFrame = -1; _sourceWidth = 0; _sourceHeight = 0;
        _pixels = new byte[BytesPerFrame * Capacity]; _samples = new Sample[Capacity];
        _cpu = new Texture2D(Width * 2, Height, TextureFormat.RGBA32, false);
        _pass = _camera!.gameObject.AddComponent<PixelPass>();
        _directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter/pixels", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        _utc = DateTime.UtcNow.ToString("o"); _started = Stopwatch.GetTimestamp();
        Test.Note($"PIXEL ON (6s after 0.5s warmup): {_directory}; two 128x64 field patches, synchronous readback overhead recorded. Keep walking straight; F6 stops.");
    }
    private static bool Valid() => Application.isFocused && Time.timeScale == 1 &&
        QualitySettings.vSyncCount == _vsync && _camera != null && _camera.isActiveAndEnabled &&
        _following != null && _following.camera != null && _following.TargetEntity != null && _player != null &&
        _following.TargetEntity.Pointer == _player.Pointer && (int)_player.moveState == 0 && !_player.IsAutoMoving && !PRStutter.GridExperiment.GameProfile.TransportActive(_player) &&
        PostProcessLite.GetMaterial() != null && PostProcessLite.GetMaterial().GetFloat("_FakeCRT") == 0 &&
        PostProcessLite.GetMaterial().GetFloat("_BlurMainGame") == 0 && PostProcessLite.GetMaterial().GetFloat("_PartialFadeOverlay") == 0 &&
        Controllers.Exists(c => c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer);

    public static void After(PixelPass pass)
    {
        if (_pixels == null || _pass == null || _pass.Pointer != pass.Pointer) return;
        if (!Valid()) { Stop("control_changed"); return; }
        long now = Stopwatch.GetTimestamp();
        if (now - _started < Stopwatch.Frequency / 2) return;
        if (now - _started >= Stopwatch.Frequency * 6.5 || _count == Capacity) { Stop("complete"); return; }
        if (_lastFrame == Time.frameCount) throw new InvalidOperationException("Repeated tile render in one frame.");
        _lastFrame = Time.frameCount;
        var target = _camera!.targetTexture;
        if (target == null || !target.IsCreated()) throw new InvalidOperationException("No field target to read.");
        if (_sourceWidth == 0) {
            _sourceWidth = target.width; _sourceHeight = target.height;
            _x0 = Math.Clamp((int)(_sourceWidth * .20) - Width / 2, 0, _sourceWidth - Width);
            _x1 = Math.Clamp((int)(_sourceWidth * .80) - Width / 2, 0, _sourceWidth - Width);
            _y = Math.Clamp((int)(_sourceHeight * .65) - Height / 2, 0, _sourceHeight - Height);
        }
        if (target.width != _sourceWidth || target.height != _sourceHeight) { Stop("grid_changed"); return; }
        var local = _player!.transform.localPosition; var world = _player.transform.position;
        var start = _player.startPos; var dest = _player.destPos;
        if (!MotionResidual.TryCalculate(start.x, start.y, dest.x, dest.y, local.x, local.y,
            _player.moveTimer, _player.moveTime, out float rx, out float ry)) { Stop("unsupported_motion"); return; }
        var logical = _following!.camera.transform.position; var render = _camera.transform.position;
        var sample = new Sample { Frame = Time.frameCount, Qpc = now, Delta = Time.unscaledDeltaTime,
            Timer = _player.moveTimer, Duration = _player.moveTime, LogicalX = world.x, LogicalY = world.y,
            CandidateX = world.x + rx, CandidateY = world.y + ry, CameraX = logical.x, CameraY = logical.y,
            RenderX = render.x, RenderY = render.y, StartX = start.x, StartY = start.y, DestX = dest.x, DestY = dest.y };
        long readStart = Stopwatch.GetTimestamp();
        try {
            ActiveTarget.Apply(target);
            _cpu!.ReadPixels(new Rect(_x0, _y, Width, Height), 0, 0, false);
            _cpu.ReadPixels(new Rect(_x1, _y, Width, Height), Width, 0, false);
            var bytes = _cpu.GetRawTextureData();
            if (bytes.Length != BytesPerFrame) throw new InvalidOperationException("Unexpected RGBA byte count.");
            bytes.CopyTo(_pixels, _count * BytesPerFrame);
        } finally { ActiveTarget.Restore(); }
        sample.ReadTicks = Stopwatch.GetTimestamp() - readStart;
        sample.ObserverTicks = Stopwatch.GetTimestamp() - now;
        _samples![_count++] = sample;
    }

    public static void Stop(string reason)
    {
        ActiveTarget.Restore();
        var pixels = _pixels; var samples = _samples; _pixels = null; _samples = null;
        if (_pass != null) UnityEngine.Object.Destroy(_pass); _pass = null;
        if (_cpu != null) UnityEngine.Object.Destroy(_cpu); _cpu = null;
        if (pixels == null || samples == null) return;
        int count = _count; string directory = _directory;
        var metadata = new { schemaVersion = 1, startedUtc = _utc, qpcFrequency = Stopwatch.Frequency,
            sourceWidth = _sourceWidth, sourceHeight = _sourceHeight, patchWidth = Width, patchHeight = Height,
            packedWidth = Width * 2, bytesPerFrame = BytesPerFrame, count, x0 = _x0, x1 = _x1, y = _y,
            screenWidth = Screen.width, screenHeight = Screen.height, vsyncCount = _vsync, reason,
            pixelLayout = "RGBA32; two patches side by side; rows bottom to top as Unity ReadPixels",
            limitation = "Synchronous readback perturbs timing. Field target after tile-camera render, before final composition; not physical scanout. Measure readback cost and reject unreliable patch matches." };
        Test.Note($"PIXEL OFF ({reason}); {count} frames, saving {_directory}");
        _save = Task.Run(() => {
            Directory.CreateDirectory(directory);
            using (var stream = File.Create(Path.Combine(directory, "patches.rgba.partial"))) stream.Write(pixels, 0, count * BytesPerFrame);
            var csv = new StringBuilder("index,frame,qpc,delta_time,move_timer,move_duration,logical_x,logical_y,candidate_x,candidate_y,camera_x,camera_y,render_x,render_y,start_x,start_y,dest_x,dest_y,read_ticks,observer_ticks\n");
            for (int i = 0; i < count; i++) {
                var s = samples[i];
                csv.AppendLine(FormattableString.Invariant($"{i},{s.Frame},{s.Qpc},{s.Delta:R},{s.Timer:R},{s.Duration:R},{s.LogicalX:R},{s.LogicalY:R},{s.CandidateX:R},{s.CandidateY:R},{s.CameraX:R},{s.CameraY:R},{s.RenderX:R},{s.RenderY:R},{s.StartX:R},{s.StartY:R},{s.DestX:R},{s.DestY:R},{s.ReadTicks},{s.ObserverTicks}"));
            }
            File.WriteAllText(Path.Combine(directory, "motion.csv"), csv.ToString());
            File.Move(Path.Combine(directory, "patches.rgba.partial"), Path.Combine(directory, "patches.rgba"));
            File.WriteAllText(Path.Combine(directory, "capture.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        });
    }
    public static void Fault(Exception e) { Test.Note("PIXEL FAULT: " + e); try { Stop("fault"); } catch (Exception restore) { Test.Note("PIXEL RESTORE FAILED: " + restore); } }
    public static void Shutdown() { Stop("shutdown"); _save?.Wait(TimeSpan.FromSeconds(3)); }
    private struct Sample {
        public int Frame;
        public long Qpc, ReadTicks, ObserverTicks;
        public float Delta, Timer, Duration, LogicalX, LogicalY, CandidateX, CandidateY, CameraX, CameraY, RenderX, RenderY, StartX, StartY, DestX, DestY;
    }
}
