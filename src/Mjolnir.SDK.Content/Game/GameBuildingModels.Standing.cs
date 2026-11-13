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

// The mod buildings standing in the world (and the placement preview): what Unity keeps no copy of, done for each structure
// as its view is set up, and their looks following their stages every frame.
internal static partial class GameBuildingModels
{
    private static readonly List<Dressed> s_dressed = [];

    /// <summary>A structure's view was set up (in the world or as the placement preview): what Unity keeps no copy of.</summary>
    public static void Setup(StructurePreview preview, Structure? structure, StructureTemplate? template)
    {
        var shown = template ?? structure?.Template;
        if (preview == null || shown == null) return;
        int id = shown.id;
        if (id >= 0) return; // the game's own: a mod's ids are negative
        var building = ContentRegistry.Shared.BuildingById(id);
        if (building == null || !building.HasOwnLook) return;
        var appearance = preview.appearance != null ? preview.appearance.transform : preview.transform;
        var pieces = Placed(appearance);
        if (pieces.Count == 0)
        {
            WarnOnce($"{building}/prefab", $"Models: {building} stands without its own prefab's look: it shows its base's");
            return;
        }
        s_dressed.RemoveAll(d => d.Is(appearance));

        var baseLook = BaseRenderers(appearance).ToList();
        foreach (var renderer in baseLook) renderer.forceRenderingOff = true;
        // The base's pick volumes the prefab switched off: one that joined its pickable as it woke leaves it.
        foreach (var volume in appearance.GetComponentsInChildren<PickableVolume>(true))
        {
            if (volume == null || IsOurs(volume.transform, appearance)) continue;
            volume.Detach();
            volume.enabled = false;
        }
        if (structure == null)
        {
            // The placement preview checks the place by what its colliders touch (Structure among the layers): the model's
            // own colliders, on its building's layer, made every place «blocked by an obstacle» (seen by the user). It shows
            // the building finished: every model.
            foreach (var piece in pieces)
            {
                if (piece.Model != null)
                {
                    foreach (var collider in piece.Model.GetComponentsInChildren<Collider>(true))
                    {
                        collider.enabled = false;
                        Object.Destroy(collider);
                    }
                    piece.Model.SetActive(true);
                }
                piece.Construction.SetActive(false);
            }
            return;
        }

        // The construction material, as the game gives its parts' geometry (a MeshMaterialReplacer on each renderer). Not
        // through the parts' own lists: once the work starts the game's construction shader draws a part by its mesh's own
        // space, and the model, made upright as Unity's are, vanished (the game's meshes stand on their Z).
        var site = structure.buildSite;
        if (site?.constructionMaterial is { } material)
        {
            foreach (var renderer in pieces.SelectMany(p => p.ConstructionRenderers))
            {
                var replacer = renderer.GetComponent<MeshMaterialReplacer>() ?? renderer.gameObject.AddComponent<MeshMaterialReplacer>();
                replacer.ApplyMaterial(material);
            }
        }

        // The pickables of the look make their picking data again, now that the construction looks have their material:
        // woken with the prefab's objects before it, a build part's was never picked (UiLab altar: no prompt on the altar
        // under way, the game's lantern at the same place found). Every piece is on while the pickables take them (the
        // prefab keeps the construction looks off, for the placement preview); following the stages decides what shows
        // right after.
        foreach (var piece in pieces)
        {
            piece.Construction.SetActive(true);
            piece.Model?.SetActive(true);
        }
        var layers = site?.layers;
        foreach (var pickable in structure.GetComponentsInChildren<Pickable>(true))
        {
            var objects = pickable?.pickableObjects;
            if (objects == null || objects.Count == 0) continue;
            bool ofTheLook = false;
            // Of the look: its objects lie in the view (a renderer, or a holder of them: the builder's hut's outlined its
            // model's root, builder_hut_L1).
            for (int i = 0; i < objects.Count && !ofTheLook; i++) ofTheLook = objects[i] != null && IsUnder(objects[i].transform, appearance);
            var part = pickable!.GetComponent<BuildPart>();
            if (part != null) Repick(pickable, PartRenderers(pieces, LayerOf(layers, part)));
            else if (ofTheLook) Repick(pickable, FinishedRenderers(pieces));
        }

        var geometry = new List<GameObject>();
        var completed = new List<GameObject>();
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            var parts = layers[i]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var part = parts[k];
                if (part == null) continue;
                Collect(part.geometryObjects, geometry);
                Collect(part.completedObjects, completed);
            }
        }
        var dressed = new Dressed(structure, appearance, pieces, geometry, completed);
        dressed.Follow();
        s_dressed.Add(dressed);
    }

    /// <summary>Each frame: the looks of the mod buildings standing follow their stages.</summary>
    public static void Tick()
    {
        for (int i = s_dressed.Count - 1; i >= 0; i--)
        {
            if (!s_dressed[i].Follow()) s_dressed.RemoveAt(i);
        }
    }

    // A pickable takes other objects: one awake and enabled already has made its renderers' picking data (their pick ids
    // and materials, made as it is activated) for the objects it had then, so it is switched off, its lists emptied, given
    // the objects and switched on again.
    private static void Repick(Pickable pickable, List<Renderer> renderers)
    {
        bool enabled = pickable.enabled;
        if (enabled) pickable.enabled = false; // _Deactivate: it leaves the picking
        pickable.pickableObjects.Clear();
        foreach (var renderer in renderers) pickable.pickableObjects.Add(renderer.gameObject);
        pickable._pickableRendererList?.Clear();
        pickable._renderers?.Clear();
        pickable.InitializePickableObjects();
        if (enabled) pickable.enabled = true; // _Activate: back with the new objects
    }

    private static void Collect(Il2CppSystem.Collections.Generic.List<GameObject>? objects, List<GameObject> into)
    {
        for (int i = 0; objects != null && i < objects.Count; i++)
        {
            if (objects[i] != null) into.Add(objects[i]);
        }
    }

    /// <summary>One mod building in the world, its pieces following its stages.</summary>
    private sealed class Dressed(Structure structure, Transform appearance, List<Piece> pieces, List<GameObject> geometry, List<GameObject> completed)
    {
        // The game's construction shader: whether the part still waits for supplies (1: the yellow of the game's parts
        // asking for them, 0: the green of a part to be built) and how much the player's look lights it.
        private const string Progression = "_ConstructionProgression", Selection = "_ConstructionSelection";
        private float _progression = float.NaN, _selection = float.NaN;

        public bool Is(Transform other) => appearance != null && other != null && appearance.Pointer == other.Pointer;

        /// <summary>False once the structure is gone.</summary>
        public bool Follow()
        {
            if (structure == null || appearance == null) return false;
            bool active = structure.IsActive;
            // The stage under way: the site's current layer; none once the building stands (its site is gone then).
            var site = active ? null : structure.buildSite;
            int current = site != null ? site.GetCurrentLayerIndex() : int.MaxValue;
            bool any = false;
            foreach (var piece in pieces)
            {
                if (piece.Construction == null) return false;
                bool underWay, finished;
                if (piece.Stage is { } stage)
                {
                    underWay = !active && stage == current;
                    finished = active || stage < current;
                }
                else
                {
                    // One piece for every stage: a stage under way shows its geometry (the base's parts and scaffolding);
                    // none shows once it is built, or when its base has no stages at all.
                    underWay = !active && geometry.Any(g => g != null && g.activeInHierarchy);
                    finished = !underWay && (active || completed.Count == 0 || completed.Any(c => c != null && c.activeInHierarchy));
                }
                if (piece.Construction.activeSelf != underWay) piece.Construction.SetActive(underWay);
                if (piece.Model != null && piece.Model.activeSelf != finished) piece.Model.SetActive(finished);
                any |= underWay;
            }
            if (any) Tint();
            return true;
        }

        // The tint the game gives its part's geometry (BuildPart._UpdateConstructionMaterial, on each renderer's own
        // material), given to the construction looks as well.
        private void Tint()
        {
            foreach (var g in geometry)
            {
                if (g == null || !g.activeInHierarchy) continue;
                var material = g.GetComponent<Renderer>()?.sharedMaterial;
                if (material == null || !material.HasProperty(Progression)) continue;
                float progression = material.GetFloat(Progression), selection = material.GetFloat(Selection);
                if (progression == _progression && selection == _selection) return;
                _progression = progression;
                _selection = selection;
                foreach (var renderer in pieces.SelectMany(p => p.ConstructionRenderers))
                {
                    if (renderer == null) continue;
                    MaterialProperties.SetFloat(renderer, Progression, progression);
                    MaterialProperties.SetFloat(renderer, Selection, selection);
                }
                return;
            }
        }
    }
}

/// <summary>The hidden patch behind <see cref="ModBuilding.WithModel(string, string)"/> (the hybrid Harmony rule).</summary>
[HarmonyPatch]
internal static class BuildingModelPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructurePreview), nameof(StructurePreview.Setup))]
    private static void Setup(StructurePreview __instance, Structure __0, StructureTemplate __1)
    {
        try
        {
            GameBuildingModels.Setup(__instance, __0, __1);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Models: a building's look failed: {e}");
        }
    }
}
