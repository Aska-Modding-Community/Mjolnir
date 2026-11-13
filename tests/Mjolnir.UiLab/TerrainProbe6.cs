using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SandSailorStudio.WorldGen;
using UnityEngine;
using BiomeType = AwesomeTechnologies.VegetationSystem.BiomeType;
using VegetationPackagePro = AwesomeTechnologies.VegetationSystem.VegetationPackagePro;
using VegetationStudioManager = AwesomeTechnologies.VegetationStudio.VegetationStudioManager;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 6" scenario, for the ground cover and biomes: which biomes the game has (its vegetation packages, the
/// biome objects of the world, the biome ids in the loaded tiles' biome maps); a biome painted over 20 × 20 m by data written
/// directly with the biome target, read back from the biome map and the cell's presence mask, looked at before and after the
/// vegetation is refreshed; whether grass grows back on dirt whose cleared-vegetation flag alone is taken off; then the world
/// saved and loaded again, and both patches read back. It changes the ground of the save and saves it: back it up.
/// </summary>
internal static partial class Autopilot
{
    private const TerrainToolData.TargetFlags Every = TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN | TerrainToolData.TargetFlags.VEGETATION | TerrainToolData.TargetFlags.BIOMES;

    private static IEnumerator<float> TerrainProbe6()
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
            Log("Probe6: no world");
            yield break;
        }
        var home = character.transform.position;

        // A. Which biomes the game has.
        Log($"Probe6: vegetation packages: {Try(() => string.Join("; ", Resources.FindObjectsOfTypeAll<VegetationPackagePro>().Select(p => $"'{p.PackageName}' ({p.name}) {p.BiomeType}={(int)p.BiomeType} order {p.BiomeSortOrder} items {p.VegetationInfoList?.Count}")))}");
        Log($"Probe6: vegetation sets: {Try(() => string.Join("; ", Resources.FindObjectsOfTypeAll<BiomeVegetationSet>().Select(s => $"'{s.name}' id {s.id} packages [{string.Join(", ", Enumerable.Range(0, s.packages?.Count ?? 0).Select(i => $"{s.packages![i]?.PackageName}:{s.packages[i]?.BiomeType}"))}]")))}");
        var biomes = GameManagers.Get<BiomesManager>();
        Log($"Probe6: biomes manager: local set '{Try(() => biomes?._localPlayerVegetationSet?.name)}', map resolution {Try(() => biomes?.Configuration?.biomeMapResolution)}");
        Log($"Probe6: biome objects: {Try(() => string.Join("; ", Object.FindObjectsOfType<Biome>().Select(b => $"'{b.name}' id {b.id} mask type {b.GetMaskTypeId()} at {P(b.transform.position)}").Take(40)))}");
        Log($"Probe6: biome ids in the tiles around: {BiomeHistogram(data, home, 1)}");
        Log($"Probe6: under the player: {BiomeSample(data, home)}");

        // B. A natural grassy spot, free of buildings.
        var spots = new List<Vector3>();
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f, 200f })
            for (int turn = 0; turn < 360; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.IsLoaded(at.x, at.z) || !Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                bool natural = true;
                for (int i = -12; i <= 12 && natural; i += 6)
                    for (int j = -12; j <= 12 && natural; j += 6)
                        natural = Landscape.KindAt(at.x + i, at.z + j) == GroundKind.Natural && !Landscape.IsCleared(at.x + i, at.z + j);
                if (!natural) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 26, 26), LandscapeOperation.Paint) != LandscapeResult.Done) continue;
                spots.Add(at);
            }
        Log($"Probe6: {spots.Count} natural spots");
        if (spots.Count == 0) yield break;
        var spot = spots[0];
        foreach (float wait in GoAbove(character, home, spot)) yield return wait;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 26f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        yield return 2f;
        string before = BiomeSample(data, spot);
        Log($"Probe6: spot {P(spot)}: {before}; around {BiomeHistogram(data, spot, 0)}");
        Shot("probe6-biome-before");
        yield return AfterShot;

        // C. The biome painted by a direct write with the biome target only.
        int present = BiomeIdAt(data, spot);
        var painted = present == (int)BiomeType.Swamp ? BiomeType.Meadow : BiomeType.Swamp;
        TerrainToolData? d = null;
        string write = Try(() =>
        {
            d = NewEvery(tsm, spot, 20f);
            d.biomeType = painted;
            int n = d.size.x;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    d.SetModified(r, c, TerrainToolData.TargetFlags.BIOMES);
            tsm.ApplyTerrainToolData(d, -1);
            return $"{n}×{n} samples, area {d.worldArea}, biome {painted}";
        });
        yield return 3f;
        Log($"Probe6: biome written ({write}): centre {BiomeSample(data, spot)}; 9 m off {BiomeSample(data, spot + new Vector3(9, 0, 0))}; 13 m off {BiomeSample(data, spot + new Vector3(13, 0, 0))}; kind {Landscape.KindAt(spot.x, spot.z)}, cleared {Landscape.IsCleared(spot.x, spot.z)}");
        Shot("probe6-biome-written");
        yield return AfterShot;
        yield return 5f;
        Log($"Probe6: 8 s after the write: {BiomeSample(data, spot)}");
        Shot("probe6-biome-later");
        yield return AfterShot;
        string refreshed = Try(() => { VegetationStudioManager.RefreshTerrainArea(new Bounds(spot, new Vector3(30, 80, 30))); return "refreshed"; });
        yield return 4f;
        Log($"Probe6: {refreshed}: {BiomeSample(data, spot)}");
        Shot("probe6-biome-refreshed");
        yield return AfterShot;

        // D. Dirt whose cleared flag alone is taken off: does the grass grow back?
        var bare = spots.Count > 1 ? spots.FirstOrDefault(s => Vector3.Distance(s, spot) > 30f) : default;
        if (bare == default) bare = spot + new Vector3(0, 0, 24f);
        bare.y = Landscape.HeightAt(bare.x, bare.z);
        camera.Position = bare + new Vector3(0, 14f, -8f);
        camera.Rotation = Quaternion.LookRotation(bare - camera.Position, Vector3.up);
        if (Vector3.Distance(character.transform.position, bare) > 60f)
            foreach (float wait in GoAbove(character, home, bare)) yield return wait;
        yield return 2f;
        Shot("probe6-cover-natural");
        yield return AfterShot;
        Log($"Probe6: cover at {P(bare)}: {Flags(tsm, bare, 8f, "dirt, cleared", TerraformingMap.TerrainType.DIRT, true)}");
        yield return 3f;
        Shot("probe6-cover-dirt");
        yield return AfterShot;
        Log($"Probe6: cover: {Flags(tsm, bare, 8f, "dirt, not cleared", TerraformingMap.TerrainType.DIRT, false)}");
        yield return 3f;
        Shot("probe6-cover-dirt-uncleared");
        yield return AfterShot;
        Try(() => { VegetationStudioManager.RefreshTerrainArea(new Bounds(bare, new Vector3(16, 80, 16))); return ""; });
        yield return 4f;
        Shot("probe6-cover-dirt-uncleared-refreshed");
        yield return AfterShot;
        // A second patch beside it: dirt, then natural with the flag still on (the kind back, the grass not).
        var beside = bare + new Vector3(10f, 0, 0);
        beside.y = Landscape.HeightAt(beside.x, beside.z);
        Log($"Probe6: cover beside: {Flags(tsm, beside, 6f, "dirt, cleared", TerraformingMap.TerrainType.DIRT, true)}");
        yield return 2f;
        Log($"Probe6: cover beside: {Flags(tsm, beside, 6f, "natural, still cleared", TerraformingMap.TerrainType.NATURAL, true)}");
        yield return 4f;
        Shot("probe6-cover-natural-cleared");
        yield return AfterShot;

        // E. Saved and loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage == null)
        {
            Log("Probe6: no storage manager");
            yield break;
        }
        var task = storage.SaveCurrentSession();
        foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
        Log($"Probe6: saved, {storage.LastSaveOperationCode}; biome at the spot {BiomeSample(data, spot)}");
        camera.Dispose();
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
        character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var data2 = GameManagers.Get<WorldDataManager>();
        if (character == null || data2 == null)
        {
            Log("Probe6: no world after loading again");
            yield break;
        }
        foreach (float wait in GoAbove(character, character.transform.position, spot)) yield return wait;
        var again = Overhead(spot);
        again.Position = spot + new Vector3(0, 26f, -14f);
        again.Rotation = Quaternion.LookRotation(spot - again.Position, Vector3.up);
        yield return 4f;
        Log($"Probe6: after loading again: spot {BiomeSample(data2, spot)}; cover {Landscape.KindAt(bare.x, bare.z)}, cleared {Landscape.IsCleared(bare.x, bare.z)}; beside {Landscape.KindAt(beside.x, beside.z)}, cleared {Landscape.IsCleared(beside.x, beside.z)}");
        Shot("probe6-biome-reloaded");
        yield return AfterShot;
        again.Position = bare + new Vector3(5f, 14f, -8f);
        again.Rotation = Quaternion.LookRotation(bare + new Vector3(5f, 0, 0) - again.Position, Vector3.up);
        yield return 2f;
        Shot("probe6-cover-reloaded");
        yield return AfterShot;
        again.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static TerrainToolData NewEvery(TerrainSurfaceManager tsm, Vector3 at, float side)
    {
        int n = (int)MathF.Ceiling(side / tsm.GetActiveTileSize()) + 1;
        var d = new TerrainToolData(n, n, tsm.GetActiveTileSize(), Every);
        d.SetPosition(at.x, at.z);
        d.ClearFlags();
        tsm.FillTerrainToolData(d);
        return d;
    }

    // Sets the kind and the cleared flag of a square, heights untouched, and tells the flags at its middle before and after.
    private static string Flags(TerrainSurfaceManager tsm, Vector3 at, float side, string what, TerraformingMap.TerrainType kind, bool cleared)
    {
        return Try(() =>
        {
            var d = NewEvery(tsm, at, side);
            int n = d.size.x, middle = n / 2;
            int was = d.GetTerraformingFlags(middle, middle);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int flags = d.GetTerraformingFlags(r, c);
                    TerraformingMap.SetTerrainType(ref flags, kind);
                    TerraformingMap.SetVegetationMask(ref flags, cleared);
                    d.SetTerraformingFlags(r, c, flags);
                    d.SetModified(r, c, TerrainToolData.TargetFlags.TERRAIN | TerrainToolData.TargetFlags.VEGETATION);
                }
            tsm.ApplyTerrainToolData(d, -1);
            return $"{what}: middle 0x{was:X} → 0x{d.GetTerraformingFlags(middle, middle):X}";
        });
    }

    // The tile's biome map and the sample under a point (index row-major, as the game's biome map operation writes it).
    private static BiomeMap? BiomeMapAt(WorldDataManager data, Vector3 at, out WorldTileData? tile, out int index)
    {
        index = -1;
        tile = data.OpenData(at.x, at.z, DataAccessMode.FETCH);
        var map = tile?.WorldMapTile?.GetBiomeMap();
        if (tile == null || map == null) return null;
        int res = map.Resolution;
        int x = Math.Clamp((int)((at.x - tile.area.m_XMin) / tile.area.m_Width * res), 0, res - 1);
        int y = Math.Clamp((int)((at.z - tile.area.m_YMin) / tile.area.m_Height * res), 0, res - 1);
        index = y * res + x;
        return map;
    }

    private static int BiomeIdAt(WorldDataManager data, Vector3 at)
    {
        try
        {
            var map = BiomeMapAt(data, at, out _, out int index);
            return map == null ? -1 : map.data[index].biomeId;
        }
        catch (Exception) { return -1; }
    }

    private static string BiomeSample(WorldDataManager data, Vector3 at)
    {
        return Try(() =>
        {
            var map = BiomeMapAt(data, at, out var tile, out int index);
            if (map == null || tile == null) return "no biome map";
            var sample = map.data[index];
            string mask = Try(() =>
            {
                var handler = data.GetDataHandler<BiomeProceduralDataHandler>(WorldDataSlot.BIOME);
                int cx = (int)((at.x - tile.area.m_XMin) / tile.area.m_Width * data.CellResolution);
                int cy = (int)((at.z - tile.area.m_YMin) / tile.area.m_Height * data.CellResolution);
                return handler == null ? "none" : handler.GetBiomePresenceMask(tile, cx, cy).ToString("X", CultureInfo.InvariantCulture);
            });
            return $"biome {(BiomeType)sample.biomeId}={sample.biomeId} topology {sample.topologyFeatureId} (map {map.Resolution}², set {map.biomeSetId}, default {map.defaultBiomeId}, modified {map.ModifiedFlags}); cell mask 0x{mask}";
        });
    }

    // The biome ids in the tiles round a point (reach 1: 3 × 3 tiles), with how many samples each.
    private static string BiomeHistogram(WorldDataManager data, Vector3 at, int reach)
    {
        return Try(() =>
        {
            var counts = new Dictionary<int, int>();
            float size = data.TileSize;
            int tiles = 0;
            for (int i = -reach; i <= reach; i++)
                for (int j = -reach; j <= reach; j++)
                {
                    var tile = data.OpenData(at.x + i * size, at.z + j * size, DataAccessMode.FETCH);
                    var map = tile?.WorldMapTile?.GetBiomeMap();
                    if (map == null) continue;
                    tiles++;
                    int length = map.Resolution * map.Resolution;
                    var samples = map.data;
                    for (int k = 0; k < length; k++)
                    {
                        int id = samples[k].biomeId;
                        counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
                    }
                }
            return $"{tiles} tiles: " + string.Join(", ", counts.OrderByDescending(p => p.Value).Select(p => $"{(BiomeType)p.Key}={p.Key} ×{p.Value}"));
        });
    }
}
