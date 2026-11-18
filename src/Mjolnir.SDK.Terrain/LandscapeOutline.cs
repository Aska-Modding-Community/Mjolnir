using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// An area's outline drawn on the ground (<see cref="Landscape.ShowOutline"/>): a preview of where a change will be made. It
/// follows the ground's heights and stays until it is disposed, or its mod is disabled after an error. Its properties may be
/// set from any thread; it is drawn again on the next frame.
/// </summary>
public sealed class LandscapeOutline : IDisposable
{
    /// <summary>How high over the ground the line runs by default, in metres, so the grass does not hide it.</summary>
    internal const float Lift = 0.3f;

    /// <summary>How wide the edge's line is by default, in metres.</summary>
    internal const float EdgeWidth = 0.2f;

    /// <summary>The highest a line runs over the ground, in metres.</summary>
    public const float MaximumHeight = 10f;

    /// <summary>The widest line, in metres.</summary>
    public const float MaximumLineWidth = 2f;

    /// <summary>The most metres between two points of a line, so it follows the ground between them.</summary>
    internal const float PointSpacing = 1f;

    // Frames between two readings of the ground's heights under a line that did not move (it follows changes of the ground).
    private const int RefreshFrames = 30;

    private static readonly List<LandscapeOutline> s_all = [];
    private static readonly object s_gate = new();

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private LandscapeArea _area;
    private Color _color;
    private float _height = Lift;
    private float _lineWidth = EdgeWidth;
    private bool _visible = true;
    private bool _disposed;
    private bool _dirty = true;
    private int _age;
    private IOutlineView? _view;

    private LandscapeOutline(Assembly owner, LandscapeArea area, Color color)
    {
        _owner = owner;
        _area = area;
        _color = color;
    }

    /// <summary>The area outlined; setting it moves the outline.</summary>
    public LandscapeArea Area
    {
        get { lock (_lock) return _area; }
        set
        {
            lock (_lock)
            {
                _area = value;
                _dirty = true;
            }
        }
    }

    /// <summary>The line's colour.</summary>
    public Color Color
    {
        get { lock (_lock) return _color; }
        set
        {
            lock (_lock)
            {
                _color = value;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// How high over the ground the line runs, in metres, from 0 to <see cref="MaximumHeight"/>; 0.3 by default, so the
    /// grass does not hide it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Out of range or not a number.</exception>
    public float Height
    {
        get { lock (_lock) return _height; }
        set
        {
            if (!(value >= 0 && value <= MaximumHeight)) throw new ArgumentOutOfRangeException(nameof(value), value, $"An outline runs 0 to {MaximumHeight} m over the ground.");
            lock (_lock)
            {
                _height = value;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// How wide the edge's line is, in metres, over 0 and at most <see cref="MaximumLineWidth"/>; 0.2 by default. The blend's
    /// line is 0.4 of it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Out of range or not a number.</exception>
    public float LineWidth
    {
        get { lock (_lock) return _lineWidth; }
        set
        {
            if (!(value > 0 && value <= MaximumLineWidth)) throw new ArgumentOutOfRangeException(nameof(value), value, $"An outline's line is over 0 and at most {MaximumLineWidth} m wide.");
            lock (_lock)
            {
                _lineWidth = value;
                _dirty = true;
            }
        }
    }

    /// <summary>Whether it is shown; a hidden outline keeps its area and colour to be shown again.</summary>
    public bool IsVisible
    {
        get { lock (_lock) return _visible; }
        set
        {
            lock (_lock)
            {
                _visible = value;
                _dirty = true;
            }
        }
    }

    /// <summary>Takes the outline off the ground for good (on the next frame).</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"outline of {Area}";

    internal static LandscapeOutline Add(Assembly owner, LandscapeArea area, Color color)
    {
        var outline = new LandscapeOutline(owner, area, color);
        lock (s_gate) s_all.Add(outline);
        return outline;
    }

    /// <summary>The outlines shown now, on the main thread: <paramref name="source"/> is null while no world is loaded.</summary>
    internal static void Tick(ITerrainSource? source)
    {
        LandscapeOutline[] all;
        lock (s_gate)
        {
            if (s_all.Count == 0) return;
            all = s_all.ToArray();
        }
        foreach (var outline in all)
        {
            if (outline.Update(source)) continue;
            lock (s_gate) s_all.Remove(outline);
        }
    }

    // Draws, hides or takes away the outline; false once it is gone.
    private bool Update(ITerrainSource? source)
    {
        LandscapeArea area;
        Color color;
        float height, width;
        bool visible, dirty;
        lock (_lock)
        {
            if (_disposed || ModCall.IsDisabled(_owner))
            {
                _view?.Destroy();
                _view = null;
                return false;
            }
            area = _area;
            color = _color;
            height = _height;
            width = _lineWidth;
            visible = _visible;
            dirty = _dirty || ++_age >= RefreshFrames;
            _dirty = false;
            if (dirty) _age = 0;
        }
        try
        {
            if (source == null || !visible)
            {
                _view?.Hide();
                if (source == null) lock (_lock) _dirty = true;
                return true;
            }
            if (!dirty) return true;
            var edge = OnGround(source, Line(area, false)!, height);
            if (edge.Count < 2)
            {
                _view?.Hide();
                return true;
            }
            var blend = Line(area, true) is { } outer ? OnGround(source, outer, height) : null;
            _view ??= source.NewOutline();
            _view.Draw(edge, blend is { Count: >= 2 } ? blend : null, color, width);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be drawn: {e.Message}");
            lock (_lock) _disposed = true;
        }
        return true;
    }

    private static List<Vector3> OnGround(ITerrainSource source, List<(float X, float Z)> line, float lift)
    {
        var points = new List<Vector3>(line.Count);
        foreach (var (x, z) in line)
        {
            if (!source.IsLoaded(x, z) || !source.TryGetHeight(x, z, out float height)) continue;
            Vector3 v = default;
            v.x = x;
            v.y = height + lift;
            v.z = z;
            points.Add(v);
        }
        return points;
    }

    /// <summary>
    /// The closed line round the area (the last point joins the first) on the ground, a point at most
    /// <see cref="PointSpacing"/> from the next: its edge, or with <paramref name="blend"/> the outer edge of a rectangle's or
    /// a circle's blend (null for other areas and for no blend). A brush's edge is its reach; a mask's its rectangle.
    /// </summary>
    internal static List<(float X, float Z)>? Line(LandscapeArea area, bool blend)
    {
        float grow = blend ? area.Blend : 0;
        if (blend && !area.HasBlendEdge) return null;
        var local = new List<(float X, float Z)>();
        if (area.Points.Count > 0)
        {
            foreach (var p in area.Points) local.Add((p.x - area.Center.x, p.z - area.Center.z));
        }
        else if (area.IsRound)
        {
            float radius = area.Width / 2 + grow;
            int count = Math.Clamp((int)MathF.Ceiling(2 * MathF.PI * radius / PointSpacing), 16, 256);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2 * MathF.PI / count;
                local.Add((MathF.Cos(a) * radius, MathF.Sin(a) * radius));
            }
            return Placed(local, area, 0);
        }
        else
        {
            float hx = area.Width / 2, hz = area.Depth / 2;
            (float X, float Z)[] corners = [(hx, hz), (-hx, hz), (-hx, -hz), (hx, -hz)];
            for (int k = 0; k < 4; k++)
            {
                var (cx, cz) = corners[k];
                if (grow <= 0)
                {
                    local.Add((cx, cz));
                    continue;
                }
                // A quarter circle round each corner: the blend's edge lies at its width from the rectangle.
                float start = k * MathF.PI / 2;
                const int arc = 6;
                for (int i = 0; i <= arc; i++)
                {
                    float a = start + i * (MathF.PI / 2) / arc;
                    local.Add((cx + MathF.Cos(a) * grow, cz + MathF.Sin(a) * grow));
                }
            }
        }
        return Placed(Subdivided(local), area, area.Points.Count > 0 ? 0 : area.Rotation);
    }

    private static List<(float X, float Z)> Subdivided(List<(float X, float Z)> points)
    {
        var line = new List<(float X, float Z)>();
        for (int i = 0; i < points.Count; i++)
        {
            var (ax, az) = points[i];
            var (bx, bz) = points[(i + 1) % points.Count];
            float length = MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Math.Max(1, (int)MathF.Ceiling(length / PointSpacing));
            for (int s = 0; s < steps; s++) line.Add((ax + (bx - ax) * s / steps, az + (bz - az) * s / steps));
        }
        return line;
    }

    private static List<(float X, float Z)> Placed(List<(float X, float Z)> local, LandscapeArea area, float rotation)
    {
        var placed = new List<(float X, float Z)>(local.Count);
        foreach (var (x, z) in local)
        {
            var (dx, dz) = rotation != 0 ? LandscapeArea.Turn(x, z, rotation) : (x, z);
            placed.Add((area.Center.x + dx, area.Center.z + dz));
        }
        return placed;
    }

    /// <summary>Forgets every outline, between unit tests.</summary>
    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}
