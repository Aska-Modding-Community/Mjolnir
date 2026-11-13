using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SandSailorStudio.Attributes;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Stats;

/// <summary>
/// The game's side of <see cref="CharacterSheet"/>: the attributes by asset name, the effects of the
/// effect database, the movesets that award experience. Nothing here throws into the game or into a mod: faults are logged as
/// [SDK.Stats].
/// </summary>
internal sealed class GameStats : IStatsSource
{
    public const string Source = "SDK.Stats";
    private static Dictionary<string, AttributeConfig>? s_attributes;
    private static Dictionary<int, AttributeConfig>? s_attributesById;
    private const long MovesetsLookedForAgain = 10_000; // ms
    private static Dictionary<SkillKind, InteractionMoveset>? s_movesets;
    private static long s_movesetsFound;
    // Made anew on the main thread when the game's list changes, never changed after: other threads read them as they are.
    private static List<EffectInfo> s_effects = [];
    private static Dictionary<int, EffectInfo> s_effectsById = [];
    private static int s_listCount = -1;
    private static bool s_patched;

    /// <summary>Whether experience awarded is reported.</summary>
    public static bool Watching { get; private set; }

    /// <summary>Connects <see cref="CharacterSheet"/> to the game (the module's start, phase A: no Unity objects yet).</summary>
    public static void Connect()
    {
        Dispatcher.Hook(); // records the main thread at GameReady
        CharacterSheet.Game = new GameStats();
    }

    public ICharacterSource? Character(nint key) => key == 0 ? null : new GameCharacterStats(new Character(key));

    public IReadOnlyList<EffectInfo> Effects
    {
        get
        {
            ReadEffects();
            return s_effects;
        }
    }

    public string? AttributeName(int id)
    {
        string? name = null;
        Guard($"finding the attribute {id:X}", () => name = Attribute(id)?.name);
        return name;
    }

    public EffectInfo? Effect(int id) =>
        s_effectsById.GetValueOrDefault(id) ?? (ReadEffects() ? s_effectsById.GetValueOrDefault(id) : null);

    public void WatchExperience(bool watch)
    {
        Watching = watch;
        if (watch && !s_patched) Guard("patching the award of experience", Patch);
    }

    /// <summary>The effect database of the game, null before it has one.</summary>
    public static StatusEffectDatabase? Database() => GameManagers.Get<StatusEffectDatabase>();

    /// <summary>The attribute of that asset name, null for one the game does not describe.</summary>
    public static AttributeConfig? Attribute(string assetName)
    {
        Load();
        return s_attributes!.GetValueOrDefault(assetName);
    }

    /// <summary>The attribute of that id, null for one the game does not describe.</summary>
    public static AttributeConfig? Attribute(int id)
    {
        Load();
        return s_attributesById!.GetValueOrDefault(id);
    }

    /// <summary>
    /// What awards experience for a skill: the game has several actions per skill, and any of them will do. They load with what
    /// uses them (a structure, a tool), so a skill without one is looked for again, at most every 10 s.
    /// </summary>
    public static InteractionMoveset? Moveset(SkillKind kind)
    {
        if (s_movesets != null && s_movesets.TryGetValue(kind, out var known) && known != null) return known;
        long now = Environment.TickCount64;
        if (s_movesets != null && now - s_movesetsFound < MovesetsLookedForAgain) return null;
        s_movesetsFound = now;
        s_movesets = [];
        foreach (var moveset in Resources.FindObjectsOfTypeAll<InteractionMoveset>().OrderBy(m => m.name))
        {
            if (moveset.expertise == null || moveset.expertiseGainMultiplier <= 0) continue;
            if (SkillOf(moveset) is { } skill) s_movesets.TryAdd(skill, moveset);
        }
        return s_movesets.GetValueOrDefault(kind);
    }

    /// <summary>The skill a moveset trains, null for one that trains none.</summary>
    public static SkillKind? SkillOf(InteractionMoveset moveset) =>
        moveset.levelingPath?.reward is { } reward ? SkillNames.ByLevel(reward.name) : null;

    /// <summary>The player with no name, else the villager of that name; empty stats when there is none.</summary>
    public static CharacterSheet FindCharacter(string name)
    {
        if (name.Length == 0)
        {
            var player = GameManagers.Get<PlayerManager>() is { } manager ? Local(manager) : null;
            return CharacterSheet.Of(player?.Pointer ?? 0);
        }
        var villager = Object.FindObjectsOfType<Villager>().FirstOrDefault(v => string.Equals(v.GetName(), name, StringComparison.OrdinalIgnoreCase));
        return CharacterSheet.Of(villager?.Pointer ?? 0);
    }

    public static void Guard(string what, Action action) => GameGuard.Run(Source, what, action);

    /// <summary>Asks the game; a fault is logged and answers false.</summary>
    public static bool Try(string what, Func<bool> ask) => GameGuard.Try(Source, what, ask, false);

    /// <summary>Asks the game; a fault is logged and answers <paramref name="fallback"/>.</summary>
    public static T Try<T>(string what, Func<T> ask, T fallback) => GameGuard.Try(Source, what, ask, fallback);

    // LocalPlayer throws without a session.
    private static PlayerCharacter? Local(PlayerManager manager)
    {
        try
        {
            return manager.LocalPlayer?.playerCharacter;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // One object per effect: a mod's own is its ModEffect, a game effect keeps the object it had, so mods compare them with ==.
    private static bool ReadEffects()
    {
        if (!GameLoop.IsGameReady || !Dispatcher.IsMainThread) return false;
        var list = Database()?.CompleteStatusEffectList?.StatusEffectList;
        if (list == null || list.Count == s_listCount) return false;
        var effects = new List<EffectInfo>();
        var byId = new Dictionary<int, EffectInfo>();
        foreach (var table in list.ToArray())
        {
            if (table == null || byId.ContainsKey(table.id)) continue;
            var effect = SDK.Effects.Registered(table.id) ?? s_effectsById.GetValueOrDefault(table.id) ?? Describe(table);
            byId[table.id] = effect;
            effects.Add(effect);
        }
        s_effects = effects;
        s_effectsById = byId;
        s_listCount = list.Count;
        return true;
    }

    private static EffectInfo Describe(StatusEffectModifierTable table) =>
        new(table.id, table.name, table.effectType?.name ?? "", () => table.Name, () => table.Description);

    // Built whole before it is kept: a database not there yet, or a fault half way, leaves nothing half filled behind.
    private static void Load()
    {
        if (s_attributes != null) return;
        if (GameManagers.Get<AttributesDatabase>() is not { } database) throw new InvalidOperationException("The game has no attribute database yet");
        var byName = new Dictionary<string, AttributeConfig>();
        var byId = new Dictionary<int, AttributeConfig>();
        foreach (var config in database.allAttributes)
        {
            byName.TryAdd(config.name, config);
            byId.TryAdd(config.attributeId, config);
        }
        s_attributesById = byId;
        s_attributes = byName;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Patch()
    {
        int count = new Harmony("mjolnir.sdk.stats").CreateClassProcessor(typeof(ExperiencePatches)).Patch()?.Count ?? 0;
        s_patched = true;
        SdkLog.Info(Source, $"Stats: experience followed, {count} game method(s) patched");
    }
}

/// <summary>The game's award of experience, reported while a mod listens.</summary>
[HarmonyPatch]
internal static class ExperiencePatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InteractionMoveset), nameof(InteractionMoveset.AwardProfficiencyPoints))]
    // A fault here is logged and the game awards as it would: nothing is thrown into its work.
    private static bool Awarding(InteractionMoveset __instance, IInteractionAgent __0, ref float __1)
    {
        if (!GameStats.Watching || __1 <= 0) return true;
        try
        {
            if (__instance == null || __0 == null || GameStats.SkillOf(__instance) is not { } skill) return true;
            float gain = __instance.expertiseGainMultiplier;
            nint character = __0.TryCast<Villager>()?.Pointer ?? __0.TryCast<PlayerInteractionAgent>()?.GetCharacter()?.Pointer ?? 0;
            if (character == 0 || gain <= 0) return true;
            if (CharacterSheet.Gaining(character, skill, __1 * gain) is not { } granted) return false;
            __1 = granted / gain;
            return granted > 0;
        }
        catch (Exception e)
        {
            SdkLog.Error(GameStats.Source, $"reporting experience: {e}");
            return true;
        }
    }
}
