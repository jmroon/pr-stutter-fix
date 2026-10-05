using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using Last.Entity.Field;
using Last.Map;
using PRStutter.PlaythroughDiagnostics;
using UnityEngine;

namespace PRStutter.PresentationAudit;

internal static class Observer
{
    private static readonly AuditWindow Window = new(Stopwatch.Frequency);
    private static readonly CaptureWriter Writer = new(Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter/presentation-audit"));
    private static RecentBuffer<object>? _rows;
    private static long _started, _ticks, _maxTicks;
    private static int _samples, _cursor;
    private static string _status = "Presentation audit OFF | Ctrl+F11: record 60s | corrections must be OFF";
    private static PropertyInfo? _runtimeCurrent;
    private static PropertyInfo[]? _runtimeFlags;
    private static bool _runtimePresent;
    private static bool _unroundedPresent;
    private static PropertyInfo? _unroundedState;
    private static PropertyInfo? _renderScale, _resolutionFrames;
    private static string _baseline = "waiting for field";
    public static bool Expired => Window.Expired(Stopwatch.GetTimestamp());
    public static string Status => Window.Active ? $"Audit RECORDING {Math.Min(60, (Stopwatch.GetTimestamp() - _started) / Stopwatch.Frequency)} / 60s | {_samples} samples | {_baseline} | Ctrl+F11 stop" : _status + " | " + Writer.Status;
    public static void Toggle()
    {
        if (Window.Active) { Stop("manual"); return; }
        if (Writer.Busy) { _status = "Audit save in progress; wait before restarting"; return; }
        _rows = new RecentBuffer<object>(1200); _samples = _cursor = 0;
        _baseline = "waiting for field";
        _ticks = _maxTicks = 0; _started = Stopwatch.GetTimestamp();
        _runtimeCurrent = null; _runtimeFlags = null; _runtimePresent = false;
        _unroundedPresent = false; _unroundedState = null; _renderScale = _resolutionFrames = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.GetName().Name == "PRStutter.UnroundedExperiment") {
                _unroundedPresent = true;
                _unroundedState = assembly.GetType("PRStutter.UnroundedExperiment.ExperimentStatus")?.GetProperty("State", BindingFlags.Static | BindingFlags.Public);
                var status = _unroundedState?.DeclaringType;
                _renderScale = status?.GetProperty("RequestedRenderScale", BindingFlags.Static | BindingFlags.Public);
                _resolutionFrames = status?.GetProperty("ResolutionCompletedFrames", BindingFlags.Static | BindingFlags.Public);
            }
            if (assembly.GetName().Name != "PRStutter.TimingExperiment") continue;
            _runtimePresent = true;
            var type = assembly.GetType("PRStutter.TimingExperiment.CorrectionStatus");
            _runtimeCurrent = type?.GetProperty("Current", BindingFlags.Static | BindingFlags.Public);
            var snapshot = _runtimeCurrent?.PropertyType;
            if (snapshot != null) {
                var flags = new List<PropertyInfo>();
                foreach (var name in new[] { "Enabled", "Timing", "Pacing", "Smoothing" }) {
                    var property = snapshot.GetProperty(name);
                    if (property?.PropertyType == typeof(bool)) flags.Add(property);
                }
                if (flags.Count == 4) _runtimeFlags = flags.ToArray();
            }
        }
        Window.Start(_started);
    }
    private static string Baseline()
    {
        if (_runtimePresent) {
            if (_runtimeCurrent == null || _runtimeFlags == null) return "runtime-status-unknown";
            var snapshot = _runtimeCurrent.GetValue(null);
            if (snapshot == null) return "runtime-status-unknown";
            foreach (var property in _runtimeFlags) if ((bool)property.GetValue(snapshot)!) return "corrections-enabled";
        }
        if (_unroundedPresent) {
            string? state = _unroundedState?.GetValue(null) as string;
            if (state == "on") {
                int scale = _renderScale == null ? 1 : (int)_renderScale.GetValue(null)!;
                return scale == 1 ? "unrounded-movement" : scale == 8 ? "unrounded-movement-8x" : "unrounded-status-unknown-or-fault";
            }
            if (state != "off") return "unrounded-status-unknown-or-fault";
        }
        return _runtimePresent ? "corrections-disabled" : "runtime-absent";
    }
    private static Point P(Vector3 v) => new(v.x, v.y, v.z);
    private static Point P(Vector2 v) => new(v.x, v.y);
    private static bool Aligned(Transform t) => t.rotation == Quaternion.identity && t.lossyScale == Vector3.one;

    // The only Harmony callback: void postfix, no writable arguments/return value.
    public static void AfterVisuals(FieldController __instance)
    {
        if (!Window.Active) return;
        long begin = Stopwatch.GetTimestamp();
        if (!Window.Take(begin)) return;
        try { Sample(__instance, begin); }
        catch (Exception e) { Fault(e); }
        finally { long cost = Stopwatch.GetTimestamp() - begin; _ticks += cost; _maxTicks = Math.Max(_maxTicks, cost); }
    }
    private static void Sample(FieldController field, long qpc)
    {
        var follow = field.cameraFollowing;
        var map = field.mainViewMapRenderer;
        var model = field.mapManager?.currentMapModel;
        var target = follow?.TargetEntity;
        var camera = follow?.camera;
        string baseline = Baseline();
        _baseline = baseline;
        string scope = baseline is "runtime-absent" or "corrections-disabled" or "unrounded-movement" or "unrounded-movement-8x" ? "" : baseline;
        if (follow == null || map == null || model == null || target == null || camera == null || field.player == null) {
            _rows!.Add(qpc, new { Qpc = qpc, Frame = Time.frameCount, Area = field.currentAreaId, Baseline = baseline, Status = "missing-field-input" });
            _samples++; return;
        }
        var targetWorld = P(target.transform.position);
        var cameraWorld = P(camera.transform.position);
        var scroll = P(field.mapScrollOffset); var offset = P(follow.offsetPosition);
        float aspect = MapUtility.GetAspect(), size = camera.orthographicSize;
        if (scope.Length == 0 && (!map.isActiveAndEnabled || map.mapWidth <= 0 || map.mapHeight <= 0 ||
            map.viewSize.x <= 0 || map.viewSize.y <= 0 || size <= 0 || aspect <= 0)) scope = "unready-map";
        if (scope.Length == 0 && (!camera.orthographic || !Aligned(camera.transform) || (int)field.MapViewType != 0)) scope = "special-view";
        var predictedCamera = AuditModel.Camera(targetWorld, scroll, offset, follow.mapSizeWidth, follow.mapSizeHeight,
            Math.Min(aspect, 2.370370388f) * size * 2, size * 2, follow.isLoopHorizontal, follow.isLoopVertical, follow.isClamp);
        var predictedMap = AuditModel.Camera(targetWorld, scroll, offset, map.mapWidth * 16, map.mapHeight * 16,
            map.viewSize.x, map.viewSize.y, map.isLoopHorizontal, map.isLoopVertical, follow.isClamp);
        bool loopX = false, loopY = false; model.GetIsLoop(ref loopX, ref loopY);
#if PR_FFIV
        var tileData = model.assetData.GetTileMapData();
        float width = tileData.width * 16, height = tileData.height * 16;
        float clampTileSubY = 0;
#else
        var range = model.assetData.mapRange;
        float width = range.width * 16, height = range.height * 16;
        float clampTileSubY = follow.ClampTileSubY;
#endif
        if (scope.Length == 0 && (width <= 0 || height <= 0)) scope = "unready-map";
        var playerWorld = P(field.player.transform.position);
        var selected = new List<FieldEntity>(8);
        void Add(FieldEntity? entity) {
            if (entity == null || !entity.gameObject.activeInHierarchy || selected.Count >= 8) return;
            foreach (var other in selected) if (other.Pointer == entity.Pointer) return;
            selected.Add(entity);
        }
        Add(field.player); Add(target);
        var list = field.locationUpdateTargetEntityList;
        int count = list?.Count ?? 0;
        // Rotate through the native list: no global scene query or unbounded walk.
        int examined = 0;
        while (examined < Math.Min(count, 16) && selected.Count < 8) {
            Add(list![(_cursor + examined) % count]); examined++;
        }
        if (count != 0) _cursor = (_cursor + examined) % count;
        var entities = new List<object>(selected.Count);
        foreach (var entity in selected) {
            var root = entity.visualParent;
            var world = P(entity.transform.position);
            Point scale = new(1, 1);
            string entityScope = scope;
#if !PR_FFIV
            if (model.ScrollScaleList != null && model.ScrollScaleList.ContainsKey(entity.gameObject.layer)) scale = P(model.ScrollScaleList[entity.gameObject.layer]);
            if (entity.Property != null && entity.Property.IsScreen && entityScope.Length == 0) entityScope = "screen-space-entity";
#endif
            if (root == null && entityScope.Length == 0) entityScope = "no-visual-root";
            var visual = root == null ? new Point() : P(root.position);
            var predictedVisual = AuditModel.Visual(world, cameraWorld, playerWorld, width, height,
                aspect * size + 32, size + 32, loopX, loopY, scale);
            var shadow = entity.shadowEntity?.ShadowVisualInstance;
            string role = entity.Pointer == field.player.Pointer ? "player" : entity.TryCast<FieldScrollDummyEntity>() != null ? "scroll-dummy" : entity.TryCast<FieldNonPlayer>() != null ? "npc" : "other";
            entities.Add(new {
                Id = entity.GetInstanceID(), Role = role, FollowTarget = entity.Pointer == target.Pointer,
                LogicalWorld = world, LogicalLocal = P(entity.transform.localPosition), Aligned = Aligned(entity.transform),
                Start = P(entity.startPos), Destination = P(entity.destPos), Timer = entity.moveTimer, Duration = entity.moveTime,
                VisualId = root?.GetInstanceID() ?? 0, VisualWorld = visual, VisualLocal = root == null ? new Point() : P(root.localPosition),
                VisualParentId = root?.parent?.GetInstanceID() ?? 0,
                ShadowWorld = shadow == null ? (Point?)null : P(shadow.transform.position),
                Scale = scale, VisualCheck = AuditModel.Compare(predictedVisual, visual, entityScope)
            });
        }
        _rows!.Add(qpc, new {
            Qpc = qpc, Frame = Time.frameCount, Delta = Time.deltaTime, Controller = field.Pointer.ToInt64(),
            RequestedRenderScale = _renderScale?.GetValue(null) as int? ?? 1,
            ResolutionCompletedFrames = _resolutionFrames?.GetValue(null) as int? ?? 0,
            Area = field.currentAreaId, View = (int)field.MapViewType, Baseline = baseline, Status = "sample",
            TargetId = target.GetInstanceID(), TargetWorld = targetWorld, PlayerWorld = playerWorld,
            CameraId = camera.GetInstanceID(), CameraWorld = cameraWorld, CameraInternal = P(follow.position),
            CameraRotation = P(camera.transform.eulerAngles), CameraScale = P(camera.transform.lossyScale),
            Scroll = scroll, Offset = offset, Aspect = aspect, Size = size, ClampTileSubY = clampTileSubY,
            FollowWidth = follow.mapSizeWidth, FollowHeight = follow.mapSizeHeight, FollowLoopX = follow.isLoopHorizontal,
            FollowLoopY = follow.isLoopVertical, Clamp = follow.isClamp,
            MapWidth = map.mapWidth, MapHeight = map.mapHeight, MapView = P(map.viewSize),
            MapLoopX = map.isLoopHorizontal, MapLoopY = map.isLoopVertical, MapCached = P(map.preCameraPosition),
            MapRoot = map.mainBGLayer == null ? (Point?)null : P(map.mainBGLayer.transform.localPosition),
            VisualWidth = width, VisualHeight = height, VisualLoopX = loopX, VisualLoopY = loopY,
            CameraCheck = AuditModel.Compare(predictedCamera, cameraWorld, scope),
            MapCheck = AuditModel.Compare(predictedMap, P(map.preCameraPosition), scope),
            AvailableVisualEntities = count, ExaminedEntities = examined, Entities = entities.ToArray()
        });
        _samples++;
    }
    public static void Stop(string reason)
    {
        if (!Window.Active) return;
        Window.Stop();
        var rows = _rows!.Snapshot(0); long overwritten = _rows.Overwritten; _rows = null;
        Writer.TrySave(new {
            SchemaVersion = 1, Kind = "presentation-adapter-audit", Game = Plugin.Game, Version = "0.1.2",
            QpcFrequency = Stopwatch.Frequency, StartedQpc = _started, SavedQpc = Stopwatch.GetTimestamp(), Reason = reason,
            Phase = "FieldController.UpdateVisualInstancePosition.postfix", Samples = rows,
            Overhead = new { SampleCount = _samples, TotalTicks = _ticks, MaxTicks = _maxTicks, Overwritten = overwritten },
            Limitation = "20 Hz spatial evidence; not every frame, final rendering, shadow/material validation or display cadence. Matches do not establish correction safety. No timing or rendering correction is applied."
        });
        _status = $"Presentation audit stopped ({reason}); {_samples} samples | Ctrl+F11 record";
    }
    public static void Fault(Exception e) { try { Stop("fault: " + e.GetType().Name + ": " + e.Message); } catch { Window.Stop(); _rows = null; } }
    public static void Wait() => Writer.Wait();
}
