namespace Mjolnir.SDK;

/// <summary>The sides of the game's world, as it sorts its characters and creatures.</summary>
public enum CombatFaction
{
    /// <summary>No side.</summary>
    None = 0,

    /// <summary>Neutral.</summary>
    Neutral = 1,

    /// <summary>The players, their villagers and pets.</summary>
    Vikings = 2,

    /// <summary>Draugar, skeletons and the like.</summary>
    Undead = 4,

    /// <summary>Animals.</summary>
    Critters = 8,

    /// <summary>Buildings.</summary>
    Structures = 16,

    /// <summary>What the game leaves out of fights (dens and their parts).</summary>
    Ignore = 32,

    /// <summary>Dangers of the world.</summary>
    Danger = 64,
}
