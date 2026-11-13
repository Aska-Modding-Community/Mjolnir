using System;
using System.Collections.Generic;
using System.IO;

namespace Mjolnir.SDK;

/// <summary>
/// Mods' AssetBundles, one load per file for every module: Unity refuses to load a bundle twice, and one bundle of a mod may
/// hold an item's model (the content module) and an animation (the animation module). The bundle is kept as an object, since
/// this library references no Unity module that has it. Unity also refuses a bundle of another file built under the same name
/// (<see cref="BundleArchive"/>): <see cref="ClashWith"/> names the loaded one. Part of the module contract.
/// </summary>
internal static class SharedBundles
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, object?> s_bundles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> s_files = new(StringComparer.Ordinal); // archive file name → bundle file

    /// <summary>The bundle of that file, loaded by <paramref name="load"/> the first time any module asks; null when it failed.</summary>
    public static T? Get<T>(string path, Func<T?> load) where T : class
    {
        string key = Path.GetFullPath(path);
        lock (s_lock)
        {
            if (s_bundles.TryGetValue(key, out var known)) return known as T;
        }
        var bundle = load();
        var names = bundle != null ? BundleArchive.FileNames(key) : null;
        lock (s_lock)
        {
            if (s_bundles.TryGetValue(key, out var known)) return known as T; // another thread was quicker
            s_bundles[key] = bundle;
            if (names != null)
                foreach (string name in names) s_files.TryAdd(name, key);
        }
        return bundle;
    }

    /// <summary>
    /// The loaded bundle (its file) whose archive holds a file named as one of this file's, which is why Unity refuses this one:
    /// both were built under the same name (by an older Mjolnir kit, which named a bundle after its folder alone). Null when none.
    /// </summary>
    public static string? ClashWith(string path)
    {
        string key = Path.GetFullPath(path);
        if (BundleArchive.FileNames(key) is not { } names) return null;
        lock (s_lock)
        {
            foreach (string name in names)
                if (s_files.TryGetValue(name, out var other) && !string.Equals(other, key, StringComparison.OrdinalIgnoreCase)) return other;
        }
        return null;
    }

    /// <summary>The text a module's warning gives for a bundle Unity did not load.</summary>
    public static string WhyNotLoaded(string path)
    {
        if (ClashWith(path) is { } other)
            return $"{path} was built under the same name as {other}, which is loaded already, and Unity loads one bundle of a name: build either mod's bundles again with the Mjolnir kit, which names each bundle after its mod";
        return $"{path} is not an AssetBundle of this game's Unity (6000.3.12f1, Windows)";
    }
}
