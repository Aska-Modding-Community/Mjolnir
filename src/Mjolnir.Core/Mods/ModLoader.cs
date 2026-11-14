using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Mjolnir.Core.Interop;

namespace Mjolnir.Core.Mods;

/// <summary>
/// Turns loaded mod assemblies into running mods: discovery, the player's turned-off mods
/// (<see cref="ModList"/>), dependency order, creation with an injected <see cref="ModContext"/>,
/// <see cref="IModEntry.Load"/> in phase A and, when Il2CppInterop runs, <see cref="IModEntry.Attach"/>. What a mod does
/// in Load/Attach is the SDK's business. What became of each mod goes into <see cref="ModCatalog"/>.
/// </summary>
internal static class ModLoader
{
    /// <summary>The platform's own version: every Mjolnir assembly carries it, and a mod declares the range it needs.</summary>
    private static Version? PlatformVersion => typeof(ModLoader).Assembly.GetName().Version;

    public static void LoadAll(IReadOnlyList<LoadedModAssembly> assemblies, LoaderPaths paths)
    {
        var turnedOff = ModList.Read(paths.ModListPath);
        var candidates = ModDiscovery.Find(assemblies);
        var result = DependencyResolver.Resolve(candidates.Select(c => c.Mod).ToList(), turnedOff, PlatformVersion);
        foreach (var (mod, reason) in result.Skipped)
        {
            if (reason.Kind == ModSkipKind.TurnedOff) Log.Info("Mods", $"{mod}: not loaded, {reason}");
            else Log.Error("Mods", $"{mod}: not loaded, {reason}");
        }

        if (!InteropRuntime.Started && result.Ordered.Count > 0)
            Log.Warn("Mods", "Il2CppInterop is not running: mods get OnLoad only, no game types and no phase B callbacks");

        var types = candidates.ToDictionary(c => c.Mod, c => c.EntryType);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<ModRecord>();
        var notLoaded = new List<ModRecord>();
        foreach (var mod in result.Ordered)
        {
            var failed = mod.Dependencies.FirstOrDefault(d => !d.Optional && !loaded.Contains(d.Id));
            if (failed != null)
            {
                var skip = new ModSkip(ModSkipKind.DependencyFailed, failed.Id);
                Log.Error("Mods", $"{mod}: not loaded, {skip}");
                notLoaded.Add(new ModRecord(mod, types[mod], ModOutcome.NotLoaded, skip));
                continue;
            }
            if (TryLoad(mod, types[mod], paths, loaded))
            {
                loaded.Add(mod.Id);
                records.Add(new ModRecord(mod, types[mod], ModOutcome.Loaded));
            }
            else notLoaded.Add(new ModRecord(mod, types[mod], ModOutcome.Failed));
        }
        records.AddRange(notLoaded);
        records.AddRange(result.Skipped.Select(s => new ModRecord(
            s.Mod, types[s.Mod], s.Reason.Kind == ModSkipKind.TurnedOff ? ModOutcome.TurnedOff : ModOutcome.NotLoaded, s.Reason)));
        ModCatalog.Set(records, paths.ModListPath, turnedOff);

        int off = result.Skipped.Count(s => s.Reason.Kind == ModSkipKind.TurnedOff);
        Log.Info("Mods", $"Mods loaded: {loaded.Count} of {candidates.Count}" + (off > 0 ? $" ({off} turned off)" : ""));
    }

    private static bool TryLoad(ModDescriptor mod, Type type, LoaderPaths paths, HashSet<string> loaded)
    {
        var watch = Stopwatch.StartNew();
        IModEntry entry;
        try
        {
            // The mod's constructor and OnLoad run as its own code (ModFaults.CurrentOwner), which the SDK's caller lookup falls back to.
            using var owner = ModFaults.Enter(mod.Assembly);
            entry = ModContext.Create(type, new ModContext(mod, Path.Combine(paths.ConfigDir, mod.Id + ".toml")));
            entry.Load();
        }
        catch (Exception e)
        {
            ModFaults.Disable(mod.Assembly, $"loading failed: {Unwrap(e)}");
            return false;
        }

        // A mod whose callbacks could not be attached is disabled, so it is no loaded mod: its dependents are not loaded.
        if (InteropRuntime.Started)
        {
            try
            {
                entry.Attach();
            }
            catch (Exception e)
            {
                ModFaults.Disable(mod.Assembly, $"attaching its callbacks failed: {Unwrap(e)}");
                return false;
            }
        }

        var after = mod.Dependencies.Where(d => loaded.Contains(d.Id)).Select(d => d.Id).ToList();
        string deps = after.Count == 0 ? "" : $" [after {string.Join(", ", after)}]";
        Log.Info("Mods", $"Loaded {mod}{deps} in {watch.ElapsedMilliseconds} ms");
        return true;
    }

    private static Exception Unwrap(Exception e) =>
        e is TargetInvocationException or TypeInitializationException && e.InnerException != null ? e.InnerException! : e;
}
