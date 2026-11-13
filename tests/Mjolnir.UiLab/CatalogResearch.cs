using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// Scenario "catalog research": the item database as a catalog of what can be given, in the main menu. Its categories as a tree
/// (shots/categories.txt: id, asset name, the name in the player's language, parent, picture, the items under each), the entries
/// that are no blueprint by their kind, and each such item's name, picture, stack, uniqueness and category path
/// (shots/catalog.txt). Then it quits.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> CatalogResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (db == null)
        {
            Log("Catalog: no item database in the main menu");
            Application.Quit();
            yield break;
        }

        var all = Items(db).ToList();
        var items = all.Where(i => i.TryCast<BlueprintInfo>() == null).ToList();
        Log($"Catalog: {all.Count} entries, {items.Count} not blueprints; kinds: " +
            string.Join(", ", items.GroupBy(i => i.GetIl2CppType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        Log($"Catalog: with a picture {items.Count(i => i.icon != null)}, with a world object {items.Count(i => i.spawnObject != null)}, " +
            $"unique {items.Count(i => i.unique)}, without a category {items.Count(i => i.category == null)}, " +
            $"stacks {string.Join(", ", items.GroupBy(i => i.stackSize).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}"))}");
        foreach (var kind in items.GroupBy(i => i.GetIl2CppType().Name).OrderByDescending(g => g.Count()))
        {
            Log($"Catalog kind {kind.Key}: {kind.Count()}, stack 0 {kind.Count(i => i.stackSize == 0)}, picture {kind.Count(i => i.icon != null)}, " +
                $"world object {kind.Count(i => i.spawnObject != null)}, storage [{string.Join(", ", kind.GroupBy(i => i.storageClass?.name ?? "none").Select(g => $"{g.Key} {g.Count()}"))}], roots {string.Join("/", kind.Select(i => Chain(i.category).FirstOrDefault()?.name).Distinct())}, " +
                $"e.g. {string.Join(", ", kind.Take(3).Select(i => i.name))}");
        }

        // The categories, as the items name them: every one with its parents.
        var categories = new Dictionary<int, ItemCategoryInfo>();
        foreach (var item in all)
        {
            foreach (var c in Chain(item.category)) categories.TryAdd(c.id, c);
        }
        var text = new StringBuilder();
        foreach (var c in categories.Values.OrderBy(c => CategoryPath(c)))
        {
            int under = items.Count(i => Within(i.category, c)), own = items.Count(i => i.category != null && i.category.id == c.id);
            int blueprints = all.Count(i => i.TryCast<BlueprintInfo>() != null && Within(i.category, c));
            text.Append($"{CategoryPath(c)}\tid {c.id}\t'{SafeCategoryName(c)}'\ticon {c.icon?.name}\titems {own} own, {under} under\tblueprints {blueprints}\n");
        }
        File.WriteAllText(System.IO.Path.Combine(ShotsDir, "categories.txt"), text.ToString());
        var roots = categories.Values.Where(c => Chain(c).First().id == c.id).OrderBy(c => c.id).ToList();
        Log($"Catalog: {categories.Count} categories, {roots.Count} roots: " +
            string.Join(", ", roots.Select(r => $"{r.name} '{SafeCategoryName(r)}' ({items.Count(i => Within(i.category, r))} items)")));

        text.Clear();
        foreach (var i in items.OrderBy(i => CategoryPath(i.category)).ThenBy(i => i.name))
        {
            text.Append($"{i.id}\t{i.name}\t{i.GetIl2CppType().Name}\t'{SafeName(i)}'\t{CategoryPath(i.category)}\tstack {i.stackSize}\tunique {i.unique}\t")
                .Append($"icon {i.icon?.name} {(i.icon != null ? $"{i.icon.rect.width}x{i.icon.rect.height}" : "")}\tspawn {i.spawnObject != null}\ttier {i.tier}\tstorage {i.storageClass?.name}\n");
        }
        File.WriteAllText(System.IO.Path.Combine(ShotsDir, "catalog.txt"), text.ToString());
        Log($"Catalog: dumped {items.Count} items to catalog.txt, {categories.Count} categories to categories.txt");
        Log("Autopilot done");
        Application.Quit();
    }

    private static bool Within(ItemCategoryInfo? category, ItemCategoryInfo ancestor) => Chain(category).Any(c => c.id == ancestor.id);

    private static string CategoryPath(ItemCategoryInfo? category) =>
        category == null ? "(none)" : string.Join("/", Chain(category).Select(c => c.name));

    // A category and its parents, the root first; a parent seen already (a category among its own parents) ends the chain.
    private static List<ItemCategoryInfo> Chain(ItemCategoryInfo? category)
    {
        var chain = new List<ItemCategoryInfo>();
        var seen = new HashSet<int>();
        for (var c = category; c != null && chain.Count < 16; c = c.parent)
        {
            if (!seen.Add(c.id))
            {
                if (s_loopsLogged.Add(c.id)) Log($"Catalog: category {c.name} ({c.id}) is among its own parents");
                break;
            }
            chain.Insert(0, c);
        }
        return chain;
    }

    private static readonly HashSet<int> s_loopsLogged = [];

    private static string SafeCategoryName(ItemCategoryInfo category)
    {
        try
        {
            return category.Name ?? "";
        }
        catch (System.Exception e)
        {
            return $"<{e.GetType().Name}>";
        }
    }
}
