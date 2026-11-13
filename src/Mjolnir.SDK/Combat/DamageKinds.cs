using System;

namespace Mjolnir.SDK;

/// <summary>What a blow is, as the game marks it.</summary>
[Flags]
public enum DamageKinds
{
    /// <summary>Nothing marked.</summary>
    None = 0,

    /// <summary>A heavy attack.</summary>
    Heavy = 1,

    /// <summary>A melee blow.</summary>
    Melee = 2,

    /// <summary>A critical hit.</summary>
    Critical = 4,

    /// <summary>Cannot be blocked.</summary>
    Unblockable = 8,

    /// <summary>Cannot be dodged.</summary>
    Undodgeable = 16,

    /// <summary>Was blocked.</summary>
    Blocked = 32,

    /// <summary>Was dodged.</summary>
    Dodged = 64,

    /// <summary>Staggered the target.</summary>
    Staggered = 128,

    /// <summary>Knocked the target down.</summary>
    KnockedDown = 256,
}
