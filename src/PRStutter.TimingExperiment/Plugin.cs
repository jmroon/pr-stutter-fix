using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Last.Entity.Field;
using Last.Management;
using Last.Map;
using UnityEngine;
using Sample = PRStutter.TimingExperiment.MovementSample;
using Row = PRStutter.TimingExperiment.MovementObservation;

namespace PRStutter.TimingExperiment;

[BepInPlugin("local.prstutter.timing", "PR Stutter Tile Timing Test", "0.4.0")]
[BepInDependency("local.prstutter.grid", "0.7.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        if (!Matches("GameAssembly.dll", "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd") ||
            !Matches("FINAL FANTASY VI_Data/il2cpp_data/Metadata/global-metadata.dat", "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd"))
        { Log.LogError("Unsupported build; timing test disabled."); return; }
        Timing.Log = Log;
        _driver = AddComponent<Driver>();
        Timing.Note("0.4.0 ready, OFF. Panel button or F4: combined timing/pacing/8x smoothing for 15 seconds; repeat to stop and save. F5 still runs timing alone for 30 seconds. Cardinal and diagonal manual walks supported. Key/button activations are logged. CRT OFF. No hooks before activation; F6 disabled during combined testing.");
    }
    public override bool Unload()
    {
        TestPanel.Stop("unload"); Timing.WaitForSave();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
    private static bool Matches(string path, string expected)
    {
        using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, path));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() {
        try { TestPanel.CheckPanel(); TestPanel.Tick(); Timing.Tick(); TestPanel.Poll(); }
        catch (Exception e) { TestPanel.Fault(e); Timing.FinishPending(); }
    }
    public void LateUpdate() { try { Timing.FinishPending(); TestPanel.Poll(); } catch (Exception e) { TestPanel.Fault(e); } }
    public void OnGUI() { TestPanel.Draw(); }
    public void OnApplicationFocus(bool focused) { if (!focused) try { TestPanel.FocusLost(); } catch (Exception e) { TestPanel.Fault(e); } }
    public void OnApplicationQuit() { try { TestPanel.Stop("quit"); } finally { Timing.WaitForSave(); } }
}

internal static class Timing
{
    public static ManualLogSource? Log;
    private static readonly Harmony Patches = new("local.prstutter.timing");
    private static FieldPlayer? _player;
    private static FieldPlayerKeyController? _controller;
    private static FieldController? _field;
    private static MainGame? _main;
    private static TaskMachine? _machine;
    private static EnumeratorTaskProcess? _arrival;
    private static int _captureFrame = -1, _arrivalFrame = -1, _arrivalCount, _primed;
    private static CameraFollowing? _following;
    private static IntPtr _parent;
    private static bool _active, _hooks, _requesting, _detachFailed;
    private static string? _pendingStop;
    private static int _inputFrame = -1, _lastUpdate = -1, _carried, _attempted;
    private static Vector2 _axis;
    private static Row? _pending;
    private static int _cameraFrame = -1, _footFrame = -1;
    private static bool _footAllowsNext;
    private static long _deadline;
    public static bool Active => _active;
    public static double Remaining => _active ? Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency) : 0;
    public static string LastStop { get; private set; } = "not started";
    public static int Carried => _carried;
    public static bool Saving => TimingCapture.Saving;
    private static double _totalCarry;
    private static string DirectoryPath => Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter/timing");
    public static void Note(string message)
    {
        Log?.LogInfo(message);
        try { Directory.CreateDirectory(DirectoryPath); File.AppendAllText(Path.Combine(DirectoryPath, "timing-test.log"), $"{DateTime.UtcNow:o} {message}{Environment.NewLine}"); }
        catch (Exception e) { Log?.LogWarning("Timing evidence write failed: " + e.Message); }
    }
    public static void Tick()
    {
        FinishPending();
        if (_active && (!Valid() || Stopwatch.GetTimestamp() >= _deadline)) Stop("timeout_or_control_changed");
        if (!TestPanel.SuppressTimingKey && Input.GetKeyDown(KeyCode.F5)) { if (_active) Stop("manual"); else Start(); }
    }
    public static void Start(long deadline = 0)
    {
        if (_hooks) throw new InvalidOperationException("Previous hooks have not been removed.");
        _following = null; _player = null; _controller = null; _field = null; _main = null; _machine = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>()) {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (_following != null) throw new InvalidOperationException("Multiple field follow targets.");
            _following = f;
        }
        _player = _following?.TargetEntity.TryCast<FieldPlayer>();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerKeyController>()) {
            if (_player == null || !c.isActiveAndEnabled || !c.InputEnable || c.fieldPlayer == null || c.fieldPlayer.Pointer != _player.Pointer) continue;
            if (_controller != null) throw new InvalidOperationException("Multiple ordinary key controllers.");
            _controller = c;
        }
        _parent = _player?.transform.parent == null ? IntPtr.Zero : _player.transform.parent.Pointer;
        _field = _controller?.playerHandle?.TryCast<FieldController>();
        _main = _field?.eventHandle?.TryCast<EventProcedure>()?.sceneHandle?.TryCast<MainGame>();
        _machine = _main?.residentMultiTask;
        if (!Valid()) throw new InvalidOperationException("Timing test requires an active ordinary manual-walking controller.");
        _inputFrame = _lastUpdate = -1; _axis = Vector2.zero; _carried = _attempted = 0; _totalCarry = 0; _detachFailed = false;
        _pending = null; _cameraFrame = _footFrame = -1; _footAllowsNext = false;
        _captureFrame = _arrivalFrame = -1; _arrival = null; _arrivalCount = _primed = 0;
        TimingCapture.Begin(); // Optional evidence cannot gate correction startup.
        // No ref Vector3 or camera hooks. Additional hooks observe arrival approval
        // and enter the field update before its native camera/visual work.
        _hooks = true; // Own partial installation so a failed second patch is removed.
        Patches.Patch(AccessTools.DeclaredMethod(typeof(FieldPlayerKeyController), nameof(FieldPlayerKeyController.OnTouchPadCallback)),
            prefix: new HarmonyMethod(typeof(Timing), nameof(InputPrefix)));
        Patches.Patch(AccessTools.DeclaredMethod(typeof(FieldPlayer), nameof(FieldPlayer.UpdateEntity)),
            prefix: new HarmonyMethod(typeof(Timing), nameof(UpdatePrefix)), postfix: new HarmonyMethod(typeof(Timing), nameof(UpdatePostfix)));
        Patches.Patch(AccessTools.DeclaredMethod(typeof(FieldController), nameof(FieldController.OnPlayerFootMonitoringFinished)),
            postfix: new HarmonyMethod(typeof(Timing), nameof(FootFinishedPostfix)));
        Patches.Patch(AccessTools.DeclaredMethod(typeof(FieldController), nameof(FieldController.UpdateController)),
            prefix: new HarmonyMethod(typeof(Timing), nameof(FieldUpdatePrefix)));
        Patches.Patch(AccessTools.DeclaredMethod(typeof(FootMonitoring), nameof(FootMonitoring.UpdateMoveAction)),
            postfix: new HarmonyMethod(typeof(Timing), nameof(ArrivalCreatedPostfix)));
        _deadline = deadline == 0 ? Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30 : deadline;
        _active = true; LastStop = "";
        Note($"TIMING ON ({Remaining:F1}s). One early arrival step with native approval and collision checks; same-frame carry only. Five hooks. Coordinated={PRStutter.GridExperiment.ExperimentControls.Coordinated}.");
    }
    private static bool Valid() => Application.isFocused && Time.timeScale == 1 && _player != null &&
        _player.gameObject.activeInHierarchy && (int)_player.moveState == 0 && !_player.IsAutoMoving && !_player.IsRiging && !_player.pauseMoving &&
        _player.MovementPositionList != null && _player.MovementPositionList.Count == 0 &&
        _player.transform.rotation == Quaternion.identity && _player.transform.lossyScale == Vector3.one &&
        (_player.transform.parent == null ? IntPtr.Zero : _player.transform.parent.Pointer) == _parent &&
        _following != null && _following.TargetEntity != null && _following.TargetEntity.Pointer == _player.Pointer &&
        _following.camera != null && _following.camera.isActiveAndEnabled &&
        _controller != null && _controller.isActiveAndEnabled && _controller.InputEnable &&
        _controller.fieldPlayer != null && _controller.fieldPlayer.Pointer == _player.Pointer &&
        _field != null && _field.player != null && _field.player.Pointer == _player.Pointer &&
        _field.cameraFollowing != null && _field.cameraFollowing.Pointer == _following.Pointer &&
        _controller.playerHandle != null && _controller.playerHandle.Pointer == _field.Pointer &&
        _main != null && _main.isActiveAndEnabled && _machine != null &&
        _main.residentMultiTask != null && _main.residentMultiTask.Pointer == _machine.Pointer &&
        _machine.Type == TaskRunType.DynamicParallel;

    private static Walk Read(FieldPlayer p)
    {
        var s = p.startPos; var d = p.destPos; var pos = p.transform.localPosition;
        return new Walk(s.x, s.y, d.x, d.y, p.moveTimer, p.moveTime, pos.x, pos.y);
    }
    private static void InputPrefix(FieldPlayerKeyController __instance, Vector2 __0)
    {
        if (!_active || _requesting || _controller == null || __instance.Pointer != _controller.Pointer) return;
        try { _axis = __0; _inputFrame = Time.frameCount; } catch (Exception e) { Fail(e); }
    }
    private static void UpdatePrefix(FieldPlayer __instance, out Sample __state)
    {
        __state = default;
        if (!_active || _player == null || __instance.Pointer != _player.Pointer) return;
        try {
            if (!Valid()) { RequestStop("control_changed"); return; }
            int frame = Time.frameCount;
            ExpirePending(frame);
            if (frame == _lastUpdate) { RequestStop("multiple_player_updates"); return; }
            _lastUpdate = frame;
            __state = new Sample(true, frame, Stopwatch.GetTimestamp(), Time.deltaTime, Read(__instance), _axis.x, _axis.y, _inputFrame);
            _arrival = null; _arrivalCount = 0; _arrivalFrame = -1;
            _captureFrame = CarryPolicy.TryRemainder(__state.Before, __state.Delta, out _) &&
                CarryPolicy.SameInput(__state.Before, _axis.x, _axis.y, _inputFrame, frame) ? frame : -1;
        } catch (Exception e) { Fail(e); }
    }
    private static void UpdatePostfix(FieldPlayer __instance, Sample __state)
    {
        if (_player != null && __instance.Pointer == _player.Pointer) _captureFrame = -1;
        if (!_active || !__state.Valid) return;
        long begin = Stopwatch.GetTimestamp();
        try {
            var after = Read(__instance);
            float remainder = 0;
            string action = "ordinary";
            if (!Valid()) { RequestStop("control_changed_after_callbacks"); action = "control_changed"; }
            else if (CarryPolicy.TryRemainder(__state.Before, __state.Delta, out remainder) && CarryPolicy.Completed(__state.Before, after)) {
                action = "no_fresh_same_direction_input";
                if (CarryPolicy.SameInput(__state.Before, __state.AxisX, __state.AxisY, __state.InputFrame, __state.Frame)) {
                    action = "pending_arrival_checks";
                }
            }
            var row = new Row(__state, after, after, remainder, 0, action, Stopwatch.GetTimestamp() - begin,
                _field?.isPlayerFootProcessing ?? true, _footFrame, _footAllowsNext, _cameraFrame);
            if (action == "pending_arrival_checks") _pending = row;
            else CorrectionEvents.Movement.Publish(row);
        } catch (Exception e) { Fail(e); }
    }
    private static void ExpirePending(int frame)
    {
        if (_pending is not { } row || row.Sample.Frame == frame) return;
        _pending = null;
        CorrectionEvents.Movement.Publish(row with { Action = "expired_before_safe_camera_phase", FootFrame = _footFrame, FootAllowsNext = _footAllowsNext, CameraFrame = _cameraFrame });
    }
    private static void FootFinishedPostfix(FieldController __instance, bool __0)
    {
        if (!_active || _field == null || __instance.Pointer != _field.Pointer) return;
        try { _footFrame = Time.frameCount; _footAllowsNext = __0; }
        catch (Exception e) { Fail(e); }
    }
    private static void ArrivalCreatedPostfix(FootMonitoring __instance, FieldEntity __0, ITask __result)
    {
        if (!_active || _captureFrame < 0 || _field?.footMonitoring == null || _player == null) return;
        try {
            if (_captureFrame != Time.frameCount || __instance.Pointer != _field.footMonitoring.Pointer ||
                __0 == null || __0.Pointer != _player.Pointer) return;
            _arrivalCount++;
            _arrivalFrame = Time.frameCount;
            _arrival = __result?.TryCast<EnumeratorTaskProcess>();
        } catch (Exception e) { Fail(e); }
    }
    private static Row PrimeArrival(Row row, int frame)
    {
        // Called after the entire original player update, never from inside its
        // completion callbacks or the task factory. No blanket scheduler replay.
        if (!Valid() || row.Sample.Frame != frame || _cameraFrame == frame ||
            !CarryPolicy.Completed(row.Sample.Before, Read(_player!)) ||
            !CarryPolicy.SameInput(row.Sample.Before, _axis.x, _axis.y, _inputFrame, frame))
            return row with { ArrivalAction = "window_or_input_changed" };
        var task = _arrival;
        if (_arrivalFrame != frame || _arrivalCount != 1 || task == null)
            return row with { ArrivalAction = "no_unique_arrival_task" };
        var iterator = task.handler?.TryCast<FootMonitoring._UpdateMonitor_d__41>();
        if (task.Status != TaskBase.State.Request || iterator == null || iterator.__1__state != 0 ||
            iterator.__4__this == null || iterator.__4__this.Pointer != _field!.footMonitoring.Pointer ||
            iterator.entity == null || iterator.entity.Pointer != _player!.Pointer ||
            iterator.monitoringFinished == null || !_field.isPlayerFootProcessing)
            return row with { ArrivalAction = "arrival_identity_or_state_changed" };
        var queue = _machine!.requestTaskQueue;
        var running = _machine.runningTaskList;
        var trash = _machine.trashTaskList;
        if (queue == null || running == null || trash == null)
            return row with { ArrivalAction = "scheduler_lists_missing" };
        row = row with { RequestCount = queue.Count, RunningCount = running.Count, ArrivalState = iterator.__1__state };
        // Do not overtake another newly queued task or interfere with cleanup.
        if (queue.Count != 1 || queue[0] == null || queue[0].Pointer != task.Pointer || trash.Count != 0)
            return row with { ArrivalAction = "queue_not_exclusive" };
        for (int i = 0; i < running.Count; i++)
            if (running[i] != null && running[i].Pointer == task.Pointer)
                return row with { ArrivalAction = "already_running" };
        long begin = Stopwatch.GetTimestamp();
        ArrivalAdmission.AdmitAndStep(new NativeArrivalAdmission(task, queue, running));
        _primed++;
        return row with { ArrivalAction = task.CheckCompleted() ? "stepped_ended" : "stepped_yielded",
            ArrivalState = iterator.__1__state, Ticks = row.Ticks + Stopwatch.GetTimestamp() - begin };
    }

    private sealed class NativeArrivalAdmission : IArrivalAdmission
    {
        private readonly EnumeratorTaskProcess _task;
        private readonly Il2CppSystem.Collections.Generic.List<ITask> _queue, _running;
        private int _addedIndex;
        public NativeArrivalAdmission(EnumeratorTaskProcess task, Il2CppSystem.Collections.Generic.List<ITask> queue,
            Il2CppSystem.Collections.Generic.List<ITask> running) { _task = task; _queue = queue; _running = running; }
        public void Start() => _task.Start();
        public void AddToRunning() { _addedIndex = _running.Count; _running.Add(_task.Cast<ITask>()); }
        public void RemoveFromQueue() => _queue.RemoveAt(0);
        public void UndoAddToRunning()
        {
            if (_running.Count != _addedIndex + 1 || _running[_addedIndex].Pointer != _task.Pointer)
                throw new InvalidOperationException("Unexpected scheduler change during arrival admission.");
            _running.RemoveAt(_addedIndex);
        }
        public void Step() => _task.Update();
    }
    private static void FieldUpdatePrefix(FieldController __instance)
    {
        if (!_active || _field == null || __instance.Pointer != _field.Pointer) return;
        try {
            int frame = Time.frameCount;
            ExpirePending(frame);
            if (_pending is { } row) {
                _pending = null; // Consume once, independent of any recording buffer.
                row = PrimeArrival(row, frame);
                bool permission = _active && Valid() && _controller!.playerHandle.IsCanPlayerOperation();
                row = row with { FootFrame = _footFrame, FootAllowsNext = _footAllowsNext, CameraFrame = _cameraFrame };
                if (!_active || !Valid() || !CarryPolicy.ReadyBeforeCamera(row.Sample.Frame, frame, _cameraFrame, _footFrame, _footAllowsNext, permission))
                    row = row with { Action = "arrival_checks_not_ready_before_camera" };
                else row = Continue(row);
                CorrectionEvents.Movement.Publish(row);
            }
            _cameraFrame = frame; // The original method now performs camera/visual updates exactly once.
        } catch (Exception e) { Fail(e); }
    }
    private static Row Continue(Row row)
    {
        long begin = Stopwatch.GetTimestamp();
        var sample = row.Sample; var player = _player!;
        if (!CarryPolicy.Completed(sample.Before, Read(player)) ||
            !CarryPolicy.SameInput(sample.Before, _axis.x, _axis.y, _inputFrame, Time.frameCount))
            return row with { Action = "endpoint_or_input_changed_while_waiting" };
        _attempted++;
        _requesting = true;
        try { _controller!.OnTouchPadCallback(_axis); }
        finally { _requesting = false; }
        var next = Read(player);
        row = row with { Final = next, Action = "blocked_or_changed_next_tile" };
        if (Valid() && CarryPolicy.ApprovedNext(sample.Before, next)) {
            var position = player.transform.localPosition; var advanced = position;
            advanced.x = CarryPolicy.Rounded(next.Sx, next.Dx, row.Remainder, next.Duration);
            advanced.y = CarryPolicy.Rounded(next.Sy, next.Dy, row.Remainder, next.Duration);
            try {
                player.moveTimer = row.Remainder; player.transform.localPosition = advanced;
                if (player.moveTimer != row.Remainder || player.transform.localPosition != advanced)
                    throw new InvalidOperationException("Carry readback mismatch.");
            } catch {
                if (player.moveTimer == row.Remainder) player.moveTimer = next.Timer;
                if (player.transform.localPosition == advanced) player.transform.localPosition = position;
                throw;
            }
            _carried++; _totalCarry += row.Remainder;
            row = row with { Final = Read(player), Applied = row.Remainder, Action = "carried" };
        }
        return row with { Ticks = row.Ticks + Stopwatch.GetTimestamp() - begin };
    }
    private static void RequestStop(string reason) { _active = false; LastStop = reason; _pendingStop ??= reason; }
    public static void Fail(Exception e) { RequestStop("fault: " + e); }
    public static void FinishPending() { if (_pendingStop is { } reason) Stop(reason); }
    public static void Stop(string reason)
    {
        bool hadSession = _active || _hooks;
        if (_active || hadSession) LastStop = reason;
        _active = false; _pendingStop = null;
        // Called outside native hook execution. Removing our hooks restores the stock update path.
        if (_hooks) {
            try { Patches.UnpatchSelf(); _hooks = false; }
            catch (Exception e) {
                _pendingStop = reason;
                if (!_detachFailed) { Note("TIMING DETACH FAILED; intervention disabled, will retry: " + e); _detachFailed = true; }
                return;
            }
        }
        if (_pending is { } pending) CorrectionEvents.Movement.Publish(pending with { Action = "cancelled_on_stop" });
        _pending = null;
        _captureFrame = _arrivalFrame = -1; _arrival = null; _main = null; _machine = null;
        _player = null; _controller = null; _following = null; _field = null; _inputFrame = -1; _requesting = false;
        TimingCapture.End(reason);
        if (hadSession) Note($"TIMING OFF ({reason}); arrivalTasksStepped={_primed}, continuationAttempts={_attempted}, carriedTiles={_carried}, carriedMs={_totalCarry * 1000:F4}. Hooks removed. Committed movement remains; game owns admitted tasks until normal completion.");
    }
    public static void WaitForSave() => TimingCapture.Wait();
}
