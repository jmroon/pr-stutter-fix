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
    private static bool _active, _managedField, _restoreFaulted;
    private static Func<bool>? _fieldEligible;
    public static bool CleanupComplete => !Vsync.Pending && !_active;
    private static long _deadline, _lastReport;
    private static int _target, _frames;
    public static bool Active => _active;
    public static double Remaining => _active ? Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency) : 0;
    public static string LastStop { get; private set; } = "not started";

    public static void Tick()
    {
        if (!_active && !_restoreFaulted) Vsync.Restore(); // Retry a failed restoration before another test.
        if (_active) {
            if (QualitySettings.vSyncCount != 1 || Application.targetFrameRate != _target)
                Stop("settings_changed");
            else if (!Valid()) Stop(_managedField ? "context_changed" : "control_or_settings_changed");
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

    public static void Start(long deadline = 0) => StartCore(deadline, null);
    public static void StartField(long deadline, Func<bool> eligible) => StartCore(deadline, eligible);
    private static void StartCore(long deadline, Func<bool>? eligible)
    {
        if (_active || Vsync.Pending) throw new InvalidOperationException("Previous pacing override is still owned.");
        _managedField = eligible != null; _fieldEligible = eligible; _restoreFaulted = false;
        Controllers.Clear(); _following = null; _player = null;
        if (!_managedField) {
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>()) {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (_following != null) throw new InvalidOperationException("Multiple pacing-test follow targets.");
            _following = f;
        }
        _player = _following?.TargetEntity.TryCast<FieldPlayer>();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (_player != null && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) Controllers.Add(c);
        }
        if (!Valid()) throw new InvalidOperationException("Pacing requires an eligible context.");
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

    private static bool Valid() => _managedField ? Application.isFocused && Time.timeScale == 1 && _fieldEligible?.Invoke() == true : Application.isFocused && Time.timeScale == 1 &&
        _player != null && _following != null && _following.TargetEntity != null &&
        _following.TargetEntity.Pointer == _player.Pointer && _following.camera != null && _following.camera.isActiveAndEnabled &&
        _player.gameObject.activeInHierarchy && (int)_player.moveState == 0 && !_player.IsAutoMoving && !PRStutter.GridExperiment.GameProfile.TransportActive(_player) && !_player.pauseMoving &&
        Controllers.Exists(c => c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer);

    public static void Stop(string reason)
    {
        bool wasActive = _active; _active = false;
        if (wasActive) LastStop = reason;
        try { Vsync.Restore(); _restoreFaulted = false; }
        catch { _restoreFaulted = true; throw; }
        finally { Controllers.Clear(); _player = null; _following = null; _fieldEligible = null; _managedField = false; }
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
