using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// What a change of the ground goes round or fits in an area (<see cref="Landscape.ObstaclesIn"/>,
/// <see cref="LandscapeOptions.ObstacleMode"/>): the footprints of the buildings and fixed places of the world standing
/// there and the protected zones of other mods. For a preview: <see cref="HeightAfter"/> tells the height a change leaves at
/// a point. Read once, it does not follow the world.
/// </summary>
public sealed class LandscapeObstacles
{
    private readonly IReadOnlyList<GroundFootprint> _footprints;
    private readonly IReadOnlyList<LandscapeArea> _zones;

    internal LandscapeObstacles(IReadOnlyList<GroundFootprint> footprints, IReadOnlyList<(string Name, LandscapeArea Area)> zones, bool fits = false)
    {
        _footprints = footprints;
        _zones = zones.Select(z => z.Area).ToList();
        Names = footprints.Select(f => f.Name).Concat(zones.Select(z => z.Name)).Distinct().ToList();
        Fits = fits;
    }

    /// <summary>Nothing to go round.</summary>
    public static LandscapeObstacles None { get; } = new([], []);

    /// <summary>Whether nothing stands in the area.</summary>
    public bool IsEmpty => _footprints.Count == 0 && _zones.Count == 0;

    /// <summary>Whether the ground under what stands there is fitted to it (<see cref="LandscapeObstacleMode.Fit"/>), not kept.</summary>
    public bool Fits { get; }

    /// <summary>What stands there, by name (the game's object, or a zone's mod), each once.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>How steep the ground round a levelled footprint may rise or fall from its height: 1 m a metre, 45°.</summary>
    internal const float EmbankmentSlope = 1f;

    /// <summary>How far round a levelled footprint its embankment is looked at, in metres.</summary>
    internal const float EmbankmentReach = 16f;

    /// <summary>
    /// Whether the ground at a point may not be the change's own: under a footprint, round a levelled one within reach of its
    /// embankment, or in a zone's own shape.
    /// </summary>
    public bool Covers(float x, float z)
    {
        foreach (var footprint in _footprints)
            if (footprint.Outside(x, z) <= (footprint.Level != null ? EmbankmentReach : 0)) return true;
        foreach (var zone in _zones)
            if (zone.Weight(x, z) >= 0.5f) return true;
        return false;
    }

    /// <summary>
    /// The height a change leaves at a point where it would make the ground <paramref name="changed"/> from
    /// <paramref name="now"/>: <paramref name="now"/> under what it goes round, the fitted height under what it fits, else
    /// <paramref name="changed"/>. Main thread: the underside of a fixed place's rocks is found by a ray.
    /// </summary>
    public float HeightAfter(float x, float z, float now, float changed) => Resolve(x, z, now, changed).Height;

    // The height left at a point, and whether the ground keeps its look there (kept, or under a levelled footprint).
    internal (float Height, bool KeepsLook) Resolve(float x, float z, float now, float changed)
    {
        foreach (var zone in _zones)
            if (zone.Weight(x, z) >= 0.5f) return (now, true);
        foreach (var footprint in _footprints)
            if (footprint.Level is { } level && footprint.Covers(x, z)) return (footprint.Fit(level, now, changed), true);
        foreach (var footprint in _footprints)
            if (footprint.Level == null && footprint.Underside == null && footprint.Covers(x, z)) return (now, true);
        // Round a levelled footprint the ground rises or falls from its height no steeper than an embankment of 45°: a pad the
        // building stands on, however often a brush passes (a blend towards its height, taken again at every write, pulled
        // the ground round it ever nearer to it).
        float embanked = changed;
        foreach (var footprint in _footprints)
        {
            if (footprint.Level is not { } level) continue;
            float d = footprint.Outside(x, z);
            if (d > EmbankmentReach) continue;
            float lowest = footprint.LevelsUp ? level - d * EmbankmentSlope : float.NegativeInfinity;
            float highest = footprint.LevelsDown ? level + d * EmbankmentSlope : float.PositiveInfinity;
            embanked = Math.Clamp(embanked, lowest, highest);
        }
        if (embanked != changed) return (embanked, false);
        // Under rocks the ground moves only between where it was and their underside: never left under them in the air, never
        // raised into them.
        foreach (var footprint in _footprints)
        {
            if (footprint.Underside is not { } underside || !footprint.Covers(x, z) || underside(x, z) is not { } u) continue;
            return (Math.Clamp(changed, MathF.Min(now, u), MathF.Max(now, u)), false);
        }
        return (changed, false);
    }

    /// <inheritdoc/>
    public override string ToString() => IsEmpty ? "nothing in the way" : string.Join(", ", Names);
}
