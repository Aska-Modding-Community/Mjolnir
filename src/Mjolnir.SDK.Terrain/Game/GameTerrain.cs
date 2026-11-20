using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mjolnir.SDK.Threading;
using SSSGame;
using SSSGame.Network;
using SandSailorStudio;
using SandSailorStudio.Render;
using SandSailorStudio.TerrainTools;
using SandSailorStudio.WorldGen;
using UnityEngine;
using BiomeType = AwesomeTechnologies.VegetationSystem.BiomeType;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's ground for the model. Heights and normals come from the game's terrain sampler (its streamed tiles), the kind
/// of ground from the world data's terraforming map (an int per half metre: the kind in its low bits, cleared vegetation,
/// changed heights), the biome from the world tile's biome map (one per 4 metres) named by the vegetation set of the part of
/// the world the player is in, the water from the water surface manager. Changes are made with a heightmap tool of the SDK's own on an
/// object that lives the whole game, as the game adds one to a building whose footprint it levels: so they are applied,
/// saved and sent to the other players by the game's own code.
/// </summary>
internal sealed class GameTerrain : ITerrainSource
{
    private static Harmony? s_harmony;

    private TerrainSurfaceManager? _surface;
    private WorldDataManager? _data;
    private WaterSurfaceManager? _water;
    private bool _available;
    private GameObject? _toolObject;
    private HeightmapTool? _tool;
    private BiomesManager? _biomes;
    private IntPtr _biomeSet;
    private LandscapeBiome[] _biomeList = [];

    public static void Start()
    {
        try
        {
            Dispatcher.Hook();
            var terrain = new GameTerrain();
            Landscape.Source = terrain;
            Action refresh = terrain.Refresh;
            GameLoop.Update += () => GameGuard.RunLogOnce(Landscape.Platform, "looking up the game's ground", refresh);
            GameLoop.Update += () => GameGuard.RunLogOnce(Landscape.Platform, "drawing the outlines", Landscape.Tick);
            s_harmony = new Harmony("mjolnir.sdk.terrain");
            int patched = s_harmony.CreateClassProcessor(typeof(TerrainPatches)).Patch()?.Count ?? 0;
            try
            {
                patched += s_harmony.CreateClassProcessor(typeof(HarvestPatches)).Patch()?.Count ?? 0;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"Harvesting gives the player what the game gives: its patches failed: {e.Message}");
            }
            try
            {
                patched += s_harmony.CreateClassProcessor(typeof(PlacementPatches)).Patch()?.Count ?? 0;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"No kinds of the world can be placed: their patches failed: {e.Message}");
            }
            try
            {
                patched += s_harmony.CreateClassProcessor(typeof(RegrowthPatches)).Patch()?.Count ?? 0;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"Placed bushes neither keep nor give again once gathered: their patches failed: {e.Message}");
            }
            TerrainCommands.Register();
            SdkLog.Info(Landscape.Platform, $"Terrain module started: {patched} game methods patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Landscape.Platform, $"Terrain module not started: {e}");
        }
    }

    /// <summary>The SDK's own tool, told apart from the game's in the patch.</summary>
    internal static IntPtr ToolPointer { get; private set; }

    // A world is loaded when the game's ground and world data are there and the local player stands in it: in the main menu
    // the menu's scene has a ground of its own, but no player.
    private void Refresh()
    {
        _surface = Alive(_surface) ?? GameManagers.Get<TerrainSurfaceManager>();
        _data = Alive(_data) ?? GameManagers.Get<WorldDataManager>();
        _water = Alive(_water) ?? GameManagers.Get<WaterSurfaceManager>();
        _biomes = Alive(_biomes) ?? GameManagers.Get<BiomesManager>();
        bool player;
        try { player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter != null; }
        catch (Exception) { player = false; } // LocalPlayer throws without a session
        _available = _surface != null && _data != null && player;
    }

    private static T? Alive<T>(T? manager) where T : Object => manager != null ? manager : null;

    public bool IsAvailable => _available;

    public bool IsLoaded(float x, float z)
    {
        if (_surface == null) return false;
        var bounds = _surface.GetActiveBounds();
        if (x < bounds.m_XMin || z < bounds.m_YMin || x >= bounds.m_XMin + bounds.m_Width || z >= bounds.m_YMin + bounds.m_Height) return false;
        return TryGetHeight(x, z, out _);
    }

    public bool TryGetHeight(float x, float z, out float height)
    {
        var point = At(x, 0, z);
        return TerrainSampler.TrySampleHeight(ref point, out height);
    }

    public Vector3 Normal(float x, float z) => TerrainSampler.GetTerrainNormal(At(x, 0, z));

    public GroundKind Kind(float x, float z) => FlagsAt(x, z) is { } flags ? KindOf(TerraformingMap.GetTerrainType(flags)) : GroundKind.Natural;

    public bool IsCleared(float x, float z) => FlagsAt(x, z) is { } flags && TerraformingMap.IsClearVegetationSet(flags);

    public float WaterLevel => _water != null ? _water.GetWaterLevel() : 0f;

    public bool IsUnderwater(float x, float z) => _water != null && _water.IsUnderwater(x, z, 0f);

    // Buildings' solid colliders near the area, their boxes on the ground overlapping the turned rectangle: the game's own
    // check of a change does not look at buildings (seen in game: it allowed levelling among the houses).
    public string? BuildingIn(LandscapeArea area) => StandingIn(area, collider => collider.GetComponentInParent<Structure>()?.name);

    // The fixed places of the world: a den (its creature and the stones it is struck through), or the rocks of a place
    // stamped into the world as it was made (a den's own, a ruin's). Neither moves with the ground: a den's rocks were
    // left 0.66 m in the air over ground dug 2 m under it (seen in game).
    public string? LandmarkIn(LandscapeArea area) => StandingIn(area, collider =>
        collider.GetComponentInParent<Den>()?.name ?? collider.GetComponentInParent<BiomeStamp>()?.name);

    // How far round what stands on the ground its footprint reaches, in metres: the ground right under a wall's foot stays.
    private const float FootprintMargin = 0.25f;

    // Every solid collider near the area of a building or a fixed place, as the rectangle it covers on the ground: a box's
    // or a mesh's own box turned with it where it stands upright (a house turned 30° covers its walls, not the square round
    // them), else the box of its bounds along the world's axes (a rock lying askew). Fitting, a building the game levels the
    // ground for gives that footprint instead, at the height it levels it to (its own levelling at its making: the
    // structure's height, the footprint's width along its x and depth along its z, its blend), and a fixed place's mesh
    // tells its underside by a ray from under it.
    public IReadOnlyList<GroundFootprint> ObstaclesIn(LandscapeArea area, bool buildings, bool landmarks, bool fit)
    {
        var found = new List<GroundFootprint>();
        if (!buildings && !landmarks) return found;
        float halfX = area.Width / 2 + area.Blend, halfZ = area.Depth / 2 + area.Blend;
        float radius = MathF.Sqrt(halfX * halfX + halfZ * halfZ) + 1f;
        TryGetHeight(area.Center.x, area.Center.z, out float ground);
        var levelled = new HashSet<IntPtr>();
        foreach (var collider in Physics.OverlapSphere(At(area.Center.x, ground, area.Center.z), radius))
        {
            if (collider == null || collider.isTrigger) continue;
            var structure = buildings ? collider.GetComponentInParent<Structure>() : null;
            if (structure != null)
            {
                if (fit && Levelled(structure, found, levelled)) continue;
                found.Add(Footprint(collider, structure.name));
                continue;
            }
            if (!landmarks) continue;
            string? name = collider.GetComponentInParent<Den>()?.name ?? collider.GetComponentInParent<BiomeStamp>()?.name;
            if (name == null) continue;
            var footprint = Footprint(collider, name);
            if (fit && collider.TryCast<MeshCollider>() != null) footprint = footprint with { Underside = UndersideOf(collider) };
            found.Add(footprint);
        }
        return found;
    }

    // A building's levelled footprints, added once per building; whether it has any (then its colliders are not kept). Its
    // own `StructureFootprintLevel`s (not those of buildings set on it): the game levels the ground there to the building's
    // height — a house's and a warehouse's ground was at it within a millimetre in the test save. Where the game levels it
    // itself as the building is made (`autoLevelWithOwnFootprint`: wells, lanterns) the building says how: filling and
    // cutting, or only filling (a well's ground 0.31 m over its foot was left so) or only cutting; else filling and cutting.
    private static bool Levelled(Structure structure, List<GroundFootprint> found, HashSet<IntPtr> done)
    {
        if (done.Contains(structure.Pointer)) return true;
        var levels = structure.GetComponentsInChildren<StructureFootprintLevel>();
        if (levels == null) return false;
        bool any = false;
        var operation = structure.autoLevelWithOwnFootprint ? structure.autoLevelOperation : TerraformingToolOperation.LEVEL;
        foreach (var level in levels)
        {
            if (level == null || level.width <= 0 || level.height <= 0) continue;
            if (level.GetComponentInParent<Structure>() is not { } owner || owner.Pointer != structure.Pointer) continue;
            var t = level.transform;
            var at = t.position;
            var right = t.right;
            float length = MathF.Sqrt(right.x * right.x + right.z * right.z);
            float cos = length > 0.001f ? right.x / length : 1, sin = length > 0.001f ? right.z / length : 0;
            found.Add(new GroundFootprint(structure.name, at.x, at.z, level.width / 2, level.height / 2, cos, sin)
            {
                Level = structure.transform.position.y,
                LevelsUp = operation != TerraformingToolOperation.DIG,
                LevelsDown = operation != TerraformingToolOperation.RAISE,
            });
            any = true;
        }
        if (any) done.Add(structure.Pointer);
        return any;
    }

    // The lowest face of a collider over a point, by a ray up from under it: the underside of a den's rocks where they
    // overhang, their foot in the ground where they stand on it; null where the collider is not over the point.
    private static Func<float, float, float?> UndersideOf(Collider collider)
    {
        float from = collider.bounds.min.y - 1f, reach = collider.bounds.size.y + 2f;
        return (x, z) =>
        {
            if (collider == null) return null;
            var ray = new Ray(new Vector3(x, from, z), Vector3.up);
            return collider.Raycast(ray, out var hit, reach) ? hit.point.y : null;
        };
    }

    private static GroundFootprint Footprint(Collider collider, string name)
    {
        var t = collider.transform;
        Bounds? local = null;
        if (collider.TryCast<BoxCollider>() is { } box) local = new Bounds(box.center, box.size);
        else if (collider.TryCast<MeshCollider>()?.sharedMesh is { } mesh) local = mesh.bounds;
        var up = t.up;
        if (local is { } own && up.y > 0.95f)
        {
            var middle = t.TransformPoint(own.center);
            var scale = t.lossyScale;
            var right = t.right;
            float length = MathF.Sqrt(right.x * right.x + right.z * right.z);
            if (length > 0.001f)
                return new GroundFootprint(name, middle.x, middle.z,
                    MathF.Abs(own.extents.x * scale.x) + FootprintMargin, MathF.Abs(own.extents.z * scale.z) + FootprintMargin,
                    right.x / length, right.z / length);
        }
        var bounds = collider.bounds;
        return new GroundFootprint(name, bounds.center.x, bounds.center.z, bounds.extents.x + FootprintMargin, bounds.extents.z + FootprintMargin, 1, 0);
    }

    // The name of the first solid collider near the area whose box on the ground overlaps the turned rectangle and which
    // <paramref name="nameOf"/> names.
    private string? StandingIn(LandscapeArea area, Func<Collider, string?> nameOf)
    {
        float halfX = area.Width / 2 + area.Blend, halfZ = area.Depth / 2 + area.Blend;
        float radius = MathF.Sqrt(halfX * halfX + halfZ * halfZ) + 1f;
        TryGetHeight(area.Center.x, area.Center.z, out float ground);
        var colliders = Physics.OverlapSphere(At(area.Center.x, ground, area.Center.z), radius);
        double turn = -area.Rotation * Math.PI / 180;
        float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);
        foreach (var collider in colliders)
        {
            if (collider == null || collider.isTrigger || nameOf(collider) is not { } name) continue;
            var box = collider.bounds;
            Vector3 c = box.center, e = box.extents;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float dx = c.x + ((i & 1) == 0 ? -e.x : e.x) - area.Center.x, dz = c.z + ((i & 2) == 0 ? -e.z : e.z) - area.Center.z;
                float lx = dx * cos - dz * sin, lz = dx * sin + dz * cos;
                minX = Math.Min(minX, lx);
                maxX = Math.Max(maxX, lx);
                minZ = Math.Min(minZ, lz);
                maxZ = Math.Max(maxZ, lz);
            }
            if (maxX >= -halfX && minX <= halfX && maxZ >= -halfZ && minZ <= halfZ) return name;
        }
        return null;
    }

    public bool TryGetPlayer(out Vector3 position, out Vector3 forward)
    {
        position = forward = default;
        PlayerCharacter? character;
        try { character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter; }
        catch (Exception) { return false; }
        if (character == null) return false;
        var transform = character.transform;
        position = transform.position;
        forward = transform.forward;
        return true;
    }

    public LandscapeResult Apply(in LandscapeEdit edit)
    {
        var tool = Tool();
        Configure(tool, edit);
        var center = Center(edit.Area);
        if (edit.Operation == LandscapeOperation.Level) tool.SetReferenceHeight(edit.Amount, false);
        try
        {
            var op = OperationOf(edit.Operation);
            TerraformingToolResult result;
            if (edit.Area.Brush is { } brush)
            {
                tool.SetSize(SizeOf(brush));
                result = tool.Run(op, center);
            }
            else result = tool.RunOnArea(op, edit.Area.Width, edit.Area.Depth, edit.Area.Blend, center, edit.Area.Rotation);
            if (result != TerraformingToolResult.SUCCESS && !Landscape.WasCancelled) SdkLog.Info(Landscape.Platform, $"The game refused {edit.Operation} over {edit.Area}: {result}");
            return ResultOf(result);
        }
        finally
        {
            if (edit.Operation == LandscapeOperation.Level) tool.ResetHeightReference();
        }
    }

    public LandscapeResult Check(in LandscapeEdit edit)
    {
        var tool = Tool();
        Configure(tool, edit);
        if (edit.Area.Brush is { } brush) tool.SetSize(SizeOf(brush));
        if (edit.Operation == LandscapeOperation.Level) tool.SetReferenceHeight(edit.Amount, false);
        try { return ResultOf(tool.Check(OperationOf(edit.Operation), Center(edit.Area))); }
        finally
        {
            if (edit.Operation == LandscapeOperation.Level) tool.ResetHeightReference();
        }
    }

    private const TerrainToolData.TargetFlags Targets = TerrainToolData.TargetFlags.HEIGHTMAP | TerrainToolData.TargetFlags.TERRAIN | TerrainToolData.TargetFlags.VEGETATION;

    // A sample marked with this target gives the biome map's 4 m cell it lies in the data's one biome (the game's routine
    // applies it only beside the terrain or vegetation target).
    private const TerrainToolData.TargetFlags WithBiomes = Targets | TerrainToolData.TargetFlags.BIOMES;

    // The game's own data over a square, filled from its ground: with the terrain target beside the heights, else applying it
    // skips every tile (the tiles' code goes on only with the terrain or vegetation target, measured). The game spans the data
    // n samples' width round the position it is given and takes its samples from the heightmap's own grid, the first at the
    // grid point its left edge rounds to: so the position is chosen to put that edge on a grid point, and sample (row, column)
    // lies at (originX + column × step, originZ + row × step) exactly.
    private TerrainToolData? NewData(float centerX, float centerZ, float side, TerrainToolData.TargetFlags targets, out float originX, out float originZ, out float step)
    {
        originX = originZ = step = 0;
        if (_surface == null) return null;
        step = _surface.GetActiveTileSize();
        if (!(step > 0)) return null;
        int n = (int)MathF.Ceiling(side / step) + 2;
        originX = MathF.Floor((centerX - (n - 1) * step / 2) / step) * step;
        originZ = MathF.Floor((centerZ - (n - 1) * step / 2) / step) * step;
        var data = new TerrainToolData(n, n, step, targets);
        data.SetPosition(originX + n * step / 2, originZ + n * step / 2);
        data.ClearFlags();
        _surface.FillTerrainToolData(data);
        return data;
    }

    public LandscapeSamples? Read(float centerX, float centerZ, float side)
    {
        var data = NewData(centerX, centerZ, side, Targets, out float originX, out float originZ, out float step);
        if (data == null || _surface == null) return null;
        int n = data.size.x;
        var samples = new LandscapeSamples(centerX, centerZ, side, n, originX, originZ, step);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                int i = samples.Index(r, c);
                samples.Heights[i] = _surface.GetWorldHeight(data.GetFullHeightmap(r, c));
                int flags = data.GetTerraformingFlags(r, c);
                samples.Flags[i] = flags;
                samples.Kinds[i] = KindOf(TerraformingMap.GetTerrainType(flags));
                samples.Cleared[i] = TerraformingMap.IsClearVegetationSet(flags);
            }
        try { ReadBiomes(samples); }
        catch (Exception e)
        {
            samples.BiomeStep = 0;
            SdkLog.Warn(Landscape.Platform, $"The biomes round ({centerX:0.#}, {centerZ:0.#}) could not be read: {e.Message}");
        }
        return samples;
    }

    // Each sample's biome, from the biome map of the world tile it lies in: each 4 m cell read once.
    private void ReadBiomes(LandscapeSamples samples)
    {
        if (_data == null) return;
        var tiles = new List<(Rect Area, BiomeMap Map, int Resolution)>();
        var cells = new Dictionary<(int Tile, int Index), int>();
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                float x = samples.X(c), z = samples.Z(r);
                int t = tiles.FindIndex(tile => Contains(tile.Area, x, z));
                if (t < 0)
                {
                    if (BiomeMapAt(x, z, out var area) is not { } map) continue;
                    tiles.Add((area, map, map.Resolution));
                    t = tiles.Count - 1;
                    if (samples.BiomeStep <= 0)
                    {
                        samples.BiomeStep = area.m_Width / map.Resolution;
                        samples.BiomeOriginX = area.m_XMin;
                        samples.BiomeOriginZ = area.m_YMin;
                    }
                }
                var (tileArea, tileMap, resolution) = tiles[t];
                int index = CellIndex(tileArea, resolution, x, z);
                if (!cells.TryGetValue((t, index), out int id)) cells[(t, index)] = id = tileMap.data[index].biomeId;
                samples.Biomes[samples.Index(r, c)] = id;
            }
    }

    private BiomeMap? BiomeMapAt(float x, float z, out Rect area)
    {
        area = default;
        var tile = _data?.OpenData(x, z, DataAccessMode.FETCH);
        var map = tile?.WorldMapTile?.GetBiomeMap();
        if (tile == null || map == null || map.Resolution <= 0) return null;
        area = tile.area;
        return map;
    }

    private static bool Contains(Rect area, float x, float z) =>
        x >= area.m_XMin && z >= area.m_YMin && x < area.m_XMin + area.m_Width && z < area.m_YMin + area.m_Height;

    // The biome map's cell (x, y) of a point, at data[y * resolution + x], as the game's biome map operation writes it: the
    // nearest cell, rounded as Mathf.RoundToInt (the game's terraforming sample i of a tile goes to round(i / (n − 1) ×
    // resolution), clamped), so cell k lies round k × 4 m from the tile's corner. Read the other way (cell k from k × 4 m to
    // k × 4 m + 4), a painted square showed half a cell off (seen in game).
    private static int CellIndex(Rect area, int resolution, float x, float z)
    {
        int cx = Math.Clamp((int)Math.Round((x - area.m_XMin) / area.m_Width * resolution), 0, resolution - 1);
        int cy = Math.Clamp((int)Math.Round((z - area.m_YMin) / area.m_Height * resolution), 0, resolution - 1);
        return cy * resolution + cx;
    }

    public bool TryGetBiome(float x, float z, out LandscapeBiome biome)
    {
        biome = default;
        if (BiomeMapAt(x, z, out var area) is not { } map) return false;
        biome = BiomeOf(map.data[CellIndex(area, map.Resolution, x, z)].biomeId);
        return true;
    }

    public IReadOnlyList<LandscapeBiome> Biomes
    {
        get
        {
            RefreshBiomes();
            return _biomeList;
        }
    }

    private LandscapeBiome BiomeOf(int id)
    {
        RefreshBiomes();
        foreach (var biome in _biomeList)
            if (biome.Id == id) return biome;
        return new LandscapeBiome(id, ((BiomeType)id).ToString());
    }

    // The vegetation set of the part of the world the player is in: its packages name the biomes it grows plants on (the
    // whole loaded ground is drawn with it). Read again when the player goes to another part.
    private void RefreshBiomes()
    {
        var set = _biomes?._localPlayerVegetationSet;
        var pointer = set == null ? IntPtr.Zero : set.Pointer;
        if (pointer == _biomeSet) return;
        _biomeSet = pointer;
        var list = new List<LandscapeBiome>();
        var packages = set?.packages;
        for (int i = 0; packages != null && i < packages.Count; i++)
        {
            var package = packages[i];
            if (package == null) continue;
            int id = (int)package.BiomeType;
            if (list.Exists(b => b.Id == id)) continue;
            string name = string.IsNullOrWhiteSpace(package.PackageName) ? package.BiomeType.ToString() : package.PackageName;
            list.Add(new LandscapeBiome(id, name));
        }
        _biomeList = list.ToArray();
    }

    public IOutlineView NewOutline() => new GameOutline();

    public IGridView NewGrid(int columns, int rows) => new GameGrid(columns, rows);

    public IKindPreviewView NewKindPreview() => new GameKindPreview();

    public IReadOnlyList<IWorldObject> ObjectsIn(float centerX, float centerZ, float radius) => GameObjects.In(centerX, centerZ, radius);

    public IReadOnlyList<PlaceableKindData> PlaceableKinds => GamePlacement.Kinds;

    public string? KindDisplayName(string name) => GamePlacement.DisplayName(name);

    public Texture2D? KindPicture(string name, int width, int height) => GamePlacement.Picture(name, width, height);

    public IWorldObject? Place(string name, Vector3 position, float rotation, float scale) => GamePlacement.Place(name, position, rotation, scale);

    public IReadOnlyList<IFieldView> Fields => GameFields.All();

    public IFieldView? PlaceField(float x, float z, int tilesX, int tilesZ, float tileSize) => GameFields.Place(x, z, tilesX, tilesZ, tileSize);

    public IReadOnlyList<IRoadPlanView> RoadPlans => GameRoads.All();

    public IRoadPlanView? PlaceRoadPlan(IReadOnlyList<Vector3> points, GroundKind kind, float width) => GameRoads.Place(points, kind, width);

    public (IReadOnlyList<Vector3> Samples, int Sections)? RoadLine(IReadOnlyList<Vector3> points, GroundKind kind) => GameRoads.Line(points, kind);

    // As the game's tool writes its own change: the data filled from the ground, the samples set and marked, the removable
    // objects taken, the data sent to the other players, then applied (the game's routine, the events through the patch).
    public LandscapeResult Write(LandscapeSamples samples, in GroundWrite write)
    {
        var biomes = BiomesWritten(samples);
        var data = NewData(samples.CenterX, samples.CenterZ, samples.Side, biomes.Count > 0 ? WithBiomes : Targets, out float originX, out float originZ, out _);
        if (data == null || _surface == null) return LandscapeResult.NotAvailable;
        if (data.size.x != samples.Size || Math.Abs(originX - samples.OriginX) > 0.01f || Math.Abs(originZ - samples.OriginZ) > 0.01f)
        {
            SdkLog.Warn(Landscape.Platform, $"{write.Operation}: the ground's samples moved between reading and writing them");
            return LandscapeResult.Failed;
        }
        int clear = TerraformingMap.c_ClearVegetationFlag, heightmap = TerraformingMap.c_HeightmapFlag;
        int n = samples.Size, written = 0;
        // Ground put back as it was goes in strips the game does not blur, when they lie on the samples.
        bool strips = write.Raw && StripsFit(data, samples);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                int i = samples.Index(r, c);
                if (!samples.Touched[i]) continue;
                written++;
                if (strips) continue; // put back in strips the game does not blur, below
                if (samples.HeightChanged[i] || write.Raw)
                    data.SetFullHeightmap(r, c, _surface.GetNormalizedHeight(Math.Clamp(samples.Heights[i], 0f, Landscape.MaximumHeight)));
                int flags;
                if (write.Raw) flags = samples.Flags[i];
                else
                {
                    flags = data.GetTerraformingFlags(r, c);
                    TerraformingMap.SetTerrainType(ref flags, TypeOf(samples.Kinds[i]));
                    flags = samples.Cleared[i] ? flags | clear : flags & ~clear;
                    if (samples.HeightChanged[i]) flags |= heightmap;
                }
                data.SetTerraformingFlags(r, c, flags);
                data.SetModified(r, c, Targets);
            }
        if (biomes.Count > 0) written += MarkBiome(data, samples, biomes[0]);
        if (written == 0) return LandscapeResult.Done;
        if (!strips) Unblur(data, samples);
        if (write.ObjectsAt is { } objectsAt && write.Objects == LandscapeObjects.Remove) RemoveObjects(samples, objectsAt);
        if (write.ObjectsAt is { } clearAt && write.Objects == LandscapeObjects.Clear) ClearObjects(samples, clearAt);
        var settling = write.ObjectsAt is { } settleAt && write.Objects is LandscapeObjects.Settle or LandscapeObjects.Harvest ? Standing(samples, settleAt) : null;
        var made = TerrainPatches.Begin(write.Area, write.Operation);
        try
        {
            if (strips) PutBack(samples, data, write.Operation);
            if (!strips || biomes.Count > 0) Send(data, write.Operation);
            // The data carries one biome: each other biome (a snapshot put back over several) is written by data of its own.
            for (int b = 1; b < biomes.Count; b++)
            {
                var more = NewData(samples.CenterX, samples.CenterZ, samples.Side, WithBiomes, out _, out _, out _);
                if (more != null && MarkBiome(more, samples, biomes[b]) > 0) Send(more, write.Operation);
            }
        }
        finally
        {
            TerrainPatches.End(made);
        }
        if (settling != null) Settle(settling, samples);
        // Harvested once the drawn ground has followed, so the loot falls on the new ground, not into it.
        if (settling != null && write.Objects == LandscapeObjects.Harvest) GameHarvest.Later(settling, write.Harvesting ?? HarvestOptions.Default);
        return LandscapeResult.Done;
    }

    private float[]? _blurKernel;

    // A strip of two rows of the square from row `first`, filled from the ground.
    private TerrainToolData Strip(TerrainToolData square, LandscapeSamples samples, int first)
    {
        int n = samples.Size;
        float step = samples.Step;
        var area = square.worldArea;
        var strip = new TerrainToolData(n, 2, step, Targets);
        strip.SetPosition(area.m_XMin + n * step / 2, area.m_YMin + first * step + step);
        strip.ClearFlags();
        _surface!.FillTerrainToolData(strip);
        return strip;
    }

    // Whether a strip lies on the square's samples: its corner where the square's row is, its first sample the same ground.
    private bool StripsFit(TerrainToolData square, LandscapeSamples samples)
    {
        int n = samples.Size, row = n / 2;
        if (n < 2) return false;
        var strip = Strip(square, samples, row);
        var at = strip.worldArea;
        var area = square.worldArea;
        bool fits = strip.size.x == n && strip.size.y == 2 && Math.Abs(at.m_XMin - area.m_XMin) < 0.01f
            && Math.Abs(at.m_YMin - (area.m_YMin + row * samples.Step)) < 0.01f
            && Math.Abs(strip.GetFullHeightmap(0, n / 2) - square.GetFullHeightmap(row, n / 2)) < 1e-6f;
        if (!fits) SdkLog.Warn(Landscape.Platform, "A strip of the ground does not lie on its samples: ground put back is undone from the game's blur instead");
        return fits;
    }

    // Ground put back as it was (a snapshot: an undo, a redo), exactly: a row of samples at a time, each in data of its own
    // two rows deep (the row before it, then the row), which the game's blur leaves alone (it blurs only the samples inside a
    // square's edge). The row put back is the strip's second: a row the strip starts on is applied to the world's tiles from
    // that row on only, so on a tile's edge the tile before kept its old edge and the ground showed a crack (seen in game,
    // light through the ground along x = −192 and z = 256 after an undo there). Heights, kinds and marks as kept; the biomes go
    // with the whole square.
    private void PutBack(LandscapeSamples samples, TerrainToolData square, LandscapeOperation operation)
    {
        int n = samples.Size;
        for (int row = 0; row < n; row++)
        {
            bool any = false;
            for (int c = 0; c < n && !any; c++)
                any = samples.Touched[samples.Index(row, c)];
            if (!any) continue;
            int first = Math.Max(0, row - 1);
            var strip = Strip(square, samples, first);
            for (int c = 0; c < n; c++)
            {
                int i = samples.Index(row, c);
                if (!samples.Touched[i]) continue;
                strip.SetFullHeightmap(row - first, c, _surface!.GetNormalizedHeight(Math.Clamp(samples.Heights[i], 0f, Landscape.MaximumHeight)));
                strip.SetTerraformingFlags(row - first, c, samples.Flags[i]);
                strip.SetModified(row - first, c, Targets);
            }
            Send(strip, operation);
        }
    }

    // The game blurs every change of heights it applies (`_ApplyTerrainToolDataInternal`: the full heightmap copied to the
    // raw one, each sample inside the square's edge made the 3 × 3 `_blurKernel`'s sum of the copy round it, before the
    // marked samples are applied), so heights written as they are came out moved: a snapshot put straight back moved the ground
    // by 9.5 mm on average, an undo over four tiles by up to 0.35 m (seen in game). The square's heights are given what,
    // blurred, makes every applied sample what the change asked (`GroundBlur`); the other players blur the same data alike.
    private void Unblur(TerrainToolData data, LandscapeSamples samples)
    {
        var kernel = BlurKernel();
        int n = samples.Size;
        var heights = new float[n * n];
        var applied = new bool[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                int i = samples.Index(r, c);
                heights[i] = _surface!.GetWorldHeight(data.GetFullHeightmap(r, c));
                applied[i] = samples.Touched[i];
            }
        var before = (float[])heights.Clone();
        float left = GroundBlur.Compensate(heights, applied, n, kernel, tolerance: 0.0001f);
        if (left > 0.05f) SdkLog.Info(Landscape.Platform, $"The game's blur of a change is undone to within {left * 1000:0} mm");
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                int i = samples.Index(r, c);
                if (heights[i] != before[i]) data.SetFullHeightmap(r, c, _surface!.GetNormalizedHeight(heights[i]));
            }
    }

    private float[] BlurKernel()
    {
        if (_blurKernel != null) return _blurKernel;
        try
        {
            if (_surface?._blurKernel is { } kernel)
            {
                var values = new Il2CppStructArray<float>(kernel.Pointer);
                if (values.Length == 9)
                {
                    _blurKernel = values.ToArray();
                    SdkLog.Info(Landscape.Platform, $"The game blurs changes of heights by [{string.Join(", ", Array.ConvertAll(_blurKernel, k => k.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)))}]");
                    return _blurKernel;
                }
            }
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The game's blur of a change could not be read: {e.Message}");
        }
        return _blurKernel = GroundBlur.Identity;
    }

    // Every natural object where the change reaches, trees and boulders too, taken at the game's own level; the items lying
    // there (a dropped thing, a planted sapling) stay.
    private static void ClearObjects(LandscapeSamples samples, Func<float, float, bool> where)
    {
        foreach (var found in Standing(samples, where))
        {
            try
            {
                if (!found.IsGone && found.Kind != NaturalObjectKind.Item) found.Clear();
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"An object could not be cleared: {e.Message}");
            }
        }
    }

    // The objects standing where the change reaches, kept before it is made (the heights they are put on come from the
    // samples the change wrote, not from the drawn ground, which takes a frame to follow).
    private static List<IWorldObject> Standing(LandscapeSamples samples, Func<float, float, bool> where)
    {
        var standing = new List<IWorldObject>();
        float side = (samples.Size - 1) * samples.Step;
        float cx = samples.OriginX + side / 2, cz = samples.OriginZ + side / 2;
        foreach (var found in GameObjects.In(cx, cz, side / 2 + 1f))
        {
            var at = found.Position;
            if (where(at.x, at.z)) standing.Add(found);
        }
        return standing;
    }

    private static void Settle(List<IWorldObject> standing, LandscapeSamples samples)
    {
        foreach (var found in standing)
        {
            try
            {
                if (found.IsGone) continue;
                var at = found.Position;
                float height = samples.HeightAt(at.x, at.z);
                if (!float.IsFinite(height) || Math.Abs(height - at.y) <= 0.001f) continue;
                at.y = height;
                found.MoveTo(at);
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"An object could not be put on the new ground: {e.Message}");
            }
        }
    }

    private void Send(TerrainToolData data, LandscapeOperation operation)
    {
        try { GameManagers.Get<NetworkWorldDataManager>()?.PropagateTerrainToolOperation(data); }
        catch (Exception e) { SdkLog.Warn(Landscape.Platform, $"{operation}: not sent to the other players: {e.Message}"); }
        _surface!.ApplyTerrainToolData(data, -1);
    }

    // The biomes the samples give their cells, each once, in the order met.
    private static List<int> BiomesWritten(LandscapeSamples samples)
    {
        var ids = new List<int>();
        for (int i = 0; i < samples.BiomeChanged.Length; i++)
            if (samples.BiomeChanged[i] && samples.Biomes[i] >= 0 && !ids.Contains(samples.Biomes[i])) ids.Add(samples.Biomes[i]);
        return ids;
    }

    // Marks the samples whose cells take the biome and gives the data that biome; how many were marked.
    private static int MarkBiome(TerrainToolData data, LandscapeSamples samples, int id)
    {
        data.biomeType = (BiomeType)id;
        int marked = 0;
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                int i = samples.Index(r, c);
                if (!samples.BiomeChanged[i] || samples.Biomes[i] != id) continue;
                data.SetModified(r, c, TerrainToolData.TargetFlags.BIOMES);
                marked++;
            }
        return marked;
    }

    // The world's removable objects (bushes, small stones, saplings) where the change clears the vegetation, taken as the
    // game's tool takes them (Game/GameObjects.cs): trees and large rocks stay.
    private static void RemoveObjects(LandscapeSamples samples, Func<float, float, bool> removeAt)
    {
        float side = (samples.Size - 1) * samples.Step;
        GameObjects.Remove(samples.OriginX + side / 2, samples.OriginZ + side / 2, side / 2, removeAt);
    }

    // The tool's settings for the change: the game's own defaults (a tool added in code) except what the edit says. No
    // height, slope or item checks: a mod's change is the mod's decision, and the SDK checks the buildings itself.
    private static void Configure(HeightmapTool tool, in LandscapeEdit edit)
    {
        var options = edit.Options;
        tool.raiseHeight = edit.Operation == LandscapeOperation.Raise ? edit.Amount : 0.5f;
        tool.digDepth = edit.Operation == LandscapeOperation.Lower ? edit.Amount : 0.5f;
        tool.clearVegetation = options.ClearVegetation || edit.Operation == LandscapeOperation.ClearVegetation;
        tool.setVegetationMask = tool.clearVegetation;
        var kind = edit.Operation == LandscapeOperation.Paint ? edit.Kind : options.Kind;
        tool.setTerrainType = edit.Operation == LandscapeOperation.Paint || (edit.Operation != LandscapeOperation.ClearVegetation && !options.KeepKind);
        tool.terrainType = TypeOf(kind ?? GroundKind.Dirt);
        tool.requiresTerrainType = false;
        tool.ignorePaintType = false;
        tool.checkTerrainSlope = false;
        tool.checkItemInstances = false;
        tool.validateHeight = false;
        tool.setBiomeType = false;
        tool.updateNavigation = options.UpdateNavigation;
        tool.propagateOnNetwork = true;
        tool.onlyValidate = false;
    }

    private HeightmapTool Tool()
    {
        if (_tool != null && _toolObject != null) return _tool;
        _toolObject = new GameObject("Mjolnir_LandscapeTool");
        Object.DontDestroyOnLoad(_toolObject);
        _toolObject.hideFlags = HideFlags.HideAndDontSave;
        _tool = _toolObject.AddComponent(Il2CppType.Of<HeightmapTool>()).Cast<HeightmapTool>();
        ToolPointer = _tool.Pointer;
        return _tool;
    }

    private static Vector3 Center(LandscapeArea area)
    {
        var point = At(area.Center.x, 0, area.Center.z);
        point.y = TerrainSampler.SampleHeight(point);
        return point;
    }

    // The flags of the terraforming map's cell under a point, from the world data tile there.
    private int? FlagsAt(float x, float z)
    {
        var tile = _data?.OpenData(x, z, DataAccessMode.FETCH);
        if (tile == null) return null;
        var slots = tile._dataSlots;
        for (int i = 0; slots != null && i < slots.Length; i++)
        {
            if (slots[i]?.TryCast<TerrainTileDataContainer>()?.terraformingMap is not { } map) continue;
            int resolution = map.Resolution;
            var area = tile.area;
            int cx = Math.Clamp((int)((x - area.m_XMin) / area.m_Width * resolution), 0, resolution - 1);
            int cy = Math.Clamp((int)((z - area.m_YMin) / area.m_Height * resolution), 0, resolution - 1);
            map.GetData(cx, cy, out int flags);
            return flags;
        }
        return null;
    }

    private static Vector3 At(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    internal static GroundKind KindOf(TerraformingMap.TerrainType type) => type switch
    {
        TerraformingMap.TerrainType.DIRT => GroundKind.Dirt,
        TerraformingMap.TerrainType.BEDROCK => GroundKind.Bedrock,
        TerraformingMap.TerrainType.PATH => GroundKind.Path,
        TerraformingMap.TerrainType.ROAD => GroundKind.Road,
        _ => GroundKind.Natural,
    };

    internal static TerraformingMap.TerrainType TypeOf(GroundKind kind) => kind switch
    {
        GroundKind.Dirt => TerraformingMap.TerrainType.DIRT,
        GroundKind.Bedrock => TerraformingMap.TerrainType.BEDROCK,
        GroundKind.Path => TerraformingMap.TerrainType.PATH,
        GroundKind.Road => TerraformingMap.TerrainType.ROAD,
        _ => TerraformingMap.TerrainType.NATURAL,
    };

    internal static LandscapeOperation OperationOf(TerraformingToolOperation operation, bool setsKind) => operation switch
    {
        TerraformingToolOperation.DIG => LandscapeOperation.Lower,
        TerraformingToolOperation.RAISE => LandscapeOperation.Raise,
        TerraformingToolOperation.LEVEL => LandscapeOperation.Level,
        _ => setsKind ? LandscapeOperation.Paint : LandscapeOperation.ClearVegetation,
    };

    private static TerraformingToolOperation OperationOf(LandscapeOperation operation) => operation switch
    {
        LandscapeOperation.Raise => TerraformingToolOperation.RAISE,
        LandscapeOperation.Lower => TerraformingToolOperation.DIG,
        LandscapeOperation.Level => TerraformingToolOperation.LEVEL,
        _ => TerraformingToolOperation.PAINT,
    };

    private static TerraformingToolSize SizeOf(LandscapeBrush brush) => brush switch
    {
        LandscapeBrush.Small => TerraformingToolSize.SMALL,
        LandscapeBrush.Soft => TerraformingToolSize.MEDIUM_SOFT,
        LandscapeBrush.Large => TerraformingToolSize.EXTRA_LARGE,
        _ => TerraformingToolSize.MEDIUM,
    };

    private static LandscapeResult ResultOf(TerraformingToolResult result) => result switch
    {
        TerraformingToolResult.SUCCESS => LandscapeResult.Done,
        TerraformingToolResult.ERROR_TOO_STEEP => LandscapeResult.TooSteep,
        TerraformingToolResult.ERROR_MAX_HEIGHT => LandscapeResult.TooHigh,
        TerraformingToolResult.ERROR_UNDERWATER => LandscapeResult.Underwater,
        TerraformingToolResult.ERROR_INIT => LandscapeResult.Failed,
        _ => LandscapeResult.Blocked,
    };
}
