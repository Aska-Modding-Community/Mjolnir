using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// Every animation characters can play: the game's own clips, by their asset names (<c>aska_wave</c>, <c>peasant_male_walk</c>,
/// <c>aska_dodge_front</c>…; Mjolnir's console lists them with <c>anims</c>), and the mods' own (<c>Animations.Add</c> in
/// the animation module, from a mod's AssetBundle). Play one with <c>LocalPlayer.Animator.Play</c> or <c>Resident.Animator.Play</c>.
/// </summary>
/// <remarks>
/// The characters of the game share one skeleton (the player's, the villagers' men and women), so a clip of one plays on the
/// others; their proportions differ, so a clip made for the player may look a little off on a villager.
/// </remarks>
public static class Animations
{
    private static readonly object s_lock = new();
    private static readonly List<AnimationInfo> s_mods = [];
    private static IAnimationSource? s_game;
    // Made anew on the main thread when the game's list changes, never changed after: other threads read them as they are.
    private static List<AnimationInfo> s_clips = [];
    private static Dictionary<string, AnimationInfo> s_clipsByName = new(StringComparer.Ordinal);
    private static int s_clipCount = -1;

    /// <summary>The game's side, set by the animation module's start.</summary>
    internal static IAnimationSource? Game
    {
        get => Volatile.Read(ref s_game);
        set
        {
            Volatile.Write(ref s_game, value);
            s_clipCount = -1;
        }
    }

    /// <summary>Seconds of a steady clock: the fades' and durations' time (tests replace it).</summary>
    internal static Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>
    /// The game's clips, then the mods' animations. The game's are known once it has loaded them, which it does at its start
    /// (on the main thread: another thread gets what was read there last).
    /// </summary>
    public static IReadOnlyList<AnimationInfo> All
    {
        get
        {
            ReadClips();
            lock (s_lock) return [.. s_clips, .. s_mods];
        }
    }

    /// <summary>A game clip by its asset name, or a mod's animation by its full name <c>&lt;mod id&gt;.&lt;name&gt;</c>; null for none.</summary>
    public static AnimationInfo? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ReadClips();
        if (s_clipsByName.TryGetValue(name, out var clip)) return clip;
        lock (s_lock) return s_mods.FirstOrDefault(m => string.Equals(m.FullName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A mod's animation joins the catalog (the animation module, as a mod adds it).</summary>
    internal static void Register(AnimationInfo animation)
    {
        lock (s_lock)
        {
            if (!s_mods.Contains(animation)) s_mods.Add(animation);
        }
    }

    /// <summary>Moves every animation playing on, once a frame on the main thread (the animation module's tick; tests call it).</summary>
    internal static void Tick() => CharacterAnimator.TickAll(Clock());

    internal static void ResetForTests()
    {
        lock (s_lock) s_mods.Clear();
        s_clips = [];
        s_clipsByName = new Dictionary<string, AnimationInfo>(StringComparer.Ordinal);
        s_clipCount = -1;
        CharacterAnimator.ResetForTests();
    }

    // The game's clips as objects: one per name, kept while the game's list is the same.
    private static void ReadClips()
    {
        if (Game is not { } game || !Threading.Dispatcher.IsMainThread) return;
        var clips = game.Clips;
        if (clips.Count == s_clipCount) return;
        var byName = new Dictionary<string, AnimationInfo>(StringComparer.Ordinal);
        var list = new List<AnimationInfo>(clips.Count);
        foreach (var clip in clips)
        {
            if (byName.ContainsKey(clip.Name)) continue;
            var info = s_clipsByName.GetValueOrDefault(clip.Name) ?? new AnimationInfo(clip.Name, null, clip.Length);
            byName[clip.Name] = info;
            list.Add(info);
        }
        s_clipsByName = byName;
        s_clips = list;
        s_clipCount = clips.Count;
    }
}

/// <summary>
/// An animation: one of the game's clips (<see cref="ModId"/> null, <see cref="Name"/> its asset name) or a mod's own. The same
/// object for the same animation, so they compare with ==.
/// </summary>
public class AnimationInfo
{
    private double _length;

    internal AnimationInfo(string name, string? modId, double length)
    {
        Name = name;
        ModId = modId;
        _length = length;
    }

    /// <summary>A game clip's asset name, or a mod's animation's name in its mod.</summary>
    public string Name { get; }

    /// <summary>The mod that added it; null for the game's.</summary>
    public string? ModId { get; }

    /// <summary>What <see cref="Animations.Find"/> takes: the asset name, or <c>&lt;mod id&gt;.&lt;name&gt;</c> for a mod's.</summary>
    public string FullName => ModId == null ? Name : $"{ModId}.{Name}";

    /// <summary>How long the clip is at speed 1; zero for a mod's animation whose clip is not loaded yet (it loads at its first play).</summary>
    public TimeSpan Length => TimeSpan.FromSeconds(Volatile.Read(ref _length));

    /// <summary>The clip's length once it is loaded (a mod's: the game side, at its first play).</summary>
    internal void SetLength(double length) => Volatile.Write(ref _length, length);

    public override string ToString() => FullName;
}

/// <summary>
/// Which bones an animation moves; the others keep the game's own animation (walking legs under a waving arm). Mjolnir has
/// <see cref="UpperBody"/>; a mod's own come from its AssetBundle (<c>AnimationMask.Add</c> in the animation module; the Mjolnir
/// kit makes them from the game's skeleton).
/// </summary>
public sealed class AnimationMask
{
    internal AnimationMask(string name, string? modId, string? bundlePath, string asset)
    {
        Name = name;
        ModId = modId;
        BundlePath = bundlePath;
        Asset = asset;
    }

    /// <summary>The spine, neck, head and arms: what is above the waist. The pelvis and the legs keep the game's animation.</summary>
    public static AnimationMask UpperBody { get; } = new("UpperBody", null, null, "UpperBody");

    /// <summary>Its name: the mask's name in its mod, or Mjolnir's.</summary>
    public string Name { get; }

    /// <summary>The mod that added it; null for Mjolnir's.</summary>
    public string? ModId { get; }

    /// <summary>The AssetBundle that has it (full path); null for Mjolnir's own bundle.</summary>
    internal string? BundlePath { get; }

    /// <summary>The mask's asset name in its bundle.</summary>
    internal string Asset { get; }

    public override string ToString() => ModId == null ? Name : $"{ModId}.{Name}";
}

/// <summary>The game's emotes, as the emote wheel and the feasts play them.</summary>
public enum Emote
{
    /// <summary>Sits down on the ground.</summary>
    Sit,

    /// <summary>Waves a hand.</summary>
    Wave,

    /// <summary>Cheers with raised arms.</summary>
    Cheer,

    /// <summary>Raises a drink, as at a feast.</summary>
    Skal,
}
