using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandSailorStudio;
using SandSailorStudio.Attributes;
using SandSailorStudio.Inventory;
using SSSGame;
using SSSGame.Combat;
using UnityEngine;
using GameStatusEffect = SSSGame.StatusEffect;

namespace Mjolnir.SDK.Content;

/// <summary>
/// What a mod's item is beyond its look (<see cref="ModItem.WithDamage"/>, <see cref="ModItem.WithAttribute"/>, the
/// <c>Applies…</c> effects). The game keeps it all in data:
/// <list type="bullet">
/// <item>a weapon's damage is its description's <c>WeaponizedItemInfo.damageValues</c>;</item>
/// <item>an item's attributes are <c>ItemInfo.attributes</c>, which count for whoever wears or holds it (the player's six
/// pieces of armour make exactly the armour of the player's sheet);</item>
/// <item>food's effects are <c>ConsumableInfo.consumeEffects</c>;</item>
/// <item>a blow's effects are the world object's <c>MeleeObject.effects</c> (the torch's burning), a list of a struct holding a
/// reference, which written from here held a broken reference (the game died reading it, 2026-11-12): the SDK puts its items'
/// effects on what they hit itself, in a postfix on <c>MeleeObject.OnDamageDealt</c>, as the game does for its own.</item>
/// </list>
/// Each is set on the item's copy from its base's own (so setting it again makes no second copy), each time the game builds its
/// item database. Effects need the game's effect database, which the main menu does not have when it builds its items: they
/// are set when the game first looks an effect up (<see cref="EffectsKnown"/>), and with each item database after that. Effects
/// while equipped are the SDK's: a hidden postfix on the equipment's world object being equipped, stowed and taken off puts them
/// on the character holding it and takes them away.
/// </summary>
internal static class GameEquipment
{
    private const string Source = GameLocalization.Source;

    // The items whose effects are on a character now, by the equipment item's pointer, with the objects themselves: held,
    // so the pointer is no other item's while it is here, and swept once the character is gone (Unity's null check; the
    // item is a plain object of the game's, never destroyed).
    private static readonly Dictionary<nint, (EquipmentItem Equipment, Character Character)> s_wearing = [];
    private static StatusEffectDatabase? s_effects;

    /// <summary>The item's own data on its copy, from its base's.</summary>
    public static void Set(ItemInfo info, ModItem item, ItemInfo? template, Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        if (template == null) return;
        if (item.MinimumDamage is { } minimum && item.MaximumDamage is { } maximum)
        {
            if (info.TryCast<WeaponizedItemInfo>() is { } weapon) weapon.damageValues = new Vector2(minimum, maximum);
            else WarnOnce($"Items: {item} is made from {item.BasedOn}, which is no weapon: its damage is left out");
        }
        if (item.Attributes.Count > 0) SetAttributes(info, item, template, list);
        if (item.EquippedEffects.Count > 0 && info.TryCast<EquipmentItemInfo>() == null)
            WarnOnce($"Items: {item} is made from {item.BasedOn}, which is neither held nor worn: its effects while equipped are left out");
        if (Effects() != null) SetEatenEffects(info, item, template);
    }

    /// <summary>
    /// The game looked an effect up: its effect database is there. Once per database, the made items take the effects they
    /// could not take without it (when eaten, on hit).
    /// </summary>
    public static void EffectsKnown(StatusEffectDatabase database)
    {
        if (s_effects != null && s_effects.Pointer == database.Pointer) return;
        s_effects = database;
        foreach (var (item, info, template) in GameItems.MadeItems())
            if (item.EatenEffects.Count > 0 && template != null) SetEatenEffects(info, item, template);
    }

    /// <summary>
    /// A weapon's blow dealt damage: a mod item's effects on hit go on what it hit, each by its chance, as the game puts its
    /// weapons' own (<c>MeleeObject.OnDamageDealt</c>: on the receiver's status effects, for their time, not kept by a save).
    /// </summary>
    public static void Hit(MeleeObject melee, DamageData? damage)
    {
        var weaponInfo = melee.WeaponObject?.WeaponizedItemInfo;
        if (weaponInfo == null || weaponInfo.id >= 0 || damage == null) return; // the game's own weapons
        var item = ContentRegistry.Shared.ItemById(weaponInfo.id);
        if (item == null || item.HitEffects.Count == 0) return;
        var receiver = damage.damageReceiver?.TryCast<Component>();
        var manager = receiver != null ? receiver.GetComponentInParent<StatusEffectManager>() : null;
        if (manager == null && damage.hitCollider != null) manager = damage.hitCollider.GetComponentInParent<StatusEffectManager>();
        if (manager == null) return; // a tree, a stone: nothing to have an effect
        foreach (var effect in item.HitEffects)
        {
            if (effect.Chance < 1f && UnityEngine.Random.value > effect.Chance) continue;
            if (Status(item, effect, persistent: false) is { } status) manager.Apply(status);
        }
    }

    private static void SetEatenEffects(ItemInfo info, ModItem item, ItemInfo template)
    {
        if (item.EatenEffects.Count == 0) return;
        if (info.TryCast<ConsumableInfo>() is not { } food || template.TryCast<ConsumableInfo>() is not { } baseFood)
        {
            WarnOnce($"Items: {item} is made from {item.BasedOn}, which is not food: its effects when eaten are left out");
            return;
        }
        var effects = new Il2CppSystem.Collections.Generic.List<GameStatusEffect>();
        for (int i = 0; baseFood.consumeEffects != null && i < baseFood.consumeEffects.Count; i++) effects.Add(baseFood.consumeEffects[i]);
        foreach (var effect in item.EatenEffects)
            if (Status(item, effect) is { } status) effects.Add(status);
        food.consumeEffects = effects;
    }

    // The attributes of the copy: the base's, with the item's own values in place of theirs or after them.
    private static void SetAttributes(ItemInfo info, ModItem item, ItemInfo template, Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        var data = new List<AttributeData>();
        for (int i = 0; template.attributes != null && i < template.attributes.Length; i++)
        {
            var own = template.attributes[i];
            if (own?.attribute == null) continue;
            data.Add(new AttributeData { attribute = own.attribute, multiplier = own.multiplier, _value = own._value });
        }
        foreach (var (name, value) in item.Attributes)
        {
            var known = data.FirstOrDefault(d => d.attribute.name == name);
            if (known != null)
            {
                known._value = value;
                continue;
            }
            if (Config(name, list) is not { } config)
            {
                WarnOnce($"Items: {item} has the attribute {name}, and the game has no such attribute");
                continue;
            }
            data.Add(new AttributeData { attribute = config, multiplier = 1f, _value = value });
        }
        info.attributes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AttributeData>(data.ToArray());
        info._cachedAttributes?.Clear();
    }

    // An attribute of the game by asset name: the attributes database's (characters'), else one an item of the game carries
    // (the armour of worn items is not in the database's list).
    private static AttributeConfig? Config(string name, Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        var configs = GameManagers.Get<AttributesDatabase>()?.allAttributes;
        for (int i = 0; configs != null && i < configs.Count; i++)
            if (configs[i] != null && configs[i].name == name) return configs[i];
        for (int i = 0; i < list.Count; i++)
        {
            var attributes = list[i]?.attributes;
            for (int k = 0; attributes != null && k < attributes.Length; k++)
                if (attributes[k]?.attribute != null && attributes[k].attribute.name == name) return attributes[k].attribute;
        }
        return null;
    }

    private static StatusEffectDatabase? Effects() => s_effects != null ? s_effects : null; // Unity's null: gone with its scene

    // A game status effect of an item's effect: its table by asset name from the game's effect list (the mods' put there first).
    private static GameStatusEffect? Status(ModItem item, ItemEffect effect, bool persistent = true)
    {
        var list = (Effects() ?? GameManagers.Get<StatusEffectDatabase>())?.CompleteStatusEffectList?.StatusEffectList;
        StatusEffectModifierTable? table = null;
        for (int i = 0; list != null && i < list.Count && table == null; i++)
            if (list[i] != null && list[i].name == effect.Effect) table = list[i];
        if (table == null)
        {
            WarnOnce($"Items: {item}'s effect {effect.Effect} is not among the game's effects: it is left out");
            return null;
        }
        return new GameStatusEffect { table = table, duration = effect.Duration is { } time ? (float)time.TotalSeconds : -1f, persistent = persistent };
    }

    /// <summary>
    /// An equipment item's world object was equipped (in hand, worn) or stowed or taken off: the item's effects go to the
    /// character holding it, or leave them. The object's own override and the base's both report it: once is enough.
    /// </summary>
    public static void Equipped(EquipmentItem? equipment, bool on)
    {
        if (equipment?.info == null || equipment.info.id >= 0) return; // the game's own: a mod's ids are negative
        var item = ContentRegistry.Shared.ItemById(equipment.info.id);
        if (item == null || item.EquippedEffects.Count == 0) return;
        Sweep();
        nint key = equipment.Pointer;
        if (on)
        {
            if (s_wearing.ContainsKey(key)) return;
            var character = equipment.GetEquipmentManager()?.GetComponentInParent<Character>();
            if (character == null) return;
            s_wearing[key] = (equipment, character);
            var effects = CharacterSheet.Of(character.Pointer).Effects;
            foreach (var effect in item.EquippedEffects)
            {
                if (SDK.Effects.Find(effect.Effect) is not { } info)
                {
                    WarnOnce($"Items: {item}'s effect {effect.Effect} is not among the game's effects: it is left out");
                    continue;
                }
                // A save keeps the effect (it is persistent): a character loaded wearing the item has it already.
                if (!effects.Has(info)) effects.Apply(info);
            }
            SdkLog.Info(Source, $"Items: {item} equipped by {character.name}: {string.Join(", ", item.EquippedEffects.Select(e => e.Effect))} on");
        }
        else
        {
            if (!s_wearing.Remove(key, out var wearing) || wearing.Character == null) return; // a character gone took its effects
            var effects = CharacterSheet.Of(wearing.Character.Pointer).Effects;
            foreach (var effect in item.EquippedEffects)
                if (SDK.Effects.Find(effect.Effect) is { } info) effects.Remove(info);
            SdkLog.Info(Source, $"Items: {item} taken off: {string.Join(", ", item.EquippedEffects.Select(e => e.Effect))} off");
        }
    }

    /// <summary>Forgets the wearers the game destroyed (Unity's null check): characters gone with their world.</summary>
    public static void Sweep()
    {
        if (s_wearing.Count == 0) return;
        foreach (var (key, wearing) in s_wearing.ToList())
        {
            if (wearing.Character == null) s_wearing.Remove(key);
        }
    }

    private static void WarnOnce(string text) => ContentLog.WarnOnce(text);
}

/// <summary>
/// The hidden patches behind <see cref="ModItem.AppliesWhileEquipped(string)"/> (the hybrid Harmony rule). The world objects'
/// overrides may share their code with the base's (the game folds identical bodies into one): a second hook on the same code
/// fails, and the first one reports both, so each is patched on its own and a shared one left as it is. The base's own
/// <c>EquipmentItemObject.OnStow</c> is empty and its code shared with 2720 methods of the game: a hook there ran for all of
/// them and the game died (2026-11-12), so stowing is heard from the overrides, which have bodies of their own.
/// </summary>
internal static class EquipmentPatches
{
    public static int Install(Harmony harmony)
    {
        var on = new HarmonyMethod(typeof(EquipmentPatches), nameof(OnEquip));
        var off = new HarmonyMethod(typeof(EquipmentPatches), nameof(OnTakeOff));
        (Type Type, string Method, HarmonyMethod Postfix)[] targets =
        [
            (typeof(EquipmentItemObject), nameof(EquipmentItemObject.OnEquip), on),
            (typeof(WeaponizedItemObject), nameof(WeaponizedItemObject.OnEquip), on),
            (typeof(WearableItemObject), nameof(WearableItemObject.OnEquip), on),
            (typeof(EquipmentItemObject), nameof(EquipmentItemObject.OnUnequip), off),
            (typeof(WeaponizedItemObject), nameof(WeaponizedItemObject.OnUnequip), off),
            (typeof(WeaponizedItemObject), nameof(WeaponizedItemObject.OnStow), off),
            (typeof(WearableItemObject), nameof(WearableItemObject.OnStow), off),
        ];
        int count = 0;
        var shared = new List<string>();
        foreach (var (type, method, postfix) in targets)
        {
            try
            {
                harmony.Patch(AccessTools.DeclaredMethod(type, method), postfix: postfix);
                count++;
            }
            catch (Exception e) when (e.ToString().Contains("ALREADY_CREATED"))
            {
                shared.Add($"{type.Name}.{method}");
            }
            catch (Exception e)
            {
                SdkLog.Warn(GameLocalization.Source, $"Items: {type.Name}.{method} not patched, effects while equipped may be missed: {e.Message}");
            }
        }
        if (shared.Count > 0) SdkLog.Info(GameLocalization.Source, $"Items: {string.Join(", ", shared)} share their code with a method patched already");
        // A blow's effects (MeleeObject.OnDamageDealt has a body of its own: no other method shares it).
        try
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(MeleeObject), nameof(MeleeObject.OnDamageDealt)),
                          postfix: new HarmonyMethod(typeof(EquipmentPatches), nameof(AfterDamageDealt)));
            count++;
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameLocalization.Source, $"Items: MeleeObject.OnDamageDealt not patched, effects on hit are missed: {e.Message}");
        }
        return count;
    }

    private static void AfterDamageDealt(MeleeObject __instance, DamageData damageData)
    {
        try
        {
            GameEquipment.Hit(__instance, damageData);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Items: an item's effects on hit failed: {e}");
        }
    }

    private static void OnEquip(EquipmentItem eqItem) => Report(eqItem, true);

    private static void OnTakeOff(EquipmentItem eqItem) => Report(eqItem, false);

    private static void Report(EquipmentItem eqItem, bool on)
    {
        try
        {
            GameEquipment.Equipped(eqItem, on);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Items: an item's effects while equipped failed: {e}");
        }
    }
}
