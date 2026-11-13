using System;
using System.Collections.Generic;
using System.Linq;
using SSSGame;
using SSSGame.Render;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "stages" scenario: a mod building whose stages have models of their own (Mjolnir.SDK.Content's
/// <c>ModBuilding.WithModel(bundle, asset, stage)</c>). UiLab's lab_hut stands on the builder's hut (three stages) and takes
/// the examples' models as its stages' looks (their bundles read from the examples' folders, whether those mods are on or
/// not): stage 0 the altar, stage 1 only a construction look (the twine spool), stage 2 the spool with the twine as its
/// construction look. It is placed in front of the player and taken through its stages as the player does (supplies, then
/// hammer work): each step logs which of the building's pieces show and what each build part's pickable outlines, with a
/// screenshot; then the placement preview's pieces. It is dismantled at the end. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> StagesScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = db?.GetItemInfo(HutAsset)?.TryCast<StructureTemplate>();
        Log($"Stages: settlement {settlement != null}, lab hut {template?.name}");
        if (settlement == null || template == null) yield break;

        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var at = cam.position + forward * 5f;
        at = GroundPoint(at) ?? at;
        var placement = new PointPlacement();
        placement.position = at;
        placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        Structure? hut = null;
        try
        {
            hut = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
        }
        catch (Exception e)
        {
            Log($"Stages: placing failed: {e}");
        }
        if (hut == null) yield break;
        yield return 2f;
        Log($"Stages: placed: {StructureState(hut)}");
        Log($"Stages:   pieces {Pieces(hut)}; parts {PartOutlines(hut)}");
        Shot("stages-placed");

        for (int step = 0; step < 20 && hut != null && !hut.IsActive; step++)
        {
            string did = AdvanceStep(hut);
            yield return 1.5f;
            Log($"Stages: step {step}: {did}; layer {(hut.buildSite != null ? hut.buildSite.GetCurrentLayerIndex() : -1)}, active {hut.IsActive}");
            Log($"Stages:   pieces {Pieces(hut)}; parts {PartOutlines(hut)}");
            Shot($"stages-step{step}");
        }
        yield return 2f;
        if (hut != null)
        {
            Log($"Stages: built: active {hut.IsActive}; pieces {Pieces(hut)}; outlines {LookOutlines(hut)}");
            Shot("stages-built");
        }

        // The placement preview: a local copy of the prefab's view, every stage's model as the finished building.
        var view = template.TryCast<NodeTemplate>()?.nodeStructure?.preview;
        if (view != null)
        {
            var ghost = UnityEngine.Object.Instantiate(view.gameObject, at + forward * -2f, Quaternion.identity);
            ghost.GetComponent<StructurePreview>()?.Setup(null, template);
            yield return 1f;
            Log($"Stages: preview pieces {Pieces(ghost.transform)}, colliders in them {ghost.GetComponentsInChildren<Collider>(true).Count(c => c.transform.name.StartsWith("Mjolnir Model") || c.transform.parent?.name.StartsWith("Mjolnir Model") == true)}");
            UnityEngine.Object.Destroy(ghost);
        }

        try
        {
            hut?.Dismantle(true);
        }
        catch (Exception e)
        {
            Log($"Stages: dismantling failed: {e.Message}");
        }
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The building's pieces (the SDK's objects under its view) and whether each shows.
    private static string Pieces(Structure s) => Pieces(s.transform);

    private static string Pieces(Transform root)
    {
        var appearance = root.GetComponentInChildren<StructureAppearance>(true)?.transform ?? root.GetComponentInChildren<StructurePreview>(true)?.transform;
        if (appearance == null) return "no view";
        var text = new List<string>();
        for (int i = 0; i < appearance.childCount; i++)
        {
            var child = appearance.GetChild(i);
            if (!child.name.StartsWith("Mjolnir Model", StringComparison.Ordinal)) continue;
            var renderer = child.GetComponentInChildren<Renderer>(true);
            text.Add($"'{child.name}' {(child.gameObject.activeInHierarchy ? "on" : "off")} '{renderer?.sharedMaterial?.name}'");
        }
        return string.Join(", ", text);
    }

    // What each build part's pickable outlines, by the pieces its objects stand in.
    private static string PartOutlines(Structure s)
    {
        var layers = s.buildSite?.layers;
        var text = new List<string>();
        for (int l = 0; layers != null && l < layers.Count; l++)
        {
            var parts = layers[l]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var pickable = parts[k]?.GetComponent<Pickable>();
                text.Add($"L{l}.{k} [{Outline(pickable)}]");
            }
        }
        return string.Join(" ", text);
    }

    private static string LookOutlines(Structure s) =>
        string.Join(" ", s.GetComponentsInChildren<Pickable>(true).Where(p => p.GetComponent<BuildPart>() == null && p.pickableObjects?.Count > 0).Select(p => $"{p.name} [{Outline(p)}]"));

    private static string Outline(Pickable? pickable)
    {
        var objects = pickable?.pickableObjects;
        var names = new List<string>();
        for (int i = 0; objects != null && i < objects.Count; i++)
        {
            var o = objects[i];
            if (o == null) continue;
            var piece = o.transform;
            while (piece.parent != null && !piece.name.StartsWith("Mjolnir Model", StringComparison.Ordinal)) piece = piece.parent;
            names.Add(piece.name.StartsWith("Mjolnir Model", StringComparison.Ordinal) ? piece.name["Mjolnir Model".Length..].Trim() : o.name);
        }
        return string.Join(", ", names.Distinct());
    }
}
