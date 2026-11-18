using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// The game's road plans on a fake game: laid through points and refused where the world or the plan will not take them,
/// listed and found nearest a point, built, paved at once (the plan then finished by the game) or kept off another mod's
/// zone, taken away; the console's road.
/// </summary>
[Collection("Terrain")]
public sealed class RoadsTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public RoadsTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(RoadsTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void APlanIsLaidThroughItsPointsOnTheGround()
    {
        var plan = RoadPlans.Place([V(0, 99, 10), V(10, 99, 10), V(20, 0, 14)]);
        Assert.NotNull(plan);
        Assert.Equal(3, plan.Points.Count);
        Assert.Equal(FakeTerrain.Ground(10), plan.Points[1].y, 3); // on the ground, whatever y was given
        Assert.Equal(GroundKind.Road, plan.Kind);
        Assert.Equal(11, plan.Sections); // 10 + 10.8 m in sections of 2
        Assert.Equal(0, plan.LevelledSections);
        Assert.False(plan.IsBuilt);
        Assert.False(plan.IsGone);
        Assert.StartsWith("road plan of 3 points from (0, 10), 0 of 11 sections done", plan.ToString(), StringComparison.Ordinal);
        Assert.Single(RoadPlans.All);
        Assert.Equal(GroundKind.Dirt, RoadPlans.Place([V(0, 0, 30), V(8, 0, 30)], GroundKind.Dirt)?.Kind);
        Assert.Equal(2, RoadPlans.All.Count);
        Assert.Equal(3, RoadPlans.Nearest(V(21, 0, 15), 4)?.Points.Count);
        Assert.Null(RoadPlans.Nearest(V(80, 0, 80), 4));
    }

    [Fact]
    public void APlanTheGameWillNotTakeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoadPlans.Place([V(0, 0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoadPlans.Place(Enumerable.Range(0, RoadPlans.MaximumPoints + 1).Select(i => V(i * 4, 0, 0))));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoadPlans.Place([V(0, 0, 0), V(0.5f, 0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoadPlans.Place([V(-90, 0, 0), V(90, 0, 0), V(90, 0, 60)])); // 240 m
        Assert.Throws<ArgumentOutOfRangeException>(() => RoadPlans.Place([V(0, 0, 0), V(8, 0, 0)], GroundKind.Path));
        Assert.Throws<ArgumentNullException>(() => RoadPlans.Place(null!));
        Assert.Null(RoadPlans.Place([V(90, 0, 0), V(110, 0, 0)])); // not all loaded
        _game.NoRoads = true;
        Assert.Null(RoadPlans.Place([V(0, 0, 0), V(8, 0, 0)]));
        _game.NoRoads = false;
        _game.IsAvailable = false;
        Assert.Null(RoadPlans.Place([V(0, 0, 0), V(8, 0, 0)]));
        Assert.Empty(RoadPlans.All);
    }

    [Fact]
    public void APlanIsBuiltPavedAndFinishedByTheGame()
    {
        var plan = RoadPlans.Place([V(0, 0, 10), V(16, 0, 10)])!;
        Assert.True(plan.Build());
        Assert.True(plan.IsBuilt);
        Assert.Equal(0, plan.LevelledSections);

        Assert.Equal(8, plan.LevelNow());
        Assert.True(plan.IsGone); // the game finished the road
        Assert.Empty(RoadPlans.All);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(5, 10));
        Assert.Equal(GroundKind.Road, Landscape.KindAt(15, 10));

        // Paving builds a plan that is not built yet; a levelling plan leaves dirt.
        var strip = RoadPlans.Place([V(0, 0, 30), V(8, 0, 30)], GroundKind.Dirt)!;
        Assert.Equal(4, strip.LevelNow());
        Assert.Equal(GroundKind.Dirt, Landscape.KindAt(3, 30));
    }

    [Fact]
    public void ASectionAnotherModsZoneKeepsIsLeftAsItIs()
    {
        var theirs = Landscape.ProtectFor(typeof(Landscape).Assembly, LandscapeArea.Rectangle(V(14, 0, 10), 8, 8, blend: 0));
        var plan = RoadPlans.Place([V(0, 0, 10), V(16, 0, 10)])!;
        int done = plan.LevelNow();
        Assert.InRange(done, 3, 5);
        Assert.False(plan.IsGone);
        Assert.Equal(GroundKind.Road, Landscape.KindAt(1, 10));
        Assert.Equal(GroundKind.Natural, Landscape.KindAt(15, 10));
        theirs.Dispose();
        Assert.Equal(8, plan.LevelNow());
        Assert.True(plan.IsGone);
    }

    [Fact]
    public void APlanIsTakenAway()
    {
        var plan = RoadPlans.Place([V(0, 0, 10), V(16, 0, 10)])!;
        Assert.True(plan.Remove());
        Assert.True(plan.IsGone);
        Assert.True(plan.Remove());
        Assert.False(plan.Build());
        Assert.Equal(0, plan.LevelNow());
        Assert.Empty(RoadPlans.All);
    }

    [Fact]
    public void TheConsoleLaysListsPavesAndTakesAwayAPlan()
    {
        DevConsole.Execute("road");
        Assert.Contains(Lines(), l => l == "No road plans in the loaded world.");

        // Ahead of the player (at 0, 0 facing z): from 3 m, 16 m long.
        DevConsole.Execute("road place");
        Assert.Contains(Lines(), l => l.StartsWith("Laid a road plan of 2 points from (0, 3), 0 of 8 sections done", StringComparison.Ordinal));
        DevConsole.Execute("road");
        Assert.Contains(Lines(), l => l == "1 road plan(s):");
        DevConsole.Execute("road build");
        Assert.Contains(Lines(), l => l.StartsWith("Built the road plan", StringComparison.Ordinal));
        DevConsole.Execute("road level");
        Assert.Contains(Lines(), l => l == "Paved 8 of 8 sections.");
        Assert.Empty(RoadPlans.All);

        DevConsole.Execute("road place 6 dirt");
        Assert.Equal(GroundKind.Dirt, RoadPlans.All.Single().Kind);
        DevConsole.Execute("road remove");
        Assert.Contains(Lines(), l => l == "The road plan is taken away.");

        DevConsole.Execute("road level");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("No road plan within 32 m", StringComparison.Ordinal));
        DevConsole.Execute("road dig");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("is nothing a road plan does", StringComparison.Ordinal));
        DevConsole.Execute("road place 500");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("A length is from", StringComparison.Ordinal));
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}

/// <summary>A road plan of a fake world: its sections lay the plan's kind of ground at their middles, in a straight line between the points.</summary>
internal sealed class FakeRoadPlan : IRoadPlanView
{
    private readonly FakeTerrain _game;
    private readonly bool[] _done;
    private readonly List<Vector3> _middles = [];

    public FakeRoadPlan(FakeTerrain game, IReadOnlyList<Vector3> points, GroundKind kind)
    {
        _game = game;
        Points = points.ToArray();
        Kind = kind;
        for (int p = 1; p < Points.Count; p++)
        {
            var a = Points[p - 1];
            var b = Points[p];
            float length = MathF.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            int count = (int)MathF.Ceiling(length / RoadPlans.SectionLength - 0.001f);
            for (int s = 0; s < count; s++)
            {
                float t = (s + 0.5f) / count;
                _middles.Add(LandscapeTests.V(a.x + (b.x - a.x) * t, 0, a.z + (b.z - a.z) * t));
            }
        }
        _done = new bool[_middles.Count];
    }

    public IReadOnlyList<Vector3> Points { get; }

    public GroundKind Kind { get; }

    public float Width => 1.75f;

    public int Sections => IsGone ? 0 : _done.Length;

    // The game counts every section done while the plan is not built.
    public int LevelledSections => IsGone ? 0 : IsBuilt ? _done.Count(d => d) : _done.Length;

    public bool IsBuilt { get; private set; }

    public bool IsGone { get; private set; }

    public bool Build()
    {
        if (IsGone) return false;
        IsBuilt = true;
        return true;
    }

    public int LevelNow(Func<Rect, bool>? skip)
    {
        if (IsGone || !IsBuilt) return 0;
        for (int i = 0; i < _done.Length; i++)
        {
            if (_done[i]) continue;
            var m = _middles[i];
            float reach = RoadPlans.SectionLength + Width;
            if (skip != null && skip(LandscapeArea.MakeRect(m.x - reach / 2, m.z - reach / 2, reach, reach))) continue;
            for (float dx = -1f; dx <= 1f; dx += 0.5f)
                _game.WrittenKinds[FakeTerrain.Cell(m.x + dx, m.z)] = Kind;
            _done[i] = true;
        }
        int done = _done.Count(d => d);
        if (done == _done.Length) Remove(); // the game finishes a road whose last section is done
        return done;
    }

    public bool Remove()
    {
        IsGone = true;
        _game.Roads.Remove(this);
        return true;
    }
}
