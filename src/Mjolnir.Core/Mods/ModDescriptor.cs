using System;
using System.Collections.Generic;
using System.Reflection;

namespace Mjolnir.Core.Mods;

/// <summary>A dependency as the resolver sees it.</summary>
internal sealed record ModDependency(string Id, VersionRange Range, bool Optional);

/// <summary>A discovered mod as the loader sees it: identity, version, dependencies and where it came from.</summary>
internal sealed class ModDescriptor(string id, string name, string versionText, Version version,
                                    IReadOnlyList<ModDependency> dependencies, Assembly assembly, string location)
{
    public string Id { get; } = id;
    public string Name { get; } = name;

    /// <summary>The version as written in the manifest (may carry a -prerelease suffix).</summary>
    public string VersionText { get; } = versionText;

    /// <summary>The comparable version: four components, missing ones are 0.</summary>
    public Version Version { get; } = version;

    public IReadOnlyList<ModDependency> Dependencies { get; } = dependencies;
    public Assembly Assembly { get; } = assembly;

    /// <summary>Path of the mod's .dll.</summary>
    public string Location { get; } = location;

    public override string ToString() => $"{Name} ({Id}) {VersionText}";
}
