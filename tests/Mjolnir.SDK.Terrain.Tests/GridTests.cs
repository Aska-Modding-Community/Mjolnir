using System;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// What a tool shows on the ground, on a fake game: a grid of cells placed on the ground's height, painted, coloured, moved,
/// hidden and taken away; a ray meeting the ground; an outline's height and line width.
/// </summary>
[Collection("Terrain")]
public sealed class GridTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public GridTests()
    {
        Dispatcher.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        Dispatcher.ResetForTests();
    }

    private static Color Blue()
    {
        Color c = default;
        c.b = 1;
        c.a = 1;
        return c;
    }

    // Grids.

    [Fact]
    public void AGridIsPlacedOnTheGroundAndPaintedOnTheNextFrame()
    {
        var grid = Landscape.ShowGrid(V(10, 0, 20), 4, 3, cellSize: 2, rotation: 30);
        Assert.Empty(_game.Grids);
        grid.SetCell(1, 0, 0.25f);
        grid.MarkCell(3, 2);
        Landscape.Tick();
        var view = Assert.Single(_game.Grids);
        Assert.Equal((4, 3), (view.Columns, view.Rows));
        Assert.True(view.IsShown);
        Assert.Equal(10f, view.Center.x);
        Assert.Equal(20f, view.Center.z);
        Assert.Equal(FakeTerrain.Ground(10), view.Center.y, 3); // the ground's height at its middle
        Assert.Equal(30f, view.Rotation);
        Assert.Equal(2f, view.CellSize);
        Assert.Equal(12, view.Kinds!.Length);
        Assert.Equal(GridCellKind.Shade, view.Kinds[1]);
        Assert.Equal(0.25f, view.Fractions![1]);
        Assert.Equal(GridCellKind.Mark, view.Kinds[2 * 4 + 3]);
        Assert.Equal(10, view.Kinds.Count(k => k == GridCellKind.Empty));
        Assert.Equal((null, null), view.Colours); // the game's own
        Assert.Contains("4 × 3 cells of 2 m", grid.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyWhatChangedIsDrawnAgain()
    {
        var grid = Landscape.ShowGrid(V(0, 0, 0), 8, 8);
        Landscape.Tick();
        var view = _game.Grids.Single();
        Assert.Equal((1, 1), (view.Places, view.Paints));
        Landscape.Tick();
        Assert.Equal((1, 1), (view.Places, view.Paints));

        grid.SetCell(0, 0, 1);
        grid.SetCell(0, 0, 1); // the same again changes nothing
        Landscape.Tick();
        Assert.Equal((1, 2), (view.Places, view.Paints));

        grid.Center = V(5, 0, 6);
        Landscape.Tick();
        Assert.Equal((2, 2), (view.Places, view.Paints));
        Assert.Equal(FakeTerrain.Ground(5), view.Center.y, 3);
        for (int i = 0; i < 30; i++) Landscape.Tick();
        Assert.Equal(3, view.Places); // the ground read again under it now and then

        grid.NearColor = Blue();
        Landscape.Tick();
        Assert.Equal(1f, view.Colours.Near!.Value.b);
        Assert.Null(view.Colours.Far);

        grid.Clear();
        Landscape.Tick();
        Assert.All(view.Kinds!, k => Assert.Equal(GridCellKind.Empty, k));
        grid.SetCell(2, 2, 0.5f);
        grid.ClearCell(2, 2);
        Landscape.Tick();
        Assert.All(view.Kinds!, k => Assert.Equal(GridCellKind.Empty, k));
    }

    [Fact]
    public void AGridHidesWithoutAWorldOrOffTheLoadedGroundAndGoesWhenDisposed()
    {
        var grid = Landscape.ShowGrid(V(0, 0, 0), 2, 2);
        Landscape.Tick();
        var view = _game.Grids.Single();
        grid.IsVisible = false;
        Landscape.Tick();
        Assert.False(view.IsShown);
        grid.IsVisible = true;
        Landscape.Tick();
        Assert.True(view.IsShown);

        _game.IsAvailable = false;
        Landscape.Tick();
        Assert.False(view.IsShown);
        _game.IsAvailable = true;
        int paints = view.Paints;
        Landscape.Tick();
        Assert.True(view.IsShown);
        Assert.Equal(paints + 1, view.Paints); // drawn whole again once a world is back

        grid.Center = V(500, 0, 0);
        Landscape.Tick();
        Assert.False(view.IsShown);
        grid.Center = V(0, 0, 0);
        Landscape.Tick();
        Assert.True(view.IsShown);

        grid.Dispose();
        Landscape.Tick();
        Assert.True(view.IsDestroyed);
        Landscape.Tick();
        Assert.Single(_game.Grids);
    }

    [Fact]
    public void ACellsMiddleFollowsTheGridsTurn()
    {
        var grid = Landscape.ShowGrid(V(10, 0, 20), 4, 2, cellSize: 2);
        var corner = grid.CellCenter(0, 0);
        Assert.Equal(7f, corner.x, 3); // 4 × 2 m across x: from 6 to 14
        Assert.Equal(19f, corner.z, 3); // 2 × 2 m along z: from 18 to 22
        Assert.Equal(13f, grid.CellCenter(3, 1).x, 3);
        Assert.Equal(21f, grid.CellCenter(3, 1).z, 3);

        // Turned as a rectangle is: the same place as the same rectangle's turned corner.
        grid.Rotation = 90;
        var (dx, dz) = LandscapeArea.Turn(-3, -1, 90);
        Assert.Equal(10 + dx, grid.CellCenter(0, 0).x, 3);
        Assert.Equal(20 + dz, grid.CellCenter(0, 0).z, 3);
    }

    [Fact]
    public void AGridIsCheckedAsItIsMade()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.ShowGrid(V(0, 0, 0), 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.ShowGrid(V(0, 0, 0), 4, LandscapeGrid.MaximumCells + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.ShowGrid(V(0, 0, 0), 4, 4, cellSize: 0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.ShowGrid(V(float.NaN, 0, 0), 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.ShowGrid(V(0, 0, 0), 4, 4, rotation: float.PositiveInfinity));
        var grid = Landscape.ShowGrid(V(0, 0, 0), 4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetCell(4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.MarkCell(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetCell(0, 0, float.NaN));
        grid.SetCell(0, 0, 3); // kept within 0…1
        Landscape.Tick();
        Assert.Equal(1f, _game.Grids.Single().Fractions![0]);
    }

    [Fact]
    public void AGridMayBeChangedFromAnotherThread()
    {
        LandscapeGrid? grid = null;
        var thread = new System.Threading.Thread(() =>
        {
            grid = Landscape.ShowGrid(V(0, 0, 0), 3, 3);
            grid.SetCell(1, 1, 0.5f);
        });
        thread.Start();
        thread.Join();
        Landscape.Tick();
        Assert.Equal(GridCellKind.Shade, _game.Grids.Single().Kinds![4]);
    }

    // Rays.

    [Fact]
    public void ARayMeetsTheGroundWithinACentimetre()
    {
        // From 20 m over the ground at x = 0, down and along x: the ground rises 0.1 m a metre.
        Assert.True(Landscape.TryRaycast(V(0, 50, 0), V(1, -1, 0), 200, out var point));
        Assert.Equal(FakeTerrain.Ground(point.x), point.y, 1);
        Assert.InRange(point.y - FakeTerrain.Ground(point.x), -0.012f, 0.012f);
        Assert.Equal(18.18f, point.x, 1); // 50 − x = 30 + 0.1x

        // Straight down, through the public overload's twin.
        Assert.True(Landscape.TryRaycast(V(10, 80, 5), V(0, -3, 0), 100, out var below));
        Assert.Equal(10f, below.x, 3);
        Assert.Equal(FakeTerrain.Ground(10), below.y, 1);
    }

    [Fact]
    public void ARayMissesWhatItCannotReach()
    {
        Assert.False(Landscape.TryRaycast(V(0, 50, 0), V(1, -1, 0), 10, out _)); // too short
        Assert.False(Landscape.TryRaycast(V(0, 50, 0), V(0, 1, 0), 100, out _)); // upwards
        Assert.False(Landscape.TryRaycast(V(0, 10, 0), V(1, 0, 0), 100, out _)); // under the ground from the start
        Assert.False(Landscape.TryRaycast(V(0, 50, 0), V(0, 0, 0), 100, out _)); // no direction
        Assert.False(Landscape.TryRaycast(V(300, 50, 0), V(0, -1, 0), 100, out _)); // not loaded there
        // From beyond the loaded ground into it.
        Assert.True(Landscape.TryRaycast(V(120, 60, 0), V(-1, -0.3f, 0), 300, out var inside));
        Assert.InRange(inside.x, -100f, 100f);
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.TryRaycast(V(0, 50, 0), V(0, -1, 0), 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.TryRaycast(V(0, 50, 0), V(0, -1, 0), Landscape.MaximumRayDistance + 1, out _));

        _game.IsAvailable = false;
        Assert.False(Landscape.TryRaycast(V(0, 50, 0), V(0, -1, 0), 100, out _));
    }

    // Outlines.

    [Fact]
    public void AnOutlineRunsAsHighAndAsWideAsAsked()
    {
        var outline = Landscape.ShowOutline(LandscapeArea.Circle(V(0, 0, 10), 3), Blue());
        Landscape.Tick();
        var view = _game.Outlines.Single();
        Assert.Equal(0.2f, view.Width, 3);
        Assert.All(view.Edge!, p => Assert.Equal(FakeTerrain.Ground(p.x) + 0.3f, p.y, 3));

        outline.Height = 0.15f;
        outline.LineWidth = 0.1f;
        Landscape.Tick();
        Assert.Equal(0.1f, view.Width, 3);
        Assert.All(view.Edge!, p => Assert.Equal(FakeTerrain.Ground(p.x) + 0.15f, p.y, 3));
        Assert.Equal(0.15f, outline.Height);

        Assert.Throws<ArgumentOutOfRangeException>(() => outline.Height = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => outline.Height = LandscapeOutline.MaximumHeight + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => outline.LineWidth = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => outline.LineWidth = float.NaN);
    }
}
