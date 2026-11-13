using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// A job of a mod's own for villagers (<see cref="Residents.AddJob"/>): its steps, in order, and its priority. Every
/// villager has it among the game's own quests (its needs, its work, complaining…), and takes the one whose priority is
/// highest at the moment; a job with a higher priority than the one a villager runs takes the villager from it, as the
/// game's own do. After its last step the job ends and the villager picks again, so a job that should not run again at
/// once lowers its priority (a cooldown, a done flag). Steps are given in <see cref="Mod.OnLoad"/>; the job's first run
/// in a world uses them.
/// </summary>
public sealed class ResidentJob
{
    private readonly object _lock = new();
    private readonly List<JobStep> _steps = [];
    private JobStep[] _snapshot = [];

    internal ResidentJob(string modId, string name, Assembly owner, Func<Resident, float> priority)
    {
        ModId = modId;
        Name = name;
        Owner = owner;
        Priority = priority;
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>Its name in the mod.</summary>
    public string Name { get; }

    internal Assembly Owner { get; }

    internal Func<Resident, float> Priority { get; }

    /// <summary>Its steps in order, as they are now: a list made anew with each step added, never changed after.</summary>
    internal IReadOnlyList<JobStep> Steps
    {
        get
        {
            lock (_lock) return _snapshot;
        }
    }

    /// <summary>Changes with each step added: the game side makes the job's state machine again.</summary>
    internal int Version { get; private set; }

    /// <summary>
    /// Adds a step: the villager walks to <paramref name="place"/> (asked when the step starts) until it is within
    /// <paramref name="distance"/> metres, or gives up after a minute. Returns the job.
    /// </summary>
    public ResidentJob MoveTo(Func<Resident, Vector3> place, float distance = 2f)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (!(distance > 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "a distance above 0");
        return Add(new MoveStep(place, distance));
    }

    /// <summary>Adds a step: the villager stays where it is for <paramref name="duration"/>. Returns the job.</summary>
    public ResidentJob Wait(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "a duration of 0 or more");
        return Add(new WaitStep(duration));
    }

    /// <summary>Adds a step: runs <paramref name="action"/> for the villager once, on the main thread. Returns the job.</summary>
    public ResidentJob Do(Action<Resident> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Add(new DoStep(action));
    }

    /// <summary>
    /// Adds a step: the villager plays <paramref name="animation"/> where it stands (<see cref="CharacterAnimator.Play"/>) and the
    /// step is over when it ends; with a <paramref name="duration"/>, when that time is up (a looping animation plays until
    /// then). Another quest taking the villager stops it. Returns the job.
    /// </summary>
    /// <remarks>
    /// This is the way to have a villager stop and act: the job takes it from its work or rest, so it stands while it plays.
    /// <c>resident.Animator.Play</c> alone plays over whatever the villager goes on doing: right for a gesture through the
    /// upper-body mask, not for an animation of the whole body while it walks.
    /// </remarks>
    public ResidentJob Play(AnimationInfo animation, AnimationOptions? options = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(animation);
        return Play(() => animation, animation.FullName, options, duration);
    }

    /// <summary>
    /// Adds a step that plays an animation by its name (<see cref="Animations.Find"/>), looked up when the step starts: the
    /// game's clips are not known yet in <see cref="Mod.OnLoad"/>, where jobs are made. Without such an animation the step is
    /// over at once. Returns the job.
    /// </summary>
    public ResidentJob Play(string animation, AnimationOptions? options = null, TimeSpan? duration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animation);
        return Play(() => Animations.Find(animation), animation, options, duration);
    }

    /// <summary>
    /// Adds a step: the villager uses an interaction (<see cref="Interactions"/>), the one <paramref name="interaction"/> gives
    /// when the step starts (<see cref="Interactions.Nearest"/>, or one the mod keeps). It walks up to it (within its
    /// <see cref="ModInteraction.Distance"/>), waits for its turn while the interaction is full (a minute at most), turns to
    /// it and stays at it for <paramref name="duration"/>, playing <paramref name="animation"/> if one is named (by its name,
    /// as <see cref="Play(string, AnimationOptions?, TimeSpan?)"/>); then <see cref="ModInteraction.Used"/> is raised. With no
    /// interaction, a removed or disabled one, or a <see cref="ModInteraction.Using"/> handler cancelling, the step is over
    /// without a use; another quest taking the villager ends it without one too. Returns the job.
    /// </summary>
    public ResidentJob Use(Func<Resident, ModInteraction?> interaction, TimeSpan duration, string? animation = null, AnimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "a duration of 0 or more");
        if (animation != null) ArgumentException.ThrowIfNullOrWhiteSpace(animation);
        return Add(new ApproachStep(interaction), new UseStep(animation, options ?? AnimationOptions.Default, duration));
    }

    private ResidentJob Play(Func<AnimationInfo?> find, string name, AnimationOptions? options, TimeSpan? duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "a duration of 0 or more");
        return Add(new PlayStep(find, name, options ?? AnimationOptions.Default, duration));
    }

    /// <summary>
    /// The job's priority for a villager now: the mod's function, through the loader's fault isolation (a throw disables
    /// the mod, and then the job is never taken). Not a number counts as -1. Meanwhile an interaction the villager has its
    /// turn at counts as free for it (<see cref="Interactions.Nearest"/>), so a job that asks for a free one keeps asking
    /// while its villager is at it.
    /// </summary>
    internal float PriorityFor(Resident resident)
    {
        var outer = InteractionRegistry.Asking;
        InteractionRegistry.Asking = resident;
        try
        {
            return ModCall.Evaluate(Priority, resident, $"{this} priority", out float value) && !float.IsNaN(value) ? value : -1f;
        }
        finally
        {
            InteractionRegistry.Asking = outer;
        }
    }

    // One version for the steps given together: the game side makes the job's state machine again once.
    private ResidentJob Add(params JobStep[] steps)
    {
        lock (_lock)
        {
            _steps.AddRange(steps);
            _snapshot = [.. _steps];
            Version++;
        }
        return this;
    }

    public override string ToString() => $"{ModId}.{Name}";
}
