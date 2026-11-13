extern alias unity;
using System;
using System.Linq;
using System.Threading;
using Mjolnir.Core.Mods;
using unity::UnityEngine.SceneManagement;

namespace Mjolnir.Core.Lifecycle;

/// <summary>
/// Phase B game-loop events, pumped by <see cref="MjolnirBehaviour"/> and PhaseB.
/// Internal: the SDK turns them into mod callbacks (Mod.OnUpdate…). Every event is raised on Unity's main thread;
/// handlers are called through <see cref="ModFaults.Invoke{T}"/>, so a throwing mod handler disables that mod only.
/// </summary>
internal static class GameEvents
{
    private static readonly Handlers<Action> s_gameReady = new(nameof(GameReady));
    private static readonly Handlers<Action> s_update = new(nameof(Update));
    private static readonly Handlers<Action> s_lateUpdate = new(nameof(LateUpdate));
    private static readonly Handlers<Action> s_fixedUpdate = new(nameof(FixedUpdate));
    private static readonly Handlers<Action<Scene, LoadSceneMode>> s_sceneLoaded = new(nameof(SceneLoaded));
    private static readonly Handlers<Action> s_applicationQuit = new(nameof(ApplicationQuit));

    /// <summary>True once phase B has started and <see cref="GameReady"/> was raised.</summary>
    public static bool IsGameReady { get; private set; }

    /// <summary>
    /// Raised once when phase B starts (the engine's first scene callback, before the first frame); Unity
    /// objects may be created from here on. A handler added after that runs immediately.
    /// </summary>
    public static event Action GameReady
    {
        add
        {
            s_gameReady.Add(value);
            if (IsGameReady && value != null)
                foreach (var part in value.GetInvocationList()) s_gameReady.Invoke((Action)part, static h => h());
        }
        remove => s_gameReady.Remove(value);
    }

    /// <summary>Every frame (Unity Update).</summary>
    public static event Action Update { add => s_update.Add(value); remove => s_update.Remove(value); }

    /// <summary>Every frame (Unity LateUpdate).</summary>
    public static event Action LateUpdate { add => s_lateUpdate.Add(value); remove => s_lateUpdate.Remove(value); }

    /// <summary>Every physics step (Unity FixedUpdate).</summary>
    public static event Action FixedUpdate { add => s_fixedUpdate.Add(value); remove => s_fixedUpdate.Remove(value); }

    /// <summary>
    /// Unity's SceneManager.sceneLoaded. On ASKA phase B starts before the first scene (<c>Loader_PC</c>) has
    /// loaded, so this sees every scene of the game.
    /// </summary>
    public static event Action<Scene, LoadSceneMode> SceneLoaded
    {
        add => s_sceneLoaded.Add(value);
        remove => s_sceneLoaded.Remove(value);
    }

    /// <summary>Once, when the game quits (Unity OnApplicationQuit), before objects are destroyed.</summary>
    public static event Action ApplicationQuit
    {
        add => s_applicationQuit.Add(value);
        remove => s_applicationQuit.Remove(value);
    }

    internal static void RaiseGameReady()
    {
        IsGameReady = true;
        s_gameReady.Raise(static h => h());
    }

    internal static void RaiseUpdate() => s_update.Raise(static h => h());
    internal static void RaiseLateUpdate() => s_lateUpdate.Raise(static h => h());
    internal static void RaiseFixedUpdate() => s_fixedUpdate.Raise(static h => h());
    internal static void RaiseSceneLoaded(Scene scene, LoadSceneMode mode) => s_sceneLoaded.Raise(h => h(scene, mode));
    internal static void RaiseApplicationQuit() => s_applicationQuit.Raise(static h => h());

    /// <summary>
    /// Copy-on-write handler list: raising never locks or allocates. A combined delegate (<c>+= a + b</c>) is kept as its
    /// parts, so each part is blamed on its own mod and one part's fault never skips another.
    /// </summary>
    private sealed class Handlers<T>(string eventName) where T : Delegate
    {
        private readonly object _lock = new();
        private T[] _items = [];

        public void Add(T? handler)
        {
            if (handler == null) return;
            lock (_lock) _items = [.. _items, .. handler.GetInvocationList().Cast<T>()];
        }

        public void Remove(T? handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                foreach (var part in handler.GetInvocationList().Cast<T>().Reverse())
                {
                    int i = Array.LastIndexOf(_items, part);
                    if (i >= 0) _items = [.. _items.AsSpan(0, i), .. _items.AsSpan(i + 1)];
                }
            }
        }

        public void Raise(Action<T> invoke)
        {
            foreach (var handler in Volatile.Read(ref _items)) Invoke(handler, invoke);
        }

        public void Invoke(T handler, Action<T> invoke) => ModFaults.Invoke(handler, eventName, invoke);
    }
}
