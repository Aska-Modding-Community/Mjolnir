using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// The combat model (<see cref="Combatant"/>, <see cref="Combatants"/>) on a fake game: one object per combatant, what is
/// listed and near, damage with and without an attacker and its events, healing, kills and their killers, teams, a combatant
/// the game destroyed, and other threads. The game's side (the combat module's patches) is checked in game.
/// </summary>
[Collection("MainThread")]
public sealed class CombatTests : IDisposable
{
    private readonly FakeCombat _game = new();
    private readonly FakeCombatant _player, _villager, _wolf;

    public CombatTests()
    {
        Dispatcher.ResetForTests();
        Combatants.ResetForTests();
        Combatants.Game = _game;
        _player = _game.Add(1, CombatantKind.Player, team: 1, position: V(0, 0, 0));
        _villager = _game.Add(2, CombatantKind.Resident, team: 1, position: V(5, 0, 0));
        _wolf = _game.Add(3, CombatantKind.Creature, team: 4, position: V(0, 0, 20), canStrike: true);
        _game.LocalPlayer = 1;
    }

    public void Dispose()
    {
        Combatants.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void ACombatantHasOneObjectAndNothingAnEmptyOne()
    {
        Assert.Same(Combatant.Of(2), Combatant.Of(2));
        var none = Combatant.Of(99);
        Assert.False(none.IsAvailable);
        Assert.Equal(0, none.Health);
        Assert.Equal(0, none.Damage(10));
    }

    [Fact]
    public void WithoutTheCombatModuleThereIsNoOne()
    {
        Combatants.Game = null;
        Assert.Empty(Combatants.All);
        Assert.False(Combatant.Of(1).IsAvailable);
    }

    [Fact]
    public void ItReadsTheGame()
    {
        var wolf = Combatant.Of(3);
        Assert.Equal(CombatantKind.Creature, wolf.Kind);
        Assert.Equal("Wolf3", wolf.Name);
        Assert.Equal("wolf 3", wolf.DisplayName);
        Assert.Equal(4, wolf.Team);
        Assert.Equal(100, wolf.Health);
        Assert.Equal(100, wolf.MaxHealth);
        Assert.True(wolf.IsAlive);
        Assert.False(wolf.IsFriendly);
        Assert.True(Combatant.Of(2).IsFriendly);
    }

    [Fact]
    public void AllAndNearListTheWorld()
    {
        Assert.Equal([1, 2, 3], Combatants.All.Select(c => (int)c.Key));
        Assert.Equal([1, 2], Combatants.Near(V(0, 0, 0), 10).Select(c => (int)c.Key));
        Assert.Equal([2, 1], Combatants.Near(V(6, 0, 0), 10).Select(c => (int)c.Key)); // nearest first
    }

    [Fact]
    public void DamageWithoutAnAttackerTakesHealthAsItIsAndIsReported()
    {
        DamagingEventArgs? before = null;
        DamagedEventArgs? after = null;
        Combatants.Damaging += (_, e) => before = e;
        Combatants.Damaged += (_, e) => after = e;
        float taken = Combatant.Of(2).Damage(30);
        Assert.Equal(30, taken);
        Assert.Equal(70, _villager.Health);
        Assert.Equal(30, before!.Amount);
        Assert.Null(before.Attacker);
        Assert.Same(Combatant.Of(2), after!.Target);
        Assert.Equal(30, after.Amount);
        Assert.Equal(70, after.Health);
        Assert.False(after.IsFatal);
    }

    [Fact]
    public void AHandlerChangesOrCancelsTheDamage()
    {
        Combatants.Damaging += (_, e) => e.Amount *= 0.5f;
        Assert.Equal(15, Combatant.Of(2).Damage(30));
        Combatants.Damaging += (_, e) => e.Cancel = true;
        Assert.Equal(0, Combatant.Of(2).Damage(30));
        Assert.Equal(85, _villager.Health);
    }

    [Fact]
    public void DamageStopsAtNoHealthAndKills()
    {
        DamagedEventArgs? after = null;
        Combatants.Damaged += (_, e) => after = e;
        Assert.Equal(100, Combatant.Of(2).Damage(500));
        Assert.True(_villager.IsDead);
        Assert.False(Combatant.Of(2).IsAlive);
        Assert.True(after!.IsFatal);
        Assert.Equal(0, Combatant.Of(2).Damage(10)); // the dead take nothing
    }

    [Fact]
    public void DamageWithAnAttackerIsTheAttackersBlow()
    {
        float taken = Combatant.Of(1).Damage(40, Combatant.Of(3));
        Assert.Equal((nint)3, _player.StruckBy);
        Assert.Equal(20, taken); // what the fake's armour left
        Assert.Equal(80, _player.Health);
    }

    [Fact]
    public void AnAttackerThatCannotStrikeDealsNothing()
    {
        Assert.Equal(0, Combatant.Of(3).Damage(40, Combatant.Of(2)));
        Assert.Equal(0, _wolf.StruckBy);
        Assert.Equal(100, _wolf.Health);
    }

    [Fact]
    public void HealingStopsAtTheMaximum()
    {
        _villager.Health = 40;
        Assert.Equal(60, Combatant.Of(2).Heal(500));
        Assert.Equal(100, _villager.Health);
        _villager.IsDead = true;
        Assert.Equal(0, Combatant.Of(2).Heal(10));
    }

    [Fact]
    public void AKillIsReportedWithItsKiller()
    {
        DiedEventArgs? died = null;
        Combatants.Died += (_, e) => died = e;
        Combatant.Of(3).Kill(Combatant.Of(1));
        Assert.True(_wolf.IsDead);
        Combatants.RaiseDied(3); // as the module's patch on the game's death
        Assert.Same(Combatant.Of(3), died!.Target);
        Assert.Same(Combatant.Of(1), died.Killer);
    }

    [Fact]
    public void TheKillerIsTheLastToStrike()
    {
        DiedEventArgs? died = null;
        Combatants.Died += (_, e) => died = e;
        Combatants.RaiseDamaging(Combatant.Of(2), Combatant.Of(3), 10, DamageKinds.Melee, V(0, 0, 0), V(0, 0, 1));
        Combatants.RaiseDied(2);
        Assert.Same(Combatant.Of(3), died!.Killer);
        Combatants.RaiseDied(1); // no one struck it: cold, hunger
        Assert.Null(died.Killer);
    }

    [Fact]
    public void ADeathIsExpectedOnlyRightAfterABlowOrAKill()
    {
        Assert.False(Combatants.IsDeathExpected(3)); // a body of the save
        Combatants.RaiseDamaging(Combatant.Of(3), Combatant.Of(1), 10, DamageKinds.None, V(0, 0, 0), V(0, 0, 1));
        Assert.True(Combatants.IsDeathExpected(3));
        Combatant.Of(2).Kill();
        Assert.True(Combatants.IsDeathExpected(2));
    }

    [Fact]
    public void AGoneCombatantReadsNothingAndTakesNothing()
    {
        var wolf = Combatant.Of(3);
        _wolf.Exists = false;
        Assert.False(wolf.IsAvailable);
        Assert.Equal(0, wolf.Health);
        Assert.Equal(0, wolf.Damage(10));
        Assert.DoesNotContain(wolf, Combatants.All);
    }

    [Fact]
    public void BlowsThatKilledNoOneAreForgotten()
    {
        float now = 100;
        Combatants.Clock = () => now;
        Combatants.RaiseDamaging(Combatant.Of(2), Combatant.Of(3), 10, DamageKinds.Melee, V(0, 0, 0), V(0, 0, 1));
        Combatants.RaiseDamaging(Combatant.Of(1), Combatant.Of(3), 10, DamageKinds.Melee, V(0, 0, 0), V(0, 0, 1));
        Assert.Equal(2, Combatants.ExpectedDeathCount);
        now = 120; // long after: neither can name a killer any more
        Combatants.RaiseDamaging(Combatant.Of(3), Combatant.Of(1), 10, DamageKinds.Melee, V(0, 0, 0), V(0, 0, 1));
        Assert.Equal(1, Combatants.ExpectedDeathCount);
        Assert.False(Combatants.IsDeathExpected(2));
        Assert.True(Combatants.IsDeathExpected(3));
    }

    [Fact]
    public void ADamageThatIsNoNumberIsRefused()
    {
        Combatants.Damaging += (_, e) => e.Amount = float.NaN;
        Combatants.Damaging += (_, e) => e.Amount = float.PositiveInfinity;
        Assert.Equal(30, Combatant.Of(2).Damage(30)); // both handlers threw; the amount stayed
        Assert.Equal(70, _villager.Health);
        var args = new DamagingEventArgs(Combatant.Of(2), null, 5, DamageKinds.None, V(0, 0, 0), V(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => args.Amount = float.NegativeInfinity);
        Assert.Equal(5, args.Amount);
    }

    [Fact]
    public void ItReadsOnlyWhatIsAskedAndItsKindOnce()
    {
        var wolf = Combatant.Of(3);
        int kinds = _wolf.KindReads;
        _ = wolf.Kind;
        _ = wolf.Name;
        Assert.Equal(kinds, _wolf.KindReads);
        _wolf.Health = 42;
        int positions = _wolf.PositionReads;
        Assert.Equal(42, wolf.Health);
        Assert.Equal(positions, _wolf.PositionReads);
        _ = wolf.Position;
        Assert.Equal(positions + 1, _wolf.PositionReads);
    }

    [Fact]
    public void NegativeDamageOrHealingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Combatant.Of(2).Damage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Combatant.Of(2).Heal(float.NaN));
    }

    [Fact]
    public void OnAnotherThreadDamageWaitsForTheNextFrame()
    {
        Dispatcher.MarkMainThread(); // this thread is the game's main one, the task's another
        var villager = Combatant.Of(2);
        float answer = Task.Run(() => villager.Damage(25)).Result;
        Assert.Equal(0, answer);
        Assert.Equal(100, _villager.Health);
        Dispatcher.Tick();
        Assert.Equal(75, _villager.Health);
    }

    [Fact]
    public void AskedForOnAnotherThreadItFindsTheGameOnTheMainThread()
    {
        Dispatcher.MarkMainThread(); // this thread is the game's main one, the task's another
        var wolf = Task.Run(() => Combatant.Of(3)).Result;
        Assert.Same(Combatant.Of(3), wolf);
        Assert.Equal(CombatantKind.Creature, wolf.Kind);
    }

    // A vector by its fields: Vector3's constructor and statics run the game's runtime, which the tests do not have.
    private static Vector3 V(float x, float y, float z)
    {
        var v = default(Vector3);
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    private sealed class FakeCombat : ICombatSource
    {
        private readonly Dictionary<nint, FakeCombatant> _all = [];

        public FakeCombatant Add(nint key, CombatantKind kind, int team, Vector3 position, bool canStrike = false) =>
            _all[key] = new FakeCombatant(this) { Kind = kind, Team = team, Position = position, CanStrike = canStrike, Name = $"{(kind == CombatantKind.Creature ? "Wolf" : kind.ToString())}{key}", DisplayName = $"{(kind == CombatantKind.Creature ? "wolf" : kind.ToString().ToLowerInvariant())} {key}" };

        public ICombatantSource? Combatant(nint key) => _all.GetValueOrDefault(key);

        public IReadOnlyList<nint> All => _all.Where(c => c.Value.Exists).Select(c => c.Key).ToList();

        public nint LocalPlayer { get; set; }
    }

    private sealed class FakeCombatant(FakeCombat game) : ICombatantSource
    {
        private float _health = 100;
        private readonly CombatantKind _kind;
        private readonly Vector3 _position;

        public bool Exists { get; set; } = true;
        public int KindReads { get; private set; }
        public int PositionReads { get; private set; }

        public CombatantKind Kind
        {
            get
            {
                KindReads++;
                return _kind;
            }
            init => _kind = value;
        }
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public CombatFaction Faction => CombatFaction.Vikings;
        public int Team { get; init; }
        public bool IsDead { get; set; }
        public float MaxHealth => 100;
        public Vector3 Position
        {
            get
            {
                PositionReads++;
                return _position;
            }
            init => _position = value;
        }
        public bool CanStrike { get; init; }
        public nint StruckBy { get; private set; }

        public float Health
        {
            get => _health;
            set
            {
                _health = Math.Clamp(value, 0, MaxHealth);
                if (_health <= 0) IsDead = true;
            }
        }

        // A blow of the game: half of it gets past the fake's armour.
        public float Strike(float amount, nint attacker)
        {
            _ = game;
            StruckBy = attacker;
            float before = Health;
            Health -= amount / 2;
            return before - Health;
        }
    }
}
