using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK;

/// <summary>
/// What the animation model sees of the game (the animation module's Game/ folder; fakes in the unit tests), as
/// <see cref="IStatsSource"/> is for characters' stats. Without that module nothing plays: characters are not available and
/// <see cref="Animations.All"/> holds only what mods added. Called on the main thread.
/// </summary>
internal interface IAnimationSource
{
    /// <summary>The game's clips, empty until the game has loaded them; one record per name.</summary>
    IReadOnlyList<GameClip> Clips { get; }

    /// <summary>The character behind a key (its IL2CPP object), null when there is none.</summary>
    IAnimatedCharacter? Character(nint key);

    /// <summary>Tells the other players that a character of this machine started an animation; false when nobody hears it.</summary>
    bool Announce(nint key, AnimationInfo animation, AnimationOptions options);

    /// <summary>Tells the other players that the character's animation stopped.</summary>
    void AnnounceStop(nint key);
}

/// <summary>A clip of the game: its asset name and its length in seconds.</summary>
internal readonly record struct GameClip(string Name, float Length);

/// <summary>One character of the game, as its animation sees it.</summary>
internal interface IAnimatedCharacter
{
    /// <summary>False once the game destroyed the character.</summary>
    bool IsAlive { get; }

    /// <summary>Its skeleton's name (<c>ragnar</c>, <c>aska</c>, <c>peasant_male</c>, <c>peasant_female</c>), "" when unknown.</summary>
    string Rig { get; }

    /// <summary>
    /// Starts a clip over the character's own animation, at weight 0, through the mask when one is given, looping or played
    /// once whatever the clip itself says; null when the clip or the mask cannot be had (logged by the game side).
    /// </summary>
    IClipPlayer? Begin(AnimationInfo animation, AnimationMask? mask, bool loop, double speed);

    /// <summary>Plays one of the game's emotes the game's way; false when the character has none of that kind.</summary>
    bool PlayEmote(Emote emote);

    /// <summary>
    /// Lets the clip move the character this frame (the game's own root motion: its ground and walls). Called every frame
    /// while the clip moves it; <paramref name="hold"/> false gives the game its own value back.
    /// </summary>
    void SetRootMotion(bool hold);

    /// <summary>How much of the character's own walking goes through, 0 to 1: the local player's, a villager's on its way; 1 gives it back.</summary>
    void SetMoveSpeed(float factor);

    /// <summary>A bone of the character's own skeleton by its name, null for none.</summary>
    Transform? Bone(string name);
}

/// <summary>A clip playing on a character. Disposing it takes it off.</summary>
internal interface IClipPlayer : IDisposable
{
    /// <summary>Seconds of the clip played so far (past its length while it loops), at its speed.</summary>
    double Time { get; }

    double Speed { set; }

    /// <summary>How much of the pose is the clip's, 0 to 1.</summary>
    float Weight { set; }
}
