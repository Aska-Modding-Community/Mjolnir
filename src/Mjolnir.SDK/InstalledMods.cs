using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>What became of an installed mod at this start.</summary>
internal enum InstalledState
{
    /// <summary>It loaded; it may be disabled after an error since (<see cref="ModManifest.IsDisabled"/>).</summary>
    Running,
    /// <summary>The player turned it off.</summary>
    TurnedOff,
    /// <summary>Skipped before loading: <see cref="InstalledMod.Problem"/> says why.</summary>
    NotLoaded,
    /// <summary>Its constructor or OnLoad threw.</summary>
    Failed,
}

/// <summary>Why a mod was skipped before loading: Core's reasons, without Core's types (the modules cannot see Core).</summary>
internal enum InstalledProblemKind
{
    Duplicate,
    Missing,
    WrongVersion,
    DependencyNotLoaded,
    DependencyTurnedOff,
    DependencyFailed,
    SelfDependency,
    Cycle,
}

/// <summary>Why a mod was skipped, for the Mods window's text in the player's language.</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Other">The mod it names: a dependency, the duplicated id, or the mods in a cycle.</param>
/// <param name="Range">The version range asked for.</param>
/// <param name="Detail">The installed version (<see cref="InstalledProblemKind.WrongVersion"/>) or the other copy's file (<see cref="InstalledProblemKind.Duplicate"/>).</param>
internal sealed record InstalledProblem(InstalledProblemKind Kind, string? Other = null, string? Range = null, string? Detail = null);

/// <summary>A mod found at start, loaded or not: its manifest, what became of it, and the mod itself if it runs.</summary>
internal sealed class InstalledMod(ModManifest manifest, InstalledState state, InstalledProblem? problem = null, Mod? mod = null)
{
    public ModManifest Manifest { get; } = manifest;
    public string Id => Manifest.Id;
    public InstalledState State { get; } = state;

    /// <summary>Why it was skipped (<see cref="InstalledState.NotLoaded"/>); null otherwise.</summary>
    public InstalledProblem? Problem { get; } = problem;

    /// <summary>The running mod (<see cref="InstalledState.Running"/>); null otherwise.</summary>
    public Mod? Mod { get; } = mod;

    /// <summary>The ids of the mods it cannot load without.</summary>
    public IEnumerable<string> Requires => Manifest.Dependencies.Where(d => !d.Optional).Select(d => d.Id);

    /// <summary>An installed mod from plain values, for the tests of the SDK's modules (they cannot see Core).</summary>
    internal static InstalledMod Make(string id, string name, string version, InstalledState state, InstalledProblem? problem = null,
                                      string? description = null, params ModDependencyInfo[] dependencies)
    {
        if (!ModVersion.TryParse(version, out var parsed)) throw new ArgumentException($"invalid version '{version}'", nameof(version));
        var deps = dependencies.Select(d => new ModDependency(d.Id, VersionRange.Parse(d.VersionRange), d.Optional)).ToList();
        var descriptor = new ModDescriptor(id, name, version, parsed, deps, typeof(InstalledMod).Assembly, "");
        return new InstalledMod(new ModManifest(descriptor, new ModManifestAttribute(id, name, version) { Description = description }), state, problem);
    }
}

/// <summary>
/// Every mod found at start, loaded or not, and which of them the player wants on at the next start: Core's mod list
/// (Mjolnir/Config/mod-list.json), written at once when the player turns a mod on or off. A change takes effect at the
/// next start, since mods can be neither unloaded nor loaded after phase A. For the Mods window (the module contract);
/// mods see the loaded ones through <see cref="ModRegistry"/>.
/// </summary>
internal sealed class InstalledMods
{
    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;
    private static InstalledMods? s_current;
    private readonly object _lock = new();
    private readonly string _listPath;
    private readonly HashSet<string> _turnedOff; // what the file says, ids of mods not installed included

    /// <param name="mods">The installed mods, in the order to show them.</param>
    /// <param name="listPath">The mod list's file; empty for none (nothing can be turned on or off).</param>
    /// <param name="turnedOff">The ids turned off at this start.</param>
    internal InstalledMods(IReadOnlyList<InstalledMod> mods, string listPath, IEnumerable<string> turnedOff)
    {
        All = mods;
        _listPath = listPath;
        _turnedOff = new HashSet<string>(turnedOff, Ids);
    }

    /// <summary>The mods of this start, from Core's catalog (filled in phase A), on first use.</summary>
    public static InstalledMods Current => LazyInitializer.EnsureInitialized(ref s_current, FromCatalog);

    /// <summary>Loaded mods in load order, then the others by name.</summary>
    public IReadOnlyList<InstalledMod> All { get; }

    /// <summary>The player's choice for the next start.</summary>
    public bool IsEnabled(InstalledMod mod)
    {
        lock (_lock) return !_turnedOff.Contains(mod.Id);
    }

    /// <summary>Turns a mod on or off for the next start and writes the file; false, and nothing changed, if it could not be written.</summary>
    public bool SetEnabled(InstalledMod mod, bool on)
    {
        lock (_lock)
        {
            if (!_turnedOff.Contains(mod.Id) == on) return true;
            if (_listPath.Length == 0) return false;
            if (on) _turnedOff.Remove(mod.Id);
            else _turnedOff.Add(mod.Id);
            if (ModList.Write(_listPath, _turnedOff)) return true;
            if (on) _turnedOff.Add(mod.Id);
            else _turnedOff.Remove(mod.Id);
            return false;
        }
    }

    /// <summary>True when a restart would change what runs: a running mod turned off, or a turned-off mod turned on.</summary>
    public bool IsPending(InstalledMod mod) => mod.State switch
    {
        InstalledState.Running => !IsEnabled(mod),
        InstalledState.TurnedOff => IsEnabled(mod),
        _ => false,
    };

    /// <summary>True while some change waits for a restart.</summary>
    public bool HasPendingChanges => All.Any(IsPending);

    /// <summary>The mods that are on and cannot load without <paramref name="mod"/>.</summary>
    public IReadOnlyList<InstalledMod> RequiredBy(InstalledMod mod) =>
        All.Where(m => m != mod && IsEnabled(m) && m.Requires.Contains(mod.Id, Ids)).ToList();

    /// <summary>The installed mods <paramref name="mod"/> cannot load without that are turned off.</summary>
    public IReadOnlyList<InstalledMod> TurnedOffRequirements(InstalledMod mod) =>
        mod.Requires.Distinct(Ids)
           .Select(id => All.FirstOrDefault(m => Ids.Equals(m.Id, id)))
           .OfType<InstalledMod>()
           .Where(m => !IsEnabled(m))
           .ToList();

    /// <summary>Core's skip reason as the SDK's; null for a turned-off mod (its state says it).</summary>
    internal static InstalledProblem? ProblemOf(ModSkip? skip) =>
        skip != null && Enum.TryParse<InstalledProblemKind>(skip.Kind.ToString(), out var kind)
            ? new InstalledProblem(kind, skip.Other, skip.Range, skip.Detail)
            : null;

    private static InstalledMods FromCatalog()
    {
        var running = new List<InstalledMod>();
        var others = new List<InstalledMod>();
        foreach (var record in ModCatalog.Mods)
        {
            if (record.Outcome == ModOutcome.Loaded)
            {
                var mod = ModRegistry.Mods.FirstOrDefault(m => m.GetType().Assembly == record.Mod.Assembly);
                running.Add(new InstalledMod(mod?.Manifest ?? ManifestOf(record), InstalledState.Running, mod: mod));
                continue;
            }
            var state = record.Outcome switch
            {
                ModOutcome.TurnedOff => InstalledState.TurnedOff,
                ModOutcome.Failed => InstalledState.Failed,
                _ => InstalledState.NotLoaded,
            };
            others.Add(new InstalledMod(ManifestOf(record), state, ProblemOf(record.Skip)));
        }
        running.AddRange(others.OrderBy(m => m.Manifest.Name, StringComparer.CurrentCultureIgnoreCase));
        return new InstalledMods(running, ModCatalog.ListPath, ModCatalog.TurnedOffAtStart);
    }

    // A mod that did not load has no Mod object: its manifest is made here, its attribute read from its class (only the
    // SDK's attribute is created; no code of the mod runs).
    private static ModManifest ManifestOf(ModRecord record)
    {
        ModManifestAttribute? attribute = null;
        try
        {
            attribute = record.EntryType.GetCustomAttribute<ModManifestAttribute>(inherit: false);
        }
        catch (Exception e)
        {
            SdkLog.Warn("SDK", $"{record.Mod}: cannot read its manifest attribute: {e.Message}");
        }
        return new ModManifest(record.Mod, attribute);
    }
}
