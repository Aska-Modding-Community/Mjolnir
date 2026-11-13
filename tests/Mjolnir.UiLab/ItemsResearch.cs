using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "items" scenario (autopilot.once containing "items"): the game's items and recipes, for Mjolnir.SDK.Content. In
/// the main menu the item database is dumped (shots/items.txt: every item with its fields and localization keys,
/// shots/blueprints.txt: every blueprint with its result, parts and station), and the blueprint lists and crafting
/// stations among the loaded assets are logged. In the world the player's inventory is logged, then a test item (a copy
/// of a game resource with an id of its own) is registered, given, shown in the inventory and taken out again, so the
/// save is left as it was; then the game quits.
/// </summary>
internal static partial class Autopilot
{
    private const int TestItemId = -424242;

    private static IEnumerator<float> ItemsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (db == null)
        {
            Log("Items: no item database in the main menu");
            yield break;
        }
        var list = db.CompleteItemInfoList.itemInfoList;
        Log($"Items: database '{db.CompleteItemInfoList.name}' with {list.Count} items; types: " +
            string.Join(", ", Items(db).GroupBy(i => i.GetIl2CppType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        long menuList = db.CompleteItemInfoList.Pointer.ToInt64();
        DumpItems(db);
        DumpBlueprints(db);
        LogAssets<ItemInfoList>("blueprint lists", l => $"{l.name} {l.itemInfoList?.Count ?? 0}: {string.Join(", ", Enumerable.Range(0, System.Math.Min(4, l.itemInfoList?.Count ?? 0)).Select(i => l.itemInfoList![i]?.name))}");
        LogAssets<CraftInteraction>("craft interactions", c => $"{c.name} list '{c.localBlueprintsList?.name}' ({c.localBlueprintsList?.itemInfoList?.Count ?? 0}) station '{c.craftStationHost?.name}'");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 3f;
        var manager = GameManagers.Get<ItemDatabaseManager>();
        db = manager?.Database;
        if (db == null)
        {
            Log("Items: no item database in the world");
            yield break;
        }
        Log($"Items in the world: {db.CompleteItemInfoList.itemInfoList.Count} items, the same list as the menu's {db.CompleteItemInfoList.Pointer.ToInt64() == menuList}");
        LogAssets<CraftInteraction>("craft interactions in the world", c => $"{c.name} list '{c.localBlueprintsList?.name}' station '{c.craftStationHost?.name}' in scene {c.gameObject.scene.IsValid()}");

        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var inventory = player?.Inventory?.GetItemCollection();
        var userItems = manager!.GetUserItems();
        Log($"Items: the player's inventory {(inventory == null ? "missing" : $"'{inventory._name}'")}, the user items are the player's {(inventory != null && userItems != null && inventory.Pointer == userItems.Pointer)}; " +
            $"containers: {(inventory == null ? "-" : string.Join(", ", Enumerable.Range(0, inventory._containers.Count).Select(i => inventory._containers[i]).Select(c => c.GetIl2CppType().Name)))}");
        if (inventory == null) yield break;
        Log($"Items: in the inventory {string.Join(", ", Enumerable.Range(0, inventory.GetItemInfos().Count).Select(i => inventory.GetItemInfos()[i]).Select(i => $"{i.name} x{inventory.GetItemQuantity(i)}"))}");

        // A test item: a copy of the rope (a resource that stacks, with a model), an id and asset name of its own, at the
        // end of the list and in the map.
        var template = db.GetItemInfo("Item_Materials_BasicRope");
        var recipe = db.GetItemInfo("Item_Tools_WoodHammer")?.TryCast<CraftBlueprintInfo>();
        var sticks = db.GetItemInfo("Item_Wood_Sticks");
        if (template == null || recipe == null || sticks == null)
        {
            Log($"Items: a template is missing (rope {template != null}, hammer recipe {recipe != null}, sticks {sticks != null})");
            yield break;
        }
        var copy = Object.Instantiate(template);
        copy.name = "Item_Mjolnir_Test";
        copy.id = TestItemId;
        Register(db, copy);
        Log($"Items: test item from '{template.name}' ({template.Name}): key '{copy.GetKey("name")}', name '{copy.Name}', localized {copy.Localized}, " +
            $"found by id {db.GetItemInfoFromID(TestItemId)?.name}, index {copy.databaseIndex}, category {copy.category?.name}");
        int added = inventory.AddItems(copy, 3);
        yield return 0.5f;
        Log($"Items: AddItems gave back {added}; the inventory holds {inventory.GetItemQuantity(copy)} of it");

        // A test recipe: a copy of the wood hammer's hand recipe (VirtualCraftingStation) making the test item of one stick,
        // known by the player as the game's recipes are: the blueprint lies in the inventory.
        var bp = Object.Instantiate(recipe);
        bp.name = "Item_MjolnirBP_Test";
        bp.id = TestItemId - 1;
        bp.result = copy;
        bp.quantity = 1;
        var part = new ItemInfoQuantity { itemInfo = sticks };
        part.quantity = 1;
        bp.parts = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ItemInfoQuantity>(new[] { part });
        Register(db, bp);
        Log($"Items: test recipe: key '{bp.GetKey("name")}', name '{bp.Name}', result {bp.result?.name}, parts {string.Join(", ", bp.parts.Select(p => $"{p.itemInfo?.name} x{p.quantity}"))}, " +
            $"interaction {bp.interaction?.name}, unlocked {SafeUnlocked(bp)}");
        inventory.AddItems(bp, 1);
        yield return 0.5f;
        Log($"Items: the inventory knows the recipe: {inventory.GetItemQuantity(bp)}");

        foreach (float wait in OpenForResearch<InventoryMenu>("inventory-test-item")) yield return wait;
        if (Menu.GetInstance<InventoryMenu>() is { } open && Menu.IsOpen<InventoryMenu>())
        {
            Dump(open.gameObject, "inventory-with-test-item");
            open.Close();
            yield return 1.5f;
        }
        foreach (float wait in OpenForResearch<BuildMenu>("build-test-recipe")) yield return wait;
        if (Menu.GetInstance<BuildMenu>() is { } build && Menu.IsOpen<BuildMenu>())
        {
            var panels = build.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("Mjolnir", System.StringComparison.Ordinal)).Select(t => t.name).ToList();
            Log($"Items: in the build menu objects named after the test {panels.Count}: {string.Join(", ", panels.Take(5))}");
            Dump(build.gameObject, "build-with-test-recipe");
            build.Close();
            yield return 1.5f;
        }

        // Taken out again, so the save is left as it was.
        inventory.RemoveAllItems(bp.GetItemFilter(), true);
        inventory.RemoveAllItems(copy.GetItemFilter(), true);
        yield return 0.5f;
        Log($"Items: after removing, the inventory holds {inventory.GetItemQuantity(copy)} test items and {inventory.GetItemQuantity(bp)} test recipes (0 and 0 expected)");
        foreach (var info in new ItemInfo[] { bp, copy })
        {
            db.CompleteItemInfoList.itemInfoList.Remove(info);
            db._itemsMap.Remove(info.id);
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void Register(ItemInfoDatabase db, ItemInfo info)
    {
        info.databaseIndex = (ushort)db.CompleteItemInfoList.itemInfoList.Count;
        db.CompleteItemInfoList.itemInfoList.Add(info);
        db._itemsMap[info.id] = info;
    }

    private static string SafeUnlocked(CraftBlueprintInfo bp)
    {
        try { return bp.IsUnlocked().ToString(); } catch (System.Exception e) { return $"<{e.GetType().Name}>"; }
    }

    private static IEnumerable<ItemInfo> Items(ItemInfoDatabase db)
    {
        var list = db.CompleteItemInfoList.itemInfoList;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null) yield return list[i];
        }
    }

    // Every item: where it sits, what it is, how it is named and keyed, how it looks and lies in the world.
    private static void DumpItems(ItemInfoDatabase db)
    {
        var text = new StringBuilder();
        foreach (var i in Items(db))
        {
            text.Append($"{i.databaseIndex}\t{i.id}\t{i.name}\t{i.GetIl2CppType().Name}\tcat {i.category?.name}\tstack {i.stackSize}\tunique {i.unique}\ttier {i.tier}\t")
                .Append($"localized {i.Localized}\tname '{i.localizedName}' key {SafeKey(i)} -> '{SafeName(i)}'\tdesc '{Short(i.localizedDescription)}'\t")
                .Append($"icon {i.icon?.name}\tspawn {i.spawnObject?.name}\tstorage {i.storageClass?.name}\tprocesses {i.processes?.Length ?? 0}\t")
                .Append($"components {i.components?.Length ?? 0}\tattributes {i.attributes?.Length ?? 0}\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "items.txt"), text.ToString());
        Log($"Items: dumped {Items(db).Count()} items to items.txt");
    }

    // Every blueprint: what it makes, of what, where, under which conditions.
    private static void DumpBlueprints(ItemInfoDatabase db)
    {
        var text = new StringBuilder();
        int count = 0;
        foreach (var info in Items(db))
        {
            var bp = info.TryCast<BlueprintInfo>();
            if (bp == null) continue;
            count++;
            var craft = bp.TryCast<CraftBlueprintInfo>();
            string parts = bp.parts == null ? "-" : string.Join(", ", bp.parts.Where(p => p != null).Select(p => $"{p.itemInfo?.name} x{p.quantity}"));
            text.Append($"{bp.id}\t{bp.name}\t{bp.GetIl2CppType().Name}\tcat {bp.category?.name}\tresult {bp.result?.name} x{bp.quantity}\tcost {bp.cost?.itemInfo?.name} x{bp.cost?.quantity}\t")
                .Append($"parts {parts}\tauto {bp.autoConstruct}\tconsume {bp.consumeOnUse}");
            if (craft != null)
            {
                text.Append($"\tinteraction {craft.interaction?.name}\tvolume {craft.craftVolume}\tconditions {craft.blueprintConditionsRules?.Count ?? 0}\t")
                    .Append($"trial {craft.availableInTrialVersion}\tnotify {craft.showUnlockNotification}");
            }
            text.Append($"\tname '{SafeName(bp)}'\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "blueprints.txt"), text.ToString());
        Log($"Items: dumped {count} blueprints to blueprints.txt");
    }

    private static void LogAssets<T>(string what, System.Func<T, string> describe) where T : Object
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>());
        var found = Enumerable.Range(0, all.Length).Select(i => all[i]?.TryCast<T>()).Where(t => t != null).Select(t => t!).ToList();
        Log($"Items: {found.Count} {what}");
        foreach (var t in found.Take(60)) Log($"Items:   {describe(t)}");
    }

    private static string SafeKey(ItemInfo info)
    {
        try { return info.GetKey("name"); } catch (System.Exception e) { return $"<{e.GetType().Name}>"; }
    }

    private static string SafeName(ItemInfo info)
    {
        try { return info.Name; } catch (System.Exception e) { return $"<{e.GetType().Name}>"; }
    }

    private static string Short(string? text) => text == null ? "" : text.Length > 40 ? text[..40] + "…" : text;
}
