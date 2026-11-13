using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using VegetationStudioManager = AwesomeTechnologies.VegetationStudio.VegetationStudioManager;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 5" scenario: a levelling field put down by code levelled cell by cell as the hoe levels it, then
/// turned into its result; a standing tree (its name starting with tree_, not exhausted, its harvest interaction near the
/// player) felled blow by blow with the items it leaves looked for; an object that has a look of its own moved in its data
/// and its look refreshed; and the world's resources that are rocks, put down as the game drops items. It changes the ground
/// and the objects of the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe5()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe5: no world");
            yield break;
        }
        var home = character.transform.position;
        static bool IsTree(string n) => n.StartsWith("tree_", StringComparison.OrdinalIgnoreCase);
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(Name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));

        // A spot with standing trees, no building.
        var spots = new List<Vector3>();
        foreach (float distance in new[] { 60f, 90f, 120f, 150f, 180f, 210f, 240f })
            for (int turn = 0; turn < 360; turn += 15)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.IsLoaded(at.x, at.z) || !Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 14, 14), LandscapeOperation.Raise) != LandscapeResult.Done) continue;
                if (Biome(at, 9f).Count(o => IsTree(Name(o)) && !o.IsExhausted()) >= 1) spots.Add(at);
            }
        Log($"Probe5: {spots.Count} spots with standing trees");
        if (spots.Count == 0) yield break;
        var spot = spots[0];
        foreach (float wait in GoAbove(character, home, spot)) yield return wait;
        var camera = Overhead(spot);
        yield return 2f;
        var near = Biome(spot, 9f);
        Log($"Probe5: at {P(spot)}: {string.Join("; ", near.Select(o => $"{Name(o)}{(o.IsExhausted() ? " exhausted" : "")} data {o.GetHarvestData():X} object '{(o.gameObject != null ? o.gameObject.name : "none")}' harvest {(o.gameObject != null && o.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null)} spawners {(o.gameObject != null ? o.gameObject.GetComponentsInChildren<HarvestSpawner>(true).Length : 0)}"))}");
        Shot("probe5-spot");
        yield return AfterShot;

        // A. A standing tree felled blow by blow.
        var tree = near.FirstOrDefault(o => IsTree(Name(o)) && !o.IsExhausted() && o.gameObject != null && o.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
        if (tree != null)
        {
            var harvest = tree.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
            string before = ItemNames(spot, 20f);
            Log($"Probe5: felling '{Name(tree)}': pieces {Try(() => harvest.harvestPieces.Count)}, piece {harvest.GetCurrentPieceIndex()}, hit points {harvest.GetHarvestHitPoints()}/{harvest.GetMaxHarvestHitPoints()}, total left {harvest.GetTotalRemainingHitPoints()}; items near {before}");
            int blows = 0;
            for (; blows < 60 && !tree.Destroyed && !tree.IsExhausted(); blows++)
            {
                int piece = harvest != null ? harvest.GetCurrentPieceIndex() : -1;
                string error = Try(() =>
                {
                    harvest!.ForceTakeDamage(new DamageData
                    {
                        damageReceiver = harvest.TryCast<IDamageReceiver>(),
                        damageDealer = dealer,
                        baseDamage = 40,
                        damageMultiplier = 1,
                        damageFlags = DamageFlags.Harvest | DamageFlags.Melee,
                        hitPosition = harvest.transform.position + Vector3.up,
                        hitDirection = (harvest.transform.position - character.transform.position).normalized,
                    });
                    return "";
                });
                yield return 0.4f;
                if (harvest == null) break;
                if (error.Length > 0 || harvest.GetCurrentPieceIndex() != piece)
                    Log($"Probe5: blow {blows + 1}: {error} piece {piece} → {harvest.GetCurrentPieceIndex()}, hit points {harvest.GetHarvestHitPoints()}, items near {ItemNames(spot, 20f)}");
            }
            yield return 6f;
            var falling = UnityEngine.Object.FindObjectsOfType<TreeFallHelper>().Count(t => t != null && Vector3.Distance(t.transform.position, spot) < 25f);
            var objects = UnityEngine.Object.FindObjectsOfType<ItemObject>().Where(o => o != null && Vector3.Distance(o.transform.position, spot) < 25f).Select(o => o.name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}");
            Log($"Probe5: after {blows} blows: destroyed {tree.Destroyed}, exhausted {Try(() => tree.IsExhausted())}; falling trees {falling}; items near {ItemNames(spot, 20f)} (before {before}); item objects [{string.Join(", ", objects)}]");
            Shot("probe5-felled");
            yield return AfterShot;
        }
        else Log("Probe5: no standing tree with a harvest interaction near");

        // B. An object with a look of its own moved in its data.
        var mover = Biome(spot, 9f).FirstOrDefault(o => !IsTree(Name(o)) && o.gameObject != null && o.GetBuffer()?.instances != null);
        if (mover != null)
        {
            string Look() => mover.gameObject != null ? mover.gameObject.transform.position.y.ToString("0.00", CultureInfo.InvariantCulture) : "no object";
            float y = mover.GetPosition().y;
            Try(() => { var ps = mover.GetBuffer().instances.positions; var p = ps[mover.Index]; p.y += 1.5f; ps[mover.Index] = p; return ""; });
            Log($"Probe5: '{Name(mover)}' moved +1.5 m: data {y:0.00} → {mover.GetPosition().y:0.00}, look {Look()}");
            Try(() => { mover.ForceRefreshGameObject(); return ""; });
            yield return 0.5f;
            Log($"Probe5: after ForceRefreshGameObject: look {Look()}");
            int contexts = mover.GetContexts();
            var each = Enum.GetValues<InstanceActivationContext>().Where(c => c != InstanceActivationContext.NONE && c != InstanceActivationContext.ALL && c != InstanceActivationContext.HAS_GAMEOBJECT && (contexts & (int)c) != 0).ToList();
            Try(() => { foreach (var c in each) mover.DeactivateContext(c); return ""; });
            yield return 0.5f;
            string between = Look();
            Try(() => { foreach (var c in each) mover.ActivateContext(c); return ""; });
            yield return 1f;
            Log($"Probe5: contexts [{string.Join(", ", each)}] taken away (look {between}) and given back: look {Look()}");
            if (mover.gameObject != null && Math.Abs(mover.gameObject.transform.position.y - mover.GetPosition().y) > 0.5f)
            {
                var t = mover.gameObject.transform;
                var p = t.position;
                p.y = mover.GetPosition().y;
                t.position = p;
                Log($"Probe5: its look moved by hand to {Look()}");
            }
            Try(() => { VegetationStudioManager.ClearCache(new Bounds(mover.GetPosition(), new Vector3(6, 30, 6))); return ""; });
            yield return 1f;
            Shot("probe5-moved");
            yield return AfterShot;
        }

        // C. A levelling field by code, levelled cell by cell, then turned into its result.
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Resources.FindObjectsOfTypeAll<NodeTemplate>().FirstOrDefault(t => t != null && t.name == "Item_Structures_TerrainLevelField");
        Vector3 fieldAt = default;
        bool placeFound = false;
        foreach (var s in spots.Skip(1).Prepend(spot))
            foreach (var offset in new[] { new Vector3(0, 0, -18), new Vector3(-18, 0, 0), new Vector3(18, 0, 0), new Vector3(0, 0, 18) })
                if (!placeFound && Landscape.Check(LandscapeArea.Rectangle(s + offset + new Vector3(4, 0, 4), 10, 10), LandscapeOperation.Level) == LandscapeResult.Done)
                {
                    fieldAt = s + offset;
                    placeFound = true;
                }
        if (settlement != null && template != null && placeFound)
        {
            fieldAt.y = Landscape.HeightAt(fieldAt.x, fieldAt.z);
            camera.Position = fieldAt + new Vector3(4, 22, -8);
            camera.Rotation = Quaternion.LookRotation(fieldAt + new Vector3(4, 0, 4) - camera.Position, Vector3.up);
            Structure? field = null;
            Try(() =>
            {
                var placement = new TerraformingPlacement();
                var a = new PointPlacement();
                a.position = fieldAt;
                a.rotation = Quaternion.identity;
                var b = new PointPlacement();
                b.position = fieldAt + new Vector3(8, 0, 8);
                b.rotation = Quaternion.identity;
                placement.points = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PointPlacement>(new[] { a, b });
                placement.gridSize = new Vector3Int(4, 1, 4);
                placement.tileSize = 2f;
                field = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                return "";
            });
            yield return 2f;
            var building = field != null ? field.GetComponentInChildren<DynamicDimensionBuilding>(true) : null;
            Try(() => { building!.SetDimensions(2f, new Vector3Int(4, 1, 4), true); return ""; });
            yield return 2f;
            var grid = field != null ? field.GetComponentInChildren<TerraformingGrid>(true) : null;
            Try(() => { grid!.RefreshCellsHeights(false, true); return ""; });
            yield return 1f;
            var centre = fieldAt + new Vector3(4, 0, 4);
            var before = Profile(centre);
            Log($"Probe5: field: cells {Try(() => grid!.GetCellsCount())}, reference {Try(() => grid!.ReferenceLevel)}, difference {Try(() => grid!.LevelDifference)}, interaction {grid?.terraformingInteraction != null}");
            Shot("probe5-field");
            yield return AfterShot;
            if (grid != null)
            {
                int cells = grid.GetCellsCount();
                string levelled = Try(() =>
                {
                    int ok = 0;
                    for (int i = 0; i < cells; i++)
                        if (grid.terraformingInteraction != null ? grid.terraformingInteraction.TryLevelTile(i) : TryGrid(grid, i)) ok++;
                    return $"{ok} of {cells} tiles";
                });
                yield return 2f;
                Try(() => { grid.RefreshCellsHeights(false, true); return ""; });
                int levelledCells = Enumerable.Range(0, cells).Count(i => grid.IsCellLeveled(i));
                Log($"Probe5: field levelled tile by tile ({levelled}): {levelledCells} of {cells} levelled, difference {Try(() => grid.LevelDifference)}; Δ across x {Profile(centre, before)}");
                Shot("probe5-field-levelled");
                yield return AfterShot;
                string done = Try(() => { grid._state!.Rpc_DebugCompleteField(); return "completed"; });
                yield return 3f;
                Log($"Probe5: field {done}: the field there {field != null && field.gameObject != null}, kind {Landscape.KindAt(centre.x, centre.z)}");
            }
        }
        else Log("Probe5: no field placed");

        // D. The world's resources that are rocks.
        var rocks = new List<ItemInfo>();
        var list = db.CompleteItemInfoList?.itemInfoList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var info = list[i];
            if (info?.TryCast<BiomeResourceInfo>() == null) continue;
            rocks.Add(info);
        }
        Log($"Probe5: biome resources ({rocks.Count}): {string.Join(", ", rocks.Select(r => r.name))}");
        var stone = rocks.FirstOrDefault(r => r.name.Contains("Stone", StringComparison.OrdinalIgnoreCase) || r.name.Contains("Rock", StringComparison.OrdinalIgnoreCase) || r.name.Contains("Boulder", StringComparison.OrdinalIgnoreCase));
        if (stone != null)
        {
            var at = spot + new Vector3(-6, 0, 6);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned = Try(() =>
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.identity;
                var scale = Vector3.one;
                var one = stone.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? one.name : "null";
            });
            yield return 3f;
            Log($"Probe5: '{stone.name}' put down at {P(at)}: {spawned}; items there {ItemNames(at, 2f)}, objects {Biome(at, 2f).Count}");
            Shot("probe5-rock");
            yield return AfterShot;
        }

        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static bool TryGrid(TerraformingGrid grid, int i)
    {
        grid.TryLevelTile(i);
        return true;
    }
}
