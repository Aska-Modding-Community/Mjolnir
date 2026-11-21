using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

// What covers the ground: its biome (what grows on it) and its grass, and outlines drawn on it.
public static partial class Landscape
{
    /// <summary>The metres across a cell of the biome map: the game keeps one biome for each such square of the world.</summary>
    public const float BiomeCellSize = 4f;

    /// <summary>
    /// The biomes the part of the world the player is in grows plants of its own on, in the game's order; empty when no world
    /// is loaded, or off the main thread. A mod paints with one of these to grow what grows elsewhere in that part.
    /// </summary>
    public static IReadOnlyList<LandscapeBiome> Biomes => Live() is { } source ? source.Biomes : [];

    /// <summary>The biome of the ground at a point; null where it is not loaded, or off the main thread.</summary>
    public static LandscapeBiome? BiomeAt(float x, float z) =>
        Live() is { } source && source.IsLoaded(x, z) && source.TryGetBiome(x, z, out var biome) ? biome : null;

    /// <summary>
    /// The biome of <see cref="Biomes"/> named <paramref name="name"/> (its <see cref="LandscapeBiome.Name"/>, in any case:
    /// "BirchForest"); null when the part of the world the player is in grows nothing of that name, or off the main thread.
    /// </summary>
    public static LandscapeBiome? FindBiome(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var biome in Biomes)
            if (string.Equals(biome.Name, name, StringComparison.OrdinalIgnoreCase)) return biome;
        return null;
    }

    /// <summary>
    /// Makes <paramref name="biome"/> the ground's biome in the area, so what grows there changes to it at once: its height,
    /// kind and grass's clearing stay. The game keeps a biome for each square of <see cref="BiomeCellSize"/> metres, so a
    /// square takes it where its middle lies in the area (its weight there at least a half): an area smaller than a square
    /// may change none, and the area's blend only widens it. Saved with the world and sent to the other players.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="biome">Which biome; one of <see cref="Biomes"/> grows that part's own plants.</param>
    /// <param name="options">How; only <see cref="LandscapeOptions.KeepOffBuildings"/> and <see cref="LandscapeOptions.OnlyOn"/>
    /// (the kind at a square's middle) count. The trees, bushes and stones standing there stay.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="biome"/> is no biome (its number below 0).</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult PaintBiome(LandscapeArea area, LandscapeBiome biome, LandscapeOptions? options = null)
    {
        if (biome.Id < 0) throw new ArgumentOutOfRangeException(nameof(biome), biome.Id, "No such biome.");
        var how = Narrowed(options);
        int id = biome.Id;
        return Write(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), LandscapeOperation.PaintBiome, area, how, 0, samples =>
        {
            if (!(samples.BiomeStep > 0)) return false;
            for (int r = 0; r < samples.Size; r++)
                for (int c = 0; c < samples.Size; c++)
                {
                    var (mx, mz) = samples.BiomeCellMiddle(samples.X(c), samples.Z(r));
                    if (area.Weight(mx, mz) < 0.5f) continue;
                    int i = samples.Index(r, c);
                    if (how.OnlyOn is { } only && KindNear(samples, mx, mz, i) != only) continue;
                    if (samples.Biomes[i] == id) continue;
                    samples.Biomes[i] = id;
                    samples.BiomeChanged[i] = true;
                }
            return true;
        });
    }

    /// <summary>
    /// Gives the ground in the area its own kind and its grass back: where it is at least half in the area it becomes
    /// <see cref="GroundKind.Natural"/> again and is no longer marked cleared, so the grass of its biome grows there again at
    /// once. Roads, paths and dug earth in the area go with it (<see cref="LandscapeOptions.OnlyOn"/> keeps them); its height
    /// stays. The bushes and saplings a change took do not come back.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="options">How; only <see cref="LandscapeOptions.KeepOffBuildings"/> and <see cref="LandscapeOptions.OnlyOn"/> count.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult RestoreVegetation(LandscapeArea area, LandscapeOptions? options = null)
    {
        var how = Narrowed(options);
        return Write(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), LandscapeOperation.RestoreVegetation, area, how, 0, samples =>
        {
            for (int r = 0; r < samples.Size; r++)
                for (int c = 0; c < samples.Size; c++)
                {
                    if (area.Weight(samples.X(c), samples.Z(r)) < 0.5f) continue;
                    int i = samples.Index(r, c);
                    if (how.OnlyOn is { } only && samples.Kinds[i] != only) continue;
                    if (samples.Kinds[i] == GroundKind.Natural && !samples.Cleared[i]) continue;
                    samples.Kinds[i] = GroundKind.Natural;
                    samples.Cleared[i] = false;
                    samples.Touched[i] = true;
                }
            return true;
        });
    }

    /// <summary>
    /// Draws the area's outline on the ground in <paramref name="color"/>, following the ground's heights: its edge as a line,
    /// and the outer edge of a rectangle's or a circle's blend as a thinner one. It stays until disposed; a mod moves it with
    /// <see cref="LandscapeOutline.Area"/> (a brush under the cursor). It is drawn on this machine only, in no save, and hidden
    /// while no world is loaded. May be called from any thread: it shows from the next frame.
    /// </summary>
    /// <param name="area">What to outline.</param>
    /// <param name="color">The line's colour.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeOutline ShowOutline(LandscapeArea area, Color color) =>
        LandscapeOutline.Add(CallingMod.Of(Assembly.GetCallingAssembly()), area, color);

    /// <summary>
    /// Draws a grid of cells on the ground, in the look of the game's levelling field: each cell framed and see-through,
    /// lying on the relief. A mod colours each cell (<see cref="LandscapeGrid.SetCell"/>, <see cref="LandscapeGrid.MarkCell"/>)
    /// to show what a change will do before it is made — ground to fill, to cut, already right — and moves the grid with
    /// <see cref="LandscapeGrid.Center"/>. It stays until disposed. It is drawn on this machine only, in no save, and hidden
    /// while no world is loaded or its middle is not loaded. May be called from any thread: it shows from the next frame.
    /// </summary>
    /// <param name="center">The grid's middle on the ground (its y is not used).</param>
    /// <param name="columns">Cells along x, from 1 to <see cref="LandscapeGrid.MaximumCells"/>.</param>
    /// <param name="rows">Cells along z, from 1 to <see cref="LandscapeGrid.MaximumCells"/>.</param>
    /// <param name="cellSize">A cell's side in metres, from <see cref="LandscapeGrid.MinimumCellSize"/> to <see cref="LandscapeGrid.MaximumCellSize"/>.</param>
    /// <param name="rotation">How far it is turned about the vertical, in degrees, as a <see cref="LandscapeArea.Rectangle"/> is.</param>
    /// <returns>The grid, every cell empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count, the size, the middle or the turn is out of range or not a number.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeGrid ShowGrid(Vector3 center, int columns, int rows, float cellSize = 1, float rotation = 0) =>
        LandscapeGrid.Add(CallingMod.Of(Assembly.GetCallingAssembly()), center, columns, rows, cellSize, rotation);

    /// <summary>
    /// Protects the area's ground from every change of this machine but this mod's own: the player's tools, the villagers',
    /// a building's footprint or field, the game's and other mods'. A mod keeps its shrine, its arena or a player's claim
    /// as it is. The zone lasts until it is disposed; it is not saved with the world.
    /// </summary>
    /// <param name="area">The ground to protect: where the area is at least half (its blend's inner half).</param>
    /// <returns>The zone: move it with <see cref="LandscapeZone.Area"/>, hear what it refused with
    /// <see cref="LandscapeZone.Refused"/>, end it with <see cref="LandscapeZone.Dispose"/>.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeZone Protect(LandscapeArea area) => ProtectFor(CallingMod.Of(Assembly.GetCallingAssembly()), area);

    /// <summary>A zone kept by <paramref name="owner"/>: the platform's own (the console's) or a mod's.</summary>
    internal static LandscapeZone ProtectFor(Assembly owner, LandscapeArea area) => LandscapeZone.Add(owner, Owner(owner), area);

    // A change of the ground's cover: only the buildings and the kind it is limited to count, and it takes no object.
    private static LandscapeOptions Narrowed(LandscapeOptions? options) => new()
    {
        ClearVegetation = false,
        KeepKind = true,
        KeepOffBuildings = (options ?? LandscapeOptions.Default).KeepOffBuildings,
        KeepOffLandmarks = (options ?? LandscapeOptions.Default).KeepOffLandmarks,
        OnlyOn = options?.OnlyOn,
    };

    // The kind of the sample nearest a point, or the given sample's where the point lies outside what was read.
    private static GroundKind KindNear(LandscapeSamples samples, float x, float z, int fallback)
    {
        int k = samples.Nearest(x, z);
        return samples.Kinds[k >= 0 ? k : fallback];
    }

    /// <summary>The module's tick on the main thread: the outlines and grids drawn, moved and taken away.</summary>
    internal static void Tick()
    {
        var source = Live();
        LandscapeOutline.Tick(source);
        LandscapeGrid.Tick(source);
        KindPreview.Tick(source);
        ObjectMarks.Tick(source);
    }
}
