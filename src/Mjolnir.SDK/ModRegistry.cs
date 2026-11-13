using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mjolnir.SDK;

/// <summary>The mods that loaded (their OnLoad succeeded), in load order; for example to check an optional dependency.</summary>
public static class ModRegistry
{
    private static readonly object s_lock = new();
    private static ModManifest[] s_loaded = [];
    private static ReadOnlyCollection<ModManifest> s_loadedView = new([]);
    private static Mod[] s_mods = [];

    /// <summary>The loaded mods' manifests, in load order.</summary>
    public static IReadOnlyList<ModManifest> Loaded => s_loadedView;

    public static bool TryGet(string id, out ModManifest manifest)
    {
        foreach (var mod in s_loaded)
        {
            if (string.Equals(mod.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                manifest = mod;
                return true;
            }
        }
        manifest = null!;
        return false;
    }

    public static bool IsLoaded(string id) => TryGet(id, out _);

    /// <summary>The loaded mods themselves, for the SDK (the Mods window reads their configs).</summary>
    internal static IReadOnlyList<Mod> Mods => s_mods;

    internal static void Add(Mod mod)
    {
        lock (s_lock)
        {
            s_loaded = [.. s_loaded, mod.Manifest];
            s_loadedView = new ReadOnlyCollection<ModManifest>(s_loaded); // a copy each time: a caller never sees the list grow
            s_mods = [.. s_mods, mod];
        }
    }
}
