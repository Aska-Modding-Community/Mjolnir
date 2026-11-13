using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandSailorStudio;
using SandSailorStudio.Attributes;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The mods' status effects in the game: each a copy of its base effect with the mod's id, name, picture
/// and attributes, in the effect list the database reads. A table is made once per game, and goes into the list when the game
/// builds its items and whenever the database is asked for an effect, so a save's effects and the network find it.
/// </summary>
internal static class GameEffects
{
    // The game's effect tokens and the mod's keys under [effect.<name>] for them.
    private static readonly (string Token, string Key)[] Texts =
        [("name", "name"), ("desc", "description"), ("message_added", "added"), ("message_remove", "removed")];

    private static readonly Dictionary<int, (StatusEffectModifierTable Table, int Version)> s_made = [];
    // The database last given the effects, held so its address is never another's, and the registry's version then.
    private static StatusEffectDatabase? s_appliedTo;
    private static int s_appliedVersion = -1;

    /// <summary>
    /// The mods' effects go into the database: once for each database, and again after a change in the registry (an effect
    /// added, an effect's changes). Every look-up of an effect comes here first, so nothing is done while nothing changed.
    /// </summary>
    public static void Apply(StatusEffectDatabase? database)
    {
        var registry = ContentRegistry.Shared;
        int version = registry.Version;
        if (database == null || (version == s_appliedVersion && s_appliedTo?.Pointer == database.Pointer)) return;
        var list = database.CompleteStatusEffectList?.StatusEffectList;
        if (list == null || GameManagers.Get<AttributesDatabase>() is not { } attributes) return;
        var cache = database._statusEffCache;
        foreach (var effect in registry.Effects.OrderBy(e => e.Id))
        {
            int changes = effect.Version;
            if (!s_made.TryGetValue(effect.Id, out var made))
            {
                if (Make(effect, list, attributes) is not { } table) continue;
                s_made[effect.Id] = made = (table, changes);
                Effects.Register(effect); // the game's list now gives the ModEffect itself for this id
            }
            else if (made.Version != changes)
            {
                SetChanges(effect, made.Table, attributes);
                s_made[effect.Id] = made = (made.Table, changes);
            }
            if (!list.ToArray().Any(t => t.id == effect.Id)) list.Add(made.Table);
            if (cache != null) cache[effect.Id] = made.Table;
        }
        s_appliedTo = database;
        s_appliedVersion = version;
    }

    private static StatusEffectModifierTable? Make(ModEffect effect, Il2CppSystem.Collections.Generic.List<StatusEffectModifierTable> list, AttributesDatabase attributes)
    {
        var source = list.ToArray().FirstOrDefault(t => t.name == effect.BasedOn);
        if (source == null)
        {
            Warn($"Effects: {effect} needs a game effect called {effect.BasedOn}, and there is none");
            return null;
        }
        var table = Object.Instantiate(source);
        table.name = effect.AssetName;
        table.id = effect.Id;
        table.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        effect.SetKind(table.effectType?.name ?? "");
        if (effect.IconPath != null && ContentPictures.Picture(effect.IconPath) is { } picture) table.icon = picture;
        // The base effect's words must not come back through its serialized texts.
        table.localizedName = effect.Name;
        table.localizedDescription = table.messageTextAdded = table.messageTextRemoved = table.dialogueAdded = table.dialogueRemoved = "";
        if (effect.Modifiers.Count + effect.Rates.Count > 0) SetChanges(effect, table, attributes);
        ContentTexts.Add(ModStringsRegistry.ForModId(effect.ModId), "effect", effect.Name, Texts.Select(t => (t.Key, table.GetKey(t.Token))).ToArray());
        return table;
    }

    private static void SetChanges(ModEffect effect, StatusEffectModifierTable table, AttributesDatabase attributes)
    {
        var sums = new Il2CppSystem.Collections.Generic.List<StatusEffectModifierTable.AttributeElement>();
        foreach (var (stat, amount, kind) in effect.Modifiers)
        {
            if (Config(effect, stat, attributes) is not { } target) continue;
            sums.Add(new StatusEffectModifierTable.AttributeElement { targetAttribute = target, modifier = new AttributeModifier(amount, Operation(kind)) });
        }
        var pools = new Il2CppSystem.Collections.Generic.List<StatusEffectModifierTable.VariableAttributeElement>();
        foreach (var (stat, perSecond) in effect.Rates)
        {
            if (Config(effect, stat, attributes) is not { } target) continue;
            pools.Add(new StatusEffectModifierTable.VariableAttributeElement { targetAttribute = target, modifier = new VariableAttributeModifier(perSecond, VariableModifierProcessMode.TIME, null) });
        }
        table.attrElements = sums;
        table.vattrElements = pools;
    }

    private static ModifierOperation Operation(ModifierKind kind) => kind switch
    {
        ModifierKind.Multiply => ModifierOperation.MULTIPLY,
        ModifierKind.Percent => ModifierOperation.PERCENTADD,
        _ => ModifierOperation.ADD,
    };

    private static AttributeConfig? Config(ModEffect effect, string stat, AttributesDatabase attributes)
    {
        foreach (var config in attributes.allAttributes)
        {
            if (config.name == stat) return config;
        }
        Warn($"Effects: {effect} changes {stat}, and the game has no such attribute");
        return null;
    }

    private static void Warn(string text) => ContentLog.WarnOnce(text);
}

/// <summary>Every look-up of an effect by id comes through here: a save's, the network's, the SDK's.</summary>
[HarmonyPatch]
internal static class EffectPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(StatusEffectDatabase), nameof(StatusEffectDatabase.TryGetStatusEffect))]
    private static void BeforeLookUp(StatusEffectDatabase __instance)
    {
        // It runs for every look-up, so it makes no delegate.
        try
        {
            GameEffects.Apply(__instance);
            GameEquipment.EffectsKnown(__instance); // the items' effects (when eaten, on hit) take their tables now
        }
        catch (Exception e)
        {
            SdkLog.Error(ContentLog.Source, $"Items: adding the mods' effects failed: {e}");
        }
    }
}
