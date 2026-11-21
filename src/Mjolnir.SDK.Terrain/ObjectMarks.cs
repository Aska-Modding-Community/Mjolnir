using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Natural objects marked in the world (<see cref="NaturalObjects.ShowMarks"/>): each drawn over in the game's own red, the
/// see-through red it shows on what keeps a building from its place — for a tool, what it is about to take. Only a
/// picture, on this machine alone. <see cref="Set"/> is the main thread's; the marks show from the next frame, follow the
/// objects' places and go when an object is gone, the mark disposed or its mod disabled.
/// </summary>
public sealed class ObjectMarks : IDisposable
{
    /// <summary>The most objects marked at once; the rest of a longer list is left unmarked.</summary>
    public const int MaximumCount = 128;

    private static readonly List<ObjectMarks> s_all = [];
    private static readonly object s_gate = new();

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private NaturalObject[] _objects = [];
    private bool _visible = true;
    private bool _disposed;
    private IMarksView? _view;

    private ObjectMarks(Assembly owner) => _owner = owner;

    /// <summary>How many objects are marked.</summary>
    public int Count
    {
        get { lock (_lock) return _objects.Length; }
    }

    /// <summary>Whether the marks are shown; hidden ones keep their objects.</summary>
    public bool IsVisible
    {
        get { lock (_lock) return _visible; }
        set { lock (_lock) _visible = value; }
    }

    /// <summary>Marks these objects (at most <see cref="MaximumCount"/>) and no others; an empty list takes every mark away.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="objects"/> is null.</exception>
    public void Set(IEnumerable<NaturalObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var list = new List<NaturalObject>();
        foreach (var found in objects)
        {
            if (found == null) continue;
            list.Add(found);
            if (list.Count >= MaximumCount) break;
        }
        lock (_lock) _objects = [.. list];
    }

    /// <summary>Takes every mark away for good (on the next frame).</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }

    internal static ObjectMarks Add(Assembly owner)
    {
        var marks = new ObjectMarks(owner);
        lock (s_gate) s_all.Add(marks);
        return marks;
    }

    /// <summary>The marks shown now, on the main thread: <paramref name="source"/> is null while no world is loaded.</summary>
    internal static void Tick(ITerrainSource? source)
    {
        ObjectMarks[] all;
        lock (s_gate)
        {
            if (s_all.Count == 0) return;
            all = s_all.ToArray();
        }
        foreach (var marks in all)
        {
            if (marks.Update(source)) continue;
            lock (s_gate) s_all.Remove(marks);
        }
    }

    // Draws, hides or takes the marks away; false once they are gone.
    private bool Update(ITerrainSource? source)
    {
        NaturalObject[] objects;
        bool visible;
        lock (_lock)
        {
            if (_disposed || ModCall.IsDisabled(_owner))
            {
                _view?.Destroy();
                _view = null;
                return false;
            }
            objects = _objects;
            visible = _visible;
        }
        try
        {
            if (source == null || !visible || objects.Length == 0)
            {
                _view?.Hide();
                return true;
            }
            var shown = new List<MarkedObject>(objects.Length);
            foreach (var found in objects)
                if (found.Placement() is { } placement) shown.Add(placement);
            _view ??= source.NewMarks();
            _view.Show(shown);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"Object marks could not be drawn: {e.Message}");
            lock (_lock) _disposed = true;
        }
        return true;
    }

    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}

/// <summary>An object as its mark draws it: its kind's name and where it stands, turned and sized.</summary>
internal readonly record struct MarkedObject(string AssetName, Vector3 Position, Quaternion Rotation, Vector3 Scale);
