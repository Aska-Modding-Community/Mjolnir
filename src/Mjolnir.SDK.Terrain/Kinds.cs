namespace Mjolnir.SDK.Terrain;

/// <summary>What the ground is at a point, as the game keeps it for every half metre of the world.</summary>
public enum GroundKind
{
    /// <summary>As the world was made: grass, moss, rock, sand, whatever its place grows.</summary>
    Natural,

    /// <summary>Bare earth: what the ground becomes where it was dug, raised or levelled.</summary>
    Dirt,

    /// <summary>Rock under the earth.</summary>
    Bedrock,

    /// <summary>A path, as the road maker lays it on dirt.</summary>
    Path,

    /// <summary>A road, as a road plan lays it.</summary>
    Road,
}

/// <summary>What a natural object of the world is, as the game sorts its vegetation.</summary>
public enum NaturalObjectKind
{
    /// <summary>Grass and the like, drawn in their thousands: no object of its own stands in the world.</summary>
    Grass,

    /// <summary>A plant: flax, a berry bush, a mushroom.</summary>
    Plant,

    /// <summary>A tree.</summary>
    Tree,

    /// <summary>A thing of the world: a small stone, a nest, a log.</summary>
    Object,

    /// <summary>A large thing: a boulder, a cliff stone, an ore deposit.</summary>
    LargeObject,

    /// <summary>An item lying in the world: one dropped by a player, or a sapling planted in the ground.</summary>
    Item,

    /// <summary>Anything else, or what the game does not say.</summary>
    Other,
}

/// <summary>
/// What a change does where its area reaches something it keeps off (<see cref="LandscapeOptions.KeepOffBuildings"/>,
/// <see cref="LandscapeOptions.KeepOffLandmarks"/>) or another mod's protected zone.
/// </summary>
public enum LandscapeObstacleMode
{
    /// <summary>The whole change is refused (<see cref="LandscapeResult.Blocked"/>).</summary>
    Refuse,

    /// <summary>
    /// The ground under what stands there stays as it is, the rest changes, and the change answers
    /// <see cref="LandscapeResult.Partial"/>.
    /// </summary>
    GoAround,

    /// <summary>
    /// The rest changes, and the ground under what stands there is fitted to it: under a building the game levels the ground
    /// for, at the height it levels it to, blending into the change round it as the game's levelling does; under a fixed
    /// place's rocks, only between where it was and their underside, so they neither hang nor sink deeper. What has neither
    /// (a fence, a wall, another mod's zone) is gone round. The change answers <see cref="LandscapeResult.Partial"/>.
    /// </summary>
    Fit,
}

/// <summary>What becomes of the natural objects standing where the ground changes.</summary>
public enum LandscapeObjects
{
    /// <summary>What the game does: the bushes, saplings and small stones a change of the ground takes are taken.</summary>
    Remove,

    /// <summary>Nothing is taken: the objects stay where they stood, now over or under the new ground.</summary>
    Keep,

    /// <summary>Nothing is taken: every object in the area is put on the new ground under it.</summary>
    Settle,

    /// <summary>
    /// Every natural object in the area is taken, trees and boulders too, as <see cref="NaturalObject.Clear"/> takes it
    /// (nothing is dropped); the items lying there stay.
    /// </summary>
    Clear,

    /// <summary>
    /// Every natural object in the area is taken with its loot, once the ground has changed: what the game harvests (a tree
    /// with its trunk, a stump, a stone clump) is harvested as <see cref="NaturalObject.Harvest"/> does, what is picked by
    /// hand gives all it has, and what gives nothing (a boulder of the scenery, grass) is cleared. The loot falls on the new
    /// ground. Who strikes is <see cref="LandscapeOptions.Harvesting"/>; the items lying there stay.
    /// </summary>
    Harvest,
}

/// <summary>A change to the ground.</summary>
public enum LandscapeOperation
{
    /// <summary>The ground goes up.</summary>
    Raise,

    /// <summary>The ground goes down (dug).</summary>
    Lower,

    /// <summary>The ground goes to one height.</summary>
    Level,

    /// <summary>The ground's kind changes; its height stays.</summary>
    Paint,

    /// <summary>The grass, bushes and saplings on it go; its height and kind stay.</summary>
    ClearVegetation,

    /// <summary>The ground's bumps and steps are evened out (<see cref="Landscape.Smooth"/>).</summary>
    Smooth,

    /// <summary>The ground becomes a ramp between two heights (<see cref="Landscape.Slope"/>).</summary>
    Slope,

    /// <summary>The ground takes the heights a mod's function gives (<see cref="Landscape.Shape"/>).</summary>
    Shape,

    /// <summary>The ground goes back to what a snapshot of it kept (<see cref="LandscapeSnapshot.Restore"/>).</summary>
    Restore,

    /// <summary>A snapshot's ground is put down somewhere (<see cref="LandscapeSnapshot.PasteAt"/>).</summary>
    Paste,

    /// <summary>The ground's biome changes, and with it what grows there (<see cref="Landscape.PaintBiome"/>).</summary>
    PaintBiome,

    /// <summary>The ground gets its own kind and its grass back (<see cref="Landscape.RestoreVegetation"/>).</summary>
    RestoreVegetation,
}

/// <summary>How a change fades out over an area's blend, from its full strength inside to nothing at the blend's outer edge.</summary>
public enum LandscapeFalloff
{
    /// <summary>Gently at both ends of the blend, steepest in its middle: no crease where the change meets the ground.</summary>
    Smooth,

    /// <summary>Evenly over the blend: a straight bank.</summary>
    Linear,
}

/// <summary>The size of a brush, as the game's tools in the player's hands use them.</summary>
public enum LandscapeBrush
{
    /// <summary>About 2.5 metres across: a hoe's dig.</summary>
    Small,

    /// <summary>About 3.5 metres across, with a hard edge: a hoe's levelling.</summary>
    Medium,

    /// <summary>About 3.5 metres across, with a soft edge: a shovel's or a road maker's.</summary>
    Soft,

    /// <summary>The game's largest brush.</summary>
    Large,
}

/// <summary>Who changed the ground.</summary>
public enum LandscapeSource
{
    /// <summary>A player, with a tool in their hands.</summary>
    Player,

    /// <summary>A villager at work.</summary>
    Resident,

    /// <summary>A building: its footprint levelled as it stands up, a field ploughed, a levelling plan or a road plan worked on.</summary>
    Building,

    /// <summary>A mod, through <see cref="Landscape"/> (<c>ModId</c> says which).</summary>
    Mod,

    /// <summary>Another player's game: the change came over the network.</summary>
    Remote,

    /// <summary>Anything else of the game: a spell, a sapling growing.</summary>
    Other,
}

/// <summary>What became of a change to the ground.</summary>
public enum LandscapeResult
{
    /// <summary>The ground changed; it shows within a frame or two, and is saved and sent to the other players as the game's own changes.</summary>
    Done,

    /// <summary>Asked from another thread: the change is made on the main thread at the start of the next frame.</summary>
    Queued,

    /// <summary>No world is loaded.</summary>
    NotAvailable,

    /// <summary>The area is not all in the part of the world loaded around the player.</summary>
    NotLoaded,

    /// <summary>
    /// A building stands in the area, another mod's protected zone keeps the ground (<see cref="Landscape.Protect"/>), or the
    /// game will not change the ground there.
    /// </summary>
    Blocked,

    /// <summary>The ground there is too steep for the game.</summary>
    TooSteep,

    /// <summary>The ground would go higher than the game allows.</summary>
    TooHigh,

    /// <summary>The area is under water.</summary>
    Underwater,

    /// <summary>A handler of <see cref="Landscape.GroundChanging"/> cancelled it.</summary>
    Cancelled,

    /// <summary>The game failed to make the change (logged).</summary>
    Failed,

    /// <summary>
    /// Made round what stands in the area (<see cref="LandscapeOptions.ObstacleMode"/>): a building, a fixed place of the
    /// world or another mod's protected zone kept the ground under it or had it fitted to it, and the rest of the area changed.
    /// </summary>
    Partial,
}
