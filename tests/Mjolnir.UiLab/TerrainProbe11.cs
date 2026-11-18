using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe11" scenario: felling a tree with its loot. Next to a tree it reads the tree's harvest pieces, what
/// each piece's moveset wants, its loot spawners and what they drop, and what the player holds; then it strikes the tree
/// as the villagers' harvest does (a blow carrying the player's own tool), reading after each blow the flags the game set,
/// the piece and its hit points, and the items lying round the tree. It changes the save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe11()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe11: no world");
            yield break;
        }
        var home = character.transform.position;
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(i => i.Descriptor?.itemInfo?.name ?? "?").GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));
        string Kind(BiomeItemInstance b) => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name ?? "?";

        // What the player holds.
        var equipment = character.GetComponentInChildren<EquipmentManager>(true);
        WeaponizedItem? weapon = null;
        var held = new List<string>();
        if (equipment != null)
        {
            foreach (var point in equipment.equipPoints)
            {
                var item = Try2(() => point.GetCurrentItem());
                if (item == null) continue;
                var weaponized = item.TryCast<WeaponizedItem>();
                held.Add($"{point.name}: {item.info?.name ?? "?"} (category {item.info?.category?.name ?? "none"}, tier {item.info?.tier}, weapon {weaponized != null})");
                if (weaponized != null && (weapon == null || (item.info?.name ?? "").Contains("Axe", StringComparison.OrdinalIgnoreCase))) weapon = weaponized;
            }
        }
        // An axe from anyone in the world who holds one (a woodcutter), the best tier first.
        string where = "the player";
        WeaponizedItem? axe = null;
        foreach (var manager in UnityEngine.Object.FindObjectsOfType<EquipmentManager>())
            foreach (var point in manager.equipPoints)
            {
                var item = Try2(() => point.GetCurrentItem())?.TryCast<WeaponizedItem>();
                if (item?.info?.category?.name != "Categ_Tools_Axes") continue;
                if (axe == null || item.info.tier > axe.info.tier)
                {
                    axe = item;
                    where = manager.gameObject.name;
                }
            }
        if (axe != null) weapon = axe;
        Log($"Probe11: the player holds [{string.Join("; ", held)}]; the weapon taken {weapon?.info?.name ?? "none"} (tier {weapon?.info?.tier}) from {where}");

        // A tree with a harvest interaction near the settlement.
        NaturalObject? standing = null;
        foreach (float distance in new[] { 20f, 40f, 60f, 80f })
        {
            standing = NaturalObjects.Around(home, Math.Min(distance, 64f)).FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree && (o.Position - home).magnitude > 6f);
            if (standing != null) break;
        }
        if (standing == null)
        {
            Log("Probe11: no tree near the player");
            yield break;
        }
        var to = standing.Position - (standing.Position - home).normalized * 3f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(to.x, to.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 4f;
        var tree = Biome(standing.Position, 1.5f).FirstOrDefault(b => b.gameObject != null && b.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
        if (tree == null)
        {
            Log($"Probe11: the tree {standing} has no harvest interaction near the player");
            yield break;
        }
        var go = tree.gameObject;
        var harvest = go.GetComponentInChildren<HarvestInteraction>(true);
        var at = tree.GetPosition();
        var camera = Overhead(at);
        camera.Position = at + new Vector3(0, 7f, -10f);
        camera.Rotation = Quaternion.LookRotation(at + Vector3.up * 2 - camera.Position, Vector3.up);

        // A. The tree.
        Log($"Probe11: tree '{Kind(tree)}' ({go.name}) at {P(at)}, health {tree.GetHealth()}, quantity {tree.GetQuantity()}; parts [{string.Join(", ", go.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(24))}]");
        int index = 0;
        foreach (var piece in harvest.harvestPieces)
        {
            var moveset = piece.attackMoveset;
            Log($"Probe11:   piece {index++}: {piece.pieceId}, hit points {piece.pieceHitPoints}, bits {piece.harvestableBitsCount}, elevate {piece.elevateIgnoreToolToForceDamage}, moveset {(moveset != null ? moveset.name : "none")} (needs {moveset?.requiredEqippmentCategory?.name ?? "nothing"}, unarmed {moveset?.baseUnarmedDamage}, × {moveset?.damageMultiplier}), area info {piece.areaInfoOverride?.name ?? "none"}");
        }
        Log($"Probe11:   now piece {Try(() => harvest.GetCurrentPieceIndex())}, hit points {Try(() => harvest.GetHarvestHitPoints())} of {Try(() => harvest.GetMaxHarvestHitPoints())}, total left {Try(() => harvest.GetTotalRemainingHitPoints())}, moveset {Try(() => harvest.GetAttackMoveset()?.name)}");
        foreach (var loot in go.GetComponentsInChildren<LootSpawner>(true))
            Log($"Probe11:   loot spawner on '{loot.gameObject.name}': on harvest [{string.Join("; ", (loot.lootOnHarvest ?? new LootSpawner.LootData[0]).Select(l => $"{l.itemInfo?.name} stacks [{string.Join(",", l.stacks ?? new int[0])}] chance {l.spawnChance} mask {(l.useHarvestMask ? l.harvestMask.ToString() : "any")}"))}], on bit {loot.lootOnBit?.Length ?? 0}");
        foreach (var spawner in go.GetComponentsInChildren<HarvestSpawner>(true))
            Log($"Probe11:   harvest spawner on '{spawner.gameObject.name}': piece loot {spawner.pieceLoot?.Count ?? 0}, bit loot {spawner.bitLoot?.Count ?? 0}");
        Shot("probe11-before");
        yield return AfterShot;

        // B. Blows carrying the player's tool, as a villager's harvest strikes.
        var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
        string itemsBefore = ItemNames(at, 10f);
        string last = "";
        int blows = 0;
        for (; blows < 60 && !tree.Destroyed; blows++)
        {
            var data = new DamageData
            {
                weapon = weapon,
                damageDealer = dealer,
                damageReceiver = harvest.TryCast<IDamageReceiver>(),
                baseDamage = 50,
                damageMultiplier = 1,
                hitPosition = harvest.transform.position + Vector3.up,
                hitDirection = (harvest.transform.position - character.transform.position).normalized,
            };
            string error = Try(() =>
            {
                harvest.TakeDamage(data);
                return "";
            });
            if (error.Length > 0)
            {
                Log($"Probe11: blow {blows + 1} failed: {error}");
                break;
            }
            string now = $"flags {data.damageFlags}, result {data.result:0.#}, piece {Try(() => harvest.GetCurrentPieceIndex())}, hit points {Try(() => harvest.GetHarvestHitPoints())}, health {Try(() => tree.GetHealth())}, items [{ItemNames(at, 10f)}]";
            if (now != last) Log($"Probe11: blow {blows + 1}: {now}");
            last = now;
            yield return 0.4f;
        }
        yield return 3f;
        Log($"Probe11: {blows} blows; destroyed {tree.Destroyed}, exhausted {Try(() => tree.IsExhausted())}; items near before [{itemsBefore}], after [{ItemNames(at, 12f)}]; through the SDK {NaturalObjects.Nearest(at, 0.6f)?.ToString() ?? "none"}");
        Shot("probe11-felled");
        yield return AfterShot;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
