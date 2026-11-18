using System;
using System.Runtime.CompilerServices;
using Mjolnir.Core;
using Mjolnir.Core.Lifecycle;
using Mjolnir.SDK.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mjolnir.SDK;

/// <summary>
/// The loader's log for SDK code and the SDK's modules (Mjolnir.SDK.UI…), which never reference Mjolnir.Core themselves:
/// Core's internals are visible to Mjolnir.SDK only.
/// </summary>
internal static class SdkLog
{
    public static void Info(string source, string message) => Log.Info(source, message);

    public static void Warn(string source, string message) => Log.Warn(source, message);

    public static void Error(string source, string message) => Log.Error(source, message);
}

/// <summary>
/// The game loop for SDK code and modules: phase B's start and every frame (Core's GameEvents), and whether Unity may be
/// used now. Handlers run through ModFaults: a throwing mod handler disables its mod.
/// </summary>
internal static class GameLoop
{
    private static readonly object s_lock = new();
    private static bool s_watching;
    private static volatile bool s_headless;

    /// <summary>True once phase B has started: Unity objects may be created.</summary>
    public static bool IsGameReady => GameEvents.IsGameReady;

    /// <summary>Raised once when phase B starts; a handler added after that runs at once.</summary>
    public static event Action GameReady
    {
        add => GameEvents.GameReady += value;
        remove => GameEvents.GameReady -= value;
    }

    /// <summary>Every frame (Unity Update), on the main thread.</summary>
    public static event Action Update
    {
        add => GameEvents.Update += value;
        remove => GameEvents.Update -= value;
    }

    /// <summary>Every frame after every Update (Unity LateUpdate), on the main thread.</summary>
    public static event Action LateUpdate
    {
        add => GameEvents.LateUpdate += value;
        remove => GameEvents.LateUpdate -= value;
    }

    /// <summary>
    /// Whether Unity may be touched now: phase B has started, this is the main thread, and the game has a screen (a
    /// headless dedicated server has none; every UI call is a no-op there). Otherwise logs why (as
    /// <paramref name="source"/>) and returns false.
    /// </summary>
    public static bool CanUseUnity(string what, string source)
    {
        WatchForHeadless();
        if (!GameEvents.IsGameReady)
        {
            Log.Warn(source, $"{what} ignored: the game is not ready yet (call it from OnGameReady or later, not OnLoad)");
            return false;
        }
        if (s_headless) return false;
        if (!Dispatcher.IsMainThread)
        {
            Log.Warn(source, $"{what} ignored: Unity can only be used from the main thread");
            return false;
        }
        return true;
    }

    /// <summary>Starts watching for a headless game (checked when phase B starts). Safe in phase A.</summary>
    public static void WatchForHeadless()
    {
        lock (s_lock)
        {
            if (s_watching) return;
            s_watching = true;
        }
        GameEvents.GameReady += OnGameReady;
    }

    private static void OnGameReady()
    {
        s_headless = IsHeadless();
        if (s_headless) Log.Info("SDK", "No graphics device (dedicated server): UI calls are no-ops");
    }

    // Unity types are touched only here, in phase B.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsHeadless() => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
}
