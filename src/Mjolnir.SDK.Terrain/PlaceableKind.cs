namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A kind of the world a mod can place (<see cref="NaturalObjects.Place"/>): a tree, a bush, a crop, a stone, a boulder of
/// the scenery, a plant — every kind the game grows in its world but its grass. What is placed is a world item of the
/// SDK's own made from the kind: it looks as the kind does, is harvested or gathered as it is (or neither, for scenery),
/// and is saved and sent to the other players as any world item is; every player needs the same SDK to see it.
/// </summary>
public sealed class PlaceableKind
{
    internal PlaceableKind(PlaceableKindData data)
    {
        Name = data.Name;
        Kind = data.Kind;
        AssetName = data.AssetName;
        MinimumScale = data.MinimumScale;
        MaximumScale = data.MaximumScale;
        IsHarvestable = data.IsHarvestable;
        IsGathered = data.IsGathered;
    }

    /// <summary>The game's name of the kind ("tree_willow", "bush_sloe_berries", "rock_shoreline_big").</summary>
    public string Name { get; }

    /// <summary>What kind of thing it is: a tree, an object, a large object or a plant.</summary>
    public NaturalObjectKind Kind { get; }

    /// <summary>The <see cref="NaturalObject.AssetName"/> of what is placed of it ("Mjolnir_World_tree_willow").</summary>
    public string AssetName { get; }

    /// <summary>The smallest scale the world grows it at; a tree placed smaller stands as a young one.</summary>
    public float MinimumScale { get; }

    /// <summary>The largest scale the world grows it at.</summary>
    public float MaximumScale { get; }

    /// <summary>Whether it is felled or mined (<see cref="NaturalObject.Harvest"/>): a tree, a stone clump, a deposit.</summary>
    public bool IsHarvestable { get; }

    /// <summary>Whether it is picked by hand (<see cref="NaturalObject.Gather"/>): a berry bush, a crop, a nest.</summary>
    public bool IsGathered { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Kind})";
}
