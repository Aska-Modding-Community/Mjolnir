using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// The ground on a fake game: reading it with and without a world and in and out of the loaded part; changes checked,
/// refused where not loaded or on a building, handed to the game as asked, queued from another thread, cancelled by a
/// handler, reported, and owned by the mod that made them; areas and their bounds; the console's commands.
/// </summary>
[Collection("Terrain")]
public sealed class LandscapeTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public LandscapeTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(LandscapeTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    internal static Vector3 V(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    private static LandscapeArea Square(float x = 0, float z = 10, float side = 4) => LandscapeArea.Rectangle(V(x, 0, z), side, side);

    [Fact]
    public void WithoutAWorldTheGroundReadsNothingAndCannotBeChanged()
    {
        _game.IsAvailable = false;
        Assert.False(Landscape.IsAvailable);
        Assert.False(Landscape.IsLoaded(0, 0));
        Assert.True(float.IsNaN(Landscape.HeightAt(0, 0)));
        Assert.False(Landscape.TryGetHeight(0, 0, out _));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(0, 0));
        Assert.Equal(LandscapeResult.NotAvailable, Landscape.Raise(Square(), 1));
        Assert.Equal(LandscapeResult.NotAvailable, Landscape.Check(Square(), LandscapeOperation.Raise));
        Landscape.Source = null;
        Assert.False(Landscape.IsAvailable);
        Assert.Empty(_game.Applied);
    }

    [Fact]
    public void TheGroundIsReadFromTheGameWhereItIsLoaded()
    {
        _game.Kinds[(10, 20)] = GroundKind.Road;
        _game.Cleared.Add((10, 20));
        Assert.True(Landscape.IsAvailable);
        Assert.Equal(31f, Landscape.HeightAt(10, 20));
        Assert.True(Landscape.TryGetHeight(10, 20, out float height));
        Assert.Equal(31f, height);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(10, 20));
        Assert.True(Landscape.IsCleared(10, 20));
        Assert.Equal(1f, Landscape.NormalAt(10, 20).y);
        Assert.Equal(32f, Landscape.WaterLevel);
        Assert.True(Landscape.IsUnderwater(-50, 0));
        Assert.False(Landscape.IsUnderwater(10, 20));

        // Beyond the loaded part: nothing, whatever the game would answer there.
        Assert.False(Landscape.IsLoaded(500, 0));
        Assert.True(float.IsNaN(Landscape.HeightAt(500, 0)));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(500, 0));
        Assert.Equal(0f, Landscape.NormalAt(500, 0).y);
        Assert.False(Landscape.IsUnderwater(500, 0));
    }

    [Fact]
    public void AnotherThreadReadsNothingButTheWaterLevelSeenLast()
    {
        Dispatcher.Tick(); // this thread is the main one now
        Assert.Equal(32f, Landscape.WaterLevel);
        float height = 0, water = 0;
        bool available = true;
        var other = new Thread(() =>
        {
            height = Landscape.HeightAt(10, 20);
            water = Landscape.WaterLevel;
            available = Landscape.IsAvailable;
        });
        other.Start();
        other.Join();
        Assert.True(float.IsNaN(height));
        Assert.Equal(32f, water);
        Assert.False(available);
    }

    [Fact]
    public void ChangesReachTheGameAsAsked()
    {
        var area = Square();
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(area, 2));
        Assert.Equal(LandscapeResult.Done, Landscape.Lower(LandscapeArea.Point(V(1, 0, 1), LandscapeBrush.Small), 0.5f));
        Assert.Equal(LandscapeResult.Done, Landscape.Level(area, 40, new LandscapeOptions { Kind = GroundKind.Path, UpdateNavigation = false }));
        Assert.Equal(LandscapeResult.Done, Landscape.Paint(area, GroundKind.Road));
        Assert.Equal(LandscapeResult.Done, Landscape.ClearVegetation(area));

        Assert.Equal([LandscapeOperation.Raise, LandscapeOperation.Lower, LandscapeOperation.Level, LandscapeOperation.Paint, LandscapeOperation.ClearVegetation], _game.Applied.Select(e => e.Operation));
        Assert.Equal([2f, 0.5f, 40f], _game.Applied.Take(3).Select(e => e.Amount));
        Assert.Equal(LandscapeBrush.Small, _game.Applied[1].Area.Brush);
        Assert.Equal(GroundKind.Path, _game.Applied[2].Options.Kind);
        Assert.False(_game.Applied[2].Options.UpdateNavigation);
        Assert.Equal(GroundKind.Road, _game.Applied[3].Kind);
        Assert.False(_game.Applied[3].Options.ClearVegetation); // a road painted beside the meadow does not mow it
        Assert.True(_game.Applied[0].Options.ClearVegetation); // a change of height clears it, as the game does
    }

    [Fact]
    public void BadAmountsAndAreasAreRefused()
    {
        var area = Square();
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Raise(area, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Raise(area, Landscape.MaximumStep + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Lower(area, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Level(area, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Level(area, Landscape.MaximumHeight + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Paint(area, (GroundKind)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Landscape.Check(area, (LandscapeOperation)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Rectangle(V(0, 0, 0), 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Rectangle(V(0, 0, 0), 4, LandscapeArea.MaximumSide + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Rectangle(V(0, 0, 0), 4, 4, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Rectangle(V(0, 0, 0), 4, 4, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Rectangle(V(float.NaN, 0, 0), 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandscapeArea.Point(V(0, 0, 0), (LandscapeBrush)42));
        Assert.Empty(_game.Applied);
    }

    [Fact]
    public void AnAreaNotAllLoadedIsNotChanged()
    {
        Assert.Equal(LandscapeResult.NotLoaded, Landscape.Raise(Square(x: 98), 1)); // its far side and blend lie beyond 100
        Assert.Equal(LandscapeResult.NotLoaded, Landscape.Check(Square(x: 98), LandscapeOperation.Raise));
        Assert.Empty(_game.Applied);
    }

    [Fact]
    public void ABuildingInTheAreaBlocksItUnlessTheModSaysOtherwise()
    {
        _game.Buildings = true;
        Assert.Equal(LandscapeResult.Blocked, Landscape.Level(Square(), 30));
        Assert.Equal(LandscapeResult.Blocked, Landscape.Check(Square(), LandscapeOperation.Level));
        Assert.Empty(_game.Applied);
        Assert.Equal(LandscapeResult.Done, Landscape.Level(Square(), 30, new LandscapeOptions { KeepOffBuildings = false }));
        Assert.Single(_game.Applied);
    }

    [Fact]
    public void ADenOrAPlaceOfTheWorldInTheAreaBlocksItUnlessTheModSaysOtherwise()
    {
        _game.Landmarks = true;
        Assert.Equal(LandscapeResult.Blocked, Landscape.Lower(Square(), 2));
        Assert.Equal(LandscapeResult.Blocked, Landscape.Check(Square(), LandscapeOperation.Lower));
        Assert.Empty(_game.Applied);
        Assert.Equal(LandscapeResult.Done, Landscape.Lower(Square(), 2, new LandscapeOptions { KeepOffLandmarks = false }));
        Assert.Single(_game.Applied);
        Assert.NotNull(Landscape.Capture(Square())); // a snapshot is taken there all the same
    }

    [Fact]
    public void AChangeThatGoesRoundLeavesTheGroundUnderWhatStandsThere()
    {
        // A house over the square's west side, x from -2.5 to -0.5.
        _game.Buildings = true;
        _game.Footprints.Add(new GroundFootprint("House_L1(Clone)", -1.5f, 10, 1, 3, 1, 0));
        var around = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.GoAround };

        Assert.Equal(LandscapeResult.Blocked, Landscape.Raise(Square(), 1)); // refused, as ever, without it
        Assert.Equal(LandscapeResult.Partial, Landscape.Check(Square(), LandscapeOperation.Raise, around));
        Assert.Equal(LandscapeResult.Partial, Landscape.Raise(Square(), 1, around));
        Assert.True(Landscape.TryGetHeight(-1.5f, 10, out float under));
        Assert.Equal(FakeTerrain.Ground(-1.5f), under, 3);          // the house's ground as it was
        Assert.True(Landscape.TryGetHeight(1, 10, out float beside));
        Assert.Equal(FakeTerrain.Ground(1) + 1, beside, 2);          // the rest raised
        Assert.Empty(_game.Applied);                                // written by the SDK, not the game's tool

        var obstacles = Landscape.ObstaclesIn(Square(), around);
        Assert.Equal(["House_L1(Clone)"], obstacles.Names);
        Assert.True(obstacles.Covers(-1.5f, 10));
        Assert.False(obstacles.Covers(1, 10));
    }

    [Fact]
    public void AChangeAllUnderWhatStandsThereIsRefusedAndNothingInTheWayIsDone()
    {
        var around = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.GoAround };
        Assert.Equal(LandscapeResult.Done, Landscape.Check(Square(), LandscapeOperation.Raise, around));
        Assert.True(Landscape.ObstaclesIn(Square(), around).IsEmpty);

        _game.Buildings = true;
        _game.Footprints.Add(new GroundFootprint("House_L1(Clone)", 0, 10, 10, 10, 1, 0));
        Assert.Equal(LandscapeResult.Blocked, Landscape.Raise(Square(), 1, around));
        Assert.True(Landscape.TryGetHeight(0, 10, out float h));
        Assert.Equal(FakeTerrain.Ground(0), h, 3);

        // A turned footprint covers its own rectangle, not the square round it.
        var turned = new GroundFootprint("Wall", 0, 0, 2, 0.5f, 0.7071f, 0.7071f);
        Assert.True(turned.Covers(1, 1));
        Assert.False(turned.Covers(1, -1));
    }

    [Fact]
    public void AChangeGoesRoundAnotherModsZone()
    {
        Landscape.ProtectFor(typeof(Landscape).Assembly, LandscapeArea.Circle(V(1, 0, 10), 0.8f, 0));
        var around = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.GoAround };
        Assert.Equal(LandscapeResult.Blocked, Landscape.Raise(Square(), 1));
        Assert.Equal(LandscapeResult.Partial, Landscape.Raise(Square(), 1, around));
        Assert.True(Landscape.TryGetHeight(1, 10, out float kept));
        Assert.Equal(FakeTerrain.Ground(1), kept, 3);
        Assert.True(Landscape.TryGetHeight(-1, 10, out float raised));
        Assert.Equal(FakeTerrain.Ground(-1) + 1, raised, 2);
    }

    [Fact]
    public void AFittingChangeLevelsTheGroundUnderABuildingToItsHeightWithAnEmbankmentRoundIt()
    {
        // The ground the game levels for a house over the square's west side, x from -2.5 to -0.5, at 29.5.
        _game.Buildings = true;
        _game.Footprints.Add(new GroundFootprint("House_L1(Clone)", -1.5f, 10, 1, 3, 1, 0) { Level = 29.5f });
        var fit = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.Fit };

        Assert.Equal(LandscapeResult.Partial, Landscape.Check(Square(), LandscapeOperation.Raise, fit));
        Assert.Equal(LandscapeResult.Partial, Landscape.Raise(Square(), 2, fit));
        Assert.True(Landscape.TryGetHeight(-1.5f, 10, out float under));
        Assert.Equal(29.5f, under, 3);                                // levelled to the house's height
        Assert.True(Landscape.TryGetHeight(0, 10, out float round));
        Assert.Equal(29.5f + 0.5f, round, 2);                         // half a metre out: half a metre up, 45°
        Assert.True(Landscape.TryGetHeight(1.5f, 10, out float beside));
        Assert.Equal(29.5f + 2, beside, 2);                           // 2 m out: no more than 2 m over it
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(-1.5f, 10));  // the house's ground keeps its look

        // Again: the embankment does not creep towards the house's height.
        Landscape.Raise(Square(), 2, fit);
        Assert.True(Landscape.TryGetHeight(0, 10, out float again));
        Assert.Equal(round, again, 2);

        var obstacles = Landscape.ObstaclesIn(Square(), fit);
        Assert.True(obstacles.Fits);
        Assert.Equal(29.5f, obstacles.HeightAfter(-1.5f, 10, 30, 40));
        Assert.Equal(29.5f + 3, obstacles.HeightAfter(2.5f, 10, 30, 40)); // 3 m out
        Assert.Equal(31, obstacles.HeightAfter(2.5f, 10, 30, 31));
        Assert.True(obstacles.Covers(10, 10));                        // within the embankment's reach
    }

    [Fact]
    public void AFittingChangeUnderABuildingTheGameOnlyFillsForLeavesItsGroundHigher()
    {
        // A well whose ground the game only fills up to 29.5: its ground at 30 and over is left so.
        _game.Buildings = true;
        _game.Footprints.Add(new GroundFootprint("WaterWell_L1(Clone)", -1.5f, 10, 1, 3, 1, 0) { Level = 29.5f, LevelsDown = false });
        var fit = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.Fit };

        Assert.Equal(LandscapeResult.Partial, Landscape.Lower(Square(), 1, fit));
        Assert.True(Landscape.TryGetHeight(-1.5f, 10, out float dug));
        Assert.Equal(29.5f, dug, 3);                                  // dug down to its foot, no further
        Assert.Equal(LandscapeResult.Partial, Landscape.Raise(Square(), 2, fit));
        Assert.True(Landscape.TryGetHeight(-1.5f, 10, out float raised));
        Assert.Equal(29.5f, raised, 3);                               // not buried by a raise either
    }

    [Fact]
    public void AFittingChangeMovesTheGroundUnderRocksOnlyUpToTheirUnderside()
    {
        // Rocks over x from 0 to 2 of the square whose underside is 0.2 m over the ground there; under the rest of their box,
        // nothing of them.
        _game.Landmarks = true;
        _game.Footprints.Add(new GroundFootprint("WolfDen", 0.5f, 10, 1.5f, 1, 1, 0)
        {
            Underside = (x, z) => x >= 0 ? FakeTerrain.Ground(x) + 0.2f : null,
        });
        var fit = new LandscapeOptions { ObstacleMode = LandscapeObstacleMode.Fit };

        Assert.Equal(LandscapeResult.Partial, Landscape.Raise(Square(), 1, fit));
        Assert.True(Landscape.TryGetHeight(1, 10, out float under));
        Assert.Equal(FakeTerrain.Ground(1) + 0.2f, under, 2);         // up to the rocks, not into them
        Assert.True(Landscape.TryGetHeight(-0.5f, 10, out float free));
        Assert.Equal(FakeTerrain.Ground(-0.5f) + 1, free, 2);          // in their box, but nothing over it

        Assert.Equal(LandscapeResult.Partial, Landscape.Lower(Square(), 2, fit));
        Assert.True(Landscape.TryGetHeight(1, 10, out float dug));
        Assert.Equal(FakeTerrain.Ground(1) + 0.2f, dug, 2);           // not dug out from under them: they would hang
    }

    [Fact]
    public void TheGamesAnswerIsPassedOnAndAFaultIsAFailure()
    {
        _game.Result = LandscapeResult.TooSteep;
        Assert.Equal(LandscapeResult.TooSteep, Landscape.Raise(Square(), 1));
        _game.Throw = true;
        Assert.Equal(LandscapeResult.Failed, Landscape.Raise(Square(), 1));
        _game.Throw = false;
        _game.Result = LandscapeResult.Done;
        Assert.Equal(LandscapeResult.Done, Landscape.Check(Square(), LandscapeOperation.Paint));
    }

    [Fact]
    public void AChangeFromAnotherThreadIsMadeOnTheNextFrameForItsMod()
    {
        Dispatcher.Tick(); // this thread is the main one now
        var changes = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) => changes.Add(e);
        LandscapeResult result = LandscapeResult.Failed;
        var other = new Thread(() => result = Landscape.Raise(Square(), 1));
        other.Start();
        other.Join();
        Assert.Equal(LandscapeResult.Queued, result);
        Assert.Empty(_game.Applied);
        Dispatcher.Tick();
        Assert.Single(_game.Applied);
        Assert.Equal("test.mod", Assert.Single(changes).ModId);
    }

    [Fact]
    public void ChangingTellsWhoAndWhereAndCanCancel()
    {
        var seen = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) =>
        {
            seen.Add(e);
            e.Cancel = e.Operation == LandscapeOperation.Lower;
        };
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(Square(), 1));
        Assert.Equal(LandscapeResult.Cancelled, Landscape.Lower(Square(), 1));
        Assert.Equal(2, seen.Count);
        Assert.All(seen, e => Assert.Equal(LandscapeSource.Mod, e.Source));
        Assert.All(seen, e => Assert.Equal("test.mod", e.ModId));
        Assert.Equal(-3f, seen[0].Area.m_XMin); // 4 m square at x 0, blend 1
        Assert.Equal(6f, seen[0].Area.m_Width);

        // The game's own changes are reported without a mod, and a cancel answers the game.
        Assert.True(Landscape.RaiseChanging(default, LandscapeOperation.Lower, LandscapeSource.Player));
        Assert.Null(seen[^1].ModId);
        Assert.Equal(LandscapeSource.Player, seen[^1].Source);
        Assert.False(Landscape.RaiseChanging(default, LandscapeOperation.Raise, LandscapeSource.Resident));
    }

    [Fact]
    public void ChangedReportsChangesHereAndFromTheNetwork()
    {
        Assert.False(Landscape.IsChangedWatched);
        var seen = new List<LandscapeChangedEventArgs>();
        Landscape.GroundChanged += (_, e) => seen.Add(e);
        Assert.True(Landscape.IsChangedWatched);
        Landscape.RaiseChanged(LandscapeArea.MakeRect(1, 2, 3, 4), LandscapeOperation.Level, LandscapeSource.Mod, "test.mod");
        Landscape.RaiseChanged(default, null, LandscapeSource.Remote, null);
        Assert.Equal(2, seen.Count);
        Assert.Equal(LandscapeOperation.Level, seen[0].Operation);
        Assert.Equal("test.mod", seen[0].ModId);
        Assert.False(seen[0].IsRemote);
        Assert.Equal(3f, seen[0].Area.m_Width);
        Assert.True(seen[1].IsRemote);
        Assert.Null(seen[1].Operation);
    }

    [Fact]
    public void AnAreasBoundsFollowItsTurnAndBlend()
    {
        var area = LandscapeArea.Rectangle(V(10, 0, 20), 8, 2, rotation: 90, blend: 1);
        var bounds = area.Bounds;
        Assert.Equal(10 - 1 - 1, bounds.m_XMin, 3); // turned a quarter: 2 m across x, 8 along z
        Assert.Equal(4f, bounds.m_Width, 3);
        Assert.Equal(20 - 4 - 1, bounds.m_YMin, 3);
        Assert.Equal(10f, bounds.m_Height, 3);
        var brush = LandscapeArea.Point(V(0, 5, 0), LandscapeBrush.Small);
        Assert.Equal(2.5f, brush.Width);
        Assert.Equal(0f, brush.Blend);
        Assert.Equal(brush, LandscapeArea.Point(V(0, 5, 0), LandscapeBrush.Small));
        Assert.NotEqual(brush, LandscapeArea.Point(V(0, 5, 0), LandscapeBrush.Medium));
        Assert.Contains("Small brush", brush.ToString(), StringComparison.Ordinal);
    }

    // The console: the square in front of the player, turned as they face; the answers.

    [Fact]
    public void TheConsoleChangesASquareInFrontOfThePlayer()
    {
        _game.Forward = V(1, 0, 0);
        DevConsole.Execute("raise 1.5 6");
        var edit = Assert.Single(_game.Applied);
        Assert.Equal(LandscapeOperation.Raise, edit.Operation);
        Assert.Equal(1.5f, edit.Amount);
        Assert.Equal(6f, edit.Area.Width);
        Assert.Equal(TerrainCommands.AheadOf(6), edit.Area.Center.x, 3); // 6.5 m: clear of the player
        Assert.Equal(0f, edit.Area.Center.z, 3);
        Assert.Equal(90f, edit.Area.Rotation, 3);
        Assert.Contains(Lines(), l => l.StartsWith("Raised 6 × 6 m", StringComparison.Ordinal));

        DevConsole.Execute("level ~-2");
        Assert.Equal(28f, _game.Applied[^1].Amount); // the player's feet at 30
        DevConsole.Execute("paint ROAD");
        Assert.Equal(GroundKind.Road, _game.Applied[^1].Kind);
        DevConsole.Execute("clearveg 3");
        Assert.Equal(LandscapeOperation.ClearVegetation, _game.Applied[^1].Operation);
        DevConsole.Execute("lower");
        Assert.Equal(0.5f, _game.Applied[^1].Amount);
    }

    [Fact]
    public void TheConsoleTellsTheGroundAndWhyNothingChanged()
    {
        _game.Kinds[(10, 20)] = GroundKind.Path;
        DevConsole.Execute("ground 10 20");
        Assert.Contains(Lines(), l => l == "(10, 20): height 31 m, path, slope 0°, biome BirchForest (2).");
        DevConsole.Execute("ground 900 0");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("not in the loaded part", StringComparison.Ordinal));
        _game.Buildings = true;
        DevConsole.Execute("level 40");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text == "Not changed: a building stands there, or the game refuses the place.");
        DevConsole.Execute("paint lava");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("is no kind of ground", StringComparison.Ordinal));
        _game.IsAvailable = false;
        DevConsole.Execute("raise");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text == "No world loaded.");
    }

    [Theory]
    [InlineData("40", 30, 40)]
    [InlineData("~", 30, 30)]
    [InlineData("~1.5", 30, 31.5)]
    [InlineData("~-2", 30, 28)]
    public void HeightsAreTypedAsANumberOrFromTheFeet(string text, float feet, float expected) =>
        Assert.Equal(expected, TerrainCommands.ParseHeight(text, feet), 3);

    [Fact]
    public void AHeightThatIsNoNumberIsAUsageError() =>
        Assert.Throws<DevCommandException>(() => TerrainCommands.ParseHeight("~up", 30));

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}

/// <summary>
/// A world whose loaded part is the square from −100 to 100; the ground rises 0.1 m a metre along x from 30 at x 0; the
/// sea covers x below −40. Its tool raises <see cref="Landscape.GroundChanging"/> as the game's patch does for the SDK's own tool.
/// </summary>
internal sealed class FakeTerrain : ITerrainSource
{
    public bool IsAvailable { get; set; } = true;

    public Dictionary<(float, float), GroundKind> Kinds { get; } = [];

    public HashSet<(float, float)> Cleared { get; } = [];

    public bool Buildings { get; set; }

    public bool Landmarks { get; set; }

    public LandscapeResult Result { get; set; } = LandscapeResult.Done;

    public bool Throw { get; set; }

    public Vector3 Forward { get; set; } = LandscapeTests.V(0, 0, 1);

    public List<LandscapeEdit> Applied { get; } = [];

    public bool IsLoaded(float x, float z) => x >= -100 && x <= 100 && z >= -100 && z <= 100;

    public bool TryGetHeight(float x, float z, out float height)
    {
        height = Written.TryGetValue(Cell(x, z), out float h) ? h : Ground(x);
        return true;
    }

    /// <summary>The ground's own height before any write: rising 0.1 m a metre along x from 30.</summary>
    public static float Ground(float x) => 30 + x * 0.1f;

    // The written samples, by their half-metre cell: heights, kinds and cleared vegetation.
    public Dictionary<(int, int), float> Written { get; } = [];

    public List<(LandscapeSamples Samples, GroundWrite Write)> Writes { get; } = [];

    /// <summary>The world's natural objects: a change clearing the vegetation takes the removable ones.</summary>
    public List<FakeObject> Objects { get; } = [];

    /// <summary>How many times the model asked for the world's objects.</summary>
    public int ObjectLookups { get; private set; }

    /// <summary>The kinds the fake world places.</summary>
    public List<PlaceableKindData> PlaceKinds { get; } =
    [
        new("tree_willow", NaturalObjectKind.Tree, "Mjolnir_World_tree_willow", 1f, 1.6f, true, false),
        new("bush_sloe_berries", NaturalObjectKind.Object, "Mjolnir_World_bush_sloe_berries", 0.8f, 1.2f, false, true),
        new("rock_shoreline_big", NaturalObjectKind.LargeObject, "Mjolnir_World_rock_shoreline_big", 1f, 1f, false, false),
    ];

    public IReadOnlyList<PlaceableKindData> PlaceableKinds => PlaceKinds;

    /// <summary>The placements asked for: kind, place, turn and scale.</summary>
    public List<(string Name, Vector3 Position, float Rotation, float Scale)> Placed { get; } = [];

    public IWorldObject? Place(string name, Vector3 position, float rotation, float scale)
    {
        Placed.Add((name, position, rotation, scale));
        var kind = PlaceKinds.Find(k => k.Name == name);
        var placed = new FakeObject { AssetName = kind.AssetName, Kind = kind.Kind, Position = position, IsHarvestable = kind.IsHarvestable };
        Objects.Add(placed);
        return placed;
    }

    public IReadOnlyList<IWorldObject> ObjectsIn(float centerX, float centerZ, float radius)
    {
        ObjectLookups++;
        if (Throw) throw new InvalidOperationException("the game's objects could not be read");
        var found = new List<IWorldObject>();
        foreach (var o in Objects)
        {
            if (o.IsGone) continue;
            float dx = o.Position.x - centerX, dz = o.Position.z - centerZ;
            if (dx * dx + dz * dz <= radius * radius) found.Add(o);
        }
        return found;
    }

    public static (int, int) Cell(float x, float z) => ((int)MathF.Round(x / Step), (int)MathF.Round(z / Step));

    private const float Step = 0.5f;

    public LandscapeSamples? Read(float centerX, float centerZ, float side)
    {
        int n = (int)MathF.Ceiling(side / Step) + 1;
        float half = (n - 1) * Step / 2;
        float originX = MathF.Round((centerX - half) / Step) * Step, originZ = MathF.Round((centerZ - half) / Step) * Step;
        var samples = new LandscapeSamples(centerX, centerZ, side, n, originX, originZ, Step);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                float x = samples.X(c), z = samples.Z(r);
                int i = samples.Index(r, c);
                TryGetHeight(x, z, out samples.Heights[i]);
                samples.Kinds[i] = KindOf(x, z);
                samples.Cleared[i] = ClearedAt(x, z);
                samples.Flags[i] = (int)samples.Kinds[i] | (samples.Cleared[i] ? 0x10 : 0);
                if (HasBiomes) samples.Biomes[i] = BiomeIdAt(x, z);
            }
        if (HasBiomes)
        {
            samples.BiomeStep = BiomeStep;
            samples.BiomeOriginX = samples.BiomeOriginZ = -128;
        }
        return samples;
    }

    // The biomes: a cell's middle every 4 m from the world's 0 (cell k round k × 4 m), each BirchForest (2) unless written or
    // given; the part of the world grows three of its own.

    public const float BiomeStep = 4f;

    public bool HasBiomes { get; set; } = true;

    public Dictionary<(int, int), int> BiomeCells { get; } = [];

    public static (int, int) BiomeCell(float x, float z) => ((int)MathF.Round(x / BiomeStep), (int)MathF.Round(z / BiomeStep));

    public int BiomeIdAt(float x, float z) => BiomeCells.TryGetValue(BiomeCell(x, z), out int id) ? id : 2;

    /// <summary>How many times data of one biome was written (the game's data carries one).</summary>
    public int BiomeWrites { get; private set; }

    public IReadOnlyList<LandscapeBiome> Biomes { get; set; } =
        [new LandscapeBiome(0, "Default"), new LandscapeBiome(2, "BirchForest"), new LandscapeBiome(1, "ResourceForest")];

    public bool TryGetBiome(float x, float z, out LandscapeBiome biome)
    {
        int id = BiomeIdAt(x, z);
        biome = Biomes.FirstOrDefault(b => b.Id == id) is { Name.Length: > 0 } named ? named : new LandscapeBiome(id, $"Biome{id}");
        return HasBiomes;
    }

    public List<FakeOutline> Outlines { get; } = [];

    /// <summary>The road plans of the fake world; with <see cref="NoRoads"/> the world takes no new one.</summary>
    public List<IRoadPlanView> Roads { get; } = [];

    public bool NoRoads { get; set; }

    IReadOnlyList<IRoadPlanView> ITerrainSource.RoadPlans => Roads;

    public IRoadPlanView? PlaceRoadPlan(IReadOnlyList<Vector3> points, GroundKind kind, float width)
    {
        if (NoRoads) return null;
        var plan = new FakeRoadPlan(this, points, kind);
        if (width > 0) plan.Width = width;
        Roads.Add(plan);
        return plan;
    }

    /// <summary>The levelling fields of the fake world; with <see cref="NoFields"/> the world takes no new one.</summary>
    public List<IFieldView> Fields { get; } = [];

    public bool NoFields { get; set; }

    IReadOnlyList<IFieldView> ITerrainSource.Fields => Fields;

    public IFieldView? PlaceField(float x, float z, int tilesX, int tilesZ, float tileSize)
    {
        if (NoFields) return null;
        var field = new FakeField(this, x, z, tilesX, tilesZ, tileSize);
        Fields.Add(field);
        return field;
    }

    public IOutlineView NewOutline()
    {
        var outline = new FakeOutline();
        Outlines.Add(outline);
        return outline;
    }

    public List<FakeGrid> Grids { get; } = [];

    public IGridView NewGrid(int columns, int rows)
    {
        var grid = new FakeGrid(columns, rows);
        Grids.Add(grid);
        return grid;
    }

    public LandscapeResult Write(LandscapeSamples samples, in GroundWrite write)
    {
        if (Throw) throw new InvalidOperationException("the game's write failed");
        Writes.Add((samples, write));
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                int i = samples.Index(r, c);
                if (!samples.Touched[i]) continue;
                float x = samples.X(c), z = samples.Z(r);
                Written[Cell(x, z)] = samples.Heights[i];
                var kind = write.Raw ? (GroundKind)(samples.Flags[i] & 0xF) : samples.Kinds[i];
                bool cleared = write.Raw ? (samples.Flags[i] & 0x10) != 0 : samples.Cleared[i];
                WrittenKinds[Cell(x, z)] = kind;
                WrittenCleared[Cell(x, z)] = cleared;
            }
        var biomes = new HashSet<int>();
        for (int i = 0; i < samples.BiomeChanged.Length; i++)
        {
            if (!samples.BiomeChanged[i]) continue;
            int r = i / samples.Size, c = i % samples.Size;
            BiomeCells[BiomeCell(samples.X(c), samples.Z(r))] = samples.Biomes[i];
            biomes.Add(samples.Biomes[i]);
        }
        BiomeWrites += biomes.Count;
        if (write.ObjectsAt is { } objectsAt && write.Objects == LandscapeObjects.Remove)
            foreach (var o in Objects)
                if (!o.IsGone && o.IsRemovable && objectsAt(o.Position.x, o.Position.z)) o.Remove();
        if (write.ObjectsAt is { } harvestAt && write.Objects == LandscapeObjects.Harvest)
            ObjectSweep.TakeAll(Objects.Where(o => harvestAt(o.Position.x, o.Position.z)).Cast<IWorldObject>().ToList(), write.Harvesting ?? HarvestOptions.Default);
        if (write.ObjectsAt is { } clearAt && write.Objects == LandscapeObjects.Clear)
            foreach (var o in Objects)
                if (!o.IsGone && o.Kind != NaturalObjectKind.Item && clearAt(o.Position.x, o.Position.z)) o.Clear();
        if (write.ObjectsAt is { } settleAt && write.Objects == LandscapeObjects.Settle)
            foreach (var found in ObjectsIn(samples.CenterX, samples.CenterZ, samples.Side))
            {
                var o = (FakeObject)found;
                if (o.IsGone || !settleAt(o.Position.x, o.Position.z)) continue;
                float height = samples.HeightAt(o.Position.x, o.Position.z);
                if (!float.IsFinite(height) || Math.Abs(height - o.Position.y) <= 0.001f) continue;
                var at = o.Position;
                at.y = height;
                o.MoveTo(at);
            }
        return Result;
    }

    public Dictionary<(int, int), GroundKind> WrittenKinds { get; } = [];

    public Dictionary<(int, int), bool> WrittenCleared { get; } = [];

    private GroundKind KindOf(float x, float z) =>
        WrittenKinds.TryGetValue(Cell(x, z), out var kind) ? kind : Kinds.TryGetValue((x, z), out var given) ? given : GroundKind.Natural;

    private bool ClearedAt(float x, float z) => WrittenCleared.TryGetValue(Cell(x, z), out bool cleared) ? cleared : Cleared.Contains((x, z));

    public Vector3 Normal(float x, float z) => LandscapeTests.V(0, 1, 0);

    public GroundKind Kind(float x, float z) => KindOf(x, z);

    public bool IsCleared(float x, float z) => ClearedAt(x, z);

    public float WaterLevel => 32f;

    public bool IsUnderwater(float x, float z) => x < -40;

    public string? BuildingIn(LandscapeArea area) => Buildings ? "House_L1(Clone)" : null;

    public string? LandmarkIn(LandscapeArea area) => Landmarks ? "WolfDenNetworkLogic(Clone)" : null;

    /// <summary>What stands on the ground, for a change that goes round it.</summary>
    public List<GroundFootprint> Footprints { get; } = [];

    public IReadOnlyList<GroundFootprint> ObstaclesIn(LandscapeArea area, bool buildings, bool landmarks, bool fit) =>
        buildings || landmarks ? Footprints : [];

    public bool TryGetPlayer(out Vector3 position, out Vector3 forward)
    {
        position = LandscapeTests.V(0, 30, 0);
        forward = Forward;
        return IsAvailable;
    }

    public LandscapeResult Apply(in LandscapeEdit edit)
    {
        if (Throw) throw new InvalidOperationException("the game's tool failed");
        if (Landscape.RaiseChanging(edit.Area.Bounds, edit.Operation, LandscapeSource.Mod)) return LandscapeResult.Blocked;
        Applied.Add(edit);
        return Result;
    }

    public LandscapeResult Check(in LandscapeEdit edit) => Throw ? throw new InvalidOperationException("the game's check failed") : Result;
}

/// <summary>An outline's lines as the model last drew them.</summary>
internal sealed class FakeOutline : IOutlineView
{
    public IReadOnlyList<Vector3>? Edge { get; private set; }

    public IReadOnlyList<Vector3>? Blend { get; private set; }

    public Color Color { get; private set; }

    public bool IsShown { get; private set; }

    public bool IsDestroyed { get; private set; }

    public int Draws { get; private set; }

    public float Width { get; private set; }

    public void Draw(IReadOnlyList<Vector3> edge, IReadOnlyList<Vector3>? blend, Color color, float width)
    {
        Edge = edge;
        Blend = blend;
        Color = color;
        Width = width;
        IsShown = true;
        Draws++;
    }

    public void Hide() => IsShown = false;

    public void Destroy()
    {
        IsShown = false;
        IsDestroyed = true;
    }
}

/// <summary>A grid's cells as the model last placed and painted them.</summary>
internal sealed class FakeGrid(int columns, int rows) : IGridView
{
    public int Columns { get; } = columns;

    public int Rows { get; } = rows;

    public Vector3 Center { get; private set; }

    public float Rotation { get; private set; }

    public float CellSize { get; private set; }

    public GridCellKind[]? Kinds { get; private set; }

    public float[]? Fractions { get; private set; }

    public (Color? Near, Color? Far) Colours { get; private set; }

    public bool IsShown { get; private set; }

    public bool IsDestroyed { get; private set; }

    public int Places { get; private set; }

    public int Paints { get; private set; }

    public void Place(Vector3 center, float rotation, float cellSize)
    {
        Center = center;
        Rotation = rotation;
        CellSize = cellSize;
        IsShown = true;
        Places++;
    }

    public void Paint(GridCellKind[] kinds, float[] fractions)
    {
        Kinds = kinds;
        Fractions = fractions;
        Paints++;
    }

    public void Colors(Color? near, Color? far) => Colours = (near, far);

    public void Hide() => IsShown = false;

    public void Destroy()
    {
        IsShown = false;
        IsDestroyed = true;
    }
}
