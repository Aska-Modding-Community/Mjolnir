using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.Attributes;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "equipment research" scenario: what makes an item what it is, for an item kind of the content module and for weapons and
/// armour of a mod's own. (A) in the main menu every item a player can carry by its class and storage class
/// (shots/item-kinds.txt), then each weapon, piece of armour, food and container in full (shots/equipment.txt): the item's own
/// attributes, a weapon's damage and type, a ranged weapon's shot, a wearable's bodies, food's effects, a container's room,
/// equip and stow handles, and on its world object the melee object (its attack set, damage, effects on hit), the sounds
/// played by animator events and the audio sources; every attack set (shots/movesets.txt). (B) in the world what the player
/// has equipped, each item's attributes beside the player's armour and its modifiers, the weapon in hand's melee object, and
/// the player's animator (its controller, whether it is an override, its clips: shots/player-animator.txt). Nothing is
/// changed. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> EquipmentResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var list = db?.CompleteItemInfoList?.itemInfoList;
        var carried = new List<ItemInfo>();
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var info = list[i];
            if (info == null || info.TryCast<BlueprintInfo>() != null) continue;
            if (info.storageClass == null || info.storageClass.name == "HarvestItem" || info.stackSize < 1) continue;
            carried.Add(info);
        }

        var kinds = new StringBuilder();
        foreach (var group in carried.GroupBy(i => (Class: i.GetIl2CppType().Name, Storage: i.storageClass.name)).OrderBy(g => g.Key.Class).ThenBy(g => g.Key.Storage))
        {
            kinds.AppendLine($"{group.Key.Class} | {group.Key.Storage} | {group.Count()} | stacks {string.Join("/", group.Select(i => i.stackSize).Distinct().OrderBy(s => s))} | " +
                             $"categories {string.Join(", ", group.Select(i => i.category?.name).Distinct())} | e.g. {string.Join(", ", group.Take(6).Select(i => i.name))}");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "item-kinds.txt"), kinds.ToString());

        var detail = new StringBuilder();
        foreach (var info in carried)
        {
            try
            {
                string? text = ItemDetail(info);
                if (text != null) detail.AppendLine(text);
            }
            catch (Exception e)
            {
                detail.AppendLine($"== {info.name}: {e.GetType().Name} {e.Message}");
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "equipment.txt"), detail.ToString());

        var movesets = new StringBuilder();
        foreach (var m in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<InteractionMoveset>()).Select(o => o.Cast<InteractionMoveset>()).OrderBy(m => m.name))
        {
            movesets.AppendLine($"{m.name} ({m.GetIl2CppType().Name}): sub-moveset {m.animatorSubMovesetID}, action '{m.attackActionName}' id {m.actionId}, damage event '{m.animatorDamageEvent}', " +
                                $"required category {m.requiredEqippmentCategory?.name}, unarmed {m.baseUnarmedDamage}, damage x{m.damageMultiplier}, weapon wear {m.weaponDurabilityDamage}, " +
                                $"expertise {m.expertise?.name}, weights [{Attributes(m.attributeWeights)}], stamina discount {m.staminaUsageDiscount}, display {m.equipHandleDisplay?.name} {m.displayDuration}");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "movesets.txt"), movesets.ToString());
        Log($"Equipment: {carried.Count} carried items in {carried.GroupBy(i => (i.GetIl2CppType().Name, i.storageClass.name)).Count()} kinds; details and attack sets written");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (player == null)
        {
            Log("Equipment: no player");
            yield break;
        }
        var world = new StringBuilder();
        var equipment = player.GetComponentInChildren<EquipmentManager>(true);
        world.AppendLine($"equipment manager {equipment?.name} ({equipment?.GetIl2CppType().Name}), points {equipment?.equipPoints?.Count}");
        for (int i = 0; equipment?.equipPoints != null && i < equipment.equipPoints.Count; i++)
        {
            var point = equipment.equipPoints[i];
            var item = point?._item;
            if (point == null) continue;
            var ids = new List<string>();
            for (int k = 0; point.ids != null && k < point.ids.Count; k++) ids.Add(point.ids[k]?.name ?? "null");
            world.AppendLine($"  point '{point.name}' [{string.Join(", ", ids)}] container {point.containerType?.name}: " +
                             (item == null ? "empty" : $"{item.info?.name} ({item.info?.GetIl2CppType().Name}) equipped {item.IsEquipped()} stowed {item.IsStowed()}, attributes [{Attributes(item.info?.attributes)}]"));
            var melee = point._itemObject != null ? point._itemObject.GetComponentInChildren<MeleeObject>(true) : null;
            if (melee != null) world.AppendLine($"    melee {Melee(melee)}");
        }
        var sheet = Mjolnir.SDK.Player.LocalPlayer.Sheet;
        world.AppendLine($"player's armour (sheet) {sheet?.Find("Attribute_Armor")?.Value}");
        var attributes = player.GetComponentInChildren<AttributeManager>(true);
        world.AppendLine($"attribute manager {attributes != null}");

        var animator = player.GetComponentInChildren<Animator>(true);
        var controller = animator?.runtimeAnimatorController;
        var overrides = controller?.TryCast<AnimatorOverrideController>();
        world.AppendLine($"animator '{animator?.name}' controller '{controller?.name}' ({controller?.GetIl2CppType().Name}), override {overrides != null} of '{overrides?.runtimeAnimatorController?.name}', " +
                         $"clips {controller?.animationClips?.Length}, layers {animator?.layerCount}");
        for (int l = 0; animator != null && l < animator.layerCount; l++) world.AppendLine($"  layer {l} '{animator.GetLayerName(l)}' weight {animator.GetLayerWeight(l)}");
        var clips = controller?.animationClips;
        var names = new List<string>();
        for (int c = 0; clips != null && c < clips.Length; c++)
            if (clips[c] != null) names.Add($"{clips[c].name} {clips[c].length:0.00}s");
        world.AppendLine("clips:").AppendLine(string.Join("\n", names.Distinct().OrderBy(n => n)));
        for (int p = 0; animator != null && p < animator.parameterCount; p++)
        {
            var parameter = animator.GetParameter(p);
            world.AppendLine($"  parameter {parameter.name} {parameter.type}");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "player-equipment.txt"), world.ToString());
        Log($"Equipment: the player's equipment and animator written ({names.Count} clips, override {overrides != null})");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // An item in full when it is equipment, food or a container; null for the rest.
    private static string? ItemDetail(ItemInfo info)
    {
        var equipmentInfo = info.TryCast<EquipmentItemInfo>();
        var consumable = info.TryCast<ConsumableInfo>();
        var container = info.TryCast<ContainerInfo>();
        if (equipmentInfo == null && consumable == null && container == null) return null;
        var text = new StringBuilder($"== {info.name} ({info.GetIl2CppType().Name}) storage {info.storageClass?.name} category {info.category?.name} tier {info.tier} stack {info.stackSize}");
        text.Append($"\n  attributes [{Attributes(info.attributes)}] networked [{string.Join(", ", (info.networkedInventoryAttributes ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AttributeConfig>(0L)).Select(a => a?.name))}]");
        if (equipmentInfo != null)
            text.Append($"\n  equip [{string.Join(", ", (equipmentInfo.equipTargets ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EquipHandleId>(0L)).Select(h => h?.name))}] stow {equipmentInfo.stowTarget?.name} auto {equipmentInfo.autoEquip}");
        if (info.TryCast<WeaponizedItemInfo>() is { } weapon)
            text.Append($"\n  weapon: damage {weapon.damageValues} wear {weapon.durabilityOnUse} type '{weapon.damageType?.name}' style {weapon.damageType?.style} '{weapon.damageType?.weaponType}' " +
                        $"vulnerability {weapon.damageType?.vulnerabilityConfig?.name} warrior {weapon.warriorGear}");
        if (info.TryCast<RangedWeaponInfo>() is { } ranged)
            text.Append($"\n  ranged: speed {ranged.lowPowerShotSpeed}..{ranged.highPowerShotSpeed} spread x{ranged.spreadMultiplier} weight {ranged.bowWeight}");
        if (info.TryCast<WearableItemInfo>() is { } wearable)
            text.Append($"\n  wearable: bodies [{string.Join(", ", (wearable.characterSpecificSetups ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<WearableItemInfo.CharacterSpecificSetup>(0L)).Select(s => $"{s?.characterDefinition?.name}: {s?.spawnObject?.name}"))}] warrior {wearable.warriorGear}");
        if (consumable != null)
        {
            var effects = new List<string>();
            for (int i = 0; consumable.consumeEffects != null && i < consumable.consumeEffects.Count; i++)
            {
                var e = consumable.consumeEffects[i];
                effects.Add($"{e?.table?.name} {e?.duration}s{(e != null && e.persistent ? " persistent" : "")}");
            }
            text.Append($"\n  food: effects [{string.Join(", ", effects)}] random {consumable.modulatedConsumeEffects?.Length ?? 0} moveset {consumable.moveSet?.name}");
        }
        if (container != null)
            text.Append($"\n  container: capacity {container.container?.capacity} type {container.container?.containerType?.name} show fill {container.showFill} drop when used {container.dropItemsWhenUsed}");
        var world = info.spawnObject;
        if (world != null)
        {
            text.Append($"\n  world object {world.name}: {string.Join(", ", world.GetComponents<Component>().Where(c => c != null && c.TryCast<Transform>() == null).Select(c => c.GetIl2CppType().Name))}");
            foreach (var melee in world.GetComponentsInChildren<MeleeObject>(true)) text.Append($"\n  melee {Melee(melee)}");
            foreach (var sounds in world.GetComponentsInChildren<AnimatorEventPlaySounds>(true))
                for (int i = 0; sounds.events != null && i < sounds.events.Length; i++)
                {
                    var e = sounds.events[i];
                    text.Append($"\n  sound on '{e?.animatorEvent}': {SoundNames(e?.soundsList)} source {e?.audioSource?.name} volume {e?.volmeScale}");
                }
            foreach (var source in world.GetComponentsInChildren<AudioSource>(true))
                text.Append($"\n  audio source '{source.name}' clip {source.clip?.name} volume {source.volume} mixer {source.outputAudioMixerGroup?.name}");
            var geometry = world.GetComponentInChildren<EquipmentGeometry>(true);
            if (geometry != null) text.Append($"\n  geometry {geometry.name}: skin {geometry.showSkin} hair {geometry.showHair}, renderers {geometry.GetComponentsInChildren<Renderer>(true).Length} (skinned {geometry.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length})");
        }
        return text.ToString();
    }

    private static string Melee(MeleeObject melee)
    {
        var effects = new List<string>();
        for (int i = 0; melee.effects != null && i < melee.effects.Count; i++)
            effects.Add($"{melee.effects[i]?.effect?.table?.name} {melee.effects[i]?.effect?.duration}s chance {melee.effects[i]?.chance}");
        var events = melee.events;
        return $"'{melee.name}' moveset {melee.moveset?.name} (sub {melee.moveset?.animatorSubMovesetID}, '{melee.moveset?.attackActionName}') wield {melee.wield}/{melee.subWield} shield {melee.isShield} " +
               $"damage {melee.baseDamage} effects [{string.Join(", ", effects)}] size {melee.length}x{melee.thickness} trail {melee.trailSpawner != null} " +
               $"listeners hit {events?.onHit?.GetPersistentEventCount()} damage {events?.onDamage?.GetPersistentEventCount()} activate {events?.onActivate?.GetPersistentEventCount()}";
    }

    private static string SoundNames(SoundsList? sounds)
    {
        var names = new List<string>();
        for (int i = 0; sounds?.clips != null && i < sounds.clips.Count; i++) names.Add(sounds.clips[i]?.name ?? "null");
        return $"{sounds?.name} [{string.Join(", ", names)}]";
    }

    private static string Attributes(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AttributeData>? data)
    {
        var text = new List<string>();
        for (int i = 0; data != null && i < data.Length; i++)
            if (data[i] != null) text.Add($"{data[i].attribute?.name}={data[i].value}{(data[i].multiplier != 1 ? $" x{data[i].multiplier}" : "")}");
        return string.Join(", ", text);
    }
}
