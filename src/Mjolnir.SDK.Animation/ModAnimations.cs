using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// Animations and masks of a mod's own, from its AssetBundle: clips made on the game's skeleton, which the Mjolnir kit
/// (<c>unity/Mjolnir.ModKit</c>, menu Mjolnir → Import Rigs) has for the player and the villagers. Added in
/// <see cref="Mod.OnLoad"/>; the clip is loaded at its first play.
/// </summary>
/// <example><code>
/// var bow = Animations.Add("bow", "bundles/anims", "Bow");
/// LocalPlayer.Animator.Play(bow);
/// var arms = AnimationMask.Add("arms", "bundles/anims", "Arms");
/// </code></example>
public static class AnimationsExtensions
{
    extension(Animations)
    {
        /// <summary>
        /// Adds an animation of the calling mod: the clip <paramref name="asset"/> (its name) in the AssetBundle
        /// <paramref name="bundle"/>, a file relative to the mod's folder, built for the game's Unity (6000.3.12f1, Windows) by
        /// the Mjolnir kit. <paramref name="name"/> is letters, digits and underscores, unique among the mod's animations; its
        /// full name is <c>&lt;mod id&gt;.&lt;name&gt;</c> (<see cref="Animations.Find"/>, the console's <c>anim</c>). Adding the
        /// same name again gives the animation already added.
        /// </summary>
        /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or it is taken by another clip.</exception>
        /// <exception cref="InvalidOperationException">The caller is not a mod.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ModAnimation Add(string name, string bundle, string asset) =>
            ModAnimations.Shared.AddAnimation(CallingMod.Of(Assembly.GetCallingAssembly()), name, bundle, asset);
    }
}

/// <summary>Masks of a mod's own (<c>AnimationMask.Add</c>), from its AssetBundle: which bones its animations move.</summary>
public static class AnimationMaskExtensions
{
    extension(AnimationMask)
    {
        /// <summary>
        /// Adds a mask of the calling mod: the avatar mask <paramref name="asset"/> in the AssetBundle <paramref name="bundle"/>
        /// (relative to the mod's folder), whose paths are the game's skeleton's from its root (<c>master/Bip001/…</c>; the
        /// Mjolnir kit makes them). Adding the same name again gives the mask already added.
        /// </summary>
        /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or it is taken by another mask.</exception>
        /// <exception cref="InvalidOperationException">The caller is not a mod.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static AnimationMask Add(string name, string bundle, string asset) =>
            ModAnimations.Shared.AddMask(CallingMod.Of(Assembly.GetCallingAssembly()), name, bundle, asset);
    }
}

/// <summary>An animation of a mod, added with <c>Animations.Add</c>: play it as any animation of the game.</summary>
public sealed class ModAnimation : AnimationInfo
{
    internal ModAnimation(string modId, string name, string bundlePath, string asset) : base(name, modId, 0)
    {
        BundlePath = bundlePath;
        Asset = asset;
    }

    /// <summary>The AssetBundle it is in (full path).</summary>
    public string BundlePath { get; }

    /// <summary>The clip's asset name in the bundle.</summary>
    public string Asset { get; }
}

/// <summary>What mods added (no Unity: unit-tested), found by the calling assembly as a mod's content is.</summary>
internal sealed class ModAnimations(Func<Assembly, (string Id, string? Folder)?> owner)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ModAnimation> _animations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AnimationMask> _masks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registry the public API fills.</summary>
    public static ModAnimations Shared { get; } = new(OwnerOf);

    public ModAnimation AddAnimation(Assembly caller, string name, string bundle, string asset)
    {
        var (modId, path) = Check(caller, name, bundle, asset);
        lock (_lock)
        {
            string key = $"{modId}.{name}";
            if (_animations.TryGetValue(key, out var known))
            {
                if (string.Equals(known.BundlePath, path, StringComparison.OrdinalIgnoreCase) && known.Asset == asset) return known;
                throw new ArgumentException($"{key} is the clip '{known.Asset}' of {known.BundlePath} already", nameof(name));
            }
            var animation = new ModAnimation(modId, name, path, asset);
            _animations[key] = animation;
            Animations.Register(animation);
            return animation;
        }
    }

    public AnimationMask AddMask(Assembly caller, string name, string bundle, string asset)
    {
        var (modId, path) = Check(caller, name, bundle, asset);
        lock (_lock)
        {
            string key = $"{modId}.{name}";
            if (_masks.TryGetValue(key, out var known))
            {
                if (string.Equals(known.BundlePath, path, StringComparison.OrdinalIgnoreCase) && known.Asset == asset) return known;
                throw new ArgumentException($"{key} is the mask '{known.Asset}' of {known.BundlePath} already", nameof(name));
            }
            var mask = new AnimationMask(name, modId, path, asset);
            _masks[key] = mask;
            return mask;
        }
    }

    /// <summary>A mask by its full name (<c>&lt;mod id&gt;.&lt;name&gt;</c>, or Mjolnir's own name), null for none.</summary>
    public AnimationMask? Mask(string fullName)
    {
        if (string.Equals(fullName, AnimationMask.UpperBody.Name, StringComparison.OrdinalIgnoreCase)) return AnimationMask.UpperBody;
        lock (_lock) return _masks.GetValueOrDefault(fullName);
    }

    public IReadOnlyList<ModAnimation> All
    {
        get
        {
            lock (_lock) return [.. _animations.Values];
        }
    }

    private (string ModId, string Path) Check(Assembly caller, string name, string bundle, string asset)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException($"'{name}' is not a name of letters, digits and underscores", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        var (modId, folder) = owner(caller) ?? throw new InvalidOperationException($"{caller.GetName().Name} is not a mod: only a mod adds animations");
        return (modId, folder == null ? bundle : Path.GetFullPath(Path.Combine(folder, bundle)));
    }

    private static (string Id, string? Folder)? OwnerOf(Assembly assembly)
    {
        var strings = ModStringsRegistry.ForAssembly(assembly);
        if (strings == null) return null;
        string location = assembly.Location;
        return (strings.ModId, location.Length > 0 ? Path.GetDirectoryName(location) : null);
    }
}
