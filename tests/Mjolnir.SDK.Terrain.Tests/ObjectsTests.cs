using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// The world's natural objects on a fake game: found in an area and round a point, nearest first; what each tells; taken
/// away and put on the ground; what a change of the ground does with them (the game's way, kept, settled); the console's
/// objects.
/// </summary>
[Collection("Terrain")]
public sealed class ObjectsTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public ObjectsTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(ObjectsTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    private FakeObject Add(float x, float z, NaturalObjectKind kind = NaturalObjectKind.Plant, string name = "Harvest_Plant_Flax", bool removable = true)
    {
        var found = new FakeObject
        {
            AssetName = name,
            Kind = kind,
            Position = V(x, FakeTerrain.Ground(x) + 0.0f, z),
            IsRemovable = removable,
            Health = kind == NaturalObjectKind.Tree ? 140 : 0,
            Quantity = kind == NaturalObjectKind.Plant ? 3 : 0,
            ResourceName = kind == NaturalObjectKind.Tree ? "Item_Wood_birch1" : "",
            IsHarvestable = kind != NaturalObjectKind.Grass,
        };
        _game.Objects.Add(found);
        return found;
    }

    [Fact]
    public void TheObjectsOfAnAreaComeNearestFirstAndTellWhatTheyAre()
    {
        Add(0, 10, NaturalObjectKind.Tree, "Harvest_Wood_birch1", removable: false);
        Add(2, 10);
        Add(0, 30); // outside

        var found = NaturalObjects.In(LandscapeArea.Circle(V(0, 0, 10), 4, blend: 0));
        Assert.Equal(2, found.Count);
        var tree = found[0];
        Assert.Equal("Harvest_Wood_birch1", tree.AssetName);
        Assert.Equal(NaturalObjectKind.Tree, tree.Kind);
        Assert.Equal(140, tree.Health);
        Assert.Equal("Item_Wood_birch1", tree.ResourceName);
        Assert.False(tree.IsRemovable);
        Assert.True(tree.IsHarvestable);
        Assert.False(tree.IsGone);
        Assert.Equal(0f, tree.HeightOverGround, 3);
        Assert.Equal("Harvest_Wood_birch1 (Tree) at (0, 10)", tree.ToString());
        Assert.Equal(3, found[1].Quantity);

        // Round a point, and the nearest of all.
        Assert.Equal(3, NaturalObjects.Around(V(0, 0, 20), 12).Count);
        Assert.Equal("Harvest_Wood_birch1", NaturalObjects.Nearest(V(0, 0, 11))?.AssetName);
        Assert.Null(NaturalObjects.Nearest(V(50, 0, 50), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.Around(V(0, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.Around(V(0, 0, 0), NaturalObjects.MaximumRadius + 1));
    }

    [Fact]
    public void WithoutAWorldOrOffTheMainThreadNothingIsFound()
    {
        Add(0, 10);
        _game.IsAvailable = false;
        Assert.Empty(NaturalObjects.In(LandscapeArea.Circle(V(0, 0, 10), 4)));
        Assert.Null(NaturalObjects.Nearest(V(0, 0, 10)));
        _game.IsAvailable = true;

        Dispatcher.Tick(); // this thread is the main one now
        IReadOnlyList<NaturalObject>? elsewhere = null;
        var thread = new System.Threading.Thread(() => elsewhere = NaturalObjects.In(LandscapeArea.Circle(V(0, 0, 10), 4)));
        thread.Start();
        thread.Join();
        Assert.Empty(elsewhere!);

        // A source that throws is logged, not passed on.
        _game.Throw = true;
        Assert.Empty(NaturalObjects.In(LandscapeArea.Circle(V(0, 0, 10), 4)));
    }

    [Fact]
    public void AnObjectIsTakenAwayAndPutOnTheGround()
    {
        var bush = Add(1, 10);
        var tree = Add(0, 10, NaturalObjectKind.Tree, "Harvest_Wood_birch1", removable: false);
        var found = NaturalObjects.In(LandscapeArea.Circle(V(0, 0, 10), 4, blend: 0));
        var first = found[0];

        // Raised ground leaves it in the air; Settle puts it back on the ground.
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Circle(V(0, 0, 10), 3, blend: 0), 1,
            new LandscapeOptions { Objects = LandscapeObjects.Keep }));
        Assert.Equal(-1f, first.HeightOverGround, 2);
        Assert.True(first.Settle());
        Assert.Equal(0f, first.HeightOverGround, 2);
        Assert.True(first.Settle()); // already there: nothing moved
        Assert.Equal(1, tree.Moves);

        // Moved somewhere of its own.
        Assert.True(first.MoveTo(V(20, 45, 20)));
        Assert.Equal(45f, first.Position.y, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.MoveTo(V(float.NaN, 0, 0)));

        // A tree is not taken by a change of the ground, so a mod does not take it that way either.
        Assert.False(found[0].Remove());
        Assert.False(found[0].IsGone);

        // Taken out of the world.
        var second = NaturalObjects.Nearest(V(1, 0, 10), 2)!;
        Assert.True(second.Remove());
        Assert.True(second.IsGone);
        Assert.True(bush.IsGone);
        Assert.Empty(NaturalObjects.In(LandscapeArea.Circle(V(1, 0, 10), 1, blend: 0)));
        Assert.False(second.Settle()); // gone: nothing to put anywhere
    }

    [Fact]
    public void AnObjectIsGatheredByHand()
    {
        var bush = Add(0, 10);
        bush.GatherCount = 3;
        var found = NaturalObjects.Nearest(V(0, 0, 10), 2)!;
        Assert.Equal(3, found.GatherCount);
        Assert.Equal(1, found.Gather());
        Assert.Equal(2, found.GatherCount);
        Assert.Equal(2, found.Gather(5)); // more than it has gives what is left
        Assert.Equal(0, found.GatherCount);
        Assert.Equal(0, found.Gather());
        Assert.Throws<ArgumentOutOfRangeException>(() => found.Gather(0));

        // A tree is felled with a tool instead: it gathers nothing.
        var tree = Add(2, 10, NaturalObjectKind.Tree, "Harvest_Wood_birch1", removable: false);
        var standing = NaturalObjects.Nearest(V(2, 0, 10), 1)!;
        Assert.Equal(0, standing.GatherCount);
        Assert.Equal(0, standing.Gather());
        Assert.False(tree.IsGone);

        // Gone or off the main thread, nothing is gathered.
        bush.GatherCount = 2;
        bush.Remove();
        Assert.Equal(0, found.Gather());
    }

    [Fact]
    public void AChangeOfTheGroundTakesKeepsOrSettlesTheObjects()
    {
        var area = LandscapeArea.Circle(V(0, 0, 10), 3, blend: 0);

        // The game's way: what a change of the ground takes is taken where the vegetation is cleared.
        var bush = Add(0, 10);
        var tree = Add(1, 10, NaturalObjectKind.Tree, "Harvest_Wood_birch1", removable: false);
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(area, 1));
        Assert.True(bush.IsGone);
        Assert.False(tree.IsGone);
        Assert.Equal(0, tree.Moves);
        Assert.Equal(-1f, tree.HeightOverGround(), 2);

        // Kept: nothing is taken, the ground rises under them.
        var kept = Add(0, 10);
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(area, 1, new LandscapeOptions { Objects = LandscapeObjects.Keep }));
        Assert.False(kept.IsGone);
        Assert.Equal(0, kept.Moves);

        // Settled: nothing is taken and everything stands on the new ground.
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(area, 1, new LandscapeOptions { Objects = LandscapeObjects.Settle }));
        Assert.False(kept.IsGone);
        Assert.Equal(1, kept.Moves);
        Assert.Equal(0f, kept.HeightOverGround(), 2);
        Assert.Equal(0f, tree.HeightOverGround(), 2);
    }

    [Fact]
    public void ObjectsOfAChangeAreNotAskedForWhereNothingIsDoneWithThem()
    {
        var area = LandscapeArea.Circle(V(0, 0, 10), 3, blend: 0);
        Add(0, 10);

        // A change that clears nothing and keeps the objects never looks for them.
        Assert.Equal(LandscapeResult.Done, Landscape.Paint(area, GroundKind.Road));
        Assert.Equal(0, _game.ObjectLookups);

        // A change through the game's tool keeps its own way with them (the game takes them itself).
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1));
        Assert.Single(_game.Applied);
        Assert.Equal(0, _game.ObjectLookups);

        // Settling them is the SDK's own write, whatever the area.
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1, new LandscapeOptions { Objects = LandscapeObjects.Settle }));
        Assert.Single(_game.Applied);
        Assert.True(_game.ObjectLookups > 0);
    }

    [Fact]
    public void TheConsoleListsWhatTheWorldGrewRoundThePlayer()
    {
        Add(0, 2, NaturalObjectKind.Tree, "Harvest_Wood_birch1", removable: false);
        Add(1, 3);
        Add(0, 40);

        DevConsole.Execute("objects 6");
        Assert.Contains(Lines(), l => l == "2 within 6 m: 1 tree, 1 plant.");
        Assert.Contains(Lines(), l => l.StartsWith("  Harvest_Wood_birch1 (tree) at (0, 2)", StringComparison.Ordinal) && l.Contains("gives Item_Wood_birch1", StringComparison.Ordinal));
        Assert.Contains(Lines(), l => l.Contains("a change of the ground takes it", StringComparison.Ordinal));

        DevConsole.Execute("objects 6 birch");
        Assert.Contains(Lines(), l => l == "1 within 6 m: 1 tree.");
        DevConsole.Execute("objects 6 stone");
        Assert.Contains(Lines(), l => l == "Nothing like 'stone' within 6 m.");
        DevConsole.Execute("objects 500");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("A radius is over 0", StringComparison.Ordinal));
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}

/// <summary>One object of a fake world: it stands where it was put until a mod moves or takes it.</summary>
internal sealed class FakeObject : IWorldObject
{
    public string AssetName { get; set; } = "";

    public NaturalObjectKind Kind { get; set; }

    public Vector3 Position { get; set; }

    public float Health { get; set; }

    public int Quantity { get; set; }

    public string ResourceName { get; set; } = "";

    public bool IsRemovable { get; set; }

    public bool IsHarvestable { get; set; }

    public bool IsGone { get; private set; }

    /// <summary>How many times it was moved.</summary>
    public int Moves { get; private set; }

    /// <summary>How many items are left to gather by hand.</summary>
    public int GatherCount { get; set; }

    public int Gather(int charges)
    {
        if (IsGone || GatherCount <= 0) return 0;
        int taken = Math.Min(charges, GatherCount);
        GatherCount -= taken;
        return taken;
    }

    public bool Remove()
    {
        if (IsGone) return true;
        if (!IsRemovable) return false; // as the game keeps a tree at the level of a change of the ground
        IsGone = true;
        return true;
    }

    public bool MoveTo(Vector3 position)
    {
        if (IsGone) return false;
        Position = position;
        Moves++;
        return true;
    }

    /// <summary>How far over the ground of the fake world it stands.</summary>
    public float HeightOverGround()
    {
        var at = Position;
        return at.y - (Landscape.TryGetHeight(at.x, at.z, out float ground) ? ground : at.y);
    }
}
