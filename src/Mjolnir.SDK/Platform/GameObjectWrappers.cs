using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// One wrapper per game object, by its IL2CPP pointer (a character's sheet, animator, combatant), so a mod gets the same
/// object each time and they compare with ==. One lifetime rule for all of them: wrappers whose game object the game
/// destroyed are swept when a new one is made on the main thread (never on another, where the game cannot be asked), and
/// what a swept wrapper watched is let go (<c>release</c>), so the game's objects hold no mod's handler. Part of the module
/// contract.
/// </summary>
internal sealed class GameObjectWrappers<T>(Func<T, bool> isGone, Action<T>? release = null) where T : class
{
    private readonly object _lock = new();
    private readonly Dictionary<nint, T> _known = [];

    /// <summary>
    /// The wrapper of a game object, made by <paramref name="create"/> the first time (null from it is kept nowhere: the
    /// object is none). <paramref name="create"/> runs under the table's lock.
    /// </summary>
    public T? Get(nint key, Func<T?> create)
    {
        List<T>? swept = null;
        T? made;
        lock (_lock)
        {
            if (_known.TryGetValue(key, out var known)) return known;
            if (Dispatcher.IsMainThread) swept = Sweep();
            made = create();
            if (made != null) _known[key] = made;
        }
        if (swept != null && release != null)
            foreach (var gone in swept) GameGuard.Run("SDK", $"Letting go of {gone}", () => release(gone));
        return made;
    }

    /// <summary>How many wrappers are kept, for unit tests.</summary>
    public int Count
    {
        get
        {
            lock (_lock) return _known.Count;
        }
    }

    /// <summary>Forgets every wrapper, between unit tests.</summary>
    public void Clear()
    {
        lock (_lock) _known.Clear();
    }

    private List<T>? Sweep()
    {
        List<T>? swept = null;
        foreach (var (key, wrapper) in _known.Where(k => isGone(k.Value)).ToList())
        {
            _known.Remove(key);
            (swept ??= []).Add(wrapper);
        }
        return swept;
    }
}
