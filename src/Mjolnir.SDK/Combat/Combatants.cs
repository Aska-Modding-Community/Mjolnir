using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK;

/// <summary>
/// Everything in the world that takes damage, and the damage dealt there: every blow of the game (a player's, a villager's,
/// a wolf's, an arrow, a trap) before and after it lands, and every death, whoever's. The game's side is the combat module's.
/// </summary>
/// <remarks>
/// The events are raised where the game applies the damage: on the host for its villagers and creatures (in single player
/// that is the player's machine); a joined client's own blows on creatures are applied by the host. Handlers run on the
/// main thread.
/// </remarks>
/// <example><code>
/// Combatants.Damaging += (_, e) =>
/// {
///     if (e.Target.Kind == CombatantKind.Resident) e.Amount *= 0.5f;   // villagers take half
///     if (e.Target == LocalPlayer.Combatant &amp;&amp; godMode) e.Cancel = true;
/// };
/// Combatants.Died += (_, e) => Log.Info($"{e.Target} died{(e.Killer != null ? $", killed by {e.Killer}" : "")}");
/// </code></example>
public static class Combatants
{
    private static ICombatSource? s_game;
    private static ModHandlers<EventHandler<DamagingEventArgs>> s_damaging = new("Combatants.Damaging");
    private static ModHandlers<EventHandler<DamagedEventArgs>> s_damaged = new("Combatants.Damaged");
    private static ModHandlers<EventHandler<DiedEventArgs>> s_died = new("Combatants.Died");
    private static readonly Dictionary<nint, (Combatant? Killer, float Time)> s_killers = [];
    private static readonly object s_lock = new();
    private static float s_nextSweep;
    private static int s_localTeam = 1;

    /// <summary>The game's side, set by the combat module's start.</summary>
    internal static ICombatSource? Game
    {
        get => Volatile.Read(ref s_game);
        set => Volatile.Write(ref s_game, value);
    }

    /// <summary>The local player's team, for <see cref="Combatant.IsFriendly"/>: the players' team, 1, without a player.</summary>
    internal static int LocalTeam
    {
        get
        {
            if (Dispatcher.IsMainThread && Game is { } game && game.LocalPlayer != 0 && Combatant.Of(game.LocalPlayer) is { IsAvailable: true } player)
                s_localTeam = player.Team;
            return s_localTeam;
        }
    }

    /// <summary>
    /// Everything in the world that takes damage now: the players, the villagers, the pets, the animals and enemies loaded
    /// around the players, the dens. Main thread: empty on another.
    /// </summary>
    public static IReadOnlyList<Combatant> All =>
        Dispatcher.IsMainThread && Game is { } game ? game.All.Select(Combatant.Of).Where(c => c.IsAvailable).ToList() : [];

    /// <summary>Those within a distance of a place, nearest first. Main thread: empty on another.</summary>
    public static IReadOnlyList<Combatant> Near(Vector3 position, float radius) =>
        All.Select(c => (Combatant: c, Distance: Distance(c.Position, position)))
           .Where(c => c.Distance <= radius).OrderBy(c => c.Distance).Select(c => c.Combatant).ToList();

    // By the fields: Vector3's own statics run the game's runtime, which unit tests do not have.
    private static float Distance(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>
    /// Before a blow lands on anyone (the game's or a mod's): a handler may change its <see cref="DamagingEventArgs.Amount"/>
    /// (before armour) or cancel it.
    /// </summary>
    public static event EventHandler<DamagingEventArgs>? Damaging
    {
        add => s_damaging.Add(value);
        remove => s_damaging.Remove(value);
    }

    /// <summary>After damage was taken: how much health it took and what is left.</summary>
    public static event EventHandler<DamagedEventArgs>? Damaged
    {
        add => s_damaged.Add(value);
        remove => s_damaged.Remove(value);
    }

    /// <summary>
    /// When anyone dies: a player or a villager by anything (a blow, a mod, the cold, hunger), a creature by a blow or a mod's
    /// kill (the bodies of a save, which the game marks dead again after a load, are not reported).
    /// </summary>
    public static event EventHandler<DiedEventArgs>? Died
    {
        add => s_died.Add(value);
        remove => s_died.Remove(value);
    }

    /// <summary>A blow is about to land: the damage to deal, null when a handler cancelled it.</summary>
    internal static float? RaiseDamaging(Combatant target, Combatant? attacker, float amount, DamageKinds kinds, Vector3 position, Vector3 direction)
    {
        ExpectDeath(target.Key, attacker);
        if (s_damaging.Count == 0) return amount;
        var args = new DamagingEventArgs(target, attacker, amount, kinds, position, direction);
        s_damaging.Raise(h => h(null, args));
        return args.Cancel ? null : Math.Max(0, args.Amount);
    }

    /// <summary>Damage was taken.</summary>
    internal static void RaiseDamaged(Combatant target, Combatant? attacker, float amount, float health, bool fatal, DamageKinds kinds)
    {
        if (s_damaged.Count == 0) return;
        var args = new DamagedEventArgs(target, attacker, amount, health, fatal, kinds);
        s_damaged.Raise(h => h(null, args));
    }

    /// <summary>It died: its killer is the last one that struck it within a few seconds.</summary>
    internal static void RaiseDied(nint key)
    {
        Combatant? killer = null;
        lock (s_lock)
        {
            if (s_killers.Remove(key, out var last) && Time() - last.Time < KillerWithin) killer = last.Killer;
        }
        if (s_died.Count == 0) return;
        var args = new DiedEventArgs(Combatant.Of(key), killer);
        s_died.Raise(h => h(null, args));
    }

    /// <summary>Who struck it last (or is about to kill it), for its death.</summary>
    internal static void ExpectDeath(nint key, Combatant? killer)
    {
        float now = Time();
        lock (s_lock)
        {
            s_killers[key] = (killer, now);
            // A blow that killed no one is forgotten once it can no longer name a killer.
            if (now < s_nextSweep) return;
            s_nextSweep = now + KillerWithin;
            foreach (var old in s_killers.Where(k => now - k.Value.Time >= KillerWithin).Select(k => k.Key).ToList()) s_killers.Remove(old);
        }
    }

    /// <summary>How many blows are remembered for a death to come, for unit tests.</summary>
    internal static int ExpectedDeathCount
    {
        get
        {
            lock (s_lock) return s_killers.Count;
        }
    }

    /// <summary>Whether a blow or a mod's kill came at it within a few seconds (a death then is one, not a body of the save).</summary>
    internal static bool IsDeathExpected(nint key)
    {
        lock (s_lock) return s_killers.TryGetValue(key, out var last) && Time() - last.Time < KillerWithin;
    }

    private const float KillerWithin = 5f; // seconds
    private static float Time() => Clock();

    /// <summary>Seconds of a steady clock; unit tests replace it.</summary>
    internal static Func<float> Clock = SteadyTime;
    private static float SteadyTime() => (float)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Forgets the game and every handler, between unit tests.</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref s_game, null);
        s_damaging = new("Combatants.Damaging");
        s_damaged = new("Combatants.Damaged");
        s_died = new("Combatants.Died");
        s_localTeam = 1;
        lock (s_lock)
        {
            s_killers.Clear();
            s_nextSweep = 0;
        }
        Clock = SteadyTime;
        Combatant.ResetForTests();
    }
}

/// <summary>A blow about to land (<see cref="Combatants.Damaging"/>).</summary>
public sealed class DamagingEventArgs(Combatant target, Combatant? attacker, float amount, DamageKinds kinds, Vector3 position, Vector3 direction) : CancelEventArgs
{
    private float _amount = amount;

    /// <summary>Who takes it.</summary>
    public Combatant Target { get; } = target;

    /// <summary>Who strikes, null for no one (a mod's damage, a trap of the world the game names no one for).</summary>
    public Combatant? Attacker { get; } = attacker;

    /// <summary>
    /// The damage before armour; a handler may change it (a value below 0 counts as 0). It must be a finite number: NaN or an
    /// infinity throws <see cref="ArgumentOutOfRangeException"/>, as the game's health would take it as it is.
    /// </summary>
    public float Amount
    {
        get => _amount;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "the damage is a finite number");
            _amount = value;
        }
    }

    /// <summary>What the blow is.</summary>
    public DamageKinds Kinds { get; } = kinds;

    /// <summary>Where it lands.</summary>
    public Vector3 Position { get; } = position;

    /// <summary>Which way it goes (zero for damage that is no blow).</summary>
    public Vector3 Direction { get; } = direction;
}

/// <summary>Damage taken (<see cref="Combatants.Damaged"/>).</summary>
public sealed class DamagedEventArgs(Combatant target, Combatant? attacker, float amount, float health, bool isFatal, DamageKinds kinds) : EventArgs
{
    /// <summary>Who took it.</summary>
    public Combatant Target { get; } = target;

    /// <summary>Who struck, null for no one.</summary>
    public Combatant? Attacker { get; } = attacker;

    /// <summary>The health it took (after armour; 0 for a blow blocked, dodged or of one's own team).</summary>
    public float Amount { get; } = amount;

    /// <summary>The health left.</summary>
    public float Health { get; } = health;

    /// <summary>Whether it killed.</summary>
    public bool IsFatal { get; } = isFatal;

    /// <summary>What the blow was, as it landed (blocked, critical…).</summary>
    public DamageKinds Kinds { get; } = kinds;
}

/// <summary>A death (<see cref="Combatants.Died"/>).</summary>
public sealed class DiedEventArgs(Combatant target, Combatant? killer) : EventArgs
{
    /// <summary>Who died.</summary>
    public Combatant Target { get; } = target;

    /// <summary>Who struck it last within a few seconds of its death, null for no one (hunger, cold, a mod's kill without one).</summary>
    public Combatant? Killer { get; } = killer;
}
