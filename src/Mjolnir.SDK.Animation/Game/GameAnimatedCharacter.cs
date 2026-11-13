using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SSSGame;
using SSSGame.AI;
using SSSGame.Controllers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// One character of the game for <see cref="CharacterAnimator"/>: its geometry's animator, its
/// emotes, its movement's root motion ratio, and its walking: the local player's drive, a villager's (or a creature's) controller.
/// </summary>
internal sealed class GameAnimatedCharacter : IAnimatedCharacter
{
    private const long LookAgainAfter = 1000; // ms: a character not found by its network id is looked for at most so often
    // Characters holding their walking or root motion: the model lets a hold go only while the character is there, so a
    // character gone (or its animator) is let go here, through what it held.
    private static readonly HashSet<GameAnimatedCharacter> s_holding = [];
    private static readonly Dictionary<uint, Character> s_byNetworkId = [];
    private static long s_lookedAt; // ms, 0: never

    private readonly Character _character;
    private readonly Animator? _animator;
    private readonly CharacterMovement? _movement;
    private readonly PlayerDrive? _drive;
    private readonly CreatureController? _controller;
    private readonly bool _villager;
    private bool _rootMotionHeld;
    private bool _driveHeld;
    private float? _speedBeforeHold;

    public GameAnimatedCharacter(Character character)
    {
        _character = character;
        _animator = character.GetComponentInChildren<CharacterGeometry>(true)?.Animator;
        _movement = character.GetComponentInChildren<CharacterMovement>(true);
        _drive = character.GetComponent<PlayerDrive>() ?? character.GetComponentInParent<PlayerDrive>();
        _villager = character.TryCast<Villager>() != null;
        if (_drive == null)
            _controller = character.GetComponent<CreatureController>() ?? character.GetComponentInChildren<CreatureController>(true);
        Rig = _animator == null ? "" : Rigs.NameOf(_animator);
    }

    public bool IsAlive => _character != null && _animator != null;

    public string Rig { get; }

    public IClipPlayer? Begin(AnimationInfo animation, AnimationMask? mask, bool loop, double speed)
    {
        if (_animator == null) return null;
        var clip = GameAnimations.Clip(animation);
        if (clip == null) return null;
        AvatarMask? avatarMask = null;
        if (mask != null && (avatarMask = GameAnimations.Mask(mask)) == null) return null;
        return ClipPlayer.Start(_animator, clip, avatarMask, loop, speed);
    }

    public bool PlayEmote(Emote emote)
    {
        var emotes = _character.GetComponentInChildren<CharacterEmotes>(true);
        if (emotes == null)
        {
            SdkLog.Warn(GameAnimations.Source, $"Animations: {_character.name} has no emotes");
            return false;
        }
        // Villagers' controller plays the feasts' emotes (seen in game: Emote_WaveFeast waves, Emote_Wave does nothing).
        string name = emote switch
        {
            Emote.Sit => "Emote_Sit",
            Emote.Wave => _villager ? "Emote_WaveFeast" : "Emote_Wave",
            Emote.Cheer => _villager ? "Emote_CheerFeast" : "Emote_Cheer",
            Emote.Skal => "Emote_SkalFeast",
            _ => "",
        };
        var moveset = Resources.FindObjectsOfTypeAll<EmoteMoveset>().FirstOrDefault(m => m.name == name);
        if (moveset == null)
        {
            SdkLog.Warn(GameAnimations.Source, $"Animations: the game has no emote {name} loaded");
            return false;
        }
        emotes.PlayEmote(moveset, true);
        return true;
    }

    public void SetRootMotion(bool hold)
    {
        if (_movement == null || (!hold && !_rootMotionHeld)) return;
        _movement.animationRootMotionRatio = hold ? 1f : 0f;
        _rootMotionHeld = hold;
        Held();
    }

    public void SetMoveSpeed(float factor)
    {
        if (_drive != null)
        {
            MovementHold.Set(_drive, factor);
            _driveHeld = factor < 1f;
            Held();
            return;
        }
        // A villager walks where its mind sends it at the controller's speed times movementSpeedMultiplier (read in
        // _GetMovementSpeed, written by the game only in the constructor): held, it stands on its
        // path and goes on along it once let go. The value before the hold comes back.
        if (_controller == null || (factor >= 1f && _speedBeforeHold == null)) return;
        _speedBeforeHold ??= _controller.movementSpeedMultiplier;
        if (factor >= 1f)
        {
            _controller.movementSpeedMultiplier = _speedBeforeHold.Value;
            _speedBeforeHold = null;
        }
        else _controller.movementSpeedMultiplier = _speedBeforeHold.Value * Math.Clamp(factor, 0f, 1f);
        Held();
    }

    private bool IsHolding => _driveHeld || _rootMotionHeld || _speedBeforeHold != null;

    private void Held()
    {
        if (IsHolding) s_holding.Add(this);
        else s_holding.Remove(this);
    }

    /// <summary>Every frame: lets go what a character no longer there still holds (a drive, a controller, a movement that outlived it).</summary>
    public static void ReleaseLost()
    {
        if (s_holding.Count == 0) return;
        foreach (var character in s_holding.Where(c => !c.IsAlive).ToList())
        {
            s_holding.Remove(character);
            GameGuard.Run(GameAnimations.Source, "letting go a character's hold", character.Release);
        }
    }

    // Through what it held, whatever became of the character: each part is asked whether it is still there itself.
    private void Release()
    {
        if (_drive != null && _driveHeld) MovementHold.Set(_drive, 1f);
        _driveHeld = false;
        if (_movement != null && _rootMotionHeld) _movement.animationRootMotionRatio = 0f;
        _rootMotionHeld = false;
        if (_controller != null && _speedBeforeHold is { } speed) _controller.movementSpeedMultiplier = speed;
        _speedBeforeHold = null;
    }

    public Transform? Bone(string name) => _animator == null ? null : Rigs.Bone(_animator, name);

    /// <summary>The id of the character's network object, 0 without one.</summary>
    public static uint NetworkId(Character character)
    {
        try
        {
            return character.Object?.Id.Raw ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The character of the world whose network object has that id, null for none. The characters are listed again only when
    /// one is not known (at most once a second), since another player's animations come often.
    /// </summary>
    public static Character? ByNetworkId(uint id)
    {
        if (id == 0) return null;
        if (s_byNetworkId.TryGetValue(id, out var known))
        {
            if (known != null && NetworkId(known) == id) return known;
            s_byNetworkId.Remove(id);
        }
        long now = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
        if (s_lookedAt != 0 && now - s_lookedAt < LookAgainAfter) return null;
        s_lookedAt = now;
        s_byNetworkId.Clear();
        foreach (var character in Object.FindObjectsOfType<Character>())
        {
            uint characterId = NetworkId(character);
            if (characterId != 0) s_byNetworkId.TryAdd(characterId, character);
        }
        return s_byNetworkId.TryGetValue(id, out known) ? known : null;
    }
}
