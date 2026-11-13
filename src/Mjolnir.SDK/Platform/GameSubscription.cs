using System;
using System.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// Undoes a subscription to a game object (a delegate added to its event or field) once, on the first dispose; a fault (the
/// game object is gone) is logged under the module's log source, never thrown. Part of the module contract.
/// </summary>
internal sealed class GameSubscription(string source, Action undo) : IDisposable
{
    private Action? _undo = undo;

    /// <summary>A subscription with nothing to undo.</summary>
    public static GameSubscription None { get; } = new("SDK", static () => { });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _undo, null) is { } undo) GameGuard.Run(source, "Unsubscribing from the game", undo);
    }
}
