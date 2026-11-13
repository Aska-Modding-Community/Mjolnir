using System;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A biome of the ground: what grows on it (the trees, bushes and grass) and how it looks. The game keeps one for every 4
/// metres of the world. Each part of the world grows its own plants on a biome: the same biome is a birch forest in one part
/// and a cypress forest in the marshes, so <see cref="Name"/> is the name of what grows there in the part of the world the
/// player is in. Get one from <see cref="Landscape.Biomes"/>, <see cref="Landscape.BiomeAt"/> or <see cref="Landscape.FindBiome"/>.
/// </summary>
public readonly struct LandscapeBiome : IEquatable<LandscapeBiome>
{
    private readonly string? _name;

    internal LandscapeBiome(int id, string name)
    {
        Id = id;
        _name = name;
    }

    /// <summary>The game's number of the biome, the same in every part of the world and in the save.</summary>
    public int Id { get; }

    /// <summary>
    /// What grows on it in the part of the world the player is in, as the game names it ("BirchForest", "ResourceForest",
    /// "Default"); where that part grows nothing of its own on it, the game's name of the biome ("Swamp").
    /// </summary>
    public string Name => _name ?? "";

    /// <summary>Whether it is the same biome: the same <see cref="Id"/>, whatever its name.</summary>
    public bool Equals(LandscapeBiome other) => Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LandscapeBiome other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id;

    /// <summary>Whether two are the same biome.</summary>
    public static bool operator ==(LandscapeBiome left, LandscapeBiome right) => left.Equals(right);

    /// <summary>Whether two are different biomes.</summary>
    public static bool operator !=(LandscapeBiome left, LandscapeBiome right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant($"{Name} ({Id})");
}
