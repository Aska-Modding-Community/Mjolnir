using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A grid of cells drawn on the ground (<see cref="Landscape.ShowGrid"/>), in the look of the game's levelling field: each
/// cell framed, see-through, lying on the ground's relief, the grass and the stones. A mod colours each cell to show what a
/// change will do there before it is made: a cell is empty, a shade between <see cref="NearColor"/> and
/// <see cref="FarColor"/> (<see cref="SetCell"/>), or marked in the game's red (<see cref="MarkCell"/>). It stays
/// until disposed or its mod is disabled after an error, and is hidden while no world is loaded. Its properties and cells
/// may be set from any thread; it is drawn again on the next frame.
/// </summary>
/// <remarks>
/// Columns run along x and rows along z before the grid is turned: cell (0, 0) is the corner at the lowest x and z.
/// </remarks>
public sealed class LandscapeGrid : IDisposable
{
    /// <summary>The most columns or rows a grid has.</summary>
    public const int MaximumCells = 256;

    /// <summary>The smallest cell, in metres.</summary>
    public const float MinimumCellSize = 0.25f;

    /// <summary>The largest cell, in metres.</summary>
    public const float MaximumCellSize = 16f;

    // Frames between two readings of the ground's height under a grid that did not move.
    private const int RefreshFrames = 30;

    private static readonly List<LandscapeGrid> s_all = [];
    private static readonly object s_gate = new();

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private readonly GridCellKind[] _kinds;
    private readonly float[] _fractions;
    private Vector3 _center;
    private float _rotation;
    private Color? _near, _far;
    private bool _visible = true;
    private bool _disposed;
    private bool _placeDirty = true, _cellsDirty = true, _colorsDirty = true;
    private int _age;
    private IGridView? _view;

    private LandscapeGrid(Assembly owner, Vector3 center, int columns, int rows, float cellSize, float rotation)
    {
        _owner = owner;
        _center = center;
        Columns = columns;
        Rows = rows;
        CellSize = cellSize;
        _rotation = rotation;
        _kinds = new GridCellKind[columns * rows];
        _fractions = new float[columns * rows];
    }

    /// <summary>How many cells it has along x before it is turned.</summary>
    public int Columns { get; }

    /// <summary>How many cells it has along z before it is turned.</summary>
    public int Rows { get; }

    /// <summary>The side of a cell, in metres.</summary>
    public float CellSize { get; }

    /// <summary>The middle of the grid (its y is not used); setting it moves the grid.</summary>
    public Vector3 Center
    {
        get { lock (_lock) return _center; }
        set
        {
            CheckFinite(value.x, nameof(value));
            CheckFinite(value.z, nameof(value));
            lock (_lock)
            {
                _center = value;
                _placeDirty = true;
            }
        }
    }

    /// <summary>How far it is turned about the vertical, in degrees, as a <see cref="LandscapeArea.Rectangle"/> is.</summary>
    public float Rotation
    {
        get { lock (_lock) return _rotation; }
        set
        {
            CheckFinite(value, nameof(value));
            lock (_lock)
            {
                _rotation = value;
                _placeDirty = true;
            }
        }
    }

    /// <summary>Whether it is shown; a hidden grid keeps its cells to be shown again.</summary>
    public bool IsVisible
    {
        get { lock (_lock) return _visible; }
        set
        {
            lock (_lock)
            {
                _visible = value;
                _placeDirty = true;
            }
        }
    }

    /// <summary>The colour of a cell set at 0 (<see cref="SetCell"/>); null for the game's own, the levelling field's green.</summary>
    public Color? NearColor
    {
        get { lock (_lock) return _near; }
        set
        {
            lock (_lock)
            {
                _near = value;
                _colorsDirty = true;
            }
        }
    }

    /// <summary>The colour of a cell set at 1 (<see cref="SetCell"/>); null for the game's own, a dark green.</summary>
    public Color? FarColor
    {
        get { lock (_lock) return _far; }
        set
        {
            lock (_lock)
            {
                _far = value;
                _colorsDirty = true;
            }
        }
    }

    /// <summary>
    /// Shows a cell in a shade between <see cref="NearColor"/> (at 0) and <see cref="FarColor"/> (at 1): how far the ground
    /// there is from what is wanted, how much a change moves it.
    /// </summary>
    /// <param name="column">From 0 to <see cref="Columns"/> − 1.</param>
    /// <param name="row">From 0 to <see cref="Rows"/> − 1.</param>
    /// <param name="fraction">From 0 to 1; a value outside is kept within.</param>
    /// <exception cref="ArgumentOutOfRangeException">No such cell, or the fraction is not a number.</exception>
    public void SetCell(int column, int row, float fraction)
    {
        if (float.IsNaN(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction), "A cell's fraction is a number.");
        Set(column, row, GridCellKind.Shade, Math.Clamp(fraction, 0f, 1f));
    }

    /// <summary>
    /// Shows a cell in the game's red, as its levelling field shows ground it cannot level: ground a change cannot reach,
    /// ground to cut, too steep. The red is the game's own and cannot be changed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">No such cell.</exception>
    public void MarkCell(int column, int row) => Set(column, row, GridCellKind.Mark, 0);

    /// <summary>Leaves a cell empty: only its frame shows.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No such cell.</exception>
    public void ClearCell(int column, int row) => Set(column, row, GridCellKind.Empty, 0);

    /// <summary>Leaves every cell empty.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_kinds);
            Array.Clear(_fractions);
            _cellsDirty = true;
        }
    }

    /// <summary>The middle of a cell on the ground (y 0), the grid's turn included.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No such cell.</exception>
    public Vector3 CellCenter(int column, int row)
    {
        CheckCell(column, row);
        Vector3 center;
        float rotation;
        lock (_lock)
        {
            center = _center;
            rotation = _rotation;
        }
        float lx = (column + 0.5f - Columns / 2f) * CellSize, lz = (row + 0.5f - Rows / 2f) * CellSize;
        var (dx, dz) = rotation != 0 ? LandscapeArea.Turn(lx, lz, rotation) : (lx, lz);
        Vector3 v = default;
        v.x = center.x + dx;
        v.z = center.z + dz;
        return v;
    }

    /// <summary>Takes the grid off the ground for good (on the next frame).</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"grid of {Columns} × {Rows} cells of {CellSize:0.##} m";

    internal static LandscapeGrid Add(Assembly owner, Vector3 center, int columns, int rows, float cellSize, float rotation)
    {
        if (columns < 1 || columns > MaximumCells) throw new ArgumentOutOfRangeException(nameof(columns), columns, $"A grid has 1 to {MaximumCells} columns.");
        if (rows < 1 || rows > MaximumCells) throw new ArgumentOutOfRangeException(nameof(rows), rows, $"A grid has 1 to {MaximumCells} rows.");
        if (!(cellSize >= MinimumCellSize && cellSize <= MaximumCellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, $"A cell is {MinimumCellSize} to {MaximumCellSize} m.");
        CheckFinite(center.x, nameof(center));
        CheckFinite(center.z, nameof(center));
        CheckFinite(rotation, nameof(rotation));
        var grid = new LandscapeGrid(owner, center, columns, rows, cellSize, rotation);
        lock (s_gate) s_all.Add(grid);
        return grid;
    }

    /// <summary>The grids shown now, on the main thread: <paramref name="source"/> is null while no world is loaded.</summary>
    internal static void Tick(ITerrainSource? source)
    {
        LandscapeGrid[] all;
        lock (s_gate)
        {
            if (s_all.Count == 0) return;
            all = s_all.ToArray();
        }
        foreach (var grid in all)
        {
            if (grid.Update(source)) continue;
            lock (s_gate) s_all.Remove(grid);
        }
    }

    // Draws, hides or takes away the grid; false once it is gone.
    private bool Update(ITerrainSource? source)
    {
        Vector3 center;
        float rotation;
        bool visible, place, cells, colors;
        GridCellKind[]? kinds = null;
        float[]? fractions = null;
        Color? near, far;
        lock (_lock)
        {
            if (_disposed || ModCall.IsDisabled(_owner))
            {
                _view?.Destroy();
                _view = null;
                return false;
            }
            center = _center;
            rotation = _rotation;
            visible = _visible;
            near = _near;
            far = _far;
            bool fresh = _view == null;
            place = _placeDirty || fresh || ++_age >= RefreshFrames;
            cells = _cellsDirty || fresh;
            colors = _colorsDirty || fresh;
            if (cells)
            {
                kinds = (GridCellKind[])_kinds.Clone();
                fractions = (float[])_fractions.Clone();
            }
            _placeDirty = _cellsDirty = _colorsDirty = false;
            if (place) _age = 0;
        }
        try
        {
            if (source == null || !visible)
            {
                _view?.Hide();
                if (source == null) Redraw();
                return true;
            }
            if (!place && !cells && !colors) return true;
            if (!source.IsLoaded(center.x, center.z) || !source.TryGetHeight(center.x, center.z, out float ground))
            {
                _view?.Hide();
                lock (_lock) _placeDirty = true;
                return true;
            }
            _view ??= source.NewGrid(Columns, Rows);
            if (colors) _view.Colors(near, far);
            if (cells) _view.Paint(kinds!, fractions!);
            if (place)
            {
                center.y = ground;
                _view.Place(center, rotation, CellSize);
            }
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be drawn: {e.Message}");
            lock (_lock) _disposed = true;
        }
        return true;
    }

    // Everything drawn again once a world is back (its view was hidden, its objects may have gone with the scene).
    private void Redraw()
    {
        lock (_lock) _placeDirty = _cellsDirty = _colorsDirty = true;
    }

    private void Set(int column, int row, GridCellKind kind, float fraction)
    {
        CheckCell(column, row);
        int i = row * Columns + column;
        lock (_lock)
        {
            if (_kinds[i] == kind && _fractions[i] == fraction) return;
            _kinds[i] = kind;
            _fractions[i] = fraction;
            _cellsDirty = true;
        }
    }

    private void CheckCell(int column, int row)
    {
        if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column), column, $"The grid has columns 0 to {Columns - 1}.");
        if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row), row, $"The grid has rows 0 to {Rows - 1}.");
    }

    private static void CheckFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name, value, "Not a number.");
    }

    /// <summary>Forgets every grid, between unit tests.</summary>
    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}

/// <summary>What a cell of a <see cref="LandscapeGrid"/> shows, as the model hands it to the game side.</summary>
internal enum GridCellKind : byte
{
    Empty,
    Shade,
    Mark,
}
