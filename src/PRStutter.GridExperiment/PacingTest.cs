using System;
using System.Collections.Generic;
using System.Diagnostics;
using Last.Entity.Field;
using Last.Map;
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.GridExperiment;

// Desktop Unity ignores targetFrameRate when vSyncCount > 0. Keep that game's
// value intact: gameplay code also reads it. This is a manual-field experiment,
// not a general high-FPS compatibility patch for menus, battles or animations.
internal static class PacingTest
{
    private static readonly ScopedOverride<int> Vsync = new(
        () => QualitySettings.vSyncCount, value => QualitySettings.vSyncCount = value);
    private static readonly List<FieldPlayerController> Controllers = new();
    private static FieldPlayer? _player;
    private static CameraFollowing? _following;
    private static bool _active;
    private static long _deadline, _lastReport;
    private static int _target, _frames;
    public static bool Active => _active;
    public static double Remaining => _active ? Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency) : 0;
    public static string LastStop { get; private set; } = "not started";

    public static void Tick()
    {
        if (!_active) Vsync.Restore(); // Retry a failed restoration before another test.
        if (_active) {
            if (!Valid() || QualitySettings.vSyncCount != 1 || Application.targetFrameRate != _target)
                Stop("control_or_settings_changed");
            else if (Stopwatch.GetTimestamp() >= _deadline)
                Stop("timeout");
        }
        if (!ExperimentControls.SuppressKeys && Input.GetKeyDown(KeyCode.F7)) {
            if (_active) Stop("manual"); else Start();
        }
        if (!_active) return;
        _frames++;
        long now = Stopwatch.GetTimestamp();
        if (now - _lastReport >= Stopwatch.Frequency * 5) {
            double fps = _frames * (double)Stopwatch.Frequency / (now - _lastReport);
            Test.Note($"PACE SAMPLE mode=vsync-display updateFps={fps:F2} targetFrameRate={Application.targetFrameRate} vSyncCount={QualitySettings.vSyncCount} screen={Screen.width}x{Screen.height} refreshReported={Screen.currentResolution.refreshRate}; update FPS is not display cadence.");
            _frames = 0; _lastReport = now;
        }
    }

    public static void Start(long deadline = 0)
    {
        if (_active) throw new InvalidOperationException("Pacing is already active.");
        Controllers.Clear(); _following = null; _player = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>()) {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (_following != null) throw new InvalidOperationException("Multiple pacing-test follow targets.");
            _following = f;
        }
        _player = _following?.TargetEntity.TryCast<FieldPlayer>();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (_player != null && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) Controllers.Add(c);
        if (!Valid()) throw new InvalidOperationException("Pacing test requires ordinary manual field control.");
        _target = Application.targetFrameRate;
        int originalVsync = QualitySettings.vSyncCount;
        if (originalVsync != 0 || _target != 60)
            throw new InvalidOperationException($"Expected stock targetFrameRate=60/vSyncCount=0; found {_target}/{originalVsync}.");
        Vsync.Apply(1);
        if (QualitySettings.vSyncCount != 1) throw new InvalidOperationException("VSync readback failed.");
        _lastReport = Stopwatch.GetTimestamp(); _deadline = deadline == 0 ? _lastReport + Stopwatch.Frequency * 90 : deadline;
        _frames = 0; _active = true; LastStop = "";
        Test.Note($"PACE ON mode=vsync-display ({Remaining:F1}s): vSyncCount=0->1; targetFrameRate=60 retained. Coordinated={ExperimentControls.Coordinated}.");
    }

    private static bool Valid() => Application.isFocused && Time.timeScale == 1 &&
        _player != null && _following != null && _following.TargetEntity != null &&
        _following.TargetEntity.Pointer == _player.Pointer && _following.camera != null && _following.camera.isActiveAndEnabled &&
        _player.gameObject.activeInHierarchy && (int)_player.moveState == 0 && !_player.IsAutoMoving && !_player.IsRiging && !_player.pauseMoving &&
        Controllers.Exists(c => c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer);

    public static void Stop(string reason)
    {
        bool wasActive = _active; _active = false;
        if (wasActive) LastStop = reason;
        Vsync.Restore();
        if (wasActive) Test.Note($"PACE OFF ({reason}); targetFrameRate={Application.targetFrameRate} vSyncCount={QualitySettings.vSyncCount}; owned VSync override released.");
        Controllers.Clear(); _player = null; _following = null;
    }
    public static void Fault(Exception e)
    {
        try { Stop("fault"); } catch (Exception restore) { Test.Note($"PACE RESTORE FAILED: {restore}"); }
        LastStop = "fault: " + e.Message;
        Test.Note($"PACE FAULT: {e}");
    }
}
