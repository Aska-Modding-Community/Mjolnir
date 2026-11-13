using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using SSSGame;
using SSSGame.Network;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SandSailorStudio.Streaming;
using SandSailorStudio.TerrainTools;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain research" scenario: how the game's ground is made and changed, for a terrain module of the SDK. In the world
/// it writes shots/terrain.txt: the terrain surface manager and its configuration (units, texture layers, the terraforming
/// layers), the streamed Unity terrains, the world's data tiles, the water level, every heightmap tool the game has loaded (on
/// tools, structures, crops) and its sizes, the terraforming grids and spline layouts (paths, roads) standing in the world, and
/// what the terraforming map says around the player (kinds of ground, cleared vegetation, changed heights). Then, on a free
/// spot up to 160 m from the settlement (the player put 12 m from it, a camera of its own above it), it does what a mod would: a heightmap tool of its own (as the game adds one to a structure that
/// levels its footprint) raises, digs, levels to a height and paints a road, the heights read back through the game's sampler
/// and a ray each time; the heights are written back directly (the tool's data filled, changed and applied), which is what an
/// undo would do; then a last change is saved, the world left and loaded again, and the heights and the map read once more. It
/// changes the ground of the save and saves it: back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private const int TerrainGrid = 7; // samples across the test spot, a metre apart

    private static IEnumerator<float> TerrainResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var tsm = GameManagers.Get<TerrainSurfaceManager>();
        var data = GameManagers.Get<WorldDataManager>();
        if (character == null || tsm == null || data == null)
        {
            Log($"Terrain: missing: character {character != null}, surface manager {tsm != null}, data manager {data != null}");
            yield break;
        }

        var text = new StringBuilder();
        try { DescribeTerrain(text, tsm, data, character.transform.position); }
        catch (Exception e) { text.AppendLine($"description failed: {e}"); }
        File.WriteAllText(Path.Combine(ShotsDir, "terrain.txt"), text.ToString());
        Log("Terrain: terrain.txt written");

        // A free spot away from the settlement: no structure within 8 m, dry ground, the game's own check of a levelling there
        // passing. The player is put 12 m from it, facing it, so the screenshots show it.
        var toolObject = new GameObject("MjolnirLab_TerrainTool");
        Object.DontDestroyOnLoad(toolObject);
        var tool = toolObject.AddComponent(Il2CppType.Of<HeightmapTool>()).Cast<HeightmapTool>();
        Log($"Terrain: a tool of our own: {DescribeTool(tool)}");
        var water = GameManagers.Get<WaterSurfaceManager>();
        var home = character.transform.position;
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f, 130f, 160f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                at.y = TerrainSampler.SampleHeight(at);
                int structures = Physics.OverlapSphere(at, 8f).Count(c => c != null && c.GetComponentInParent<Structure>() != null);
                bool wet = water != null && water.IsUnderwater(at.x, at.z, 0.5f);
                TerraformingToolResult check = TerraformingToolResult.ERROR_INIT;
                try { check = tool.Check(TerraformingToolOperation.LEVEL, at); } catch (Exception e) { Log($"Terrain: Check failed: {e.Message}"); }
                if (structures == 0 && !wet && check == TerraformingToolResult.SUCCESS)
                {
                    spot = at;
                    found = true;
                    Log($"Terrain: free spot {P(at)} at {distance} m, {turn}° from the player");
                }
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Terrain: no free spot found within 160 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 12f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        character.transform.LookAt(new Vector3(spot.x, character.transform.position.y, spot.z));
        spot.y = TerrainSampler.SampleHeight(spot);
        yield return 1f;
        Log($"Terrain: the test spot {P(spot)}; under it {RayGround(spot)}; {DescribeMap(data, spot, 2)}");
        var before = Heights(spot);
        Log($"Terrain: heights before {Summary(before, before)}; colliders within 4 m: {Colliders(spot)}");
        var overhead = Mjolnir.SDK.Camera.GameCamera.Take(100);
        overhead.Position = spot + new Vector3(0, 14f, -6f);
        overhead.Rotation = Quaternion.LookRotation(spot - overhead.Position, Vector3.up);
        overhead.FieldOfView = 50;
        yield return 1f;
        Shot("terrain-before");
        yield return AfterShot;

        // The tool's own data (the medium brush), filled with the ground as it is: the snapshot an undo writes back.
        TerrainToolData? snapshotData = null;
        float[,]? snapshot = null;
        try
        {
            // 33 × 33 samples at the tile size the tool's own data uses (the small brush's: 5 × 5 over 2.5 m).
            tool.SetSize(TerraformingToolSize.SMALL);
            var small = tool._activeConfig?.data;
            Log($"Terrain: the small brush's data: size {small?.size} over {small?.worldArea}, targets {small?.GetTargetFlags()}; active tile size {tsm.GetActiveTileSize()}");
            snapshotData = new TerrainToolData(33, 33, tsm.GetActiveTileSize(), TerrainToolData.TargetFlags.HEIGHTMAP);
            if (snapshotData != null)
            {
                snapshotData.SetPosition(spot.x, spot.z);
                snapshotData.ClearFlags();
                tsm.FillTerrainToolData(snapshotData);
                snapshot = ReadFull(snapshotData);
                Log($"Terrain: our own data: size {snapshotData.size}, area {snapshotData.worldArea}, targets {snapshotData.GetTargetFlags()}, centre full {snapshotData.GetFullHeightmap(snapshotData.size.y / 2, snapshotData.size.x / 2):0.000000} raw {snapshotData.GetRawHeightmap(snapshotData.size.y / 2, snapshotData.size.x / 2):0.000000}, world {tsm.GetWorldHeight(snapshotData.GetFullHeightmap(snapshotData.size.y / 2, snapshotData.size.x / 2)):0.000} (sampler {spot.y:0.000})");
            }
            else Log("Terrain: the tool has no data after SetSize(MEDIUM)");
        }
        catch (Exception e) { Log($"Terrain: the snapshot failed: {e}"); }

        // Raise, dig, level to a height: the area operation a structure's footprint uses (4 × 4 m, blended over 1 m).
        foreach (var op in new[] { TerraformingToolOperation.RAISE, TerraformingToolOperation.DIG, TerraformingToolOperation.LEVEL })
        {
            if (op == TerraformingToolOperation.LEVEL) tool.SetReferenceHeight(spot.y + 1.5f, false);
            var heightsBefore = Heights(spot);
            TerraformingToolResult result = TerraformingToolResult.ERROR_INIT;
            float started = Time.realtimeSinceStartup;
            try { result = tool.RunOnArea(op, 4f, 4f, 1f, spot, 0f); } catch (Exception e) { Log($"Terrain: RunOnArea({op}) failed: {e}"); }
            yield return 0.1f;
            Log($"Terrain: RunOnArea({op}) {result}; after 0.1 s {Summary(Heights(spot), heightsBefore)}");
            yield return 1f;
            Log($"Terrain: {op} after 1.1 s {Summary(Heights(spot), heightsBefore)}; ray {RayGround(spot)}; {DescribeMap(data, spot, 2)}");
            Shot($"terrain-{op.ToString().ToLowerInvariant()}");
            yield return AfterShot;
            Log($"Terrain: {op} took {Time.realtimeSinceStartup - started:0.00} s to show");
        }
        tool.ResetHeightReference();

        // The brush sizes of Run(op, point): each size's brush and its data.
        foreach (var size in Enum.GetValues<TerraformingToolSize>())
        {
            try
            {
                tool.SetSize(size);
                var config = tool._activeConfig;
                Log($"Terrain: size {size}: brush {config?.size} samples, data {config?.data?.size} over {config?.data?.worldArea}");
            }
            catch (Exception e) { Log($"Terrain: SetSize({size}) failed: {e.Message}"); }
        }

        // Point operations, as the shovel does them: one raise with the small brush at the spot's corner.
        var corner = spot + new Vector3(2.5f, 0, 2.5f);
        corner.y = TerrainSampler.SampleHeight(corner);
        TerraformingToolResult raised = TerraformingToolResult.ERROR_INIT;
        try
        {
            tool.SetSize(TerraformingToolSize.SMALL);
            raised = tool.Run(TerraformingToolOperation.RAISE, corner);
        }
        catch (Exception e) { Log($"Terrain: Run(RAISE) failed: {e.Message}"); }
        yield return 1f;
        Log($"Terrain: Run(RAISE, small) at {P(corner)}: {raised}; the corner now {TerrainSampler.SampleHeight(corner):0.000}");

        // Paint a road across the spot: the terraforming map's kind of ground and the textures.
        var paintBefore = MapCells(data, spot, 6);
        tool.setTerrainType = true;
        tool.terrainType = TerraformingMap.TerrainType.ROAD;
        TerraformingToolResult painted = TerraformingToolResult.ERROR_INIT;
        try { painted = tool.RunOnArea(TerraformingToolOperation.PAINT, 2f, 6f, 0.5f, spot, 0f); } catch (Exception e) { Log($"Terrain: PAINT failed: {e}"); }
        yield return 1.5f;
        Log($"Terrain: RunOnArea(PAINT ROAD, 2 × 6) {painted}; {DiffCells(paintBefore, MapCells(data, spot, 6))}; heights {Summary(Heights(spot), before)}");
        Shot("terrain-road");
        yield return AfterShot;

        // The undo: the snapshot written back through the same data, every sample marked, applied as the game applies a tool.
        if (snapshotData != null && snapshot != null)
        {
            try
            {
                snapshotData.SetPosition(spot.x, spot.z);
                snapshotData.ClearFlags();
                tsm.FillTerrainToolData(snapshotData);
                for (int row = 0; row < snapshotData.size.y; row++)
                    for (int column = 0; column < snapshotData.size.x; column++)
                    {
                        snapshotData.SetFullHeightmap(row, column, snapshot[row, column]);
                        snapshotData.SetModified(row, column, TerrainToolData.TargetFlags.HEIGHTMAP);
                    }
                tsm.ApplyTerrainToolData(snapshotData, -1);
            }
            catch (Exception e) { Log($"Terrain: writing the snapshot back failed: {e}"); }
            yield return 1.5f;
            Log($"Terrain: the snapshot written back: {Summary(Heights(spot), before)}; ray {RayGround(spot)}");
            Shot("terrain-undone");
            yield return AfterShot;
        }

        // What a save keeps: the tile's container and its flags, then a raise saved and loaded again.
        try { tool.RunOnArea(TerraformingToolOperation.RAISE, 3f, 3f, 1f, spot, 0f); } catch (Exception e) { Log($"Terrain: last raise failed: {e.Message}"); }
        yield return 1.5f;
        var saved = Heights(spot);
        Log($"Terrain: before the save {Summary(saved, before)}; {DescribeContainer(data, spot)}");
        var storage = GameManagers.Get<StorageManager>();
        if (storage == null)
        {
            Log("Terrain: no storage manager");
            yield break;
        }
        var task = storage.SaveCurrentSession();
        foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
        Log($"Terrain: saved, {storage.LastSaveOperationCode}");
        overhead.Dispose();
        Object.Destroy(toolObject);
        MainMenuReady = false;
        HudReady = false;
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
        yield return 1.5f;
        if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
        foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var data2 = GameManagers.Get<WorldDataManager>();
        Log($"Terrain: after loading again {Summary(Heights(spot), saved)} (against the saved heights); against the start {Summary(Heights(spot), before)}; " +
            $"{(data2 != null ? DescribeMap(data2, spot, 2) + "; " + DescribeContainer(data2, spot) : "no data manager")}");
        var again = Mjolnir.SDK.Camera.GameCamera.Take(100);
        again.Position = spot + new Vector3(0, 14f, -6f);
        again.Rotation = Quaternion.LookRotation(spot - again.Position, Vector3.up);
        again.FieldOfView = 50;
        yield return 1f;
        Shot("terrain-reloaded");
        yield return AfterShot;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void DescribeTerrain(StringBuilder text, TerrainSurfaceManager tsm, WorldDataManager data, Vector3 player)
    {
        var config = tsm.Config;
        text.AppendLine($"surface manager: tile {Try(() => tsm.GetActiveTileSize())}, height scale {Try(() => tsm.GetTerrainHeightScale())}, terraforming unit {Try(() => tsm.GetTerraformingUnit())} " +
                        $"({Try(() => tsm.GetWorldHeight(tsm.GetTerraformingUnit()) - tsm.GetWorldHeight(0))} m), hole {Try(() => tsm.GetGlobalHoleSize())}, world height of 0 {Try(() => tsm.GetWorldHeight(0))} and of 1 {Try(() => tsm.GetWorldHeight(1))}, " +
                        $"normalized of 35 m {Try(() => tsm.GetNormalizedHeight(35))}, bounds {Try(() => tsm.GetActiveBounds())} terrain bounds {Try(() => tsm.GetActiveTerrainBounds())}, active terrain '{Try(() => tsm.GetActiveTerain()?.name)}', " +
                        $"fields: unit {Try(() => tsm._terraformingUnit)} heightmap unit {Try(() => tsm._heightmapUnit)} global heightmap {Try(() => tsm._globalHeightmapResolution)} texture {Try(() => tsm._globalTextureResolution)} tile {Try(() => tsm._globalTileSize)} texel {Try(() => tsm._globalTexelSize)} streaming surface {Try(() => tsm._streamingTerrainSurfaceSize)} generator {Try(() => tsm._generatorHeightmapResolution)}+{Try(() => tsm._generatorHeightmapOffset)} bounds {Try(() => tsm._bounds)}, terrain '{Try(() => tsm.terrain?.name)}'");
        try {
        if (config != null)
        {
            text.AppendLine($"config '{config.name}': alphamap {config.alphamapResolution}, expand heightmap {config.expandHeightmap}, hmap update ratio {config.hmapUpdateRatio}, holes ratio {config.holesResetRatio}, unit multiplier {config.terraformingUnitMultiplier}, splat alpha {config.terraformingSplatAlpha}, rock slope layer {config.terraformingRockSlopeIndex} at {config.terraformingRockSlopeAngle}°, colour flatten {config.enableColorFlatten} {config.flattenMapResolution}");
            var layers = config.terraformingLayers;
            for (int i = 0; layers != null && i < layers.Count; i++)
                text.AppendLine($"    terraforming layer {layers[i].terrainType} → texture layer {layers[i].layerIndex}");
        }
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var defaults = tsm.GetDefaultTerrainLayers();
        for (int i = 0; defaults != null && i < defaults.Length; i++)
            text.AppendLine($"    texture layer {i}: '{defaults[i]?.name}' diffuse '{defaults[i]?.diffuseTexture?.name}' tile {defaults[i]?.tileSize}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var streaming = GameManagers.Get<WorldStreamingManager>();
        if (streaming != null)
            text.AppendLine($"streaming: current tile {streaming.CurrentTile}, resident {streaming.ResidentTilesCount} tiles {streaming.ResidentArea} = {streaming.ResidentBounds}, visible {streaming.GetVisibleTileArea()} {streaming.GetVisibleAreaSize()} m, active world tiles {streaming.GetActiveTiles()?.Count}, teleporting {streaming.IsTeleporting}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        text.AppendLine($"world data: tile {data.TileSize} m, cell {data.CellSize} m, {data.CellResolution} cells across, {data.FetchAllData()?.Count} tiles with data");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        foreach (var terrain in Object.FindObjectsOfType<Terrain>())
        {
            var td = terrain.terrainData;
            text.AppendLine($"terrain '{terrain.name}' under '{terrain.transform.parent?.name}' at {P(terrain.transform.position)}: heightmap {td?.heightmapResolution} size {td?.size}, alphamap {td?.alphamapResolution} × {td?.alphamapLayers} layers [{string.Join(", ", (td?.terrainLayers ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<TerrainLayer>(Array.Empty<TerrainLayer>())).Select(l => l?.name))}], trees {td?.treeInstanceCount}, details {td?.detailPrototypes?.Length}, draw foliage {terrain.drawTreesAndFoliage}, material '{terrain.materialTemplate?.name}' shader '{terrain.materialTemplate?.shader?.name}', layer {LayerMask.LayerToName(terrain.gameObject.layer)}");
        }
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var water = GameManagers.Get<WaterSurfaceManager>();
        if (water != null)
            text.AppendLine($"water: level {water.GetWaterLevel():0.00}, at the player {water.GetWaterLevel(player.x, player.z):0.00}, underwater there {water.IsUnderwater(player.x, player.z, 0)}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        text.AppendLine($"network world data: can send to anyone {GameManagers.Get<NetworkWorldDataManager>()?.CanSendToAnyone}");

        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var tools = Resources.FindObjectsOfTypeAll<HeightmapTool>();
        text.AppendLine($"heightmap tools loaded: {tools.Length}");
        foreach (var tool in tools)
            text.AppendLine($"    '{PathOf(tool.transform)}' (scene '{tool.gameObject.scene.name}'): {DescribeTool(tool)}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        foreach (var digging in Resources.FindObjectsOfTypeAll<SSSGame.Combat.DiggingMeleeObject>())
            text.AppendLine($"digging tool '{PathOf(digging.transform)}': size {digging.toolSize} {digging.terraformingOperation}, alt {digging.altToolSize} {digging.altOperation}, hole {digging.spawnHole}, durability {digging.hitDurabilityDamage}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        foreach (var level in Resources.FindObjectsOfTypeAll<StructureFootprintLevel>().Take(20))
            text.AppendLine($"footprint level '{PathOf(level.transform)}': {level.width} × {level.height}, blend {level.blendDistance}, kernel {level.useKernel}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        text.AppendLine($"footprint levels loaded: {Resources.FindObjectsOfTypeAll<StructureFootprintLevel>().Length}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var grids = Object.FindObjectsOfType<TerraformingGrid>();
        text.AppendLine($"terraforming grids in the world: {grids.Length}");
        foreach (var grid in grids.Take(10))
            text.AppendLine($"    '{PathOf(grid.transform)}': cells {grid.GetCellsCount()}, max difference {grid.maxLevelDifference}, error {grid.levelError}, reference {grid.ReferenceLevel:0.00}, result '{grid.ResultStructureTemplate?.name}'");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var splines = Object.FindObjectsOfType<TerraformingSplineInteraction>();
        text.AppendLine($"spline terraforming (paths, roads) in the world: {splines.Length}");
        foreach (var spline in splines.Take(20))
            text.AppendLine($"    '{PathOf(spline.transform)}': level as {spline.levelTerrainType}, overpaint {spline.overpaint} {spline.overpaintTerrainType} border {spline.overpaintWidthBorder}, ready state {spline.interactReadyState}, built state {spline.constructedState}, tool {DescribeTool(spline.heightmapTool)}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        foreach (var layout in Object.FindObjectsOfType<StructureSplineLayout>().Take(20))
            text.AppendLine($"spline layout '{PathOf(layout.transform)}': {layout.GetPointCount()} points, {layout.GetSectionCount()} sections, width {layout.SplineWidth} footprint {layout.FootprintWidth}, template '{layout.GetStructureTemplate()?.name}'");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        var roads = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.roadStatusEffects;
        for (int i = 0; roads != null && i < roads.Length; i++)
            text.AppendLine($"road effect {i}: surfaces {roads[i]?.roadSurfaceTypes} effect {roads[i]?.roadStatusEffect != null}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        text.AppendLine($"around the player: {DescribeMap(data, player, 64)}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
        try {
        text.AppendLine($"tile container at the player: {DescribeContainer(data, player)}");
        } catch (Exception e) { text.AppendLine($"section failed: {e.GetType().Name}: {e.Message.Split((char)10)[0]}"); }
    }

    private static string Try(Func<object?> read)
    {
        try { return string.Format(CultureInfo.InvariantCulture, "{0}", read()); }
        catch (Exception e) { return $"<{e.GetType().Name}: {e.Message.Split((char)10)[0]}>"; }
    }

    private static string DescribeTool(HeightmapTool? tool)
    {
        if (tool == null) return "none";
        return $"radius {tool.radius} distance {tool.distance} raise {tool.raiseHeight} dig {tool.digDepth} vegetation mask {tool.setVegetationMask} clear vegetation {tool.clearVegetation} " +
               $"set type {tool.setTerrainType} {tool.terrainType} needs type {tool.requiresTerrainType} {tool.requiredTerrainType} ignore paint {tool.ignorePaintType} {tool.ignoreTerrainType} slope {tool.checkTerrainSlope} {tool.maxTerrainSlopeDegrees}° " +
               $"items {tool.checkItemInstances} {tool.checkItemInstancesRadius} '{tool.itemsToCheckTable?.name}' biome {tool.setBiomeType} {tool.biomeType} bias {tool.biasTerraformingWeights} network {tool.propagateOnNetwork} validate only {tool.onlyValidate} " +
               $"navigation {tool.updateNavigation} validate height {tool.validateHeight} water offset {tool.waterHeightOffset} max height {tool.maxTerrainHeight} overlap layers {tool.overlapLayers.value}";
    }

    // The terraforming map of the tile under a point: its resolution, and the cells within a radius (in cells) counted by kind
    // of ground, cleared vegetation and changed heights.
    private static string DescribeMap(WorldDataManager data, Vector3 at, int radius)
    {
        try
        {
            var tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
            var container = TerrainContainer(tile);
            var map = container?.terraformingMap;
            if (tile == null || map == null) return $"no terraforming map (tile {tile != null}, container {container != null})";
            int res = map.Resolution;
            int cx = (int)((at.x - tile.area.x) / tile.area.width * res);
            int cy = (int)((at.z - tile.area.y) / tile.area.height * res);
            var kinds = new Dictionary<string, int>();
            int cleared = 0, changed = 0, cells = 0;
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(res - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(res - 1, cx + radius); x++)
                {
                    int flags = 0;
                    map.GetData(x, y, out flags);
                    cells++;
                    string kind = TerraformingMap.GetTerrainType(flags).ToString();
                    kinds[kind] = kinds.TryGetValue(kind, out int n) ? n + 1 : 1;
                    if (TerraformingMap.IsClearVegetationSet(flags)) cleared++;
                    if (TerraformingMap.IsHeightmapModified(flags)) changed++;
                }
            return $"map of tile {tile.Id} area {tile.area}: {res} × {res} ({tile.area.width / res:0.###} m a cell), cell ({cx}, {cy}); within {radius}: {cells} cells, kinds [{string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}"))}], vegetation cleared {cleared}, heights changed {changed}, locked {map.IsLocked}";
        }
        catch (Exception e) { return $"map failed: {e.Message}"; }
    }

    private static Dictionary<(int, int), int> MapCells(WorldDataManager data, Vector3 at, int radius)
    {
        var cells = new Dictionary<(int, int), int>();
        var tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
        var map = TerrainContainer(tile)?.terraformingMap;
        if (tile == null || map == null) return cells;
        int res = map.Resolution;
        int cx = (int)((at.x - tile.area.x) / tile.area.width * res);
        int cy = (int)((at.z - tile.area.y) / tile.area.height * res);
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(res - 1, cy + radius); y++)
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(res - 1, cx + radius); x++)
            {
                int flags = 0;
                map.GetData(x, y, out flags);
                cells[(x, y)] = flags;
            }
        return cells;
    }

    private static string DiffCells(Dictionary<(int, int), int> before, Dictionary<(int, int), int> after)
    {
        var changed = after.Where(kv => before.TryGetValue(kv.Key, out int was) && was != kv.Value).ToList();
        if (changed.Count == 0) return $"no cell of {after.Count} changed";
        int minX = changed.Min(c => c.Key.Item1), maxX = changed.Max(c => c.Key.Item1), minY = changed.Min(c => c.Key.Item2), maxY = changed.Max(c => c.Key.Item2);
        return $"{changed.Count} of {after.Count} cells changed, x {minX}..{maxX} y {minY}..{maxY}, e.g. {string.Join(", ", changed.Take(4).Select(c => $"({c.Key.Item1},{c.Key.Item2}) {before[c.Key]:X}→{c.Value:X} {TerraformingMap.GetTerrainType(c.Value)}"))}";
    }

    private static TerrainTileDataContainer? TerrainContainer(WorldTileData? tile)
    {
        var slots = tile?._dataSlots;
        for (int i = 0; slots != null && i < slots.Length; i++)
            if (slots[i]?.TryCast<TerrainTileDataContainer>() is { } container) return container;
        return null;
    }

    private static string DescribeContainer(WorldDataManager data, Vector3 at)
    {
        try
        {
            var tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
            var c = TerrainContainer(tile);
            if (c == null) return "no terrain container";
            return $"tile {tile!.Id}: container dirty {c.IsDirty}, heightmap persistent {c.IsHeightmapPersistent()} generated {c.IsHeightmapGenerated()}, heightmap {c.heightMap?.Resolution}, range {c.normalizedHeightmapRange}, map {c.terraformingMap?.Resolution}";
        }
        catch (Exception e) { return $"container failed: {e.Message}"; }
    }

    private static float[,] Heights(Vector3 centre)
    {
        var heights = new float[TerrainGrid, TerrainGrid];
        for (int i = 0; i < TerrainGrid; i++)
            for (int j = 0; j < TerrainGrid; j++)
                heights[i, j] = TerrainSampler.SampleHeight(centre + new Vector3(i - TerrainGrid / 2, 0, j - TerrainGrid / 2));
        return heights;
    }

    private static string Summary(float[,] now, float[,] then)
    {
        float min = float.MaxValue, max = float.MinValue, sum = 0;
        for (int i = 0; i < TerrainGrid; i++)
            for (int j = 0; j < TerrainGrid; j++)
            {
                float d = now[i, j] - then[i, j];
                min = Math.Min(min, d);
                max = Math.Max(max, d);
                sum += Math.Abs(d);
            }
        return string.Create(CultureInfo.InvariantCulture, $"centre {now[TerrainGrid / 2, TerrainGrid / 2]:0.000} (Δ {now[TerrainGrid / 2, TerrainGrid / 2] - then[TerrainGrid / 2, TerrainGrid / 2]:+0.000;-0.000}), Δ {min:+0.000;-0.000}..{max:+0.000;-0.000}, mean |Δ| {sum / (TerrainGrid * TerrainGrid):0.000}");
    }

    private static float[,] ReadFull(TerrainToolData data)
    {
        var values = new float[data.size.y, data.size.x];
        for (int row = 0; row < data.size.y; row++)
            for (int column = 0; column < data.size.x; column++)
                values[row, column] = data.GetFullHeightmap(row, column);
        return values;
    }

    // The ground a ray finds under a point (the terrain's collider), beside the game's sampler.
    private static string RayGround(Vector3 at)
    {
        var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 50f, at.z), Vector3.down, 200f);
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        var ground = Enumerable.Range(0, hits.Length).Select(i => hits[i]).Where(h => h.collider != null && h.collider.gameObject.layer == terrainLayer).OrderBy(h => h.distance).ToList();
        string ray = ground.Count > 0 ? $"{ground[0].point.y:0.000} on '{ground[0].collider.name}'" : "nothing";
        return $"ray {ray}, sampler {TerrainSampler.SampleHeight(at):0.000}";
    }

    private static string Colliders(Vector3 at) =>
        string.Join(", ", Physics.OverlapSphere(at, 4f).Where(c => c != null).GroupBy(c => $"{c.name} ({LayerMask.LayerToName(c.gameObject.layer)})").Select(g => $"{g.Key} ×{g.Count()}").Take(20));
}
