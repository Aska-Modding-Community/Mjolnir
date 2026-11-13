using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using VegetationStudioManager = AwesomeTechnologies.VegetationStudio.VegetationStudioManager;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 3" scenario: the world's trees and rocks on a wooded spot inland. A removable object removed as the
/// game's tool removes it, and one that is not at the system's level; a tree felled through its harvest interaction as a
/// blow fells it (the drops and the fall looked for); an object moved by writing its position in the vegetation data and its
/// look refreshed, then set on the ground raised under it; a tree added through the vegetation system and looked at; and a
/// levelling field of the game put down by code with its size set, then completed. It changes the ground and the objects of
/// the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe3()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe3: no world");
            yield break;
        }
        var home = character.transform.position;
        static bool IsTree(string name) => name.Contains("tree", StringComparison.OrdinalIgnoreCase) || name.Contains("birch", StringComparison.OrdinalIgnoreCase) ||
                                           name.Contains("pine", StringComparison.OrdinalIgnoreCase) || name.Contains("spruce", StringComparison.OrdinalIgnoreCase) ||
                                           name.Contains("oak", StringComparison.OrdinalIgnoreCase) || name.Contains("fir", StringComparison.OrdinalIgnoreCase);
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();

        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f, 200f, 240f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 15)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.IsLoaded(at.x, at.z) || !Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 12, 12), LandscapeOperation.Raise) != LandscapeResult.Done) continue;
                var objects = Biome(at, 8f);
                if (objects.Count(o => IsTree(Name(o))) >= 2)
                {
                    spot = at;
                    found = true;
                    Log($"Probe3: wooded spot {P(at)} ({distance} m, {turn}°): {objects.Count} objects: {string.Join(", ", objects.Select(Name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"))}");
                }
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe3: no wooded spot within 240 m");
            yield break;
        }
        foreach (float wait in GoAbove(character, home, spot)) yield return wait;
        var camera = Overhead(spot);
        yield return 2f;
        var all = Biome(spot, 8f);
        Log($"Probe3: near: {string.Join("; ", all.Select(Describe))}");
        Log($"Probe3: their game objects: {string.Join("; ", all.Select(o => $"'{Name(o)}' active {o.Active}, object '{(o.gameObject != null ? o.gameObject.name : "none")}', harvest {(o.gameObject != null && o.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null)}, index {o.Index}"))}");
        Shot("probe3-wooded");
        yield return AfterShot;

        // Removal: a removable object at the terraforming level, a non-removable one at the system's.
        var removable = all.FirstOrDefault(o => !IsTree(Name(o)) && o.IsRemovable(InstanceDestructionLevel.TERRAFORMING));
        foreach (var (target, level) in new[] { (removable, InstanceDestructionLevel.TERRAFORMING), (all.FirstOrDefault(o => o != removable && !o.IsRemovable(InstanceDestructionLevel.TERRAFORMING) && !IsTree(Name(o))), InstanceDestructionLevel.SYSTEM) })
        {
            if (target == null) continue;
            string name = Name(target);
            bool silent = false;
            var l = level;
            string result = Try(() => { target.Destroy(ref silent, ref l); return $"destroyed {target.Destroyed}"; });
            yield return 1.5f;
            Log($"Probe3: '{name}' destroyed at {level}: {result}; objects now {Biome(spot, 8f).Count}");
        }
        Shot("probe3-removed");
        yield return AfterShot;

        // Moving an object by its vegetation data: up 1 m.
        var mover = Biome(spot, 8f).FirstOrDefault(o => o.GetBuffer()?.instances != null);
        if (mover != null)
        {
            string name = Name(mover);
            var before = mover.GetPosition();
            string moved = Try(() =>
            {
                var positions = mover.GetBuffer().instances.positions;
                int index = mover.Index;
                var p = positions[index];
                p.y += 1f;
                positions[index] = p;
                mover.ForceRefreshGameObject();
                return $"index {index} of {positions.Length}";
            });
            yield return 1.5f;
            var after = mover.GetPosition();
            Log(string.Create(CultureInfo.InvariantCulture, $"Probe3: '{name}' moved by its data ({moved}): y {before.y:0.00} → {after.y:0.00}; object {(mover.gameObject != null ? mover.gameObject.transform.position.y.ToString("0.00", CultureInfo.InvariantCulture) : "none")}"));
            Try(() => { VegetationStudioManager.ClearCache(new Bounds(after, new Vector3(4, 20, 4))); return ""; });
            yield return 1.5f;
            Shot("probe3-moved");
            yield return AfterShot;
        }

        // A tree felled through its harvest interaction, as a blow of the player's fells it.
        var tree = Biome(spot, 8f).FirstOrDefault(o => IsTree(Name(o)) && o.gameObject != null && o.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
        if (tree != null)
        {
            string name = Name(tree);
            var harvest = tree.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
            string drops = Drops(spot);
            string hits = Try(() =>
            {
                float total = harvest.GetTotalRemainingHitPoints();
                int n = 0;
                for (; n < 60 && !tree.Destroyed && harvest.GetTotalRemainingHitPoints() > 0; n++)
                    harvest.ForceTakeDamage(new DamageData
                    {
                        damageReceiver = harvest.TryCast<IDamageReceiver>(),
                        damageDealer = dealer,
                        baseDamage = 500,
                        damageMultiplier = 1,
                        damageFlags = DamageFlags.Harvest | DamageFlags.IgnoreHarvestTool,
                        hitPosition = harvest.transform.position + Vector3.up,
                        hitDirection = harvest.transform.position - character.transform.position,
                    });
                return $"{n} blows, hit points {total} → {harvest.GetTotalRemainingHitPoints()}, piece {harvest.GetCurrentPieceIndex()}";
            });
            yield return 6f;
            Log($"Probe3: '{name}' felled through its harvest: {hits}; destroyed {tree.Destroyed}, exhausted {Try(() => tree.IsExhausted())}; drops {drops} → {Drops(spot)}");
            Shot("probe3-felled");
            yield return AfterShot;
        }
        else Log("Probe3: no tree with a harvest interaction near");

        // Raised under the rest, then whether anything sets them on the new ground.
        var rest = Biome(spot, 8f).Select(o => (Instance: (WorldItemInstance)o, Pos: o.GetPosition())).ToList();
        Landscape.Raise(LandscapeArea.Rectangle(spot, 12, 12, 0, 2), 1f, new LandscapeOptions { ClearVegetation = false });
        yield return 2f;
        Log($"Probe3: raised 1 m under {rest.Count} objects: {Seated(rest)}");
        Shot("probe3-raised");
        yield return AfterShot;

        // A tree added through the vegetation system.
        var model = Biome(spot, 10f).FirstOrDefault(o => IsTree(Name(o)) && o.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo() != null);
        string? id = model?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.VegetationItemID;
        if (id != null)
        {
            var place = spot + new Vector3(-6, 0, 4);
            place.y = Landscape.HeightAt(place.x, place.z);
            string added = Try(() => { VegetationStudioManager.AddVegetationItemInstance(id, place, Vector3.one, Quaternion.identity, true, 0, 1f, true); return "added"; });
            yield return 2f;
            Log($"Probe3: '{Name(model!)}' added at {P(place)}: {added}; objects within 2 m now {Biome(place, 2f).Count}");
            Shot("probe3-added");
            yield return AfterShot;
        }

        // A levelling field by code with its size set.
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Resources.FindObjectsOfTypeAll<NodeTemplate>().FirstOrDefault(t => t != null && t.name == "Item_Structures_TerrainLevelField");
        var fieldAt = spot + new Vector3(0, 0, -16);
        if (settlement != null && template != null && Landscape.Check(LandscapeArea.Rectangle(fieldAt, 10, 10), LandscapeOperation.Level) == LandscapeResult.Done)
        {
            fieldAt.y = Landscape.HeightAt(fieldAt.x, fieldAt.z);
            Structure? field = null;
            string created = Try(() =>
            {
                var placement = new PointPlacement();
                placement.position = fieldAt;
                placement.rotation = Quaternion.identity;
                field = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                return field != null ? field.name : "null";
            });
            yield return 2f;
            var building = field != null ? field.GetComponentInChildren<DynamicDimensionBuilding>(true) : null;
            string sized = Try(() => { building!.SetDimensions(2f, new Vector3Int(4, 1, 4), true); return $"area {building.WorldArea}, tile {building.TileSize}, grid {building.GridSize}"; });
            yield return 2f;
            var grid = field != null ? field.GetComponentInChildren<TerraformingGrid>(true) : null;
            Try(() => { grid!.RefreshCellsHeights(false, true); return ""; });
            Log($"Probe3: field {created}: sized {sized}; cells {Try(() => grid!.GetCellsCount())}, unlevelled {Try(() => grid!.GetUnleveledTile())}, reference {Try(() => grid!.ReferenceLevel)}, difference {Try(() => grid!.LevelDifference)}");
            Shot("probe3-field");
            yield return AfterShot;
            if (grid?._state is { } state)
            {
                var before = Profile(fieldAt);
                string done = Try(() => { state.Rpc_DebugCompleteField(); return "completed"; });
                yield return 3f;
                Log($"Probe3: field {done}: Δ across x {Profile(fieldAt, before)}; kind {Landscape.KindAt(fieldAt.x, fieldAt.z)}; still there {field != null}");
                Shot("probe3-field-done");
                yield return AfterShot;
            }
        }
        else Log("Probe3: no field placed (no settlement, template or free place)");

        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
