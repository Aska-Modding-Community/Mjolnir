using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The interactions mods added (no Unity: unit-tested), owned by the calling mod as jobs are. The game side attaches each to
/// its object as it is added and follows them every frame.
/// </summary>
internal sealed class InteractionRegistry(Func<Assembly, string?> owner)
{
    private readonly object _lock = new();
    private readonly List<ModInteraction> _all = [];

    // The villager whose job's priority the SDK is asking on this thread: its own turn at an interaction leaves that one free
    // for it, else a job asking for the nearest free one would stop asking as its villager takes its turn, and lose it.
    [ThreadStatic] private static Resident? t_asking;

    /// <summary>The registry the public API fills.</summary>
    public static InteractionRegistry Shared { get; } = new(assembly => ModStringsRegistry.ForAssembly(assembly)?.ModId);

    /// <summary>The live ones, gone ones dropped.</summary>
    public IReadOnlyList<ModInteraction> All
    {
        get
        {
            lock (_lock)
            {
                if (_all.RemoveAll(i => !i.IsAvailable) > 0) ForgetGoneBuildings();
                return [.. _all];
            }
        }
    }

    private readonly List<(string Building, string Name, Assembly Owner, Action<ModInteraction> SetUp)> _kinds = [];
    private readonly Dictionary<(int Kind, nint Building), ModInteraction> _onBuildings = [];

    /// <summary>How many buildings' interactions are known (unit tests: gone ones are let go).</summary>
    internal int BuildingsKnown
    {
        get
        {
            lock (_lock) return _onBuildings.Count;
        }
    }

    /// <summary>Whether any mod put interactions on a kind of building.</summary>
    public bool HasBuildings
    {
        get
        {
            lock (_lock) return _kinds.Count > 0;
        }
    }

    public void AddToBuildings(Assembly caller, string building, string name, Action<ModInteraction> setUp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(building);
        CheckName(name);
        ArgumentNullException.ThrowIfNull(setUp);
        if (owner(caller) == null) throw new InvalidOperationException($"{caller.GetName().Name} is not a mod: only a mod adds interactions");
        lock (_lock) _kinds.Add((building, name, caller, setUp));
    }

    /// <summary>
    /// A building of <paramref name="kind"/> stood up in the world (its object's identity <paramref name="building"/>): each
    /// mod's interaction for that kind goes on it once, settled by the mod's code.
    /// </summary>
    public void BuildingAppeared(nint building, string kind, Func<ModInteraction, IInteractionTarget> attach)
    {
        List<(int Index, string Name, Assembly Owner, Action<ModInteraction> SetUp)> wanted = [];
        lock (_lock)
        {
            ForgetGoneBuildings();
            for (int i = 0; i < _kinds.Count; i++)
            {
                if (!string.Equals(_kinds[i].Building, kind, StringComparison.Ordinal)) continue;
                if (_onBuildings.TryGetValue((i, building), out var known) && known.IsAvailable) continue;
                wanted.Add((i, _kinds[i].Name, _kinds[i].Owner, _kinds[i].SetUp));
            }
        }
        foreach (var (index, name, mod, setUp) in wanted)
        {
            var interaction = Add(mod, name, attach);
            lock (_lock) _onBuildings[(index, building)] = interaction;
            ModCall.Run(mod, $"{interaction} set-up", setUp, interaction);
        }
    }

    // Buildings dismantled or unloaded: their interactions are gone, and the identity may come back as another building.
    private void ForgetGoneBuildings()
    {
        List<(int, nint)>? gone = null;
        foreach (var (key, interaction) in _onBuildings)
        {
            if (!interaction.IsAvailable) (gone ??= []).Add(key);
        }
        if (gone == null) return;
        foreach (var key in gone) _onBuildings.Remove(key);
    }

    private static void CheckName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"'{name}' is not a name of letters, digits and underscores", nameof(name));
    }

    public ModInteraction Add(Assembly caller, string name, Func<ModInteraction, IInteractionTarget> attach)
    {
        CheckName(name);
        string modId = owner(caller) ?? throw new InvalidOperationException($"{caller.GetName().Name} is not a mod: only a mod adds interactions");
        var interaction = new ModInteraction(modId, name, caller);
        interaction.Attach(attach(interaction));
        lock (_lock) _all.Add(interaction);
        return interaction;
    }

    public bool Remove(ModInteraction interaction)
    {
        bool removed = interaction.Remove();
        lock (_lock)
        {
            _all.Remove(interaction);
            ForgetGoneBuildings();
        }
        return removed;
    }

    /// <summary>
    /// The villager whose job's priority is asked on this thread now (set around the mod's priority function): an interaction
    /// it has its turn at counts as free for it in <see cref="Nearest"/>.
    /// </summary>
    public static Resident? Asking
    {
        get => t_asking;
        set => t_asking = value;
    }

    public ModInteraction? Nearest(Assembly caller, (float X, float Y, float Z) position, string name, float radius)
    {
        string? modId = owner(caller);
        if (modId == null) return null;
        ModInteraction? best = null;
        float bestDistance = radius;
        foreach (var interaction in All)
        {
            if (interaction.ModId != modId || interaction.Name != name || !interaction.IsFreeFor(t_asking)) continue;
            var p = interaction.PositionXyz;
            float dx = p.X - position.X, dz = p.Z - position.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance <= bestDistance)
            {
                best = interaction;
                bestDistance = distance;
            }
        }
        return best;
    }
}
