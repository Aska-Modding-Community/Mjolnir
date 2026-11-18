using System;
using System.ComponentModel;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A change to the ground about to be made on this machine (<see cref="Landscape.GroundChanging"/>). Setting
/// <see cref="CancelEventArgs.Cancel"/> leaves the ground as it is.
/// </summary>
public sealed class LandscapeChangingEventArgs : CancelEventArgs
{
    internal LandscapeChangingEventArgs(Rect area, LandscapeOperation operation, LandscapeSource source, string? modId)
    {
        Area = area;
        Operation = operation;
        Source = source;
        ModId = modId;
    }

    /// <summary>The ground it touches, about: x is the world's x, y the world's z.</summary>
    public Rect Area { get; }

    /// <summary>What it does.</summary>
    public LandscapeOperation Operation { get; }

    /// <summary>Who makes it.</summary>
    public LandscapeSource Source { get; }

    /// <summary>
    /// The mod whose call makes it — a change through <see cref="Landscape"/>, or a levelling field's tool it sets going
    /// (<see cref="LevellingField.LevelNow"/>) — or null when the game makes it of itself.
    /// </summary>
    public string? ModId { get; }
}

/// <summary>A change to the ground that was made (<see cref="Landscape.GroundChanged"/>), here or on another player's machine.</summary>
public sealed class LandscapeChangedEventArgs : EventArgs
{
    internal LandscapeChangedEventArgs(Rect area, LandscapeOperation? operation, LandscapeSource source, string? modId)
    {
        Area = area;
        Operation = operation;
        Source = source;
        ModId = modId;
    }

    /// <summary>The ground that changed: x is the world's x, y the world's z.</summary>
    public Rect Area { get; }

    /// <summary>What was done, or null for a change that came over the network (the game does not send it).</summary>
    public LandscapeOperation? Operation { get; }

    /// <summary>Who made it; <see cref="LandscapeSource.Remote"/> for another player's machine.</summary>
    public LandscapeSource Source { get; }

    /// <summary>The mod that made it through <see cref="Landscape"/> on this machine, or null.</summary>
    public string? ModId { get; }

    /// <summary>Whether it came from another player's machine.</summary>
    public bool IsRemote => Source == LandscapeSource.Remote;
}
