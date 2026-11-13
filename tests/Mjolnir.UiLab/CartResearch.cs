using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "cart research" scenario: structures built in several stages and the game's cart, for the content module's buildings.
/// (A) in the main menu every template of two or more build layers with each part's cost and its looks by name (the objects
/// shown while the part is built and once it is: shots/stages.txt), and the cart and the sled in full (shots/cart-detail.txt:
/// the prefab's objects and components, the cart's handles, mount, wheels, storage);
/// (B) in the world the game's cart and UiLab's lab_cart (a mod building on the cart, no model of its own) placed in front of
/// the player and taken through their stages as the player does (supplies, then hammer work), each step logged with the
/// objects of every part's looks that show, and a screenshot;
/// (C) once built, both carts' storage and harness: the player harnessed through the game's own interaction, a screenshot,
/// unharnessed. Both are dismantled at the end. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private const string CartAsset = "Item_Bushcraft_Cart";
    private const string LabCartAsset = "Item_mjolnir.test.uilab_lab_cart";

    private static IEnumerator<float> CartResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var stages = new StringBuilder();
        var detail = new StringBuilder();
        int all = 0, several = 0, distinct = 0;
        var list = db?.CompleteItemInfoList?.itemInfoList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var t = list[i]?.TryCast<NodeTemplate>();
            if (t == null) continue;
            all++;
            try
            {
                var site = t.nodeStructure != null ? t.nodeStructure.GetComponentInChildren<BuildSite>(true) : null;
                int layers = site?.layers?.Count ?? 0;
                if (t.name is CartAsset or "Item_Bushcraft_Sled") detail.AppendLine(TemplateDetail(t)).AppendLine(VehicleDetail(t)).AppendLine();
                if (layers < 2) continue;
                several++;
                if (LayersLookApart(site!)) distinct++;
                stages.AppendLine($"== {t.name} ({layers} layers, {t.category?.name})");
                for (int l = 0; l < layers; l++)
                {
                    var parts = site!.layers[l]?.parts;
                    for (int k = 0; parts != null && k < parts.Count; k++)
                    {
                        var p = parts[k];
                        if (p == null) continue;
                        stages.AppendLine($"  L{l}.{k} {p.name}: cost {{{Cost(p.container?.itemConfig)}}} vol {p.buildInteraction?.buildVolume:0.##} " +
                                          $"geometry [{ObjectNames(p.geometryObjects)}] completed [{ObjectNames(p.completedObjects)}]");
                    }
                }
            }
            catch (Exception e)
            {
                stages.AppendLine($"{t.name}: {e.GetType().Name} {e.Message}");
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "stages.txt"), stages.ToString());
        File.WriteAllText(Path.Combine(ShotsDir, "cart-detail.txt"), detail.ToString());
        Log($"Cart: {all} templates, {several} with two or more layers, {distinct} of them with looks of their own per layer");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var agent = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        var cartTemplate = db?.GetItemInfo(CartAsset)?.TryCast<StructureTemplate>();
        var labTemplate = db?.GetItemInfo(LabCartAsset)?.TryCast<StructureTemplate>();
        Log($"Cart: settlement {settlement != null}, agent {agent != null}, cart {cartTemplate?.name}, lab cart {labTemplate?.name}");
        if (settlement == null || cartTemplate == null) yield break;

        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var side = Vector3.Cross(Vector3.up, forward);
        var placed = new List<Structure>();
        Structure? Place(StructureTemplate? t, Vector3 offset)
        {
            if (t == null) return null;
            var at = cam.position + offset;
            at = GroundPoint(at) ?? at;
            var placement = new PointPlacement();
            placement.position = at;
            placement.rotation = Quaternion.LookRotation(side, Vector3.up);
            try
            {
                var s = settlement.CreateStructure(t, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                if (s != null) placed.Add(s);
                Log($"Cart: placed {t.name}: {(s != null ? $"{s.name} ({s.GetIl2CppType().Name})" : "null")}");
                return s;
            }
            catch (Exception e)
            {
                Log($"Cart: placing {t.name} failed: {e}");
                return null;
            }
        }
        var cart = Place(cartTemplate, forward * 4.5f - side * 1.8f);
        var lab = Place(labTemplate, forward * 4.5f + side * 1.8f);
        // A game structure of three stages (the builder's hut) and UiLab's lab_hut on it, further on.
        var hut = Place(db?.GetItemInfo("Item_Structures_BuilderHutL1")?.TryCast<StructureTemplate>(), forward * 14f - side * 6f);
        var labHut = Place(db?.GetItemInfo(HutAsset)?.TryCast<StructureTemplate>(), forward * 14f + side * 6f);
        yield return 2f;
        foreach (var s in placed) Log($"Cart: at once: {StructureState(s)}; showing {Showing(s)}");
        // The lab cart shares its base's cost asset, which the game's fill empties: it is finished at once, as the game's
        // autobuilder finishes a structure, so its harness can be tried.
        try
        {
            lab?.buildSite?.FinishBuild(-1);
        }
        catch (Exception e)
        {
            Log($"Cart: finishing the lab cart failed: {e.Message}");
        }
        Shot("cart-placed");

        for (int step = 0; step < 30 && placed.Any(s => s != null && !s.IsActive); step++)
        {
            foreach (var s in placed)
            {
                if (s == null || s.IsActive) continue;
                string did = AdvanceStep(s);
                Log($"Cart: step {step} {s.Template?.name}: {did}");
            }
            yield return 1.5f;
            foreach (var s in placed)
            {
                Log($"Cart:   {StructureState(s)}");
                Log($"Cart:     showing {Showing(s)}");
            }
            Shot($"cart-step{step}");
        }
        yield return 2f;
        foreach (var s in placed) Log($"Cart: built: {StructureState(s)}; {VehicleState(s)}");

        // The harness, as the player's own key does it: the vehicle's interaction used by the player.
        foreach (var s in new[] { cart, lab }.Where(s => s != null).Select(s => s!))
        {
            var vehicle = s.TryCast<VehicleStructure>();
            var interaction = vehicle?.VehicleInteraction;
            var mount = s.GetComponentInChildren<CartMount>(true);
            if (agent == null || interaction == null)
            {
                Log($"Cart: {s.name}: no vehicle interaction ({vehicle != null})");
                continue;
            }
            bool used = false;
            try
            {
                used = agent.TryRemoteInteract(interaction);
            }
            catch (Exception e)
            {
                Log($"Cart: {s.name}: harness threw {e.GetType().Name} {e.Message}");
            }
            yield return 2f;
            Log($"Cart: {s.name}: harness {used}, in use {vehicle!.InUse}, mounted {Describe(() => mount?._mountedPlayer != null)}, sessions {interaction.ActiveSessionsCount}");
            Shot($"cart-harness-{(s == cart ? "game" : "lab")}");
            var before = s.transform.position;
            yield return 1f;
            Log($"Cart: {s.name}: moved {Vector3.Distance(before, s.transform.position):0.00} m standing");
            try
            {
                mount?.Dismount();
            }
            catch (Exception e)
            {
                Log($"Cart: {s.name}: dismount threw {e.GetType().Name} {e.Message}");
            }
            yield return 2f;
            Log($"Cart: {s.name}: after dismount in use {vehicle.InUse}, sessions {interaction.ActiveSessionsCount}");
        }

        foreach (var s in placed)
        {
            try
            {
                s.Dismantle(true);
            }
            catch (Exception e)
            {
                Log($"Cart: dismantling failed: {e.Message}");
            }
        }
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Describe(Func<bool> read)
    {
        try
        {
            return read().ToString();
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    // One step of a structure's construction: the first part of the current layer that is not supplied gets its supplies,
    // else the first one not built gets half its build volume, as hits do.
    private static string AdvanceStep(Structure s)
    {
        var site = s.buildSite;
        var parts = site?.GetCurrentLayer()?.parts;
        for (int k = 0; parts != null && k < parts.Count; k++)
        {
            var part = parts[k];
            if (part == null) continue;
            try
            {
                if (!part.IsSupplied())
                {
                    var cost = part.container?.itemConfig?.TryCast<ItemManifestConfig>()?.items;
                    if (cost == null || cost.Count == 0) continue;
                    // A copy: the game empties the list it is given, and a building without a cost of its own shares its base's.
                    var copy = new Il2CppSystem.Collections.Generic.List<ItemInfoQuantity>();
                    for (int i = 0; i < cost.Count; i++) copy.Add(cost[i]);
                    site!._FillPartWithItems(part, copy);
                    return $"supplied L{site.GetCurrentLayerIndex()}.{k}";
                }
                if (!part.IsBuilt() && part.buildInteraction != null)
                {
                    float delta = Math.Min(part.buildInteraction.TargetBuildVolume / 2, part.buildInteraction.TargetBuildVolume - part.buildInteraction.CurrentBuildVolume);
                    part.buildInteraction.RequestAddBuildVolume(ref delta);
                    return $"built L{site!.GetCurrentLayerIndex()}.{k} (+{delta:0.##})";
                }
            }
            catch (Exception e)
            {
                return $"part {k}: {e.GetType().Name} {e.Message}";
            }
        }
        return "nothing";
    }

    // Whether the layers show different objects: each layer's parts have looks, and no two layers share them all.
    private static bool LayersLookApart(BuildSite site)
    {
        var looks = new List<string>();
        for (int l = 0; l < site.layers.Count; l++)
        {
            var names = new List<string>();
            var parts = site.layers[l]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
                if (parts[k] != null) names.Add(ObjectNames(parts[k].geometryObjects) + "|" + ObjectNames(parts[k].completedObjects));
            looks.Add(string.Join(";", names));
        }
        return looks.All(l => l.Trim('|', ';').Length > 0) && looks.Distinct().Count() == looks.Count;
    }

    // Every part's look objects that show now, by name.
    private static string Showing(Structure s)
    {
        var site = s.buildSite;
        if (site == null) return "no site";
        var text = new List<string>();
        for (int l = 0; site.layers != null && l < site.layers.Count; l++)
        {
            var parts = site.layers[l]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var p = parts[k];
                if (p == null) continue;
                text.Add($"L{l}.{k} geometry [{ActiveNames(p.geometryObjects)}] completed [{ActiveNames(p.completedObjects)}]");
            }
        }
        return string.Join("; ", text);
    }

    private static string ActiveNames(Il2CppSystem.Collections.Generic.List<GameObject>? objects)
    {
        var names = new List<string>();
        for (int i = 0; objects != null && i < objects.Count; i++)
        {
            var o = objects[i];
            if (o == null || !o.activeInHierarchy) continue;
            var r = o.GetComponent<Renderer>();
            names.Add(r != null ? $"{o.name} '{r.sharedMaterial?.name}'" : o.name);
        }
        return string.Join(", ", names);
    }

    // The cart's parts that make it a vehicle, on its template's prefab.
    private static string VehicleDetail(NodeTemplate t)
    {
        var prefab = t.nodeStructure;
        if (prefab == null) return "  no prefab";
        var text = new StringBuilder($"  vehicle: structure {prefab.GetIl2CppType().Name}");
        var root = prefab.transform;
        string At(Transform? x) => x == null ? "-" : $"{x.name} {root.InverseTransformPoint(x.position)}";
        var cart = prefab.TryCast<CartStructure>();
        if (cart != null)
            text.Append($", friction {cart.extraLateralFriction}/{cart.extraForwardFriction}, kinematic after {cart.setKinematicAfterSecondsInactive} s");
        var interaction = prefab.GetComponentInChildren<CartInteraction>(true);
        if (interaction != null)
            text.Append($"\n  interaction {interaction.name}: right {At(interaction.rightHandle)}, " +
                        $"left {At(interaction.leftHandle)}, anchor {interaction.anchorPoint}, hold {interaction.holdOffset}, spring {interaction.jointSpring}, " +
                        $"server driver {At(interaction.serverDriverLocation)}, match {At(interaction.matchTargetPos)}, rigidbody {interaction.cartRigidbody?.name} mass {interaction.cartRigidbody?.mass}");
        var mount = prefab.GetComponentInChildren<CartMount>(true);
        if (mount != null)
        {
            var animator = mount._animator;
            text.Append($"\n  mount {mount.name}: animator {animator?.name} controller {animator?.runtimeAnimatorController?.name} avatar {animator?.avatar?.name}, " +
                        $"stationary mass {mount._stationaryMass}, speed cost {mount._speedCostEmpty}..{mount._speedCostFull}, storage {mount._resourceStorage?.name}");
        }
        foreach (var storage in prefab.GetComponentsInChildren<ResourceStorage>(true))
            text.Append($"\n  storage {storage.name} ({storage.GetIl2CppType().Name})");
        foreach (var inv in prefab.GetComponentsInChildren<SandSailorStudio.Inventory.InventoryComponent>(true))
            text.Append($"\n  inventory {inv.name}: {inv.GetIl2CppType().Name}");
        foreach (var x in prefab.GetComponentsInChildren<Transform>(true).Where(x => x.name.Contains("wheel", StringComparison.OrdinalIgnoreCase) || x.name.Contains("handle", StringComparison.OrdinalIgnoreCase)))
            text.Append($"\n  {At(x)} renderer {x.GetComponent<Renderer>()?.GetIl2CppType().Name ?? "-"}");
        return text.ToString();
    }

    private static string VehicleState(Structure s)
    {
        var vehicle = s.TryCast<VehicleStructure>();
        if (vehicle == null) return $"no vehicle ({s.GetIl2CppType().Name})";
        string inventory = Describe(() => vehicle.Inventory != null);
        return $"{s.GetIl2CppType().Name}: drivable {vehicle.Drivable}, inventory {inventory}, storage {vehicle.StorageInteraction?.name}, vehicle interaction {vehicle.VehicleInteraction?.name} " +
               $"({vehicle.VehicleInteraction?.GetIl2CppType().Name}), mount {s.GetComponentInChildren<CartMount>(true) != null}, rigidbody {s.GetComponent<Rigidbody>()?.isKinematic}";
    }
}
