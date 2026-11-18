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
/// The "terrain probe12" scenario: what felling with loot needs. A tree felled with a woodcutter's axe leaves its trunk
/// lying as an item; the probe chops that trunk too and reads what it drops. Then, on other trees, it strikes without a
/// tool but with the flag that lets a blow through without one, and with an axe item made in code that no one carries,
/// reading the flags, the pieces, and the items dropped. It changes the save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe12()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe12: no world");
            yield break;
        }
        var home = character.transform.position;
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(i => i.Descriptor?.itemInfo?.name ?? "?").GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));
        string Kind(BiomeItemInstance b) => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name ?? "?";

        // A woodcutter's axe.
        string where = "nobody";
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
        Log($"Probe12: the axe {axe?.info?.name ?? "none"} (tier {axe?.info?.tier}) from {where}");
        var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();

        // Trees near the settlement, a few metres apart.
        // The test save's wooded spot (six standing trees, found by the "terrain clearing" scenario), then anywhere.
        var picked = NaturalObjects.Around(new Vector3(-16.1f, 41.5f, 249.5f), 10).Where(o => o.Kind == NaturalObjectKind.Tree).ToList();
        Log($"Probe12: picked [{string.Join("; ", picked)}]");
        if (picked.Count == 0) yield break;

        var variants = new (string Name, Func<WeaponizedItem?> Weapon, DamageFlags Flags, bool Dealer)[]
        {
            ("no tool, IgnoreHarvestTool", () => null, DamageFlags.IgnoreHarvestTool, true),
            ("an axe made in code", () => axe == null ? null : new WeaponizedItem { info = axe.info }, 0, true),
        };
        int vi = 0;
        for (int v = 0; vi < variants.Length && v < picked.Count; v++)
        {
            var (name, makeWeapon, flags, withDealer) = variants[vi];
            var standing = picked[v];
            var to = standing.Position - (standing.Position - home).normalized * 3f;
            var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(to.x, to.z);
            foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
            yield return 4f;
            var tree = Biome(standing.Position, 1.5f).FirstOrDefault(b => b.gameObject != null && b.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
            if (tree == null)
            {
                Log($"Probe12: [{name}] the tree {standing} has no harvest interaction");
                continue;
            }
            var harvest = tree.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            if (harvest.GetCurrentPieceIndex() != 0)
            {
                Log($"Probe12: [{name}] '{Kind(tree)}' at {P(tree.GetPosition())} is a stump (piece {harvest.GetCurrentPieceIndex()}, health {tree.GetHealth()})");
                continue;
            }
            vi++;
            var at = tree.GetPosition();
            WeaponizedItem? weapon = null;
            string made = Try(() =>
            {
                weapon = makeWeapon();
                return weapon == null ? "none" : $"{weapon.info?.name}";
            });
            string itemsBefore = ItemNames(at, 20f);
            Log($"Probe12: [{name}] tree '{Kind(tree)}' at {P(at)}, health {tree.GetHealth()}, weapon {made}");
            Log($"Probe12: [{name}]   now piece {Try(() => harvest.GetCurrentPieceIndex())}, hit points {Try(() => harvest.GetHarvestHitPoints())}, total left {Try(() => harvest.GetTotalRemainingHitPoints())}");
            foreach (float wait in Strike(harvest, weapon, withDealer ? dealer : null, flags, character, $"[{name}] tree", 30, () => ItemNames(at, 20f))) yield return wait;
            yield return 3f;
            Log($"Probe12: [{name}] felled: items before [{itemsBefore}], after [{ItemNames(at, 20f)}]");

            // The trunk lying there.
            string resource = standing.ResourceName ?? "";
            var trunk = Items(at, 20f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name == resource && i.gameObject != null && i.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null);
            if (trunk == null)
            {
                Log($"Probe12: [{name}] no trunk '{resource}' with a harvest interaction within 20 m");
                continue;
            }
            var chop = trunk.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var trunkAt = trunk.GetPosition();
            int index = 0;
            foreach (var piece in chop.harvestPieces)
                Log($"Probe12: [{name}]   trunk piece {index++}: {piece.pieceId}, hit points {piece.pieceHitPoints}, bits {piece.harvestableBitsCount}, moveset {(piece.attackMoveset != null ? piece.attackMoveset.name : "none")} (needs {piece.attackMoveset?.requiredEqippmentCategory?.name ?? "nothing"})");
            Log($"Probe12: [{name}] trunk '{trunk.gameObject.name}' at {P(trunkAt)}; parts [{string.Join(", ", trunk.gameObject.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(24))}]");
            foreach (var loot in trunk.gameObject.GetComponentsInChildren<LootSpawner>(true))
                Log($"Probe12: [{name}]   loot spawner: on harvest [{string.Join("; ", (loot.lootOnHarvest ?? new LootSpawner.LootData[0]).Select(l => $"{l.itemInfo?.name} stacks [{string.Join(",", l.stacks ?? new int[0])}] chance {l.spawnChance} mask {(l.useHarvestMask ? l.harvestMask.ToString() : "any")}"))}], on bit {loot.lootOnBit?.Length ?? 0} × {loot.bitCount}");
            string beforeChop = ItemNames(trunkAt, 10f);
            foreach (float wait in Strike(chop, weapon, withDealer ? dealer : null, flags, character, $"[{name}] trunk", 40, () => ItemNames(trunkAt, 10f))) yield return wait;
            yield return 3f;
            Log($"Probe12: [{name}] trunk chopped: items before [{beforeChop}], after [{ItemNames(trunkAt, 10f)}], the trunk destroyed {trunk.Destroyed}");
        }
        Shot("probe12-done");
        yield return AfterShot;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IEnumerable<float> Strike(HarvestInteraction harvest, WeaponizedItem? weapon, IDamageDealer? dealer, DamageFlags flags, Component character, string what, int most, Func<string> items)
    {
        string last = "";
        int blows = 0;
        for (; blows < most; blows++)
        {
            var data = new DamageData
            {
                weapon = weapon,
                damageDealer = dealer,
                damageReceiver = harvest.TryCast<IDamageReceiver>(),
                baseDamage = 50,
                damageMultiplier = 1,
                damageFlags = flags,
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
                Log($"Probe12: {what} blow {blows + 1} failed: {error}");
                break;
            }
            string now = $"flags {data.damageFlags}, result {data.result:0}, piece {Try(() => harvest.GetCurrentPieceIndex())}, hit points {Try(() => harvest.GetHarvestHitPoints())}, items [{items()}]";
            if (now != last) Log($"Probe12: {what} blow {blows + 1}: {now}");
            last = now;
            if ((data.damageFlags & DamageFlags.Invalid) != 0) break;
            yield return 0.4f;
        }
    }
}
