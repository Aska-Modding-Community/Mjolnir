using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SSSGame;
using SSSGame.Combat;

namespace Mjolnir.SDK.Combat;

/// <summary>
/// The game's damage and deaths, reported to <see cref="Combatants"/>: every blow passes
/// <c>Character.TakeDamage</c> (the player's and a villager's overrides call it) or <c>Creature.TakeDamage</c>, reached through
/// <c>IDamageReceiver</c>. A character's death, whatever killed it (a blow, health written to 0, hunger, the network's state
/// on another machine), runs <c>Character._OnDeadChanged</c> (its <c>IsDead</c> setter is not on the way of a blow); a
/// creature's sets <c>Creature.IsDead</c>. Each death is reported once, until it lives again. Hidden: mods see C# events.
/// </summary>
[HarmonyPatch]
internal static class DamagePatches
{
    /// <summary>What a blow was before it landed, for its report after.</summary>
    internal struct Before
    {
        public bool Skipped;
        public float Health;
        public bool Dead;
        public nint Attacker;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage))]
    private static bool CharacterTaking(Character __instance, DamageData __0, out Before __state) => Taking(__instance.Pointer, __instance.CurrentHealth, __instance.IsDead, __0, out __state);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage))]
    private static void CharacterTaken(Character __instance, DamageData __0, Before __state) => Taken(__instance.Pointer, __instance.CurrentHealth, __instance.IsDead, __0, __state);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.TakeDamage))]
    private static bool CreatureTaking(Creature __instance, DamageData __0, out Before __state) => Taking(__instance.Pointer, __instance.CurrentHealth, __instance.IsDead, __0, out __state);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.TakeDamage))]
    private static void CreatureTaken(Creature __instance, DamageData __0, Before __state) => Taken(__instance.Pointer, __instance.CurrentHealth, __instance.IsDead, __0, __state);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Character), "_OnDeadChanged")]
    private static void CharacterDeadChanged(Character __instance) => DeadChanged(__instance, __instance.IsDead, body: Loading());

    // A creature's state comes over the network well after the world loaded, so a body of the save gets IsDead then too (a
    // deer of the save 16 s after the load, its health not restored yet; the time since its spawn told nothing): a creature's
    // death is reported when a blow or a mod's kill came at it just before, which is how creatures die.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.IsDead), MethodType.Setter)]
    private static void CreatureDeadChanged(Creature __instance, bool __0)
    {
        if (__instance.isComponent) return;
        DeadChanged(__instance, __0, body: !Combatants.IsDeathExpected(__instance.Pointer));
    }

    // The dead reported, so a death is reported once (the game sets it again as its state comes back over the network). Each
    // is kept with its object, so its pointer is not another object's while it is kept, and one the game destroyed (a body
    // gone, a world left) is dropped at the next death.
    private static readonly Dictionary<nint, UnityEngine.Object> s_dead = [];

    // A death while the world loads is a body of the save, not a death: kept as dead, not reported.
    private static void DeadChanged(UnityEngine.Object target, bool dead, bool body)
    {
        nint key = target.Pointer;
        if (!dead)
        {
            s_dead.Remove(key); // revived, respawned
            return;
        }
        if (s_dead.ContainsKey(key)) return;
        DropDestroyed();
        s_dead[key] = target;
        if (!body) Died(key);
    }

    private static void DropDestroyed()
    {
        try
        {
            foreach (nint gone in s_dead.Where(d => d.Value == null).Select(d => d.Key).ToList()) s_dead.Remove(gone);
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameCombat.Source, $"Combat: forgetting the gone dead failed: {e.GetType().Name} {e.Message}");
        }
    }

    private static bool Loading()
    {
        try
        {
            return SandSailorStudio.GameManagers.Get<PopulationManager>() is { IsLoading: true };
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Before a blow: the handlers may change its damage or cancel it (the game's method is then skipped).
    private static bool Taking(nint target, float health, bool dead, DamageData data, out Before state)
    {
        state = new Before { Health = health, Dead = dead };
        if (dead || data == null) return true;
        try
        {
            nint attacker = GameCombat.OwnerOf(data.damageDealer?.gameObject);
            state.Attacker = attacker;
            float amount = data.baseDamage * data.damageMultiplier;
            var result = Combatants.RaiseDamaging(SDK.Combatant.Of(target), attacker != 0 ? SDK.Combatant.Of(attacker) : null, amount,
                GameCombat.KindsOf(data.damageFlags), data.hitPosition, data.hitDirection);
            if (result is not { } changed)
            {
                state.Skipped = true;
                return false;
            }
            if (Math.Abs(changed - amount) > 0.0001f)
            {
                data.baseDamage = changed;
                data.damageMultiplier = 1f;
            }
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameCombat.Source, $"Combat: reporting a blow failed: {e.GetType().Name} {e.Message}");
        }
        return true;
    }

    // After a blow: what it took (what is gone from the health: armour, blocks and teams counted).
    private static void Taken(nint target, float health, bool dead, DamageData data, Before state)
    {
        if (state.Skipped || state.Dead || data == null) return;
        try
        {
            Combatants.RaiseDamaged(SDK.Combatant.Of(target), state.Attacker != 0 ? SDK.Combatant.Of(state.Attacker) : null,
                Math.Max(0, state.Health - Math.Max(0, health)), Math.Max(0, health), dead, GameCombat.KindsOf(data.damageFlags));
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameCombat.Source, $"Combat: reporting damage failed: {e.GetType().Name} {e.Message}");
        }
    }

    private static void Died(nint target)
    {
        try
        {
            Combatants.RaiseDied(target);
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameCombat.Source, $"Combat: reporting a death failed: {e.GetType().Name} {e.Message}");
        }
    }
}
