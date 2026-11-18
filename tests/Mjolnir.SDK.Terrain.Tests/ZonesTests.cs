using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using Xunit;
using static Mjolnir.SDK.Terrain.Tests.LandscapeTests;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// Protected zones on a fake game: the game's changes and other mods' refused where they reach a zone, the zone's own mod
/// let through, the refusal heard, the zone moved and ended; what reaching a zone means at its blend; the console's protect.
/// </summary>
[Collection("Terrain")]
public sealed class ZonesTests : IDisposable
{
    private readonly FakeTerrain _game = new();

    public ZonesTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        Landscape.ResetForTests();
        Landscape.Source = _game;
        DevConsole.OwnerOf = a => a == typeof(ZonesTests).Assembly ? "test.mod" : "mjolnir";
        TerrainCommands.Register();
    }

    public void Dispose()
    {
        Landscape.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    // A zone kept by another mod: the platform's own, as far as the test mod is concerned.
    private static LandscapeZone OthersZone(LandscapeArea area) => Landscape.ProtectFor(typeof(Landscape).Assembly, area);

    private static UnityEngine.Rect Rect(float x, float z, float width, float depth) => LandscapeArea.MakeRect(x, z, width, depth);

    [Fact]
    public void AZoneRefusesTheGamesChangesThatReachIt()
    {
        var zone = Landscape.Protect(LandscapeArea.Circle(V(0, 0, 10), 4, blend: 0));
        Assert.Equal("test.mod", zone.ModId);
        Assert.True(zone.IsActive);
        var refused = new List<(object? Sender, LandscapeChangingEventArgs Change)>();
        zone.Refused += (sender, e) => refused.Add((sender, e));
        var seen = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) => seen.Add(e);

        // The player's hoe in the zone: refused before any handler sees it.
        Assert.True(Landscape.RaiseChanging(Rect(-1, 9, 2, 2), LandscapeOperation.Lower, LandscapeSource.Player));
        var (sender, change) = Assert.Single(refused);
        Assert.Same(zone, sender);
        Assert.True(change.Cancel);
        Assert.Equal(LandscapeSource.Player, change.Source);
        Assert.Empty(seen);

        // A villager's and a building's too; beside the zone, nothing is refused and the handlers hear it.
        Assert.True(Landscape.RaiseChanging(Rect(2, 12, 4, 4), LandscapeOperation.Level, LandscapeSource.Resident));
        Assert.True(Landscape.RaiseChanging(Rect(-10, 0, 20, 20), LandscapeOperation.Level, LandscapeSource.Building));
        Assert.False(Landscape.RaiseChanging(Rect(10, 10, 4, 4), LandscapeOperation.Raise, LandscapeSource.Player));
        Assert.Equal(3, refused.Count);
        Assert.Single(seen);
    }

    [Fact]
    public void AZoneIsItsOwnShapeNotItsBlendsOuterHalf()
    {
        var zone = Landscape.Protect(LandscapeArea.Circle(V(0, 0, 0), 4, blend: 2));
        Assert.True(zone.Reaches(Rect(4.6f, -0.5f, 1, 1)));  // inside the blend's inner half
        Assert.False(zone.Reaches(Rect(5.2f, -0.5f, 1, 1))); // its outer half
        Assert.False(zone.Reaches(Rect(5.2f, 5.2f, 1, 1)));  // the corner of its box, outside the circle
        Assert.True(zone.Reaches(Rect(-20, -20, 40, 40)));   // a change all round it
        var thin = Landscape.Protect(LandscapeArea.Rectangle(V(0, 0, 30), 0.6f, 20, blend: 0));
        Assert.True(thin.Reaches(Rect(-5, 25, 10, 1)));      // a strip thinner than a metre is still found
    }

    [Fact]
    public void TheZonesOwnModChangesItAndOtherModsAreBlocked()
    {
        var mine = Landscape.Protect(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0));
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1));
        Assert.Single(_game.Applied);
        Assert.Equal(LandscapeResult.Done, Landscape.Check(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), LandscapeOperation.Raise));
        mine.Dispose();

        var theirs = OthersZone(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0));
        var refused = new List<LandscapeChangingEventArgs>();
        theirs.Refused += (_, e) => refused.Add(e);
        Assert.Equal(LandscapeResult.Blocked, Landscape.Check(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), LandscapeOperation.Raise));
        Assert.Equal(LandscapeResult.Blocked, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, 10), 4, 4), 1));
        Assert.Single(_game.Applied); // the game's tool never ran
        Assert.Equal(LandscapeResult.Blocked, Landscape.Smooth(LandscapeArea.Circle(V(0, 0, 10), 3), 1));
        Assert.Empty(_game.Writes); // nor the SDK's own write
        Assert.Equal(LandscapeResult.Blocked, Landscape.PaintBiome(LandscapeArea.Circle(V(0, 0, 10), 3), new LandscapeBiome(1, "ResourceForest")));
        Assert.Equal(3, refused.Count);
        Assert.All(refused, e => Assert.Equal("test.mod", e.ModId));
        Assert.All(refused, e => Assert.Equal(LandscapeSource.Mod, e.Source));

        // Beside it the mod changes the ground as before; reading is never refused.
        Assert.Equal(LandscapeResult.Done, Landscape.Raise(LandscapeArea.Rectangle(V(30, 0, 10), 4, 4), 1));
        Assert.True(Landscape.Measure(LandscapeArea.Circle(V(0, 0, 10), 3)).IsKnown);
        Assert.NotNull(Landscape.Capture(LandscapeArea.Circle(V(0, 0, 10), 3)));
    }

    [Fact]
    public void AZoneMovesAndEnds()
    {
        var zone = Landscape.Protect(LandscapeArea.Circle(V(0, 0, 10), 3, blend: 0));
        Assert.True(Landscape.RaiseChanging(Rect(-1, 9, 2, 2), LandscapeOperation.Raise, LandscapeSource.Player));
        zone.Area = LandscapeArea.Circle(V(40, 0, 10), 3, blend: 0);
        Assert.False(Landscape.RaiseChanging(Rect(-1, 9, 2, 2), LandscapeOperation.Raise, LandscapeSource.Player));
        Assert.True(Landscape.RaiseChanging(Rect(39, 9, 2, 2), LandscapeOperation.Raise, LandscapeSource.Player));
        Assert.Contains("kept by test.mod", zone.ToString(), StringComparison.Ordinal);

        zone.Dispose();
        Assert.False(zone.IsActive);
        Assert.False(Landscape.RaiseChanging(Rect(39, 9, 2, 2), LandscapeOperation.Raise, LandscapeSource.Player));
        zone.Dispose(); // twice is harmless
    }

    [Fact]
    public void TheConsoleProtectsListsAndEndsItsZones()
    {
        DevConsole.Execute("protect list");
        Assert.Contains(Lines(), l => l == "The console protects no ground.");

        // A square in front of the player (at 0, 0 facing z): the player's tool refused there.
        DevConsole.Execute("protect");
        Assert.Contains(Lines(), l => l.StartsWith("Protected 8 × 8 m", StringComparison.Ordinal));
        float ahead = TerrainCommands.AheadOf(TerrainCommands.ZoneSize);
        Assert.True(Landscape.RaiseChanging(Rect(-1, ahead - 1, 2, 2), LandscapeOperation.Lower, LandscapeSource.Player));

        // The test mod's change there is another's, so it is blocked; the console's own goes through.
        Assert.Equal(LandscapeResult.Blocked, Landscape.Raise(LandscapeArea.Rectangle(V(0, 0, ahead), 2, 2), 1));
        DevConsole.Execute("raise 1 2");
        Assert.Contains(Lines(), l => l.StartsWith("Raised 2 × 2 m", StringComparison.Ordinal));

        DevConsole.Execute("protect list");
        Assert.Contains(Lines(), l => l.StartsWith("  8 × 8 m", StringComparison.Ordinal));
        DevConsole.Execute("protect clear");
        Assert.Contains(Lines(), l => l == "Ended 1 zone(s).");
        Assert.False(Landscape.RaiseChanging(Rect(-1, ahead - 1, 2, 2), LandscapeOperation.Lower, LandscapeSource.Player));
    }

    [Fact]
    public void AFieldIsLevelledButWhereAnotherModsZoneKeepsItsGround()
    {
        // Another's zone over the field's west half (x below 0): those tiles are not tried, so the game marks none worked.
        var theirs = OthersZone(LandscapeArea.Rectangle(V(-2, 0, 10), 4, 8, blend: 0));
        int refused = 0;
        theirs.Refused += (_, _) => refused++;
        var field = LevellingFields.Place(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0))!;
        Assert.Equal(16, field.Tiles);
        Assert.Equal(8, field.LevelNow());
        Assert.Equal(0, refused);
        Assert.Equal(FakeTerrain.Ground(-3), Landscape.HeightAt(-3, 10), 3); // kept
        Assert.Equal(field.Height, Landscape.HeightAt(3, 10), 3);             // levelled

        // The mod's own zone lets its own levelling through.
        theirs.Dispose();
        var mine = Landscape.Protect(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0));
        Assert.Equal(16, field.LevelNow());
        Assert.True(mine.IsActive);
    }

    [Fact]
    public void AGamesToolAModSetsGoingIsThatModsChange()
    {
        var mine = Landscape.Protect(LandscapeArea.Rectangle(V(0, 0, 10), 8, 8, blend: 0));
        var seen = new List<LandscapeChangingEventArgs>();
        Landscape.GroundChanging += (_, e) => seen.Add(e);
        // A field's tool (a building's) set going by the mod's call: reported with the mod, let through its zone.
        Assert.False(Landscape.Editing("test.mod", () => Landscape.RaiseChanging(Rect(-1, 9, 2, 2), LandscapeOperation.Level, LandscapeSource.Building)));
        var change = Assert.Single(seen);
        Assert.Equal(LandscapeSource.Building, change.Source);
        Assert.Equal("test.mod", change.ModId);
        // The same tool of itself (a villager at work on the field) is refused.
        Assert.True(Landscape.RaiseChanging(Rect(-1, 9, 2, 2), LandscapeOperation.Level, LandscapeSource.Building));
        Assert.Null(Landscape.EditingMod);
    }

    [Fact]
    public void WithoutZonesOrHandlersNothingIsAsked()
    {
        Assert.False(Landscape.RaiseChanging(Rect(0, 0, 4, 4), LandscapeOperation.Raise, LandscapeSource.Player));
        Assert.Empty(LandscapeZone.Active);
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();
}
