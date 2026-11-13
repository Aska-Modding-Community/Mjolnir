using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "construction research" scenario: how the game builds a structure, for the content module's buildings.
/// (A) in the main menu every structure template with its build site: layers, parts, their costs, build volumes and the tool
/// each needs, and what else the prefab carries (shots/construction.txt, the chosen ones in full in construction-detail.txt);
/// (B) in the world the stone lantern and AltarExample's altar placed as the player's builder places them (Settlement.
/// CreateStructure with SpawnedByTool), their state at once, then taken through their stages as the player does (a part's
/// supplies put in, its build volume added half at a time, as hits do): each step logged with the renderers of their looks
/// (material, mesh attributes) and a screenshot. Every
/// structure placed is dismantled at the end. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static readonly Regex ConstructionDetail = new("DecorativeBirch|Lantern|Arch|WaterWell|altarexample", RegexOptions.IgnoreCase);

    private static IEnumerator<float> ConstructionResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var templates = new List<NodeTemplate>();
        var list = db?.CompleteItemInfoList?.itemInfoList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var t = list[i]?.TryCast<NodeTemplate>();
            if (t != null) templates.Add(t);
        }
        var summary = new StringBuilder();
        var detail = new StringBuilder();
        foreach (var t in templates)
        {
            try
            {
                summary.AppendLine(TemplateLine(t));
                if (ConstructionDetail.IsMatch(t.name)) detail.AppendLine(TemplateDetail(t)).AppendLine();
            }
            catch (Exception e)
            {
                summary.AppendLine($"{t.name}: {e.GetType().Name} {e.Message}");
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "construction.txt"), summary.ToString());
        File.WriteAllText(Path.Combine(ShotsDir, "construction-detail.txt"), detail.ToString());
        Log($"Construction: {templates.Count} templates written");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        NodeTemplate? Find(Func<string, bool> name)
        {
            var all = db?.CompleteItemInfoList?.itemInfoList;
            for (int i = 0; all != null && i < all.Count; i++)
            {
                var t = all[i]?.TryCast<NodeTemplate>();
                if (t != null && name(t.name)) return t;
            }
            return null;
        }
        var lantern = Find(n => n.Contains("Lantern", StringComparison.OrdinalIgnoreCase) && n.Contains("Stone", StringComparison.OrdinalIgnoreCase))
                      ?? Find(n => n.Contains("Lantern", StringComparison.OrdinalIgnoreCase));
        var birch = Find(n => n == "Item_Decoration_DecorativeBirch");
        var altar = Find(n => n == "Item_mjolnir.altarexample_altar");
        Log($"Construction: lantern {lantern?.name}, birch {birch?.name}, altar {altar?.name}, settlement {settlement != null}");
        if (settlement == null || lantern == null) yield break;

        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var side = Vector3.Cross(Vector3.up, forward);
        var placed = new List<Structure>();
        Structure? Place(NodeTemplate? t, Vector3 offset)
        {
            if (t == null) return null;
            var at = cam.position + offset;
            at = GroundPoint(at) ?? at;
            var placement = new PointPlacement();
            placement.position = at;
            placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            try
            {
                var s = settlement.CreateStructure(t, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                if (s != null) placed.Add(s);
                Log($"Construction: placed {t.name}: {(s != null ? s.name : "null")}");
                return s;
            }
            catch (Exception e)
            {
                Log($"Construction: placing {t.name} failed: {e}");
                return null;
            }
        }
        var lanternBuilt = Place(lantern, forward * 3f - side * 1.5f);
        var birchBuilt = (Structure?)null;
        var altarBuilt = Place(altar, forward * 3f + side * 2.5f);
        yield return 2f;
        foreach (var s in placed) Log($"Construction: at once: {StructureState(s)}");
        Shot("construction-placed");

        // The lantern and the altar through their stages: each layer's parts supplied, then their build volume added as hits do.
        for (int step = 0; step < 12 && lanternBuilt != null; step++)
        {
            if (altarBuilt != null && altarBuilt.buildSite != null && !altarBuilt.IsActive) Advance(altarBuilt);
            var site = lanternBuilt.buildSite;
            if (site == null || lanternBuilt.IsActive) break;
            var layer = site.GetCurrentLayer();
            var parts = layer?.parts;
            string did = "nothing";
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var part = parts[k];
                if (part == null) continue;
                try
                {
                    if (!part.IsSupplied())
                    {
                        var cost = part.container?.itemConfig?.TryCast<ItemManifestConfig>()?.items;
                        if (cost != null && cost.Count > 0)
                        {
                            site._FillPartWithItems(part, cost);
                            did = $"supplied part {k}";
                        }
                        else did = $"part {k} not supplied, no cost";
                    }
                    else if (!part.IsBuilt() && part.buildInteraction != null)
                    {
                        float delta = Math.Min(part.buildInteraction.TargetBuildVolume / 2, part.buildInteraction.TargetBuildVolume - part.buildInteraction.CurrentBuildVolume);
                        part.buildInteraction.RequestAddBuildVolume(ref delta);
                        did = $"built part {k} (+{delta:0.##})";
                    }
                }
                catch (Exception e)
                {
                    did = $"part {k}: {e.GetType().Name} {e.Message}";
                }
                break;
            }
            yield return 1.5f;
            Log($"Construction: step {step}: {did}; {StructureState(lanternBuilt)}");
            Log($"Construction:   lantern renderers {Renderers(lanternBuilt)}");
            if (altarBuilt != null) Log($"Construction:   altar {StructureState(altarBuilt)}; renderers {Renderers(altarBuilt)}");
            Shot($"construction-step{step}");
        }
        yield return 2f;
        foreach (var s in placed) Log($"Construction: at the end: {StructureState(s)}");
        foreach (var s in placed)
        {
            try
            {
                s.Dismantle(true);
            }
            catch (Exception e)
            {
                Log($"Construction: dismantling failed: {e.Message}");
            }
        }
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // One step of a structure's construction, as the lantern's above.
    private static void Advance(Structure s)
    {
        var site = s.buildSite;
        var part = site?.GetCurrentLayer()?.parts?[0];
        if (site == null || part == null) return;
        if (!part.IsSupplied())
        {
            var cost = part.container?.itemConfig?.TryCast<ItemManifestConfig>()?.items;
            if (cost != null) site._FillPartWithItems(part, cost);
        }
        else if (part.buildInteraction != null)
        {
            float delta = Math.Min(part.buildInteraction.TargetBuildVolume / 2, part.buildInteraction.TargetBuildVolume - part.buildInteraction.CurrentBuildVolume);
            part.buildInteraction.RequestAddBuildVolume(ref delta);
        }
    }

    private static string Renderers(Structure s)
    {
        var appearance = s.GetComponentInChildren<StructureAppearance>(true);
        if (appearance == null) return "no appearance";
        return string.Join("; ", appearance.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.StartsWith("grass") && !r.name.StartsWith("tree_")).Select(r =>
            $"{r.name} {(r.gameObject.activeInHierarchy ? "on" : "off")}{(r.forceRenderingOff ? " forcedOff" : "")}{(r.enabled ? "" : " disabled")} '{r.sharedMaterial?.name}' {(r.GetComponent<SandSailorStudio.Render.MeshMaterialReplacer>() != null ? "replacer" : "")} {MeshInfo(r)}"));
    }

    private static string MeshInfo(Renderer r)
    {
        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return "";
        var attributes = new List<string>();
        foreach (var a in new[] { UnityEngine.Rendering.VertexAttribute.Color, UnityEngine.Rendering.VertexAttribute.TexCoord0, UnityEngine.Rendering.VertexAttribute.TexCoord1,
                                  UnityEngine.Rendering.VertexAttribute.TexCoord2, UnityEngine.Rendering.VertexAttribute.TexCoord3, UnityEngine.Rendering.VertexAttribute.Tangent })
        {
            if (mesh.HasVertexAttribute(a)) attributes.Add($"{a}:{mesh.GetVertexAttributeDimension(a)}");
        }
        string colors = "";
        if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color) && mesh.isReadable)
        {
            var c = mesh.colors;
            if (c.Length > 0) colors = $" color0 {c[0]} colorN {c[c.Length - 1]}";
        }
        return $"[mesh {mesh.name} v{mesh.vertexCount} sub{mesh.subMeshCount} readable {mesh.isReadable} bounds {mesh.bounds.min}..{mesh.bounds.max} {string.Join(",", attributes)}{colors}]";
    }

    private static string TemplateLine(NodeTemplate t)
    {
        var prefab = t.nodeStructure;
        var site = prefab != null ? prefab.GetComponentInChildren<BuildSite>(true) : null;
        var autobuilder = prefab != null ? prefab.GetComponentInChildren<StructureAutobuilder>(true) : null;
        return $"{t.name} | {t.GetIl2CppType().Name} | id {t.id} | category {t.category?.name} | tool {t.placementTool?.GetIl2CppType().Name} | " +
               $"site {(site != null ? $"local {site.localBuildSite}, {Layers(site)}" : "none")} | autobuilder {(autobuilder != null ? autobuilder.structureToBuild?.name ?? "-" : "no")} | " +
               $"unique {t.uniqueBuilding}, upgrades {t.structureUpgrades?.Count ?? 0}, addons {t.structureAddons?.Count ?? 0}, nonDismantlable {t.nonDismantlable}, " +
               $"settlementReq {t.settlementRequirement}, outpostReq {t.outpostRequirement}";
    }

    private static string Layers(BuildSite site)
    {
        var text = new StringBuilder();
        var layers = site.layers;
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            text.Append($"L{i}[");
            var parts = layers[i]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var p = parts[k];
                if (p == null) continue;
                var bi = p.buildInteraction;
                text.Append($"{(k > 0 ? "; " : "")}{p.GetIl2CppType().Name} '{p.name}' cost {{{Cost(p.container?.itemConfig)}}} vol {bi?.buildVolume:0.##} tool {bi?.toolRequirement?.name ?? "-"}");
            }
            text.Append("] ");
        }
        if (site.upgradeLayer?.parts is { Count: > 0 } up) text.Append($"upgrade[{up.Count} part(s)] ");
        return text.ToString().TrimEnd();
    }

    private static string Cost(BaseItemsConfig? config)
    {
        var items = config?.TryCast<ItemManifestConfig>()?.items;
        if (items == null) return config == null ? "" : config.GetIl2CppType().Name;
        var text = new List<string>();
        for (int i = 0; i < items.Count; i++) text.Add($"{items[i].itemInfo?.name} x{items[i].quantity}");
        return string.Join(", ", text);
    }

    private static string TemplateDetail(NodeTemplate t)
    {
        var text = new StringBuilder();
        text.AppendLine($"== {TemplateLine(t)}");
        text.AppendLine($"  ignoreTerrain {t.ignoreTerrainValidation}, ignoreCollision {t.ignoreCollisionValidation}, canChangeOutpost {t.canChangeOutpost}, IndexedName {t.IndexedName}, " +
                        $"buildCostMultiplier {t.buildCostMultiplier}, dismantleParts {t.dismantlePartsConfig?.name}, buildAssist {t.buildAssistStructures?.Count ?? 0}, preview {t.nodePreview?.name}, " +
                        $"conditions {t.blueprintConditionsRules?.Count ?? 0}, trial {t.availableInTrialVersion}");
        var prefab = t.nodeStructure;
        if (prefab == null) return text.ToString();
        foreach (var tr in prefab.GetComponentsInChildren<Transform>(true))
        {
            var comps = tr.GetComponents<Component>().Where(c => c != null && c.TryCast<Transform>() == null).Select(c => c.GetIl2CppType().Name).ToList();
            if (comps.Count == 0) continue;
            int depth = 0;
            for (var p = tr; p != null && p != prefab.transform; p = p.parent) depth++;
            text.AppendLine($"  {new string(' ', depth * 2)}{tr.name} [{LayerMask.LayerToName(tr.gameObject.layer)}] {(tr.gameObject.activeSelf ? "" : "(off) ")}{string.Join(", ", comps)}");
        }
        var site = prefab.GetComponentInChildren<BuildSite>(true);
        if (site != null)
        {
            text.AppendLine($"  site: partPrefab {site.partPrefab?.name}, buildPartConfig {site.buildPartConfig?.name} ({site.buildPartConfig?.subparts?.Count ?? 0} subparts), " +
                            $"constructionMaterial {site.constructionMaterial?.name}, networkPrefab {site.networkPrefab?.name}, deployFx {site.deployFx?.name}, hittable {site.hittable != null}");
            var layers = site.layers;
            for (int i = 0; layers != null && i < layers.Count; i++)
            {
                var parts = layers[i]?.parts;
                for (int k = 0; parts != null && k < parts.Count; k++)
                {
                    var p = parts[k];
                    if (p == null) continue;
                    var bi = p.buildInteraction;
                    text.AppendLine($"  L{i}.{k} {p.name}: cost {{{Cost(p.container?.itemConfig)}}} capacity {p.container?.capacity}, geometry [{ObjectNames(p.geometryObjects)}], completed [{ObjectNames(p.completedObjects)}], " +
                                    $"build {bi?.name} vol {bi?.buildVolume} tool {bi?.toolRequirement?.name} exact {bi?.useExactInteractionPosition}, supply {p.supplyInteraction?.name}, " +
                                    $"workstation {p.workstationInteraction?.name}, structure {p.structureInteraction?.name}, supplyDesc {p.supplyAreaDescription?.name}, buildDesc {p.buildAreaDescription?.name}");
                }
            }
        }
        return text.ToString();
    }

    private static string ObjectNames(Il2CppSystem.Collections.Generic.List<GameObject>? objects)
    {
        var names = new List<string>();
        for (int i = 0; objects != null && i < objects.Count; i++) names.Add(objects[i] == null ? "null" : objects[i].name);
        return string.Join(", ", names);
    }

    private static string StructureState(Structure s)
    {
        if (s == null) return "gone";
        var site = s.buildSite;
        var text = new StringBuilder($"{s.Template?.name}: active {s.IsActive}, site {(site != null ? $"layer {site.GetCurrentLayerIndex()}" : "none")}");
        var layers = site?.layers;
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            var parts = layers[i]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var p = parts[k];
                if (p == null) continue;
                var bi = p.buildInteraction;
                string state;
                try
                {
                    state = $"supplied {p.IsSupplied()} built {p.IsBuilt()} fill {p.container?.GetFillPercentage():0.00} vol {bi?.CurrentBuildVolume:0.##}/{bi?.TargetBuildVolume:0.##} " +
                            $"build {(bi != null ? bi.enabled.ToString() : "-")} supply {(p.supplyInteraction != null ? p.supplyInteraction.enabled.ToString() : "-")} " +
                            $"geometry {Active(p.geometryObjects)} completed {Active(p.completedObjects)} name '{p.GetName()}' desc '{p.GetDescription()}'";
                }
                catch (Exception e)
                {
                    state = e.GetType().Name;
                }
                text.Append($"; L{i}.{k} {state}");
            }
        }
        return text.ToString();
    }

    private static string Active(Il2CppSystem.Collections.Generic.List<GameObject>? objects)
    {
        int on = 0, all = 0;
        for (int i = 0; objects != null && i < objects.Count; i++)
        {
            if (objects[i] == null) continue;
            all++;
            if (objects[i].activeInHierarchy) on++;
        }
        return $"{on}/{all}";
    }
}
