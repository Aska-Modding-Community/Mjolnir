using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Status effects of a mod's own. An effect is made from one of the game's (<c>basedOn</c>, the asset name of an effect such
/// as <c>Effect_Leisure</c>): its kind, stacking rules and visual effects are the game's; the mod gives it a name, an id, texts
/// and a picture, and if it likes its own changes to attributes (<see cref="ModEffect.Modifies"/>, <see cref="ModEffect.PerSecond"/>),
/// which replace the base effect's. Its texts come from the mod's language files, <c>[effect.&lt;name&gt;] name</c>,
/// <c>description</c>, <c>added</c> and <c>removed</c> (the messages when it comes and goes). Put it on a character with
/// <c>Sheet.Effects.Apply</c>, like any effect of the game; the game shows it, and a save keeps it with the time it has left by
/// its id, the same for every player and start. Effects are added in <see cref="Mod.OnLoad"/>.
/// </summary>
/// <remarks>
/// Ids are short, as the network carries them in 16 bits. When an effect's id falls on an effect added before it (of this mod
/// or another), it is left out of the game: an error in the log names both, and <c>Apply</c> answers false for it, while the
/// mod goes on. Another name gives another id.
/// </remarks>
/// <example><code>
/// var blessing = Effects.Add("blessing", icon: "items/blessing.png")
///                       .Modifies("Attribute_Strength", 3)
///                       .PerSecond("VariableAttribute_Health", 1);
/// LocalPlayer.Sheet.Effects.Apply(blessing, TimeSpan.FromMinutes(1));
/// </code></example>
public static class EffectsExtensions
{
    extension(Effects)
    {
        /// <summary>
        /// Adds an effect of the calling mod. <paramref name="name"/> is letters, digits and underscores, unique among the mod's
        /// effects; <paramref name="icon"/> a PNG or JPG file relative to the mod's folder (the base effect's picture without one).
        /// Adding the same name again gives the effect already added.
        /// </summary>
        /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or it is taken by an effect made from
        /// another game effect.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ModEffect Add(string name, string basedOn = "Effect_Leisure", string? icon = null) =>
            ContentRegistry.Shared.AddEffect(CallingMod.Of(Assembly.GetCallingAssembly()), name, basedOn, icon);
    }
}

/// <summary>A status effect of a mod, added with <c>Effects.Add</c>. It is an effect like the game's: give it to <c>Sheet.Effects</c>.</summary>
public sealed class ModEffect : EffectInfo
{
    private readonly object _lock = new();
    private readonly List<(string Stat, float Amount, ModifierKind Kind)> _modifiers = [];
    private readonly List<(string Stat, float PerSecond)> _rates = [];
    private readonly Action _changed;
    private int _version;

    internal ModEffect(string modId, string key, string basedOn, string? iconPath, Action changed)
        : base(ContentIds.EffectId(modId, key), ContentIds.EffectAssetName(modId, key), "",
               () => Text(modId, key, "name", key), () => Text(modId, key, "description", ""))
    {
        ModId = modId;
        Name = key;
        BasedOn = basedOn;
        IconPath = iconPath;
        _changed = changed;
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>Its name in the mod (its texts are <c>[effect.&lt;name&gt;]</c> in the mod's language files).</summary>
    public string Name { get; }

    /// <summary>The game effect it is made from, by asset name.</summary>
    public string BasedOn { get; }

    /// <summary>Its picture's file (full path); null for the base effect's picture.</summary>
    public string? IconPath { get; }

    internal IReadOnlyList<(string Stat, float Amount, ModifierKind Kind)> Modifiers
    {
        get
        {
            lock (_lock) return [.. _modifiers];
        }
    }

    internal IReadOnlyList<(string Stat, float PerSecond)> Rates
    {
        get
        {
            lock (_lock) return [.. _rates];
        }
    }

    /// <summary>Grows with each change, so the game side gives an effect it made already its new changes.</summary>
    internal int Version
    {
        get
        {
            lock (_lock) return _version;
        }
    }

    /// <summary>
    /// While the effect is on, changes an attribute that is a sum (<see cref="CharacterStat.IsPool"/> false), by its asset name
    /// (<c>Attribute_Strength</c>). Given once the effect is in the game, it counts for characters as they take the effect.
    /// </summary>
    public ModEffect Modifies(string stat, float amount, ModifierKind kind = ModifierKind.Add)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stat);
        lock (_lock)
        {
            _modifiers.Add((stat, amount, kind));
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// While the effect is on, adds to a pool every second, by its asset name (<c>VariableAttribute_Health</c>); a negative
    /// amount takes away. Given once the effect is in the game, it counts for characters as they take the effect.
    /// </summary>
    public ModEffect PerSecond(string stat, float amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stat);
        lock (_lock)
        {
            _rates.Add((stat, amount));
            _version++;
        }
        _changed();
        return this;
    }

    private static string Text(string modId, string key, string text, string fallback)
    {
        var strings = ModStringsRegistry.ForModId(modId);
        return strings != null && strings.TryGet($"effect.{key}.{text}", Localization.Language, out string found) ? found : fallback;
    }
}
