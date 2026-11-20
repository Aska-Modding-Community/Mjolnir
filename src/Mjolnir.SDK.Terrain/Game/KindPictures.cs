using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Pictures of the kinds of the world: the kind's model (its nearest level of detail) copied far above the world on a layer
/// nobody uses, taken by a camera of its own into a texture, then taken away; one picture a kind and size, kept.
/// </summary>
/// <remarks>
/// The game's rendering adapts its exposure to the scene: a camera of its own left at that shows a white blot, and a fixed
/// exposure alone shows the model dark at dawn. So the picture has its own volume (a fixed exposure, no fog, no bloom) and
/// its own two point lights, three times the sun and more, the exposure worked out from what they give (EV ≈ log2 of the
/// illuminance): the time of day hardly shows.
/// </remarks>
internal static class KindPictures
{
    /// <summary>How high above the world the model stands while it is taken.</summary>
    private const float Height = 2500f;

    /// <summary>The background, the build menu's dark: the HDRP camera's buffer keeps no transparency.</summary>
    private static readonly Color Background = new(0.035f, 0.04f, 0.04f, 1f);

    /// <summary>How long the sun's intensity read for the lights is used again, in seconds: a scene-wide search each time.</summary>
    private const float SunKept = 1f;

    private static readonly Dictionary<(string, int, int), Texture2D> s_taken = [];
    private static int s_layer = -2;
    private static VolumeProfile? s_profile;
    private static Exposure? s_exposure;
    private static float s_sun, s_sunRead = float.NegativeInfinity;

    /// <summary>The picture of a model, <paramref name="width"/> × <paramref name="height"/>; null when it has no mesh.</summary>
    public static Texture2D? Of(string key, GameObject model, int width, int height)
    {
        if (s_taken.TryGetValue((key, width, height), out var kept) && kept != null) return kept;
        int layer = FreeLayer();
        if (layer < 0) return null;
        var root = new GameObject("Mjolnir_KindPicture");
        GameObject? cameraObject = null;
        RenderTexture? target = null;
        try
        {
            root.transform.position = new Vector3(0, Height, 0);
            if (!Copy(model, root.transform, layer, out var bounds)) return null;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cameraObject = new GameObject("Mjolnir_KindPictureCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << layer;
            camera.targetTexture = target;
            camera.fieldOfView = 30;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000;
            float size = Math.Max(bounds.extents.magnitude, 0.1f);
            float distance = size / MathF.Sin(15 * MathF.PI / 180) * 0.95f;
            camera.transform.position = bounds.center + new Vector3(-0.6f, 0.45f, -1f).normalized * distance;
            camera.transform.LookAt(bounds.center);
            var hd = cameraObject.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Background;
            hd.volumeLayerMask = 1 << layer;

            float sun = Sun();
            float keyLux = 3 * sun + 2000;
            AddLight(root.transform, camera.transform.position + new Vector3(0, size, 0), keyLux, distance, layer);
            AddLight(root.transform, bounds.center + new Vector3(size * 1.5f, size * 0.5f, size * 1.5f), keyLux * 0.4f, size * 2.2f, layer);
            AddVolume(root.transform, layer, MathF.Log2(keyLux + sun));

            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply(false, true);
            RenderTexture.active = previous;
            picture.name = $"Mjolnir_KindPicture_{key}";
            picture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            s_taken[(key, width, height)] = picture;
            return picture;
        }
        finally
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(root);
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }

    // The renderers of the model's nearest level (all of its mesh renderers without levels) as plain copies under the root.
    internal static bool Copy(GameObject model, Transform root, int layer, out Bounds bounds)
    {
        bounds = default;
        var renderers = new List<Renderer>();
        var lod = model.GetComponentInChildren<LODGroup>(true);
        if (lod != null && lod.GetLODs() is { Length: > 0 } levels)
            foreach (var renderer in levels[0].renderers)
                if (renderer != null) renderers.Add(renderer);
        if (renderers.Count == 0) renderers.AddRange(model.GetComponentsInChildren<MeshRenderer>(true));
        var origin = model.transform.position;
        int copied = 0;
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var part = new GameObject("Part") { layer = layer };
            part.transform.SetParent(root, false);
            part.transform.position = root.position + (renderer.transform.position - origin);
            part.transform.rotation = renderer.transform.rotation;
            part.transform.localScale = renderer.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var mesh = part.AddComponent<MeshRenderer>();
            mesh.sharedMaterials = renderer.sharedMaterials;
            mesh.shadowCastingMode = ShadowCastingMode.Off;
            if (copied++ == 0) bounds = mesh.bounds;
            else bounds.Encapsulate(mesh.bounds);
        }
        return copied > 0;
    }

    // A point light giving lux at a distance (HDRP's point lights are in lumen: lux × 4π × d²), reaching a little past the model.
    private static void AddLight(Transform root, Vector3 at, float lux, float distance, int layer)
    {
        var holder = new GameObject("Light") { layer = layer };
        holder.transform.SetParent(root, false);
        holder.transform.position = at;
        var light = holder.AddComponent<Light>();
        holder.AddComponent<HDAdditionalLightData>();
        light.type = LightType.Point;
        light.range = distance * 2.5f;
        light.intensity = lux * 4 * MathF.PI * distance * distance;
        light.shadows = LightShadows.None;
    }

    // The picture's own volume: a fixed exposure, no fog, no bloom. Its profile is one for every picture, kept for the
    // game's run (a profile is an asset the volume's object does not take with it).
    private static void AddVolume(Transform root, int layer, float exposureValue)
    {
        var holder = new GameObject("Volume") { layer = layer };
        holder.transform.SetParent(root, false);
        var volume = holder.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000;
        if (s_profile == null || s_exposure == null)
        {
            s_profile = ScriptableObject.CreateInstance<VolumeProfile>();
            s_profile.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            s_exposure = s_profile.Add<Exposure>(true);
            s_exposure.mode.Override(ExposureMode.Fixed);
            s_profile.Add<Fog>(true).enabled.Override(false);
            s_profile.Add<Bloom>(true).intensity.Override(0f);
        }
        s_exposure.fixedExposure.Override(exposureValue);
        volume.sharedProfile = s_profile;
    }

    // The strongest directional light's intensity (the sun's), read again at most once a second.
    private static float Sun()
    {
        float now = Time.realtimeSinceStartup;
        if (now - s_sunRead < SunKept) return s_sun;
        s_sunRead = now;
        s_sun = 0;
        foreach (var light in Object.FindObjectsOfType<Light>())
            if (light != null && light.type == LightType.Directional && light.isActiveAndEnabled) s_sun = Math.Max(s_sun, light.intensity);
        return s_sun;
    }

    // The highest layer the game names nothing on (29 in this build), looked up once.
    private static int FreeLayer()
    {
        if (s_layer != -2) return s_layer;
        s_layer = -1;
        for (int layer = 31; layer >= 8 && s_layer < 0; layer--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(layer))) s_layer = layer;
        if (s_layer < 0) SdkLog.Warn(Landscape.Platform, "No free layer for the kinds' pictures");
        return s_layer;
    }
}
