using System;
using HarmonyLib;
using Last.Entity.Field;
using Last.Management;
using Last.Map;
using PRStutter.GridExperiment;
using UnityEngine;

namespace PRStutter.TimingExperiment;

// Control-plane observation, not a diagnostic dependency. One void postfix
// records the field that actually updated; no scene scan or gameplay mutation.
internal static class FieldContext
{
    private static readonly Harmony Hooks = new("local.prstutter.fieldcontext");
    private static FieldController? _field;
    private static FieldPlayerKeyController? _manualController;
    private static int _frame = -10;
    private static bool _multiple, _installed;
    public static int Frame => _frame;
    public static void Clear() { _field = null; _manualController = null; _frame = -10; _multiple = false; }
    public static FieldPlayerKeyController? AcquireManualController() => Read().Manual ? _manualController : null;
    public static bool SceneIsPlayer() => Last.Management.SceneManager.Instance?.GetCurrentSubSceneManager()
        ?.TryCast<SubSceneManagerMainGame>()?.GetCurrentState() == SubSceneManagerMainGame.State.Player;
    public static void Attach()
    {
        if (_installed) throw new InvalidOperationException("Field context hook already owned");
        _installed = true;
        Hooks.Patch(AccessTools.DeclaredMethod(typeof(FieldController), nameof(FieldController.UpdateController)),
            postfix: new HarmonyMethod(typeof(FieldContext), nameof(AfterField)));
    }
    public static void Detach()
    {
        if (_installed) { Hooks.UnpatchSelf(); _installed = false; }
        Clear();
    }
    public static void AfterField(FieldController __instance)
    {
        int frame = Time.frameCount;
        if (_frame == frame && _field?.Pointer != __instance.Pointer) _multiple = true;
        if (_frame != frame) _multiple = false;
        _field = __instance; _frame = frame;
    }
    public static FieldContextSnapshot Read()
    {
        _manualController = null;
        FieldContextSnapshot No(string kind, string reason) => new(kind, kind, kind, false, false, false, reason);
        if (!Application.isFocused) return No("unfocused", "application unfocused");
        if (Time.timeScale != 1) return No("paused", "time scale changed");
        var manager = Last.Management.SceneManager.Instance?.GetCurrentSubSceneManager()?.TryCast<SubSceneManagerMainGame>();
        if (manager == null) return No("non-field-scene", "no main-game scene manager");
        string state = manager.GetCurrentState().ToString();
        if (!SceneEligibility.FieldState(state)) return No(state, "unsupported scene state: " + state);
        if (_field == null || Time.frameCount - _frame is < 0 or > 1 || _multiple)
            return No(state, _multiple ? "multiple field updates" : "waiting for fresh field update");
        var field = _field;
        var main = field.eventHandle?.TryCast<EventProcedure>()?.sceneHandle?.TryCast<MainGame>();
        var model = field.mapManager?.currentMapModel;
        var renderer = field.mainViewMapRenderer;
        var follow = field.cameraFollowing;
        var camera = follow?.camera;
        var player = field.player;
        if (main == null || !main.isActiveAndEnabled || model == null || renderer == null || !renderer.isActiveAndEnabled)
            return No(state, "field resources not ready");
        if ((int)field.MapViewType != 0) return No("alternate-field-view", "unsupported field view " + (int)field.MapViewType);
        if (player != null && ((int)player.moveState != 0 || GameProfile.TransportActive(player)))
            return No("transport", "transport motion remains native");
        string identity = $"{field.Pointer}:{model.Pointer}:{renderer.Pointer}:{field.currentAreaId}:{main.Pointer}";
        bool pacing = camera != null && camera.isActiveAndEnabled;
        FieldPlayerKeyController? controller = null;
        var controllers = field.FieldPlayerControllerList;
        bool multipleControllers = false;
        if (controllers != null && controllers.Length <= 8)
            foreach (var candidate in controllers) {
                var c = candidate?.TryCast<FieldPlayerKeyController>();
                if (c == null || !c.isActiveAndEnabled || !c.InputEnable || c.fieldPlayer?.Pointer != player?.Pointer ||
                    c.playerHandle?.Pointer != field.Pointer) continue;
                if (controller != null) multipleControllers = true;
                controller = c;
            }
        bool manual = state == "Player" && !multipleControllers && controller != null && player != null &&
            player.gameObject.activeInHierarchy && !player.IsAutoMoving && !player.pauseMoving && GameProfile.PathQueueEmpty(player) &&
            player.transform.rotation == Quaternion.identity && player.transform.lossyScale == Vector3.one &&
            follow?.TargetEntity?.Pointer == player.Pointer && pacing && main.residentMultiTask != null &&
            main.residentMultiTask.Type == TaskRunType.DynamicParallel;
        string timingIdentity = $"{identity}:{player?.Pointer}:{controller?.Pointer}:{follow?.Pointer}:{main.residentMultiTask?.Pointer}";
        string kind = manual ? "field-manual" : state == "Player" ? "field-scripted-control" : "field-" + state.ToLowerInvariant();
        if (manual) _manualController = controller;
        return new(identity, timingIdentity, kind, true, pacing, manual,
            manual ? "manual field control" : "scripted field control; manual timing suspended",
            field.Pointer.ToInt64(), model.Pointer.ToInt64(), field.currentAreaId);
    }
}
