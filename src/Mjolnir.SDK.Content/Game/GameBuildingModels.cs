using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SSSGame;
using SSSGame.Render;
using SandSailorStudio.Render;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// A mod building's own look (<see cref="ModBuilding.WithModel(string, string)"/>, or one per stage,
/// <see cref="ModBuilding.WithModel(string, string, int)"/>). The building has a network prefab of its own (<see cref="BuildingPrefabs"/>), a
/// copy of its base's, and its look is put on that prefab once (<see cref="DressPrefab"/>), so every structure the game
/// spawns from it, on every machine, and the placement preview (a copy of the prefab's own view) carry it:
/// <list type="bullet">
/// <item>its pieces stand at the structure's origin under its view's appearance, on the layer of the base's building meshes:
/// one piece for the whole building, or one per stage, each a finished model and a construction look (its construction
/// model, else its model, without colliders);</item>
/// <item>the base's pick volumes are switched off and out of the pickables' lists, the pickables outline the look they
/// stand for (a build part its stage's construction look, the others the finished models), the base's solid colliders are
/// off (the models' own stand instead), and its footprint and the boxes the player's look finds are fitted round the
/// models.</item>
/// </list>
/// What Unity keeps no copy of is done for each structure as its view is set up (<c>StructurePreview.Setup</c>, which the
/// game calls for a structure in the world and for the placement preview: <see cref="Setup"/>): the base's renderers are
/// switched off (<c>forceRenderingOff</c>: the game's culling turns renderers on and off by <c>enabled</c>, and the game's
/// stages switch the base's objects), the construction looks take the site's construction material, and each frame the
/// pieces follow the stages, so the look is right whatever the order the game sets a structure up in (built, under way,
/// loaded). One piece for the whole building shows its construction look while any stage is under way and its model once
/// the building is finished; a stage's piece shows its construction look while its stage is under way, its model once its
/// stage is built, and nothing before, as the game's houses grow.
/// </summary>
internal static partial class GameBuildingModels
{
    private const string Source = GameLocalization.Source;
    private const string ModelName = "Mjolnir Model";
    private const string ConstructionSuffix = " (construction)";
    private const string StagePrefix = ModelName + " stage ";
    private const float Room = 0.05f; // round the model, so the player's look and the footprint take its whole shape


    /// <summary>
    /// The building's own prefab takes its look, once (again when the building's settings change). A prefab copied from
    /// another mod building's first loses that one's look.
    /// </summary>
    public static void DressPrefab(Structure prefab, ModBuilding building)
    {
        var view = prefab.preview;
        if (view == null) return;
        var appearance = view.appearance != null ? view.appearance.transform : view.transform;
        for (int i = appearance.childCount - 1; i >= 0; i--)
        {
            var child = appearance.GetChild(i);
            if (child.name.StartsWith(ModelName, StringComparison.Ordinal)) Object.DestroyImmediate(child.gameObject);
        }
        try
        {
            var pieces = Pieces(prefab, building);
            if (pieces.Count == 0) return;
            Dress(prefab, appearance, pieces);
            int layers = prefab.buildSite?.layers?.Count ?? 0;
            SdkLog.Info(Source, pieces[0].Stage == null
                ? $"Models: {building} has its model '{building.ModelAsset}' on its own prefab, its construction look in {layers} stage(s)"
                : $"Models: {building} has models of {pieces.Count} of its {layers} stage(s) on its own prefab");
        }
        catch (Exception e)
        {
            SdkLog.Warn(Source, $"Models: {building}'s model failed, it keeps its base's look: {e.Message}");
        }
    }

    // The building's pieces from its bundles: one for every stage, or one per stage that has a look.
    private static List<Piece> Pieces(Structure prefab, ModBuilding building)
    {
        var pieces = new List<Piece>();
        var stages = building.StageLooks;
        if (stages.Count == 0)
        {
            if (building.ModelPath is not { } path || building.ModelAsset is not { } asset) return pieces;
            if (GameModels.Prefab(path, asset, building) is not { } model) return pieces;
            var construction = building.ConstructionModelPath is { } cPath && building.ConstructionModelAsset is { } cAsset
                ? GameModels.Prefab(cPath, cAsset, building) ?? model
                : model;
            pieces.Add(new Piece(null, model, construction));
            return pieces;
        }
        if (building.ModelPath != null || building.ConstructionModelPath != null)
            WarnOnce($"{building}/whole", $"Models: {building} has models of its stages: its model for every stage is left out");
        int layers = Math.Max(1, prefab.buildSite?.layers?.Count ?? 0);
        foreach (var look in stages)
        {
            if (look.Stage >= layers)
            {
                WarnOnce($"{building}/{look.Stage}", $"Models: {building}'s base has {layers} stage(s): the model of stage {look.Stage} is left out");
                continue;
            }
            var model = look.ModelPath is { } path && look.ModelAsset is { } asset ? GameModels.Prefab(path, asset, building) : null;
            var construction = look.ConstructionPath is { } cPath && look.ConstructionAsset is { } cAsset
                ? GameModels.Prefab(cPath, cAsset, building) ?? model
                : model;
            if (construction != null) pieces.Add(new Piece(look.Stage, model, construction));
        }
        return pieces;
    }

    // The prefab's look: the pieces placed, the base's pick volumes and solid colliders off, the footprint and the look's
    // boxes fitted, the pickables given the pieces' renderers. The prefab never wakes, so what is set here is what every
    // structure spawned from it starts with.
    private static void Dress(Structure prefab, Transform appearance, List<Piece> pieces)
    {
        var root = prefab.transform;
        // The models' layer is that of the base's own building meshes (Structure): the game's picking draws only renderers of
        // its layers, and a base may also hold grass and trees on another (the birch's: Default, and no prompt came).
        int layer = -1, structureLayer = LayerMask.NameToLayer("Structure");
        foreach (var renderer in BaseRenderers(appearance))
        {
            int own = renderer.gameObject.layer;
            if (layer < 0 || (own == structureLayer && layer != structureLayer)) layer = own;
        }

        // The base's pick volumes in its look (the birch's PickProxy, a mesh of the tree): the picking and the outline draw
        // them beside the pickables' objects, so the player's look outlined the birch round the model (seen by the user).
        var baseVolumes = new HashSet<nint>();
        foreach (var volume in appearance.GetComponentsInChildren<PickableVolume>(true))
        {
            if (volume == null) continue;
            baseVolumes.Add(volume.Pointer);
            volume.enabled = false;
        }
        foreach (var pickable in root.GetComponentsInChildren<Pickable>(true))
        {
            var volumes = pickable?.pickableVolumes;
            for (int i = (volumes?.Count ?? 0) - 1; i >= 0; i--)
            {
                if (volumes![i] != null && baseVolumes.Contains(volumes[i].Pointer)) volumes.RemoveAt(i);
            }
        }

        var placed = new List<Piece>();
        foreach (var piece in pieces)
        {
            string name = piece.Stage is { } stage ? StagePrefix + stage : ModelName;
            var model = piece.Model != null ? Place(piece.Model, name, appearance, root, layer) : null;
            var construction = Place(piece.Construction, name + ConstructionSuffix, appearance, root, layer);
            foreach (var collider in construction.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            construction.SetActive(false);
            placed.Add(new Piece(piece.Stage, model, construction));
        }

        Bounds? bounds = null;
        foreach (var piece in placed) bounds = ModelGeometry.Union(bounds, ModelGeometry.Bounds(piece.Model ?? piece.Construction));
        if (bounds is { } all)
        {
            var modelColliders = new HashSet<nint>(placed.Where(p => p.Model != null).SelectMany(p => p.Model!.GetComponentsInChildren<Collider>(true)).Select(c => c.Pointer));
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || modelColliders.Contains(collider.Pointer)) continue;
                var box = collider.TryCast<BoxCollider>();
                if (collider.isTrigger)
                {
                    // The footprint (placement, the navigation) and the boxes the player's look finds take the models' shape.
                    if (box != null && (box.name == "Footprint" || box.GetComponent<InteractionAreaLink>() != null)) ModelGeometry.Fit(box, all, Room);
                }
                else if (box != null && box.name == "NavCollider")
                {
                    ModelGeometry.Fit(box, all, Room); // the obstacle the villagers' navigation walks round: the models', not the base's
                }
                else if (collider.gameObject.layer == layer)
                {
                    collider.enabled = false; // the base's solid shape: the models' own colliders stand instead
                }
            }
        }

        // What the game outlines when the player looks at it: a build part its stage's construction look, the rest the
        // finished models.
        var layers = prefab.buildSite?.layers;
        foreach (var pickable in root.GetComponentsInChildren<Pickable>(true))
        {
            var objects = pickable?.pickableObjects;
            if (objects == null || objects.Count == 0) continue;
            // Of the look: objects in the view (the base's renderers or a holder of them: the builder's hut's outlined its
            // model's root, builder_hut_L1, and kept the base's hidden look), or none left (a mod building's prefab
            // copied: its model was taken out).
            bool ofTheLook = true;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] == null) continue;
                ofTheLook = IsUnder(objects[i].transform, appearance);
                if (ofTheLook) break;
            }
            var part = pickable!.GetComponent<BuildPart>();
            if (!ofTheLook && part == null) continue; // a repair site, a dismantled pile: their own meshes
            objects.Clear();
            foreach (var renderer in part != null ? PartRenderers(placed, LayerOf(layers, part)) : FinishedRenderers(placed)) objects.Add(renderer.gameObject);
        }
    }

    // The pieces a structure's view carries, read back from its prefab's objects by name.
    private static List<Piece> Placed(Transform appearance)
    {
        var models = new Dictionary<int, GameObject>();
        var constructions = new Dictionary<int, GameObject>();
        for (int i = 0; i < appearance.childCount; i++)
        {
            var child = appearance.GetChild(i);
            string name = child.name;
            if (!name.StartsWith(ModelName, StringComparison.Ordinal)) continue;
            bool construction = name.EndsWith(ConstructionSuffix, StringComparison.Ordinal);
            if (construction) name = name[..^ConstructionSuffix.Length];
            int stage = -1; // the piece for every stage
            if (name.StartsWith(StagePrefix, StringComparison.Ordinal) && !int.TryParse(name[StagePrefix.Length..], out stage)) continue;
            (construction ? constructions : models)[stage] = child.gameObject;
        }
        return [.. constructions.OrderBy(c => c.Key).Select(c => new Piece(c.Key < 0 ? null : c.Key, models.GetValueOrDefault(c.Key), c.Value))];
    }

    // A build part's outline: its stage's construction look; a stage without a look of its own outlines the stages built
    // before it, else every construction look (only what shows is drawn).
    private static List<Renderer> PartRenderers(List<Piece> pieces, int layer)
    {
        var own = pieces.FirstOrDefault(p => p.Stage == null || p.Stage == layer);
        if (own != null) return own.ConstructionRenderers;
        var before = pieces.Where(p => p.Stage < layer).SelectMany(p => p.Renderers).ToList();
        return before.Count > 0 ? before : [.. pieces.SelectMany(p => p.ConstructionRenderers)];
    }

    // The finished building's outline: its models (a stage with only a construction look shows nothing then); without any
    // model, the construction looks.
    private static List<Renderer> FinishedRenderers(List<Piece> pieces)
    {
        var models = pieces.Where(p => p.Model != null).SelectMany(p => p.Renderers).ToList();
        return models.Count > 0 ? models : [.. pieces.SelectMany(p => p.ConstructionRenderers)];
    }

    private static int LayerOf(Il2CppSystem.Collections.Generic.List<BuildSite.BuildLayer>? layers, BuildPart part)
    {
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            var parts = layers[i]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                if (parts[k] != null && parts[k].Pointer == part.Pointer) return i;
            }
        }
        return 0;
    }

    // The renderers of the base's look: everything in the view but the mod's pieces.
    private static IEnumerable<Renderer> BaseRenderers(Transform appearance)
    {
        foreach (var renderer in appearance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || IsOurs(renderer.transform, appearance)) continue;
            yield return renderer;
        }
    }

    private static bool IsUnder(Transform transform, Transform appearance)
    {
        for (var t = transform; t != null; t = t.parent)
        {
            if (t.Pointer == appearance.Pointer) return true;
        }
        return false;
    }

    private static bool IsOurs(Transform transform, Transform appearance)
    {
        for (var t = transform; t != null && t.Pointer != appearance.Pointer; t = t.parent)
        {
            if (t.name.StartsWith(ModelName, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void WarnOnce(string key, string message) => ContentLog.WarnOnce($"models/{key}", message);

    private static GameObject Place(GameObject prefab, string name, Transform appearance, Transform root, int layer)
    {
        var model = Object.Instantiate(prefab, appearance, false);
        model.name = name;
        model.transform.position = root.position;
        model.transform.rotation = root.rotation;
        model.transform.localScale = prefab.transform.localScale;
        if (layer >= 0) ModelGeometry.SetLayer(model.transform, layer);
        return model;
    }

    /// <summary>
    /// A piece of a building's look: its stage (null for every stage), its finished model (null for a stage with a
    /// construction look only) and its construction look.
    /// </summary>
    private sealed class Piece(int? stage, GameObject? model, GameObject construction)
    {
        private List<Renderer>? _renderers, _constructionRenderers;

        public int? Stage { get; } = stage;
        public GameObject? Model { get; } = model;
        public GameObject Construction { get; } = construction;
        public List<Renderer> Renderers => _renderers ??= Model == null ? [] : [.. Model.GetComponentsInChildren<Renderer>(true).Where(r => r != null)];
        public List<Renderer> ConstructionRenderers => _constructionRenderers ??= [.. Construction.GetComponentsInChildren<Renderer>(true).Where(r => r != null)];
    }
}
