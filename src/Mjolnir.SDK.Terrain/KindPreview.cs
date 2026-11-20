using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A kind's model standing on the ground where an object of it would be placed (<see cref="NaturalObjects.ShowPreview"/>):
/// the ghost a tool shows under the cursor before a click places it. It is only a picture — nothing to harvest, collide with
/// or save — drawn on this machine alone. Its properties may be set from any thread; they show from the next frame. It is
/// hidden while no world is loaded or its place is not loaded, and goes when disposed or when its mod is disabled.
/// </summary>
public sealed class KindPreview : IDisposable
{
    private static readonly List<KindPreview> s_all = [];
    private static readonly object s_gate = new();

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private string _kind;
    private Vector3 _position;
    private float _rotation;
    private float _scale = 1f;
    private bool _blocked;
    private bool _visible = true;
    private bool _disposed;
    private IKindPreviewView? _view;

    private KindPreview(Assembly owner, string kind, Vector3 position)
    {
        _owner = owner;
        _kind = kind;
        _position = position;
    }

    /// <summary>The kind shown (its <see cref="PlaceableKind.Name"/>); setting it shows another.</summary>
    /// <exception cref="ArgumentException">No such kind.</exception>
    public string Kind
    {
        get { lock (_lock) return _kind; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var found = NaturalObjects.FindKind(value) ?? throw new ArgumentException($"No kind '{value}' (NaturalObjects.Kinds).", nameof(value));
            Set(ref _kind, found.Name);
        }
    }

    /// <summary>Where it stands; its y is not used, the model stands on the ground there.</summary>
    public Vector3 Position
    {
        get { lock (_lock) return _position; }
        set => Set(ref _position, value);
    }

    /// <summary>Its turn round the vertical, in degrees, as <see cref="NaturalObjects.Place"/> takes it.</summary>
    public float Rotation
    {
        get { lock (_lock) return _rotation; }
        set => Set(ref _rotation, value);
    }

    /// <summary>Its size against the kind's own, over 0 and at most <see cref="NaturalObjects.MaximumScale"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Out of range.</exception>
    public float Scale
    {
        get { lock (_lock) return _scale; }
        set
        {
            if (!(value > 0 && value <= NaturalObjects.MaximumScale)) throw new ArgumentOutOfRangeException(nameof(value), value, $"A scale is over 0 and at most {NaturalObjects.MaximumScale}.");
            Set(ref _scale, value);
        }
    }

    /// <summary>Whether it cannot be placed there: the model is drawn plain red instead of in its own look.</summary>
    public bool IsBlocked
    {
        get { lock (_lock) return _blocked; }
        set => Set(ref _blocked, value);
    }

    /// <summary>Whether it is shown; a hidden preview keeps its place to be shown again.</summary>
    public bool IsVisible
    {
        get { lock (_lock) return _visible; }
        set => Set(ref _visible, value);
    }

    /// <summary>Takes the preview away for good (on the next frame).</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"preview of {Kind}";

    private void Set<T>(ref T field, T value)
    {
        lock (_lock)
        {
            field = value;
        }
    }

    internal static KindPreview Add(Assembly owner, string kind, Vector3 position)
    {
        var preview = new KindPreview(owner, kind, position);
        lock (s_gate) s_all.Add(preview);
        return preview;
    }

    /// <summary>The previews shown now, on the main thread: <paramref name="source"/> is null while no world is loaded.</summary>
    internal static void Tick(ITerrainSource? source)
    {
        KindPreview[] all;
        lock (s_gate)
        {
            if (s_all.Count == 0) return;
            all = s_all.ToArray();
        }
        foreach (var preview in all)
        {
            if (preview.Update(source)) continue;
            lock (s_gate) s_all.Remove(preview);
        }
    }

    // Draws, hides or takes away the preview; false once it is gone.
    private bool Update(ITerrainSource? source)
    {
        string kind;
        Vector3 position;
        float rotation, scale;
        bool blocked, visible;
        lock (_lock)
        {
            if (_disposed || ModCall.IsDisabled(_owner))
            {
                _view?.Destroy();
                _view = null;
                return false;
            }
            // The ground under it may change while it stands: it is placed again every frame it is shown.
            kind = _kind;
            position = _position;
            rotation = _rotation;
            scale = _scale;
            blocked = _blocked;
            visible = _visible;
        }
        try
        {
            if (source == null || !visible || !source.IsLoaded(position.x, position.z) || !source.TryGetHeight(position.x, position.z, out position.y))
            {
                _view?.Hide();
                return true;
            }
            _view ??= source.NewKindPreview();
            _view.Show(kind, position, rotation, scale, blocked);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be drawn: {e.Message}");
            lock (_lock) _disposed = true;
        }
        return true;
    }

    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}
