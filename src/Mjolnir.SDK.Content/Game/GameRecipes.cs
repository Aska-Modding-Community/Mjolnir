using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio.Inventory;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The game side of <see cref="Recipes"/>, and of what the player knows from the start: a recipe is a copy of a game recipe in
/// the recipe lists of the crafting tables that make it; hand recipes and buildings are in the player's start blueprints,
/// and the local player's inventory when they came after the world was up.
/// </summary>
internal static class GameRecipes
{
    private const string Source = ContentLog.Source;

    // The player's own crafting knows the recipes in this list; a new game's player starts with them (seen in game).
    private const string StartBlueprintsList = "StartBlueprints";

    private static readonly Dictionary<int, List<ItemInfoList>> s_placed = [];
    private static readonly HashSet<int> s_known = []; // blueprints the player knows from the start: hand recipes, buildings
    private static float s_nextCheck;

    // A mod's recipe: a copy of a game recipe (its kind, category and sounds) making the mod's item of its parts.
    public static ItemInfo? Make(ItemIndex index, ModRecipe recipe)
    {
        var result = GameItems.MakeItem(index, recipe.Result);
        if (result == null) return null;
        var parts = new List<ItemInfoQuantity>();
        foreach (var part in recipe.Parts)
        {
            if (GameItems.Quantity(index, part, recipe, "parts") is not { } quantity) return null;
            parts.Add(quantity);
        }
        if (GameItems.Made.TryGetValue(recipe.Id, out var made) && made != null)
        {
            // Ids are the registry's own (it leaves out content whose id another took), so what is made under it is this recipe.
            var known = made.TryCast<CraftBlueprintInfo>();
            if (known == null) return null;
            known.parts = new Il2CppReferenceArray<ItemInfoQuantity>(parts.ToArray()); // parts added later
            known.adjustedParts = new Il2CppReferenceArray<ItemInfoQuantity>(parts.ToArray());
            return made;
        }
        var template = index.Find(recipe.BasedOn)?.TryCast<CraftBlueprintInfo>();
        if (template == null)
        {
            ContentLog.WarnOnce($"{recipe}: no game recipe '{recipe.BasedOn}' to make it from");
            return null;
        }
        var blueprint = Object.Instantiate(template);
        blueprint.name = recipe.AssetName;
        blueprint.id = recipe.Id;
        blueprint.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        blueprint.result = result;
        blueprint.quantity = recipe.Quantity;
        // The build menu shows a recipe by its own picture, which would be the base recipe's result.
        blueprint.icon = result.icon;
        blueprint.previewImage = result.previewImage;
        blueprint.parts = new Il2CppReferenceArray<ItemInfoQuantity>(parts.ToArray());
        // The base recipe's adjusted parts (the world's cost settings) would show its own cost until the game computes them.
        blueprint.adjustedParts = new Il2CppReferenceArray<ItemInfoQuantity>(parts.ToArray());
        blueprint.blueprintConditionsRules = new Il2CppSystem.Collections.Generic.List<BlueprintConditionsRule>();
        GameItems.Made[recipe.Id] = blueprint;
        return blueprint;
    }

    // A recipe joins the lists of the crafting tables that make it: those making its base recipe, or those of the places
    // the mod named. A station's table knows its list's recipes as it wakes, and the player's own crafting knows the start
    // list's, which the game gives to a player who lacks them. Lists it no longer belongs to give it up.
    public static void Place(ModRecipe recipe, ItemInfo blueprint, CraftingLists lists)
    {
        var wanted = new List<ItemInfoList>();
        void Want(ItemInfoList list)
        {
            if (!wanted.Any(w => w.Pointer == list.Pointer)) wanted.Add(list);
        }
        if (recipe.Places.Count == 0 && lists.Index.Find(recipe.BasedOn) is { } template)
        {
            foreach (var list in lists.Making(template)) Want(list);
        }
        foreach (string place in recipe.Places)
        {
            var at = lists.At(place, out string? problem);
            if (problem != null) ContentLog.WarnOnce($"{recipe}: {problem}");
            foreach (var list in at) Want(list);
        }
        if (wanted.Count == 0) ContentLog.WarnOnce($"{recipe}: no crafting table makes it");
        if (s_placed.TryGetValue(recipe.Id, out var placed))
        {
            foreach (var old in placed.Where(p => !wanted.Any(w => w.Pointer == p.Pointer))) old.itemInfoList?.Remove(blueprint);
        }
        foreach (var list in wanted)
        {
            if (GameItems.IndexOf(list.itemInfoList, blueprint) < 0) list.itemInfoList.Add(blueprint);
        }
        s_placed[recipe.Id] = wanted;
        if (lists.Hand != null && wanted.Any(w => w.Pointer == lists.Hand.Pointer)) s_known.Add(recipe.Id);
        else s_known.Remove(recipe.Id);
    }

    // A building is known from the start as a hand recipe is: in the start blueprints, which the game gives a player who
    // lacks them as the player appears.
    public static void KnowFromStart(ItemInfo blueprint, CraftingLists lists)
    {
        if (lists.Hand == null)
        {
            ContentLog.WarnOnce("Items: the player's start blueprints were not found");
            return;
        }
        if (GameItems.IndexOf(lists.Hand.itemInfoList, blueprint) < 0) lists.Hand.itemInfoList.Add(blueprint);
        s_known.Add(blueprint.id);
    }

    // The game gives the player each recipe of the start list it lacks as the player appears, an older save's player too
    // (seen in game). A hand recipe a mod adds once the world is up would be known only from the next load: it goes into
    // the local player's inventory, checked every two seconds (the save's inventory loads after the player appears, so a
    // recipe given too early would be lost). A building goes the same way; a station's recipe stays out of it: the player's
    // own crafting would show it.
    public static void KnowBlueprints()
    {
        if (Time.realtimeSinceStartup < s_nextCheck) return;
        s_nextCheck = Time.realtimeSinceStartup + 2;
        if (s_known.Count == 0) return;
        var inventory = GameItems.PlayerInventory();
        if (inventory == null) return;
        int given = 0;
        foreach (int id in s_known)
        {
            if (GameItems.Made.TryGetValue(id, out var blueprint) && blueprint != null && !inventory.HasItem(blueprint))
                given += inventory.AddItems(blueprint, 1);
        }
        if (given > 0) SdkLog.Info(Source, $"Items: the player now knows {given} mod recipe(s) and building(s)");
    }

    private static ItemInfoList? StartBlueprints()
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ItemInfoList>());
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i]?.name == StartBlueprintsList) return all[i].TryCast<ItemInfoList>();
        }
        return null;
    }

    /// <summary>
    /// The recipe lists of the game's crafting tables, read once per database build: the player's own (the start
    /// blueprints) and those of the tables in the database's structures (stations and their add-ons; a template's
    /// structure is a prefab).
    /// </summary>
    internal sealed class CraftingLists(ItemIndex index)
    {
        private List<List<ItemInfoList>>? _tables;

        public ItemIndex Index { get; } = index;

        public ItemInfoList? Hand { get; } = StartBlueprints() is { itemInfoList: not null } hand ? hand : null;

        /// <summary>The lists that hold a game recipe: where the game makes it.</summary>
        public IEnumerable<ItemInfoList> Making(ItemInfo recipe)
        {
            if (Hand != null && GameItems.IndexOf(Hand.itemInfoList, recipe) >= 0) yield return Hand;
            _tables ??= Tables(Index.List);
            foreach (var list in _tables.SelectMany(t => t))
            {
                if (GameItems.IndexOf(list.itemInfoList, recipe) >= 0) yield return list;
            }
        }

        /// <summary>
        /// The lists of a place: the hand's, or those of a structure's tables and of the structures it is upgraded to;
        /// <paramref name="problem"/> says why a place has none.
        /// </summary>
        public List<ItemInfoList> At(string place, out string? problem)
        {
            problem = null;
            if (string.Equals(place, ModRecipe.ByHand, StringComparison.OrdinalIgnoreCase))
            {
                if (Hand == null) problem = "the player's own crafting list was not found";
                return Hand == null ? [] : [Hand];
            }
            var template = Index.Find(place)?.TryCast<StructureTemplate>();
            if (template == null)
            {
                problem = $"no structure '{place}' in the game";
                return [];
            }
            var found = new List<ItemInfoList>();
            var seen = new HashSet<IntPtr>();
            var next = new Queue<StructureTemplate>([template]);
            while (next.Count > 0)
            {
                var structure = next.Dequeue();
                if (!seen.Add(structure.Pointer)) continue;
                found.AddRange(TablesOf(structure).Where(l => !found.Any(f => f.Pointer == l.Pointer)));
                var upgrades = structure.structureUpgrades;
                for (int i = 0; upgrades != null && i < upgrades.Count; i++)
                {
                    if (upgrades[i] != null) next.Enqueue(upgrades[i]);
                }
            }
            if (found.Count == 0) problem = $"the structure '{place}' has no crafting table";
            return found;
        }

        private static List<List<ItemInfoList>> Tables(Il2CppSystem.Collections.Generic.List<ItemInfo> database)
        {
            var tables = new List<List<ItemInfoList>>();
            for (int i = 0; i < database.Count; i++)
            {
                if (database[i]?.TryCast<StructureTemplate>() is { } template && TablesOf(template) is { Count: > 0 } lists) tables.Add(lists);
            }
            return tables;
        }

        // The recipe lists of the crafting tables in a template's structure.
        private static List<ItemInfoList> TablesOf(StructureTemplate template)
        {
            var structure = template.TryCast<NodeTemplate>()?.nodeStructure;
            if (structure == null) return [];
            var lists = new List<ItemInfoList>();
            foreach (var table in structure.GetComponentsInChildren<CraftInteraction>(true))
            {
                var list = table.localBlueprintsList;
                if (list?.itemInfoList != null && !lists.Any(l => l.Pointer == list.Pointer)) lists.Add(list);
            }
            return lists;
        }
    }
}
