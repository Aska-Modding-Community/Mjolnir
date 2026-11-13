using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// A mod's own models in the world: prefabs of its AssetBundles (built with the Mjolnir kit for the game's Unity, Windows,
/// HDRP), placed where the mod wants them. Their materials take the game's own shaders of the same name, as items' models
/// do (a bundle's HDRP/Lit draws black in the game). An object placed so is the mod's: the game does not save it, and it goes
/// with its world (or with <c>Object.Destroy</c>).
/// </summary>
/// <example><code>
/// var altar = Models.Spawn("bundles/models", "Altar", place, Quaternion.identity);
/// </code></example>
public static class Models
{
    /// <summary>
    /// Places a copy of the prefab <paramref name="asset"/> of the calling mod's bundle <paramref name="bundle"/> (a file
    /// relative to its folder) in the world. Null (logged) when the bundle or the prefab is missing, off the main thread,
    /// before the game is ready, or when the caller is no mod.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static GameObject? Spawn(string bundle, string asset, Vector3 position, Quaternion rotation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        var caller = CallingMod.Of(Assembly.GetCallingAssembly());
        if (ContentRegistry.Shared.TryPathOf(caller, bundle, $"Models.Spawn('{asset}')") is not { } path) return null;
        if (!GameLoop.IsGameReady || !Dispatcher.IsMainThread)
        {
            SdkLog.Warn(GameLocalization.Source, $"Models.Spawn('{asset}') ignored: on the main thread of a running game only");
            return null;
        }
        var prefab = GameModels.Prefab(path, asset, caller.GetName().Name ?? "a mod");
        if (prefab == null) return null;
        var placed = Object.Instantiate(prefab, position, rotation);
        placed.name = asset;
        return placed;
    }
}
