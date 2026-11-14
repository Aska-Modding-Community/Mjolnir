using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>A dependency declared with <see cref="ModDependencyAttribute"/>.</summary>
public sealed record ModDependencyInfo(string Id, string VersionRange, bool Optional);

/// <summary>What Mjolnir knows about a loaded mod, from its <see cref="ModManifestAttribute"/>.</summary>
public sealed class ModManifest
{
    private readonly ModDescriptor _mod;

    /// <summary>The picture a mod gets without <see cref="ModManifestAttribute.Icon"/>, next to its .dll (Thunderstore's name).</summary>
    internal const string DefaultIconFile = "icon.png";

    /// <param name="mod">What the loader read and checked.</param>
    /// <param name="attribute">The mod class's own attribute, for what the loader does not read (null outside a mod class).</param>
    /// <param name="warn">The mod's log, for an icon it names but does not ship.</param>
    internal ModManifest(ModDescriptor mod, ModManifestAttribute? attribute, Action<string>? warn = null)
    {
        _mod = mod;
        Network = attribute?.Network ?? NetworkCompatibility.ClientOnly;
        Description = TextOrNull(attribute?.Description);
        Author = TextOrNull(attribute?.Author);
        IconPath = FindIcon(mod.Location, TextOrNull(attribute?.Icon), warn);
        Dependencies = mod.Dependencies.Select(d => new ModDependencyInfo(d.Id, d.Range.ToString(), d.Optional)).ToList();
    }

    /// <summary>
    /// The full path of the mod's picture (<see cref="ModManifestAttribute.Icon"/>, else an <c>icon.png</c> next to its
    /// .dll), found when the mod was loaded; null if it has none.
    /// </summary>
    public string? IconPath { get; }

    /// <summary>What the mod does (<see cref="ModManifestAttribute.Description"/>); null if not given.</summary>
    public string? Description { get; }

    /// <summary>Who made the mod (<see cref="ModManifestAttribute.Author"/>); null if not given.</summary>
    public string? Author { get; }

    public string Id => _mod.Id;
    public string Name => _mod.Name;

    /// <summary>The version as written in the manifest (may carry a -prerelease suffix).</summary>
    public string VersionText => _mod.VersionText;

    /// <summary>The comparable version: four components, missing ones are 0.</summary>
    public Version Version => _mod.Version;

    public NetworkCompatibility Network { get; }
    public IReadOnlyList<ModDependencyInfo> Dependencies { get; }
    public Assembly Assembly => _mod.Assembly;

    /// <summary>Path of the mod's .dll.</summary>
    public string Location => _mod.Location;

    /// <summary>True once the mod threw: its callbacks are no longer called (its Harmony patches stay).</summary>
    public bool IsDisabled => ModFaults.IsDisabled(_mod.Assembly);

    public override string ToString() => _mod.ToString();

    /// <summary>
    /// <paramref name="icon"/> relative to the folder of <paramref name="modLocation"/> if that file exists, else an
    /// <see cref="DefaultIconFile"/> there; null for neither. An icon named but not found is reported to <paramref name="warn"/>.
    /// </summary>
    internal static string? FindIcon(string modLocation, string? icon, Action<string>? warn)
    {
        string? folder = Path.GetDirectoryName(modLocation);
        if (string.IsNullOrEmpty(folder)) return null;
        if (icon != null)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(folder, icon));
                if (File.Exists(path)) return path;
                warn?.Invoke($"icon '{icon}' not found ({path}); using {DefaultIconFile} if there is one");
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warn?.Invoke($"icon '{icon}' is not a valid path ({e.Message}); using {DefaultIconFile} if there is one");
            }
        }
        string conventional = Path.Combine(folder, DefaultIconFile);
        return File.Exists(conventional) ? Path.GetFullPath(conventional) : null;
    }

    private static string? TextOrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
