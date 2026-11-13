using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The jobs mods added (no Unity: unit-tested), found by the calling assembly as a mod's strings are. The game side reads
/// it every second as it gives villagers the jobs they lack, and makes a job's state machine again when its
/// <see cref="ResidentJob.Version"/> changed.
/// </summary>
internal sealed class JobRegistry(Func<Assembly, string?> owner)
{
    private readonly object _lock = new();
    private readonly List<ResidentJob> _jobs = [];

    /// <summary>The registry the public API fills.</summary>
    public static JobRegistry Shared { get; } = new(assembly => ModStringsRegistry.ForAssembly(assembly)?.ModId);

    public IReadOnlyList<ResidentJob> Jobs
    {
        get
        {
            lock (_lock) return [.. _jobs];
        }
    }

    public ResidentJob Add(Assembly caller, string name, Func<Resident, float> priority)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"'{name}' is not a name of letters, digits and underscores", nameof(name));
        ArgumentNullException.ThrowIfNull(priority);
        string modId = owner(caller) ?? throw new InvalidOperationException($"{caller.GetName().Name} is not a mod: only a mod adds jobs");
        ResidentJob job;
        lock (_lock)
        {
            if (_jobs.Any(j => j.ModId == modId && j.Name == name)) throw new ArgumentException($"{modId}.{name} is a job of the mod already", nameof(name));
            job = new ResidentJob(modId, name, caller, priority);
            _jobs.Add(job);
        }
        return job;
    }
}
