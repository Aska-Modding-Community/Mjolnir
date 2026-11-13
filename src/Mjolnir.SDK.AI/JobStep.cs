using System;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>A step of a job.</summary>
internal abstract record JobStep;

/// <summary>Walks to a place the mod gives when the step starts, until within <see cref="Distance"/>.</summary>
internal sealed record MoveStep(Func<Resident, Vector3> Place, float Distance) : JobStep;

/// <summary>Stays for a while.</summary>
internal sealed record WaitStep(TimeSpan Duration) : JobStep;

/// <summary>Runs the mod's code once.</summary>
internal sealed record DoStep(Action<Resident> Action) : JobStep;

/// <summary>Plays an animation, until it ends or for its duration.</summary>
internal sealed record PlayStep(Func<AnimationInfo?> Find, string Name, AnimationOptions Options, TimeSpan? Duration) : JobStep;

/// <summary>Walks to the interaction the mod gives when the step starts, until within its distance (the first half of Use).</summary>
internal sealed record ApproachStep(Func<Resident, ModInteraction?> Find) : JobStep;

/// <summary>Takes a turn at the interaction the walk went to and stays at it for its duration (the second half of Use).</summary>
internal sealed record UseStep(string? Animation, AnimationOptions Options, TimeSpan Duration) : JobStep;
