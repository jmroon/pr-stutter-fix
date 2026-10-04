using System;
using System.Collections.Generic;
using Last.Entity.Field;
using PRStutter.RenderExperiment;
using UnityEngine;

namespace PRStutter.GridExperiment;

// Only temporary rendering transforms. The logical player and follow camera are read-only.
internal sealed class VisualMotion
{
    private readonly FieldPlayer _player;
    private readonly Camera _logicalCamera;
    private readonly int _scale;
    private readonly List<CameraPose> _cameras = new();
    private readonly List<VisualPose> _visuals = new();
    private readonly Camera _renderRoot;
    private readonly InheritedTranslation<Vector3> _cameraTranslation;
    private Vector3 _logicalPosition, _logicalCameraPosition;
    private bool _applied;
    private int _frames, _corrected;
    private float _maxResidual, _maxError;
    public Vector3 CameraOffset { get; private set; }
    public string Summary => $"motionFrames={_frames}, correctedFrames={_corrected}, maxResidual={_maxResidual:F5}, maxQuantizationError={_maxError:F5}";
    private static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    public VisualMotion(FieldPlayer player, Camera logicalCamera, List<Camera> cameras, int scale)
    {
        _player = player; _logicalCamera = logicalCamera; _scale = scale;
        if (player.transform.lossyScale != Vector3.one || player.transform.rotation != Quaternion.identity)
            throw new InvalidOperationException("Unsupported logical player transform.");
        if (player.transform.parent != null && (player.transform.parent.lossyScale != Vector3.one || player.transform.parent.rotation != Quaternion.identity))
            throw new InvalidOperationException("Player movement coordinates are not aligned with world XY.");
        Camera? tile = null;
        foreach (var camera in cameras) {
            if (camera.Pointer == logicalCamera.Pointer || camera.transform.rotation != Quaternion.identity ||
                camera.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Unsupported render-camera transform.");
            if (camera.name == "CameraTileMap") tile = camera;
            _cameras.Add(new CameraPose(camera));
        }
        if (_cameras.Count != 3 || tile == null || tile.cullingMask != 58867457)
            throw new InvalidOperationException("Expected tile/upper/ceiling render cameras.");
        _renderRoot = tile;
        var descendants = new List<Func<Vector3>>();
        foreach (var pose in _cameras) {
            if (pose.Camera.Pointer == tile.Pointer) continue;
            if (pose.Camera.transform.parent == null || pose.Camera.transform.parent.Pointer != tile.transform.Pointer)
                throw new InvalidOperationException("Transparency cameras must be direct children of the tile camera.");
            descendants.Add(() => pose.Camera == null ? pose.Original : pose.Camera.transform.position);
        }
        if (logicalCamera.transform.IsChildOf(tile.transform) || player.transform.IsChildOf(tile.transform))
            throw new InvalidOperationException("Render camera root must not contain gameplay transforms.");
        var originalRoot = tile.transform.position;
        _cameraTranslation = new InheritedTranslation<Vector3>(
            () => tile == null ? originalRoot : tile.transform.position,
            value => { if (tile != null) tile.transform.position = value; },
            descendants, (a, b) => a + b, Exact);
        Test.Note("Motion camera hierarchy: move CameraTileMap only; CameraUpperTransparentRT and CameraCeilTransparentRT inherit the translation. Logical follow camera is separate.");
        AddVisual(player.spriteRenderer, 0, "body");
        AddVisual(player.headSpriteRenderer, 0, "head");
        if (player.shadowEntity == null || player.shadowEntity.ShadowVisualInstance == null)
            throw new InvalidOperationException("Expected inspected player shadow.");
        AddVisual(player.shadowEntity.ShadowVisualInstance, 17, "shadow");
    }
    private void AddVisual(SpriteRenderer? renderer, int layer, string role)
    {
        if (renderer == null) throw new InvalidOperationException("Missing player visual: " + role);
        var pose = new VisualPose(renderer, layer, role);
        ValidateVisual(pose);
        foreach (var existing in _visuals)
            if (existing.Transform.Pointer == pose.Transform.Pointer || existing.Transform.IsChildOf(pose.Transform) || pose.Transform.IsChildOf(existing.Transform))
                throw new InvalidOperationException("Overlapping player visual transforms.");
        if (role != "shadow" && (renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name != "Last/Sprites/FieldCharaEntityShader"))
            throw new InvalidOperationException("Unsupported player visual shader.");
        _visuals.Add(pose);
    }
    private void ValidateVisual(VisualPose pose)
    {
        if (pose.Renderer == null || pose.Transform == null || pose.Transform.parent == null ||
            pose.Transform.Pointer == _player.transform.Pointer || !pose.Transform.IsChildOf(_player.transform) ||
            pose.Transform.lossyScale != Vector3.one ||
            pose.Renderer.gameObject.layer != pose.Layer)
            throw new InvalidOperationException("Player visual hierarchy changed: " + pose.Role);
    }
    public void Apply()
    {
        if (_applied) throw new InvalidOperationException("Motion applied twice without restoration.");
        if (_player.spriteRenderer == null || _player.spriteRenderer.Pointer != _visuals[0].Renderer.Pointer ||
            _player.headSpriteRenderer == null || _player.headSpriteRenderer.Pointer != _visuals[1].Renderer.Pointer ||
            _player.shadowEntity == null || _player.shadowEntity.ShadowVisualInstance == null ||
            _player.shadowEntity.ShadowVisualInstance.Pointer != _visuals[2].Renderer.Pointer)
            throw new InvalidOperationException("Player renderers replaced.");
        var local = _player.transform.localPosition; var start = _player.startPos; var dest = _player.destPos;
        if (!MotionResidual.TryCalculate(start.x, start.y, dest.x, dest.y, local.x, local.y,
                _player.moveTimer, _player.moveTime, out float rx, out float ry))
            throw new InvalidOperationException("Unsupported walking interpolation.");
        float x = StabilizationMath.Quantize(rx, _scale), y = StabilizationMath.Quantize(ry, _scale);
        CameraOffset = new Vector3(x, y, 0);
        _logicalPosition = _player.transform.position;
        _logicalCameraPosition = _logicalCamera.transform.position;
        // Mark ownership before the first setter so partial application is recoverable.
        _applied = true;
        foreach (var pose in _visuals) {
            ValidateVisual(pose);
            var offset = pose.Transform.parent.InverseTransformVector(CameraOffset);
            if (Math.Abs(offset.z) > .00001f) throw new InvalidOperationException("Player visual has non-planar parent.");
            pose.Applied = pose.Transform.localPosition + offset;
            pose.Override.Apply(pose.Applied);
        }
        foreach (var pose in _cameras) {
            if (pose.Camera == null || !Exact(pose.Camera.transform.position, pose.Original) ||
                pose.Camera.transform.rotation != Quaternion.identity || pose.Camera.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Render camera no longer matches inspected coordinates.");
        }
        ValidateCameraHierarchy();
        _cameraTranslation.Apply(CameraOffset);
        ValidateApplied();
        _frames++;
        if (x != 0 || y != 0) _corrected++;
        _maxResidual = Math.Max(_maxResidual, Math.Max(Math.Abs(rx), Math.Abs(ry)));
        _maxError = Math.Max(_maxError, Math.Max(Math.Abs(rx - x), Math.Abs(ry - y)));
    }
    public void ValidateApplied()
    {
        if (!_applied) return;
        if (_player == null || _logicalCamera == null || !Exact(_player.transform.position, _logicalPosition) ||
            !Exact(_logicalCamera.transform.position, _logicalCameraPosition))
            throw new InvalidOperationException("Logical player/follow-camera position changed during rendering.");
        foreach (var pose in _visuals) {
            ValidateVisual(pose);
            if (!Exact(pose.Transform.localPosition, pose.Applied)) throw new InvalidOperationException("Player visual overwritten before draw.");
        }
        ValidateCameraHierarchy();
        _cameraTranslation.Validate();
    }
    private void ValidateCameraHierarchy()
    {
        if (_renderRoot == null) throw new InvalidOperationException("Render camera root destroyed.");
        foreach (var pose in _cameras)
            if (pose.Camera == null || (pose.Camera.Pointer != _renderRoot.Pointer &&
                (pose.Camera.transform.parent == null || pose.Camera.transform.parent.Pointer != _renderRoot.transform.Pointer)))
                throw new InvalidOperationException("Render camera hierarchy changed.");
    }
    public void Restore()
    {
        Exception? error = null;
        foreach (var pose in _visuals) try { pose.Override.Restore(); } catch (Exception e) { error ??= e; }
        try { _cameraTranslation.Restore(); } catch (Exception e) { error ??= e; }
        if (error != null) throw error;
        _applied = false; CameraOffset = Vector3.zero;
    }
    private sealed class CameraPose
    {
        public readonly Camera Camera;
        public readonly Vector3 Original;
        public CameraPose(Camera camera)
        {
            Camera = camera; Original = camera.transform.position;
        }
    }
    private sealed class VisualPose
    {
        public readonly SpriteRenderer Renderer;
        public readonly Transform Transform;
        public readonly int Layer;
        public readonly string Role;
        public readonly ScopedOverride<Vector3> Override;
        public Vector3 Applied;
        public VisualPose(SpriteRenderer renderer, int layer, string role)
        {
            Renderer = renderer; Transform = renderer.transform; Layer = layer; Role = role;
            Override = new ScopedOverride<Vector3>(() => Transform == null ? Vector3.zero : Transform.localPosition,
                value => { if (Transform != null) Transform.localPosition = value; }, Exact);
        }
    }
}
