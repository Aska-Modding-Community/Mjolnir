using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

// The changes the SDK writes sample by sample (every half metre), each blended by the area's weight there: the five basic
// changes over a circle, a polygon or a mask, and smoothing, ramps, a mod's own shape, measuring and snapshots over any area.
// They are applied, saved and sent to the other players as the game's tool's changes are, raise the same events, and take
// the removable objects where they clear the vegetation, as the game's tool does.
public static partial class Landscape
{
    // A height changes when it moves by more than this, in metres (the game keeps heights to about a millimetre).
    private const float Unchanged = 0.0005f;

    // Smoothing evens a sample with its neighbours this many samples away on each side (a metre at the game's half metre).
    private const int SmoothReach = 2;

    /// <summary>
    /// Evens out the ground's bumps and steps in the area: each point moves towards the average of the ground about a metre
    /// round it, by <paramref name="strength"/> (and the area's weight). Done again, it smooths further, as a brush does.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="strength">How far towards the average, over 0 and at most 1.</param>
    /// <param name="options">How; <see cref="LandscapeOptions.Kind"/> and <see cref="LandscapeOptions.ClearVegetation"/> as for any change of height.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strength"/> is out of range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Smooth(LandscapeArea area, float strength = 0.5f, LandscapeOptions? options = null)
    {
        if (!(strength > 0 && strength <= 1)) throw new ArgumentOutOfRangeException(nameof(strength), strength, "A strength is over 0 and at most 1.");
        var how = options ?? LandscapeOptions.Default;
        return Write(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), LandscapeOperation.Smooth, area, how, SmoothReach, samples =>
        {
            var before = (float[])samples.Heights.Clone();
            int n = samples.Size;
            Toward(samples, area, how, (i, _, _, now) =>
            {
                int row = i / n, column = i % n;
                float sum = 0;
                int count = 0;
                for (int r = Math.Max(row - SmoothReach, 0); r <= Math.Min(row + SmoothReach, n - 1); r++)
                    for (int c = Math.Max(column - SmoothReach, 0); c <= Math.Min(column + SmoothReach, n - 1); c++)
                    {
                        sum += before[r * n + c];
                        count++;
                    }
                return now + (sum / count - now) * strength;
            });
            return true;
        });
    }

    /// <summary>
    /// Makes the ground in the area a ramp from <paramref name="from"/> to <paramref name="to"/>: each point takes the height
    /// of the line between them where it lies across it (the heights of the two points' y; before the first and past the
    /// second the ramp stays level), blended by the area's weight.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="from">The ramp's foot: its place on the ground and its height (y), from 0 to <see cref="MaximumHeight"/>.</param>
    /// <param name="to">The ramp's top, likewise; not at <paramref name="from"/>'s place.</param>
    /// <param name="options">How; as for any change of height.</param>
    /// <exception cref="ArgumentOutOfRangeException">A height is out of range, or the two points stand at one place.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Slope(LandscapeArea area, Vector3 from, Vector3 to, LandscapeOptions? options = null)
    {
        CheckHeight(from.y, nameof(from));
        CheckHeight(to.y, nameof(to));
        float dx = to.x - from.x, dz = to.z - from.z, length = dx * dx + dz * dz;
        if (!(length > 0.0001f) || !float.IsFinite(length)) throw new ArgumentOutOfRangeException(nameof(to), "A ramp's two ends stand at different places.");
        var how = options ?? LandscapeOptions.Default;
        return Write(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), LandscapeOperation.Slope, area, how, 0, samples =>
        {
            Toward(samples, area, how, (_, x, z, _) =>
            {
                float t = Math.Clamp(((x - from.x) * dx + (z - from.z) * dz) / length, 0f, 1f);
                return from.y + (to.y - from.y) * t;
            });
            return true;
        });
    }

    /// <summary>
    /// Gives the ground in the area the heights <paramref name="height"/> works out: it is asked for every point (every half
    /// metre) with the point's offset from the area's middle along x and z and the ground's height there now, and gives the
    /// height wanted there (NaN to leave the point as it is). Each answer is blended by the area's weight and kept within 0
    /// and <see cref="MaximumHeight"/>. A brush of the mod's own: a crater, terraces, a noise of its own.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="height">The mod's function: (offset x, offset z, height now) → the height wanted, in metres. If it throws, nothing changes and the mod is disabled.</param>
    /// <param name="options">How; as for any change of height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="height"/> is null.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Shape(LandscapeArea area, Func<float, float, float, float> height, LandscapeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(height);
        var how = options ?? LandscapeOptions.Default;
        var owner = height.Method.DeclaringType?.Assembly;
        float cx = area.Center.x, cz = area.Center.z;
        return Write(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), LandscapeOperation.Shape, area, how, 0, samples =>
            ModCall.TryRun(owner, "Landscape.Shape", () =>
            {
                Toward(samples, area, how, (_, x, z, now) => height(x - cx, z - cz, now));
                return true;
            }, out bool _));
    }

    /// <summary>
    /// The heights of the ground in the area: the lowest, the highest and the average (each point counted by the area's
    /// weight there), every half metre. <see cref="LandscapeHeights.IsKnown"/> is false where the area is not all loaded, or
    /// off the main thread.
    /// </summary>
    public static LandscapeHeights Measure(LandscapeArea area)
    {
        if (Live() is not { } source || Refused(source, area, Unguarded, out _) != null) return LandscapeHeights.Unknown;
        var samples = Read(source, area, 0);
        if (samples == null) return LandscapeHeights.Unknown;
        float minimum = float.MaxValue, maximum = float.MinValue;
        double sum = 0, weights = 0;
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                float w = area.Weight(samples.X(c), samples.Z(r));
                if (w <= 0) continue;
                float h = samples.Heights[samples.Index(r, c)];
                minimum = Math.Min(minimum, h);
                maximum = Math.Max(maximum, h);
                sum += h * w;
                weights += w;
            }
        return weights > 0 ? new LandscapeHeights(minimum, maximum, (float)(sum / weights)) : LandscapeHeights.Unknown;
    }

    /// <summary>
    /// Keeps the ground of the area as it is now, every half metre: its heights, its kind, its vegetation and its biome. With it a mod
    /// puts the ground back later (<see cref="LandscapeSnapshot.Restore"/>, an undo) or lays the same ground somewhere else
    /// (<see cref="LandscapeSnapshot.PasteAt"/>). It lives in the mod's memory, not in the save.
    /// </summary>
    /// <returns>The snapshot, or null where the area is not all loaded, no world is loaded, or off the main thread.</returns>
    public static LandscapeSnapshot? Capture(LandscapeArea area)
    {
        if (Live() is not { } source || Refused(source, area, Unguarded, out _) != null) return null;
        try
        {
            var samples = Read(source, area, 0);
            return samples == null ? null : new LandscapeSnapshot(area, samples.Copy(), samples.HeightAt(area.Center.x, area.Center.z));
        }
        catch (Exception e)
        {
            SdkLog.Warn(Platform, $"The ground of {area} could not be read: {e.Message}");
            return null;
        }
    }

    private static readonly LandscapeOptions Unguarded = new() { KeepOffBuildings = false };

    private static void CheckHeight(float height, string name)
    {
        if (!(height >= 0 && height <= MaximumHeight)) throw new ArgumentOutOfRangeException(name, height, $"A height is from 0 to {MaximumHeight} m.");
    }

    // The five basic changes written by the SDK (a circle, a polygon, a mask).
    private static LandscapeResult WriteBasic(string modId, LandscapeEdit edit)
    {
        var area = edit.Area;
        var how = edit.Options;
        float amount = edit.Amount;
        return Write(modId, edit.Operation, area, how, 0, samples =>
        {
            switch (edit.Operation)
            {
                case LandscapeOperation.Raise:
                    Toward(samples, area, how, (_, _, _, now) => now + amount);
                    break;
                case LandscapeOperation.Lower:
                    Toward(samples, area, how, (_, _, _, now) => now - amount);
                    break;
                case LandscapeOperation.Level:
                    Toward(samples, area, how, (_, _, _, _) => amount);
                    break;
                default:
                    Mark(samples, area, edit.Kind, how.ClearVegetation || edit.Operation == LandscapeOperation.ClearVegetation, how.OnlyOn);
                    break;
            }
            return true;
        });
    }

    /// <summary>
    /// Makes a change the SDK writes: checked as every change, <see cref="GroundChanging"/> raised (a cancel stops it), the
    /// ground read round the area (with <paramref name="margin"/> samples more on each side), changed by
    /// <paramref name="change"/> (false: a mod's code failed, nothing written), and written back with the touched samples.
    /// </summary>
    internal static LandscapeResult Write(string modId, LandscapeOperation operation, LandscapeArea area, LandscapeOptions options, int margin,
        Func<LandscapeSamples, bool> change, LandscapeSamples? readAs = null, bool raw = false)
    {
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Write(modId, operation, area, options, margin, change, readAs, raw));
            return LandscapeResult.Queued;
        }
        if (Live() is not { } source) return LandscapeResult.NotAvailable;
        if (Refused(source, area, options, out string? building) is { } refused)
        {
            if (building != null) SdkLog.Info(Platform, $"{operation} over {area} refused: '{building}' stands there");
            return refused;
        }
        if (Zoned(modId, area, operation)) return LandscapeResult.Blocked;
        s_editingMod = modId;
        s_editing = true;
        s_cancelled = false;
        try
        {
            var bounds = area.Bounds;
            if (RaiseChanging(bounds, operation, LandscapeSource.Mod)) return LandscapeResult.Cancelled;
            return Guarded(() =>
            {
                var samples = readAs != null ? source.Read(readAs.CenterX, readAs.CenterZ, readAs.Side) : Read(source, area, margin);
                if (samples == null) return LandscapeResult.NotLoaded;
                if (readAs != null && (samples.Size != readAs.Size || Math.Abs(samples.OriginX - readAs.OriginX) > 0.01f || Math.Abs(samples.OriginZ - readAs.OriginZ) > 0.01f))
                {
                    SdkLog.Warn(Platform, $"{operation} over {area}: the ground's samples no longer lie where the snapshot's did");
                    return LandscapeResult.Failed;
                }
                if (!change(samples)) return LandscapeResult.Failed;
                bool touched = Array.IndexOf(samples.Touched, true) >= 0;
                if (!touched && Array.IndexOf(samples.BiomeChanged, true) < 0) return LandscapeResult.Done;
                bool objects = touched && (options.Objects == LandscapeObjects.Settle || (options.Objects == LandscapeObjects.Remove && options.ClearVegetation && !raw));
                Func<float, float, bool>? objectsAt = objects ? (x, z) => area.Weight(x, z) >= 0.5f : null;
                return source.Write(samples, new GroundWrite(operation, bounds, raw, objectsAt, options.Objects));
            });
        }
        finally
        {
            s_editing = false;
            s_editingMod = null;
        }
    }

    private static LandscapeSamples? Read(ITerrainSource source, LandscapeArea area, int margin)
    {
        var bounds = area.Bounds;
        float side = Math.Max(bounds.m_Width, bounds.m_Height) + 2 * margin * 0.5f + 1f;
        return source.Read(bounds.m_XMin + bounds.m_Width / 2, bounds.m_YMin + bounds.m_Height / 2, side);
    }

    /// <summary>
    /// Moves every sample of the area towards the height <paramref name="target"/> gives it (index, x, z, height now → height
    /// wanted; NaN leaves it), by the area's weight there; a sample whose height changes takes the change's kind and loses its
    /// vegetation, as the game's tool does.
    /// </summary>
    internal static void Toward(LandscapeSamples samples, LandscapeArea area, LandscapeOptions options, Func<int, float, float, float, float> target)
    {
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                float x = samples.X(c), z = samples.Z(r);
                float w = area.Weight(x, z);
                if (w <= 0) continue;
                int i = samples.Index(r, c);
                if (options.OnlyOn is { } only && samples.Kinds[i] != only) continue;
                float now = samples.Heights[i];
                float wanted = target(i, x, z, now);
                if (!float.IsFinite(wanted)) continue;
                float height = Math.Clamp(now + (wanted - now) * w, 0f, MaximumHeight);
                if (Math.Abs(height - now) <= Unchanged) continue;
                samples.Heights[i] = height;
                samples.HeightChanged[i] = true;
                samples.Touched[i] = true;
                if (!options.KeepKind) samples.Kinds[i] = options.Kind ?? GroundKind.Dirt;
                if (options.ClearVegetation) samples.Cleared[i] = true;
            }
    }

    // The kind (when given) and the vegetation (when cleared) of every sample at least half in the area.
    private static void Mark(LandscapeSamples samples, LandscapeArea area, GroundKind? kind, bool clear, GroundKind? onlyOn)
    {
        for (int r = 0; r < samples.Size; r++)
            for (int c = 0; c < samples.Size; c++)
            {
                if (area.Weight(samples.X(c), samples.Z(r)) < 0.5f) continue;
                int i = samples.Index(r, c);
                if (onlyOn is { } only && samples.Kinds[i] != only) continue;
                if (kind is { } k && samples.Kinds[i] != k)
                {
                    samples.Kinds[i] = k;
                    samples.Touched[i] = true;
                }
                if (clear && !samples.Cleared[i])
                {
                    samples.Cleared[i] = true;
                    samples.Touched[i] = true;
                }
            }
    }
}
