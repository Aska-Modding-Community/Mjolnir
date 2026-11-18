namespace Mjolnir.SDK.Terrain;

/// <summary>How a change to the ground is made, beyond its area and amount. Each has the game's own behaviour by default.</summary>
public sealed class LandscapeOptions
{
    /// <summary>The options of a change made without any: what the game does.</summary>
    public static LandscapeOptions Default { get; } = new();

    /// <summary>
    /// Whether the grass, bushes and saplings in the area go. The game clears them on every change of height; a change
    /// of kind keeps them unless this says otherwise.
    /// </summary>
    public bool ClearVegetation { get; init; } = true;

    /// <summary>
    /// The kind the ground becomes where its height changes; null for the game's own (<see cref="GroundKind.Dirt"/>).
    /// <see cref="Landscape.Paint"/> takes its kind as an argument instead.
    /// </summary>
    public GroundKind? Kind { get; init; }

    /// <summary>
    /// Whether the ground keeps its kind (a meadow stays a meadow, a road a road) where its height changes, instead of
    /// becoming <see cref="Kind"/>. With <see cref="ClearVegetation"/> false the grass stays too: a gentle smoothing of a
    /// natural slope, a hill shaped in a meadow. False by default, as the game makes every changed ground dirt.
    /// </summary>
    public bool KeepKind { get; init; }

    /// <summary>
    /// Whether the change is refused (<see cref="LandscapeResult.Blocked"/>) when a building stands in the area. The game's
    /// own check does not look at buildings; the SDK does, unless this is false.
    /// </summary>
    public bool KeepOffBuildings { get; init; } = true;

    /// <summary>
    /// Whether the change is refused (<see cref="LandscapeResult.Blocked"/>) when a fixed place of the world stands in the
    /// area: a den, or the rocks of a place the world was made with. The game cannot move them with the ground, so ground
    /// dug or raised under them leaves them hanging or buried. True by default; a snapshot put back over the ground it took
    /// may say false, as the ground under them is the same.
    /// </summary>
    public bool KeepOffLandmarks { get; init; } = true;

    /// <summary>
    /// What becomes of the natural objects (bushes, saplings, small stones) standing where the ground changes:
    /// <see cref="LandscapeObjects.Remove"/>, the game's own way, takes those a change of the ground takes, wherever it
    /// clears the vegetation; <see cref="LandscapeObjects.Keep"/> leaves every one standing; <see cref="LandscapeObjects.Settle"/>
    /// puts them on the new ground instead (<see cref="NaturalObject.Settle"/>); <see cref="LandscapeObjects.Clear"/> takes
    /// every one, trees and boulders too (<see cref="NaturalObject.Clear"/>); <see cref="LandscapeObjects.Harvest"/> takes
    /// every one with its loot.
    /// </summary>
    public LandscapeObjects Objects { get; init; }

    /// <summary>
    /// With <see cref="LandscapeObjects.Harvest"/>, who strikes and what the game gives them (<see cref="HarvestOptions"/>);
    /// null for <see cref="HarvestOptions.Default"/>: nobody.
    /// </summary>
    public HarvestOptions? Harvesting { get; init; }

    /// <summary>Whether the villagers' and creatures' paths are worked out again for the changed ground, as the game does.</summary>
    public bool UpdateNavigation { get; init; } = true;

    /// <summary>
    /// The only kind of ground the change touches: with <see cref="GroundKind.Natural"/> a raise, a smoothing or a biome
    /// leaves the roads, paths and dug earth in the area as they are. Null (the default) for every kind. The kind is the
    /// ground's before the change; a change limited so is written by the SDK, whatever the area.
    /// </summary>
    public GroundKind? OnlyOn { get; init; }
}
