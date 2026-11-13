using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// The changes the SDK writes sample by sample on a fake ground: the areas' shapes and weights, the basic changes over a
/// circle, a polygon and a mask, smoothing, ramps, a mod's own shape, measuring, snapshots put back and pasted, the objects
/// taken where the vegetation is cleared, and the checks, events and threads every change goes through; the console's
/// smooth and undo.
/// </summary>
[Collection("Terrain")]
public sealed class WritingTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public WritingTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(WritingTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    private float Height(float x, float z)
    {
        _game.TryGetHeight(x, z, out float height);
        return height;
    }

    // Shapes and their weights.

    [Fact]
    public void ACircleIsFullInsideAndFadesOverItsBlend()
    {
        var smooth = LandscapeArea.Circle(V(0, 0, 0), 4, blend: 2);
        Assert.Equal(1f, smooth.Weight(0, 0));
        Assert.Equal(1f, smooth.Weight(4, 0));
        Assert.Equal(0.5f, smooth.Weight(5, 0), 3);
        Assert.True(smooth.Weight(4.5f, 0) > 0.75f); // gently at the inner end
        Assert.Equal(0f, smooth.Weight(6, 0));
        var linear = LandscapeArea.Circle(V(0, 0, 0), 4, blend: 2, falloff: LandscapeFalloff.Linear);
        Assert.Equal(0.75f, linear.Weight(4.5f, 0), 3);
        Assert.Equal(-6f, smooth.Bounds.m_XMin, 3);
        Assert.Equal(12f, smooth.Bounds.m_Width, 3);
        Assert.Equal(8f, smooth.Width);
        Assert.Contains("circle of 4 m", smooth.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void APolygonIsFullInsideAndFadesFromItsEdges()
    {
        var triangle = LandscapeArea.Polygon([V(0, 0, 0), V(10, 0, 0), V(0, 0, 10)], blend: 2, falloff: LandscapeFalloff.Linear);
        Assert.Equal(1f, triangle.Weight(2, 2));
        Assert.Equal(0f, triangle.Weight(8, 8));
        Assert.Equal(0.5f, triangle.Weight(5, -1), 3); // a metre outside its lower edge
        Assert.Equal(0f, triangle.Weight(-3, 5));
        Assert.Equal(5f, triangle.Center.x, 3);
        Assert.Equal(10f, triangle.Width, 3);
        Assert.Equal(3, triangle.Points.Count);
        Assert.Equal(-2f, triangle.Bounds.m_XMin, 3);
        Assert.Equal(triangle, LandscapeArea.Polygon([V(0, 0, 0), V(10, 0, 0), V(0, 0, 10)], blend: 2, falloff: LandscapeFalloff.Linear));
    }

    [Fact]
    public void AMaskIsBlendedBetweenItsSamplesAndTurnedWithTheArea()
    {
        var weights = new float[,] { { 0, 1 }, { 0, 1 } }; // nothing at x −2, all at x +2
        var mask = LandscapeArea.Mask(V(0, 0, 0), weights, 4, 4);
        weights[0, 0] = 1; // copied: a later change does not reach it
        Assert.Equal(0f, mask.Weight(-2, 0), 3);
        Assert.Equal(0.5f, mask.Weight(0, 0), 3);
        Assert.Equal(1f, mask.Weight(2, 1), 3);
        Assert.Equal(0f, mask.Weight(3, 0)); // outside
        var turned = LandscapeArea.Mask(V(0, 0, 0), new float[,] { { 0, 1 }, { 0, 1 } }, 4, 4, rotation: 90);
        Assert.Equal(1f, turned.Weight(0, 2), 3); // its x side now along z
        Assert.Equal(0f, turned.Weight(0, -2), 3);
    }

    [Fact]
    public void BadShapesAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Circle(V(0, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Circle(V(0, 0, 0), LandscapeArea.MaximumSide));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Circle(V(0, 0, 0), 4, LandscapeArea.MaximumBlend + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Circle(V(0, 0, 0), 4, 1, (LandscapeFalloff)9));
        Assert.Throws<ArgumentNullException>(() => LandscapeArea.Polygon(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Polygon([V(0, 0, 0), V(1, 0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Polygon([V(0, 0, 0), V(1, 0, 0), V(2, 0, 0)])); // on one line
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Polygon([V(0, 0, 0), V(70, 0, 0), V(0, 0, 5)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Polygon([V(0, 0, 0), V(float.NaN, 0, 0), V(0, 0, 5)]));
        Assert.Throws<ArgumentNullException>(() => LandscapeArea.Mask(V(0, 0, 0), null!, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Mask(V(0, 0, 0), new float[1, 4], 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Mask(V(0, 0, 0), new float[,] { { 0, float.NaN }, { 0, 0 } }, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Mask(V(0, 0, 0), new float[2, 2], 0, 4));
    }

    // The basic changes over the SDK's own shapes.

    [Fact]
    public void ARaiseOverACircleIsWrittenSampleBySample()
    {
        var changes = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) => changes.Add(e);
        var circle = LandscapeArea.Circle(V(10, 0, 10), 3, blend: 2);
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(circle, 1.5f));
        Assert.Empty(_game.Applied); // not the game's tool
        Assert.Equal(FakeTerrain.Ground(10) + 1.5f, Height(10, 10), 3);
        Assert.Equal(FakeTerrain.Ground(13) + 1.5f, Height(13, 10), 3);
        Assert.Equal(FakeTerrain.Ground(14) + 0.75f, Height(14, 10), 3);
        Assert.Equal(FakeTerrain.Ground(16), Height(16, 10), 3); // past the blend: not written
        Assert.Equal(GroundKind.Dirt, Landscape.KindAt(10, 10));
        Assert.True(Landscape.IsCleared(10, 10));
        var change = Assert.Single(changes);
        Assert.Equal(LandscapeOperation.Raise, change.Operation);
        Assert.Equal(LandscapeSource.Mod, change.Source);
        Assert.Equal("test.mod", change.ModId);
        var write = Assert.Single(_game.Writes).Write;
        Assert.Equal(LandscapeOperation.Raise, write.Operation);
        Assert.False(write.Raw);
    }

    [Fact]
    public void LevelLowerAndPaintTakeAnyShape()
    {
        var polygon = LandscapeArea.Polygon([V(0, 0, 0), V(8, 0, 0), V(8, 0, 4), V(0, 0, 4)], blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Level(polygon, 50, new LandscapeOptions { Kind = GroundKind.Path, ClearVegetation = false }));
        Assert.Equal(50f, Height(4, 2), 3);
        Assert.Equal(GroundKind.Path, Landscape.KindAt(4, 2));
        Assert.False(Landscape.IsCleared(4, 2));
        Assert.Equal(FakeTerrain.Ground(10), Height(10, 2), 3);

        var circle = LandscapeArea.Circle(V(30, 0, 0), 2, 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Lower(circle, 2));
        Assert.Equal(FakeTerrain.Ground(30) - 2, Height(30, 0), 3);

        Assert.Equal(LandscapeResult.Done, Landscape.Paint(LandscapeArea.Circle(V(50, 0, 0), 2, 0), GroundKind.Road));
        Assert.Equal(GroundKind.Road, Landscape.KindAt(50, 0));
        Assert.False(Landscape.IsCleared(50, 0)); // painting keeps the grass
        Assert.Equal(FakeTerrain.Ground(50), Height(50, 0), 3);

        Assert.Equal(LandscapeResult.Done, Landscape.ClearVegetation(LandscapeArea.Circle(V(60, 0, 0), 2, 0)));
        Assert.True(Landscape.IsCleared(60, 0));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(60, 0));
        Assert.Empty(_game.Applied);
    }

    [Fact]
    public void KeepKindLeavesTheGroundsKindAndGrass()
    {
        _game.Kinds[(70, 0)] = GroundKind.Road;
        var options = new LandscapeOptions { KeepKind = true, ClearVegetation = false };
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Circle(V(70, 0, 0), 3, 0), 1, options));
        Assert.Equal(FakeTerrain.Ground(70) + 1, Height(70, 0), 3);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(70, 0));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(71, 0));
        Assert.False(Landscape.IsCleared(71, 0));
        Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 0), 4, 4), 1, options);
        Assert.True(_game.Applied[^1].Options.KeepKind); // handed to the game's tool too
    }

    [Fact]
    public void TheGamesOwnShapesStillGoThroughItsTool()
    {
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 0), 4, 4), 1));
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Point(V(0, 0, 0)), 1));
        Assert.Equal(2, _game.Applied.Count);
        Assert.Empty(_game.Writes);
    }

    // Smoothing, ramps and a mod's own shape.

    [Fact]
    public void SmoothingLowersASpikeAndRaisesAPit()
    {
        _game.Written[FakeTerrain.Cell(0, 0)] = FakeTerrain.Ground(0) + 4;
        _game.Written[FakeTerrain.Cell(5, 0)] = FakeTerrain.Ground(5) - 4;
        var area = LandscapeArea.Rectangle(V(2.5f, 0, 0), 10, 4, blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Smooth(area, 1));
        float spike = Height(0, 0) - FakeTerrain.Ground(0), pit = Height(5, 0) - FakeTerrain.Ground(5);
        Assert.InRange(spike, 0.1f, 0.2f); // 4 m shared among 25 neighbours
        Assert.InRange(pit, -0.2f, -0.1f);
        Assert.Equal(LandscapeOperation.Smooth, _game.Writes[^1].Write.Operation);

        // A gentle stroke goes part of the way.
        _game.Written[FakeTerrain.Cell(20, 0)] = FakeTerrain.Ground(20) + 4;
        Landscape.Smooth(LandscapeArea.Circle(V(20, 0, 0), 2, 0), 0.25f);
        Assert.InRange(Height(20, 0) - FakeTerrain.Ground(20), 2.9f, 3.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Smooth(area, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Smooth(area, 1.5f));
    }

    [Fact]
    public void ARampRunsBetweenItsTwoEnds()
    {
        var area = LandscapeArea.Rectangle(V(5, 0, 0), 10, 4, blend: 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Slope(area, V(0, 40, 0), V(10, 45, 0)));
        Assert.Equal(40f, Height(0, 0), 3);
        Assert.Equal(42.5f, Height(5, 1), 3);
        Assert.Equal(45f, Height(10, -1), 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Slope(area, V(0, 40, 0), V(0, 45, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Slope(area, V(0, -1, 0), V(10, 45, 0)));
    }

    [Fact]
    public void AModsShapeIsAskedForEveryPointWithItsOffset()
    {
        var asked = new List<(float, float)>();
        var circle = LandscapeArea.Circle(V(40, 0, 40), 2, 0);
        Assert.Equal(LandscapeResult.Done, Landscape.Shape(circle, (dx, dz, now) =>
        {
            asked.Add((dx, dz));
            return dx > 0 ? now + 1 : float.NaN; // only the east half, the west left as it is
        }));
        Assert.Contains((0f, 0f), asked);
        Assert.All(asked, a => Assert.True(MathF.Sqrt(a.Item1 * a.Item1 + a.Item2 * a.Item2) <= 2.01f));
        Assert.Equal(FakeTerrain.Ground(41) + 1, Height(41, 40), 3);
        Assert.Equal(FakeTerrain.Ground(39), Height(39, 40), 3);
        Assert.Equal(LandscapeOperation.Shape, _game.Writes[^1].Write.Operation);

        // Heights are kept within the world's.
        Landscape.Shape(circle, (_, _, _) => 500);
        Assert.Equal(Landscape.MaximumHeight, Height(40, 40), 3);
        Assert.Throws<ArgumentNullException>(() => Landscape.Shape(circle, null!));
    }

    [Fact]
    public void AShapeThatThrowsChangesNothing()
    {
        int writes = _game.Writes.Count;
        var result = Landscape.Shape(LandscapeArea.Circle(V(0, 0, 0), 2), (_, _, _) => throw new InvalidOperationException("the mod's bug"));
        Assert.Equal(LandscapeResult.Failed, result);
        Assert.Equal(writes, _game.Writes.Count);
    }

    // Measuring and snapshots.

    [Fact]
    public void MeasureGivesTheLowestTheHighestAndTheAverage()
    {
        var heights = Landscape.Measure(LandscapeArea.Rectangle(V(0, 0, 0), 10, 2, blend: 0));
        Assert.True(heights.IsKnown);
        Assert.Equal(FakeTerrain.Ground(-5), heights.Minimum, 3);
        Assert.Equal(FakeTerrain.Ground(5), heights.Maximum, 3);
        Assert.Equal(FakeTerrain.Ground(0), heights.Average, 3);
        Assert.False(Landscape.Measure(LandscapeArea.Circle(V(99, 0, 0), 4)).IsKnown);
        Assert.Equal("unknown", Landscape.Measure(LandscapeArea.Circle(V(99, 0, 0), 4)).ToString());
        _game.IsAvailable = false;
        Assert.True(float.IsNaN(Landscape.Measure(LandscapeArea.Circle(V(0, 0, 0), 4)).Average));
    }

    [Fact]
    public void ASnapshotPutsTheGroundBackExactly()
    {
        var area = LandscapeArea.Circle(V(10, 0, 0), 3, 1);
        _game.Kinds[(10, 0)] = GroundKind.Natural;
        var snapshot = Landscape.Capture(area);
        Assert.NotNull(snapshot);
        Assert.Equal(FakeTerrain.Ground(10), snapshot.CenterHeight, 3);
        Assert.Equal(FakeTerrain.Ground(11), snapshot.HeightAt(11, 0), 3);
        Assert.True(float.IsNaN(snapshot.HeightAt(50, 0)));

        Landscape.Raise(area, 2);
        Landscape.Paint(area, GroundKind.Road, new LandscapeOptions { ClearVegetation = true });
        Assert.Equal(GroundKind.Road, Landscape.KindAt(10, 0));

        var changes = new List<LandscapeOperation>();
        Landscape.GroundChanging += (_, e) => changes.Add(e.Operation);
        Assert.Equal(LandscapeResult.Done, snapshot.Restore());
        Assert.Equal([LandscapeOperation.Restore], changes);
        Assert.Equal(FakeTerrain.Ground(10), Height(10, 0), 3);
        Assert.Equal(FakeTerrain.Ground(13.5f), Height(13.5f, 0), 3);
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(10, 0));
        Assert.False(Landscape.IsCleared(10, 0));
        Assert.True(_game.Writes[^1].Write.Raw);
        Assert.Null(_game.Writes[^1].Write.ObjectsAt); // putting back takes no objects
    }

    [Fact]
    public void ASnapshotIsPastedMovedTurnedAndRaised()
    {
        // A mound of 2 m on a strip along x at the origin, kept and laid turned a quarter at (50, 50), 1 m higher.
        var strip = LandscapeArea.Rectangle(V(0, 0, 0), 6, 2, blend: 0);
        Landscape.Shape(strip, (_, _, _) => 40, new LandscapeOptions { Kind = GroundKind.Road }); // a rectangle's level would be the game's tool
        var snapshot = Landscape.Capture(LandscapeArea.Rectangle(V(0, 0, 0), 6, 2, blend: 0, falloff: LandscapeFalloff.Linear))!;
        Assert.Equal(LandscapeResult.Done, snapshot.PasteAt(V(50, 0, 50), rotation: 90, heightOffset: 1));
        Assert.Equal(41f, Height(50, 50), 3);
        Assert.Equal(41f, Height(50, 52.5f), 3); // the strip now runs along z
        Assert.Equal(FakeTerrain.Ground(52.5f), Height(52.5f, 50), 3);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(50, 52));
        Assert.Equal(LandscapeOperation.Paste, _game.Writes[^1].Write.Operation);
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.PasteAt(V(float.NaN, 0, 0)));
    }

    [Fact]
    public void NothingIsKeptWhereTheGroundIsNotLoaded()
    {
        Assert.Null(Landscape.Capture(LandscapeArea.Circle(V(99, 0, 0), 4)));
        _game.IsAvailable = false;
        Assert.Null(Landscape.Capture(LandscapeArea.Circle(V(0, 0, 0), 4)));
    }

    // What every change goes through.

    [Fact]
    public void WritesAreCheckedCancelledAndQueuedAsEveryChange()
    {
        var circle = LandscapeArea.Circle(V(0, 0, 10), 3);
        _game.Buildings = true;
        Assert.Equal(LandscapeResult.Blocked, Landscape.Smooth(circle));
        Assert.Equal(LandscapeResult.Blocked, Landscape.Check(circle, LandscapeOperation.Smooth));
        _game.Buildings = false;
        Assert.Equal(LandscapeResult.Done, Landscape.Check(circle, LandscapeOperation.Raise)); // no game check for a circle
        Assert.Equal(LandscapeResult.NotLoaded, Landscape.Raise(LandscapeArea.Circle(V(98, 0, 0), 3), 1));
        Assert.Empty(_game.Writes);

        Landscape.GroundChanging += (_, e) => e.Cancel = e.Operation == LandscapeOperation.Slope;
        Assert.Equal(LandscapeResult.Cancelled, Landscape.Slope(circle, V(0, 30, 0), V(0, 31, 20)));
        Assert.Empty(_game.Writes);

        Dispatcher.Tick(); // this thread is the main one now
        LandscapeResult result = LandscapeResult.Failed;
        var other = new Thread(() => result = Landscape.Raise(circle, 1));
        other.Start();
        other.Join();
        Assert.Equal(LandscapeResult.Queued, result);
        Assert.Empty(_game.Writes);
        Dispatcher.Tick();
        Assert.Single(_game.Writes);

        _game.Throw = true;
        Assert.Equal(LandscapeResult.Failed, Landscape.Raise(circle, 1));
    }

    [Fact]
    public void ObjectsGoWhereTheVegetationIsCleared()
    {
        foreach (var (x, z) in new[] { (0f, 0f), (2f, 0f), (5f, 0f), (30f, 0f) })
            _game.Objects.Add(new FakeObject { AssetName = "Harvest_Plant_Flax", Position = V(x, 0, z), IsRemovable = true });
        Landscape.Raise(LandscapeArea.Circle(V(0, 0, 0), 2, blend: 2), 1);
        Assert.Equal([5f, 30f], _game.Objects.Where(o => !o.IsGone).Select(o => o.Position.x)); // the one at 5 m lies past the blend's middle
        Landscape.Raise(LandscapeArea.Circle(V(30, 0, 0), 2), 1, new LandscapeOptions { ClearVegetation = false });
        Assert.Contains(_game.Objects, o => !o.IsGone && o.Position.x == 30f);
    }

    // The console.

    [Fact]
    public void TheConsoleSmoothsAndUndoesItsLastChange()
    {
        DevConsole.Execute("undo");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text == "Nothing to undo.");
        DevConsole.Execute("raise 2");
        float ahead = TerrainCommands.AheadOf(TerrainCommands.DefaultSize);
        Assert.Single(_game.Applied); // the console's square goes through the game's tool (the fake applies nothing)
        _game.Written[FakeTerrain.Cell(0, ahead)] = 99;
        DevConsole.Execute("undo");
        Assert.Contains(DevConsole.Lines, l => l.Text.StartsWith("Put back", StringComparison.Ordinal));
        Assert.Equal(FakeTerrain.Ground(0), Height(0, ahead), 3);
        DevConsole.Execute("undo");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text == "Nothing to undo.");
        DevConsole.Execute("smooth 0.3 6");
        Assert.Contains(DevConsole.Lines, l => l.Text.StartsWith("Smoothed 6 × 6 m", StringComparison.Ordinal));
        DevConsole.Execute("smooth 2");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("strength", StringComparison.Ordinal));
    }
}
