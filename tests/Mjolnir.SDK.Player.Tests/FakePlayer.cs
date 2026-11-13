using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Mjolnir.SDK.Player.Tests;

/// <summary>The local player's state is process-wide: the player test classes run one after another.</summary>
[CollectionDefinition("Player", DisableParallelization = true)]
public sealed class PlayerCollection;

/// <summary>
/// A stand-in for the game's character: its stats as they are at the start of a game, which its
/// sheet reads through <see cref="FakeSheets"/>, as the game's through the stats module.
/// </summary>
internal sealed class FakePlayer : IPlayerSource
{
    private static long s_next = 1000;

    public FakePlayer(nint? id = null)
    {
        Id = id ?? (nint)Interlocked.Increment(ref s_next);
        FakeSheets.Add(this);
    }

    public nint Id { get; }
    public bool IsAlive { get; set; } = true;
    public bool IsDead { get; set; }
    public string? Name { get; set; } = "Ragnar";
    public Action<bool>? DeathWatcher { get; private set; }

    public Dictionary<StatKind, FakeStat> Stats { get; } = new()
    {
        [StatKind.Health] = new(200, 200),
        [StatKind.Stamina] = new(212, 215),
        [StatKind.Food] = new(95.8f, 100),
        [StatKind.Water] = new(97.6f, 100),
        [StatKind.Warmth] = new(100, 100),
    };

    public IDisposable WatchDeath(Action<bool> deadChanged)
    {
        DeathWatcher = deadChanged;
        return new Undo(() => DeathWatcher = null);
    }

    /// <summary>Where the character stands; a teleport moves it at once, as the game's does by the next physics step.</summary>
    public (float X, float Y, float Z) Position { get; set; } = (486.2f, 33.7f, -174.4f);

    public List<(float X, float Y, float Z)> Teleports { get; } = [];

    /// <summary>The ground's height at a place; null while the world there has not streamed in.</summary>
    public Func<float, float, float?> Ground { get; set; } = (_, _) => null;

    /// <summary>A teleport waits for <see cref="Arrive"/>, as the game's waits for its next physics step.</summary>
    public bool MovesLater { get; set; }

    public void Teleport(float x, float y, float z)
    {
        Teleports.Add((x, y, z));
        if (!MovesLater) Position = (x, y, z);
    }

    public void Arrive() => Position = Teleports[^1];

    public float? GroundHeight(float x, float z) => Ground(x, z);
}

/// <summary>
/// The stats module's side for the fake characters: each <see cref="FakePlayer"/> by its id, its pools under made-up asset
/// names. Installed as the sheets' game by every new fake character.
/// </summary>
internal sealed class FakeSheets : IStatsSource
{
    private static readonly FakeSheets s_instance = new();
    private static readonly Dictionary<nint, FakePlayer> s_players = [];

    public static void Add(FakePlayer player)
    {
        lock (s_players) s_players.TryAdd(player.Id, player);
        CharacterSheet.Game = s_instance;
    }

    public static string NameOf(StatKind kind) => $"Pool_{PlayerStat.AttributeId(kind):X}";

    public ICharacterSource? Character(nint key)
    {
        lock (s_players) return s_players.TryGetValue(key, out var player) ? new SheetOf(player) : null;
    }

    public IReadOnlyList<EffectInfo> Effects => [];

    public EffectInfo? Effect(int id) => null;

    public string? AttributeName(int id) => $"Pool_{id:X}";

    public void WatchExperience(bool watch) { }

    private sealed class SheetOf(FakePlayer player) : ICharacterSource
    {
        public bool IsAlive => player.IsAlive;

        public ICharacterStatSource? Stat(string assetName)
        {
            foreach (var kind in Enum.GetValues<StatKind>())
            {
                if (NameOf(kind) == assetName) return player.Stats.GetValueOrDefault(kind);
            }
            return null;
        }

        public IReadOnlyList<ICharacterStatSource> Stats => [.. player.Stats.Values];

        public bool AddExperience(SkillKind skill, float amount) => false;

        public IReadOnlyList<ActiveEffectState> Effects => [];

        public bool ApplyEffect(EffectInfo effect, float? seconds) => false;

        public bool RemoveEffect(EffectInfo effect) => false;

        public IDisposable WatchEffects(Action<int, int, int> changed) => new Undo(() => { });
    }
}

/// <summary>A stand-in for a variable attribute: the game keeps it within 0 and its max, and tells its watchers.</summary>
internal sealed class FakeStat(float value, float max) : ICharacterStatSource
{
    private readonly List<Action> _watchers = [];
    private float _value = value;

    public int Reads { get; private set; }
    public int Watchers => _watchers.Count;

    public string AssetName => "";
    public string Name => "";
    public string Description => "";
    public bool IsPool => true;

    public float Value
    {
        get
        {
            Reads++;
            return _value;
        }
        set => GameSets(Math.Clamp(value, 0, Maximum));
    }

    public float BaseValue
    {
        get => _value;
        set => Value = value;
    }

    public float Minimum => 0;

    public float Maximum { get; set; } = max;

    public IStatModifierSource AddModifier(float amount, ModifierKind kind) => throw new InvalidOperationException("a pool has no modifiers");

    public IDisposable Watch(Action changed)
    {
        _watchers.Add(changed);
        return new Undo(() => _watchers.Remove(changed));
    }

    /// <summary>The game changes the value; like the game's OnChanged, the watchers hear of real changes only.</summary>
    public void GameSets(float value)
    {
        if (_value.Equals(value)) return;
        _value = value;
        foreach (var watcher in _watchers.ToArray()) watcher();
    }
}

internal sealed class Undo(Action undo) : IDisposable
{
    public void Dispose() => undo();
}

/// <summary>Records an event while alive and unsubscribes when disposed, so no handler outlives its test.</summary>
internal sealed class Recorder<T> : IDisposable
{
    private readonly Action _unsubscribe;

    public Recorder(Action<EventHandler<T>> subscribe, Action<EventHandler<T>> unsubscribe)
    {
        EventHandler<T> handler = (_, e) => Seen.Add(e);
        subscribe(handler);
        _unsubscribe = () => unsubscribe(handler);
    }

    public List<T> Seen { get; } = [];

    public void Dispose() => _unsubscribe();
}
