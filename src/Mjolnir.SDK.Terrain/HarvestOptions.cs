namespace Mjolnir.SDK.Terrain;

/// <summary>How <see cref="NaturalObject.Harvest"/> strikes: who does it, what the game gives them, and whether a felled tree's trunk is chopped too.</summary>
public sealed class HarvestOptions
{
    /// <summary>The options of a harvest made without any: nobody strikes, and a felled tree's trunk is chopped too.</summary>
    public static HarvestOptions Default { get; } = new();

    /// <summary>
    /// Whether the local player strikes: the game then gives the player the experience of the work, its chance of a work
    /// injury and the harvest in their deeds, as for a blow of their own. False (the default): nobody strikes, and the game
    /// gives none of these to anyone; the loot falls all the same.
    /// </summary>
    public bool AsPlayer { get; init; }

    /// <summary>With <see cref="AsPlayer"/>, whether the player gains the game's experience of the work. True by default.</summary>
    public bool AwardExperience { get; init; } = true;

    /// <summary>With <see cref="AsPlayer"/>, whether a blow may give the player the game's work injury. True by default.</summary>
    public bool AllowInjuries { get; init; } = true;

    /// <summary>
    /// Whether the trunk a felled tree leaves lying is chopped too, into the logs, sticks and bark the game drops for it.
    /// The game lets a trunk be struck only a moment after it falls, so it is chopped on a later frame. True by default.
    /// </summary>
    public bool ChopTrunk { get; init; } = true;
}
