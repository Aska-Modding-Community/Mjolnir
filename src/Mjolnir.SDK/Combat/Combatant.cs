using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK;

/// <summary>
/// A character or creature that takes damage: the player (<c>LocalPlayer.Combatant</c>), a villager (<c>Resident.Combatant</c>),
/// a tamed animal, an animal or enemy of the world, a den (<see cref="Combatants.All"/>). One object per character or
/// creature; after a load take it again. Without it (gone, or no combat module) it reads 0 and takes no damage.
/// </summary>
/// <remarks>
/// <para>
/// Use it on the main thread (mod callbacks, events, a job's steps). On another thread it reads what was last seen on the main
/// thread, and <see cref="Damage"/>, <see cref="Heal"/> and <see cref="Kill"/> run on the next frame (and answer 0).
/// </para>
/// <para>
/// Damage with an attacker is a blow of the game's own, struck with the attacker's fighting: armour, blocking and teams count
/// as for any blow (the game's characters of one team do not hurt one another, so a villager's blow takes nothing from the
/// player), and the game sends it over the network. Damage without an attacker is taken from the health as it is, armour and
/// teams left out: a fall, a curse, a mod's trap. Both are reported by <see cref="Combatants.Damaging"/> and
/// <see cref="Combatants.Damaged"/>, and a death by <see cref="Combatants.Died"/>.
/// </para>
/// </remarks>
/// <example><code>
/// foreach (var wolf in Combatants.Near(LocalPlayer.Position, 30).Where(c => c.Kind == CombatantKind.Creature &amp;&amp; !c.IsFriendly))
///     wolf.Damage(25, LocalPlayer.Combatant);       // the player's blow, as the game strikes it
/// LocalPlayer.Combatant.Heal(50);
/// </code></example>
public sealed class Combatant
{
    private static readonly GameObjectWrappers<Combatant> s_known = new(combatant => combatant._source is { Exists: false });
    private static readonly Combatant s_none = new(0, null);

    private ICombatantSource? _source;
    private bool _resolved;
    private CombatantKind _kind = CombatantKind.Creature;
    private string _name = "", _displayName = "";
    private CombatFaction _faction;
    private int _team;
    private bool _available, _alive;
    private float _health, _maxHealth;
    private Vector3 _position;

    private Combatant(nint key, ICombatantSource? source)
    {
        Key = key;
        _source = source;
        _resolved = source != null || key == 0;
        if (source != null) Read(source);
    }

    /// <summary>The game's object (its IL2CPP pointer), 0 for none.</summary>
    internal nint Key { get; }

    /// <summary>
    /// The combatant of a character or creature, one object each; an empty one for none. Asked for on another thread, it finds
    /// its game object at its first use on the main thread.
    /// </summary>
    internal static Combatant Of(nint key)
    {
        if (key == 0 || Combatants.Game is not { } game) return s_none;
        return s_known.Get(key, () =>
        {
            if (!Dispatcher.IsMainThread) return new Combatant(key, null);
            return game.Combatant(key) is { } source ? new Combatant(key, source) : null;
        }) ?? s_none;
    }

    /// <summary>Forgets every combatant, between unit tests.</summary>
    internal static void ResetForTests()
    {
        s_known.Clear();
    }

    // The game's object on the main thread, null without it (then it reads as gone); what was read of it is kept for other
    // threads. Each property reads only its own value, and what never changes (its kind and name) once.
    private ICombatantSource? Source
    {
        get
        {
            if (!Dispatcher.IsMainThread) return null;
            if (!_resolved)
            {
                _resolved = true;
                _source = Combatants.Game?.Combatant(Key);
                if (_source is { Exists: true } found) Read(found);
            }
            if (_source is { Exists: true } source)
            {
                Volatile.Write(ref _available, true);
                return source;
            }
            Volatile.Write(ref _available, false);
            _alive = false;
            _health = 0;
            return null;
        }
    }

    // Everything at once, when it is first found.
    private void Read(ICombatantSource source)
    {
        _available = true;
        _kind = source.Kind;
        _name = source.Name;
        _displayName = source.DisplayName;
        _faction = source.Faction;
        _team = source.Team;
        ReadHealth(source);
        _position = source.Position;
    }

    private void ReadHealth(ICombatantSource source)
    {
        _alive = !source.IsDead;
        _health = source.Health;
        _maxHealth = source.MaxHealth;
    }

    /// <summary>Whether it is in the world (false once the game destroyed it, or without the combat module).</summary>
    public bool IsAvailable
    {
        get
        {
            _ = Source;
            return Volatile.Read(ref _available);
        }
    }

    /// <summary>What it is.</summary>
    public CombatantKind Kind
    {
        get
        {
            _ = Source;
            return _kind;
        }
    }

    /// <summary>
    /// What it is, as the game names its kind: a creature's sheet (<c>SmolkrTameDataSheet</c>, <c>DeerDataSheet</c>), a
    /// character's (<c>CharacterRagnar</c>, <c>CharacterVillagerFemale</c>).
    /// </summary>
    public string Name
    {
        get
        {
            _ = Source;
            return _name;
        }
    }

    /// <summary>What the player sees: the player's or a villager's name, a creature's kind in the player's language.</summary>
    public string DisplayName
    {
        get
        {
            if (Source is { } source) _displayName = source.DisplayName;
            return _displayName;
        }
    }

    /// <summary>Its side of the world.</summary>
    public CombatFaction Faction
    {
        get
        {
            if (Source is { } source) _faction = source.Faction;
            return _faction;
        }
    }

    /// <summary>Its team, as the game numbers them: the players, their villagers and pets are 1; one team does not hurt itself.</summary>
    public int Team
    {
        get
        {
            if (Source is { } source) _team = source.Team;
            return _team;
        }
    }

    /// <summary>Whether it is on the local player's team (the player, the villagers, the pets).</summary>
    public bool IsFriendly => Team == Combatants.LocalTeam;

    /// <summary>Whether it is in the world and not dead.</summary>
    public bool IsAlive
    {
        get
        {
            if (Source is { } source) _alive = !source.IsDead;
            return Volatile.Read(ref _available) && _alive;
        }
    }

    /// <summary>Its health; 0 when dead or gone.</summary>
    public float Health
    {
        get
        {
            if (Source is { } source) _health = source.Health;
            return _health;
        }
    }

    /// <summary>Its health at the most.</summary>
    public float MaxHealth
    {
        get
        {
            if (Source is { } source) _maxHealth = source.MaxHealth;
            return _maxHealth;
        }
    }

    /// <summary>Where it is.</summary>
    public Vector3 Position
    {
        get
        {
            if (Source is { } source) _position = source.Position;
            return _position;
        }
    }

    /// <summary>Its attributes, skills and effects: a character's (the player, a villager); a creature's are empty for now.</summary>
    public CharacterSheet Sheet => Kind is CombatantKind.Player or CombatantKind.Resident ? CharacterSheet.Of(Key) : CharacterSheet.Of(0);

    /// <summary>
    /// Takes health: with an attacker, the attacker's blow as the game strikes it (armour, blocking, teams; sent over the
    /// network); without one, from the health as it is. The health it took (what armour or a team left of the blow), 0 when
    /// it is dead or gone, the attacker cannot strike, or a handler of <see cref="Combatants.Damaging"/> cancelled it.
    /// </summary>
    /// <param name="amount">The damage, at least 0; before armour for a blow.</param>
    /// <param name="attacker">Who strikes it (the player, a villager, a creature that fights), null for no one.</param>
    public float Damage(float amount, Combatant? attacker = null)
    {
        if (amount < 0 || float.IsNaN(amount)) throw new ArgumentOutOfRangeException(nameof(amount), amount, "the damage is at least 0");
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Damage(amount, attacker));
            return 0;
        }
        if (Source is not { } source || source.IsDead) return 0;
        if (attacker != null && attacker != this)
        {
            if (attacker.Source is not { CanStrike: true }) return 0;
            float before = source.Health;
            source.Strike(amount, attacker.Key); // reported by the game's own damage (the module's patches)
            ReadHealth(source);
            return Math.Max(0, before - source.Health);
        }
        // No one's blow: the health as it is, reported here as the game's blows are.
        var damaging = Combatants.RaiseDamaging(this, null, amount, DamageKinds.None, source.Position, default); // no direction: no blow
        if (damaging == null) return 0;
        float was = source.Health;
        Combatants.ExpectDeath(Key, null);
        source.Health = Math.Max(0, was - damaging.Value);
        ReadHealth(source);
        float taken = Math.Max(0, was - source.Health);
        Combatants.RaiseDamaged(this, null, taken, source.Health, source.IsDead, DamageKinds.None);
        return taken;
    }

    /// <summary>Gives health back, up to <see cref="MaxHealth"/>; the health it gave, 0 when it is dead or gone.</summary>
    /// <param name="amount">The health to give, at least 0.</param>
    public float Heal(float amount)
    {
        if (amount < 0 || float.IsNaN(amount)) throw new ArgumentOutOfRangeException(nameof(amount), amount, "the health given is at least 0");
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Heal(amount));
            return 0;
        }
        if (Source is not { } source || source.IsDead) return 0;
        float was = source.Health;
        source.Health = Math.Min(source.MaxHealth, was + amount);
        ReadHealth(source);
        return Math.Max(0, source.Health - was);
    }

    /// <summary>
    /// Kills it as the game does at no health (a villager leaves its body and loot, a creature its carcass). Reported by
    /// <see cref="Combatants.Died"/> with that killer.
    /// </summary>
    /// <param name="killer">Who killed it, for <see cref="Combatants.Died"/>; null for no one.</param>
    public void Kill(Combatant? killer = null)
    {
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Kill(killer));
            return;
        }
        if (Source is not { } source || source.IsDead) return;
        Combatants.ExpectDeath(Key, killer);
        source.Health = 0;
        ReadHealth(source);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{(_displayName.Length > 0 ? _displayName : _name)} ({_kind})";
}
