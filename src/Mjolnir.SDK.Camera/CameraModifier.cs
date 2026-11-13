using System;
using System.Reflection;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// A mod's change to the game's cameras (<see cref="GameCamera.AddModifier"/>), run every frame after the game has set them.
/// Dispose it to take it away; the game's values come back on the next frame. A mod disabled after an error loses its
/// modifiers the same way.
/// </summary>
public sealed class CameraModifier : IDisposable
{
    private volatile bool _disposed;

    internal CameraModifier(Assembly owner, Action<CameraView> modify, int priority, long sequence)
    {
        Owner = owner;
        Modify = modify;
        Priority = priority;
        Sequence = sequence;
    }

    /// <summary>Its place among the modifiers: a higher one runs later, so it has the last word.</summary>
    public int Priority { get; }

    /// <summary>Whether it still changes the cameras: not disposed, and its mod not disabled.</summary>
    public bool IsActive => !_disposed && !ModCall.IsDisabled(Owner);

    internal Assembly Owner { get; }

    internal Action<CameraView> Modify { get; }

    internal long Sequence { get; }

    /// <summary>Takes the change away. Calling it again does nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CameraDirector.Removed(this);
    }

    /// <inheritdoc/>
    public override string ToString() => $"camera modifier of {Owner.GetName().Name} (priority {Priority})";
}
