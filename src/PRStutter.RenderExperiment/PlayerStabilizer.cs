using System;
using System.Collections.Generic;
using Last.Entity.Field;
using Last.Map;
using UnityEngine;

namespace PRStutter.RenderExperiment;

public sealed class FieldPass : MonoBehaviour
{
    public FieldPass(IntPtr pointer) : base(pointer) { }
    public void OnPreCull() { try { Experiment.FieldPreCull(this); } catch (Exception e) { Experiment.Fault(e); } }
    public void OnPostRender() { try { Experiment.FieldPostRender(this); } catch (Exception e) { Experiment.Fault(e); } }
    public void OnDisable() { try { Experiment.FieldDisabled(this); } catch (Exception e) { Experiment.Fault(e); } }
}

// Owns only rendering overrides. No entity position, movement, layer or physics writes.
internal sealed class PlayerStabilizer
{
    public const int Scale = 8;
    private readonly FieldPlayer _player;
    private readonly Camera _main, _tile;
    private readonly Material _compositor;
    private readonly RenderTexture _original;
    private readonly FieldPlayerController[] _controllers;
    private readonly List<Transform> _visuals = new();
    private readonly List<ScopedOverride<Vector3>> _positions = new();
    private readonly List<ScopedOverride<RenderTexture>> _targets = new();
    private readonly ScopedOverride<Texture> _mainTexture;
    private readonly ScopedOverride<float> _diffuseBias;
    private readonly ScopedOverride<float> _overlayDiffuseBias;
    private readonly List<SpriteRenderer> _renderers = new();
    private readonly List<int> _visualLayers = new();
    private readonly List<string> _visualRoles = new();
    private RenderTexture? _high;
    public FieldPass? Pass { get; private set; }
    public int PreparedFrame { get; private set; } = -1;
    public int RenderedFrame { get; private set; } = -1;
    public float X { get; private set; }
    public float Y { get; private set; }
    private readonly int _screenWidth = Screen.width, _screenHeight = Screen.height;

    public PlayerStabilizer(FieldPlayer player, Camera main, RenderTexture original, Material compositor)
    {
        _player = player; _main = main; _original = original; _compositor = compositor;
        if (main.cullingMask != 0 || (int)main.clearFlags != 4 || original.antiAliasing != 1)
            throw new InvalidOperationException("Unrecognized main field camera layout.");
        Camera? tile = null;
        int consumers = 0;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            if (!c.isActiveAndEnabled || c.targetTexture == null || c.targetTexture.Pointer != original.Pointer) continue;
            consumers++;
            if (c.name == "CameraTileMap") { if (tile != null) throw new InvalidOperationException("Multiple tile cameras."); tile = c; }
        }
        if (tile == null || consumers != 2 || tile.cullingMask != 58867457 || (int)tile.clearFlags != 2 ||
            !tile.orthographic || Math.Abs(tile.orthographicSize - 90) > .001f || tile.depth >= main.depth ||
            tile.transform.rotation != Quaternion.identity || tile.rect != new Rect(0, 0, 1, 1))
            throw new InvalidOperationException("Unrecognized tile camera layout.");
        _tile = tile;
        var controllers = new List<FieldPlayerController>();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<FieldPlayerController>())
            if (c.fieldPlayer != null && c.fieldPlayer.Pointer == player.Pointer) controllers.Add(c);
        _controllers = controllers.ToArray();
        if (!HasPlayerControl()) throw new InvalidOperationException("Ordinary player control is required.");
        AddVisual(player.spriteRenderer, true, 0, "body");
        AddVisual(player.headSpriteRenderer, true, 0, "head");
        var shadow = player.shadowEntity;
        // The shipped FieldPlayer prefab puts its shadow on layer 17, not body/head layer 0.
        if (shadow != null) AddVisual(shadow.ShadowVisualInstance, false, 17, "shadow");
        if (_visuals.Count < 2) throw new InvalidOperationException("Expected separate body/head visuals.");
        foreach (var c in new[] { main, tile })
            _targets.Add(new ScopedOverride<RenderTexture>(() => c == null ? original : c.targetTexture,
                value => { if (c != null) c.targetTexture = value; }, SameTexture));
        _mainTexture = new ScopedOverride<Texture>(() => compositor == null ? original : compositor.GetTexture("_MainGameTex"),
            value => { if (compositor != null) compositor.SetTexture("_MainGameTex", value); }, SameTexture);
        _diffuseBias = new ScopedOverride<float>(() => compositor == null ? 0 : compositor.GetFloat("_MainGameDiffuseBias"),
            value => { if (compositor != null) compositor.SetFloat("_MainGameDiffuseBias", value); });
        _overlayDiffuseBias = new ScopedOverride<float>(() => compositor == null ? 0 : compositor.GetFloat("_OverlayDiffuseBias"),
            value => { if (compositor != null) compositor.SetFloat("_OverlayDiffuseBias", value); });
    }

    public void Initialize()
    {
        if (SystemInfo.maxTextureSize < 320 * Scale) throw new InvalidOperationException("Render target exceeds GPU limit.");
        // Copy format/depth/color-space settings; resize our new resource, never the game's texture.
        _high = new RenderTexture(_original);
        _high.width = 320 * Scale; _high.height = 180 * Scale;
        _high.name = "PRStutter temporary field";
        _high.filterMode = _original.filterMode; _high.wrapMode = _original.wrapMode;
        if (!_high.Create()) throw new InvalidOperationException("Could not allocate the temporary field target.");
        Pass = _tile.gameObject.AddComponent<FieldPass>();
    }

    private static bool SameTexture(Texture? a, Texture? b) =>
        a == null ? b == null : b != null && a.Pointer == b.Pointer;

    private void AddVisual(SpriteRenderer? renderer, bool required, int expectedLayer, string role)
    {
        if (renderer == null) { if (required) throw new InvalidOperationException($"Missing player {role} visual."); return; }
        var transform = renderer.transform;
        bool isRoot = transform.Pointer == _player.transform.Pointer;
        bool isChild = transform.IsChildOf(_player.transform);
        var scale = transform.lossyScale;
        int layer = renderer.gameObject.layer;
        if (isRoot || !isChild || scale != Vector3.one || layer != expectedLayer || (_tile.cullingMask & (1 << layer)) == 0)
            throw new InvalidOperationException($"Player {role} visual '{renderer.name}' rejected: root={isRoot}, childOfPlayer={isChild}, scale=({scale.x},{scale.y},{scale.z}), layer={layer}, expectedLayer={expectedLayer}, tileMask={_tile.cullingMask}.");
        if (required && (renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name != "Last/Sprites/FieldCharaEntityShader"))
            throw new InvalidOperationException("Unrecognized player shader.");
        foreach (var previous in _visuals)
            if (previous.Pointer == transform.Pointer || previous.IsChildOf(transform) || transform.IsChildOf(previous))
                throw new InvalidOperationException("Overlapping player visual transforms.");
        _visuals.Add(transform);
        _renderers.Add(renderer);
        _visualLayers.Add(expectedLayer);
        _visualRoles.Add(role);
        Experiment.Note($"F10 visual accepted: role={role}, name={renderer.name}, layer={layer}, parent={transform.parent.name}.");
        _positions.Add(new ScopedOverride<Vector3>(() => transform == null ? Vector3.zero : transform.localPosition,
            value => { if (transform != null) transform.localPosition = value; },
            (a, b) => a.x == b.x && a.y == b.y && a.z == b.z));
    }

    private bool HasPlayerControl()
    {
        if (_player == null || _player.IsAutoMoving || _player.IsRiging) return false;
        foreach (var c in _controllers)
            if (c != null && c.isActiveAndEnabled && c.InputEnable && c.fieldPlayer != null && c.fieldPlayer.Pointer == _player.Pointer) return true;
        return false;
    }

    public void Prepare()
    {
        Restore();
        if (!HasPlayerControl() || _high == null || !_high.IsCreated() || _main == null || _tile == null ||
            !_tile.isActiveAndEnabled || Screen.width != _screenWidth || Screen.height != _screenHeight ||
            !SameTexture(_main.targetTexture, _original) || !SameTexture(_tile.targetTexture, _original))
            throw new InvalidOperationException("Player control, target or display changed.");
        foreach (var target in _targets) target.Apply(_high);
        PreparedFrame = Time.frameCount;
    }

    public bool TargetsReady => _high != null && _tile != null && _main != null &&
        SameTexture(_main.targetTexture, _high) && SameTexture(_tile.targetTexture, _high);

    public void OffsetVisuals(float x, float y)
    {
        if (PreparedFrame != Time.frameCount || !TargetsReady || !HasPlayerControl())
            throw new InvalidOperationException("Field rendering order or player control changed.");
        X = StabilizationMath.Quantize(x, Scale); Y = StabilizationMath.Quantize(y, Scale);
        if (_player.spriteRenderer == null || _player.headSpriteRenderer == null ||
            _player.spriteRenderer.Pointer != _renderers[0].Pointer || _player.headSpriteRenderer.Pointer != _renderers[1].Pointer)
            throw new InvalidOperationException("Player body/head visual changed.");
        for (int i = 0; i < _positions.Count; i++)
        {
            var transform = _visuals[i];
            if (transform == null || _renderers[i] == null)
                throw new InvalidOperationException($"Player {_visualRoles[i]} visual was removed.");
            int layer = _renderers[i].gameObject.layer;
            var scale = transform.lossyScale;
            bool isChild = transform.IsChildOf(_player.transform);
            if (layer != _visualLayers[i] || (_tile.cullingMask & (1 << layer)) == 0 ||
                transform.parent == null || scale != Vector3.one || !isChild || transform.Pointer == _player.transform.Pointer)
                throw new InvalidOperationException($"Player {_visualRoles[i]} visual changed: childOfPlayer={isChild}, scale=({scale.x},{scale.y},{scale.z}), layer={layer}, expectedLayer={_visualLayers[i]}, parentMissing={transform.parent == null}.");
            // Convert world XY to this visual's parent coordinates; preserve its original Z.
            var localOffset = transform.parent.InverseTransformVector(new Vector3(X, Y, 0));
            if (Math.Abs(localOffset.z) > .00001f) throw new InvalidOperationException("Rotated player visual hierarchy.");
            var original = transform.localPosition;
            _positions[i].Apply(new Vector3(original.x + localOffset.x, original.y + localOffset.y, original.z));
        }
    }

    public void AfterField()
    {
        RestorePositions();
        if (PreparedFrame == Time.frameCount) RenderedFrame = Time.frameCount;
    }

    public void Present()
    {
        if (RenderedFrame != Time.frameCount || !TargetsReady || _high == null)
            throw new InvalidOperationException("No corrected field rendered this frame.");
        _mainTexture.Apply(_high);
        // CRT color spread uses field texel size: keep its original world-space width.
        _diffuseBias.Apply(_compositor.GetFloat("_MainGameDiffuseBias") * Scale);
        // The installed CRT vertex program uses main texel size for overlay taps too.
        _overlayDiffuseBias.Apply(_compositor.GetFloat("_OverlayDiffuseBias") * Scale);
    }

    public void RestorePositions()
    {
        Exception? error = null;
        foreach (var position in _positions) try { position.Restore(); } catch (Exception e) { error ??= e; }
        if (error != null) throw error;
    }
    public void Restore()
    {
        Exception? error = null;
        try { RestorePositions(); } catch (Exception e) { error = e; }
        try { _diffuseBias.Restore(); } catch (Exception e) { error ??= e; }
        try { _overlayDiffuseBias.Restore(); } catch (Exception e) { error ??= e; }
        try { _mainTexture.Restore(); } catch (Exception e) { error ??= e; }
        foreach (var target in _targets) try { target.Restore(); } catch (Exception e) { error ??= e; }
        if (error != null) throw error;
        PreparedFrame = RenderedFrame = -1;
    }
    public void Dispose()
    {
        Restore(); // Do not release a texture while any restoration failed.
        var pass = Pass; Pass = null;
        if (pass != null) UnityEngine.Object.Destroy(pass);
        if (_high != null) { _high.Release(); UnityEngine.Object.Destroy(_high); _high = null; }
    }
}
