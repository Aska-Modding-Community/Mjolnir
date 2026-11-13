using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// Interactions of a mod's own on buildings: something the player does at a building as at the game's own (the game's prompt
/// with the interact key, held for a moment, beside the building's own: a well's «Drink», a house's «Show radius») and that
/// villagers do in a job (<see cref="ResidentJob.Use"/>: they walk up to it, take their turn and act for a while). The mod's
/// code runs when it is used (<see cref="ModInteraction.Used"/>).
/// </summary>
/// <remarks>
/// The player uses an interaction on a building (one of the game's, or a mod's: <c>Buildings.Add</c> of the content module),
/// where the game outlines it and shows its card and the prompt as for its own; on any other object only villagers use it.
/// <see cref="AddToBuildings"/> puts one on every building of a kind as it stands up, those of a save too. An interaction goes
/// with its building, or with <see cref="Remove"/>. It belongs to the machine that made it: in multiplayer every player's mod
/// makes its own, the player uses it on their own machine, and villagers use it on the host, where their minds run.
/// </remarks>
/// <example><code>
/// // In OnLoad: an altar the player builds, prayed at by the player and by villagers.
/// var altar = Buildings.Add("altar", basedOn: "Item_Decoration_DecorativeBirch").WithModel("bundles/models", "Altar");
/// Interactions.AddToBuildings(altar.AssetName, "altar", a =>
/// {
///     a.WithText(Localization.Get("altar.pray")).WithHoldTime(TimeSpan.FromSeconds(1.5));
///     a.Used += (_, e) => Notify.Message(e.IsPlayer ? "You prayed" : $"{e.Resident!.Name} prayed");
/// });
/// // A villager praying keeps its turn at its altar: the altar it is at counts as free for its own job's priority.
/// Residents.AddJob("pray", r => Interactions.Nearest(r.Position, "altar") != null ? 12 : -1)
///          .Use(r => Interactions.Nearest(r.Position, "altar"), TimeSpan.FromSeconds(5), "peasant_male_conjuring");
/// </code></example>
public static class Interactions
{
    /// <summary>The interactions there are now, every mod's, in the order they were added. Main thread.</summary>
    public static IReadOnlyList<ModInteraction> All => InteractionRegistry.Shared.All;

    /// <summary>
    /// Puts an interaction of the calling mod on every building of a kind: <paramref name="building"/> is its asset name (a
    /// mod's building's <c>AssetName</c>, or one of the game's, such as <c>Item_Structures_WaterWellL1</c>). Each building
    /// of that kind gets its own <see cref="ModInteraction"/> named <paramref name="name"/> as it stands up in the world
    /// (built, or loaded with a save), and <paramref name="setUp"/> settles it (its texts, its events) on the main thread.
    /// Given in <see cref="Mod.OnLoad"/>, it reaches every building of the session.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or no building is named.</exception>
    /// <exception cref="InvalidOperationException">Not called by a mod.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AddToBuildings(string building, string name, Action<ModInteraction> setUp) =>
        InteractionRegistry.Shared.AddToBuildings(CallingMod.Of(Assembly.GetCallingAssembly()), building, name, setUp);

    /// <summary>
    /// Makes <paramref name="target"/> an interaction of the calling mod. <paramref name="name"/> is letters, digits and
    /// underscores, what the mod calls this kind of thing: several altars may share it (<see cref="Nearest"/> finds one).
    /// On a building (a structure's object, or an object inside it) the player can use it at once; settle its texts with
    /// <see cref="ModInteraction.WithText"/> and the others. On any other object only villagers use it. Main thread, in a
    /// running game (the object must be in the world).
    /// </summary>
    /// <exception cref="ArgumentException">The name is not letters, digits and underscores.</exception>
    /// <exception cref="InvalidOperationException">Not on the main thread, before the game is ready, or not called by a
    /// mod.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModInteraction Add(GameObject target, string name)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (!GameLoop.IsGameReady || !Dispatcher.IsMainThread)
            throw new InvalidOperationException("an interaction is added on the main thread of a running game (OnGameReady or later)");
        return InteractionRegistry.Shared.Add(CallingMod.Of(Assembly.GetCallingAssembly()), name, interaction => GameInteractions.Attach(target, interaction));
    }

    /// <summary>
    /// Takes an interaction away: its prompt goes, villagers on their way to it give up, and it is never used again. False
    /// for one already removed. Main thread.
    /// </summary>
    public static bool Remove(ModInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        return InteractionRegistry.Shared.Remove(interaction);
    }

    /// <summary>
    /// The calling mod's interaction named <paramref name="name"/> nearest to <paramref name="position"/> (across the ground)
    /// that can take one more user now: available, enabled and not full. Null for none, or none within
    /// <paramref name="radius"/> metres. Main thread.
    /// </summary>
    /// <remarks>
    /// Asked in a job's priority function (<see cref="Residents.AddJob"/>), an interaction the villager asked about has its
    /// turn at counts as free: the job keeps asking while the villager uses it, and the villager is not called away half way.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModInteraction? Nearest(Vector3 position, string name, float radius = float.PositiveInfinity) =>
        InteractionRegistry.Shared.Nearest(CallingMod.Of(Assembly.GetCallingAssembly()), (position.x, position.y, position.z), name, radius);
}
