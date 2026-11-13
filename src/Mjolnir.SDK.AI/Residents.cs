using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The villagers of the loaded world and jobs of a mod's own for them. Villagers are the game's: they come and go as it
/// spawns them, loads them with a save, or loses them; <see cref="Added"/> and <see cref="Removed"/> say so. A job
/// (<see cref="AddJob"/>) is one more thing a villager may do, among its own needs and work, when the job asks for it
/// more than they do. The villagers' minds run on the host of a world (in single player, the player).
/// <para>
/// A villager is a <see cref="Resident"/> in the SDK, the game's <c>SSSGame.Villager</c>.
/// </para>
/// </summary>
/// <example><code>
/// Residents.AddJob("greet", priority: v => Wants(v) ? 12 : -1)
///          .MoveTo(v => PlayerPosition(), distance: 3)
///          .Wait(TimeSpan.FromSeconds(2))
///          .Do(v => Greeted(v));
/// </code></example>
public static class Residents
{
    private static readonly ModHandlers<EventHandler<ResidentEventArgs>> s_added = new("Residents.Added");
    private static readonly ModHandlers<EventHandler<ResidentEventArgs>> s_removed = new("Residents.Removed");

    /// <summary>The villagers of the loaded world, in the order they came; empty outside a world. Main thread.</summary>
    public static IReadOnlyList<Resident> All => ResidentRoster.All;

    /// <summary>
    /// Raised on the main thread when a villager comes into the world: spawned, loaded with a save, or seen for the first
    /// time by the module.
    /// </summary>
    public static event EventHandler<ResidentEventArgs>? Added
    {
        add => s_added.Add(value);
        remove => s_added.Remove(value);
    }

    /// <summary>Raised on the main thread when a villager leaves the world: it died or left, or its world unloaded.</summary>
    public static event EventHandler<ResidentEventArgs>? Removed
    {
        add => s_removed.Add(value);
        remove => s_removed.Remove(value);
    }

    /// <summary>
    /// Asks the game for a new villager near <paramref name="position"/>, as its own debug tools add one (on walkable
    /// ground within 3 m; none where there is none), with the game's welcome of a new villager; it comes with
    /// <see cref="Added"/>. False outside a world or off the main thread.
    /// </summary>
    public static bool Spawn(Vector3 position) => GameVillagers.Spawn(position);

    /// <summary>
    /// Adds a job of the calling mod; give its steps with <see cref="ResidentJob.MoveTo"/>, <see cref="ResidentJob.Wait"/>
    /// and <see cref="ResidentJob.Do"/>. <paramref name="name"/> is letters, digits and underscores, unique in the mod.
    /// <paramref name="priority"/> says how much a villager wants the job now; it is asked about once a second for every
    /// villager and whenever a villager picks what to do, on the main thread, so it should be quick. A villager takes the
    /// job when it asks more than every other quest of the villager, within a second or so of asking. In a working
    /// settlement the game's own ask 21 for sleep at its hour, 15 for work, 13 to complain about a workplace, 11 to idle at
    /// one, 2 when a cave collapses, 1 to complain and 0 to talk to others, and -1 when they do not want the villager: a job
    /// that asks 12 takes only those with nothing to do, one that asks 16 takes a worker from its work. Return -1 or less
    /// when the job is not for the villager now.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or the mod has a job of that
    /// name.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ResidentJob AddJob(string name, Func<Resident, float> priority) =>
        JobRegistry.Shared.Add(CallingMod.Of(Assembly.GetCallingAssembly()), name, priority);

    internal static void RaiseAdded(Resident resident) => s_added.Raise(h => h(null, new ResidentEventArgs(resident)));

    internal static void RaiseRemoved(Resident resident) => s_removed.Raise(h => h(null, new ResidentEventArgs(resident)));
}
