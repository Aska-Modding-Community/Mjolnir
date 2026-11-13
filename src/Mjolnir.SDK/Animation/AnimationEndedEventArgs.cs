using System;

namespace Mjolnir.SDK;

/// <summary>Raised when an animation ends: it played to its end, or it was stopped (by a mod, another animation, the character leaving).</summary>
public sealed class AnimationEndedEventArgs(bool wasStopped) : EventArgs
{
    /// <summary>True when it did not play to its end.</summary>
    public bool WasStopped { get; } = wasStopped;
}
