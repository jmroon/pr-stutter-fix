using System;
using System.Collections.Generic;
using System.Text.Json;
using Last.Entity.Field;
using UnityEngine;

namespace PRStutter.Diagnostics;

// A one-time read at capture start. No Renderer.material calls: those instantiate materials.
internal static class RenderInspection
{
    public static string Read(CameraFollowing following)
    {
        var cameras = new List<object>();
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            var matrix = camera.projectionMatrix;
            var rect = camera.rect;
            cameras.Add(new {
                Id = camera.GetInstanceID(), Name = camera.name,
                Enabled = camera.enabled, Active = camera.gameObject.activeInHierarchy,
                camera.cullingMask, camera.depth, ClearFlags = (int)camera.clearFlags,
                camera.orthographic, camera.orthographicSize,
                camera.pixelWidth, camera.pixelHeight,
                Rect = new { rect.x, rect.y, rect.width, rect.height },
                Projection = new { matrix.m00, matrix.m11, matrix.m02, matrix.m12, matrix.m03, matrix.m13 },
                Transform = Hierarchy(camera.transform), Target = TextureInfo(camera.targetTexture)
            });
        }
        var entity = following.TargetEntity;
        var sprite = entity.TryCast<FieldSpriteEntity>();
        var character = entity.TryCast<FieldCharaEntity>();
        var material = PostProcessLite.GetMaterial();
        var properties = new Dictionary<string, float>();
        if (material != null)
            foreach (string name in new[] { "_MainGameUL", "_MainGameUH", "_MainGameVL", "_MainGameVH",
                "_OverlayUL", "_OverlayUH", "_OverlayVL", "_OverlayVH", "_BlurMainGame", "_FakeCRT", "_OverlayAlpha" })
                if (material.HasProperty(name)) properties[name] = material.GetFloat(name);
        return JsonSerializer.Serialize(new {
            SchemaVersion = 1, PluginVersion = Plugin.Version, Frame = Time.frameCount,
            Limitation = "Capture-start snapshot, not pre-render state. Disabled cameras may be present; inactive GameObjects are excluded by discovery.",
            Cameras = cameras, FollowedEntity = Hierarchy(entity.transform),
            VisualParent = Hierarchy(entity.visualParent),
            Body = RendererInfo(sprite?.spriteRenderer), Head = RendererInfo(character?.headSpriteRenderer),
            Compositor = material == null ? null : new {
                Id = material.GetInstanceID(), Name = material.name,
                Shader = material.shader == null ? "" : material.shader.name,
                Properties = properties,
                MainGame = TextureInfo(material.HasProperty("_MainGameTex") ? material.GetTexture("_MainGameTex") : null),
                Overlay = TextureInfo(material.HasProperty("_OverlayTex") ? material.GetTexture("_OverlayTex") : null)
            }
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object? RendererInfo(SpriteRenderer? renderer)
    {
        if (renderer == null) return null;
        var material = renderer.sharedMaterial;
        return new {
            Id = renderer.GetInstanceID(), Name = renderer.name, renderer.enabled,
            Layer = renderer.gameObject.layer, renderer.sortingLayerID, renderer.sortingOrder,
            Transform = Hierarchy(renderer.transform),
            MaterialId = material == null ? 0 : material.GetInstanceID(),
            MaterialName = material == null ? "" : material.name,
            ShaderName = material == null || material.shader == null ? "" : material.shader.name
        };
    }

    private static object? TextureInfo(Texture? texture)
    {
        if (texture == null) return null;
        var rt = texture.TryCast<RenderTexture>();
        return new {
            Id = texture.GetInstanceID(), Name = texture.name, texture.width, texture.height,
            FilterMode = (int)texture.filterMode, WrapMode = (int)texture.wrapMode,
            IsRenderTexture = rt != null, Format = rt == null ? -1 : (int)rt.format,
            Depth = rt == null ? -1 : rt.depth, AntiAliasing = rt == null ? -1 : rt.antiAliasing
        };
    }

    private static List<object> Hierarchy(Transform? transform)
    {
        var nodes = new List<object>();
        for (int i = 0; transform != null && i < 8; i++, transform = transform.parent)
        {
            var world = transform.position;
            var local = transform.localPosition;
            var rotation = transform.rotation;
            var scale = transform.lossyScale;
            nodes.Add(new {
                Id = transform.GetInstanceID(), Name = transform.name,
                World = new Point3(world.x, world.y, world.z), Local = new Point3(local.x, local.y, local.z),
                Rotation = new { rotation.x, rotation.y, rotation.z, rotation.w },
                Scale = new Point3(scale.x, scale.y, scale.z)
            });
        }
        return nodes;
    }
}
