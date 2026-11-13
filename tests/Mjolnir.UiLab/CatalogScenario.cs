using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Content;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CatalogItems = Mjolnir.SDK.Content.Items;
using GameContextMenu = SSSGame.UI.ContextMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// Scenario "catalog" (with -Examples): Mjolnir.SDK.Content's item catalog and the Cheat Inventory example on it. In the main
/// menu: the items and their categories, a few items' names, pictures, stacks and categories, what is left out (a tree of
/// the world, a mark on the map, a recipe), no giving; the example's window by its key, its tabs and first page, the note
/// and the disabled buttons, a search filtering as it is typed (a screenshot). In the world: the window again, seven sticks
/// typed into their row and added (the inventory counted before and after, a screenshot), then the sticks taken out.
/// </summary>
internal static partial class Autopilot
{
    private const string CatalogKey = "F6";

    private static IEnumerator<float> CatalogScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var all = CatalogItems.All;
        Log($"Catalog: {all.Count} items, {CatalogItems.Categories.Count} top categories: " +
            string.Join(", ", CatalogItems.Categories.Select(c => $"{c.AssetName} '{c.DisplayName}' ({c.Children.Count} under, {all.Count(i => c.Contains(i.Category))} items)")));
        foreach (string asset in new[] { "Item_Wood_Sticks", "Item_Materials_BasicRope", "Item_Helmets_Leather", TwineAsset })
        {
            var item = CatalogItems.Find(asset);
            Log(item == null ? $"Catalog: no {asset}" :
                $"Catalog: {item.AssetName} '{item.DisplayName}', id {item.Id}, stack {item.StackSize}, picture {item.Icon?.name} " +
                $"{(item.Icon != null ? $"{item.Icon.rect.width:0}x{item.Icon.rect.height:0}" : "")}, category {item.Category?.Root.AssetName}/{item.Category?.AssetName} " +
                $"'{item.Category?.DisplayName}', mod {item.ModId ?? "none"}, description '{Short(item.Description)}'");
        }
        Log($"Catalog: left out: a tree {CatalogItems.Find("Item_Wood_Fir1") == null}, a mark {CatalogItems.Find("Item_Misc_BiomeMarkerCave") == null}, " +
            $"a recipe {CatalogItems.Find("Item_Binders_BasicRope") == null}, a world's spruce {CatalogItems.Find("Harvest_Wood_DwarfSpruce") == null} (all True expected); " +
            $"one object {ReferenceEquals(CatalogItems.Find("item_wood_sticks"), all.FirstOrDefault(i => i.AssetName == "Item_Wood_Sticks"))}, " +
            $"can give {CatalogItems.CanGive} (False expected in the main menu)");

        PressNextFrame(CatalogKey);
        yield return 2f;
        var window = CatalogWindowObject();
        if (window == null)
        {
            Log("Catalog: the example's window did not open");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        Log($"Catalog window '{window.name}': {CatalogPage(window)}; world note shown {Shown(window, "WorldOnly")} (True expected), " +
            $"first Add button interactable {FirstGive(window)?.IsInteractable()} (False expected)");
        Shot("catalog-first-page");
        yield return AfterShot;
        if (TryFind(window, "NextPage")?.GetComponentInChildren<Button>(false) is { } next)
        {
            Click(next.gameObject);
            yield return 0.5f;
            Log($"Catalog: the next page: {CatalogPage(window)}");
        }
        if (SearchField(window) is { } search)
        {
            search.text = "верёв";
            yield return 0.5f;
            Log($"Catalog: searched 'верёв': {CatalogPage(window)}");
        }
        Shot("catalog-menu");
        yield return AfterShot;
        if (window.GetComponentsInChildren<TMP_Dropdown>(false).FirstOrDefault() is { } categories)
        {
            if (window.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name.StartsWith("Tab", System.StringComparison.Ordinal) &&
                    b.GetComponentInChildren<TMP_Text>(false)?.text.Contains("Armor", System.StringComparison.OrdinalIgnoreCase) == true) is { } armor)
                Click(armor.gameObject);
            yield return 0.5f;
            categories = window.GetComponentsInChildren<TMP_Dropdown>(false).FirstOrDefault() ?? categories;
            categories.Show();
            yield return 0.5f;
            Log($"Catalog: the category list open: {CheckboxOffsets(categories)} (the frame at the checkmark's x expected, 0)");
            Shot("catalog-dropdown");
            yield return AfterShot;
            categories.Hide();
            yield return 0.5f;
        }
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 4f;
        var inventory = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        var sticks = GameManagers.Get<ItemDatabaseManager>()?.Database?.GetItemInfo("Item_Wood_Sticks");
        int Count() => inventory != null && sticks != null ? inventory.GetItemQuantity(sticks) : -1;
        int before = Count();
        Log($"Catalog: in the world, can give {CatalogItems.CanGive} (True expected), {before} sticks");

        PressNextFrame(CatalogKey);
        yield return 2f;
        window = CatalogWindowObject();
        if (window != null && SearchField(window) is { } field)
        {
            field.text = "Item_Wood_Sticks";
            yield return 0.5f;
            Log($"Catalog: searched 'Item_Wood_Sticks': {CatalogPage(window)}; world note shown {Shown(window, "WorldOnly")} (False expected)");
            var row = TryFind(window, "Item Item_Wood_Sticks");
            var count = row?.GetComponentInChildren<TMP_InputField>(false);
            var give = row != null ? TryFind(row, "Give")?.GetComponentInChildren<Button>(false) : null;
            if (count != null && give != null)
            {
                count.text = "7";
                yield return 0.3f;
                Click(give.gameObject);
                yield return 1f;
                Log($"Catalog: 7 sticks added: {Count()} sticks ({before} + 7 expected), the field '{count.text}'");
                Shot("catalog-world");
                yield return AfterShot;
            }
            else
            {
                Log($"Catalog: no sticks' row to give with (row {row != null}, field {count != null}, button {give != null})");
            }
            MenuManager.Instance.BackPressTopMenu();
            yield return 1f;
        }
        else
        {
            Log("Catalog: the example's window did not open in the world");
        }

        if (inventory != null && sticks != null && Count() > before)
        {
            inventory.RemoveItems(sticks, Count() - before);
            yield return 0.5f;
            Log($"Catalog: the sticks given taken out: {Count()} left");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The example's window: the mod window holding its category tabs.
    private static GameObject? CatalogWindowObject() =>
        Object.FindObjectsByType<GameContextMenu>(FindObjectsSortMode.None).Select(m => m.gameObject).FirstOrDefault(w => TryFind(w, "CatalogCategories") != null);

    private static TMP_InputField? SearchField(GameObject window) =>
        window.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name == "Search")?.GetComponentInChildren<TMP_InputField>(false);

    private static Button? FirstGive(GameObject window) =>
        window.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name == "Give")?.GetComponentInChildren<Button>(false);

    // The page shown: its tab, what it says it found, its page number and its rows.
    private static string CatalogPage(GameObject window)
    {
        var page = window.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name.StartsWith("Page ", System.StringComparison.Ordinal));
        string Text(string name) => page?.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name == name)?.GetComponentInChildren<TMP_Text>(false)?.text ?? "none";
        var rows = page?.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Item ", System.StringComparison.Ordinal)).Select(t => t.name[5..]).ToList() ?? [];
        return $"{page?.name ?? "no page"}, '{Text("Found")}', page '{Text("PageNumber")}', {rows.Count} rows: {string.Join(", ", rows.Take(6))}";
    }
}
