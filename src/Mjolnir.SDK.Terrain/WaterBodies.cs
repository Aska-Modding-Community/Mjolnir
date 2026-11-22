using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Still water on land: the world's lakes and ponds of mods. A mod digs a basin with the ground's changes
/// (<see cref="Landscape.Lower"/>, <see cref="Landscape.Shape"/>…) and fills it: <see cref="Place"/> fills the basin a point
/// lies in up to a level, as far as the ground under that level reaches from the point, within a square around it. The
/// water is the game's lake water, the character swims in it, and the game saves the pond and sends it to the other
/// players (who need the same SDK). The pond follows the ground: dig its shore and the water follows on the next frame.
/// The sea is not one of them (<see cref="Landscape.WaterLevel"/>). Read and change them on the main thread.
/// </summary>
/// <example><code>
/// // A pond in a basin dug a moment ago: filled to half a metre under its rim.
/// var fill = WaterBodies.Measure(bottom, rim - 0.5f);
/// if (fill.Result == WaterFillResult.Fits)
///     WaterBodies.Place(bottom, rim - 0.5f);
/// </code></example>
public static class WaterBodies
{
    /// <summary>The smallest square a pond is looked for in, in metres.</summary>
    public const float MinimumSize = 4f;

    /// <summary>The largest square a pond is looked for in, in metres.</summary>
    public const float MaximumSize = 128f;

    /// <summary>The finest step the ground is read at, in metres: the game's heightmap's.</summary>
    internal const float FinestStep = 0.5f;

    /// <summary>The most cells along a side of a pond's square: a larger square is read at a coarser step.</summary>
    internal const int MaximumCells = 128;

    // How often a pond that could not be filled (its ground not loaded) is tried again, in seconds.
    private const float RetrySeconds = 2f;

    private static readonly Dictionary<object, Pond> s_ponds = [];
    private static readonly List<Rect> s_changed = [];
    private static readonly List<object> s_gone = [];

    /// <summary>The game's water; a fake in the unit tests.</summary>
    internal static IWaterSource? Source { get; set; }

    /// <summary>
    /// The still water of the world: every pond of a mod and the lakes of the loaded world. Empty off the main thread or
    /// without a world.
    /// </summary>
    public static IReadOnlyList<WaterBody> All
    {
        get
        {
            if (Live() is not { } water) return [];
            try
            {
                var bodies = water.Bodies;
                var list = new List<WaterBody>(bodies.Count);
                foreach (var body in bodies)
                    if (!body.IsGone) list.Add(new WaterBody(body));
                return list;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"The still water could not be read: {e.Message}");
                return [];
            }
        }
    }

    /// <summary>The water whose middle is nearest <paramref name="center"/>, within <paramref name="radius"/> metres; null for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static WaterBody? Nearest(Vector3 center, float radius = 32f)
    {
        if (!(radius > 0 && radius < 10000f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is over 0 m.");
        WaterBody? best = null;
        float nearest = radius * radius;
        foreach (var body in All)
        {
            var at = body.Center;
            float dx = at.x - center.x, dz = at.z - center.z, distance = dx * dx + dz * dz;
            if (distance > nearest) continue;
            nearest = distance;
            best = body;
        }
        return best;
    }

    /// <summary>The still water at a point (a pond's or a lake's); null where there is none, or in the sea.</summary>
    public static WaterBody? At(float x, float z)
    {
        foreach (var body in All)
            if (body.Covers(x, z)) return body;
        return null;
    }

    /// <summary>
    /// What a pond filled from <paramref name="point"/> up to <paramref name="level"/> would cover, without filling it: for
    /// a preview, with the reason where it would not be placed. Main thread; elsewhere and without a world,
    /// <see cref="WaterFillResult.NotAvailable"/>.
    /// </summary>
    /// <param name="point">A point of the basin; its y is not used.</param>
    /// <param name="level">The height of the water's surface, in metres of the world.</param>
    /// <param name="size">The side of the square around the point the basin is looked for in, from <see cref="MinimumSize"/>
    /// to <see cref="MaximumSize"/> metres: the water reaches no further.</param>
    /// <exception cref="ArgumentOutOfRangeException">A point, level or size out of range or not a number.</exception>
    public static WaterFill Measure(Vector3 point, float level, float size = 64f)
    {
        Check(point, level, size);
        if (Landscape.LiveSource() is not { } terrain || Live() is not { } water) return WaterFill.None(WaterFillResult.NotAvailable, level);
        return Fill(terrain, water, point, level, size, null);
    }

    /// <summary>
    /// Fills the basin <paramref name="point"/> lies in up to <paramref name="level"/>, as <see cref="Measure"/> measures it,
    /// if it fits there (<see cref="WaterFillResult.Fits"/>): the pond is placed in the world, saved and sent to the other
    /// players, and drawn and swum in at once.
    /// </summary>
    /// <param name="point">A point of the basin; its y is not used. The pond keeps it as its <see cref="WaterBody.Center"/>.</param>
    /// <param name="level">The height of the water's surface, in metres of the world.</param>
    /// <param name="size">The side of the square around the point the basin is looked for in (<see cref="Measure"/>).</param>
    /// <returns>The pond, or null where it does not fit, off the main thread or without a world (logged).</returns>
    /// <exception cref="ArgumentOutOfRangeException">A point, level or size out of range or not a number.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WaterBody? Place(Vector3 point, float level, float size = 64f)
    {
        Check(point, level, size);
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (Landscape.LiveSource() is not { } terrain || Live() is not { } water) return null;
        var fill = Fill(terrain, water, point, level, size, null);
        if (fill.Result != WaterFillResult.Fits)
        {
            SdkLog.Info(Landscape.Platform, FormattableString.Invariant($"{modId}'s pond at ({point.x:0.#}, {point.z:0.#}) to {level:0.##} m not placed: {fill.Result}"));
            return null;
        }
        try
        {
            var placed = water.Place(At(point.x, level, point.z), size);
            if (placed == null) return null;
            var pond = new Pond(placed) { Fill = fill };
            s_ponds[placed.Key] = pond;
            water.Show(placed, fill);
            SdkLog.Info(Landscape.Platform, FormattableString.Invariant($"{modId} filled a pond at ({point.x:0.#}, {point.z:0.#}) to {level:0.##} m: {fill.Area:0} m², {fill.Volume:0} m³"));
            return new WaterBody(placed);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A pond could not be placed: {e.Message}");
            return null;
        }
    }

    private static void Check(Vector3 point, float level, float size)
    {
        if (!float.IsFinite(point.x) || !float.IsFinite(point.z)) throw new ArgumentOutOfRangeException(nameof(point), "A point of the world.");
        if (!(level > -1000f && level < 1000f)) throw new ArgumentOutOfRangeException(nameof(level), level, "A height of the world.");
        if (!(size >= MinimumSize && size <= MaximumSize))
            throw new ArgumentOutOfRangeException(nameof(size), size, FormattableString.Invariant($"A pond's square is from {MinimumSize} to {MaximumSize} m."));
    }

    // By the fields: Rect's own members and Vector3's constructor run the IL2CPP runtime, which unit tests do not have.
    internal static bool Overlap(Rect a, Rect b) =>
        a.m_XMin < b.m_XMin + b.m_Width && b.m_XMin < a.m_XMin + a.m_Width && a.m_YMin < b.m_YMin + b.m_Height && b.m_YMin < a.m_YMin + a.m_Height;

    internal static Vector3 At(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    private static IWaterSource? Live() => Landscape.IsMainThread && Source is { IsAvailable: true } water ? water : null;

    // The fill of a basin, the other ponds' water kept out of it (all of them but the one being filled again).
    private static WaterFill Fill(ITerrainSource terrain, IWaterSource water, Vector3 point, float level, float size, object? self)
    {
        var fill = WaterFill.Compute(terrain, point, level, size, self == null);
        if (fill.Result != WaterFillResult.Fits) return fill;
        foreach (var other in water.Bodies)
        {
            if (other.IsGone || (self != null && Equals(other.Key, self))) continue;
            if (!other.IsWorldLake && s_ponds.TryGetValue(other.Key, out var pond) && pond.Fill is { } theirs)
            {
                if (Overlap(fill.Bounds, theirs.Bounds) && fill.Shares(theirs)) return fill.As(WaterFillResult.Overlaps);
            }
            else if (other.IsWorldLake && Overlap(fill.Bounds, other.Bounds) && fill.Any(other.Covers))
                return fill.As(WaterFillResult.Overlaps);
        }
        return fill;
    }

    /// <summary>A change of the ground: the ponds it reaches are filled again on the next tick.</summary>
    internal static void GroundChanged(Rect area)
    {
        if (s_ponds.Count == 0) return;
        lock (s_changed) s_changed.Add(area);
    }

    /// <summary>Whether a pond watches the ground's changes (the game's side keeps them for it).</summary>
    internal static bool WatchesGround => s_ponds.Count > 0;

    /// <summary>
    /// The module's tick on the main thread: every pond of the world filled from the ground once it is loaded, again when the
    /// ground changes under it, and its water shown; a gone pond forgotten.
    /// </summary>
    internal static void Tick(ITerrainSource? terrain)
    {
        if (terrain == null || Live() is not { } water)
        {
            s_ponds.Clear();
            lock (s_changed) s_changed.Clear();
            return;
        }
        Rect[] changed;
        lock (s_changed)
        {
            changed = s_changed.ToArray();
            s_changed.Clear();
        }
        float now = (float)(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        IReadOnlyList<IWaterBodyView> bodies;
        try
        {
            bodies = water.Bodies;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The ponds could not be read: {e.Message}");
            return;
        }
        foreach (var body in bodies)
        {
            if (body.IsWorldLake || body.IsGone) continue;
            if (!s_ponds.TryGetValue(body.Key, out var pond)) s_ponds[body.Key] = pond = new Pond(body);
            bool reached = false;
            foreach (var area in changed)
                if (Overlap(area, Square(body))) reached = true;
            if (!reached && pond.Fill != null) continue;
            if (!reached && now < pond.RetryAt) continue;
            var center = body.Center;
            var fill = WaterFill.Compute(terrain, center, center.y, body.Size, false);
            // Its own water: a point of it is under water already, which the ground's read does not stop.
            var shown = fill.Result is WaterFillResult.Fits or WaterFillResult.Spills ? fill : null;
            pond.RetryAt = now + RetrySeconds;
            if (shown == null && pond.Fill == null) continue;
            pond.Fill = shown;
            try
            {
                water.Show(body, shown);
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"A pond's water could not be shown: {e.Message}");
            }
        }
        foreach (var (key, pond) in s_ponds)
            if (pond.View.IsGone) s_gone.Add(key);
        foreach (var key in s_gone) s_ponds.Remove(key);
        s_gone.Clear();
    }

    private static Rect Square(IWaterBodyView body)
    {
        var c = body.Center;
        return LandscapeArea.MakeRect(c.x - body.Size / 2, c.z - body.Size / 2, body.Size, body.Size);
    }

    /// <summary>A pond's water as the model filled it last; null while it is not filled.</summary>
    internal static WaterFill? FillOf(object key) => s_ponds.TryGetValue(key, out var pond) ? pond.Fill : null;

    /// <summary>Forgets everything, between unit tests.</summary>
    internal static void ResetForTests()
    {
        s_ponds.Clear();
        lock (s_changed) s_changed.Clear();
        Source = null;
    }

    private sealed class Pond(IWaterBodyView view)
    {
        public IWaterBodyView View { get; } = view;

        public WaterFill? Fill { get; set; }

        public float RetryAt { get; set; }
    }
}

/// <summary>
/// Still water (<see cref="WaterBodies"/>): a pond of a mod, filled from a point up to a level, or a lake of the world. It
/// stands for the water in the world, so what it tells follows it, and it is <see cref="IsGone"/> once taken away.
/// </summary>
public sealed class WaterBody
{
    private readonly IWaterBodyView _view;

    internal WaterBody(IWaterBodyView view) => _view = view;

    /// <summary>Where it was filled from (a lake: its middle), at the height of its surface.</summary>
    public Vector3 Center => _view.Center;

    /// <summary>The height of its surface, in metres of the world.</summary>
    public float Level => _view.Center.y;

    /// <summary>The side of the square around <see cref="Center"/> its water reaches no further than, in metres.</summary>
    public float Size => _view.Size;

    /// <summary>Whether it is one of the world's lakes, which a mod reads but does not change or take away.</summary>
    public bool IsWorldLake => _view.IsWorldLake;

    /// <summary>Whether it is no longer in the world: taken away, or its part of the world left.</summary>
    public bool IsGone => !Landscape.IsMainThread || _view.IsGone;

    /// <summary>The square of the world its water lies in: a pond's water as filled, a lake's whole area.</summary>
    public Rect Bounds => !_view.IsWorldLake && WaterBodies.FillOf(_view.Key) is { } fill ? fill.Bounds : _view.Bounds;

    /// <summary>The square metres of its surface: a pond's as filled; 0 for a lake, or a pond whose ground is not loaded.</summary>
    public float Area => WaterBodies.FillOf(_view.Key)?.Area ?? 0f;

    /// <summary>The cubic metres of its water: a pond's as filled; 0 for a lake, or a pond whose ground is not loaded.</summary>
    public float Volume => WaterBodies.FillOf(_view.Key)?.Volume ?? 0f;

    /// <summary>Whether its water covers a point.</summary>
    public bool Covers(float x, float z) => _view.IsWorldLake
        ? _view.Covers(x, z)
        : WaterBodies.FillOf(_view.Key) is { } fill && fill.Covers(x, z);

    /// <summary>How deep its water is at a point, in metres: 0 where it does not cover it or the ground is not loaded.</summary>
    public float DepthAt(float x, float z)
    {
        if (!Covers(x, z) || Landscape.LiveSource() is not { } terrain || !terrain.TryGetHeight(x, z, out float ground)) return 0f;
        return MathF.Max(0f, Level - ground);
    }

    /// <summary>Takes a pond away, as it was placed: saved, and for the other players too. A lake of the world stays.</summary>
    /// <returns>Whether it is gone now; false for a lake, off the main thread.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Remove()
    {
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (!Landscape.IsMainThread || IsWorldLake) return false;
        if (_view.IsGone) return true;
        try
        {
            bool gone = _view.Remove();
            if (gone) SdkLog.Info(Landscape.Platform, $"{modId} took away the {this}");
            return gone;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be taken away: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant(
        $"{(IsWorldLake ? "lake" : "pond")} at ({Center.x:0.#}, {Center.z:0.#}), its surface at {Level:0.##} m");
}

/// <summary>Why a pond fits or does not where it is asked for (<see cref="WaterBodies.Measure"/>).</summary>
public enum WaterFillResult
{
    /// <summary>The basin holds the water: a pond may be placed.</summary>
    Fits,

    /// <summary>No world, or not the main thread.</summary>
    NotAvailable,

    /// <summary>Part of the square around the point is not loaded.</summary>
    NotLoaded,

    /// <summary>The ground at the point is at the level or above: nothing to fill.</summary>
    AboveLevel,

    /// <summary>The water reaches the edge of the square: the basin is open at that level, or larger than the square.</summary>
    Spills,

    /// <summary>The point is in water already: the sea, a lake or a pond.</summary>
    InWater,

    /// <summary>The water would run into a pond's or a lake's.</summary>
    Overlaps,
}

/// <summary>
/// What a pond filled from a point up to a level covers (<see cref="WaterBodies.Measure"/>): read from the ground once, at
/// most every half metre, within its square.
/// </summary>
public sealed class WaterFill
{
    private readonly float _originX, _originZ, _step;
    private readonly int _cells;
    private readonly bool[] _wet;
    private readonly float[] _depth;

    private WaterFill(WaterFillResult result, float level, float originX, float originZ, float step, int cells, bool[] wet, float[] depth, Rect bounds, float area, float volume)
    {
        Result = result;
        Level = level;
        _originX = originX;
        _originZ = originZ;
        _step = step;
        _cells = cells;
        _wet = wet;
        _depth = depth;
        Bounds = bounds;
        Area = area;
        Volume = volume;
    }

    /// <summary>Whether a pond fits, or why not.</summary>
    public WaterFillResult Result { get; }

    /// <summary>The height of the surface asked for.</summary>
    public float Level { get; }

    /// <summary>The square of the world the water lies in; empty for none.</summary>
    public Rect Bounds { get; }

    /// <summary>The square metres of its surface.</summary>
    public float Area { get; }

    /// <summary>The cubic metres of water it holds.</summary>
    public float Volume { get; }

    /// <summary>Whether its water covers a point.</summary>
    public bool Covers(float x, float z) => Index(x, z) is int i && _wet[i];

    /// <summary>How deep its water is at a point, as read: 0 where it does not cover it.</summary>
    public float DepthAt(float x, float z) => Index(x, z) is int i && _wet[i] ? _depth[i] : 0f;

    /// <summary>The cells along a side of its grid, and the metres a cell spans: what its mask is drawn from.</summary>
    internal int Cells => _cells;

    internal float Step => _step;

    internal float OriginX => _originX;

    internal float OriginZ => _originZ;

    /// <summary>Whether a cell of its grid is water (column, row).</summary>
    internal bool IsWet(int column, int row) => _wet[row * _cells + column];

    private int? Index(float x, float z)
    {
        if (_cells == 0) return null;
        int i = (int)MathF.Floor((x - _originX) / _step), j = (int)MathF.Floor((z - _originZ) / _step);
        if (i < 0 || j < 0 || i >= _cells || j >= _cells) return null;
        return j * _cells + i;
    }

    internal WaterFill As(WaterFillResult result) =>
        new(result, Level, _originX, _originZ, _step, _cells, _wet, _depth, Bounds, Area, Volume);

    /// <summary>Whether a point of this water is water of the other too.</summary>
    internal bool Shares(WaterFill other) => Any(other.Covers);

    /// <summary>Whether any wet cell's middle passes <paramref name="test"/>.</summary>
    internal bool Any(Func<float, float, bool> test)
    {
        for (int j = 0; j < _cells; j++)
            for (int i = 0; i < _cells; i++)
                if (_wet[j * _cells + i] && test(_originX + (i + 0.5f) * _step, _originZ + (j + 0.5f) * _step)) return true;
        return false;
    }

    internal static WaterFill None(WaterFillResult result, float level) => new(result, level, 0, 0, 1, 0, [], [], default, 0, 0);

    /// <summary>
    /// Fills the basin a point lies in: every cell of the square whose ground lies under the level and is reached from the
    /// point's cell through such cells, side by side. A fill that reaches the square's edge spills.
    /// </summary>
    /// <param name="terrain">The ground.</param>
    /// <param name="point">The point; its y is not used.</param>
    /// <param name="level">The height of the surface.</param>
    /// <param name="size">The side of the square.</param>
    /// <param name="refuseWater">Whether a point in water already is refused (a new pond); a pond filled again stands in its own.</param>
    internal static WaterFill Compute(ITerrainSource terrain, Vector3 point, float level, float size, bool refuseWater)
    {
        float step = MathF.Max(WaterBodies.FinestStep, size / WaterBodies.MaximumCells);
        int n = Math.Max(1, (int)MathF.Round(size / step));
        float originX = point.x - n * step / 2, originZ = point.z - n * step / 2;
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = originX + (i + 0.5f) * step, z = originZ + (j + 0.5f) * step;
                if (!terrain.IsLoaded(x, z) || !terrain.TryGetHeight(x, z, out heights[j * n + i]))
                    return None(WaterFillResult.NotLoaded, level);
            }
        if (refuseWater && terrain.IsUnderwater(point.x, point.z)) return None(WaterFillResult.InWater, level);
        int si = Math.Clamp((int)MathF.Floor((point.x - originX) / step), 0, n - 1);
        int sj = Math.Clamp((int)MathF.Floor((point.z - originZ) / step), 0, n - 1);
        if (heights[sj * n + si] >= level) return None(WaterFillResult.AboveLevel, level);
        var wet = new bool[n * n];
        var depth = new float[n * n];
        var queue = new Queue<int>();
        wet[sj * n + si] = true;
        queue.Enqueue(sj * n + si);
        bool spills = false;
        int minI = si, maxI = si, minJ = sj, maxJ = sj, count = 0;
        double volume = 0;
        while (queue.Count > 0)
        {
            int k = queue.Dequeue(), i = k % n, j = k / n;
            depth[k] = level - heights[k];
            volume += depth[k];
            count++;
            if (i == 0 || j == 0 || i == n - 1 || j == n - 1) spills = true;
            minI = Math.Min(minI, i);
            maxI = Math.Max(maxI, i);
            minJ = Math.Min(minJ, j);
            maxJ = Math.Max(maxJ, j);
            Visit(i - 1, j);
            Visit(i + 1, j);
            Visit(i, j - 1);
            Visit(i, j + 1);
        }
        var bounds = LandscapeArea.MakeRect(originX + minI * step, originZ + minJ * step, (maxI - minI + 1) * step, (maxJ - minJ + 1) * step);
        float cell = step * step;
        return new WaterFill(spills ? WaterFillResult.Spills : WaterFillResult.Fits, level, originX, originZ, step, n, wet, depth, bounds, count * cell, (float)(volume * cell));

        void Visit(int i, int j)
        {
            if (i < 0 || j < 0 || i >= n || j >= n) return;
            int k = j * n + i;
            if (wet[k] || heights[k] >= level) return;
            wet[k] = true;
            queue.Enqueue(k);
        }
    }
}
