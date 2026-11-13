using System;
using System.Collections.Generic;
using System.Linq;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "equipment" scenario: items of a kind with what makes them the mod's (Mjolnir.SDK.Content's <c>ItemKind</c>,
/// <c>WithDamage</c>, <c>WithArmor</c>/<c>WithAttribute</c>, <c>AppliesOnHit</c>, <c>AppliesWhileEquipped</c>,
/// <c>AppliesWhenEaten</c>). UiLab adds a sword (damage 30 to 40, burning on every blow), a hood (armour 500, cold resistance
/// 7, energized while worn) and a cake (energized for a minute when eaten). In the main menu their copies in the game's
/// database are read (the sword's damage and its world object's blow effects, the hood's attributes, the cake's effects); in
/// the world the hood and the sword are given and equipped on the player through the game's own equipping, the player's
/// armour and effects read before, while and after, and what the player wore put back. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private const string LabSword = "Item_mjolnir.test.uilab_lab_sword";
    private const string LabHood = "Item_mjolnir.test.uilab_lab_hood";
    private const string LabCake = "Item_mjolnir.test.uilab_lab_cake";

    private static IEnumerator<float> EquipmentScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        LogLabItems("in the main menu");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        LogLabItems("in the world");
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var equipment = player?.GetComponentInChildren<EquipmentManager>(true);
        var inventory = player?.Inventory?.GetItemCollection();
        var sheet = Mjolnir.SDK.Player.LocalPlayer.Sheet;
        var energized = Mjolnir.SDK.Effects.Find("Effect_Energized");
        if (equipment == null || inventory == null || energized == null)
        {
            Log($"Equipment: missing: equipment {equipment != null}, inventory {inventory != null}, energized {energized != null}");
            yield break;
        }
        string State() => $"armour {sheet.Find("Attribute_Armor")?.Value}, cold resistance {sheet.Find("Attribute_ColdResistance")?.Value}, energized {sheet.Effects.Has(energized)}";
        var head = Point(equipment, "Head")?._item;
        var hand = Point(equipment, "RightHand")?._item;
        Log($"Equipment: before: head {head?.info?.name}, right hand {hand?.info?.name}; {State()}");

        Log($"Equipment: given hood {Mjolnir.SDK.Content.Items.Give(LabHood)}, sword {Mjolnir.SDK.Content.Items.Give(LabSword)}");
        yield return 1f;
        var hood = Find(inventory, LabHood);
        Log($"Equipment: hood in the inventory {hood != null}; equipping it {(hood != null && equipment.TransferItemForEquipping(hood))}");
        yield return 3f;
        Log($"Equipment: the hood worn: head {Point(equipment, "Head")?._item?.info?.name}; {State()}");
        Shot("equipment-hood");

        var sword = Find(inventory, LabSword);
        Log($"Equipment: sword in the inventory {sword != null}; equipping it {(sword != null && equipment.TransferItemForEquipping(sword))}");
        yield return 3f;
        var held = Point(equipment, "RightHand");
        var melee = held?._itemObject != null ? held._itemObject.GetComponentInChildren<MeleeObject>(true) : null;
        Log($"Equipment: the sword held: right hand {held?._item?.info?.name}, its blow effects [{(melee != null ? HitEffects(melee) : "no melee object")}]; {State()}");

        // A blow of the sword dealt to the nearest villager, through the game's own end of a blow (MeleeObject.OnDamageDealt): the
        // SDK's postfix puts the sword's burning on it.
        var villager = UnityEngine.Object.FindObjectsOfType<Villager>().OrderBy(v => Vector3.Distance(v.transform.position, player!.transform.position)).FirstOrDefault();
        var burning = GameManagers.Get<StatusEffectDatabase>()?.CompleteStatusEffectList?.StatusEffectList?.ToArray().FirstOrDefault(t => t.name == "Effect_Burning");
        if (melee != null && villager != null && burning != null)
        {
            bool before = villager.StatusEffects?.HasStatusEffect(burning) ?? false;
            var blow = new DamageData
            {
                damageReceiver = villager.Cast<IDamageReceiver>(),
                hitCollider = villager.GetComponentInChildren<Collider>(),
                weapon = held?._item?.TryCast<WeaponizedItem>(),
                baseDamage = 0,
            };
            melee.OnDamageDealt(blow);
            yield return 0.5f;
            Log($"Equipment: a blow dealt to {villager.name} {Vector3.Distance(villager.transform.position, player!.transform.position):0.0} m off: burning before {before}, after {villager.StatusEffects?.HasStatusEffect(burning)}");
        }
        else Log($"Equipment: no blow: melee {melee != null}, villager {villager != null}, burning {burning != null}");

        // What the player wore, back.
        if (head?.TryCast<EquipmentItem>() is { } oldHead) Log($"Equipment: the old helmet back {equipment.TransferItemForEquipping(oldHead)}");
        if (hand?.TryCast<EquipmentItem>() is { } oldHand) Log($"Equipment: the old tool back {equipment.TransferItemForEquipping(oldHand)}");
        yield return 3f;
        Log($"Equipment: after: head {Point(equipment, "Head")?._item?.info?.name}, right hand {Point(equipment, "RightHand")?._item?.info?.name}; {State()}");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void LogLabItems(string where)
    {
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var sword = db?.GetItemInfo(LabSword);
        var hood = db?.GetItemInfo(LabHood);
        var cake = db?.GetItemInfo(LabCake);
        var weapon = sword?.TryCast<WeaponizedItemInfo>();
        var melee = sword?.spawnObject != null ? sword.spawnObject.GetComponentInChildren<MeleeObject>(true) : null;
        Log($"Equipment: {where}: sword {sword?.GetIl2CppType().Name} storage {sword?.storageClass?.name} damage {weapon?.damageValues} world {sword?.spawnObject?.name} blow effects [{(melee != null ? HitEffects(melee) : "none")}] attributes [{Attributes(sword?.attributes)}]");
        Log($"Equipment: {where}: hood {hood?.GetIl2CppType().Name} storage {hood?.storageClass?.name} attributes [{Attributes(hood?.attributes)}]");
        var food = cake?.TryCast<ConsumableInfo>();
        var eaten = new List<string>();
        for (int i = 0; food?.consumeEffects != null && i < food.consumeEffects.Count; i++)
            eaten.Add($"{food.consumeEffects[i]?.table?.name} {food.consumeEffects[i]?.duration}s");
        Log($"Equipment: {where}: cake {cake?.GetIl2CppType().Name} effects when eaten [{string.Join(", ", eaten)}]");
    }

    private static string HitEffects(MeleeObject melee)
    {
        var effects = new List<string>();
        for (int i = 0; melee.effects != null && i < melee.effects.Count; i++)
            effects.Add($"{melee.effects[i]?.effect?.table?.name} {melee.effects[i]?.effect?.duration}s chance {melee.effects[i]?.chance}");
        return string.Join(", ", effects);
    }

    private static EquipPoint? Point(EquipmentManager equipment, string name)
    {
        for (int i = 0; equipment.equipPoints != null && i < equipment.equipPoints.Count; i++)
            if (equipment.equipPoints[i]?.name == name) return equipment.equipPoints[i];
        return null;
    }

    private static EquipmentItem? Find(ItemCollection inventory, string assetName)
    {
        var info = GameManagers.Get<ItemDatabaseManager>()?.Database?.GetItemInfo(assetName);
        var items = info != null ? inventory.GetItems(info) : null;
        for (int i = 0; items != null && i < items.Count; i++)
            if (items[i]?.TryCast<EquipmentItem>() is { } found) return found;
        return null;
    }
}
