using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// Every mod's strings, read once per mod: for the loaded mods (ModRegistry), and for the assembly that calls the API,
/// which may be a mod still in its OnLoad (not registered yet) or a library in a mod's folder.
/// </summary>
internal static class ModStringsRegistry
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, ModStrings> s_byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Assembly, ModStrings?> s_byAssembly = [];
    private static ModStrings[]? s_all; // by id, made again when a mod's strings come or a mod loads
    private static int s_allForMods = -1;

    /// <summary>The strings of the mod <paramref name="assembly"/> belongs to; null for platform code and other assemblies.</summary>
    public static ModStrings? ForAssembly(Assembly assembly)
    {
        lock (s_lock)
        {
            if (s_byAssembly.TryGetValue(assembly, out var known)) return known;
        }
        var owner = FindMod(assembly);
        var strings = owner == null ? null : ForMod(owner.Value.Id, owner.Value.Folder);
        // A mod's assembly found no mod while mods are still loading may be a library asked about before its mod could be
        // told (its mod in OnLoad, not registered yet): asked again later, it may be found then.
        if (strings != null || ModCatalog.IsFilled || ModFaults.IsPlatform(assembly))
            lock (s_lock) s_byAssembly[assembly] = strings;
        return strings;
    }

    /// <summary>The strings of every loaded mod (and of every mod an assembly lookup found), by id.</summary>
    public static IReadOnlyList<ModStrings> All()
    {
        var loaded = ModRegistry.Loaded;
        lock (s_lock)
        {
            if (s_all != null && s_allForMods == loaded.Count) return s_all;
        }
        foreach (var manifest in loaded) ForMod(manifest.Id, Path.GetDirectoryName(manifest.Location));
        lock (s_lock)
        {
            if (s_all == null || s_allForMods != loaded.Count)
            {
                s_all = [.. s_byId.Values.OrderBy(s => s.ModId, StringComparer.Ordinal)];
                s_allForMods = loaded.Count;
            }
            return s_all;
        }
    }

    /// <summary>The strings of a mod by its id: a loaded mod's (read now if they were not yet) or one an assembly lookup found; null for none.</summary>
    public static ModStrings? ForModId(string modId)
    {
        lock (s_lock)
        {
            if (s_byId.TryGetValue(modId, out var known)) return known;
        }
        return ModRegistry.TryGet(modId, out var manifest) ? ForMod(manifest.Id, Path.GetDirectoryName(manifest.Location)) : null;
    }

    /// <summary>A full key of any mod, <c>&lt;mod id&gt;.&lt;key&gt;</c> (mod ids contain dots, so each id is tried).</summary>
    public static bool TryGetFullKey(string key, string language, out string text)
    {
        foreach (var strings in All())
        {
            if (key.StartsWith(strings.KeyPrefix, StringComparison.OrdinalIgnoreCase) &&
                strings.TryGet(key[strings.KeyPrefix.Length..], language, out text))
                return true;
        }
        text = "";
        return false;
    }

    /// <summary>A mod's override of a game string in <paramref name="language"/> (the first mod by id wins).</summary>
    public static bool TryGetOverride(string gameKey, string language, out string text)
    {
        foreach (var strings in All())
            if (strings.TryGetOverride(gameKey, language, out text)) return true;
        text = "";
        return false;
    }

    /// <summary>Unit tests: strings of a mod made in the test, optionally as the owner of an assembly.</summary>
    internal static void Register(ModStrings strings, Assembly? owner = null)
    {
        lock (s_lock)
        {
            s_byId[strings.ModId] = strings;
            s_all = null;
            if (owner != null) s_byAssembly[owner] = strings;
        }
    }

    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_byId.Clear();
            s_byAssembly.Clear();
            s_all = null;
        }
    }

    private static ModStrings ForMod(string id, string? folder)
    {
        lock (s_lock)
        {
            if (s_byId.TryGetValue(id, out var known)) return known;
        }
        var strings = ModStrings.Load(id, folder, message => SdkLog.Warn(Localization.Source, $"{id}: {message}"));
        lock (s_lock)
        {
            // Another thread may have read it meanwhile: keep the first, so every caller sees one table.
            if (s_byId.TryGetValue(id, out var first)) return first;
            s_byId[id] = strings;
            s_all = null;
        }
        foreach (string language in strings.Languages) SdkLog.Info(Localization.Source, $"{id}: strings in {language}");
        return strings;
    }

    // A registered mod by its .dll, a mod still loading by its manifest attribute, a library by the mod in its load context
    // (a mod folder's own, which holds the mod's .dll too, registered or still in its OnLoad) or in its folder.
    private static (string Id, string? Folder)? FindMod(Assembly assembly)
    {
        string location = SafeLocation(assembly);
        if (location.Length == 0) return ManifestId(assembly) is { } own ? (own, null) : InContext(assembly);
        string folder = Path.GetDirectoryName(location) ?? "";
        foreach (var manifest in ModRegistry.Loaded)
            if (SamePath(manifest.Location, location)) return (manifest.Id, folder);
        if (ManifestId(assembly) is { } id) return (id, folder);
        if (InContext(assembly) is { } sibling) return sibling;
        foreach (var manifest in ModRegistry.Loaded)
            if (SamePath(Path.GetDirectoryName(manifest.Location), folder)) return (manifest.Id, folder);
        return null;
    }

    private static (string Id, string? Folder)? InContext(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context == null || context == AssemblyLoadContext.Default) return null;
        foreach (var other in context.Assemblies)
        {
            if (other == assembly || ManifestId(other) is not { } id) continue;
            string location = SafeLocation(other);
            return (id, location.Length == 0 ? null : Path.GetDirectoryName(location));
        }
        return null;
    }

    private static string? ManifestId(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }
        foreach (var type in types)
        {
            if (type == null || !typeof(Mod).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttribute<ModManifestAttribute>() is { Id.Length: > 0 } manifest) return manifest.Id;
        }
        return null;
    }

    private static string SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.IsDynamic ? "" : assembly.Location;
        }
        catch (NotSupportedException)
        {
            return "";
        }
    }

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
