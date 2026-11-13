using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.Content;

/// <summary>An item of a mod, added with <see cref="Items.Add(string, string, string?, int?)"/>.</summary>
public sealed class ModItem
{
    private readonly object _lock = new();
    private readonly string? _folder;
    private readonly Action _changed;
    private string? _modelPath;
    private string? _modelAsset;
    private int _version, _modelVersion;
    private float? _minimumDamage, _maximumDamage;
    private readonly List<KeyValuePair<string, float>> _attributes = [];
    private readonly List<ItemEffect> _hitEffects = [], _equippedEffects = [], _eatenEffects = [];

    internal ModItem(string modId, string name, string basedOn, string? iconPath, int? stackSize, string? folder, Action changed, ItemKind? kind = null)
    {
        _folder = folder;
        _changed = changed;
        Kind = kind;
        ModId = modId;
        Name = name;
        BasedOn = basedOn;
        IconPath = iconPath;
        StackSize = stackSize;
        Id = ContentIds.ItemId(modId, name);
        AssetName = ContentIds.ItemAssetName(modId, name);
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>Its name in the mod (its texts are <c>[item.&lt;name&gt;]</c> in the mod's language files).</summary>
    public string Name { get; }

    /// <summary>The game item it is made from, by asset name.</summary>
    public string BasedOn { get; }

    /// <summary>Its kind when it was added by one (<see cref="Items.Add(string, ItemKind, string?, int?)"/>); null when made from a named game item.</summary>
    public ItemKind? Kind { get; }

    /// <summary>The least damage a blow of it deals; null for its base's.</summary>
    public float? MinimumDamage
    {
        get
        {
            lock (_lock) return _minimumDamage;
        }
    }

    /// <summary>The most damage a blow of it deals; null for its base's.</summary>
    public float? MaximumDamage
    {
        get
        {
            lock (_lock) return _maximumDamage;
        }
    }

    /// <summary>The attributes it was given (<see cref="WithAttribute"/>), by asset name, in the order given; its base's others stay.</summary>
    public IReadOnlyList<KeyValuePair<string, float>> Attributes
    {
        get
        {
            lock (_lock) return [.. _attributes];
        }
    }

    /// <summary>The effects its blows may put on what they hit (<see cref="AppliesOnHit(string, TimeSpan, float)"/>).</summary>
    public IReadOnlyList<ItemEffect> HitEffects
    {
        get
        {
            lock (_lock) return [.. _hitEffects];
        }
    }

    /// <summary>The effects on whoever holds or wears it (<see cref="AppliesWhileEquipped(string)"/>).</summary>
    public IReadOnlyList<ItemEffect> EquippedEffects
    {
        get
        {
            lock (_lock) return [.. _equippedEffects];
        }
    }

    /// <summary>The effects on whoever eats it (<see cref="AppliesWhenEaten(string, TimeSpan)"/>).</summary>
    public IReadOnlyList<ItemEffect> EatenEffects
    {
        get
        {
            lock (_lock) return [.. _eatenEffects];
        }
    }

    /// <summary>Its picture's file (full path); null for the base item's picture.</summary>
    public string? IconPath { get; }

    /// <summary>How many fit in one slot; null for the base item's.</summary>
    public int? StackSize { get; }

    /// <summary>Its id in the game: negative, from the mod id and the name, the same for every player.</summary>
    public int Id { get; }

    /// <summary>Its asset name in the game: <c>Item_&lt;mod id&gt;_&lt;name&gt;</c>.</summary>
    public string AssetName { get; }

    /// <summary>The AssetBundle its model is in (full path); null while it has the base item's model.</summary>
    public string? ModelPath
    {
        get
        {
            lock (_lock) return _modelPath;
        }
    }

    /// <summary>The name of its model, a prefab in <see cref="ModelPath"/>; null while it has the base item's model.</summary>
    public string? ModelAsset
    {
        get
        {
            lock (_lock) return _modelAsset;
        }
    }

    /// <summary>Grows with each change, so the game side gives an item made already what changed.</summary>
    internal int Version
    {
        get
        {
            lock (_lock) return _version;
        }
    }

    /// <summary>Grows with each change of its model only: the game side makes its world object again only then.</summary>
    internal int ModelVersion
    {
        get
        {
            lock (_lock) return _modelVersion;
        }
    }

    /// <summary>
    /// Gives the item a model of the mod's own: the prefab <paramref name="asset"/> (its name) in the AssetBundle
    /// <paramref name="bundle"/>, a file relative to the mod's folder, built for the game's Unity (6000.3.12f1, Windows, HDRP):
    /// the Mjolnir kit's project <c>unity/Mjolnir.ModKit</c> builds them. The item stays what its base item is in the world,
    /// picked up, carried, thrown and sent over the network as the base is; only its look is the model, whose pivot is the
    /// item's origin, and the base's box colliders are fitted round it with the room they left round the base's look, so it
    /// lies on the ground, a path or a shelf as the base does (the model's own colliders are left out). Its
    /// materials use the game's shaders, such as <c>HDRP/Lit</c>. Returns the item.
    /// </summary>
    public ModItem WithModel(string bundle, string asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        string path = ContentRegistry.Resolve(_folder, bundle);
        lock (_lock)
        {
            if (_modelPath == path && _modelAsset == asset) return this;
            _modelPath = path;
            _modelAsset = asset;
            _version++;
            _modelVersion++;
        }
        _changed(); // the game side gives an item made already its model
        return this;
    }

    /// <summary>
    /// The damage a blow of the weapon deals, between <paramref name="minimum"/> and <paramref name="maximum"/> before the
    /// game's own sums (the attack's multiplier, the wielder's strength and skill, the target's armour and weaknesses), as
    /// the game's weapons have (an iron sword 9 to 11, a two-handed one more, a bow 3 to 6). Only weapons deal damage
    /// (<see cref="ItemKind.OneHandedWeapon"/>, <see cref="ItemKind.TwoHandedWeapon"/>, <see cref="ItemKind.Shield"/>,
    /// <see cref="ItemKind.Bow"/>, <see cref="ItemKind.Arrow"/>, or a weapon of the game as <c>basedOn</c>; on any other a
    /// warning in the log). Returns the item.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative damage, or a maximum below the minimum.</exception>
    /// <exception cref="InvalidOperationException">The item's kind is no weapon.</exception>
    public ModItem WithDamage(float minimum, float maximum)
    {
        if (!(minimum >= 0) || float.IsInfinity(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "a damage of 0 or more");
        if (!(maximum >= minimum) || float.IsInfinity(maximum)) throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "a damage of the minimum or more");
        if (Kind is { } kind && !ItemKinds.IsWeapon(kind)) throw new InvalidOperationException($"{this} is a {kind}, which deals no damage");
        lock (_lock)
        {
            if (_minimumDamage == minimum && _maximumDamage == maximum) return this;
            _minimumDamage = minimum;
            _maximumDamage = maximum;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// Gives the item an attribute of the game by asset name, or another value for one its base has. A worn or held item's
    /// attributes count for whoever wears or holds it, as the game's armour does (<c>Attribute_Armor</c>,
    /// <c>Attribute_ColdResistance</c>, <c>Attribute_RainProtection</c>, <c>Attribute_WindProtection</c>); the item's own are
    /// <c>Property_Durability</c> (how long it lasts) and <c>Property_DecayRate</c>. The base's other attributes stay: give
    /// one 0 to take it away. An attribute the game lacks is left out, with a warning in the log. Returns the item.
    /// </summary>
    public ModItem WithAttribute(string attribute, float value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "a finite value");
        lock (_lock)
        {
            int at = _attributes.FindIndex(a => a.Key == attribute);
            if (at >= 0 && _attributes[at].Value == value) return this;
            if (at >= 0) _attributes[at] = new(attribute, value);
            else _attributes.Add(new(attribute, value));
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// The armour the item gives whoever wears or holds it (<c>Attribute_Armor</c>, <see cref="WithAttribute"/>): the game's
    /// cloth gives none, a chain shirt 400, a plate one 600, a plate helmet 160. Returns the item.
    /// </summary>
    public ModItem WithArmor(float armor) => WithAttribute("Attribute_Armor", armor);

    /// <summary>
    /// How long the item lasts (<c>Property_Durability</c>, <see cref="WithAttribute"/>): an iron sword 550, a chain shirt
    /// 2800. Returns the item.
    /// </summary>
    public ModItem WithDurability(float durability)
    {
        if (!(durability > 0) || float.IsInfinity(durability)) throw new ArgumentOutOfRangeException(nameof(durability), durability, "a durability above 0");
        return WithAttribute("Property_Durability", durability);
    }

    /// <summary>
    /// Adds an effect a blow of the weapon may put on what it hits, for <paramref name="duration"/>, with a
    /// <paramref name="chance"/> from 0 to 1 a blow, as the game's torch sets on fire (<c>Effect_Burning</c>, 5 s, every
    /// blow): a game effect by asset name, which the game knows only once it runs (so an item added in <c>OnLoad</c> names it),
    /// or the mod's own (<see cref="AppliesOnHit(EffectInfo, TimeSpan, float)"/>). Only weapons that strike with the item
    /// itself (one- and two-handed weapons, shields). An effect the game lacks is left out, with a warning in the log. Returns
    /// the item.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A duration not above zero, or a chance outside 0 to 1.</exception>
    /// <exception cref="InvalidOperationException">The item's kind does not strike.</exception>
    public ModItem AppliesOnHit(string effect, TimeSpan duration, float chance = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effect);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "a duration above zero");
        if (!(chance > 0 && chance <= 1)) throw new ArgumentOutOfRangeException(nameof(chance), chance, "a chance above 0, at most 1");
        if (Kind is { } kind && !ItemKinds.Strikes(kind)) throw new InvalidOperationException($"{this} is a {kind}, which does not strike");
        return AddEffect(_hitEffects, new ItemEffect(effect, duration, chance));
    }

    /// <summary>Adds an effect, a mod's own or one found in the game, a blow may put on what it hits; as <see cref="AppliesOnHit(string, TimeSpan, float)"/>.</summary>
    public ModItem AppliesOnHit(EffectInfo effect, TimeSpan duration, float chance = 1f)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return AppliesOnHit(effect.AssetName, duration, chance);
    }

    /// <summary>
    /// Adds an effect on whoever holds or wears the item, for as long as they do: a weapon in hand (not on the back), a piece
    /// of armour worn; the player and villagers alike. It comes when the item is equipped and goes when it is taken off,
    /// with every stack of it (two items giving one effect: taking one off takes it). A game effect by asset name, or the
    /// mod's own (<see cref="AppliesWhileEquipped(EffectInfo)"/>). Returns the item.
    /// </summary>
    /// <exception cref="InvalidOperationException">The item's kind is neither held nor worn.</exception>
    public ModItem AppliesWhileEquipped(string effect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effect);
        if (Kind is { } kind && !ItemKinds.IsEquipment(kind)) throw new InvalidOperationException($"{this} is a {kind}, which is neither held nor worn");
        return AddEffect(_equippedEffects, new ItemEffect(effect, null, 1f));
    }

    /// <summary>Adds an effect, a mod's own or one found in the game, on whoever holds or wears the item; as <see cref="AppliesWhileEquipped(string)"/>.</summary>
    public ModItem AppliesWhileEquipped(EffectInfo effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return AppliesWhileEquipped(effect.AssetName);
    }

    /// <summary>
    /// Adds an effect on whoever eats the item, for <paramref name="duration"/>, beside its base's own (a meal's hunger and
    /// health): a game effect by asset name, or the mod's own (<see cref="AppliesWhenEaten(EffectInfo, TimeSpan)"/>). Only food
    /// (<see cref="ItemKind.Food"/>, or a food of the game as <c>basedOn</c>). Returns the item.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A duration not above zero.</exception>
    /// <exception cref="InvalidOperationException">The item's kind is not food.</exception>
    public ModItem AppliesWhenEaten(string effect, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effect);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "a duration above zero");
        if (Kind is { } kind && kind != ItemKind.Food) throw new InvalidOperationException($"{this} is a {kind}, which is not eaten");
        return AddEffect(_eatenEffects, new ItemEffect(effect, duration, 1f));
    }

    /// <summary>Adds an effect, a mod's own or one found in the game, on whoever eats the item; as <see cref="AppliesWhenEaten(string, TimeSpan)"/>.</summary>
    public ModItem AppliesWhenEaten(EffectInfo effect, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return AppliesWhenEaten(effect.AssetName, duration);
    }

    public override string ToString() => $"{ModId}.{Name}";

    private ModItem AddEffect(List<ItemEffect> effects, ItemEffect effect)
    {
        lock (_lock)
        {
            if (effects.Contains(effect)) return this;
            effects.Add(effect);
            _version++;
        }
        _changed();
        return this;
    }
}

/// <summary>
/// An effect an item gives (<see cref="ModItem.AppliesOnHit(string, TimeSpan, float)"/>, <see cref="ModItem.AppliesWhileEquipped(string)"/>,
/// <see cref="ModItem.AppliesWhenEaten(string, TimeSpan)"/>): the effect by asset name (a game effect's, or a mod effect's
/// <see cref="EffectInfo.AssetName"/>), for how long (null: as long as the item is held or worn), and its chance from 0 to 1.
/// </summary>
public sealed record ItemEffect(string Effect, TimeSpan? Duration, float Chance);
