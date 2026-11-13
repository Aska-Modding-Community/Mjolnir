using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK;

/// <summary>
/// What the combat model sees of the game (the combat module's Game/ folder; fakes in the unit tests), as
/// <see cref="IStatsSource"/> is for characters' stats. Without that module nothing is connected: there are no combatants,
/// and no damage is reported. Called on the main thread.
/// </summary>
internal interface ICombatSource
{
    /// <summary>The character or creature behind a key (its IL2CPP object), null when it is neither.</summary>
    ICombatantSource? Combatant(nint key);

    /// <summary>The keys of everything in the world that takes damage: the players, the villagers, the creatures.</summary>
    IReadOnlyList<nint> All { get; }

    /// <summary>The key of the local player's character, 0 without one.</summary>
    nint LocalPlayer { get; }
}

/// <summary>One character or creature of the game.</summary>
internal interface ICombatantSource
{
    /// <summary>False once the game destroyed it.</summary>
    bool Exists { get; }

    CombatantKind Kind { get; }

    /// <summary>What it is, as the game names its kind: a creature's sheet (<c>SmolkrTameDataSheet</c>), a character's prefab.</summary>
    string Name { get; }

    /// <summary>What the player sees: a villager's or the player's name, a creature's in the player's language.</summary>
    string DisplayName { get; }

    CombatFaction Faction { get; }

    int Team { get; }

    bool IsDead { get; }

    /// <summary>Its health; written as it is (no armour, no blow), the game's death at 0.</summary>
    float Health { get; set; }

    float MaxHealth { get; }

    Vector3 Position { get; }

    /// <summary>
    /// A blow through the game's own damage, from that attacker's fighting (its melee): armour, blocking, teams and the
    /// network as for a blow of the game. The health it took; 0 when the attacker has nothing to strike with.
    /// </summary>
    float Strike(float amount, nint attacker);

    /// <summary>Whether it can strike others (it has the game's fighting component a blow needs).</summary>
    bool CanStrike { get; }
}
