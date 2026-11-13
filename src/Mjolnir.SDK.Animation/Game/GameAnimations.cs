using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The game's side of <see cref="Animations"/> and <see cref="CharacterAnimator"/>: the clips the
/// game has loaded, mods' clips and masks from their bundles, Mjolnir's masks from its own, the characters, and the animation
/// messages between players. Nothing here throws into the game or a mod: faults are logged as [SDK.Animation].
/// </summary>
internal sealed class GameAnimations : IAnimationSource
{
    public const string Source = "SDK.Animation";
    private const long ClipsReadAgain = 10_000; // ms: the game loads clips with what uses them
    private static readonly Dictionary<string, AnimationClip> s_clips = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Path, string Asset), Object?> s_assets = [];
    private static readonly HashSet<string> s_warned = [];
    private static List<GameClip> s_list = [];
    private static long s_readAt; // 0: never read
    private static bool s_listening;

    /// <summary>Connects the model to the game (the module's start, phase A: no Unity objects yet).</summary>
    public static void Connect()
    {
        Dispatcher.Hook(); // the dispatcher's queue runs before the animations each frame
        Animations.Game = new GameAnimations();
        GameLoop.Update += Tick;
    }

    public IReadOnlyList<GameClip> Clips
    {
        get
        {
            Guard("reading the game's clips", () => ReadClips(force: false));
            return s_list;
        }
    }

    public IAnimatedCharacter? Character(nint key)
    {
        if (key == 0) return null;
        GameAnimatedCharacter? character = null;
        Guard($"finding the character {key:X}", () => character = new GameAnimatedCharacter(new Character(key)));
        return character;
    }

    public bool Announce(nint key, AnimationInfo animation, AnimationOptions options)
    {
        if (PlatformNetwork.Game is not { IsMultiplayer: true } network) return false;
        bool sent = false;
        Guard($"sending {animation}", () =>
        {
            uint id = GameAnimatedCharacter.NetworkId(new Character(key));
            if (id != 0) sent = network.SendToAll(AnimationWire.Channel, AnimationWire.Play(id, animation.FullName, options));
        });
        return sent;
    }

    public void AnnounceStop(nint key)
    {
        if (PlatformNetwork.Game is not { IsMultiplayer: true } network) return;
        Guard("sending a stop", () =>
        {
            uint id = GameAnimatedCharacter.NetworkId(new Character(key));
            if (id != 0) network.SendToAll(AnimationWire.Channel, AnimationWire.Stop(id));
        });
    }

    /// <summary>The clip of an animation: a game clip by its name, a mod's from its bundle; null (warned once) without it.</summary>
    public static AnimationClip? Clip(AnimationInfo animation)
    {
        if (animation is ModAnimation mod)
        {
            var clip = Asset<AnimationClip>(mod.BundlePath, mod.Asset, mod.ToString());
            if (clip != null && mod.Length == TimeSpan.Zero) mod.SetLength(clip.length);
            return clip;
        }
        if (s_clips.TryGetValue(animation.Name, out var known) && known != null) return known;
        ReadClips(force: true); // loaded since, or the old object went with a scene
        if (s_clips.TryGetValue(animation.Name, out known) && known != null) return known;
        WarnOnce($"Animations: the game has no clip '{animation.Name}' loaded");
        return null;
    }

    /// <summary>The avatar mask of a mask: Mjolnir's from its own bundle, a mod's from the mod's; null (warned once) without it.</summary>
    public static AvatarMask? Mask(AnimationMask mask)
    {
        string path = mask.BundlePath ?? Path.Combine(Path.GetDirectoryName(typeof(GameAnimations).Assembly.Location) ?? "", "bundles", "masks");
        return Asset<AvatarMask>(path, mask.Asset, mask.ToString());
    }

    // An asset of a bundle, the bundle loaded once for every module (SharedBundles).
    private static T? Asset<T>(string path, string asset, string owner) where T : Object
    {
        if (s_assets.TryGetValue((path, asset), out var known)) return known?.TryCast<T>();
        T? found = null;
        var bundle = SharedBundles.Get(path, () => LoadBundle(path, owner));
        if (bundle != null)
        {
            found = bundle.LoadAsset(asset, Il2CppType.Of<T>())?.TryCast<T>();
            if (found == null) WarnOnce($"Animations: {owner}: {Path.GetFileName(path)} has no {typeof(T).Name} '{asset}'");
            else found.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }
        s_assets[(path, asset)] = found;
        return found;
    }

    private static AssetBundle? LoadBundle(string path, string owner)
    {
        if (!File.Exists(path))
        {
            WarnOnce($"Animations: {owner}: no bundle {path}");
            return null;
        }
        // The game's own way of loading a bundle (the synchronous one is not in the game); asking for the bundle waits for it.
        var bundle = AssetBundle.LoadFromFileAsync(path)?.assetBundle;
        if (bundle == null) WarnOnce($"Animations: {owner}: {SharedBundles.WhyNotLoaded(path)}");
        else SdkLog.Info(Source, $"Animations: bundle {Path.GetFileName(path)} loaded");
        return bundle;
    }

    private static void ReadClips(bool force)
    {
        long now = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
        if (!force && s_readAt != 0 && now - s_readAt < ClipsReadAgain) return;
        s_readAt = now;
        var list = new List<GameClip>();
        s_clips.Clear();
        foreach (var clip in Resources.FindObjectsOfTypeAll<AnimationClip>())
        {
            if (clip == null || string.IsNullOrEmpty(clip.name) || s_clips.ContainsKey(clip.name)) continue;
            if (ModAnimations.Shared.All.Count > 0 && IsModClip(clip)) continue;
            s_clips[clip.name] = clip;
            list.Add(new GameClip(clip.name, clip.length));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        s_list = list;
    }

    // A clip some mod's bundle brought: it is that mod's animation, not one of the game's.
    private static bool IsModClip(AnimationClip clip) =>
        s_assets.Values.Any(a => a != null && a.Pointer == clip.Pointer);

    // Every frame: the animations move on, the platform's network is listened to once it is there, and the world's skeletons
    // are written for the kit.
    private static void Tick()
    {
        if (!s_listening && PlatformNetwork.Game is { } network)
        {
            s_listening = true;
            network.Listen(AnimationWire.Channel, Received);
        }
        Animations.Tick();
        GameAnimatedCharacter.ReleaseLost();
        WriteRigs();
        if (GameClips.Enabled) Guard("writing the game's clips", GameClips.Tick);
    }

    // The game's skeletons for the Mjolnir kit, once its character definitions are loaded (the main menu has them): looked for
    // every 15 s, a few times at most.
    private static void WriteRigs()
    {
        long now = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
        if (s_rigLooks >= RigLooks || now - s_rigLookedAt < RigLookEvery) return;
        s_rigLookedAt = now;
        s_rigLooks++;
        Guard("writing the skeletons", () =>
        {
            if (Rigs.ExportAll()) s_rigLooks = RigLooks;
        });
    }

    /// <summary>Looks for the skeletons again (the kit's models were turned on): at once, then as at the start.</summary>
    public static void LookForRigsAgain()
    {
        Rigs.ExportAgain();
        s_rigLooks = 0;
        s_rigLookedAt = 0;
    }

    private const long RigLookEvery = 15_000; // ms
    private const int RigLooks = 12;
    private static long s_rigLookedAt;
    private static int s_rigLooks;

    private static void Received(int sender, byte[] data)
    {
        if (!AnimationWire.TryRead(data, ModAnimations.Shared.Mask, out uint id, out string? name, out var options))
        {
            WarnOnce($"Animations: a message from player {sender} this side cannot read (another Mjolnir, or a mask it does not have)");
            return;
        }
        if (GameAnimatedCharacter.ByNetworkId(id) is not { } character) return; // not streamed in here
        // Only a character's owner sends its animations: one aimed at this side's own player came from no Mjolnir.
        if (LocalPlayer() is { } local && local.Pointer == character.Pointer) return;
        var animator = CharacterAnimator.Of(character.Pointer);
        if (name == null)
        {
            animator.StopRemote();
            return;
        }
        if (Animations.Find(name) is not { } animation)
        {
            WarnOnce($"Animations: player {sender} played '{name}', which this side does not have");
            return;
        }
        animator.PlayRemote(animation, options!);
    }

    /// <summary>The local player's character, null without one.</summary>
    public static PlayerCharacter? LocalPlayer()
    {
        try
        {
            return GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Guard(string what, Action action) => GameGuard.Run(Source, what, action);

    private static void WarnOnce(string message)
    {
        if (s_warned.Add(message)) SdkLog.Warn(Source, message);
    }
}
