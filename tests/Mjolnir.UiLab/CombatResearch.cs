using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using SSSGame;
using SSSGame.Combat;
using SSSGame.Network;
using HitInfo = SSSGame.Network.NetworkHittable.HitInfo;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "combat research" scenario (autopilot.once containing "combat research"), what led to the SDK's combat module: in the hosted world, every
/// creature loaded (its sheet, faction, team, health, authority) next to the player and the villagers; probes on the game's
/// damage, health and death (<see cref="CombatProbes"/>: Character and Creature TakeDamage before and after, their
/// _OnTakeDamage, IsDead, OnDeath, the network's NetworkHittable.Hit); then damage dealt by hand through the game's own
/// TakeDamage(DamageData): to a villager (from the player, from nobody, as healing), to the player, to the nearest creature
/// (and a kill), and to a villager spawned for it (a kill). Into shots/combat.txt and the log. It changes the user's save:
/// back it up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> CombatResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (player == null)
        {
            Log("Combat: no player");
            yield break;
        }
        Log($"Combat: probes {UiLab.Instance.Harmony.CreateClassProcessor(typeof(CombatProbes)).Patch()?.Count ?? 0} method(s) patched");
        var text = new StringBuilder();
        text.AppendLine($"player {player.name}: health {player.CurrentHealth}/{player.MaxHealth} dead {player.IsDead} team {Team(player)} authority {player.HasStateAuthority}");
        var villagers = Object.FindObjectsOfType<Villager>().ToList();
        text.AppendLine($"villagers {villagers.Count}: teams {string.Join(", ", villagers.GroupBy(Team).Select(g => $"{g.Key} x{g.Count()}"))}");
        var creatures = Object.FindObjectsByType<Creature>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
        text.AppendLine($"creatures {creatures.Count}:");
        foreach (var group in creatures.GroupBy(c => c.dataSheet != null ? c.dataSheet.name : c.name).OrderBy(g => g.Key))
        {
            var c = group.First();
            float nearest = group.Min(x => Vector3.Distance(x.transform.position, player.transform.position));
            text.AppendLine($"  {group.Key} x{group.Count()}: '{c.name}' type {c.GetIl2CppType().Name}, name key {c.dataSheet?.localizedName}, faction {c.Faction}, team {c.TeamId}, " +
                            $"health {c.CurrentHealth}/{c.MaxHealth}, dead {c.IsDead}, invulnerable {c.IsInvulnerable}, component {c.isComponent}, authority {c.HasAuthority}, nearest {nearest:0} m");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "combat.txt"), text.ToString());
        Log("Combat: " + text.ToString().Replace("\n", " | "));

        // A villager near the player: from the player, from nobody, as healing.
        var villager = villagers.Where(v => !v.IsDead).OrderBy(v => Vector3.Distance(v.transform.position, player.transform.position)).FirstOrDefault();
        if (villager != null)
        {
            Log($"Combat: the villager {villager.name} at {Vector3.Distance(villager.transform.position, player.transform.position):0} m, health {villager.CurrentHealth}/{villager.MaxHealth}");
            Log($"Combat: melee managers: the player {(Melee(player) is { } pm ? pm.gameObject.name : "none")}, the villager {(Melee(villager) is { } vm ? vm.gameObject.name : "none")}; hittables: the villager {villager.GetComponentInChildren<NetworkHittable>(true) != null}, the player {player.GetComponentInChildren<NetworkHittable>(true) != null}");
            Hit(villager, villager.transform, Melee(player), 10f, 0, "a villager, from the player");
            yield return 1f;
            Log($"Combat: … a second later {villager.CurrentHealth}");
            Hit(villager, villager.transform, Melee(villager), 10f, 0, "a villager, by its own melee");
            yield return 1f;
            Hit(villager, villager.transform, Melee(player), 15f, DamageFlags.Healing, "a villager, healing");
            yield return 1f;
            Hit(villager, villager.transform, Melee(player), 10f, DamageFlags.Unblockable | DamageFlags.Undodgeable, "a villager, unblockable");
            yield return 1f;
            Log($"Combat: … a second later {villager.CurrentHealth}");
        }
        // The player.
        Hit(player, player.transform, Melee(villager != null ? villager : player), 10f, 0, "the player, from the villager");
        yield return 1.5f;
        // Who may hurt whom: the teams and the receivers' rule on players' blows.
        Log($"Combat: friendly damage from a player: the player {player.Cast<IDamageReceiver>().FriendlyDamageFromPlayer}, the villager {villager?.Cast<IDamageReceiver>().FriendlyDamageFromPlayer}; " +
            $"dealers' teams: the player's melee {Melee(player)?.Cast<IDamageDealer>().TeamId}, the villager's {(villager != null ? Melee(villager)?.Cast<IDamageDealer>().TeamId : null)}");
        Hit(player, player.transform, Melee(player), 10f, 0, "the player, by its own melee");
        yield return 1f;
        foreach (var dealer in Object.FindObjectsByType<StructureDamageDealer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Take(1))
        {
            Log($"Combat: a structure's dealer {dealer.name}, team {dealer.Cast<IDamageDealer>().TeamId}");
            Hit(player, player.transform, dealer, 10f, 0, "the player, by a structure's dealer");
            yield return 1f;
        }
        // Health written straight: the player's 10 down (and back), a villager's to 0.
        float was = player.CurrentHealth;
        player.CurrentHealth = was - 10f;
        yield return 1f;
        Log($"Combat: the player's health written {was} -> {was - 10f}: reads {player.CurrentHealth}, dead {player.IsDead}");
        player.CurrentHealth = was;
        Shot("combat-player-hit");
        Log($"Combat: … the player a moment later {player.CurrentHealth}/{player.MaxHealth}");

        // The nearest creature: a hit, then a kill.
        var creature = creatures.Where(c => !c.IsDead && !c.isComponent && c.GetIl2CppType().Name == "Pet").OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position)).FirstOrDefault();
        if (creature != null)
        {
            Log($"Combat: the creature {creature.name} ({creature.dataSheet?.name}) at {Vector3.Distance(creature.transform.position, player.transform.position):0} m");
            Log($"Combat: the creature's hittable {creature.GetComponentInChildren<NetworkHittable>(true) != null}, its melee {(Melee(creature) is { } cm ? cm.gameObject.name : "none")}");
            Hit(creature, creature.transform, Melee(player), 10f, 0, "a creature, from the player");
            yield return 1f;
            Log($"Combat: … a second later {creature.CurrentHealth}/{creature.MaxHealth}");
            Hit(creature, creature.transform, Melee(player), 100000f, 0, "a creature, a kill");
            yield return 3f;
            Log($"Combat: … dead {creature.IsDead}, health {creature.CurrentHealth}, still there {creature != null}");
        }

        // A villager spawned for it, killed.
        int count = Residents.All.Count;
        var front = player.transform.position + player.transform.forward * 3f;
        Log($"Combat: spawn asked {Residents.Spawn(front)}");
        foreach (float wait in WaitFor("a villager to kill", () => Residents.All.Count > count, 15)) yield return wait;
        yield return 2f;
        var fresh = Object.FindObjectsOfType<Villager>().Where(v => !v.IsDead).OrderBy(v => Vector3.Distance(v.transform.position, front)).FirstOrDefault();
        if (fresh != null)
        {
            CombatProbes.Watching = true;
            fresh.CurrentHealth = 0f;
            CombatProbes.Watching = false;
            yield return 2f;
            Log($"Combat: a spawned villager's health written to 0: reads {fresh.CurrentHealth}, dead {fresh.IsDead}");
            if (!fresh.IsDead) Hit(fresh, fresh.transform, Melee(player), 100000f, 0, "a spawned villager, a kill");
            yield return 2f;
            Shot("combat-villager-killed");
            Log($"Combat: … dead {fresh.IsDead}, health {fresh.CurrentHealth}; residents {Residents.All.Count} (were {count + 1})");
            yield return 5f;
            Log($"Combat: … 5 s later still there {fresh != null}, residents {Residents.All.Count}");
        }
        Log($"Combat: probes saw {CombatProbes.Seen} call(s) in all");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static float Health(Il2CppSystem.Object o) =>
        o.TryCast<Character>() is { } c ? c.CurrentHealth : o.TryCast<Creature>() is { } k ? k.CurrentHealth : float.NaN;

    // A character's or creature's own fighting component, which the game takes as the dealer of its blows.
    private static Component? Melee(Component c) => c.GetComponentInChildren<MeleeManager>(true) ?? c.GetComponentInParent<MeleeManager>();

    private static int Team(Character c)
    {
        try { return c.Cast<IDamageReceiver>().TeamId; }
        catch { return int.MinValue; }
    }

    // A hit through the game's own TakeDamage, as a weapon's would: the receiver, who deals it, its base and flags.
    private static void Hit(Il2CppSystem.Object receiver, Transform at, Component? dealer, float amount, DamageFlags flags, string what)
    {
        CombatProbes.Watching = true;
        try
        {
            var target = receiver.Cast<IDamageReceiver>();
            var data = new DamageData
            {
                damageReceiver = target,
                damageDealer = dealer?.TryCast<IDamageDealer>(),
                baseDamage = amount,
                damageMultiplier = 1f,
                damageFlags = flags,
                hitPosition = at.position + Vector3.up,
                hitDirection = at.forward,
            };
            float before = Health(receiver);
            Log($"Combat: hit {what}: dealer {(dealer == null ? "none" : data.damageDealer == null ? "not a dealer" : dealer.gameObject.name + "'s " + dealer.GetIl2CppType().Name)}, {amount} {flags}, health {before}");
            target.TakeDamage(data);
            Log($"Combat: … result {data.result}, flags after {data.damageFlags}, health {before} -> {Health(receiver)}");
        }
        catch (Exception e)
        {
            Log($"Combat: hit {what} failed: {e.GetType().Name} {e.Message}");
        }
        finally
        {
            CombatProbes.Watching = false;
        }
    }
}

/// <summary>What the game does when damage is taken, health changes and characters die (logged, the first 80 calls).</summary>
[HarmonyPatch]
internal static class CombatProbes
{
    public static int Seen;

    private static void Note(string line)
    {
        if (++Seen <= 200) UiLab.Instance.Log.Info("Combat probe: " + line);
    }

    private static string Of(DamageData d) =>
        $"base {d.baseDamage} x{d.damageMultiplier} +{d.attributeBonus} flags {d.damageFlags} result {d.result} dealer {(d.damageDealer?.gameObject != null ? d.damageDealer.gameObject.name : "none")} weapon {(d.weapon != null ? d.weapon.Pointer.ToString("x") : "none")}";

    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.TakeDamage))]
    private static void CharacterBefore(Character __instance, DamageData __0, out float __state)
    {
        __state = __instance.CurrentHealth;
        Note($"Character.TakeDamage {__instance.name} ({__instance.GetIl2CppType().Name}) health {__state}: {Of(__0)}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Character), nameof(Character.TakeDamage))]
    private static void CharacterAfter(Character __instance, DamageData __0, float __state) =>
        Note($"  after: health {__state} -> {__instance.CurrentHealth}, dead {__instance.IsDead}, {Of(__0)}");

    [HarmonyPrefix, HarmonyPatch(typeof(Creature), nameof(Creature.TakeDamage))]
    private static void CreatureBefore(Creature __instance, DamageData __0, out float __state)
    {
        __state = __instance.CurrentHealth;
        Note($"Creature.TakeDamage {__instance.name} health {__state} authority {__instance.HasAuthority}: {Of(__0)}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Creature), nameof(Creature.TakeDamage))]
    private static void CreatureAfter(Creature __instance, DamageData __0, float __state) =>
        Note($"  after: health {__state} -> {__instance.CurrentHealth}, dead {__instance.IsDead}, {Of(__0)}");

    [HarmonyPostfix, HarmonyPatch(typeof(Character), "_OnTakeDamage")]
    private static void CharacterOnTake(Character __instance, DamageData __0) => Note($"Character._OnTakeDamage {__instance.name}: {Of(__0)}");

    [HarmonyPostfix, HarmonyPatch(typeof(Creature), "_OnTakeDamage")]
    private static void CreatureOnTake(Creature __instance, DamageData __0) => Note($"Creature._OnTakeDamage {__instance.name}: {Of(__0)}");

    [HarmonyPostfix, HarmonyPatch(typeof(Character), nameof(Character.IsDead), MethodType.Setter)]
    private static void CharacterDead(Character __instance, bool __0) => Note($"Character.IsDead = {__0} on {__instance.name}");

    [HarmonyPostfix, HarmonyPatch(typeof(Creature), nameof(Creature.IsDead), MethodType.Setter)]
    private static void CreatureDead(Creature __instance, bool __0) => Note($"Creature.IsDead = {__0} on {__instance.name}");

    [HarmonyPostfix, HarmonyPatch(typeof(Character), nameof(Character.SpawnDeathLoot))]
    private static void Loot(Character __instance) => Note($"Character.SpawnDeathLoot {__instance.name}");

    [HarmonyPrefix, HarmonyPatch(typeof(NetworkHittable), nameof(NetworkHittable.Hit))]
    private static void NetworkHit(NetworkHittable __instance, HitInfo __0)
    {
        if (__instance.name != "StructureHittable") Note($"NetworkHittable.Hit on {__instance.name}: damage {__0.damage} flags {__0.flags} weapon {__0.weaponID}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(NetworkHittable), nameof(NetworkHittable.HitStateAuthority))]
    private static void NetworkHitAuthority(NetworkHittable __instance)
    {
        if (__instance.name != "StructureHittable") Note($"NetworkHittable.HitStateAuthority on {__instance.name}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Character), nameof(Character.CurrentHealth), MethodType.Setter)]
    private static void CharacterHealth(Character __instance, float __0)
    {
        if (Watching) Note($"Character.CurrentHealth = {__0} on {__instance.name}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Creature), nameof(Creature.CurrentHealth), MethodType.Setter)]
    private static void CreatureHealth(Creature __instance, float __0)
    {
        if (Watching) Note($"Creature.CurrentHealth = {__0} on {__instance.name}");
    }

    /// <summary>While the scenario hits: health is set every frame by regeneration otherwise.</summary>
    public static bool Watching;
}
