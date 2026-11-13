using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mjolnir.Core.Mods;

internal sealed record ModCandidate(ModDescriptor Mod, Type EntryType);

/// <summary>
/// Finds the entry class of each loaded mod assembly and validates its manifest, through the contracts in
/// Contracts.cs only. One mod per assembly: exactly one class with a manifest attribute, implementing
/// <see cref="IModEntry"/>. An assembly without one is a library (for example a mod's dependency).
/// </summary>
internal static partial class ModDiscovery
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex ValidId();

    /// <summary>The platform's own id: its settings are Mjolnir/Config/mjolnir.toml, where a mod with this id would keep its config.</summary>
    internal const string ReservedId = "mjolnir";

    public static IReadOnlyList<ModCandidate> Find(IReadOnlyList<LoadedModAssembly> assemblies)
    {
        var found = new List<ModCandidate>();
        foreach (var loaded in assemblies)
        {
            try
            {
                var candidate = Inspect(LoadableTypes(loaded.Assembly, Path.GetFileName(loaded.Path)), loaded.Assembly, loaded.Path);
                if (candidate != null) found.Add(candidate);
            }
            catch (Exception e)
            {
                Log.Error("Mods", $"{Path.GetFileName(loaded.Path)}: cannot inspect mod assembly: {e.Message}");
            }
        }
        return found;
    }

    /// <summary>Finds and validates the mod in one assembly's types; null if there is none or it is invalid (logged).</summary>
    internal static ModCandidate? Inspect(IReadOnlyList<Type> types, Assembly assembly, string path)
    {
        string file = Path.GetFileName(path);
        var marked = types.Where(t => HasAttribute<IModManifest>(t)).ToList();
        if (marked.Count == 0)
        {
            var unmarked = types.FirstOrDefault(t => typeof(IModEntry).IsAssignableFrom(t) && !t.IsAbstract);
            if (unmarked != null) Log.Warn("Mods", $"{file}: {unmarked.FullName} is a mod class without a manifest attribute; not loaded");
            else Log.Info("Mods", $"{file}: no mod class, treated as a library");
            return null;
        }
        if (marked.Count > 1)
        {
            Log.Error("Mods", $"{file}: more than one mod class ({string.Join(", ", marked.Select(t => t.FullName))}); not loaded");
            return null;
        }

        var type = marked[0];
        string? problem = Validate(type, assembly, path, out var descriptor);
        if (problem != null)
        {
            Log.Error("Mods", $"{file}: {type.FullName}: {problem}; not loaded");
            return null;
        }
        return new ModCandidate(descriptor!, type);
    }

    private static string? Validate(Type type, Assembly assembly, string path, out ModDescriptor? descriptor)
    {
        descriptor = null;
        if (!typeof(IModEntry).IsAssignableFrom(type)) return "the class with the manifest is not a mod class";
        if (type.IsAbstract || type.ContainsGenericParameters) return "the mod class must be a non-abstract, non-generic class";
        if (type.GetConstructor(Type.EmptyTypes) == null) return "the mod class needs a public parameterless constructor";

        var manifests = Attributes<IModManifest>(type);
        if (manifests.Length != 1) return "the mod class needs exactly one manifest attribute";
        var manifest = manifests[0];
        string id = manifest.Id?.Trim() ?? "";
        if (!ValidId().IsMatch(id))
            return $"invalid mod id '{manifest.Id}' (letters, digits, '.', '_', '-'; starting with a letter or digit)";
        if (string.Equals(id, ReservedId, StringComparison.OrdinalIgnoreCase))
            return $"the mod id '{id}' is reserved for the platform (its settings file is Mjolnir/Config/{ReservedId}.toml)";
        if (!ModVersion.TryParse(manifest.Version, out var version))
            return $"invalid version '{manifest.Version}' (expected Major[.Minor[.Patch[.Revision]]])";

        var dependencies = new List<ModDependency>();
        foreach (var dep in Attributes<IModDependencyDeclaration>(type))
        {
            if (string.IsNullOrWhiteSpace(dep.Id)) return "a dependency has an empty id";
            try
            {
                dependencies.Add(new ModDependency(dep.Id.Trim(), VersionRange.Parse(dep.VersionRange), dep.Optional));
            }
            catch (FormatException e)
            {
                return $"dependency {dep.Id}: {e.Message}";
            }
        }

        string name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name.Trim();
        descriptor = new ModDescriptor(id, name, manifest.Version.Trim(), version, dependencies, assembly, path);
        return null;
    }

    // Attribute metadata is read first, and only attributes implementing T are created: other attributes on mod
    // classes (for example [HarmonyPatch(typeof(SomeGameType))]) are never instantiated here.
    private static bool HasAttribute<T>(Type type) =>
        type.GetCustomAttributesData().Any(d => typeof(T).IsAssignableFrom(d.AttributeType));

    private static T[] Attributes<T>(Type type) =>
        type.GetCustomAttributesData()
            .Select(d => d.AttributeType)
            .Where(t => typeof(T).IsAssignableFrom(t))
            .Distinct()
            .SelectMany(t => type.GetCustomAttributes(t, inherit: false))
            .Cast<T>()
            .ToArray();

    private static Type[] LoadableTypes(Assembly assembly, string file)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            var missing = e.LoaderExceptions.Where(x => x != null).Select(x => x!.Message).Distinct().Take(3);
            Log.Warn("Mods", $"{file}: some types cannot be loaded ({string.Join("; ", missing)})");
            return e.Types.Where(t => t != null).ToArray()!;
        }
    }
}
