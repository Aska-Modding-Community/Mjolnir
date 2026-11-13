using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Player.Tests;

/// <summary>
/// The local player on a fake character: spawning and despawning (the same character, wrapped anew, changes nothing),
/// a character the game destroyed, death, and the stats as binding sources (reading, writing, Changed with the old and
/// the new value, watching the game only while a mod listens, a stat the character gets later, other threads).
/// </summary>
[Collection("Player")]
public sealed class LocalPlayerTests : IDisposable
{
    public LocalPlayerTests()
    {
        Dispatcher.ResetForTests();
        LocalPlayer.ResetForTests();
        CharacterSheet.ResetForTests();
    }

    public void Dispose()
    {
        LocalPlayer.ResetForTests();
        CharacterSheet.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void WithoutACharacterNothingIsSpawnedAndEveryStatReadsZero()
    {
        Assert.False(LocalPlayer.IsSpawned);
        Assert.False(LocalPlayer.IsDead);
        Assert.Null(LocalPlayer.Name);
        Assert.Equal(0, LocalPlayer.CharacterId);
        foreach (var stat in Stats())
        {
            Assert.Equal(0f, stat.Value);
            Assert.Equal(0f, stat.Maximum);
            Assert.Equal(0f, stat.Fraction);
        }
    }

    [Fact]
    public void ACharacterSpawnsOnceWithLiveStats()
    {
        using var events = new PlayerEvents();
        var player = new FakePlayer();
        LocalPlayer.Track(player);

        Assert.Equal(["Spawned"], events.Log);
        Assert.True(LocalPlayer.IsSpawned);
        Assert.Equal("Ragnar", LocalPlayer.Name);
        Assert.Equal(player.Id, LocalPlayer.CharacterId);
        Assert.Equal(200f, LocalPlayer.Health.Value);
        Assert.Equal(215f, LocalPlayer.Stamina.Maximum);
        Assert.Equal(212f / 215f, LocalPlayer.Stamina.Fraction, 5);
        Assert.Equal(95.8f, LocalPlayer.Food.Value);

        // The facade wraps the game's character anew on every SetPlayer call: the same id is the same character.
        LocalPlayer.Track(player);
        LocalPlayer.Track(new FakePlayer(player.Id));
        Assert.Equal(["Spawned"], events.Log);
    }

    [Fact]
    public void SpawnedComesAfterTheStatsAreLive()
    {
        float seen = -1;
        EventHandler spawned = (_, _) => seen = LocalPlayer.Health.Value;
        LocalPlayer.Spawned += spawned;
        try
        {
            LocalPlayer.Track(new FakePlayer());
            Assert.Equal(200f, seen);
        }
        finally
        {
            LocalPlayer.Spawned -= spawned;
        }
    }

    [Fact]
    public void AnotherCharacterDespawnsTheOldOneFirst()
    {
        using var events = new PlayerEvents();
        var first = new FakePlayer();
        var second = new FakePlayer();
        second.Stats[StatKind.Health].Value = 150;
        LocalPlayer.Track(first);
        LocalPlayer.Track(second);

        Assert.Equal(["Spawned", "Despawned", "Spawned"], events.Log);
        Assert.Equal(second.Id, LocalPlayer.CharacterId);
        Assert.Equal(150f, LocalPlayer.Health.Value);
        Assert.Null(first.DeathWatcher); // the old character is no longer watched
    }

    [Fact]
    public void NoCharacterDespawnsAndTheStatsReadZeroAgain()
    {
        using var events = new PlayerEvents();
        LocalPlayer.Track(new FakePlayer());
        LocalPlayer.Track(null);
        LocalPlayer.Track(null);

        Assert.Equal(["Spawned", "Despawned"], events.Log);
        Assert.False(LocalPlayer.IsSpawned);
        Assert.Null(LocalPlayer.Name);
        Assert.Equal(0f, LocalPlayer.Health.Value);
        Assert.Equal(0f, LocalPlayer.Health.Maximum);
    }

    [Fact]
    public void ACharacterTheGameDestroyedDespawnsOnTheNextTick()
    {
        using var events = new PlayerEvents();
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        LocalPlayer.Tick();
        Assert.True(LocalPlayer.IsSpawned);

        player.IsAlive = false; // its world was unloaded without UnsetPlayer
        LocalPlayer.Tick();
        Assert.Equal(["Spawned", "Despawned"], events.Log);
        Assert.False(LocalPlayer.IsSpawned);
    }

    [Fact]
    public void DeathAndRevivalAreRaisedOnce()
    {
        using var events = new PlayerEvents();
        var player = new FakePlayer();
        LocalPlayer.Track(player);

        player.DeathWatcher!(true);
        Assert.True(LocalPlayer.IsDead);
        player.DeathWatcher!(true);
        player.DeathWatcher!(false);
        Assert.False(LocalPlayer.IsDead);
        Assert.Equal(["Spawned", "Died", "Revived"], events.Log);

        LocalPlayer.Track(null);
        Assert.Null(player.DeathWatcher);
    }

    [Fact]
    public void ACharacterThatSpawnsDeadIsDeadWithoutDied()
    {
        using var events = new PlayerEvents();
        LocalPlayer.Track(new FakePlayer { IsDead = true });
        Assert.True(LocalPlayer.IsDead);
        Assert.Equal(["Spawned"], events.Log);
    }

    [Fact]
    public void TheNameIsAskedAgainWhileTheGameHasNone()
    {
        var player = new FakePlayer { Name = null };
        LocalPlayer.Track(player);
        Assert.Null(LocalPlayer.Name);
        player.Name = "Ragnar";
        Assert.Equal("Ragnar", LocalPlayer.Name);
    }

    [Fact]
    public void ChangedReportsTheOldAndTheNewValue()
    {
        using var food = Changes(LocalPlayer.Food);
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        player.Stats[StatKind.Food].GameSets(95f);
        player.Stats[StatKind.Food].GameSets(95f);
        LocalPlayer.Track(null);

        // Spawning from 0, the game's change, despawning to 0; the game's second set changed nothing.
        Assert.Equal([(0f, 95.8f), (95.8f, 95f), (95f, 0f)], food.Seen.Select(e => (e.OldValue, e.NewValue)));
    }

    [Fact]
    public void TheGameIsWatchedOnlyWhileAModListens()
    {
        var player = new FakePlayer();
        var water = player.Stats[StatKind.Water];
        LocalPlayer.Track(player);
        Assert.Equal(0, water.Watchers);

        using (var first = Changes(LocalPlayer.Water))
        using (var second = Changes(LocalPlayer.Water))
        {
            Assert.Equal(1, water.Watchers);
            // A listener that came late hears nothing of what happened before it.
            Assert.Empty(first.Seen);
            water.GameSets(90f);
            Assert.Single(first.Seen);
            Assert.Single(second.Seen);
        }
        Assert.Equal(0, water.Watchers);
    }

    [Fact]
    public void ANewCharacterIsWatchedInsteadOfTheOldOne()
    {
        using var health = Changes(LocalPlayer.Health);
        var first = new FakePlayer();
        var second = new FakePlayer();
        LocalPlayer.Track(first);
        LocalPlayer.Track(second);

        Assert.Equal(0, first.Stats[StatKind.Health].Watchers);
        Assert.Equal(1, second.Stats[StatKind.Health].Watchers);
        first.Stats[StatKind.Health].GameSets(10f);
        Assert.DoesNotContain(health.Seen, e => e.NewValue == 10f);
    }

    [Fact]
    public void SettingAValueWritesTheGameAndRaisesChangedOnce()
    {
        using var stamina = Changes(LocalPlayer.Stamina);
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        LocalPlayer.Stamina.Value = 100f;

        Assert.Equal(100f, player.Stats[StatKind.Stamina].Value);
        Assert.Equal([(0f, 212f), (212f, 100f)], stamina.Seen.Select(e => (e.OldValue, e.NewValue)));
    }

    [Fact]
    public void TheGameKeepsAValueSetWithinItsLimits()
    {
        LocalPlayer.Track(new FakePlayer());
        LocalPlayer.Health.Value = 500f;
        Assert.Equal(200f, LocalPlayer.Health.Value);
    }

    [Fact]
    public void SettingWithoutACharacterIsIgnored()
    {
        LocalPlayer.Health.Value = 50f;
        Assert.Equal(0f, LocalPlayer.Health.Value);
    }

    [Fact]
    public void AStatTheCharacterGetsLaterIsFoundOnTheTick()
    {
        using var warmth = Changes(LocalPlayer.Warmth);
        var player = new FakePlayer();
        var stat = player.Stats[StatKind.Warmth];
        player.Stats.Remove(StatKind.Warmth);
        LocalPlayer.Track(player);
        Assert.Equal(0f, LocalPlayer.Warmth.Value);

        player.Stats[StatKind.Warmth] = stat;
        LocalPlayer.Tick();
        Assert.Equal(100f, LocalPlayer.Warmth.Value);
        Assert.Equal([(0f, 100f)], warmth.Seen.Select(e => (e.OldValue, e.NewValue)));
        Assert.Equal(1, stat.Watchers);
    }

    [Fact]
    public void ToStringShowsWhatWasSeenWithoutReadingTheGame()
    {
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        _ = LocalPlayer.Health.Value;
        player.Stats[StatKind.Health].GameSets(150f);
        int reads = player.Stats[StatKind.Health].Reads;

        Assert.Equal("Health 200 of 200", LocalPlayer.Health.ToString());
        Assert.Equal(reads, player.Stats[StatKind.Health].Reads);
    }

    [Fact]
    public void AnotherThreadReadsTheValueLastSeenOnTheMainThread()
    {
        Dispatcher.MarkMainThread();
        var player = new FakePlayer();
        var health = player.Stats[StatKind.Health];
        LocalPlayer.Track(player);
        Assert.Equal(200f, LocalPlayer.Health.Value);
        health.GameSets(150f); // nobody listens, so only a read on the main thread sees it

        Assert.Equal(200f, OnAnotherThread(() => LocalPlayer.Health.Value));
        Assert.Equal(150f, LocalPlayer.Health.Value);
    }

    [Fact]
    public void AnotherThreadsWriteIsDoneOnTheNextFrame()
    {
        Dispatcher.MarkMainThread();
        var player = new FakePlayer();
        LocalPlayer.Track(player);
        OnAnotherThread(() => LocalPlayer.Health.Value = 50f);
        Assert.Equal(200f, player.Stats[StatKind.Health].Value);

        Dispatcher.Tick();
        Assert.Equal(50f, player.Stats[StatKind.Health].Value);
    }

    private static T OnAnotherThread<T>(Func<T> read)
    {
        T result = default!;
        var worker = new Thread(() => result = read());
        worker.Start();
        worker.Join();
        return result;
    }

    private static PlayerStat[] Stats() =>
        [LocalPlayer.Health, LocalPlayer.Stamina, LocalPlayer.Food, LocalPlayer.Water, LocalPlayer.Warmth];

    private static Recorder<ValueChangedEventArgs<float>> Changes(PlayerStat stat) => new(h => stat.Changed += h, h => stat.Changed -= h);

    /// <summary>The four LocalPlayer events in the order they came.</summary>
    private sealed class PlayerEvents : IDisposable
    {
        private readonly EventHandler _spawned, _despawned, _died, _revived;

        public PlayerEvents()
        {
            _spawned = (_, _) => Log.Add("Spawned");
            _despawned = (_, _) => Log.Add("Despawned");
            _died = (_, _) => Log.Add("Died");
            _revived = (_, _) => Log.Add("Revived");
            LocalPlayer.Spawned += _spawned;
            LocalPlayer.Despawned += _despawned;
            LocalPlayer.Died += _died;
            LocalPlayer.Revived += _revived;
        }

        public List<string> Log { get; } = [];

        public void Dispose()
        {
            LocalPlayer.Spawned -= _spawned;
            LocalPlayer.Despawned -= _despawned;
            LocalPlayer.Died -= _died;
            LocalPlayer.Revived -= _revived;
        }
    }
}
