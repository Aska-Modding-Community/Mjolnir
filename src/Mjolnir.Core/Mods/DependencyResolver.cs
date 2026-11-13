using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.Core.Mods;

internal sealed record ResolveResult(IReadOnlyList<ModDescriptor> Ordered, IReadOnlyList<(ModDescriptor Mod, ModSkip Reason)> Skipped);

/// <summary>
/// Decides which mods load and in what order. Pure: no I/O, no logging.
/// <list type="number">
/// <item>Duplicate ids: the first mod (in input order) wins.</item>
/// <item>A mod the player turned off is skipped; an optional dependency on it does not block.</item>
/// <item>A mod is skipped when a required dependency is missing or skipped, or any present dependency is outside its
/// version range; this repeats until nothing changes, so dependents of skipped mods are skipped too.</item>
/// <item>The rest is sorted topologically (dependencies first, ties by id, ordinal ignoring case);
/// mods left over sit in or behind a dependency cycle and are skipped.</item>
/// </list>
/// </summary>
internal static class DependencyResolver
{
    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;

    /// <param name="mods">The mods found, in discovery order.</param>
    /// <param name="turnedOff">The ids the player turned off (<see cref="ModList"/>); null for none.</param>
    public static ResolveResult Resolve(IReadOnlyList<ModDescriptor> mods, IReadOnlySet<string>? turnedOff = null)
    {
        var skipped = new List<(ModDescriptor, ModSkip)>();
        var byId = new Dictionary<string, ModDescriptor>(Ids);
        foreach (var mod in mods)
        {
            if (byId.TryGetValue(mod.Id, out var first))
                skipped.Add((mod, new ModSkip(ModSkipKind.Duplicate, mod.Id, Detail: first.Location)));
            else
                byId.Add(mod.Id, mod);
        }

        var reasons = new Dictionary<string, ModSkip>(Ids);
        foreach (string id in byId.Keys)
        {
            if (turnedOff != null && turnedOff.Contains(id)) reasons[id] = new ModSkip(ModSkipKind.TurnedOff);
        }
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var mod in byId.Values)
            {
                if (reasons.ContainsKey(mod.Id)) continue;
                var reason = Unloadable(mod, byId, reasons);
                if (reason == null) continue;
                reasons[mod.Id] = reason;
                changed = true;
            }
        }

        var remaining = byId.Values.Where(m => !reasons.ContainsKey(m.Id)).ToDictionary(m => m.Id, Ids);
        var dependents = remaining.Keys.ToDictionary(id => id, _ => new List<string>(), Ids);
        var pending = remaining.Keys.ToDictionary(id => id, _ => 0, Ids);
        foreach (var mod in remaining.Values)
        {
            foreach (var dep in mod.Dependencies.Where(d => remaining.ContainsKey(d.Id)).Select(d => d.Id).Distinct(Ids))
            {
                dependents[dep].Add(mod.Id);
                pending[mod.Id]++;
            }
        }

        var ready = new SortedSet<string>(pending.Where(p => p.Value == 0).Select(p => p.Key), Ids);
        var ordered = new List<ModDescriptor>();
        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            ordered.Add(remaining[id]);
            foreach (string next in dependents[id])
            {
                if (--pending[next] == 0) ready.Add(next);
            }
        }

        var cyclic = pending.Where(p => p.Value > 0).Select(p => p.Key).OrderBy(id => id, Ids).ToList();
        foreach (string id in cyclic)
            reasons[id] = new ModSkip(ModSkipKind.Cycle, string.Join(", ", cyclic));

        skipped.AddRange(byId.Values.Where(m => reasons.ContainsKey(m.Id)).Select(m => (m, reasons[m.Id])));
        return new ResolveResult(ordered, skipped);
    }

    private static ModSkip? Unloadable(ModDescriptor mod, Dictionary<string, ModDescriptor> byId, Dictionary<string, ModSkip> reasons)
    {
        foreach (var dep in mod.Dependencies)
        {
            if (Ids.Equals(dep.Id, mod.Id)) return new ModSkip(ModSkipKind.SelfDependency);
            bool present = byId.TryGetValue(dep.Id, out var target);
            if (present && reasons.TryGetValue(dep.Id, out var why))
            {
                if (!dep.Optional)
                    return new ModSkip(why.Kind == ModSkipKind.TurnedOff ? ModSkipKind.DependencyTurnedOff : ModSkipKind.DependencyNotLoaded, dep.Id);
                continue;
            }
            if (!present)
            {
                if (!dep.Optional) return new ModSkip(ModSkipKind.Missing, dep.Id, dep.Range.ToString());
                continue;
            }
            if (!dep.Range.Contains(target!.Version))
                return new ModSkip(ModSkipKind.WrongVersion, dep.Id, dep.Range.ToString(), target.VersionText);
        }
        return null;
    }
}
