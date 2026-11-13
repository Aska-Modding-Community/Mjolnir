using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Where a change to the ground goes: a rectangle, a circle or a polygon on the ground whose edge blends into the ground around
/// it, a mask of the mod's own, or one of the game's brushes at a point. Heights are the world's, in metres; only the x and z
/// of a point count.
/// </summary>
/// <remarks>
/// <para><see cref="Landscape.Raise"/>, <see cref="Landscape.Lower"/>, <see cref="Landscape.Level"/>, <see cref="Landscape.Paint"/>
/// and <see cref="Landscape.ClearVegetation"/> change a rectangle or a brush with the game's own tool, its own edge with them;
/// every other area, and every other change, is written by the SDK sample by sample (every half metre), each sample changed
/// as much as the area's weight there: 1 inside, falling over the blend as <see cref="Falloff"/> says, 0 outside.</para>
/// <para>A brush written by the SDK is a circle as wide as the brush, at full strength over its middle half.</para>
/// </remarks>
public readonly struct LandscapeArea : IEquatable<LandscapeArea>
{
    /// <summary>The largest side of a rectangle, a mask or a polygon's box, in metres (a circle is at most this wide).</summary>
    public const float MaximumSide = 64f;

    /// <summary>The widest blend of an area's edge, in metres.</summary>
    public const float MaximumBlend = 16f;

    /// <summary>The most points a polygon has.</summary>
    public const int MaximumPoints = 256;

    /// <summary>The most samples a mask has along either side.</summary>
    public const int MaximumMaskSamples = 1024;

    private enum Form : byte { Rectangle, Brush, Circle, Polygon, Mask }

    private readonly Form _form;
    private readonly float[]? _points; // a polygon's x and z, in turn
    private readonly float[,]? _mask;   // a mask's weights: rows along z, columns along x

    private LandscapeArea(Form form, Vector3 center, float width, float depth, float rotation, float blend, LandscapeBrush? brush,
        LandscapeFalloff falloff, float[]? points = null, float[,]? mask = null)
    {
        _form = form;
        Center = center;
        Width = width;
        Depth = depth;
        Rotation = rotation;
        Blend = blend;
        Brush = brush;
        Falloff = falloff;
        _points = points;
        _mask = mask;
    }

    /// <summary>The middle of the area (its y is not used): a polygon's is the middle of its box.</summary>
    public Vector3 Center { get; }

    /// <summary>Its size along x before it is turned, in metres: a brush's is the brush's size, a circle's its diameter, a polygon's its box's.</summary>
    public float Width { get; }

    /// <summary>Its size along z before it is turned, in metres: a brush's is the brush's size, a circle's its diameter, a polygon's its box's.</summary>
    public float Depth { get; }

    /// <summary>How far it is turned about the vertical, in degrees (clockwise seen from above): a rectangle's or a mask's.</summary>
    public float Rotation { get; }

    /// <summary>How wide its edge blends into the ground around it, in metres, outside its own shape (a brush's and a mask's are their own).</summary>
    public float Blend { get; }

    /// <summary>The game's brush, or null for any other area.</summary>
    public LandscapeBrush? Brush { get; }

    /// <summary>How a change the SDK writes fades out over the blend (the game's tool keeps its own edge).</summary>
    public LandscapeFalloff Falloff { get; }

    /// <summary>A polygon's corners on the ground (y 0), in order; empty for any other area.</summary>
    public IReadOnlyList<Vector3> Points
    {
        get
        {
            if (_points == null) return [];
            var points = new Vector3[_points.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = At(_points[2 * i], _points[2 * i + 1]);
            return points;
        }
    }

    /// <summary>Whether the game's own tool can make the five basic changes over it: a rectangle or a brush.</summary>
    internal bool IsGameShape => _form is Form.Rectangle or Form.Brush;

    /// <summary>Whether its outline is a circle: a circle's, or a brush's reach.</summary>
    internal bool IsRound => _form is Form.Circle or Form.Brush;

    /// <summary>Whether its blend has an outer edge of its own to outline: a rectangle's or a circle's.</summary>
    internal bool HasBlendEdge => _form is Form.Rectangle or Form.Circle && Blend > 0;

    /// <summary>
    /// A rectangle on the ground: <paramref name="width"/> along x and <paramref name="depth"/> along z, each from just over 0
    /// to <see cref="MaximumSide"/> metres, turned by <paramref name="rotation"/> degrees about <paramref name="center"/>, its
    /// edge blending into the ground over <paramref name="blend"/> metres (0 to <see cref="MaximumBlend"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size, the turn or the blend is out of range or not a number.</exception>
    public static LandscapeArea Rectangle(Vector3 center, float width, float depth, float rotation = 0, float blend = 1, LandscapeFalloff falloff = LandscapeFalloff.Smooth)
    {
        Check(center);
        CheckSide(width, nameof(width));
        CheckSide(depth, nameof(depth));
        CheckTurn(rotation);
        CheckBlend(blend);
        CheckFalloff(falloff);
        return new LandscapeArea(Form.Rectangle, center, width, depth, rotation, blend, null, falloff);
    }

    /// <summary>One of the game's brushes at <paramref name="center"/>, as its tools use them.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The centre is not a number, or the brush is none of the game's.</exception>
    public static LandscapeArea Point(Vector3 center, LandscapeBrush brush = LandscapeBrush.Medium)
    {
        Check(center);
        float size = SizeOf(brush);
        return new LandscapeArea(Form.Brush, center, size, size, 0, 0, brush, brush == LandscapeBrush.Medium ? LandscapeFalloff.Linear : LandscapeFalloff.Smooth);
    }

    /// <summary>
    /// A circle of <paramref name="radius"/> metres round <paramref name="center"/> (over 0, at most half of
    /// <see cref="MaximumSide"/>), its edge blending into the ground over <paramref name="blend"/> metres outside it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius or the blend is out of range or not a number.</exception>
    public static LandscapeArea Circle(Vector3 center, float radius, float blend = 1, LandscapeFalloff falloff = LandscapeFalloff.Smooth)
    {
        Check(center);
        if (!(radius > 0 && radius <= MaximumSide / 2)) throw new ArgumentOutOfRangeException(nameof(radius), radius, $"A radius is over 0 and at most {MaximumSide / 2} m.");
        CheckBlend(blend);
        CheckFalloff(falloff);
        return new LandscapeArea(Form.Circle, center, 2 * radius, 2 * radius, 0, blend, null, falloff);
    }

    /// <summary>
    /// A polygon on the ground through <paramref name="points"/> in order (3 to <see cref="MaximumPoints"/>, the last joined to
    /// the first; its box at most <see cref="MaximumSide"/> on either side), its edge blending into the ground over
    /// <paramref name="blend"/> metres outside it. A polygon that crosses itself counts its parts inside by turns.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Too few or too many points, one not a number, too large a box, or a blend out of range.</exception>
    public static LandscapeArea Polygon(IEnumerable<Vector3> points, float blend = 1, LandscapeFalloff falloff = LandscapeFalloff.Smooth)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.ToArray();
        if (list.Length < 3 || list.Length > MaximumPoints) throw new ArgumentOutOfRangeException(nameof(points), list.Length, $"A polygon has 3 to {MaximumPoints} points.");
        var xz = new float[list.Length * 2];
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < list.Length; i++)
        {
            Check(list[i]);
            xz[2 * i] = list[i].x;
            xz[2 * i + 1] = list[i].z;
            minX = Math.Min(minX, list[i].x);
            maxX = Math.Max(maxX, list[i].x);
            minZ = Math.Min(minZ, list[i].z);
            maxZ = Math.Max(maxZ, list[i].z);
        }
        if (maxX - minX > MaximumSide || maxZ - minZ > MaximumSide) throw new ArgumentOutOfRangeException(nameof(points), $"A polygon's box is at most {MaximumSide} m on either side.");
        if (maxX - minX <= 0 || maxZ - minZ <= 0) throw new ArgumentOutOfRangeException(nameof(points), "A polygon's points do not lie on one line.");
        CheckBlend(blend);
        CheckFalloff(falloff);
        return new LandscapeArea(Form.Polygon, At((minX + maxX) / 2, (minZ + maxZ) / 2), maxX - minX, maxZ - minZ, 0, blend, null, falloff, xz);
    }

    /// <summary>
    /// A mask of the mod's own: <paramref name="weights"/> from 0 (unchanged) to 1 (changed in full), its rows along z and its
    /// columns along x, spread over <paramref name="width"/> × <paramref name="depth"/> metres round <paramref name="center"/>
    /// and turned by <paramref name="rotation"/> degrees; between its samples the weight is blended. The weights are copied:
    /// a change to the array later does not change the area.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or the turn is out of range, the mask has fewer than 2 or more than
    /// <see cref="MaximumMaskSamples"/> samples along a side, or a weight is not a number.</exception>
    public static LandscapeArea Mask(Vector3 center, float[,] weights, float width, float depth, float rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(weights);
        Check(center);
        CheckSide(width, nameof(width));
        CheckSide(depth, nameof(depth));
        CheckTurn(rotation);
        int rows = weights.GetLength(0), columns = weights.GetLength(1);
        if (rows < 2 || columns < 2 || rows > MaximumMaskSamples || columns > MaximumMaskSamples)
            throw new ArgumentOutOfRangeException(nameof(weights), $"A mask has 2 to {MaximumMaskSamples} samples along each side.");
        var copy = new float[rows, columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                float w = weights[r, c];
                if (!float.IsFinite(w)) throw new ArgumentOutOfRangeException(nameof(weights), "A weight is a number from 0 to 1.");
                copy[r, c] = Math.Clamp(w, 0f, 1f);
            }
        return new LandscapeArea(Form.Mask, center, width, depth, rotation, 0, null, LandscapeFalloff.Linear, mask: copy);
    }

    /// <summary>The brush's size across, in metres, as the game's data covers it.</summary>
    internal static float SizeOf(LandscapeBrush brush) => brush switch
    {
        LandscapeBrush.Small => 2.5f,
        LandscapeBrush.Medium => 3.5f,
        LandscapeBrush.Soft => 3.5f,
        LandscapeBrush.Large => 6.5f,
        _ => throw new ArgumentOutOfRangeException(nameof(brush), brush, "No such brush."),
    };

    /// <summary>The rectangle on the ground the area touches, its blend and turn included: x is the world's x, y the world's z.</summary>
    public Rect Bounds => _form switch
    {
        Form.Circle or Form.Brush or Form.Polygon => BoundsOf(Center.x, Center.z, Width, Depth, 0, Blend),
        _ => BoundsOf(Center.x, Center.z, Width, Depth, Rotation, Blend),
    };

    /// <summary>The same area moved so its middle is at <paramref name="center"/> and turned on by <paramref name="rotation"/> degrees about it.</summary>
    internal LandscapeArea Moved(Vector3 center, float rotation)
    {
        switch (_form)
        {
            case Form.Polygon:
                var xz = new float[_points!.Length];
                for (int i = 0; i < xz.Length; i += 2)
                {
                    var (dx, dz) = Turn(_points[i] - Center.x, _points[i + 1] - Center.z, rotation);
                    xz[i] = center.x + dx;
                    xz[i + 1] = center.z + dz;
                }
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < xz.Length; i += 2)
                {
                    minX = Math.Min(minX, xz[i]);
                    maxX = Math.Max(maxX, xz[i]);
                    minZ = Math.Min(minZ, xz[i + 1]);
                    maxZ = Math.Max(maxZ, xz[i + 1]);
                }
                return new LandscapeArea(Form.Polygon, At((minX + maxX) / 2, (minZ + maxZ) / 2), maxX - minX, maxZ - minZ, 0, Blend, null, Falloff, xz);
            case Form.Circle:
            case Form.Brush:
                return new LandscapeArea(_form, center, Width, Depth, 0, Blend, Brush, Falloff);
            default:
                return new LandscapeArea(_form, center, Width, Depth, Rotation + rotation, Blend, Brush, Falloff, null, _mask);
        }
    }

    /// <summary>
    /// How strongly a change the SDK writes acts at a point: 1 inside the area, falling over its blend, 0 outside.
    /// </summary>
    internal float Weight(float x, float z)
    {
        float dx = x - Center.x, dz = z - Center.z;
        switch (_form)
        {
            case Form.Rectangle:
            {
                var (lx, lz) = Turn(dx, dz, -Rotation);
                float ox = Math.Max(Math.Abs(lx) - Width / 2, 0), oz = Math.Max(Math.Abs(lz) - Depth / 2, 0);
                return Fade(MathF.Sqrt(ox * ox + oz * oz), Blend);
            }
            case Form.Circle:
                return Fade(Math.Max(MathF.Sqrt(dx * dx + dz * dz) - Width / 2, 0), Blend);
            case Form.Brush:
                return Fade(Math.Max(MathF.Sqrt(dx * dx + dz * dz) - Width / 4, 0), Width / 4);
            case Form.Polygon:
                return Inside(x, z) ? 1f : Fade(EdgeDistance(x, z), Blend);
            default:
            {
                var (lx, lz) = Turn(dx, dz, -Rotation);
                return MaskAt(lx, lz);
            }
        }
    }

    private float Fade(float outside, float blend)
    {
        if (outside <= 0) return 1f;
        if (blend <= 0 || outside >= blend) return 0f;
        float t = 1 - outside / blend;
        return Falloff == LandscapeFalloff.Linear ? t : t * t * (3 - 2 * t);
    }

    // Even-odd: a point is inside when a line from it crosses the edges an odd number of times.
    private bool Inside(float x, float z)
    {
        var p = _points!;
        bool inside = false;
        for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
        {
            float xi = p[i], zi = p[i + 1], xj = p[j], zj = p[j + 1];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
        }
        return inside;
    }

    private float EdgeDistance(float x, float z)
    {
        var p = _points!;
        float best = float.MaxValue;
        for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
        {
            float ax = p[j], az = p[j + 1], ex = p[i] - ax, ez = p[i + 1] - az;
            float length = ex * ex + ez * ez;
            float t = length > 0 ? Math.Clamp(((x - ax) * ex + (z - az) * ez) / length, 0f, 1f) : 0f;
            float qx = x - (ax + t * ex), qz = z - (az + t * ez);
            best = Math.Min(best, qx * qx + qz * qz);
        }
        return MathF.Sqrt(best);
    }

    private float MaskAt(float lx, float lz)
    {
        var mask = _mask!;
        int rows = mask.GetLength(0), columns = mask.GetLength(1);
        float u = (lx / Width + 0.5f) * (columns - 1), v = (lz / Depth + 0.5f) * (rows - 1);
        if (!(u >= 0 && v >= 0 && u <= columns - 1 && v <= rows - 1)) return 0f;
        int c0 = Math.Min((int)u, columns - 2), r0 = Math.Min((int)v, rows - 2);
        float fu = u - c0, fv = v - r0;
        float near = mask[r0, c0] + (mask[r0, c0 + 1] - mask[r0, c0]) * fu;
        float far = mask[r0 + 1, c0] + (mask[r0 + 1, c0 + 1] - mask[r0 + 1, c0]) * fu;
        return near + (far - near) * fv;
    }

    /// <summary>A vector on the ground turned by <paramref name="degrees"/> as an area's rotation turns its shape.</summary>
    internal static (float X, float Z) Turn(float x, float z, float degrees)
    {
        double turn = degrees * Math.PI / 180;
        float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);
        return (x * cos - z * sin, x * sin + z * cos);
    }

    /// <summary>The rectangle on the ground a turned rectangle with a blended edge touches (the game's patch uses it too).</summary>
    internal static Rect BoundsOf(float x, float z, float width, float depth, float rotation, float blend)
    {
        double turn = rotation * Math.PI / 180;
        float cos = (float)Math.Abs(Math.Cos(turn)), sin = (float)Math.Abs(Math.Sin(turn));
        float halfX = (width * cos + depth * sin) / 2 + blend, halfZ = (width * sin + depth * cos) / 2 + blend;
        return MakeRect(x - halfX, z - halfZ, 2 * halfX, 2 * halfZ);
    }

    // By the fields: Rect's own setters and constructor run the IL2CPP runtime, which unit tests do not have.
    internal static Rect MakeRect(float x, float z, float width, float depth)
    {
        Rect rect = default;
        rect.m_XMin = x;
        rect.m_YMin = z;
        rect.m_Width = width;
        rect.m_Height = depth;
        return rect;
    }

    // By the fields, as Rect: Vector3's constructor runs the IL2CPP runtime.
    private static Vector3 At(float x, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.z = z;
        return v;
    }

    private static void Check(Vector3 point)
    {
        if (!float.IsFinite(point.x) || !float.IsFinite(point.z)) throw new ArgumentOutOfRangeException(nameof(point), "A point is a point of the world.");
    }

    private static void CheckSide(float side, string name)
    {
        if (!(side > 0 && side <= MaximumSide)) throw new ArgumentOutOfRangeException(name, side, $"A side is over 0 and at most {MaximumSide} m.");
    }

    private static void CheckTurn(float rotation)
    {
        if (!float.IsFinite(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "A turn is a number of degrees.");
    }

    private static void CheckBlend(float blend)
    {
        if (!(blend >= 0 && blend <= MaximumBlend)) throw new ArgumentOutOfRangeException(nameof(blend), blend, $"A blend is from 0 to {MaximumBlend} m.");
    }

    private static void CheckFalloff(LandscapeFalloff falloff)
    {
        if (!Enum.IsDefined(falloff)) throw new ArgumentOutOfRangeException(nameof(falloff), falloff, "No such falloff.");
    }

    /// <inheritdoc/>
    public bool Equals(LandscapeArea other) =>
        _form == other._form && Center.x == other.Center.x && Center.y == other.Center.y && Center.z == other.Center.z && Width == other.Width &&
        Depth == other.Depth && Rotation == other.Rotation && Blend == other.Blend && Brush == other.Brush && Falloff == other.Falloff &&
        ReferenceEquals(_mask, other._mask) && (ReferenceEquals(_points, other._points) || (_points != null && other._points != null && _points.AsSpan().SequenceEqual(other._points)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LandscapeArea other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_form, Center.x, Center.z, Width, Depth, Rotation, Blend, Brush);

    /// <summary>Whether two areas are the same.</summary>
    public static bool operator ==(LandscapeArea left, LandscapeArea right) => left.Equals(right);

    /// <summary>Whether two areas differ.</summary>
    public static bool operator !=(LandscapeArea left, LandscapeArea right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => _form switch
    {
        Form.Brush => FormattableString.Invariant($"{Brush} brush at ({Center.x:0.##}, {Center.z:0.##})"),
        Form.Circle => FormattableString.Invariant($"circle of {Width / 2:0.##} m at ({Center.x:0.##}, {Center.z:0.##}), blend {Blend:0.##} m"),
        Form.Polygon => FormattableString.Invariant($"polygon of {_points!.Length / 2} points, {Width:0.##} × {Depth:0.##} m at ({Center.x:0.##}, {Center.z:0.##}), blend {Blend:0.##} m"),
        Form.Mask => FormattableString.Invariant($"mask of {_mask!.GetLength(1)} × {_mask.GetLength(0)}, {Width:0.##} × {Depth:0.##} m at ({Center.x:0.##}, {Center.z:0.##}), turned {Rotation:0.#}°"),
        _ => FormattableString.Invariant($"{Width:0.##} × {Depth:0.##} m at ({Center.x:0.##}, {Center.z:0.##}), turned {Rotation:0.#}°, blend {Blend:0.##} m"),
    };
}
