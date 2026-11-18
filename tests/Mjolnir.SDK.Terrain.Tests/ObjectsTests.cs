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
    public void ATreeAndABoulderAreClearedThoughAChangeOfTheGroundKeepsThem()
    {
        var tree = Add(0, 10, NaturalObjectKind.Tree, "tree_willow", removable: false);
        var boulder = Add(3, 10, NaturalObjectKind.LargeObject, "rock_shoreline_big", removable: false);
        var kept = Add(6, 10, NaturalObjectKind.LargeObject, "quest_stone", removable: false);
        kept.IsClearable = false;
        var found = NaturalObjects.Around(V(3, 0, 10), 4);
        Assert.All(found.Take(2), o => Assert.False(o.Remove()));
        Assert.True(found.Single(o => o.AssetName == "tree_willow").Clear());
        Assert.True(found.Single(o => o.AssetName == "rock_shoreline_big").Clear());
        var quest = found.Single(o => o.AssetName == "quest_stone");
        Assert.False(quest.IsClearable);
        Assert.False(quest.Clear());
        Assert.True(tree.IsGone);
        Assert.True(boulder.IsGone);
        Assert.False(kept.IsGone);
    }

    [Fact]
    public void AChangeOfTheGroundClearsEveryNaturalObjectButTheItems()
    {
        var tree = Add(0, 10, NaturalObjectKind.Tree, "tree_birch1", removable: false);
        var bush = Add(1, 10);
        var sapling = Add(-1, 10, NaturalObjectKind.Item, "Item_Wood_TreeBirch1Sapling", removable: false);
        var outside = Add(9, 10, NaturalObjectKind.Tree, "tree_fir4", removable: false);
        Assert.Equal(LandscapeResult.Done, Landscape.Level(LandscapeArea.Rectangle(V(0, 0, 10), 6, 6, blend: 0), 31, new LandscapeOptions { Objects = LandscapeObjects.Clear }));
        Assert.Empty(_game.Applied); // written by the SDK, not the game's tool
        Assert.True(tree.IsGone);
        Assert.True(bush.IsGone);
        Assert.False(sapling.IsGone);
        Assert.False(outside.IsGone);
    }

    [Fact]
    public void TheConsoleClearsWhatGrowsRoundThePlayer()
    {
        var tree = Add(0, 2, NaturalObjectKind.Tree, "tree_birch1", removable: false);
        var dropped = Add(1, 1, NaturalObjectKind.Item, "Item_Wood_Sticks", removable: false);
        var far = Add(0, 20, NaturalObjectKind.Tree, "tree_birch1", removable: false);
        DevConsole.Execute("objects clear");
        Assert.Contains(DevConsole.Lines, l => l.Text == "Cleared 1 of 1 within 4 m.");
        Assert.True(tree.IsGone);
        Assert.False(dropped.IsGone);
        Assert.False(far.IsGone);
    }

    [Fact]
    public void ATreeIsHarvestedByNobodyUnlessAsked()
    {
        var tree = Add(0, 10, NaturalObjectKind.Tree, "tree_willow", removable: false);
        var far = Add(3, 10, NaturalObjectKind.Tree, "tree_fir4", removable: false);
        far.HarvestHere = false;
        var found = NaturalObjects.Around(V(0, 0, 10), 4);
        Assert.True(found.Single(o => o.AssetName == "tree_willow").Harvest());
        Assert.Same(HarvestOptions.Default, tree.Harvested);
        Assert.False(HarvestOptions.Default.AsPlayer);
        Assert.True(HarvestOptions.Default.ChopTrunk);
        Assert.True(tree.IsGone);
        var asPlayer = new HarvestOptions { AsPlayer = true, AllowInjuries = false };
        Assert.False(found.Single(o => o.AssetName == "tree_fir4").Harvest(asPlayer));
        Assert.Same(asPlayer, far.Harvested);
        Assert.False(far.IsGone);
    }

    [Fact]
    public void AHarvestOffTheMainThreadOrOfAGoneObjectDoesNothing()
    {
        var tree = Add(0, 10, NaturalObjectKind.Tree, "tree_willow", removable: false);
        var found = NaturalObjects.Nearest(V(0, 0, 10), 2)!;
        tree.IsGone = true;
        Assert.False(found.Harvest());
        Assert.Null(tree.Harvested);
        tree.IsGone = false;
        Dispatcher.Tick(); // this thread is the main one now
        bool other = true;
        var thread = new System.Threading.Thread(() => other = found.Harvest());
        thread.Start();
        thread.Join();
        Assert.False(other);
        Assert.Null(tree.Harvested);
    }

    [Fact]
    public void TheConsoleHarvestsWhatGrowsRoundThePlayer()
    {
        var tree = Add(0, 2, NaturalObjectKind.Tree, "tree_birch1", removable: false);
        var bush = Add(1, 1);
        bush.HarvestHere = false; // picked by hand, no harvest of its own
        DevConsole.Execute("objects harvest 4 player");
        Assert.Contains(DevConsole.Lines, l => l.Text == "Harvested 1 of 2 within 4 m as the player.");
        Assert.True(tree.Harvested!.AsPlayer);
        Assert.True(tree.IsGone);
        Assert.False(bush.IsGone);
    }

    [Fact]
    public void AChangeOfTheGroundHarvestsGathersAndClearsWhatItReaches()
    {
        var tree = Add(0, 10, NaturalObjectKind.Tree, "tree_fir3", removable: false);
        var bush = Add(1, 10, NaturalObjectKind.Object, "bush_flax_fibres");
        bush.HarvestHere = false;
        bush.GatherCount = 6;
        var boulder = Add(-1, 10, NaturalObjectKind.LargeObject, "rock_shoreline_big", removable: false);
        boulder.HarvestHere = false;
        var lying = Add(0, 11, NaturalObjectKind.Item, "Item_Wood_Sticks", removable: false);
        var outside = Add(9, 10, NaturalObjectKind.Tree, "tree_fir4", removable: false);
        var asPlayer = new HarvestOptions { AsPlayer = true };
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Circle(V(0, 0, 10), 3), 1, new LandscapeOptions { Objects = LandscapeObjects.Harvest, Harvesting = asPlayer }));
        Assert.Same(asPlayer, tree.Harvested);
        Assert.True(tree.IsGone);
        Assert.True(bush.Gathered > 0);
        Assert.Equal(0, bush.GatherCount);
        Assert.True(bush.IsGone); // cleared once it gave all it had
        Assert.True(boulder.IsGone); // gives nothing: cleared
        Assert.False(lying.IsGone);
        Assert.Null(lying.Harvested);
        Assert.False(outside.IsGone);
    }

    [Fact]
    public void ABushAtRestKeepsWhenItGivesAgainInItsMark()
    {
        foreach (float due in new[] { 0f, 0.004f, 142.37f, 9000.5f })
        {
            int mark = Regrowth.Encode(due);
            Assert.True(mark < 0);
            Assert.Equal(MathF.Round(due * 100f) / 100f, Regrowth.Decode(mark), 3);
        }
        Assert.True(Regrowth.Encode(-5f) < 0);
    }

    [Fact]
    public void APlacedTreeGrowsToASizeOfItsKindTheSameForItsPlace()
    {
        var at = V(-144.7f, 30f, 322.8f);
        float grown = Regrowth.GrownScale(1f, 1.6f, at);
        Assert.InRange(grown, 1f, 1.6f);
        Assert.Equal(grown, Regrowth.GrownScale(1f, 1.6f, V(-144.7f, 35f, 322.8f)));
        var sizes = Enumerable.Range(0, 20).Select(i => Regrowth.GrownScale(1f, 1.6f, V(-144.7f + i * 3f, 30f, 322.8f))).Distinct().Count();
        Assert.True(sizes > 10);
        Assert.Equal(1.2f, Regrowth.GrownScale(1.2f, 1.2f, at));
    }

    [Fact]
    public void TreesGrowOverDaysOrNotAtAll()
    {
        float before = NaturalObjects.TreeGrowthDays;
        try
        {
            Assert.Equal(32f, before);
            NaturalObjects.TreeGrowthDays = 0f;
            Assert.Equal(0f, NaturalObjects.TreeGrowthDays);
            Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.TreeGrowthDays = -1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.TreeGrowthDays = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.TreeGrowthDays = float.PositiveInfinity);
        }
        finally
        {
            NaturalObjects.TreeGrowthDays = before;
        }
    }

    [Fact]
    public void KindsOfTheWorldArePlacedOnTheGround()
    {
        Assert.Equal(3, NaturalObjects.Kinds.Count);
        var willow = NaturalObjects.FindKind("tree_willow")!;
        Assert.Equal(NaturalObjectKind.Tree, willow.Kind);
        Assert.Equal("Mjolnir_World_tree_willow", willow.AssetName);
        Assert.Equal(1.6f, willow.MaximumScale);
        Assert.True(willow.IsHarvestable);
        Assert.False(willow.IsGathered);
        Assert.Equal("tree_willow (Tree)", willow.ToString());
        Assert.Same(willow, NaturalObjects.FindKind("Mjolnir_World_tree_willow"));
        Assert.True(NaturalObjects.FindKind("BUSH_SLOE_BERRIES")!.IsGathered);
        Assert.Null(NaturalObjects.FindKind("no_such_kind"));

        var placed = NaturalObjects.Place("tree_willow", V(0, 99, 10), 30f, 0.6f)!;
        Assert.Equal("Mjolnir_World_tree_willow", placed.AssetName);
        Assert.Equal(NaturalObjectKind.Tree, placed.Kind);
        var (name, at, rotation, scale) = _game.Placed.Single();
        Assert.Equal("tree_willow", name);
        Assert.Equal(FakeTerrain.Ground(0), at.y, 3); // on the ground, whatever y was given
        Assert.Equal(30f, rotation);
        Assert.Equal(0.6f, scale);
        Assert.Same(placed.AssetName, NaturalObjects.Nearest(V(0, 0, 10), 1)?.AssetName);

        Assert.Throws<ArgumentException>(() => NaturalObjects.Place("no_such_kind", V(0, 0, 10)));
        Assert.Throws<ArgumentNullException>(() => NaturalObjects.Place(null!, V(0, 0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.Place("tree_willow", V(0, 0, 10), scale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.Place("tree_willow", V(0, 0, 10), scale: NaturalObjects.MaximumScale + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NaturalObjects.Place("tree_willow", V(float.NaN, 0, 10)));
        Assert.Null(NaturalObjects.Place("tree_willow", V(500, 0, 10))); // not loaded
        _game.IsAvailable = false;
        Assert.Null(NaturalObjects.Place("tree_willow", V(0, 0, 10)));
        Assert.Single(_game.Placed);
    }

    [Fact]
    public void TheConsoleListsAndPlacesKinds()
    {
        DevConsole.Execute("place");
        Assert.Contains(DevConsole.Lines, l => l.Text == "3 kinds of the world to place:");
        Assert.Contains(DevConsole.Lines, l => l.Text == "  Tree: tree_willow");
        DevConsole.Execute("place rock_shoreline_big 2");
        var (name, at, _, scale) = _game.Placed.Single();
        Assert.Equal("rock_shoreline_big", name);
        Assert.Equal(TerrainCommands.PlaceAhead, at.z, 3); // ahead of the player at (0, 0) facing z
        Assert.Equal(2f, scale);
        Assert.Contains(DevConsole.Lines, l => l.Text.StartsWith("Placed Mjolnir_World_rock_shoreline_big", StringComparison.Ordinal));
        DevConsole.Execute("place no_such_kind");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("No kind 'no_such_kind'", StringComparison.Ordinal));
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

    public bool IsGone { get; set; }

    /// <summary>How many times it was moved.</summary>
    public int Moves { get; private set; }

    /// <summary>How many items are left to gather by hand.</summary>
    public int GatherCount { get; set; }

    /// <summary>The charges taken by hand.</summary>
    public int Gathered { get; private set; }

    public int Gather(int charges)
    {
        if (IsGone || GatherCount <= 0) return 0;
        int taken = Math.Min(charges, GatherCount);
        GatherCount -= taken;
        Gathered += taken;
        return taken;
    }

    /// <summary>Whether it has a harvest of its own here (a tree near the player).</summary>
    public bool HarvestHere { get; set; } = true;

    /// <summary>The options of the last harvest asked for.</summary>
    public HarvestOptions? Harvested { get; private set; }

    public bool Harvest(HarvestOptions options)
    {
        Harvested = options;
        if (IsGone) return true;
        if (!HarvestHere) return false;
        IsGone = true;
        return true;
    }

    /// <summary>Whether the game's own level takes it (everything but a few things of the game).</summary>
    public bool IsClearable { get; set; } = true;

    public bool Clear()
    {
        if (IsGone) return true;
        if (!IsClearable) return false;
        IsGone = true;
        return true;
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
