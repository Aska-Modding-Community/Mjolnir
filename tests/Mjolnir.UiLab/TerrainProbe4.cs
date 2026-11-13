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
using BiomeType = AwesomeTechnologies.VegetationSystem.BiomeType;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 4" scenario, the open questions of the first three: a standing tree felled through its harvest
/// interaction blow by blow, the items it leaves on the ground looked for among the world's items; an object moved in the
/// vegetation data and its look brought along (the game's refresh, its contexts taken away and given back, the vegetation
/// cache); a sapling and a rock put into the world as the game drops items, and what they become; a levelling field put down
/// by code with its size set after it stands, then completed; and a biome painted over a large grassy patch, looked at before
/// and after. It changes the ground and the objects of the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe4()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe4: no world");
            yield break;
        }
        var home = character.transform.position;
        static bool IsTree(string n) => n.Contains("tree", StringComparison.OrdinalIgnoreCase) || n.Contains("birch", StringComparison.OrdinalIgnoreCase) || n.Contains("pine", StringComparison.OrdinalIgnoreCase) || n.Contains("spruce", StringComparison.OrdinalIgnoreCase) || n.Contains("oak", StringComparison.OrdinalIgnoreCase);
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(Name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));

        // A spot with standing trees: trees not exhausted, a removable object beside them, no building.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 90f, 120f, 150f, 180f, 210f, 240f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 15)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.IsLoaded(at.x, at.z) || !Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 14, 14), LandscapeOperation.Raise) != LandscapeResult.Done) continue;
                var objects = Biome(at, 9f);
                int standing = objects.Count(o => IsTree(Name(o)) && !o.IsExhausted());
                if (standing >= 2 && objects.Any(o => o.IsRemovable(InstanceDestructionLevel.TERRAFORMING)))
                {
                    spot = at;
                    found = true;
                    Log($"Probe4: spot {P(at)} ({distance} m, {turn}°): {standing} standing trees; {string.Join(", ", objects.Select(o => $"{Name(o)}{(o.IsExhausted() ? " (exhausted)" : "")} data {o.GetHarvestData():X}"))}");
                }
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe4: no spot with standing trees within 240 m");
            yield break;
        }
        foreach (float wait in GoAbove(character, home, spot)) yield return wait;
        var camera = Overhead(spot);
        yield return 2f;
        Shot("probe4-spot");
        yield return AfterShot;

        // A. A standing tree felled blow by blow through its harvest interaction.
        var tree = Biome(spot, 9f).FirstOrDefault(o => IsTree(Name(o)) && !o.IsExhausted() && o.gameObject != null && o.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
        if (tree != null)
        {
            var harvest = tree.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var spawners = tree.gameObject.GetComponentsInChildren<HarvestSpawner>(true).Length;
            var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
            string before = ItemNames(spot, 15f);
            Log($"Probe4: felling '{Name(tree)}' at {P(tree.GetPosition())}: pieces {Try(() => harvest.harvestPieces.Count)}, piece {harvest.GetCurrentPieceIndex()}, hit points {harvest.GetHarvestHitPoints()}/{harvest.GetMaxHarvestHitPoints()} (total left {harvest.GetTotalRemainingHitPoints()}), harvest spawners {spawners}, data {tree.GetHarvestData():X}; items near {before}");
            int blows = 0;
            for (; blows < 40 && !tree.Destroyed && !tree.IsExhausted(); blows++)
            {
                int piece = harvest.GetCurrentPieceIndex();
                string hit = Try(() =>
                {
                    harvest.ForceTakeDamage(new DamageData
                    {
                        damageReceiver = harvest.TryCast<IDamageReceiver>(),
                        damageDealer = dealer,
                        baseDamage = 60,
                        damageMultiplier = 1,
                        damageFlags = DamageFlags.Harvest | DamageFlags.Melee,
                        hitPosition = harvest.transform.position + Vector3.up,
                        hitDirection = (harvest.transform.position - character.transform.position).normalized,
                    });
                    return "";
                });
                yield return 0.3f;
                if (hit.Length > 0 || harvest.GetCurrentPieceIndex() != piece)
                    Log($"Probe4: blow {blows + 1}: {hit} piece {piece} → {harvest.GetCurrentPieceIndex()}, hit points {harvest.GetHarvestHitPoints()}, data {tree.GetHarvestData():X}");
            }
            yield return 6f;
            var fallen = UnityEngine.Object.FindObjectsOfType<TreeFallHelper>().Where(t => t != null && Vector3.Distance(t.transform.position, spot) < 20f).ToList();
            var loose = UnityEngine.Object.FindObjectsOfType<ItemObject>().Where(o => o != null && Vector3.Distance(o.transform.position, spot) < 20f).Select(o => o.name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}").ToList();
            Log($"Probe4: after {blows} blows: destroyed {tree.Destroyed}, exhausted {Try(() => tree.IsExhausted())}, piece {Try(() => harvest != null ? harvest.GetCurrentPieceIndex() : -1)}; falling trees {fallen.Count}; world items near {ItemNames(spot, 15f)} (before {before}); item objects within 20 m [{string.Join(", ", loose)}]");
            Shot("probe4-felled");
            yield return AfterShot;
        }
        else Log("Probe4: no standing tree with a harvest interaction");

        // B. An object moved in its data, and its look brought along.
        var mover = Biome(spot, 9f).FirstOrDefault(o => !IsTree(Name(o)) && o.GetBuffer()?.instances != null);
        if (mover != null)
        {
            string name = Name(mover);
            string Look() => mover.gameObject != null ? mover.gameObject.transform.position.y.ToString("0.00", CultureInfo.InvariantCulture) : "no object";
            var pos = mover.GetPosition();
            Try(() =>
            {
                var positions = mover.GetBuffer().instances.positions;
                var p = positions[mover.Index];
                p.y += 1.5f;
                positions[mover.Index] = p;
                return "";
            });
            Log($"Probe4: '{name}' moved +1.5 m in its data: position {pos.y:0.00} → {mover.GetPosition().y:0.00}, look {Look()}, contexts {mover.GetContexts()}");
            Try(() => { mover.ForceRefreshGameObject(); return ""; });
            yield return 0.5f;
            Log($"Probe4: after ForceRefreshGameObject: look {Look()}");
            int contexts = mover.GetContexts();
            Try(() =>
            {
                foreach (var c in Enum.GetValues<InstanceActivationContext>().Where(c => c != InstanceActivationContext.NONE && c != InstanceActivationContext.ALL && c != InstanceActivationContext.HAS_GAMEOBJECT && (contexts & (int)c) != 0))
                    mover.DeactivateContext(c);
                return "";
            });
            yield return 0.5f;
            string between = Look();
            Try(() =>
            {
                foreach (var c in Enum.GetValues<InstanceActivationContext>().Where(c => c != InstanceActivationContext.NONE && c != InstanceActivationContext.ALL && c != InstanceActivationContext.HAS_GAMEOBJECT && (contexts & (int)c) != 0))
                    mover.ActivateContext(c);
                return "";
            });
            yield return 0.5f;
            Log($"Probe4: contexts {contexts} taken away (look {between}) and given back: look {Look()}, active {mover.Active}");
            Try(() => { VegetationStudioManager.ClearCache(new Bounds(mover.GetPosition(), new Vector3(6, 30, 6))); return ""; });
            yield return 1f;
            Log($"Probe4: after the vegetation cache cleared: look {Look()}");
            Shot("probe4-moved");
            yield return AfterShot;
        }

        // C. A sapling and a rock put into the world as the game drops items.
        var plantAt = spot + new Vector3(-7, 0, 6);
        plantAt.y = Landscape.HeightAt(plantAt.x, plantAt.z);
        var candidates = new List<ItemInfo>();
        var list = db.CompleteItemInfoList?.itemInfoList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var info = list[i];
            if (info == null) continue;
            string n = info.name;
            if ((n.Contains("Sapling", StringComparison.OrdinalIgnoreCase) && n.Contains("Birch", StringComparison.OrdinalIgnoreCase)) ||
                (info.TryCast<BiomeResourceInfo>() != null && (n.Contains("Rock", StringComparison.OrdinalIgnoreCase) || n.Contains("Boulder", StringComparison.OrdinalIgnoreCase))))
                candidates.Add(info);
        }
        Log($"Probe4: items to put down: {string.Join(", ", candidates.Take(12).Select(c => $"{c.name} ({c.GetIl2CppType().Name}, processes {c.processes?.Length ?? 0}, object '{(c.spawnObject != null ? c.spawnObject.name : "none")}')"))}");
        int k = 0;
        foreach (var info in candidates.Where(c => c.name.Contains("Sapling", StringComparison.OrdinalIgnoreCase)).Take(1).Concat(candidates.Where(c => !c.name.Contains("Sapling", StringComparison.OrdinalIgnoreCase)).Take(1)))
        {
            var at = plantAt + new Vector3(3 * k++, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string before = $"{Items(at, 1.5f).Count} items, {Biome(at, 1.5f).Count} objects";
            string spawned = Try(() =>
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.identity;
                var scale = Vector3.one;
                var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? one.name : "null";
            });
            yield return 3f;
            Log($"Probe4: '{info.name}' put down at {P(at)}: {spawned}; near {before} → {Items(at, 1.5f).Count} items ({string.Join(", ", Items(at, 1.5f).Select(Name))}), {Biome(at, 1.5f).Count} objects");
        }
        Shot("probe4-planted");
        yield return AfterShot;

        // D. A levelling field by code: a terraforming placement, its size set once it stands, then completed.
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Resources.FindObjectsOfTypeAll<NodeTemplate>().FirstOrDefault(t => t != null && t.name == "Item_Structures_TerrainLevelField");
        var fieldAt = spot + new Vector3(0, 0, -18);
        if (settlement != null && template != null && Landscape.Check(LandscapeArea.Rectangle(fieldAt + new Vector3(4, 0, 4), 10, 10), LandscapeOperation.Level) == LandscapeResult.Done)
        {
            fieldAt.y = Landscape.HeightAt(fieldAt.x, fieldAt.z);
            Structure? field = null;
            string created = Try(() =>
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
                placement.isDirty = true;
                field = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                return field != null ? field.name : "null";
            });
            yield return 2f;
            var building = field != null ? field.GetComponentInChildren<DynamicDimensionBuilding>(true) : null;
            string first = Try(() => $"area {building!.WorldArea}, tile {building.TileSize}, grid {building.GridSize}");
            string sized = Try(() => { building!.SetDimensions(2f, new Vector3Int(4, 1, 4), true); return $"area {building.WorldArea}, tile {building.TileSize}, grid {building.GridSize}"; });
            yield return 2f;
            var grid = field != null ? field.GetComponentInChildren<TerraformingGrid>(true) : null;
            Try(() => { grid!.RefreshCellsHeights(false, true); return ""; });
            Log($"Probe4: field {created}: as made {first}; sized {sized}; cells {Try(() => grid!.GetCellsCount())}, unlevelled {Try(() => grid!.GetUnleveledTile())}, reference {Try(() => grid!.ReferenceLevel)}, difference {Try(() => grid!.LevelDifference)}");
            Shot("probe4-field");
            yield return AfterShot;
            if (grid?._state is { } state)
            {
                var centre = fieldAt + new Vector3(4, 0, 4);
                var before = Profile(centre);
                string done = Try(() => { state.Rpc_DebugCompleteField(); return "completed"; });
                yield return 3f;
                Log($"Probe4: field {done}: Δ across x {Profile(centre, before)}; kind {Landscape.KindAt(centre.x, centre.z)}; still there {field != null}");
                Shot("probe4-field-done");
                yield return AfterShot;
            }
        }
        else Log("Probe4: no field placed");

        // E. A biome over a large grassy patch, looked at before and after.
        var patch = spot + new Vector3(16, 0, 0);
        patch.y = Landscape.HeightAt(patch.x, patch.z);
        camera.Position = patch + new Vector3(0, 26f, -12f);
        camera.Rotation = Quaternion.LookRotation(patch - camera.Position, Vector3.up);
        yield return 1.5f;
        Shot("probe4-biome-before");
        yield return AfterShot;
        var tool = ProbeTool();
        tool.setBiomeType = true;
        tool.biomeType = BiomeType.Swamp;
        tool.setTerrainType = false;
        tool.clearVegetation = false;
        tool.setVegetationMask = false;
        tool.requiresTerrainType = false;
        tool.checkTerrainSlope = false;
        tool.checkItemInstances = false;
        tool.validateHeight = false;
        string painted = Try(() => tool.RunOnArea(TerraformingToolOperation.PAINT, 20, 20, 2, patch, 0).ToString());
        yield return 2f;
        Try(() => { VegetationStudioManager.RefreshTerrainArea(new Bounds(patch, new Vector3(30, 60, 30))); return ""; });
        yield return 4f;
        Log($"Probe4: biome Swamp over 20 × 20 m: {painted}");
        Shot("probe4-biome-after");
        yield return AfterShot;

        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
