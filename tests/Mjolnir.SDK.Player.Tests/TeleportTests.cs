using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Player.Tests;

/// <summary>
/// The local player's place and teleports on a fake character: the position (on other threads the one last seen), an
/// exact teleport, a teleport onto the ground where the world is there already and where it streams in first, waiting
/// in vain, a despawn or another teleport cutting a wait short; and the console's pos and tp, coordinates with ~.
/// </summary>
[Collection("Player")]
public sealed class TeleportTests : IDisposable
{
    private long _now;

    public TeleportTests()
    {
        Dispatcher.ResetForTests();
        LocalPlayer.ResetForTests();
        DevConsole.ResetForTests();
        PlayerCommands.Register();
        LocalPlayer.Clock = () => _now;
    }

    public void Dispose()
    {
        LocalPlayer.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void WithoutACharacterThePlaceIsZeroAndNothingMoves()
    {
        Assert.Equal((0f, 0f, 0f), LocalPlayer.PositionNow);
        Assert.False(LocalPlayer.TeleportTo(1, 2, 3));
        Assert.False(LocalPlayer.TeleportToGroundAsync(1, 2).Result);
    }

    [Fact]
    public void ThePlaceIsTheCharactersAndOtherThreadsSeeTheLastOne()
    {
        var player = Spawn();
        Dispatcher.Tick(); // this thread is now the main thread
        LocalPlayer.Tick();
        player.Position = (1, 2, 3);
        (float, float, float) seen = default;

        var worker = new Thread(() => seen = LocalPlayer.PositionNow);
        worker.Start();
        worker.Join();

        Assert.Equal((486.2f, 33.7f, -174.4f), seen);
        Assert.Equal((1f, 2f, 3f), LocalPlayer.PositionNow);
    }

    [Fact]
    public void AnExactTeleportGoesWhereItIsTold()
    {
        var player = Spawn();

        Assert.True(LocalPlayer.TeleportTo(10, 20, 30));

        Assert.Equal([(10f, 20f, 30f)], player.Teleports);
    }

    [Fact]
    public void ACharacterTheGameDestroyedGivesItsLastPlaceAndGoesNowhere()
    {
        var player = Spawn();
        Dispatcher.Tick(); // this thread is now the main thread
        LocalPlayer.Tick();
        player.Position = (1, 2, 3);
        player.IsAlive = false; // its world unloaded; the tick that despawns it has not run yet

        Assert.True(LocalPlayer.IsSpawned);
        Assert.Equal((486.2f, 33.7f, -174.4f), LocalPlayer.PositionNow);
        Assert.False(LocalPlayer.TeleportTo(10, 20, 30));
        Assert.False(LocalPlayer.TeleportToGroundAsync(10, 30).Result);
        Assert.Empty(player.Teleports);
    }

    [Fact]
    public void APlaceMustBeFinite() =>
        Assert.Throws<ArgumentException>(() => LocalPlayer.TeleportTo(float.NaN, 0, 0));

    [Fact]
    public void WhereTheGroundIsThereTheCharacterLandsAtOnce()
    {
        var player = Spawn();
        player.Ground = (_, _) => 40;

        var landed = LocalPlayer.TeleportToGroundAsync(100, 200);

        Assert.True(landed.Result);
        Assert.Equal([(100f, 40.1f, 200f)], player.Teleports);
    }

    [Fact]
    public void FarAwayTheCharacterWaitsThereForTheWorldAndThenLands()
    {
        var player = Spawn();
        var landed = LocalPlayer.TeleportToGroundAsync(800, -170);

        Assert.Equal([(800f, 33.7f, -170f)], player.Teleports); // there at its height: the world streams in
        LocalPlayer.Tick();
        Assert.False(landed.IsCompleted);

        player.Ground = (x, z) => x == 800 && z == -170 ? 29 : null;
        LocalPlayer.Tick();

        Assert.True(landed.Result);
        Assert.Equal((800f, 29.1f, -170f), player.Teleports[^1]);
    }

    [Fact]
    public void TheGroundCountsOnceTheCharacterIsThere()
    {
        var player = Spawn();
        player.MovesLater = true;
        var landed = LocalPlayer.TeleportToGroundAsync(800, -170);
        player.Ground = (_, _) => 29;

        LocalPlayer.Tick();
        Assert.False(landed.IsCompleted);

        player.Arrive();
        LocalPlayer.Tick();
        Assert.True(landed.Result);
        Assert.Equal((800f, 29.1f, -170f), player.Teleports[^1]);
    }

    [Fact]
    public void NoGroundWithinTenSecondsGivesUp()
    {
        var player = Spawn();
        var landed = LocalPlayer.TeleportToGroundAsync(800, -170);

        _now += 9 * Stopwatch.Frequency;
        LocalPlayer.Tick();
        Assert.False(landed.IsCompleted);
        _now += 2 * Stopwatch.Frequency;
        LocalPlayer.Tick();

        Assert.False(landed.Result);
        Assert.Single(player.Teleports);
    }

    [Fact]
    public void ADespawnOrAnotherTeleportCutsTheWaitShort()
    {
        var player = Spawn();
        var first = LocalPlayer.TeleportToGroundAsync(800, -170);
        LocalPlayer.TeleportTo(1, 2, 3);
        Assert.False(first.Result);

        var second = LocalPlayer.TeleportToGroundAsync(800, -170);
        LocalPlayer.Track(null);
        Assert.False(second.Result);
        Assert.Equal((0f, 0f, 0f), LocalPlayer.PositionNow);
    }

    [Theory]
    [InlineData("~", 10, 10)]
    [InlineData("~5", 10, 15)]
    [InlineData("~-2.5", 10, 7.5f)]
    [InlineData("300", 10, 300)]
    [InlineData("1,5", 10, 1.5f)]
    public void ACoordinateIsANumberOrADistanceFromThePlayer(string word, float own, float expected) =>
        Assert.Equal(expected, PlayerCommands.Coordinate(word, own));

    [Theory]
    [InlineData("x")]
    [InlineData("~x")]
    [InlineData("")]
    public void AnythingElseIsNotACoordinate(string word) =>
        Assert.Throws<DevCommandException>(() => PlayerCommands.Coordinate(word, 0));

    [Fact]
    public void TpMovesThePlayerExactlyOrOntoTheGround()
    {
        var player = Spawn();
        player.Ground = (_, _) => 50;

        DevConsole.Execute("tp 10 20 30");
        DevConsole.Execute("tp ~5 ~");

        Assert.Equal([(10f, 20f, 30f), (15f, 50.1f, 30f)], player.Teleports);
        Assert.Contains("Teleported to 10.0 20.0 30.0", Lines());
        Assert.Contains("On the ground at 15.0 50.1 30.0", Lines());
    }

    [Fact]
    public void TpWithTheWrongWordsShowsItsUsage()
    {
        Spawn();

        DevConsole.Execute("tp 1");

        Assert.Equal(["tp takes x z, or x y z.", "Usage: tp <x> [y] <z>"], Lines()[^2..]);
    }

    [Fact]
    public void PosAndTpNeedAPlayerInTheWorld()
    {
        DevConsole.Execute("pos");
        DevConsole.Execute("tp 1 2");

        Assert.Equal(2, Lines().Count(l => l == "No player in the world."));
    }

    [Fact]
    public void PosShowsWhereThePlayerStands()
    {
        Spawn();

        DevConsole.Execute("pos");

        Assert.Equal("Position 486.2 33.7 -174.4", Lines()[^1]);
    }

    private static FakePlayer Spawn()
    {
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        return player;
    }

    private static string[] Lines() => [.. DevConsole.Lines.Select(l => l.Text)];
}
