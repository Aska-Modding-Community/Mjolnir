using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// Still water on a fake game: a basin dug in the fake ground filled into a pond as far as the ground under the level
/// reaches, refused where it is open, above the level, in water, not loaded or running into another pond; listed, found at a
/// point and nearest, measured, filled again when the ground under it changes, taken away; a pond read from the world filled
/// by the tick; a lake read; the console's water.
/// </summary>
[Collection("Terrain")]
public sealed class WaterTests : IDisposable
{
    private readonly FakeTerrain _game = new();
    private readonly FakeWater _water = new();

    public WaterTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        WaterBodies.Source = _water;
        Dispatcher.Tick(); // this thread is the main one
        DevConsole.OwnerOf = a => a == typeof(WaterTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    // A round basin dug 3 m into the fake ground (which rises 0.1 m a metre along x).
    private void Dig(float cx, float cz, float radius, float depth = 3f)
    {
        for (float x = cx - radius; x <= cx + radius; x += 0.5f)
            for (float z = cz - radius; z <= cz + radius; z += 0.5f)
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) <= radius * radius)
                    _game.Written[FakeTerrain.Cell(x, z)] = FakeTerrain.Ground(x) - depth;
    }

    [Fact]
    public void ABasinIsFilledAsFarAsItsGroundLiesUnderTheLevel()
    {
        Dig(20, 20, 6);
        float level = FakeTerrain.Ground(20) - 1; // 2 m over the basin's floor, 1 m under its rim
        var fill = WaterBodies.Measure(V(20, 0, 20), level, 32);
        Assert.Equal(WaterFillResult.Fits, fill.Result);
        Assert.Equal(level, fill.Level);
        Assert.InRange(fill.Area, 105, 120);            // a circle of 6 m: 113 m²
        Assert.InRange(fill.Volume, 200, 250);          // 2 m deep on average
        Assert.InRange(fill.Bounds.m_Width, 11.5f, 13.5f);
        Assert.True(fill.Covers(20, 20));
        Assert.True(fill.Covers(25, 20));
        Assert.False(fill.Covers(27, 20));
        Assert.InRange(fill.DepthAt(20, 20), 1.9f, 2.1f);
        Assert.Equal(0f, fill.DepthAt(30, 20));
        Assert.Empty(_water.Placed);                    // measured, not placed
    }

    [Fact]
    public void APondIsRefusedWhereItWouldNotHold()
    {
        float at20 = FakeTerrain.Ground(20);
        Assert.Equal(WaterFillResult.Spills, WaterBodies.Measure(V(20, 0, 20), at20 + 1, 32).Result);    // open, sloping ground
        Assert.Equal(WaterFillResult.AboveLevel, WaterBodies.Measure(V(20, 0, 20), at20 - 1, 32).Result);
        Assert.Equal(WaterFillResult.InWater, WaterBodies.Measure(V(-60, 0, 0), 99, 16).Result);         // the fake's sea: x < -40
        Assert.Equal(WaterFillResult.NotLoaded, WaterBodies.Measure(V(95, 0, 0), 99, 32).Result);        // the fake loads ±100 m
        Dig(20, 20, 6);
        Assert.Equal(WaterFillResult.Spills, WaterBodies.Measure(V(20, 0, 20), at20 - 1, 8).Result);     // larger than its square
        Assert.Null(WaterBodies.Place(V(20, 0, 20), at20 + 1, 32));
        Assert.Empty(_water.Placed);
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterBodies.Measure(V(20, 0, 20), at20, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterBodies.Place(V(20, 0, 20), at20, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterBodies.Place(V(float.NaN, 0, 20), at20, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterBodies.Place(V(20, 0, 20), float.NaN, 32));
    }

    [Fact]
    public void APondIsPlacedShownListedAndFound()
    {
        Dig(20, 20, 6);
        float level = FakeTerrain.Ground(20) - 1;
        var pond = WaterBodies.Place(V(20, 0, 20), level, 32);
        Assert.NotNull(pond);
        var placed = Assert.Single(_water.Placed);
        Assert.Equal(level, placed.Center.y);
        Assert.Equal(32f, placed.Size);
        Assert.NotNull(_water.Shown[placed].Fill);          // drawn at once
        Assert.Equal(level, pond.Level);
        Assert.False(pond.IsWorldLake);
        Assert.False(pond.IsGone);
        Assert.InRange(pond.Area, 105, 120);
        Assert.InRange(pond.Volume, 200, 250);
        Assert.True(pond.Covers(22, 21));
        Assert.False(pond.Covers(29, 20));
        Assert.InRange(pond.DepthAt(20, 20), 1.9f, 2.1f);
        Assert.Equal(0f, pond.DepthAt(40, 40));
        Assert.Single(WaterBodies.All);
        Assert.NotNull(WaterBodies.At(20, 20));
        Assert.Null(WaterBodies.At(40, 40));
        Assert.NotNull(WaterBodies.Nearest(V(25, 0, 25), 10));
        Assert.Null(WaterBodies.Nearest(V(60, 0, 60), 10));
        Assert.StartsWith("pond at (20, 20)", pond.ToString(), StringComparison.Ordinal);
        // A second pond in the same basin runs into the first.
        Assert.Equal(WaterFillResult.Overlaps, WaterBodies.Measure(V(22, 0, 20), level, 32).Result);
        Assert.Null(WaterBodies.Place(V(22, 0, 20), level, 32));
    }

    [Fact]
    public void APondFollowsTheGroundAndIsTakenAway()
    {
        Dig(20, 20, 4);
        float level = FakeTerrain.Ground(20) - 1;
        var pond = WaterBodies.Place(V(20, 0, 20), level, 32)!;
        float before = pond.Area;
        WaterBodies.Tick(_game);
        Assert.Equal(1, _water.ShowCount);                   // nothing changed: not filled again
        Dig(20, 20, 7);                                      // its shore dug wider
        Landscape.RaiseChanged(LandscapeArea.MakeRect(10, 10, 20, 20), LandscapeOperation.Lower, LandscapeSource.Mod, "test.mod");
        WaterBodies.Tick(_game);
        Assert.Equal(2, _water.ShowCount);
        Assert.True(pond.Area > before * 2);
        Landscape.RaiseChanged(LandscapeArea.MakeRect(60, 60, 4, 4), LandscapeOperation.Lower, LandscapeSource.Mod, "test.mod");
        WaterBodies.Tick(_game);
        Assert.Equal(2, _water.ShowCount);                   // a change elsewhere leaves it
        Assert.True(pond.Remove());
        Assert.True(pond.IsGone);
        WaterBodies.Tick(_game);
        Assert.Empty(WaterBodies.All);
        Assert.True(pond.Remove());                          // gone already
    }

    [Fact]
    public void APondOfTheWorldIsFilledByTheTickAndALakeIsRead()
    {
        Dig(-20, 30, 5);
        var saved = new FakeWater.Pond(V(-20, FakeTerrain.Ground(-20) - 1, 30), 24);
        _water.Bodies.Add(saved);
        _water.Bodies.Add(new FakeWater.Lake(V(50, 40, 50), 20));
        Assert.Empty(_water.Shown);
        WaterBodies.Tick(_game);
        Assert.NotNull(_water.Shown[saved].Fill);
        var all = WaterBodies.All;
        Assert.Equal(2, all.Count);
        var lake = all.Single(b => b.IsWorldLake);
        Assert.Equal(40f, lake.Level);
        Assert.True(lake.Covers(52, 48));
        Assert.False(lake.Covers(70, 48));
        Assert.Equal(0f, lake.Area);
        Assert.False(lake.Remove());
        Assert.StartsWith("lake at (50, 50)", lake.ToString(), StringComparison.Ordinal);
        var pond = all.Single(b => !b.IsWorldLake);
        Assert.True(pond.Covers(-20, 30));
        // A pond would not run into the lake.
        for (float x = 44; x <= 56; x += 0.5f)
            for (float z = 44; z <= 56; z += 0.5f)
                _game.Written[FakeTerrain.Cell(x, z)] = 30;
        Assert.Equal(WaterFillResult.Overlaps, WaterBodies.Measure(V(50, 0, 50), 33, 32).Result);
    }

    [Fact]
    public void TheConsoleFillsListsAndTakesAwayAPond()
    {
        DevConsole.Execute("water");
        Assert.Contains(DevConsole.Lines, l => l.Text.Contains("No ponds or lakes", StringComparison.Ordinal));
        Dig(0, 4, 5); // ahead of the fake player at (0, 0), looking along z
        DevConsole.Execute("water fill 1.5 24");
        Assert.Contains(DevConsole.Lines, l => l.Text.StartsWith("Filled a pond", StringComparison.Ordinal));
        Assert.Single(_water.Placed);
        DevConsole.Execute("water");
        Assert.Contains(DevConsole.Lines, l => l.Text.Contains("1 pond(s) and lake(s)", StringComparison.Ordinal));
        DevConsole.Execute("water fill 9 24");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("Spills", StringComparison.Ordinal));
        DevConsole.Execute("water fill 1 24");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("Overlaps", StringComparison.Ordinal));
        DevConsole.Execute("water remove");
        Assert.Contains(DevConsole.Lines, l => l.Text.StartsWith("Took away the pond", StringComparison.Ordinal));
        DevConsole.Execute("water remove");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("No pond within", StringComparison.Ordinal));
    }
}

/// <summary>The game's still water, faked: what the model places and shows, and ponds and lakes a test adds.</summary>
internal sealed class FakeWater : IWaterSource
{
    public bool IsAvailable { get; set; } = true;

    public List<IWaterBodyView> Bodies { get; } = [];

    IReadOnlyList<IWaterBodyView> IWaterSource.Bodies => Bodies;

    public List<Pond> Placed { get; } = [];

    public Dictionary<IWaterBodyView, (WaterFill? Fill, int Count)> Shown { get; } = [];

    public int ShowCount { get; private set; }

    public IWaterBodyView? Place(Vector3 center, float size)
    {
        var pond = new Pond(center, size);
        Placed.Add(pond);
        Bodies.Add(pond);
        return pond;
    }

    public void Show(IWaterBodyView pond, WaterFill? fill)
    {
        ShowCount++;
        Shown[pond] = (fill, Shown.TryGetValue(pond, out var was) ? was.Count + 1 : 1);
    }

    public sealed class Pond(Vector3 center, float size) : IWaterBodyView
    {
        public object Key { get; } = new object();

        public Vector3 Center { get; } = center;

        public float Size { get; } = size;

        public bool IsWorldLake => false;

        public bool IsGone { get; private set; }

        public Rect Bounds => LandscapeArea.MakeRect(Center.x - Size / 2, Center.z - Size / 2, Size, Size);

        public bool Covers(float x, float z) => false;

        public bool Remove() => IsGone = true;
    }

    public sealed class Lake(Vector3 center, float radius) : IWaterBodyView
    {
        public object Key { get; } = new object();

        public Vector3 Center { get; } = center;

        public float Size => radius * 2;

        public bool IsWorldLake => true;

        public bool IsGone => false;

        public Rect Bounds => LandscapeArea.MakeRect(Center.x - radius, Center.z - radius, radius * 2, radius * 2);

        public bool Covers(float x, float z) => (x - Center.x) * (x - Center.x) + (z - Center.z) * (z - Center.z) <= radius * radius;

        public bool Remove() => false;
    }
}
