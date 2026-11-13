using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The ground of an area as it was when <see cref="Landscape.Capture"/> kept it, every half metre: its heights, its kind, its
/// vegetation and its biome. <see cref="Restore"/> puts it back (an undo); <see cref="PasteAt"/> lays the same ground
/// elsewhere. It lives in the mod's memory, not in the save; the world's objects (trees, stones) are not part of it.
/// </summary>
public sealed class LandscapeSnapshot
{
    private readonly LandscapeSamples _samples;

    internal LandscapeSnapshot(LandscapeArea area, LandscapeSamples samples, float centerHeight)
    {
        Area = area;
        _samples = samples;
        CenterHeight = centerHeight;
    }

    /// <summary>The area kept.</summary>
    public LandscapeArea Area { get; }

    /// <summary>The ground's height at the area's middle when it was kept, in metres (to paste it relative to another place).</summary>
    public float CenterHeight { get; }

    /// <summary>The kept height at a point, in metres; NaN outside what was kept.</summary>
    public float HeightAt(float x, float z) => _samples.HeightAt(x, z);

    /// <summary>
    /// Puts the area's ground back as it was kept: its heights, its kind, its vegetation and its biome, exactly, wherever the
    /// area reaches (its blend included). Like every change it is checked, raised as <see cref="Landscape.GroundChanging"/>
    /// (<see cref="LandscapeOperation.Restore"/>), saved and sent to the other players.
    /// </summary>
    /// <param name="options">How; only <see cref="LandscapeOptions.KeepOffBuildings"/> counts.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LandscapeResult Restore(LandscapeOptions? options = null)
    {
        string modId = DevConsole.OwnerOf(CallingMod.Of(Assembly.GetCallingAssembly()));
        var area = Area;
        var kept = _samples;
        return Landscape.Write(modId, LandscapeOperation.Restore, area, options ?? LandscapeOptions.Default, 0, samples =>
        {
            for (int r = 0; r < samples.Size; r++)
                for (int c = 0; c < samples.Size; c++)
                {
                    if (area.Weight(samples.X(c), samples.Z(r)) <= 0) continue;
                    int i = samples.Index(r, c);
                    samples.HeightChanged[i] = Math.Abs(samples.Heights[i] - kept.Heights[i]) > 0.0005f;
                    samples.Heights[i] = kept.Heights[i];
                    samples.Flags[i] = kept.Flags[i];
                    samples.Kinds[i] = kept.Kinds[i];
                    samples.Cleared[i] = kept.Cleared[i];
                    samples.Touched[i] = true;
                    if (kept.Biomes[i] >= 0 && samples.Biomes[i] != kept.Biomes[i])
                    {
                        samples.Biomes[i] = kept.Biomes[i];
                        samples.BiomeChanged[i] = true;
                    }
                }
            return true;
        }, readAs: kept, raw: true);
    }

    /// <summary>
    /// Lays the kept ground down with the area's middle at <paramref name="center"/>, turned on by <paramref name="rotation"/>
    /// degrees, every height moved by <paramref name="heightOffset"/> metres (to lay it relative to the ground there:
    /// <c>Landscape.HeightAt(x, z) - snapshot.CenterHeight</c>). Its heights blend into the ground by the area's weight, and
    /// where the area is at least half its kind and vegetation come with them; the biome stays the place's own.
    /// </summary>
    /// <param name="center">Where the area's middle goes (its y is not used).</param>
    /// <param name="rotation">How far the area is turned on about its middle, in degrees.</param>
    /// <param name="heightOffset">How much higher (or lower) the kept ground is laid, in metres.</param>
    /// <param name="options">How; <see cref="LandscapeOptions.KeepOffBuildings"/>, <see cref="LandscapeOptions.OnlyOn"/> and
    /// <see cref="LandscapeOptions.ClearVegetation"/> (the objects where the area is at least half) count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The centre, the turn or the offset is not a number.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LandscapeResult PasteAt(Vector3 center, float rotation = 0, float heightOffset = 0, LandscapeOptions? options = null)
    {
        if (!float.IsFinite(center.x) || !float.IsFinite(center.z)) throw new ArgumentOutOfRangeException(nameof(center), "A centre is a point of the world.");
        if (!float.IsFinite(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "A turn is a number of degrees.");
        if (!float.IsFinite(heightOffset)) throw new ArgumentOutOfRangeException(nameof(heightOffset), heightOffset, "An offset is a number of metres.");
        string modId = DevConsole.OwnerOf(CallingMod.Of(Assembly.GetCallingAssembly()));
        var source = Area;
        var target = source.Moved(center, rotation);
        var kept = _samples;
        var how = options ?? LandscapeOptions.Default;
        var onlyOn = how.OnlyOn;
        return Landscape.Write(modId, LandscapeOperation.Paste, target, how, 0, samples =>
        {
            for (int r = 0; r < samples.Size; r++)
                for (int c = 0; c < samples.Size; c++)
                {
                    float x = samples.X(c), z = samples.Z(r);
                    float w = target.Weight(x, z);
                    if (w <= 0) continue;
                    var (dx, dz) = LandscapeArea.Turn(x - center.x, z - center.z, -rotation);
                    float sx = source.Center.x + dx, sz = source.Center.z + dz;
                    float wanted = kept.HeightAt(sx, sz);
                    if (!float.IsFinite(wanted)) continue;
                    int i = samples.Index(r, c);
                    if (onlyOn is { } only && samples.Kinds[i] != only) continue;
                    float now = samples.Heights[i];
                    float height = Math.Clamp(now + (wanted + heightOffset - now) * w, 0f, Landscape.MaximumHeight);
                    if (Math.Abs(height - now) > 0.0005f)
                    {
                        samples.Heights[i] = height;
                        samples.HeightChanged[i] = true;
                        samples.Touched[i] = true;
                    }
                    int k = w >= 0.5f ? kept.Nearest(sx, sz) : -1;
                    if (k >= 0 && (samples.Kinds[i] != kept.Kinds[k] || samples.Cleared[i] != kept.Cleared[k]))
                    {
                        samples.Kinds[i] = kept.Kinds[k];
                        samples.Cleared[i] = kept.Cleared[k];
                        samples.Touched[i] = true;
                    }
                }
            return true;
        });
    }

    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant($"snapshot of {Area}");
}
