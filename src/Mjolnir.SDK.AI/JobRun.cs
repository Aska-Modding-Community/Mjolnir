using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.AI;

/// <summary>
/// One villager running one job (no Unity: unit-tested): which step it is at, since when, where a walk goes, and when a
/// step is over. The game side starts each step's state and asks <see cref="IsOver"/> every update.
/// </summary>
internal sealed class JobRun(ResidentJob job, Resident resident, IReadOnlyList<JobStep>? steps = null)
{
    /// <summary>A walk that has not arrived after this long moves on: the villager may be stuck.</summary>
    public static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A walk whose villager has stood still this long within <see cref="CloseEnough"/> metres beyond its distance is
    /// over: others stand in the way (seen in game: two villagers walking up to the player).
    /// </summary>
    public static readonly TimeSpan StandStill = TimeSpan.FromSeconds(3);

    /// <summary>How far beyond its distance a villager that stopped counts as arrived.</summary>
    public const float CloseEnough = 3f;

    // Where the villager last moved from, and when (a step of more than StillWithin metres).
    private const float StillWithin = 0.25f;
    private (float X, float Y, float Z) _still;
    private double _stillSince;

    public ResidentJob Job { get; } = job;

    public Resident Resident { get; } = resident;

    /// <summary>The job's steps as they were when its state machine was made (by default, as they are now).</summary>
    public IReadOnlyList<JobStep> Steps { get; } = steps ?? job.Steps;

    /// <summary>The step under way, -1 before the first.</summary>
    public int Index { get; private set; } = -1;

    public JobStep? Step => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;

    /// <summary>When the step started, in seconds of the game's clock.</summary>
    public double Started { get; private set; }

    /// <summary>Where a walk goes; null while the mod's place is not known (it threw).</summary>
    public (float X, float Y, float Z)? Target { get; set; }

    /// <summary>The animation a play step started; stopped when the step or the run ends.</summary>
    public AnimationPlayback? Playback { get; set; }

    /// <summary>The interaction a Use walks to and is at; null before the walk or when the mod gave none.</summary>
    public ModInteraction? Interaction { get; set; }

    // Whether this villager has its turn at the interaction, and since when.
    private bool _reserved;
    private double _usingSince;

    /// <summary>Whether the villager is at its interaction now (its turn taken, not over).</summary>
    public bool IsUsing => _reserved;

    public void Begin(int index, double now)
    {
        Playback?.Stop();
        Playback = null;
        LetGo();
        if (Steps.Count > index && index >= 0 && Steps[index] is ApproachStep) Interaction = null;
        Index = index;
        Started = now;
        Target = null;
        _still = Resident.PositionXyz;
        _stillSince = now;
    }

    /// <summary>
    /// Whether the step is over at <paramref name="now"/>: a wait after its duration, a walk once within its distance of
    /// its place (or close to it and standing still, or without a place, or after <see cref="MoveTimeout"/>), a piece of
    /// code at once (it ran as the step began). Asked every update, so a walk follows the villager's steps.
    /// </summary>
    public bool IsOver(double now)
    {
        double elapsed = now - Started;
        switch (Step)
        {
            case WaitStep wait:
                return elapsed >= wait.Duration.TotalSeconds;
            case PlayStep play:
                return play.Duration is { } duration ? elapsed >= duration.TotalSeconds : Playback is not { IsPlaying: true };
            case UseStep use:
                return Using(use, now);
            case ApproachStep when Interaction is not { IsAvailable: true, IsEnabled: true }:
                return true;
            case MoveStep or ApproachStep:
                float within = Step is MoveStep move ? move.Distance : Interaction!.Distance;
                if (Target is not { } target || elapsed >= MoveTimeout.TotalSeconds) return true;
                float distance = Distance(target);
                if (distance <= within) return true;
                var here = Resident.PositionXyz;
                if (Across(here, _still) > StillWithin)
                {
                    _still = here;
                    _stillSince = now;
                    return false;
                }
                return now - _stillSince >= StandStill.TotalSeconds && distance <= within + CloseEnough;
            default:
                return true;
        }
    }

    /// <summary>The run is over (its job ended, or another quest took the villager): an animation it started stops.</summary>
    public void End()
    {
        Playback?.Stop();
        Playback = null;
        LetGo();
    }

    // A Use: waits for its turn (a minute at most), asks the handlers, turns to it, plays, and after its time reports the use.
    private bool Using(UseStep use, double now)
    {
        var interaction = Interaction;
        if (interaction is not { IsAvailable: true })
        {
            LetGo();
            return true;
        }
        if (!_reserved)
        {
            if (!interaction.IsEnabled || now - Started >= MoveTimeout.TotalSeconds) return true;
            if (!interaction.TryReserve(Resident)) return false;
            _reserved = true;
            _usingSince = now;
            if (!interaction.RaiseUsing(Resident))
            {
                LetGo();
                return true;
            }
            Resident.Source.Face(interaction.PositionXyz);
            if (use.Animation != null)
            {
                if (Animations.Find(use.Animation) is { } animation) Playback = Resident.Animator.Play(animation, ForDuration(use.Options, use.Duration));
                else SdkLog.Warn("SDK.AI", $"Villagers: {Job} uses {interaction}: no animation '{use.Animation}'");
            }
        }
        if (now - _usingSince < use.Duration.TotalSeconds) return false;
        LetGo();
        interaction.RaiseUsed(Resident);
        return true;
    }

    // Its turn given back (the use ended, or was cut short).
    private void LetGo()
    {
        if (!_reserved) return;
        _reserved = false;
        Interaction?.Release(Resident);
    }

    /// <summary>The options to play an animation for a time: a looping one plays until then (a duration of its own wins).</summary>
    public static AnimationOptions ForDuration(AnimationOptions options, TimeSpan duration) =>
        options.Duration == null && options.Loop
            ? new AnimationOptions
            {
                Loop = true, Speed = options.Speed, FadeIn = options.FadeIn, FadeOut = options.FadeOut,
                Duration = duration, Mask = options.Mask, RootMotion = options.RootMotion, MoveSpeed = options.MoveSpeed,
            }
            : options;

    // Across the ground: a villager on a slope or a step stands a little above or below its place.
    private float Distance((float X, float Y, float Z) target) => Across(Resident.PositionXyz, target);

    private static float Across((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
