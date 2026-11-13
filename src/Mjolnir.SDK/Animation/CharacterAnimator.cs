using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK;

/// <summary>
/// A character's animations, the player's or a villager's alike (<c>LocalPlayer.Animator</c>, <c>Resident.Animator</c>): an
/// animation of the game or of a mod played over the game's own (<see cref="Play"/>), the game's emotes (<see cref="PlayEmote"/>),
/// and the character's bones, to put a mod's effect in a hand. An object belongs to one character: after a load take it again.
/// Without the character it is empty: nothing plays.
/// </summary>
/// <remarks>
/// <para>
/// One animation plays at a time: a new one takes over from the one playing, which fades out meanwhile. The game's own animation
/// goes on under it, and comes back as it ends.
/// </para>
/// <para>
/// A whole-body animation holds the character where it is, the player and a villager alike, and one through a mask lets it walk
/// (<see cref="AnimationOptions.MoveSpeed"/> says otherwise). A villager keeps what its own mind does meanwhile: held on its way,
/// it stands and then walks on; at work, it goes on working under the animation. So, for a villager:
/// <list type="bullet">
/// <item>to take it from what it does (bow, dance, cast where it stands, for as long as wanted), give it a job whose step plays
/// the animation (<c>Residents.AddJob(…).Play(animation)</c> in Mjolnir.SDK.AI): the job takes the villager by its priority,
/// as the game's own tasks do, and gives it back after;</item>
/// <item>for a short pause on its way (a bow to someone it meets), play a whole-body animation here: it stops, plays, and walks
/// on;</item>
/// <item>for a gesture without stopping (a wave while walking), play it through <see cref="AnimationMask.UpperBody"/>: the
/// legs stay the villager's.</item>
/// </list>
/// </para>
/// <para>
/// In multiplayer, an animation played here is played on the other players' machines too, where they run this mod (it is sent
/// once Mjolnir.SDK.Net is installed, as it is with Mjolnir). Emotes go the game's way: the character's owner plays them, the
/// game shows them to everyone.
/// </para>
/// </remarks>
/// <example><code>
/// LocalPlayer.Animator.Play(Animations.Find("aska_wave")!);
/// LocalPlayer.Animator.Play(dash, new AnimationOptions { RootMotion = true });
/// resident.Animator.PlayEmote(Emote.Cheer);
/// </code></example>
public sealed class CharacterAnimator
{
    private static readonly object s_lock = new();
    private static readonly GameObjectWrappers<CharacterAnimator> s_known = new(animator => animator._looked && animator._source is not { IsAlive: true });
    private static readonly List<AnimationPlayback> s_active = [];
    private static readonly CharacterAnimator s_none = new(0, null);

    private readonly object _lock = new();
    private IAnimatedCharacter? _source;
    private bool _looked;
    private AnimationPlayback? _current;

    private CharacterAnimator(nint key, IAnimatedCharacter? source)
    {
        Key = key;
        _source = source;
        _looked = source != null;
    }

    internal nint Key { get; }

    /// <summary>
    /// The animator of a character, one object per character; an empty one without it. Off the main thread the game's side is
    /// looked up at its first use there.
    /// </summary>
    internal static CharacterAnimator Of(nint key)
    {
        if (key == 0 || Animations.Game == null) return s_none;
        return s_known.Get(key, () => new CharacterAnimator(key, null))!;
    }

    /// <summary>What the game has of the character, looked up on the main thread.</summary>
    internal IAnimatedCharacter? Source
    {
        get
        {
            if (Key == 0) return null;
            if (!_looked && Dispatcher.IsMainThread)
            {
                _source = Animations.Game?.Character(Key);
                _looked = true;
            }
            return _source;
        }
    }

    /// <summary>Whether its character is in the world. Main thread: false on another until it was looked up there.</summary>
    public bool IsAvailable => Source is { IsAlive: true };

    /// <summary>
    /// The character's skeleton, named as in the Mjolnir kit: <c>ragnar</c> or <c>aska</c> (the player's two characters),
    /// <c>peasant_male</c>, <c>peasant_female</c> (villagers); "" without the character.
    /// </summary>
    public string Rig => Source is { IsAlive: true } source ? source.Rig : "";

    /// <summary>The animation playing now (fading out counts), null for none.</summary>
    public AnimationPlayback? Current
    {
        get
        {
            lock (_lock) return _current is { IsPlaying: true } current ? current : null;
        }
    }

    /// <summary>
    /// Plays an animation over the character's own: it comes in over <see cref="AnimationOptions.FadeIn"/>, plays as the
    /// options say and gives the character back to the game at its end. The animation playing before fades out. Called off the
    /// main thread, it starts on the next frame; without the character it ends at once (<see cref="AnimationPlayback.Ended"/>
    /// with <c>WasStopped</c>).
    /// </summary>
    public AnimationPlayback Play(AnimationInfo animation, AnimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(animation);
        var playback = Start(animation, options ?? AnimationOptions.Default, remote: false);
        if (Key != 0) Dispatcher.Run(() => Animations.Game?.Announce(Key, animation, playback.Options));
        return playback;
    }

    /// <summary>
    /// Plays one of the game's emotes, as the emote wheel does for the player and a feast for villagers; the game shows it to
    /// the other players. False without the character, or when it has no emote of that kind. Main thread.
    /// </summary>
    public bool PlayEmote(Emote emote)
    {
        if (!Dispatcher.IsMainThread || Source is not { IsAlive: true } source) return false;
        try
        {
            return source.PlayEmote(emote);
        }
        catch (Exception e)
        {
            SdkLog.Error("SDK", $"Animations: the emote {emote} on {this} failed: {e}");
            return false;
        }
    }

    /// <summary>Stops the animation playing (<see cref="AnimationPlayback.Stop"/>); nothing without one.</summary>
    public void Stop() => Current?.Stop();

    /// <summary>
    /// A bone of the character's own skeleton by its name (<c>Bip001 R Hand</c>, <c>Bip001 Head</c>, <c>Bone hand_weapon</c>;
    /// the Mjolnir kit's skeletons show them), to put a mod's effect on: not the copies its equipment carries. Null without
    /// the character or such a bone. Main thread.
    /// </summary>
    public Transform? Bone(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Dispatcher.IsMainThread && Source is { IsAlive: true } source ? source.Bone(name) : null;
    }

    public override string ToString() => Key == 0 ? "no character" : $"character {Key:X}";

    /// <summary>An animation another player's machine played on this character: shown here, never sent back.</summary>
    internal AnimationPlayback PlayRemote(AnimationInfo animation, AnimationOptions options) =>
        Start(animation, options.ForRemote(), remote: true);

    /// <summary>The animation another player's machine stopped.</summary>
    internal void StopRemote()
    {
        AnimationPlayback? current;
        lock (_lock) current = _current;
        if (current is { IsRemote: true }) current.Stop();
    }

    /// <summary>A playback of this character was stopped by a mod: the other machines hear of it.</summary>
    internal void Announced(AnimationPlayback playback)
    {
        if (Key != 0) Dispatcher.Run(() => Animations.Game?.AnnounceStop(Key));
    }

    /// <summary>A playback ended: it is no longer the character's.</summary>
    internal void Ended(AnimationPlayback playback)
    {
        lock (_lock)
        {
            if (_current == playback) _current = null;
        }
        lock (s_lock) s_active.Remove(playback);
    }

    internal static void TickAll(double now)
    {
        AnimationPlayback[] active;
        lock (s_lock) active = [.. s_active];
        foreach (var playback in active)
        {
            try
            {
                playback.Tick(now);
            }
            catch (Exception e)
            {
                SdkLog.Error("SDK", $"Animations: {playback} failed: {e}");
                playback.End(stopped: true);
            }
        }
    }

    internal static void ResetForTests()
    {
        s_known.Clear();
        lock (s_lock) s_active.Clear();
    }

    private AnimationPlayback Start(AnimationInfo animation, AnimationOptions options, bool remote)
    {
        var playback = new AnimationPlayback(this, animation, options, remote);
        AnimationPlayback? before;
        lock (_lock)
        {
            before = _current;
            _current = playback;
        }
        before?.Stop();
        lock (s_lock) s_active.Add(playback);
        return playback;
    }
}
