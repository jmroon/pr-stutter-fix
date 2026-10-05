using System;
using System.Collections.Generic;
using Last.Map;
using PRStutter.GridExperiment;
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Texture ownership only: no movement reconstruction, transform writes, shader
// float changes, camera projection changes or frame pacing changes belong here.
internal sealed class ResolutionSession
{
    private readonly List<Target> _targets = new();
    private readonly List<CameraBinding> _cameras = new();
    private readonly List<TextureBinding> _textures = new();
    private readonly List<ResolutionPass> _passes = new();
    private readonly ResolutionFrame _frame;
    private readonly FieldController _field;
    private readonly CameraFollowing _following;
    private readonly Material _compositor;
    private readonly Camera _front;
    private readonly Camera _logicalCamera;
    private readonly IntPtr _model, _renderer;
    private readonly int _mapWidth, _mapHeight, _width = Screen.width, _height = Screen.height;
    private readonly float _mainBias, _overlayBias;
    private ResolutionPass? _final;
    public int Frames => _frame.CompletedFrames;
    public const int Scale = 8;
    public string Summary => $"8x (2560x1440), completedFrames={Frames}, targets={_targets.Count}, materialBindings={_textures.Count}";
    private static bool Same(Texture? a, Texture? b) => a == null ? b == null : b != null && a.Pointer == b.Pointer;

    public ResolutionSession()
    {
        if (SystemInfo.maxTextureSize < 2560) throw new InvalidOperationException("8x exceeds GPU texture-size limit");
        CameraFollowing? follow = null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<CameraFollowing>()) {
            if (f.TargetEntity == null || f.camera == null || !f.camera.isActiveAndEnabled) continue;
            if (follow != null) throw new InvalidOperationException("Multiple active follow cameras");
            follow = f;
        }
        _following = follow ?? throw new InvalidOperationException("No active field camera");
        FieldController? field = null;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerKeyController>()) {
            var candidate = c.playerHandle?.TryCast<FieldController>();
            if (candidate?.cameraFollowing?.Pointer != _following.Pointer) continue;
            if (field != null && field.Pointer != candidate.Pointer) throw new InvalidOperationException("Multiple field controllers");
            field = candidate;
        }
        _field = field ?? throw new InvalidOperationException("No matching field context; start from an ordinary field area");
        var model = _field.mapManager?.currentMapModel;
        var renderer = _field.mainViewMapRenderer;
        if (model == null || renderer == null) throw new InvalidOperationException("Field map is unavailable");
        _model = model.Pointer; _renderer = renderer.Pointer; _mapWidth = renderer.mapWidth; _mapHeight = renderer.mapHeight;
        _compositor = PostProcessLite.GetMaterial();
        if (_compositor == null || _compositor.shader.name != "Last/PostProcessLite") throw new InvalidOperationException("Unsupported compositor");
        _mainBias = _compositor.GetFloat("_MainGameDiffuseBias"); _overlayBias = _compositor.GetFloat("_OverlayDiffuseBias");
        if (!float.IsFinite(_mainBias) || !float.IsFinite(_overlayBias)) throw new InvalidOperationException("Invalid compositor bias");
        Camera? front = null; var names = new HashSet<string>();
        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
        foreach (var c in cameras) {
            if (!c.isActiveAndEnabled) continue;
            if (c.name == "CameraFrontFilter") {
                if (front != null) throw new InvalidOperationException("Multiple compositors");
                front = c;
            }
            if (c.name is not ("CameraFieldMain" or "CameraTileMap" or "CameraUpperTransparentRT" or "CameraCeilTransparentRT")) continue;
            if (!names.Add(c.name) || c.targetTexture == null || !c.orthographic || Math.Abs(c.orthographicSize-90)>.001f || c.rect != new Rect(0,0,1,1))
                throw new InvalidOperationException("Unsupported field camera: " + c.name);
            var texture = c.targetTexture;
            if (texture.width != 320 || texture.height != 180 || texture.antiAliasing != 1 || !texture.IsCreated())
                throw new InvalidOperationException("Expected stock 320x180 single-sample target");
            var target = _targets.Find(t => Same(t.Original, texture));
            if (target == null) { target = new Target(texture); _targets.Add(target); }
            _cameras.Add(new CameraBinding(c,target));
        }
        _front = front ?? throw new InvalidOperationException("No compositor camera");
        if (!FieldLayoutPolicy.Accepts(Experiment.Game,names,_targets.Count) || _front.targetTexture != null || _front.depth != 99 || _front.rect != new Rect(0,0,1,1))
            throw new InvalidOperationException("Unsupported field target layout");
        var main = _cameras.Find(c => c.Camera.name == "CameraFieldMain")!;
        _logicalCamera = main.Camera;
        var tile = _cameras.Find(c => c.Camera.name == "CameraTileMap")!;
        if (main.Camera.Pointer != _following.camera.Pointer || main.Camera.cullingMask != 0 ||
            !Same(main.Target.Original,tile.Target.Original) || !Same(main.Target.Original,_compositor.GetTexture("_MainGameTex")))
            throw new InvalidOperationException("Field/compositor target mismatch");
        foreach (var c in cameras) {
            if (c.isActiveAndEnabled && _targets.Exists(t => Same(t.Original,c.targetTexture)) && !_cameras.Exists(b => b.Camera.Pointer == c.Pointer))
                throw new InvalidOperationException("Additional field texture producer: " + c.name);
        }
        foreach (var material in Resources.FindObjectsOfTypeAll<Material>()) {
            foreach (string property in material.GetTexturePropertyNames()) {
                var target = _targets.Find(t => Same(t.Original,material.GetTexture(property)));
                if (target != null) _textures.Add(new TextureBinding(material,property,target));
            }
        }
        if (!_textures.Exists(t => t.Material.Pointer == _compositor.Pointer && t.Property == "_MainGameTex"))
            throw new InvalidOperationException("Compositor consumer was not discovered");
        _frame = new ResolutionFrame(_cameras.Count);
        Validate();
    }
    // Called only after the session is retained by its owner so partial
    // allocation/attachment can always be cleaned up.
    public void Initialize()
    {
        foreach (var t in _targets) {
            t.High = new RenderTexture(t.Original);
            t.High.name = "PRStutter.ResolutionOnly." + t.Original.name;
            t.High.width = 2560; t.High.height = 1440;
            t.High.filterMode = t.Original.filterMode; t.High.wrapMode = t.Original.wrapMode;
            if (!t.High.Create()) throw new InvalidOperationException("High-resolution target allocation failed");
        }
        foreach (var c in _cameras) _passes.Add(c.Camera.gameObject.AddComponent<ResolutionPass>());
        _final = _front.gameObject.AddComponent<ResolutionPass>(); _passes.Add(_final);
    }
    private void Validate()
    {
        var map = _field.mainViewMapRenderer;
        if (!Application.isFocused || _following == null || _following.TargetEntity == null ||
            _following.camera == null || _following.camera.Pointer != _logicalCamera.Pointer ||
            _field.cameraFollowing?.Pointer != _following.Pointer || (int)_field.MapViewType != 0 ||
            _field.mapManager?.currentMapModel?.Pointer != _model || map == null || !map.isActiveAndEnabled ||
            map.Pointer != _renderer || map.mapWidth != _mapWidth || map.mapHeight != _mapHeight)
            throw new InvalidOperationException("Scene or field view changed; resolution test stopped");
        if (_front == null || !_front.isActiveAndEnabled || _front.targetTexture != null || _front.depth != 99 || _front.rect != new Rect(0,0,1,1) ||
            Screen.width != _width || Screen.height != _height || _compositor == null || _compositor.shader.name != "Last/PostProcessLite")
            throw new InvalidOperationException("Display/compositor changed");
        if (_compositor.GetFloat("_FakeCRT") != 0) throw new InvalidOperationException("Turn CRT OFF before the 8x test");
        if (_compositor.GetFloat("_BlurMainGame") != 0 || _compositor.GetFloat("_PartialFadeOverlay") != 0 ||
            _compositor.GetFloat("_MainGameDiffuseBias") != _mainBias || _compositor.GetFloat("_OverlayDiffuseBias") != _overlayBias)
            throw new InvalidOperationException("Compositor effect/transition changed");
        foreach (var c in _cameras) {
            if (c.Camera == null || !c.Camera.isActiveAndEnabled || !c.Camera.orthographic || c.Camera.orthographicSize != c.Size ||
                c.Camera.rect != new Rect(0,0,1,1) || c.Camera.transform.rotation != c.Rotation || c.Camera.transform.lossyScale != c.LossyScale ||
                c.Camera.depth != c.Depth || c.Camera.depth >= _front.depth || c.Camera.cullingMask != c.Mask || c.Camera.clearFlags != c.Clear)
                throw new InvalidOperationException("Field camera topology changed");
        }
    }
    public bool Owns(ResolutionPass pass) => _passes.Exists(p => p != null && p.Pointer == pass.Pointer);
    private bool Final(ResolutionPass pass) => _final != null && _final.Pointer == pass.Pointer;
    public void Prepare()
    {
        Restore(); Validate();
        foreach (var c in _cameras) if (!Same(c.Camera.targetTexture,c.Target.Original)) throw new InvalidOperationException("Native camera target changed");
        foreach (var b in _textures) if (b.Material == null || !Same(b.Material.GetTexture(b.Property),b.Target.Original)) throw new InvalidOperationException("Texture consumer changed");
        foreach (var t in _targets) if (t.High == null || !t.High.IsCreated()) throw new InvalidOperationException("Owned target lost");
        foreach (var c in _cameras) c.Override.Apply(c.Target.High!);
        foreach (var b in _textures) b.Override.Apply(b.Target.High!);
        _frame.Prepared(Time.frameCount);
    }
    public void Before(ResolutionPass pass)
    {
        if (!_frame.Ready(Time.frameCount)) return;
        Validate();
        foreach (var c in _cameras) if (!Same(c.Camera.targetTexture,c.Target.High)) throw new InvalidOperationException("Target overwritten before draw");
        foreach (var b in _textures) if (b.Material == null || !Same(b.Material.GetTexture(b.Property),b.Target.High)) throw new InvalidOperationException("Consumer overwritten before draw");
        _frame.Before(Time.frameCount,pass.Pointer.ToInt64(),Final(pass));
    }
    public void After(ResolutionPass pass)
    {
        if (_frame.After(Time.frameCount,pass.Pointer.ToInt64(),Final(pass))) Restore();
    }
    public void Restore()
    {
        Exception? failure = null;
        foreach (var b in _textures) try { b.Override.Restore(); } catch (Exception e) { failure ??= e; }
        foreach (var c in _cameras) try { c.Override.Restore(); } catch (Exception e) { failure ??= e; }
        _frame.Restored();
        if (failure != null) throw failure;
    }
    public void Dispose()
    {
        Restore(); // Retain resources if any owned reference could not be restored.
        foreach (var pass in _passes) if (pass != null) UnityEngine.Object.Destroy(pass);
        _passes.Clear(); _final = null;
        foreach (var t in _targets) if (t.High != null) { t.High.Release(); UnityEngine.Object.Destroy(t.High); t.High = null; }
    }
    private sealed class Target
    {
        public readonly RenderTexture Original;
        public RenderTexture? High;
        public Target(RenderTexture original) { Original = original; }
    }
    private sealed class CameraBinding
    {
        public readonly Camera Camera; public readonly Target Target;
        public readonly Quaternion Rotation; public readonly Vector3 LossyScale;
        public readonly float Size, Depth; public readonly int Mask; public readonly CameraClearFlags Clear;
        public readonly ScopedOverride<RenderTexture> Override;
        public CameraBinding(Camera camera, Target target)
        {
            Camera = camera; Target = target; Rotation = camera.transform.rotation; LossyScale = camera.transform.lossyScale;
            Size = camera.orthographicSize; Depth = camera.depth; Mask = camera.cullingMask; Clear = camera.clearFlags;
            Override = new ScopedOverride<RenderTexture>(() => camera == null ? target.Original : camera.targetTexture,
                value => { if (camera != null) camera.targetTexture = value; }, Same);
        }
    }
    private sealed class TextureBinding
    {
        public readonly Material Material; public readonly string Property; public readonly Target Target;
        public readonly ScopedOverride<Texture> Override;
        public TextureBinding(Material material, string property, Target target)
        {
            Material = material; Property = property; Target = target;
            Override = new ScopedOverride<Texture>(() => material == null ? target.Original : material.GetTexture(property),
                value => { if (material != null) material.SetTexture(property,value); }, Same);
        }
    }
}

internal static class Resolution
{
    private static ResolutionSession? _session;
    private static bool _active, _stopping;
    public static bool Faulted { get; private set; }
    public static int RequestedScale => _active ? 8 : 1;
    public static int CompletedFrames => _session?.Frames ?? 0;
    public static string Note { get; private set; } = "stock 320x180";
    public static string Label => $"FIELD RESOLUTION {(_active ? "8x 2560x1440" : "STOCK")} | Alt+F11 toggle | {(_active ? $"drawn frames {CompletedFrames}" : Note)}";
    public static void Toggle()
    {
        if (_active) { Stop("manual"); return; }
        if (Faulted) return;
        if (!ReleaseStopped()) return;
        try {
            _session = new ResolutionSession(); _session.Initialize(); _active = true;
            Note = "8x requested"; Experiment.Log?.LogInfo("RESOLUTION ON: " + _session.Summary + "; no pose or pacing changes");
        } catch (Exception e) { Stop(e.Message); }
    }
    // Update is outside camera rendering. Restore immediately on a callback
    // failure, but defer releasing textures/passes until this safe boundary.
    public static void Recover()
    {
        if (_active) try { _session?.Restore(); } catch (Exception e) { Stop(e.Message); }
        if (!_active && !Faulted) ReleaseStopped(); // A cleanup failure is latched, not retried/logged every frame.
    }
    public static bool ReleaseStopped()
    {
        if (_active) return false;
        try { _session?.Dispose(); _session = null; return true; }
        catch (Exception e) { Faulted = true; Note = "CLEANUP FAILED; restart game"; Experiment.Log?.LogError(e); return false; }
    }
    public static void Prepare() { if (_active) try { _session!.Prepare(); } catch (Exception e) { Stop(e.Message); } }
    public static void Before(ResolutionPass pass) { if (_active && _session!.Owns(pass)) try { _session.Before(pass); } catch (Exception e) { Stop(e.Message); } }
    public static void After(ResolutionPass pass) { if (_active && _session!.Owns(pass)) try { _session.After(pass); } catch (Exception e) { Stop(e.Message); } }
    public static void Disabled(ResolutionPass pass) { if (_active && _session!.Owns(pass)) Stop("camera disabled"); }
    public static bool Stop(string reason)
    {
        if (_stopping) return false;
        _stopping = true;
        try {
            bool hadSession = _session != null; _active = false;
            string summary = _session?.Summary ?? "not active";
            _session?.Restore(); Note = reason;
            if (hadSession || reason != "unrounded stop") Experiment.Log?.LogInfo("RESOLUTION OFF: " + reason + "; " + summary);
            return true;
        } catch (Exception e) { Faulted = true; Note = "RESTORE FAILED; restart game"; Experiment.Log?.LogError(e); return false; }
        finally { _stopping = false; }
    }
}

public sealed class ResolutionPass : MonoBehaviour
{
    public ResolutionPass(IntPtr pointer) : base(pointer) { }
    public void OnPreCull() => Resolution.Before(this);
    public void OnPostRender() => Resolution.After(this);
    public void OnDisable() => Resolution.Disabled(this);
}
