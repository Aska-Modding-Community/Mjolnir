using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// What the model sees of the game's ground (Game/GameTerrain.cs; a fake in the unit tests). Used on the main thread only.
/// </summary>
internal interface ITerrainSource
{
    /// <summary>Whether a world is loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether the point is in the part of the world loaded around the player, its ground known.</summary>
    bool IsLoaded(float x, float z);

    bool TryGetHeight(float x, float z, out float height);

    Vector3 Normal(float x, float z);

    GroundKind Kind(float x, float z);

    /// <summary>Whether the grass and bushes at the point were cleared (by any change of the ground).</summary>
    bool IsCleared(float x, float z);

    float WaterLevel { get; }

    bool IsUnderwater(float x, float z);

    /// <summary>The name of a building standing in the area (its solid colliders), or null for none.</summary>
    string? BuildingIn(LandscapeArea area);

    /// <summary>Where the local player stands and which way they face on the ground (the console's commands).</summary>
    bool TryGetPlayer(out Vector3 position, out Vector3 forward);

    /// <summary>The biome at the point, named as the part of the world the player is in grows it; false where it is not known.</summary>
    bool TryGetBiome(float x, float z, out LandscapeBiome biome);

    /// <summary>The biomes the part of the world the player is in grows plants of its own on, in the game's order.</summary>
    IReadOnlyList<LandscapeBiome> Biomes { get; }

    /// <summary>A line of the game's own to draw an outline on the ground with (hidden until drawn).</summary>
    IOutlineView NewOutline();

    /// <summary>A grid of <paramref name="columns"/> × <paramref name="rows"/> cells drawn on the ground (hidden until placed).</summary>
    IGridView NewGrid(int columns, int rows);

    /// <summary>The world's natural objects whose place lies within <paramref name="radius"/> metres of a point.</summary>
    IReadOnlyList<IWorldObject> ObjectsIn(float centerX, float centerZ, float radius);

    /// <summary>The kinds of the world a mod can place, made once the game has its item database; empty before.</summary>
    IReadOnlyList<PlaceableKindData> PlaceableKinds { get; }

    /// <summary>Places a world item of a kind at a point of the loaded world; null when the game takes none.</summary>
    IWorldObject? Place(string name, Vector3 position, float rotation, float scale);

    /// <summary>The game's levelling fields in the loaded world.</summary>
    IReadOnlyList<IFieldView> Fields { get; }

    /// <summary>
    /// Puts a levelling field down with its corner at (<paramref name="x"/>, <paramref name="z"/>) and a grid of
    /// <paramref name="tilesX"/> × <paramref name="tilesZ"/> tiles of <paramref name="tileSize"/> metres; null when the
    /// game will not take one.
    /// </summary>
    IFieldView? PlaceField(float x, float z, int tilesX, int tilesZ, float tileSize);

    /// <summary>The game's road plans in the loaded world.</summary>
    IReadOnlyList<IRoadPlanView> RoadPlans { get; }

    /// <summary>Lays a road plan of <paramref name="kind"/> through the points (on the ground); null when the game will not take one.</summary>
    IRoadPlanView? PlaceRoadPlan(IReadOnlyList<Vector3> points, GroundKind kind, float width);

    /// <summary>Makes the change with the game's tool; the game's patch raises <see cref="Landscape.GroundChanging"/> for it.</summary>
    LandscapeResult Apply(in LandscapeEdit edit);

    /// <summary>The game's own check of the change, without making it.</summary>
    LandscapeResult Check(in LandscapeEdit edit);

    /// <summary>
    /// The ground's samples of a square of about <paramref name="side"/> metres round a point, as the game keeps them (every
    /// half metre); null when it cannot be read. The same point and side give the same samples' places every time.
    /// </summary>
    LandscapeSamples? Read(float centerX, float centerZ, float side);

    /// <summary>
    /// Writes the samples marked <see cref="LandscapeSamples.Touched"/> into the game's ground, and the biome of those marked
    /// <see cref="LandscapeSamples.BiomeChanged"/>, as the game's tool writes its own changes: applied, saved, sent to the other
    /// players, the removable objects where <paramref name="write"/> says gone.
    /// </summary>
    LandscapeResult Write(LandscapeSamples samples, in GroundWrite write);
}

/// <summary>
/// One natural object of the world for the model (Game/GameObjects.cs; a fake in the unit tests). Used on the main thread
/// only; the object may be gone by the time the model asks again.
/// </summary>
internal interface IWorldObject
{
    /// <summary>The game's name of its kind ("Harvest_Wood_birch1").</summary>
    string AssetName { get; }

    NaturalObjectKind Kind { get; }

    Vector3 Position { get; }

    float Health { get; }

    int Quantity { get; }

    /// <summary>The game's name of the item it gives, or "" for none.</summary>
    string ResourceName { get; }

    /// <summary>Whether a change of the ground takes it (a bush, not a tree).</summary>
    bool IsRemovable { get; }

    /// <summary>Whether the player can gather or fell it.</summary>
    bool IsHarvestable { get; }

    /// <summary>Whether it is gone from the world.</summary>
    bool IsGone { get; }

    /// <summary>How many items are left to gather out of it by hand; 0 for a thing that is not gathered so.</summary>
    int GatherCount { get; }

    /// <summary>Gathers charges out of it as a player picking by hand does; how many items it gave.</summary>
    int Gather(int charges);

    /// <summary>Takes it out of the world as a change of the ground does; false when the game keeps it.</summary>
    bool Remove();

    /// <summary>Whether the game lets it be taken at its own level: a tree or a boulder too.</summary>
    bool IsClearable { get; }

    /// <summary>Takes it out of the world at the game's own level, whatever it is; false when the game keeps it.</summary>
    bool Clear();

    /// <summary>
    /// Strikes it as the game's harvest does until nothing is left, its loot falling beside it; true when it is used up,
    /// false when it has no harvest of its own here (too far from the player, or a thing picked by hand) or the game
    /// turned a blow away.
    /// </summary>
    bool Harvest(HarvestOptions options);

    /// <summary>Moves it to a place, its look with it; false when it could not be moved.</summary>
    bool MoveTo(Vector3 position);
}

/// <summary>
/// One of the game's levelling fields for the model (Game/GameFields.cs; a fake in the unit tests). Used on the main thread
/// only; the plan may be gone by the time the model asks again.
/// </summary>
internal interface IFieldView
{
    Vector3 Center { get; }

    float Width { get; }

    float Depth { get; }

    int Tiles { get; }

    int LevelledTiles { get; }

    /// <summary>The height its tiles are levelled to.</summary>
    float Height { get; }

    /// <summary>How far its ground still stands from that height at its worst.</summary>
    float HeightDifference { get; }

    bool IsGone { get; }

    /// <summary>
    /// Levels every tile not levelled yet, as a villager's hoe does, but those whose ground <paramref name="skip"/> says to
    /// leave (the game marks a tile worked before its tool runs, so a tile a zone would refuse is never tried); how many
    /// are levelled now.
    /// </summary>
    int LevelNow(Func<Rect, bool>? skip);

    /// <summary>Turns the plan into what it builds, as the game does once it is levelled.</summary>
    bool Finish();

    /// <summary>Takes the plan away, the ground left as it is.</summary>
    bool Remove();
}

/// <summary>
/// One of the game's road plans for the model (Game/GameRoads.cs; a fake in the unit tests). Used on the main thread only;
/// the plan may be gone by the time the model asks again.
/// </summary>
internal interface IRoadPlanView
{
    IReadOnlyList<Vector3> Points { get; }

    /// <summary>What it lays: a road, or dirt for the levelling plan.</summary>
    GroundKind Kind { get; }

    /// <summary>The strip each section levels, in metres; set, the plan takes the template of that width (kept and sent).</summary>
    float Width { get; set; }

    int Sections { get; }

    /// <summary>The sections levelled and paved (the game counts every section done while the plan is not built yet).</summary>
    int LevelledSections { get; }

    /// <summary>Whether its build part is done, so its sections can be levelled.</summary>
    bool IsBuilt { get; }

    bool IsGone { get; }

    /// <summary>Finishes its build part as if the villagers had brought what it needs.</summary>
    bool Build();

    /// <summary>
    /// Levels every section not done yet, as a villager does, but those whose ground <paramref name="skip"/> says to leave
    /// (the game marks a section worked before its tool runs); how many are done now.
    /// </summary>
    int LevelNow(Func<Rect, bool>? skip);

    /// <summary>Takes the plan away, the ground left as it is.</summary>
    bool Remove();
}

/// <summary>An outline's lines in the world (Game/GameOutline.cs; a fake in the unit tests). Used on the main thread only.</summary>
internal interface IOutlineView
{
    /// <summary>
    /// Shows the area's edge as a closed line <paramref name="width"/> wide through <paramref name="edge"/>, and its blend's
    /// outer edge, when it has one, as a thinner line through <paramref name="blend"/>.
    /// </summary>
    void Draw(IReadOnlyList<Vector3> edge, IReadOnlyList<Vector3>? blend, Color color, float width);

    void Hide();

    /// <summary>Takes the lines out of the world for good.</summary>
    void Destroy();
}

/// <summary>A grid's cells in the world (Game/GameGrid.cs; a fake in the unit tests). Used on the main thread only.</summary>
internal interface IGridView
{
    /// <summary>Shows the grid with its middle at <paramref name="center"/> (y: the ground's height there), turned as a rectangle is.</summary>
    void Place(Vector3 center, float rotation, float cellSize);

    /// <summary>What each cell shows, row after row from the lowest z, each row from the lowest x.</summary>
    void Paint(GridCellKind[] kinds, float[] fractions);

    /// <summary>The colours of a shade's two ends; null for the game's own. A marked cell is always the game's red.</summary>
    void Colors(Color? near, Color? far);

    void Hide();

    /// <summary>Takes the grid out of the world for good.</summary>
    void Destroy();
}

/// <summary>A kind of the world a mod can place, as the game side made it (<see cref="PlaceableKind"/>).</summary>
internal readonly record struct PlaceableKindData(string Name, NaturalObjectKind Kind, string AssetName, float MinimumScale, float MaximumScale, bool IsHarvestable, bool IsGathered);

/// <summary>How samples are written (<see cref="ITerrainSource.Write"/>).</summary>
/// <param name="Operation">The change, for <see cref="Landscape.GroundChanged"/>.</param>
/// <param name="Area">The rectangle on the ground it touches, for the events.</param>
/// <param name="Raw">Whether each touched sample's flags are written as they are in <see cref="LandscapeSamples.Flags"/> (a
/// snapshot put back), instead of from its kind, its vegetation and whether its height changed.</param>
/// <param name="ObjectsAt">Where the world's natural objects are touched (the area's own ground); null for nowhere.</param>
/// <param name="Objects">What becomes of the objects there: taken as the game's tool takes them, left, or put on the new ground.</param>
/// <param name="Harvesting">With <see cref="LandscapeObjects.Harvest"/>, who strikes.</param>
internal readonly record struct GroundWrite(LandscapeOperation Operation, Rect Area, bool Raw, Func<float, float, bool>? ObjectsAt, LandscapeObjects Objects, HarvestOptions? Harvesting = null);

/// <summary>
/// The ground's samples over a square, row by row along z, each row along x: what the game keeps every half metre (its
/// height, the kind of ground, whether its vegetation was cleared, and its raw flags), the biome of the coarser cell each lies
/// in, and what the model changed.
/// </summary>
internal sealed class LandscapeSamples
{
    public LandscapeSamples(float centerX, float centerZ, float side, int size, float originX, float originZ, float step)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        Side = side;
        Size = size;
        OriginX = originX;
        OriginZ = originZ;
        Step = step;
        int count = size * size;
        Heights = new float[count];
        Flags = new int[count];
        Kinds = new GroundKind[count];
        Cleared = new bool[count];
        Touched = new bool[count];
        HeightChanged = new bool[count];
        Biomes = new int[count];
        BiomeChanged = new bool[count];
        Array.Fill(Biomes, -1);
    }

    /// <summary>Where and how large it was asked for (a write asks again the same way).</summary>
    public float CenterX { get; }

    public float CenterZ { get; }

    public float Side { get; }

    /// <summary>Samples along each side.</summary>
    public int Size { get; }

    /// <summary>The first sample's place.</summary>
    public float OriginX { get; }

    public float OriginZ { get; }

    /// <summary>Metres between samples.</summary>
    public float Step { get; }

    /// <summary>World heights, in metres.</summary>
    public float[] Heights { get; }

    /// <summary>The game's flags of each sample, as read.</summary>
    public int[] Flags { get; }

    public GroundKind[] Kinds { get; }

    public bool[] Cleared { get; }

    /// <summary>Which samples are written back.</summary>
    public bool[] Touched { get; }

    /// <summary>Which samples' heights changed (their flags then say so, as the game's do).</summary>
    public bool[] HeightChanged { get; }

    /// <summary>The biome of the cell each sample lies in (its <see cref="LandscapeBiome.Id"/>), or −1 where it is not known.</summary>
    public int[] Biomes { get; }

    /// <summary>Which samples' cells take the biome in <see cref="Biomes"/> (a cell takes it when any of its samples says so).</summary>
    public bool[] BiomeChanged { get; }

    /// <summary>Metres across a biome's cell; 0 when the biomes could not be read.</summary>
    public float BiomeStep { get; set; }

    /// <summary>The middle of a biome's cell (the cells lie side by side round it, a cell's middle every <see cref="BiomeStep"/>).</summary>
    public float BiomeOriginX { get; set; }

    public float BiomeOriginZ { get; set; }

    /// <summary>The middle of the biome's cell a point lies in: the nearest, as the game rounds (half way, to the even cell).</summary>
    public (float X, float Z) BiomeCellMiddle(float x, float z) =>
        (BiomeOriginX + MathF.Round((x - BiomeOriginX) / BiomeStep) * BiomeStep,
         BiomeOriginZ + MathF.Round((z - BiomeOriginZ) / BiomeStep) * BiomeStep);

    public float X(int column) => OriginX + column * Step;

    public float Z(int row) => OriginZ + row * Step;

    public int Index(int row, int column) => row * Size + column;

    /// <summary>The height between the samples at a point by its four neighbours; NaN outside.</summary>
    public float HeightAt(float x, float z)
    {
        float u = (x - OriginX) / Step, v = (z - OriginZ) / Step;
        if (!(u >= 0 && v >= 0 && u <= Size - 1 && v <= Size - 1)) return float.NaN;
        int c0 = Math.Min((int)u, Size - 2), r0 = Math.Min((int)v, Size - 2);
        float fu = u - c0, fv = v - r0;
        float near = Heights[Index(r0, c0)] + (Heights[Index(r0, c0 + 1)] - Heights[Index(r0, c0)]) * fu;
        float far = Heights[Index(r0 + 1, c0)] + (Heights[Index(r0 + 1, c0 + 1)] - Heights[Index(r0 + 1, c0)]) * fu;
        return near + (far - near) * fv;
    }

    /// <summary>The sample nearest a point, or -1 outside.</summary>
    public int Nearest(float x, float z)
    {
        int c = (int)MathF.Round((x - OriginX) / Step), r = (int)MathF.Round((z - OriginZ) / Step);
        return c >= 0 && r >= 0 && c < Size && r < Size ? Index(r, c) : -1;
    }

    /// <summary>A copy of what was read, nothing touched (a snapshot keeps one).</summary>
    public LandscapeSamples Copy()
    {
        var copy = new LandscapeSamples(CenterX, CenterZ, Side, Size, OriginX, OriginZ, Step)
        {
            BiomeStep = BiomeStep,
            BiomeOriginX = BiomeOriginX,
            BiomeOriginZ = BiomeOriginZ,
        };
        Biomes.CopyTo(copy.Biomes, 0);
        Heights.CopyTo(copy.Heights, 0);
        Flags.CopyTo(copy.Flags, 0);
        Kinds.CopyTo(copy.Kinds, 0);
        Cleared.CopyTo(copy.Cleared, 0);
        return copy;
    }
}

/// <summary>A change to the ground as the model hands it to the game.</summary>
internal readonly record struct LandscapeEdit(LandscapeArea Area, LandscapeOperation Operation, float Amount, GroundKind? Kind, LandscapeOptions Options);
