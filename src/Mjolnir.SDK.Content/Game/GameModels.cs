using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using SandSailorStudio.Inventory;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Mods' models from their AssetBundles. A bundle is loaded once and kept for the
/// whole game. A mod item with a model gets a world object of its own: a copy of its base item's (the game's components that
/// pick it up, hold it, throw it and send it over the network stay), with the base's look taken off its <c>geometry</c> and
/// the mod's model put there, and the base's box colliders fitted round the model with the room they left round the base's
/// look. The copies live under an inactive object that outlasts every scene, so they never wake: the game's pool makes the
/// live objects from them.
/// </summary>
internal static class GameModels
{
    private const string Source = GameLocalization.Source;
    private const float ThinnestCollider = 0.02f;

    private static readonly Dictionary<(string Bundle, string Asset), GameObject?> s_prefabs = [];
    private static readonly Dictionary<int, (GameObject World, int Version)> s_dressed = [];
    private static GameObject? s_holder;

    /// <summary>
    /// Gives a mod item's copy of its base item the world object it should have: the mod's model when it has one, else the
    /// base's own. Made once per item and model (again only when its model changes), kept for the game.
    /// </summary>
    public static void Dress(ItemInfo info, ModItem item, ItemInfo? template)
    {
        try
        {
            DressWith(info, item, template);
        }
        catch (Exception e)
        {
            // The item stays as its base looks: a model must never keep a mod's items out of the game.
            if (template != null) info.spawnObject = template.spawnObject;
            WarnOnce($"Models: {item}'s model failed, it keeps its base's look: {e}");
        }
    }

    private static void DressWith(ItemInfo info, ModItem item, ItemInfo? template)
    {
        if (item.ModelPath is not { } path || item.ModelAsset is not { } asset)
        {
            if (template != null) info.spawnObject = template.spawnObject;
            return;
        }
        int version = item.ModelVersion;
        if (s_dressed.TryGetValue(item.Id, out var dressed) && dressed.Version == version && dressed.World != null)
        {
            info.spawnObject = dressed.World;
            return;
        }
        if (template?.spawnObject == null)
        {
            WarnOnce($"Models: {item} has no world object of its base {item.BasedOn} to put its model on");
            return;
        }
        if (Prefab(path, asset, item) is not { } model) return;
        var world = Build(template.spawnObject, model, info.name);
        if (dressed.World != null) Object.Destroy(dressed.World);
        s_dressed[item.Id] = (world, version);
        info.spawnObject = world;
        SdkLog.Info(Source, $"Models: {item} has its model '{asset}' from {Path.GetFileName(path)} ({Describe(world)})");
    }

    /// <summary>A prefab of a mod's bundle, null (warned once) when the bundle or the prefab is missing.</summary>
    public static GameObject? Prefab(string bundlePath, string asset, object owner)
    {
        if (s_prefabs.TryGetValue((bundlePath, asset), out var known)) return known;
        GameObject? prefab = null;
        if (Bundle(bundlePath, owner) is { } bundle)
        {
            prefab = bundle.LoadAsset(asset, Il2CppType.Of<GameObject>())?.TryCast<GameObject>();
            if (prefab == null) WarnOnce($"Models: {owner}: {Path.GetFileName(bundlePath)} has no prefab '{asset}'");
            else
            {
                prefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                UseGameShaders(prefab, owner);
            }
        }
        return s_prefabs[(bundlePath, asset)] = prefab;
    }

    // The bundle loaded once for every module (SharedBundles: a mod's bundle may hold a model and an animation).
    private static AssetBundle? Bundle(string path, object owner)
    {
        if (!File.Exists(path))
        {
            WarnOnce($"Models: {owner}: no bundle {path}");
            return null;
        }
        return SharedBundles.Get(path, () =>
        {
            // The game's own way of loading a bundle (the synchronous one is not in the game, and its stand-in fails); asking
            // for the bundle waits for the load.
            var bundle = AssetBundle.LoadFromFileAsync(path)?.assetBundle;
            if (bundle == null) WarnOnce($"Models: {owner}: {SharedBundles.WhyNotLoaded(path)}");
            else SdkLog.Info(Source, $"Models: bundle {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))}/{Path.GetFileName(path)} loaded");
            return bundle;
        });
    }

    // A bundle's materials take the game's own shader of the same name (HDRP/Lit): the copy the bundle carries is compiled for
    // the kit's HDRP settings, not the game's, and draws black in the game (seen 2026-11-04). The material keeps its properties.
    private static void UseGameShaders(GameObject prefab, object owner)
    {
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material?.shader == null) continue;
                string name = material.shader.name;
                if (Shader.Find(name) is { } own) material.shader = own;
                else WarnOnce($"Models: {owner}: the game has no shader '{name}' for {material.name}; the bundle's own may draw wrong");
            }
        }
    }

    // The base item's world object with the mod's look: its geometry keeps its own components (equipment, culling) and its
    // place in the pickables, which find their renderers under it as they wake.
    private static GameObject Build(GameObject baseWorld, GameObject model, string name)
    {
        var world = Object.Instantiate(baseWorld, ModelGeometry.Holder(ref s_holder, "Mjolnir models", HideFlags.HideAndDontSave).transform, false);
        world.name = name;
        var geometry = world.GetComponent<ItemObject>()?.geometry ?? world;
        var boxes = world.GetComponentsInChildren<BoxCollider>(true).ToList();
        var room = boxes.Select(box => Room(box, ModelGeometry.Corners(geometry))).ToList();
        foreach (var lod in geometry.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
        foreach (var renderer in geometry.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
        foreach (var filter in geometry.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(filter);

        // The model's origin is the item's, whatever the base's geometry is turned or scaled by.
        var look = Object.Instantiate(model, world.transform, false);
        look.name = "Model";
        look.transform.SetParent(geometry.transform, true);
        foreach (var collider in look.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (var body in look.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        ModelGeometry.SetLayer(look.transform, geometry.layer);

        // Each box collider of the base (the body's, the picking volume's) round the model with the room it left round the
        // base's look: the game's looks stand above their colliders (a dropped rope's by 8 cm, a stick's by 4), because the
        // ground is drawn above the terrain the physics knows, a paved path more so; a model fitted tight sank into a path
        // (seen by the user, 2026-11-04).
        var corners = ModelGeometry.Corners(look);
        if (corners.Count == 0) return world;
        for (int i = 0; i < boxes.Count; i++)
        {
            var (min, max) = Span(boxes[i], corners);
            boxes[i].center = (min + max) / 2;
            boxes[i].size = Vector3.Max(max - min + room[i], Vector3.one * ThinnestCollider);
        }
        return world;
    }

    // How much bigger than the base's look a box collider is, along its own axes.
    private static Vector3 Room(BoxCollider box, List<Vector3> look)
    {
        if (look.Count == 0) return Vector3.zero;
        var (min, max) = Span(box, look);
        return Vector3.Max(box.size - (max - min), Vector3.zero);
    }

    // Where points lie along a collider's own axes.
    private static (Vector3 Min, Vector3 Max) Span(BoxCollider box, List<Vector3> points)
    {
        var local = points.Select(p => box.transform.InverseTransformPoint(p)).ToList();
        return (local.Aggregate(Vector3.Min), local.Aggregate(Vector3.Max));
    }

    private static string Describe(GameObject world)
    {
        var renderers = world.GetComponentsInChildren<Renderer>(true);
        var shaders = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader?.name ?? "-").Distinct();
        return $"{renderers.Length} renderer(s), shaders {string.Join(", ", shaders)}, {world.GetComponentsInChildren<BoxCollider>(true).Length} box collider(s) fitted";
    }

    private static void WarnOnce(string message) => ContentLog.WarnOnce(message);
}
