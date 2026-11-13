using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// What covers the ground, on a fake game: the biomes read and named, painted a 4 m cell at a time where its middle lies in
/// the area, kept by a snapshot and put back; the grass given back; a change limited to one kind of ground; outlines drawn
/// on the ground, moved, hidden and taken away; the console's biome and grass.
/// </summary>
[Collection("Terrain")]
public sealed class CoverTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public CoverTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(CoverTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    private static Color Orange()
    {
        Color c = default;
        c.r = 1;
        c.g = 0.6f;
        c.a = 1;
        return c;
    }

    private static LandscapeBiome Resource => Landscape.FindBiome("resourceforest") ?? throw new InvalidOperationException("no ResourceForest");

    // Biomes.

    [Fact]
    public void BiomesAreReadAndNamedByThePartOfTheWorld()
    {
        Assert.Equal(["Default", "BirchForest", "ResourceForest"], Landscape.Biomes.Select(b => b.Name));
        Assert.Equal(new LandscapeBiome(2, "BirchForest"), Landscape.BiomeAt(5, 5));
        Assert.Equal("BirchForest (2)", Landscape.BiomeAt(5, 5).ToString());
        _game.BiomeCells[(1, 1)] = 10;
        Assert.Equal("Biome10", Landscape.BiomeAt(5, 5)?.Name); // grown nothing of its own here: the game's name
        Assert.Equal(1, Resource.Id);
        Assert.Null(Landscape.FindBiome("CypressForest"));
        Assert.Null(Landscape.BiomeAt(500, 0)); // not loaded
        Assert.Equal(new LandscapeBiome(1, "another name"), Resource); // the same biome, whatever its name
        Assert.Equal("", default(LandscapeBiome).Name);

        _game.IsAvailable = false;
        Assert.Empty(Landscape.Biomes);
        Assert.Null(Landscape.BiomeAt(5, 5));
        Assert.Null(Landscape.FindBiome("BirchForest"));
    }

    [Fact]
    public void ABiomeIsPaintedOnTheCellsWhoseMiddlesLieInTheArea()
    {
        var seen = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) => seen.Add(e);
        _game.Objects.Add(new FakeObject { AssetName = "Harvest_Plant_Flax", Position = V(10, 0, 10), IsRemovable = true });

        // 9 m round (10, 10): the cells' middles at 8 and 12 lie in it, those at 4 and 16 do not.
        var area = LandscapeArea.Rectangle(V(10, 0, 10), 9, 9, blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(area, Resource));
        for (int x = 0; x <= 5; x++)
            for (int z = 0; z <= 5; z++)
                Assert.Equal(x is 2 or 3 && z is 2 or 3 ? 1 : 2, _game.BiomeIdAt(x * 4, z * 4));
        Assert.Equal(1, _game.BiomeWrites);
        Assert.Empty(_game.Written); // no height, kind or vegetation written
        Assert.DoesNotContain(_game.Objects, o => o.IsGone); // nothing taken
        var e = Assert.Single(seen);
        Assert.Equal(LandscapeOperation.PaintBiome, e.Operation);
        Assert.Equal("test.mod", e.ModId);

        // Painted again with the same biome: nothing to write.
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(area, Resource));
        Assert.Equal(1, _game.BiomeWrites);
    }

    [Fact]
    public void AnAreaHoldingNoCellsMiddleChangesNoBiome()
    {
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(LandscapeArea.Circle(V(9, 0, 9), 1, blend: 0), Resource));
        Assert.Empty(_game.Writes);
        Assert.Equal(2, _game.BiomeIdAt(10, 10));

        // A 3 m square round a cell's middle takes that cell alone.
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(LandscapeArea.Rectangle(V(8, 0, 8), 3, 3, blend: 0), Resource));
        Assert.Equal(1, _game.BiomeIdAt(8, 8));
        Assert.Equal(1, _game.BiomeIdAt(9.9f, 6.1f)); // the cell reaches 2 m round its middle
        Assert.Equal(2, _game.BiomeIdAt(4, 8));
        Assert.Equal(2, _game.BiomeIdAt(12, 12));
    }

    [Fact]
    public void ABiomeIsRefusedWhereItCannotBeMade()
    {
        var resource = Resource;
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.PaintBiome(LandscapeArea.Circle(V(0, 0, 10), 4), new LandscapeBiome(-1, "none")));
        Assert.Equal(LandscapeResult.NotLoaded, Landscape.PaintBiome(LandscapeArea.Circle(V(98, 0, 0), 4), resource));
        _game.Buildings = true;
        Assert.Equal(LandscapeResult.Blocked, Landscape.PaintBiome(LandscapeArea.Circle(V(0, 0, 10), 4), resource));
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(LandscapeArea.Circle(V(0, 0, 10), 4), resource, new LandscapeOptions { KeepOffBuildings = false }));
        _game.HasBiomes = false;
        Assert.Equal(LandscapeResult.Failed, Landscape.PaintBiome(LandscapeArea.Circle(V(20, 0, 10), 4), resource, new LandscapeOptions { KeepOffBuildings = false }));
        _game.IsAvailable = false;
        Assert.Equal(LandscapeResult.NotAvailable, Landscape.PaintBiome(LandscapeArea.Circle(V(0, 0, 10), 4), resource));
    }

    [Fact]
    public void ABiomeLimitedToOneKindLeavesTheCellsOfOthers()
    {
        _game.WrittenKinds[FakeTerrain.Cell(8, 8)] = GroundKind.Road; // the middle of cell (2, 2)
        var area = LandscapeArea.Rectangle(V(10, 0, 10), 9, 9, blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(area, Resource, new LandscapeOptions { OnlyOn = GroundKind.Natural }));
        Assert.Equal(2, _game.BiomeIdAt(8, 8));
        Assert.Equal(1, _game.BiomeIdAt(12, 8));
        Assert.Equal(1, _game.BiomeIdAt(12, 12));
    }

    [Fact]
    public void ASnapshotKeepsTheBiomesAndPutsThemBack()
    {
        _game.BiomeCells[(2, 2)] = 1;
        var area = LandscapeArea.Rectangle(V(10, 0, 10), 9, 9, blend: 0);
        var snapshot = Landscape.Capture(area)!;
        Assert.Equal(LandscapeResult.Done, Landscape.PaintBiome(area, new LandscapeBiome(0, "Default")));
        Assert.Equal(0, _game.BiomeIdAt(8, 8));
        Assert.Equal(0, _game.BiomeIdAt(12, 12));
        int before = _game.BiomeWrites;

        Assert.Equal(LandscapeResult.Done, snapshot.Restore());
        Assert.Equal(1, _game.BiomeIdAt(8, 8));
        Assert.Equal(2, _game.BiomeIdAt(12, 12));
        Assert.Equal(2, _game.BiomeIdAt(12, 8));
        Assert.Equal(before + 2, _game.BiomeWrites); // two biomes: data of each

        // A paste keeps the place's own biome.
        Assert.Equal(LandscapeResult.Done, snapshot.PasteAt(V(50, 0, 50)));
        Assert.Equal(2, _game.BiomeIdAt(50, 50));
    }

    // The grass given back.

    [Fact]
    public void RestoringTheVegetationMakesTheGroundNaturalAndUncleared()
    {
        var circle = LandscapeArea.Circle(V(0, 0, 10), 3, blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Paint(circle, GroundKind.Dirt, new LandscapeOptions { ClearVegetation = true }));
        Assert.Equal(GroundKind.Dirt, Landscape.KindAt(0, 10));
        Assert.True(Landscape.IsCleared(0, 10));
        _game.WrittenKinds[FakeTerrain.Cell(1, 10)] = GroundKind.Road;
        _game.Objects.Add(new FakeObject { AssetName = "Harvest_Plant_Flax", Position = V(0.5f, 0, 10), IsRemovable = true });
        var seen = new List<LandscapeOperation>();
        Landscape.GroundChanging += (_, e) => seen.Add(e.Operation);

        // Limited to dirt: the road stays.
        Assert.Equal(LandscapeResult.Done, Landscape.RestoreVegetation(circle, new LandscapeOptions { OnlyOn = GroundKind.Dirt }));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(0, 10));
        Assert.False(Landscape.IsCleared(0, 10));
        Assert.Equal(GroundKind.Road, Landscape.KindAt(1, 10));
        Assert.Equal(FakeTerrain.Ground(0), Landscape.HeightAt(0, 10), 3);
        Assert.DoesNotContain(_game.Objects, o => o.IsGone); // nothing taken, even with the default options

        Assert.Equal(LandscapeResult.Done, Landscape.RestoreVegetation(circle));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(1, 10));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(4, 10)); // outside: never touched
        Assert.Equal([LandscapeOperation.RestoreVegetation, LandscapeOperation.RestoreVegetation], seen);

        // Nothing left to give back: nothing written.
        int writes = _game.Writes.Count;
        Assert.Equal(LandscapeResult.Done, Landscape.RestoreVegetation(circle));
        Assert.Equal(writes, _game.Writes.Count);
    }

    // A change limited to one kind of ground.

    [Fact]
    public void AChangeLimitedToOneKindIsWrittenByTheSdkAndLeavesTheOthers()
    {
        _game.WrittenKinds[FakeTerrain.Cell(0, 10)] = GroundKind.Road;
        var options = new LandscapeOptions { OnlyOn = GroundKind.Natural };
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1, options));
        Assert.Empty(_game.Applied); // not the game's tool: it cannot tell the kinds apart
        Assert.Equal(FakeTerrain.Ground(0), Landscape.HeightAt(0, 10), 3);
        Assert.Equal(FakeTerrain.Ground(1) + 1, Landscape.HeightAt(1, 10), 3);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(0, 10));

        Assert.Equal(LandscapeResult.Done, Landscape.Paint(LandscapeArea.Point(V(0, 0, 10), LandscapeBrush.Large), GroundKind.Path, options));
        Assert.Empty(_game.Applied);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(0, 10));

        Assert.Equal(LandscapeResult.Done, Landscape.Smooth(LandscapeArea.Circle(V(0, 0, 10), 3), 1, new LandscapeOptions { OnlyOn = GroundKind.Road, KeepKind = true }));
        Assert.True(Landscape.HeightAt(0, 10) > FakeTerrain.Ground(0) + 0.1f); // the road's sample, in a pit, smoothed up
        Assert.Equal(FakeTerrain.Ground(1) + 1, Landscape.HeightAt(1, 10), 3); // the raised meadow beside it untouched
        Assert.Equal(GroundKind.Road, Landscape.KindAt(0, 10));

        // Without it, a rectangle still goes through the game's tool.
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1));
        Assert.Single(_game.Applied);
    }

    // Outlines.

    [Fact]
    public void AnOutlineIsDrawnOnTheGroundRoundTheArea()
    {
        var outline = Landscape.ShowOutline(LandscapeArea.Rectangle(V(0, 0, 10), 4, 2, rotation: 90, blend: 1), Orange());
        Assert.Empty(_game.Outlines); // drawn on the next frame
        Landscape.Tick();
        var view = Assert.Single(_game.Outlines);
        Assert.True(view.IsShown);
        Assert.Equal(Orange().g, view.Color.g);
        var edge = view.Edge!;
        // Turned a quarter: 2 m across x, 4 along z.
        Assert.Equal(-1f, edge.Min(p => p.x), 3);
        Assert.Equal(1f, edge.Max(p => p.x), 3);
        Assert.Equal(8f, edge.Min(p => p.z), 3);
        Assert.Equal(12f, edge.Max(p => p.z), 3);
        Assert.All(edge, p => Assert.Equal(FakeTerrain.Ground(p.x) + LandscapeOutline.Lift, p.y, 3));
        for (int i = 0; i < edge.Count; i++)
        {
            var a = edge[i];
            var b = edge[(i + 1) % edge.Count];
            Assert.True(MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z)) <= LandscapeOutline.PointSpacing + 0.001f);
        }
        var blend = view.Blend!;
        Assert.Equal(-2f, blend.Min(p => p.x), 2);
        Assert.Equal(13f, blend.Max(p => p.z), 2);
        Assert.Contains("outline of", outline.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EachAreaHasItsOwnOutline()
    {
        var circle = LandscapeOutline.Line(LandscapeArea.Circle(V(0, 0, 0), 3, blend: 1), false)!;
        Assert.All(circle, p => Assert.Equal(3f, MathF.Sqrt(p.X * p.X + p.Z * p.Z), 3));
        var outer = LandscapeOutline.Line(LandscapeArea.Circle(V(0, 0, 0), 3, blend: 1), true)!;
        Assert.All(outer, p => Assert.Equal(4f, MathF.Sqrt(p.X * p.X + p.Z * p.Z), 3));
        var brush = LandscapeOutline.Line(LandscapeArea.Point(V(0, 0, 0), LandscapeBrush.Large), false)!;
        Assert.All(brush, p => Assert.Equal(3.25f, MathF.Sqrt(p.X * p.X + p.Z * p.Z), 3)); // its reach
        Assert.Null(LandscapeOutline.Line(LandscapeArea.Point(V(0, 0, 0)), true));
        var triangle = LandscapeArea.Polygon([V(0, 0, 0), V(6, 0, 0), V(0, 0, 6)]);
        var line = LandscapeOutline.Line(triangle, false)!;
        Assert.Contains(line, p => Math.Abs(p.X - 6) < 0.001f && Math.Abs(p.Z) < 0.001f);
        Assert.All(line, p => Assert.True(p.X + p.Z <= 6.001f));
        Assert.Null(LandscapeOutline.Line(triangle, true));
        Assert.Null(LandscapeOutline.Line(LandscapeArea.Rectangle(V(0, 0, 0), 4, 4, blend: 0), true));
        var mask = LandscapeOutline.Line(LandscapeArea.Mask(V(0, 0, 0), new float[2, 2], 8, 4), false)!;
        Assert.Equal(4f, mask.Max(p => p.X), 3);
        Assert.Equal(2f, mask.Max(p => p.Z), 3);
    }

    [Fact]
    public void AnOutlineMovesHidesAndGoes()
    {
        var outline = Landscape.ShowOutline(LandscapeArea.Circle(V(0, 0, 10), 2), Orange());
        Landscape.Tick();
        var view = _game.Outlines.Single();
        Assert.Equal(1, view.Draws);
        Landscape.Tick();
        Assert.Equal(1, view.Draws); // nothing moved

        outline.Area = LandscapeArea.Circle(V(20, 0, 10), 2);
        Landscape.Tick();
        Assert.Equal(2, view.Draws);
        Assert.Equal(20f, view.Edge!.Average(p => p.x), 1);
        for (int i = 0; i < 30; i++) Landscape.Tick();
        Assert.Equal(3, view.Draws); // the ground read again under it now and then

        outline.IsVisible = false;
        Landscape.Tick();
        Assert.False(view.IsShown);
        outline.IsVisible = true;
        Landscape.Tick();
        Assert.True(view.IsShown);

        _game.IsAvailable = false;
        Landscape.Tick();
        Assert.False(view.IsShown);
        _game.IsAvailable = true;
        Landscape.Tick();
        Assert.True(view.IsShown);

        // Off the loaded ground it is not drawn.
        outline.Area = LandscapeArea.Circle(V(500, 0, 0), 2);
        Landscape.Tick();
        Assert.False(view.IsShown);

        outline.Dispose();
        Landscape.Tick();
        Assert.True(view.IsDestroyed);
        Landscape.Tick();
        Assert.Single(_game.Outlines);
    }

    [Fact]
    public void AnOutlineMayBeShownFromAnotherThread()
    {
        LandscapeOutline? outline = null;
        var thread = new System.Threading.Thread(() => outline = Landscape.ShowOutline(LandscapeArea.Circle(V(0, 0, 10), 2), Orange()));
        thread.Start();
        thread.Join();
        Landscape.Tick();
        Assert.NotNull(outline);
        Assert.True(Assert.Single(_game.Outlines).IsShown);
    }

    // The console.

    [Fact]
    public void TheConsolePaintsAndListsTheBiomesAndGivesTheGrassBack()
    {
        DevConsole.Execute("biome");
        Assert.Contains(Lines(), l => l == "Biome here: BirchForest (2). This part of the world grows: Default (0), BirchForest (2), ResourceForest (1).");

        // In front of the player (at 0, 0 facing z): an 8 m square 8.5 m ahead.
        DevConsole.Execute("biome RESOURCEFOREST");
        Assert.Equal(1, _game.BiomeIdAt(0, TerrainCommands.AheadOf(TerrainCommands.BiomeSize)));
        Assert.Contains(Lines(), l => l.StartsWith("Made 8 × 8 m", StringComparison.Ordinal) && l.EndsWith("ResourceForest (1).", StringComparison.Ordinal));
        DevConsole.Execute("undo");
        Assert.Equal(2, _game.BiomeIdAt(0, TerrainCommands.AheadOf(TerrainCommands.BiomeSize)));

        DevConsole.Execute("biome 10 12");
        Assert.Equal(10, _game.BiomeIdAt(0, TerrainCommands.AheadOf(12)));
        Assert.Equal(0, TerrainCommands.ParseBiome("0", Landscape.Biomes).Id);
        Assert.Equal("biome 7", TerrainCommands.ParseBiome("7", Landscape.Biomes).Name);
        DevConsole.Execute("biome lava");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("is no biome of this part of the world: Default, BirchForest, ResourceForest", StringComparison.Ordinal));

        float ahead = TerrainCommands.AheadOf(TerrainCommands.DefaultSize);
        _game.WrittenKinds[FakeTerrain.Cell(0, ahead)] = GroundKind.Dirt;
        DevConsole.Execute("grass");
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(0, ahead));
        Assert.Contains(Lines(), l => l.StartsWith("Gave 4 × 4 m", StringComparison.Ordinal));
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}
