using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A point round which the game keeps the world's items up as it does round the character
/// (<see cref="NaturalObjects.KeepActive"/>): kinds placed by mods, the trees the villagers plant, what lies on the
/// ground, and the objects' colliders. The game brings them up only near what it tracks, the character alone by default,
/// so a mod's camera far from the character sees none of them; the world's own trees and rocks are drawn round the camera
/// anyway. On this machine alone; <see cref="Center"/> may be moved from any thread and is applied on the next frame. It
/// stays until disposed, its mod is disabled, and is kept through a change of world.
/// </summary>
public sealed class ActivationPoint : IDisposable
{
    private static readonly List<ActivationPoint> s_all = [];
    private static readonly object s_gate = new();

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private Vector3 _center;
    private bool _disposed;
    private bool _active;
    private IActivationView? _view;

    private ActivationPoint(Assembly owner, Vector3 center)
    {
        _owner = owner;
        _center = center;
    }

    /// <summary>The point round which the world's items are kept up.</summary>
    public Vector3 Center
    {
        get { lock (_lock) return _center; }
        set { lock (_lock) _center = value; }
    }

    /// <summary>Whether the game keeps the world up round the point now: false without a loaded world or once disposed.</summary>
    public bool IsActive
    {
        get { lock (_lock) return _active; }
    }

    /// <summary>Lets the game forget the point (on the next frame); what only it kept up goes down again.</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }

    internal static ActivationPoint Add(Assembly owner, Vector3 center)
    {
        var point = new ActivationPoint(owner, center);
        lock (s_gate) s_all.Add(point);
        return point;
    }

    /// <summary>The points kept, on the main thread: <paramref name="source"/> is null while no world is loaded.</summary>
    internal static void Tick(ITerrainSource? source)
    {
        ActivationPoint[] all;
        lock (s_gate)
        {
            if (s_all.Count == 0) return;
            all = s_all.ToArray();
        }
        foreach (var point in all)
        {
            if (point.Update(source)) continue;
            lock (s_gate) s_all.Remove(point);
        }
    }

    // Registers, moves or forgets the point; false once it is gone.
    private bool Update(ITerrainSource? source)
    {
        Vector3 center;
        lock (_lock)
        {
            if (_disposed || ModCall.IsDisabled(_owner))
            {
                _view?.Destroy();
                _view = null;
                _active = false;
                return false;
            }
            center = _center;
        }
        try
        {
            if (source == null)
            {
                // The world's tracker goes with the world: the next one is given the point anew.
                _view?.Destroy();
                _view = null;
                lock (_lock) _active = false;
                return true;
            }
            _view ??= source.NewActivationPoint();
            _view.MoveTo(center);
            bool active = _view.IsActive;
            lock (_lock) _active = active;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"An activation point could not be kept: {e.Message}");
            _view?.Destroy();
            _view = null;
            lock (_lock)
            {
                _active = false;
                _disposed = true;
            }
        }
        return true;
    }

    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}
