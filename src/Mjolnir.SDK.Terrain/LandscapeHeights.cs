using System;

namespace Mjolnir.SDK.Terrain;

/// <summary>The heights of the ground over an area (<see cref="Landscape.Measure"/>), in metres.</summary>
public readonly struct LandscapeHeights : IEquatable<LandscapeHeights>
{
    internal static LandscapeHeights Unknown { get; } = new(float.NaN, float.NaN, float.NaN);

    internal LandscapeHeights(float minimum, float maximum, float average)
    {
        Minimum = minimum;
        Maximum = maximum;
        Average = average;
    }

    /// <summary>Whether the heights were read: false where the area is not all loaded, or off the main thread (the heights are then NaN).</summary>
    public bool IsKnown => float.IsFinite(Average);

    /// <summary>The lowest ground in the area.</summary>
    public float Minimum { get; }

    /// <summary>The highest ground in the area.</summary>
    public float Maximum { get; }

    /// <summary>The average height, each point counted by the area's weight there (its blend counts less).</summary>
    public float Average { get; }

    /// <inheritdoc/>
    public bool Equals(LandscapeHeights other) => Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum) && Average.Equals(other.Average);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LandscapeHeights other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Minimum, Maximum, Average);

    /// <summary>Whether two measures are the same.</summary>
    public static bool operator ==(LandscapeHeights left, LandscapeHeights right) => left.Equals(right);

    /// <summary>Whether two measures differ.</summary>
    public static bool operator !=(LandscapeHeights left, LandscapeHeights right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        IsKnown ? FormattableString.Invariant($"{Minimum:0.##} to {Maximum:0.##} m, {Average:0.##} on average") : "unknown";
}
