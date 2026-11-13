using System;

namespace Mjolnir.SDK;

/// <summary>
/// How an animation plays (<see cref="CharacterAnimator.Play"/>). Every value has a default: <c>new AnimationOptions { Loop = true }</c>.
/// </summary>
public sealed class AnimationOptions
{
    private readonly double _speed = 1;
    private readonly float? _moveSpeed;

    /// <summary>The defaults: once, at speed 1, the whole body, 0.2 s in and out, the character standing still.</summary>
    public static AnimationOptions Default { get; } = new();

    /// <summary>Plays the clip again and again until <see cref="Duration"/> or <see cref="AnimationPlayback.Stop"/>.</summary>
    public bool Loop { get; init; }

    /// <summary>How fast the clip plays: 1 as made, 2 twice as fast. Above 0.</summary>
    public double Speed
    {
        get => _speed;
        init => _speed = value > 0 && double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(Speed), value, "a speed above 0");
    }

    /// <summary>How long the animation takes to come in over the game's own; 0.2 s by default.</summary>
    public TimeSpan FadeIn { get; init; } = TimeSpan.FromSeconds(0.2);

    /// <summary>How long it takes to give the character back to the game's animation, at its end or when stopped; 0.2 s by default.</summary>
    public TimeSpan FadeOut { get; init; } = TimeSpan.FromSeconds(0.2);

    /// <summary>How long it plays, fade out included; null for the clip's own length (or, looping, until stopped).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The bones it moves (<see cref="AnimationMask.UpperBody"/>); null for the whole body.</summary>
    public AnimationMask? Mask { get; init; }

    /// <summary>
    /// Whether the clip moves the character, as the game's own rolls and attacks do: a step, a dash, a jump forward. The game's
    /// movement takes it, so the character stops at walls and keeps to the ground. Off by default: the clip plays on the spot.
    /// On another player's machine the character's place comes from the network, as for any movement.
    /// </summary>
    public bool RootMotion { get; init; }

    /// <summary>
    /// How much of the character's own walking goes through while it plays, from 0 (standing) to 1 (as ever): the player's, or
    /// a villager's on its way, who goes on along it once the animation ends. By default 0 for the whole body, whose legs would
    /// slide, and 1 with a <see cref="Mask"/>, whose legs walk.
    /// </summary>
    public float? MoveSpeed
    {
        get => _moveSpeed;
        init => _moveSpeed = value is null or (>= 0 and <= 1) ? value : throw new ArgumentOutOfRangeException(nameof(MoveSpeed), value, "from 0 to 1");
    }

    internal float EffectiveMoveSpeed => MoveSpeed ?? (Mask == null ? 0f : 1f);

    /// <summary>The same options for another player's machine: the network moves the character there, so no root motion and no hold.</summary>
    internal AnimationOptions ForRemote() => new()
    {
        Loop = Loop, Speed = Speed, FadeIn = FadeIn, FadeOut = FadeOut, Duration = Duration, Mask = Mask, RootMotion = false, MoveSpeed = 1,
    };
}
