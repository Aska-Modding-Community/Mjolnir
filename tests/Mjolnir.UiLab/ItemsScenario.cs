using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Content;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using TMPro;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The UI scenario's check of Mjolnir.SDK.Content's items and recipes with Mjolnir.ItemsExample's twine: in the main
/// menu the item in the game's database; in the world the item (id, texts in the player's language, picture, stack, the
/// rope's model), its hand recipe (result, parts, the player's knowledge of it), Items.Give into the inventory, the
/// inventory and the build menu's category with the recipe on screen; the twine given is taken out again.
/// </summary>
internal static partial class Autopilot
{
    private const string TwineAsset = "Item_mjolnir.itemsexample_twine";
    private const string TwineRecipeAsset = "Item_mjolnir.itemsexample_twine_Recipe";

    private static void ItemsInMenu()
    {
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var twine = db?.GetItemInfo(TwineAsset);
        Log($"Items in the main menu: {db?.CompleteItemInfoList.itemInfoList.Count} entries, twine {(twine == null ? "missing" : $"id {twine.id}, found by id {db!.GetItemInfoFromID(twine.id)?.name == TwineAsset}")}");
    }

    private static IEnumerable<float> ItemsCheck()
    {
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var twine = db?.GetItemInfo(TwineAsset);
        var recipe = db?.GetItemInfo(TwineRecipeAsset)?.TryCast<CraftBlueprintInfo>();
        if (db == null || twine == null || recipe == null)
        {
            Log($"Items: missing (database {db != null}, twine {twine != null}, recipe {recipe != null})");
            yield break;
        }
        Log($"Items: twine id {twine.id} (negative expected), index {twine.databaseIndex} of {db.CompleteItemInfoList.itemInfoList.Count}, by id {db.GetItemInfoFromID(twine.id)?.name == TwineAsset}, " +
            $"name '{twine.Name}', description '{twine.Description}', stack {twine.stackSize} (20 expected), icon {twine.icon?.rect.width}x{twine.icon?.rect.height} (128x128 expected), " +
            $"model {twine.spawnObject?.name}, category {twine.category?.name}");
        Log($"Items: recipe id {recipe.id}, name '{recipe.Name}', result {recipe.result?.name} x{recipe.quantity} (2 expected), parts " +
            $"{string.Join(", ", (recipe.parts ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ItemInfoQuantity>(0L)).Select(p => $"{p.itemInfo?.name} x{p.quantity}"))}, " +
            $"station {recipe.interaction?.name}, category {recipe.category?.name}, the twine's picture {recipe.icon != null && twine.icon != null && recipe.icon.Pointer == twine.icon.Pointer}");

        var inventory = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        if (inventory == null)
        {
            Log("Items: no player's inventory");
            yield break;
        }
        foreach (float wait in WaitFor("the player knowing the twine recipe", () => inventory.HasItem(recipe), 10)) yield return wait;
        int before = inventory.GetItemQuantity(twine); // the save's own twine stays
        int given = Mjolnir.SDK.Content.Items.Give(TwineAsset, 3);
        yield return 0.5f;
        Log($"Items: Give gave {given}; the inventory holds {inventory.GetItemQuantity(twine)} twine ({before + 3} expected)");

        foreach (float wait in OpenForResearch<InventoryMenu>("items-inventory")) yield return wait;
        if (Menu.GetInstance<InventoryMenu>() is { } bag && Menu.IsOpen<InventoryMenu>())
        {
            bag.Close();
            yield return 1.5f;
        }

        Menu.Show<BuildMenu>(null);
        yield return 2f;
        if (Menu.GetInstance<BuildMenu>() is { } build && Menu.IsOpen<BuildMenu>())
        {
            var category = build.GetComponentsInChildren<TabButtonCategory>(false)
                                .FirstOrDefault(b => b.categoryInfos != null && Enumerable.Range(0, b.categoryInfos.Count)
                                    .Any(i => b.categoryInfos[i] != null && recipe.category != null && b.categoryInfos[i].Pointer == recipe.category.Pointer));
            if (category != null)
            {
                Click(category.gameObject);
                yield return 2f;
            }
            var texts = build.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text).ToList();
            Log($"Items: build menu category '{category?.CategoryDisplayName ?? "none"}' for {recipe.category?.name}; the twine's name on screen {texts.Any(t => t.Contains(twine.Name))}");
            Shot("items-build");
            yield return AfterShot;
            Dump(build.gameObject, "items-build");
            build.Close();
            yield return 1.5f;
        }

        inventory.RemoveItems(twine, given);
        yield return 0.5f;
        Log($"Items: the twine given taken out again, {inventory.GetItemQuantity(twine)} left ({before} expected)");
    }
}
