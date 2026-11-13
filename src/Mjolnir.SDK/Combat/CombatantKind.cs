namespace Mjolnir.SDK;

/// <summary>What takes damage in the world.</summary>
public enum CombatantKind
{
    /// <summary>A player's character.</summary>
    Player,

    /// <summary>A villager of the settlement.</summary>
    Resident,

    /// <summary>A tamed animal of the settlement (the game's pets: dogs, smolkrs…).</summary>
    Pet,

    /// <summary>An animal or an enemy of the world: wolves, deer, draugar, skeletons…</summary>
    Creature,

    /// <summary>A den creatures come out of, which can be destroyed.</summary>
    Den,
}
