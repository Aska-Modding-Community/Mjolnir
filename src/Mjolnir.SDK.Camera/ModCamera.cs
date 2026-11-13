using System;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// A camera of a mod's own over the game's (<see cref="GameCamera.Take"/>): while it is there and no camera of a higher
/// priority is, the screen shows what it sees, and the player's camera waits behind it. It starts where the player's camera
/// is. Move it each frame (<see cref="Position"/>, <see cref="Rotation"/>), or let it follow a thing and look at one
/// (<see cref="Follow"/>, <see cref="LookAt"/>). Dispose it to give the view back; it also ends when its mod is disabled
/// after an error or the world is left (<see cref="Ended"/>). The player still walks, aims and builds as ever: the game reads
/// input whatever the screen shows. Set it on the main thread.
/// </summary>
public sealed class ModCamera : IDisposable
{
    private ModHandlers<EventHandler>? _ended;
    private volatile bool _disposed;
    private Vector3 _position;
    private Quaternion _rotation;
    private float _fieldOfView;

    internal ModCamera(Assembly owner, int priority, TimeSpan blend, long sequence)
    {
        Owner = owner;
        Priority = priority;
        Blend = blend;
        Sequence = sequence;
    }

    /// <summary>Its place among the mods' cameras: the highest is shown (of two alike, the newer).</summary>
    public int Priority { get; }

    /// <summary>How long the view takes to go over to it and back (0: at once).</summary>
    public TimeSpan Blend { get; }

    /// <summary>Where it stands in the world (each frame from <see cref="Follow"/> while it follows a thing).</summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            Unset &= ~Start.Position;
        }
    }

    /// <summary>Where it looks (each frame towards <see cref="LookAt"/> while it looks at a thing).</summary>
    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value;
            Unset &= ~Start.Rotation;
        }
    }

    /// <summary>Its vertical field of view in degrees, from 1 to 179; at first the player's camera's.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            _fieldOfView = value;
            Unset &= ~Start.FieldOfView;
        }
    }

    /// <summary>A thing it follows: it stands at the thing's place plus <see cref="FollowOffset"/> each frame. Null: it stays.</summary>
    public Transform? Follow { get; set; }

    /// <summary>Where it stands from what it follows, in the world's axes.</summary>
    public Vector3 FollowOffset { get; set; }

    /// <summary>A thing it looks at each frame. Null: it keeps <see cref="Rotation"/>.</summary>
    public Transform? LookAt { get; set; }

    /// <summary>Whether it is there: not disposed, its mod not disabled, the world not left.</summary>
    public bool IsActive => !_disposed && !HasEnded && !ModCall.IsDisabled(Owner);

    /// <summary>Whether the screen shows it now (no camera of a higher priority is there).</summary>
    public bool IsShown { get; internal set; }

    /// <summary>Raised once when it ends, whatever the reason: disposed, its mod disabled, the world left.</summary>
    public event EventHandler? Ended
    {
        add => ModHandlers<EventHandler>.Of(ref _ended, "ModCamera.Ended").Add(value);
        remove => _ended?.Remove(value);
    }

    internal Assembly Owner { get; }

    internal long Sequence { get; }

    /// <summary>What it still takes from the player's camera on its first frame: what the mod has not set since.</summary>
    internal Start Unset { get; set; }

    /// <summary>Starts it where the player's camera is, in what the mod has not set itself.</summary>
    internal void StartAt(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        if ((Unset & Start.Position) != 0) _position = position;
        if ((Unset & Start.Rotation) != 0) _rotation = rotation;
        if ((Unset & Start.FieldOfView) != 0) _fieldOfView = fieldOfView;
        Unset = Start.None;
    }

    [Flags]
    internal enum Start
    {
        None = 0,
        Position = 1,
        Rotation = 2,
        FieldOfView = 4,
        All = Position | Rotation | FieldOfView,
    }

    /// <summary>Set once it ended, so it never comes back.</summary>
    internal bool HasEnded { get; private set; }

    /// <summary>Gives the view back to the game's camera, blended over <see cref="Blend"/>. Calling it again does nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; // ended on the next frame, on the main thread (its handlers, the blend back)
    }

    /// <summary>Ends it and tells its handlers (main thread).</summary>
    internal void End()
    {
        if (HasEnded) return;
        HasEnded = true;
        IsShown = false;
        _ended?.Raise(h => h(this, EventArgs.Empty));
    }

    /// <inheritdoc/>
    public override string ToString() => $"camera of {Owner.GetName().Name} (priority {Priority})";
}
