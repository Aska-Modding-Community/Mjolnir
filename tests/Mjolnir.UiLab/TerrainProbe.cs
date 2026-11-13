using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Render;
using SandSailorStudio.TerrainTools;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;
using VegetationStudioManager = AwesomeTechnologies.VegetationStudio.VegetationStudioManager;
using BiomeType = AwesomeTechnologies.VegetationSystem.BiomeType;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe" scenario: what a terraforming tool of a mod needs beyond the terrain module's first part. On a free
/// spot away from the settlement (a camera of the SDK's above it) it writes heights directly in several ways (the data made
/// by hand, with the kind of ground too, the tool's own data, the game's apply called at once) and keeps the first that
/// works for a circle, a smoothing, an undo of the heights and of the kind of ground, and a large area timed against the
/// tool's own raise; paints biomes and the ground cover; then, on a spot with trees and rocks, lists the world's objects in an
/// area, raises the ground under them and refreshes the vegetation, removes a rock as the game's tool does, fells a tree as a
/// blow does, and adds a tree through the vegetation system; puts down one of the game's levelling fields by code and
/// completes it; lists the road plans' templates; and draws an outline on the ground. It changes the ground and the objects
/// of the save (the game saves the world by itself): back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe()
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
            Log($"Probe: no world: character {character != null}, surface {tsm != null}, data {data != null}, landscape {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;

        // Two spots away from the settlement: a bare one (few objects) and one with trees and rocks.
        Vector3 bare = default, wooded = default;
        bool foundBare = false, foundWooded = false;
        foreach (float distance in new[] { 50f, 70f, 90f, 110f, 140f, 170f })
            for (int turn = 0; turn < 360; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 14, 14, 0, 2), LandscapeOperation.Level) != LandscapeResult.Done) continue;
                int objects = Instances(at, 7f).Count;
                if (!foundBare && objects <= 1) { bare = at; foundBare = true; Log($"Probe: bare spot {P(at)} ({distance} m, {turn}°), {objects} object(s)"); }
                else if (!foundWooded && objects >= 4) { wooded = at; foundWooded = true; Log($"Probe: wooded spot {P(at)} ({distance} m, {turn}°), {objects} object(s)"); }
            }
        if (!foundBare)
        {
            Log("Probe: no bare spot found");
            yield break;
        }

        foreach (float wait in GoAbove(character, home, bare)) yield return wait;
        var camera = Overhead(bare);
        bare.y = Landscape.HeightAt(bare.x, bare.z);
        Shot("probe-bare");
        yield return AfterShot;

        // A. Writing heights directly: +1 m over a 16.5 m square, each way until one works.
        string? working = null;
        foreach (var way in new[] { "own data", "own data with the kind", "the tool's data", "applied at once" })
        {
            float before = Landscape.HeightAt(bare.x, bare.z);
            string how = "";
            try { how = WriteRaise(tsm, way, bare, 1f); }
            catch (Exception e) { how = $"failed: {e.GetType().Name}: {e.Message}"; }
            yield return 1.5f;
            float after = Landscape.HeightAt(bare.x, bare.z);
            Log(string.Create(CultureInfo.InvariantCulture, $"Probe: write ({way}): {how}; centre {before:0.000} → {after:0.000} (Δ {after - before:+0.000;-0.000}); heightmap {ContainerHeight(data, tsm, bare)}; pending {Try(() => tsm._pendingToolDataToApply?.Count)}"));
            if (after - before > 0.5f)
            {
                working = way;
                break;
            }
        }
        Log($"Probe: direct writing works by: {working ?? "none"}");
        Shot("probe-written");
        yield return AfterShot;

        if (working != null)
        {
            // A circle: a disc of radius 5 m raised 1.5 m with a 2 m falloff; heights read across it.
            float baseH = Landscape.HeightAt(bare.x, bare.z);
            var before = Profile(bare);
            float ms = Timed(() => WriteShape(tsm, working, bare, 9f, (dx, dz, h) =>
            {
                float r = MathF.Sqrt(dx * dx + dz * dz);
                float w = r <= 5 ? 1 : r >= 7 ? 0 : 1 - (r - 5) / 2;
                return h + 1.5f * w;
            }));
            yield return 1.5f;
            Log($"Probe: circle r 5 m +1.5 m, falloff 2 m ({ms:0} ms): Δ across x {Profile(bare, before)}");
            Shot("probe-circle");
            yield return AfterShot;

            // Smoothing: a 3 × 3 average, three passes, over the circle's edge.
            var beforeSmooth = Profile(bare);
            ms = Timed(() => Smooth(tsm, working, bare, 9f, 3));
            yield return 1.5f;
            Log($"Probe: smoothed 3 passes ({ms:0} ms): Δ across x {Profile(bare, beforeSmooth)}");
            Shot("probe-smoothed");
            yield return AfterShot;

            // Undo: the heights and the kind of ground of a square read, a road painted and the ground raised, both written back.
            var snapshot = ReadSquare(tsm, bare, 8f);
            var flagsBefore = MapCells(data, bare, 6);
            Landscape.Paint(LandscapeArea.Rectangle(bare, 4, 8, 0, 0.5f), GroundKind.Road);
            Landscape.Raise(LandscapeArea.Rectangle(bare, 6, 6), 2f);
            yield return 1.5f;
            Log($"Probe: before the undo: kind {Landscape.KindAt(bare.x, bare.z)}, centre {Landscape.HeightAt(bare.x, bare.z):0.000}");
            string undone = "";
            try { undone = WriteSquare(tsm, working, bare, 8f, snapshot); }
            catch (Exception e) { undone = $"failed: {e.Message}"; }
            yield return 1.5f;
            Log($"Probe: undone ({undone}): kind {Landscape.KindAt(bare.x, bare.z)}, centre {Landscape.HeightAt(bare.x, bare.z):0.000} (was {snapshot.Centre:0.000}); cells {DiffCells(flagsBefore, MapCells(data, bare, 6))}");

            // A large area: 64 × 64 m raised 0.3 m by writing, against the tool's own raise of the same.
            var big = bare + new Vector3(0, 0, 0);
            ms = Timed(() => WriteShape(tsm, working, big, 64f, (dx, dz, h) => h + 0.3f));
            yield return 2f;
            float toolMs = Timed(() => Landscape.Raise(LandscapeArea.Rectangle(big, 64, 64, 0, 0), 0.3f));
            yield return 2f;
            Log($"Probe: 64 × 64 m: written in {ms:0} ms, the tool's raise in {toolMs:0} ms; centre now {Landscape.HeightAt(big.x, big.z):0.000} (start {baseH:0.000})");
            Shot("probe-large");
            yield return AfterShot;
        }

        // B. Biomes and the ground cover, through the game's tool.
        var cover = bare + new Vector3(12, 0, 0);
        foreach (var (what, setUp) in new (string, Action<HeightmapTool>)[]
                 {
                     ("biome Meadow", t => { t.setBiomeType = true; t.biomeType = BiomeType.Meadow; t.setTerrainType = false; t.clearVegetation = false; t.setVegetationMask = false; }),
                     ("biome BorealForest", t => { t.setBiomeType = true; t.biomeType = BiomeType.BorealForest; t.setTerrainType = false; t.clearVegetation = false; t.setVegetationMask = false; }),
                     ("natural, vegetation back", t => { t.setBiomeType = false; t.setTerrainType = true; t.terrainType = TerraformingMap.TerrainType.NATURAL; t.clearVegetation = false; t.setVegetationMask = false; }),
                     ("dirt, vegetation cleared", t => { t.setBiomeType = false; t.setTerrainType = true; t.terrainType = TerraformingMap.TerrainType.DIRT; t.clearVegetation = true; t.setVegetationMask = true; }),
                     ("natural, mask kept", t => { t.setBiomeType = false; t.setTerrainType = true; t.terrainType = TerraformingMap.TerrainType.NATURAL; t.clearVegetation = false; t.setVegetationMask = true; }),
                 })
        {
            string biomeBefore = Try(() => VegetationStudioManager.GetBiomeType(cover));
            var result = ToolPaint(cover, setUp);
            yield return 2f;
            Log($"Probe: {what}: {result}; biome {biomeBefore} → {Try(() => VegetationStudioManager.GetBiomeType(cover))}; kind {Landscape.KindAt(cover.x, cover.z)}, cleared {Landscape.IsCleared(cover.x, cover.z)}; cell {Try(() => FlagsHex(data, cover))}");
            Shot("probe-cover-" + what.Split(',')[0].Replace(' ', '-'));
            yield return AfterShot;
        }

        // C. The outline of an area on the ground: a line following the heights.
        string outline = "";
        try { outline = Outline(bare, 6f); }
        catch (Exception e) { outline = $"failed: {e.Message}"; }
        yield return 1f;
        Log($"Probe: outline: {outline}");
        Shot("probe-outline");
        yield return AfterShot;

        // D. The world's objects, on the wooded spot.
        if (foundWooded)
        {
            camera.Dispose();
            foreach (float wait in GoAbove(character, home, wooded)) yield return wait;
            camera = Overhead(wooded);
            wooded.y = Landscape.HeightAt(wooded.x, wooded.z);
            var found = Instances(wooded, 7f);
            Log($"Probe: objects within 7 m of {P(wooded)}: {found.Count}: {string.Join("; ", found.Take(25).Select(Describe))}");
            Shot("probe-wooded");
            yield return AfterShot;

            // Raised under them without clearing: where they stand then, and after the vegetation is refreshed.
            var standing = found.Select(i => (i, Pos: i.GetPosition())).ToList();
            Landscape.Raise(LandscapeArea.Rectangle(wooded, 10, 10, 0, 2), 1.5f, new LandscapeOptions { ClearVegetation = false, KeepOffBuildings = true });
            yield return 2f;
            var still = Instances(wooded, 7f);
            Log($"Probe: raised 1.5 m without clearing: {still.Count} of {found.Count} left; {Seated(standing)}");
            Shot("probe-raised-objects");
            yield return AfterShot;
            var bounds = new Bounds(wooded, new Vector3(16, 40, 16));
            string refresh = Try(() => { VegetationStudioManager.RefreshTerrainArea(bounds); return "RefreshTerrainArea"; });
            yield return 2f;
            Log($"Probe: after {refresh}: {Seated(standing)}; objects {Instances(wooded, 7f).Count}");
            string cache = Try(() => { VegetationStudioManager.ClearCache(bounds); return "ClearCache"; });
            yield return 2f;
            Log($"Probe: after {cache}: {Seated(standing)}; objects {Instances(wooded, 7f).Count}");
            Shot("probe-refreshed");
            yield return AfterShot;

            // Removal as the game's tool does it, and a blow that fells.
            var objects = Instances(wooded, 7f).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null).ToList();
            var rock = objects.FirstOrDefault(b => Name(b!).Contains("Rock", StringComparison.OrdinalIgnoreCase) || Name(b!).Contains("Stone", StringComparison.OrdinalIgnoreCase)) ?? objects.FirstOrDefault();
            if (rock != null)
            {
                string name = Name(rock);
                bool silent = false;
                var level = InstanceDestructionLevel.TERRAFORMING;
                string removed = Try(() => { rock.Destroy(ref silent, ref level); return $"destroyed {rock.Destroyed}"; });
                yield return 1.5f;
                Log($"Probe: '{name}' removed as the tool removes: {removed}; objects {Instances(wooded, 7f).Count}; drops near {Drops(wooded)}");
            }
            var tree = objects.FirstOrDefault(b => b != rock && Name(b!).Contains("Tree", StringComparison.OrdinalIgnoreCase)) ?? objects.FirstOrDefault(b => b != rock);
            if (tree != null)
            {
                string name = Name(tree);
                float health = tree.GetHealth();
                string drops = Drops(wooded);
                string felled = Try(() => { tree.TakeDamage(health + 1000); return $"health {tree.GetHealth()}, exhausted {tree.IsExhausted()}, destroyed {tree.Destroyed}"; });
                yield return 4f;
                Log($"Probe: '{name}' (health {health}) struck to death: {felled}; after 4 s destroyed {tree.Destroyed}; drops near {drops} → {Drops(wooded)}");
                Shot("probe-felled");
                yield return AfterShot;
            }

            // A tree added through the vegetation system, beside the spot.
            var model = objects.FirstOrDefault(b => b != null && b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo() != null);
            string? id = model?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.VegetationItemID;
            if (id != null)
            {
                var place = wooded + new Vector3(5, 0, -5);
                place.y = Landscape.HeightAt(place.x, place.z);
                int near = Instances(place, 1.5f).Count;
                string added = Try(() => { VegetationStudioManager.AddVegetationItemInstance(id, place, Vector3.one, Quaternion.identity, true, 0, 1f, true); return "added"; });
                yield return 2f;
                Log($"Probe: '{Name(model!)}' ({id}) added at {P(place)}: {added}; objects within 1.5 m {near} → {Instances(place, 1.5f).Count}: {string.Join("; ", Instances(place, 1.5f).Select(Describe))}");
                Shot("probe-added");
                yield return AfterShot;
            }
        }
        else Log("Probe: no wooded spot found");

        // E. One of the game's levelling fields, put down by code and completed.
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var fields = Resources.FindObjectsOfTypeAll<NodeTemplate>().Where(t => t != null && Try(() => t.nodeStructure != null && t.nodeStructure.GetComponentInChildren<TerraformingGrid>(true) != null) == "True").ToList();
        Log($"Probe: templates with a levelling field: {string.Join(", ", fields.Select(t => $"{t.name} (tool {t.placementTool?.GetIl2CppType().Name})"))}");
        var fieldTemplate = fields.FirstOrDefault(t => t.name.Contains("Field", StringComparison.OrdinalIgnoreCase)) ?? fields.FirstOrDefault();
        if (settlement != null && fieldTemplate != null)
        {
            var at = bare + new Vector3(-14, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            var placement = new PointPlacement();
            placement.position = at;
            placement.rotation = Quaternion.identity;
            Structure? field = null;
            string created = Try(() => { field = settlement.CreateStructure(fieldTemplate, placement, null, StructureTemplate.Options.SpawnedByTool, 0); return field != null ? field.name : "null"; });
            yield return 2f;
            var grid = field != null ? field.GetComponentInChildren<TerraformingGrid>(true) : null;
            var state = grid != null ? grid._state : null;
            Log($"Probe: field '{fieldTemplate.name}' at {P(at)}: {created}; grid {grid != null}: cells {Try(() => grid!.GetCellsCount())}, unlevelled {Try(() => grid!.GetUnleveledTile())}, difference {Try(() => grid!.LevelDifference)}, reference {Try(() => grid!.ReferenceLevel)}, result '{Try(() => grid!.ResultStructureTemplate?.name)}'");
            Shot("probe-field");
            yield return AfterShot;
            if (state != null)
            {
                float before = Landscape.HeightAt(at.x, at.z);
                string completed = Try(() => { state.Rpc_DebugCompleteField(); return "Rpc_DebugCompleteField"; });
                yield return 3f;
                Log($"Probe: field completed by {completed}: centre {before:0.000} → {Landscape.HeightAt(at.x, at.z):0.000}; the structure there {field != null}; kind {Landscape.KindAt(at.x, at.z)}");
                Shot("probe-field-done");
                yield return AfterShot;
            }
        }

        // F. The road plans' templates.
        foreach (var spline in Resources.FindObjectsOfTypeAll<SplineStructureTemplate>())
            Log($"Probe: road plan '{spline.name}': points {spline.maxControlPoints}, segment {spline.maxSegmentLength} m, section {spline.sectionLength} m, depth {spline.maxSectionDepth}, width {spline.width}, footprint {spline.footprintWidth}, sampling {spline.samplingStep}");

        camera.Dispose();
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The player 14 m from a spot, the spot loaded around them.
    private static IEnumerable<float> GoAbove(PlayerCharacter character, Vector3 home, Vector3 spot)
    {
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 14f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
    }

    private static ModCamera Overhead(Vector3 spot)
    {
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 22f, -10f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;
        return camera;
    }

    private static float Timed(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return (float)watch.Elapsed.TotalMilliseconds;
    }

    // Writes +amount over a 16.5 m square in one of the ways tried.
    private static string WriteRaise(TerrainSurfaceManager tsm, string way, Vector3 at, float amount)
    {
        float unit = tsm.GetNormalizedHeight(amount) - tsm.GetNormalizedHeight(0);
        TerrainToolData d;
        if (way == "the tool's data")
        {
            var tool = ProbeTool();
            tool.SetSize(TerraformingToolSize.SMALL);
            d = tool._activeConfig!.data!;
            d.SetPosition(at.x, at.z);
            d.ClearFlags();
        }
        else d = new TerrainToolData(33, 33, tsm.GetActiveTileSize(), way == "own data with the kind" ? TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN : TerrainToolData.TargetFlags.HEIGHTMAP);
        if (way != "the tool's data")
        {
            d.SetPosition(at.x, at.z);
            d.ClearFlags();
        }
        tsm.FillTerrainToolData(d);
        int rows = d.size.y, columns = d.size.x;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                d.SetFullHeightmap(r, c, d.GetFullHeightmap(r, c) + unit);
                d.SetModified(r, c, TerrainToolData.TargetFlags.HEIGHTMAP);
            }
        string state = $"size {d.size}, area {d.worldArea}, targets {d.GetTargetFlags()}, tiles {d.tiles?.Count}, update areas {d.updateAreas?.Count}, modified at the middle {d.IsModified(rows / 2, columns / 2, TerrainToolData.TargetFlags.HEIGHTMAP)}";
        if (way == "applied at once") tsm._ApplyTerrainToolDataInternal(d, -1);
        else tsm.ApplyTerrainToolData(d, -1);
        s_probeData = d;
        return state;
    }

    private static TerrainToolData? s_probeData;
    private static HeightmapTool? s_probeTool;

    private static HeightmapTool ProbeTool()
    {
        if (s_probeTool != null) return s_probeTool;
        var go = new GameObject("MjolnirLab_ProbeTool");
        Object.DontDestroyOnLoad(go);
        s_probeTool = go.AddComponent(Il2CppType.Of<HeightmapTool>()).Cast<HeightmapTool>();
        return s_probeTool;
    }

    // A square of side metres centred on a point, each sample's new world height from its offset and its height now.
    private static void WriteShape(TerrainSurfaceManager tsm, string way, Vector3 at, float side, Func<float, float, float, float> height)
    {
        int n = (int)MathF.Ceiling(side / tsm.GetActiveTileSize()) + 1;
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), way == "own data with the kind" ? TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN : TerrainToolData.TargetFlags.HEIGHTMAP);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
        var area = d.worldArea;
        float step = area.m_Width / (n - 1);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                float dx = area.m_XMin + c * step - at.x, dz = area.m_YMin + r * step - at.z;
                float now = tsm.GetWorldHeight(d.GetFullHeightmap(r, c));
                d.SetFullHeightmap(r, c, tsm.GetNormalizedHeight(height(dx, dz, now)));
                d.SetModified(r, c, TerrainToolData.TargetFlags.HEIGHTMAP);
            }
        if (way == "applied at once") tsm._ApplyTerrainToolDataInternal(d, -1);
        else tsm.ApplyTerrainToolData(d, -1);
    }

    private static void Smooth(TerrainSurfaceManager tsm, string way, Vector3 at, float side, int passes)
    {
        int n = (int)MathF.Ceiling(side / tsm.GetActiveTileSize()) + 1;
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), TerrainToolData.TargetFlags.HEIGHTMAP);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
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
        if (way == "applied at once") tsm._ApplyTerrainToolDataInternal(d, -1);
        else tsm.ApplyTerrainToolData(d, -1);
    }

    private sealed record Square(float[,] Heights, int[,] Flags, float Centre);

    private static Square ReadSquare(TerrainSurfaceManager tsm, Vector3 at, float side)
    {
        int n = (int)MathF.Ceiling(side / tsm.GetActiveTileSize()) + 1;
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
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

    private static string WriteSquare(TerrainSurfaceManager tsm, string way, Vector3 at, float side, Square square)
    {
        int n = square.Heights.GetLength(0);
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                d.SetFullHeightmap(r, c, square.Heights[r, c]);
                d.SetTerraformingFlags(r, c, square.Flags[r, c]);
                d.SetModified(r, c, TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN);
            }
        if (way == "applied at once") tsm._ApplyTerrainToolDataInternal(d, -1);
        else tsm.ApplyTerrainToolData(d, -1);
        return $"{n} × {n}";
    }

    // Heights along x through a point, every 1 m from -8 to 8; with a before, their changes.
    private static float[] Profile(Vector3 at) => Enumerable.Range(-8, 17).Select(d => Landscape.HeightAt(at.x + d, at.z)).ToArray();

    private static string Profile(Vector3 at, float[] before)
    {
        var now = Profile(at);
        return string.Join(" ", now.Select((h, i) => string.Create(CultureInfo.InvariantCulture, $"{i - 8}:{h - before[i]:+0.00;-0.00}")));
    }

    private static string ContainerHeight(WorldDataManager data, TerrainSurfaceManager tsm, Vector3 at)
    {
        try
        {
            var tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
            var c = TerrainContainer(tile);
            if (tile == null || c?.heightMap == null) return "none";
            int res = c.heightMap.Resolution;
            int x = (int)((at.x - tile.area.m_XMin) / tile.area.m_Width * (res - 1));
            int y = (int)((at.z - tile.area.m_YMin) / tile.area.m_Height * (res - 1));
            return string.Create(CultureInfo.InvariantCulture, $"{tsm.GetWorldHeight(c.heightMap.GetValue(ref x, ref y)):0.000} at ({x}, {y}), persistent {c.IsHeightmapPersistent()}");
        }
        catch (Exception e) { return $"failed: {e.Message}"; }
    }

    private static string FlagsHex(WorldDataManager data, Vector3 at)
    {
        var cells = MapCells(data, at, 0);
        return cells.Count > 0 ? cells.First().Value.ToString("X", CultureInfo.InvariantCulture) : "none";
    }

    // A paint with the probe's tool over a 6 × 6 m square, set up as asked.
    private static string ToolPaint(Vector3 at, Action<HeightmapTool> setUp)
    {
        var tool = ProbeTool();
        tool.requiresTerrainType = false;
        tool.ignorePaintType = false;
        tool.checkTerrainSlope = false;
        tool.checkItemInstances = false;
        tool.validateHeight = false;
        tool.propagateOnNetwork = true;
        setUp(tool);
        at.y = Landscape.HeightAt(at.x, at.z);
        try { return tool.RunOnArea(TerraformingToolOperation.PAINT, 6, 6, 1, at, 0).ToString(); }
        catch (Exception e) { return $"failed: {e.Message}"; }
    }

    private static string Outline(Vector3 at, float radius)
    {
        var go = new GameObject("MjolnirLab_Outline");
        var line = go.AddComponent(Il2CppType.Of<LineRenderer>()).Cast<LineRenderer>();
        var shaders = new[] { "HDRP/Unlit", "Sprites/Default", "Unlit/Color" };
        string? used = null;
        foreach (var name in shaders)
        {
            var shader = Shader.Find(name);
            if (shader == null) continue;
            var material = new Material(shader);
            material.color = new Color(1f, 0.7f, 0.1f, 1f);
            line.material = material;
            used = name;
            break;
        }
        const int count = 64;
        line.positionCount = count;
        line.loop = true;
        line.widthMultiplier = 0.12f;
        for (int i = 0; i < count; i++)
        {
            float a = i * MathF.PI * 2 / count;
            var p = at + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
            p.y = Landscape.HeightAt(p.x, p.z) + 0.15f;
            line.SetPosition(i, p);
        }
        return $"shader {used ?? "none found"}, {count} points, layer {LayerMask.LayerToName(go.layer)}";
    }

    private static List<WorldItemInstance> Instances(Vector3 at, float radius)
    {
        var found = new List<WorldItemInstance>();
        try
        {
            var list = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
            GameManagers.Get<ResourceManager>()?.GetAllInstancesList(list, at, Quaternion.identity, new Vector3(radius * 2, 40, radius * 2), SDFShape.CIRCLE, null, InstanceActivationContext.RESOURCE_FINDER, false);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) found.Add(list[i]);
        }
        catch (Exception e) { Log($"Probe: GetAllInstancesList failed: {e.Message}"); }
        return found;
    }

    private static string Name(WorldItemInstance instance)
    {
        var b = instance.TryCast<BiomeItemInstance>();
        return b?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name ?? instance.Descriptor?.itemInfo?.name ?? instance.GetIl2CppType().Name;
    }

    private static string Describe(WorldItemInstance instance)
    {
        var b = instance.TryCast<BiomeItemInstance>();
        var p = instance.GetPosition();
        string ground = string.Create(CultureInfo.InvariantCulture, $"{p.y - Landscape.HeightAt(p.x, p.z):+0.00;-0.00}");
        string more = b == null ? instance.GetIl2CppType().Name
            : $"health {Try(() => b.GetHealth())}, quantity {Try(() => b.GetQuantity())}, resource '{Try(() => b.GetResourceInfo()?.itemInfo?.name)}', removable by terraforming {Try(() => b.IsRemovable(InstanceDestructionLevel.TERRAFORMING))}, harvestable {Try(() => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.IsHarvestable)}";
        return $"'{Name(instance)}' at {P(p)} ({ground} to the ground), {more}";
    }

    private static string Seated(List<(WorldItemInstance Instance, Vector3 Pos)> standing) =>
        string.Join(", ", standing.Take(8).Select(s =>
        {
            var p = s.Instance.Destroyed ? s.Pos : s.Instance.GetPosition();
            return string.Create(CultureInfo.InvariantCulture, $"'{Name(s.Instance)}' y {s.Pos.y:0.00}→{p.y:0.00} ground {Landscape.HeightAt(p.x, p.z):0.00}{(s.Instance.Destroyed ? " (gone)" : "")}");
        }));

    // What lies loose on the ground near a point: the game's items dropped in the world.
    private static string Drops(Vector3 at)
    {
        var names = Physics.OverlapSphere(at, 10f).Where(c => c != null && c.GetComponentInParent<ItemObject>() != null)
            .Select(c => c.GetComponentInParent<ItemObject>().name).Distinct().ToList();
        return names.Count == 0 ? "none" : $"{names.Count}: {string.Join(", ", names.Take(10))}";
    }
}
