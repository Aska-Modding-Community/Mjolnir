using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Network;
using SandSailorStudio;
using SandSailorStudio.Render;
using SandSailorStudio.UI;
using UnityEngine;
using VegetationStudioManager = AwesomeTechnologies.VegetationStudio.VegetationStudioManager;
using BiomeType = AwesomeTechnologies.VegetationSystem.BiomeType;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 2" scenario, after the first probe found that a write needs the terrain target beside the heights:
/// smoothing and an exact undo (heights, kind of ground and vegetation) with every target; a written change sent to the other
/// players as the game sends its own; the biome painted and read back where the game keeps it; the world's trees and rocks
/// found with the game's own filter and other contexts, raised under, refreshed, removed, felled and added; one of the game's
/// levelling fields put down by code and completed. It changes the ground and the objects of the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private const TerrainToolData.TargetFlags All = TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN | TerrainToolData.TargetFlags.VEGETATION;

    private static IEnumerator<float> TerrainProbe2()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var tsm = GameManagers.Get<TerrainSurfaceManager>();
        var data = GameManagers.Get<WorldDataManager>();
        if (character == null || tsm == null || data == null || !Landscape.IsAvailable)
        {
            Log("Probe2: no world");
            yield break;
        }
        var home = character.transform.position;

        // Which way of looking for the world's objects finds trees and rocks.
        var ways = new (string Name, Func<Vector3, float, List<WorldItemInstance>> Find)[]
        {
            ("the game's removable filter, resource finder", (p, r) => Objects(p, r, Removable(), InstanceActivationContext.RESOURCE_FINDER)),
            ("no filter, every context", (p, r) => Objects(p, r, null, InstanceActivationContext.ALL)),
            ("no filter, area", (p, r) => Objects(p, r, null, InstanceActivationContext.AREA)),
            ("the game's removable filter, every context", (p, r) => Objects(p, r, Removable(), InstanceActivationContext.ALL)),
        };
        Vector3 bare = default, wooded = default;
        bool foundBare = false, foundWooded = false;
        int bestWay = -1;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f, 130f, 160f })
            for (int turn = 0; turn < 360; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 14, 14, 0, 2), LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var counts = ways.Select(w => w.Find(at, 7f).Count(i => i.TryCast<BiomeItemInstance>() != null)).ToArray();
                if (!foundWooded && counts.Max() >= 3)
                {
                    wooded = at;
                    foundWooded = true;
                    bestWay = Array.IndexOf(counts, counts.Max());
                    Log($"Probe2: wooded spot {P(at)} ({distance} m, {turn}°): biome objects by way [{string.Join(", ", ways.Select((w, i) => $"{w.Name} {counts[i]}"))}]");
                }
                else if (!foundBare && counts.Max() == 0) { bare = at; foundBare = true; Log($"Probe2: bare spot {P(at)} ({distance} m, {turn}°)"); }
            }

        if (foundBare)
        {
            foreach (float wait in GoAbove(character, home, bare)) yield return wait;
            var camera = Overhead(bare);

            // Smoothing: a mound of 2 m with a steep edge, then four passes of a 3 × 3 average.
            WriteAll(tsm, bare, 10f, (dx, dz, h) => MathF.Sqrt(dx * dx + dz * dz) <= 3 ? h + 2 : h);
            yield return 1.5f;
            var mound = Profile(bare);
            SmoothAll(tsm, bare, 10f, 4);
            yield return 1.5f;
            Log($"Probe2: mound then smoothed 4 passes: Δ across x {Profile(bare, mound)}");
            Shot("probe2-smoothed");
            yield return AfterShot;

            // An exact undo: every target read and written back over a road and a raise.
            var snapshot = ReadAll(tsm, bare, 8f);
            var cells = MapCells(data, bare, 6);
            Landscape.Paint(LandscapeArea.Rectangle(bare, 4, 8, 0, 0.5f), GroundKind.Road);
            Landscape.Raise(LandscapeArea.Rectangle(bare, 6, 6), 2f);
            yield return 1.5f;
            WriteAllBack(tsm, bare, snapshot);
            yield return 1.5f;
            Log($"Probe2: undone with every target: centre {Landscape.HeightAt(bare.x, bare.z):0.000} (was {snapshot.Centre:0.000}); cells {DiffCells(cells, MapCells(data, bare, 6))}");

            // A written change sent as the game sends its own.
            var sent = WriteAll(tsm, bare + new Vector3(10, 0, 0), 4f, (dx, dz, h) => h + 0.5f);
            string propagated = Try(() => { GameManagers.Get<NetworkWorldDataManager>()!.PropagateTerrainToolOperation(sent); return $"sent (can send {GameManagers.Get<NetworkWorldDataManager>()!.CanSendToAnyone})"; });
            Log($"Probe2: a written change propagated: {propagated}");

            // Biomes painted and read back: the area under the point and the cell's biome mask.
            var patch = bare + new Vector3(-12, 0, 0);
            patch.y = Landscape.HeightAt(patch.x, patch.z);
            foreach (var biome in new[] { BiomeType.Swamp, BiomeType.BorealForest, BiomeType.Meadow })
            {
                string before = BiomeAt(tsm, data, patch);
                var result = ToolPaint(patch, t => { t.setBiomeType = true; t.biomeType = biome; t.setTerrainType = true; t.terrainType = TerraformingMap.TerrainType.NATURAL; t.clearVegetation = false; t.setVegetationMask = true; });
                yield return 1.5f;
                string refreshed = Try(() => { VegetationStudioManager.RefreshTerrainArea(new Bounds(patch, new Vector3(12, 40, 12))); return "refreshed"; });
                yield return 2f;
                Log($"Probe2: biome {biome}: {result}, {refreshed}; {before} → {BiomeAt(tsm, data, patch)}");
                Shot("probe2-biome-" + biome);
                yield return AfterShot;
            }
            camera.Dispose();
        }

        // The world's trees and rocks.
        if (foundWooded)
        {
            var find = ways[bestWay].Find;
            foreach (float wait in GoAbove(character, home, wooded)) yield return wait;
            var camera = Overhead(wooded);
            wooded.y = Landscape.HeightAt(wooded.x, wooded.z);
            var found = find(wooded, 7f).Where(i => i.TryCast<BiomeItemInstance>() != null).ToList();
            Log($"Probe2: by '{ways[bestWay].Name}' {found.Count} biome objects: {string.Join("; ", found.Take(20).Select(Describe))}");
            Shot("probe2-wooded");
            yield return AfterShot;

            var standing = found.Select(i => (i, Pos: i.GetPosition())).ToList();
            Landscape.Raise(LandscapeArea.Rectangle(wooded, 10, 10, 0, 2), 1.5f, new LandscapeOptions { ClearVegetation = false });
            yield return 2f;
            Log($"Probe2: raised 1.5 m without clearing: {find(wooded, 7f).Count(i => i.TryCast<BiomeItemInstance>() != null)} left; {Seated(standing)}");
            Shot("probe2-raised");
            yield return AfterShot;
            var bounds = new Bounds(wooded, new Vector3(16, 40, 16));
            Try(() => { VegetationStudioManager.RefreshTerrainArea(bounds); return ""; });
            yield return 2f;
            Log($"Probe2: after RefreshTerrainArea: {Seated(standing)}");
            Shot("probe2-refreshed");
            yield return AfterShot;

            var biomeObjects = find(wooded, 7f).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
            var rock = biomeObjects.FirstOrDefault(b => Name(b).Contains("Rock", StringComparison.OrdinalIgnoreCase) || Name(b).Contains("Stone", StringComparison.OrdinalIgnoreCase) || Name(b).Contains("Boulder", StringComparison.OrdinalIgnoreCase)) ?? biomeObjects.FirstOrDefault();
            if (rock != null)
            {
                string name = Name(rock);
                bool silent = false;
                var level = InstanceDestructionLevel.TERRAFORMING;
                string removed = Try(() => { rock.Destroy(ref silent, ref level); return $"destroyed {rock.Destroyed}"; });
                yield return 1.5f;
                Log($"Probe2: '{name}' removed as the tool removes: {removed}; left {find(wooded, 7f).Count(i => i.TryCast<BiomeItemInstance>() != null)}; drops near {Drops(wooded)}");
                Shot("probe2-removed");
                yield return AfterShot;
            }
            var tree = biomeObjects.FirstOrDefault(b => b != rock && (Name(b).Contains("Tree", StringComparison.OrdinalIgnoreCase) || Name(b).Contains("Birch", StringComparison.OrdinalIgnoreCase) || Name(b).Contains("Pine", StringComparison.OrdinalIgnoreCase))) ?? biomeObjects.FirstOrDefault(b => b != rock);
            if (tree != null)
            {
                string name = Name(tree);
                float health = tree.GetHealth();
                string dropsBefore = Drops(wooded);
                string struck = Try(() => { tree.TakeDamage(health + 1000); return $"health {tree.GetHealth()}, exhausted {tree.IsExhausted()}, destroyed {tree.Destroyed}"; });
                yield return 5f;
                Log($"Probe2: '{name}' (health {health}) struck to death: {struck}; after 5 s destroyed {tree.Destroyed}, exhausted {Try(() => tree.IsExhausted())}; drops {dropsBefore} → {Drops(wooded)}");
                Shot("probe2-felled");
                yield return AfterShot;
            }
            var model = biomeObjects.FirstOrDefault(b => !b.Destroyed && b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo() != null);
            string? id = model?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.VegetationItemID;
            if (id != null)
            {
                var place = wooded + new Vector3(5, 0, -5);
                place.y = Landscape.HeightAt(place.x, place.z);
                int near = find(place, 1.5f).Count;
                string added = Try(() => { VegetationStudioManager.AddVegetationItemInstance(id, place, Vector3.one, Quaternion.identity, true, 0, 1f, true); return "added"; });
                yield return 2f;
                Log($"Probe2: '{Name(model!)}' ({id}) added at {P(place)}: {added}; objects within 1.5 m {near} → {find(place, 1.5f).Count}");
                Shot("probe2-added");
                yield return AfterShot;
            }
            camera.Dispose();
        }
        else Log("Probe2: no wooded spot found by any way");

        // A levelling field by code: the game's field template, a terraforming placement of 4 × 4 tiles, then completed.
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Resources.FindObjectsOfTypeAll<NodeTemplate>().FirstOrDefault(t => t != null && t.name == "Item_Structures_TerrainLevelField");
        if (settlement != null && template != null && foundBare)
        {
            var at = bare + new Vector3(0, 0, 14);
            at.y = Landscape.HeightAt(at.x, at.z);
            Structure? field = null;
            string created = Try(() =>
            {
                var placement = new TerraformingPlacement();
                var a = new PointPlacement();
                a.position = at;
                a.rotation = Quaternion.identity;
                var b = new PointPlacement();
                b.position = at + new Vector3(8, 0, 8);
                b.rotation = Quaternion.identity;
                placement.points = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PointPlacement>(new[] { a, b });
                placement.gridSize = new Vector3Int(4, 1, 4);
                placement.tileSize = 2f;
                field = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
                return field != null ? field.name : "null";
            });
            yield return 3f;
            var grid = field != null ? field.GetComponentInChildren<TerraformingGrid>(true) : null;
            Log($"Probe2: field at {P(at)}: {created}; grid {grid != null}, cells {Try(() => grid!.GetCellsCount())}, reference {Try(() => grid!.ReferenceLevel)}, difference {Try(() => grid!.LevelDifference)}, area {Try(() => field!.GetComponentInChildren<DynamicDimensionBuilding>(true)?.WorldArea)}");
            Shot("probe2-field");
            yield return AfterShot;
            if (grid?._state is { } state)
            {
                var before = Profile(at + new Vector3(4, 0, 4));
                string done = Try(() => { state.Rpc_DebugCompleteField(); return "completed"; });
                yield return 3f;
                Log($"Probe2: field {done}: Δ across x {Profile(at + new Vector3(4, 0, 4), before)}; still there {field != null}");
                Shot("probe2-field-done");
                yield return AfterShot;
            }
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IWorldItemInstanceFilter Removable() => new HeightmapTool.RemovableInstanceFilter().BoxIl2CppObject().Cast<IWorldItemInstanceFilter>();

    private static List<WorldItemInstance> Objects(Vector3 at, float radius, IWorldItemInstanceFilter? filter, InstanceActivationContext context)
    {
        var found = new List<WorldItemInstance>();
        try
        {
            var list = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
            GameManagers.Get<ResourceManager>()?.GetAllInstancesList(list, at, Quaternion.identity, new Vector3(radius * 2, 40, radius * 2), SDFShape.CIRCLE, filter, context, false);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) found.Add(list[i]);
        }
        catch (Exception e) { Log($"Probe2: GetAllInstancesList ({context}) failed: {e.Message}"); }
        return found;
    }

    private static TerrainToolData NewAll(TerrainSurfaceManager tsm, Vector3 at, float side)
    {
        int n = (int)MathF.Ceiling(side / tsm.GetActiveTileSize()) + 1;
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), All);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
        return d;
    }

    private static TerrainToolData WriteAll(TerrainSurfaceManager tsm, Vector3 at, float side, Func<float, float, float, float> height)
    {
        var d = NewAll(tsm, at, side);
        int n = d.size.x;
        var area = d.worldArea;
        float step = area.m_Width / (n - 1);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                float dx = area.m_XMin + c * step - at.x, dz = area.m_YMin + r * step - at.z;
                d.SetFullHeightmap(r, c, tsm.GetNormalizedHeight(height(dx, dz, tsm.GetWorldHeight(d.GetFullHeightmap(r, c)))));
                d.SetModified(r, c, TerrainToolData.TargetFlags.HEIGHTMAP);
            }
        tsm.ApplyTerrainToolData(d, -1);
        return d;
    }

    private static void SmoothAll(TerrainSurfaceManager tsm, Vector3 at, float side, int passes)
    {
        var d = NewAll(tsm, at, side);
        int n = d.size.x;
        var h = new float[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++) h[r, c] = d.GetFullHeightmap(r, c);
        for (int p = 0; p < passes; p++)
        {
            var next = (float[,])h.Clone();
            for (int r = 1; r < n - 1; r++)
                for (int c = 1; c < n - 1; c++)
                {
                    float sum = 0;
                    for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++) sum += h[r + i, c + j];
                    next[r, c] = sum / 9;
                }
            h = next;
        }
        for (int r = 1; r < n - 1; r++)
            for (int c = 1; c < n - 1; c++)
            {
                d.SetFullHeightmap(r, c, h[r, c]);
                d.SetModified(r, c, TerrainToolData.TargetFlags.HEIGHTMAP);
            }
        tsm.ApplyTerrainToolData(d, -1);
    }

    private static Square ReadAll(TerrainSurfaceManager tsm, Vector3 at, float side)
    {
        var d = NewAll(tsm, at, side);
        int n = d.size.x;
        var heights = new float[n, n];
        var flags = new int[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                heights[r, c] = d.GetFullHeightmap(r, c);
                flags[r, c] = d.GetTerraformingFlags(r, c);
            }
        return new Square(heights, flags, Landscape.HeightAt(at.x, at.z));
    }

    private static void WriteAllBack(TerrainSurfaceManager tsm, Vector3 at, Square square)
    {
        int n = square.Heights.GetLength(0);
        var d = NewAll(tsm, at, (n - 1) * tsm.GetActiveTileSize());
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                d.SetFullHeightmap(r, c, square.Heights[r, c]);
                d.SetTerraformingFlags(r, c, square.Flags[r, c]);
                d.SetModified(r, c, All);
            }
        tsm.ApplyTerrainToolData(d, -1);
    }

    // Where the game keeps a point's biome: the world generator's area under it, and the biome data's presence mask of its cell.
    private static string BiomeAt(TerrainSurfaceManager tsm, WorldDataManager data, Vector3 at)
    {
        string area = Try(() => { var a = tsm.GetCurrentArea(at.x, at.z); return a == null ? "none" : $"{a.GetIl2CppType().Name}"; });
        string mask = Try(() =>
        {
            var tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
            var handler = data.GetDataHandler<BiomeProceduralDataHandler>(WorldDataSlot.BIOME);
            if (tile == null || handler == null) return "none";
            int cx = (int)((at.x - tile.area.m_XMin) / tile.area.m_Width * data.CellResolution);
            int cy = (int)((at.z - tile.area.m_YMin) / tile.area.m_Height * data.CellResolution);
            return handler.GetBiomePresenceMask(tile, cx, cy).ToString("X", CultureInfo.InvariantCulture);
        });
        return $"area {area}, presence mask {mask}, studio {Try(() => VegetationStudioManager.GetBiomeType(at))}";
    }
}
