using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Localization;
using SSSGame;
using UnityEngine;
using GameStringDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, string>;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The game side of <see cref="Items"/>, <see cref="Recipes"/> and <see cref="Buildings"/>.
/// The game builds its item database from one list asset (<c>CompleteItemInfoList</c>, kept from the main menu to the
/// world) each time a scene with it loads: a hidden prefix on <c>ItemInfoDatabase.Initialize</c> puts the mods' items,
/// recipes and buildings at the list's end first, made once per game from the game's content they are based on. Their
/// texts go into the game's dictionaries under the keys the game makes for them (<see cref="ContentTexts"/>). A recipe also
/// joins the recipe lists of the crafting tables that make it (<see cref="GameRecipes"/>), list assets too: a station's
/// table knows its list's recipes as it wakes, and the player's own crafting is the start blueprints, which the game gives a
/// player who lacks them as the player appears (a new game and an older save alike); a hand recipe added once the world is
/// up goes into the local player's inventory, where the game looks for the recipes a player knows. A building is a copy of
/// a game structure template, known from the start as a hand recipe is; its cost, when the mod gives one, is an item config
/// of the mod's own on its build site's parts and in the build menu (<see cref="GameBuildings"/>, hidden patches).
/// </summary>
internal static class GameItems
{
    private const string Source = ContentLog.Source;

    /// <summary>The mods' content made in the game, by id: items, recipes and buildings, each made once per game.</summary>
    internal static readonly Dictionary<int, ItemInfo> Made = [];

    private static ItemInfoDatabase? s_database;
    private static int s_applyQueued;
    private static int s_appliedVersion = -1;

    /// <summary>The module's start (phase A: Harmony only, no Unity objects).</summary>
    public static void Start()
    {
        try
        {
            SdkLog.Info(Source, $"Items started: {Install()} game methods patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Items: patching the game failed: {e}");
        }
        try
        {
            ContentRegistry.Shared.Updated += ApplySoon;
            GameLoop.Update += () => ContentLog.Guard("recipes and buildings the player knows", GameRecipes.KnowBlueprints);
            GameLoop.Update += () => ContentLog.Guard("the looks of mod buildings", GameBuildingModels.Tick);
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Items not started: {e}");
        }
    }

    // Each patch class on its own: one the game refuses leaves the others working.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Install()
    {
        var harmony = new Harmony("mjolnir.sdk.content.items");
        return Patch("item database", () => harmony.CreateClassProcessor(typeof(ItemPatches)).Patch()?.Count ?? 0)
               + Patch("effect look-ups", () => harmony.CreateClassProcessor(typeof(EffectPatches)).Patch()?.Count ?? 0)
               + Patch("equipment", () => EquipmentPatches.Install(harmony))
               + Patch("building looks", () => harmony.CreateClassProcessor(typeof(BuildingModelPatches)).Patch()?.Count ?? 0);
    }

    private static int Patch(string what, Func<int> patch) => GameGuard.Try(Source, $"Items: patching the {what}", patch, 0);

    /// <summary>The hidden prefix: the game is about to build its database from the list.</summary>
    public static void BeforeDatabase(ItemInfoDatabase database)
    {
        s_database = database;
        GameEquipment.Sweep(); // a database is built as a scene loads: the wearers of the last one may be gone
        Apply(database, built: false);
    }

    /// <summary>Gives the local player items by asset name; how many went in.</summary>
    public static int Give(string assetName, int count)
    {
        if (count <= 0 || !GameLoop.IsGameReady || !Dispatcher.IsMainThread) return 0;
        var info = Find(assetName);
        if (info == null)
        {
            SdkLog.Warn(Source, $"Items.Give({assetName}): no such item");
            return 0;
        }
        return Give(info, count);
    }

    /// <summary>Gives the local player items of the database; how many went in.</summary>
    public static int Give(ItemInfo info, int count)
    {
        if (count <= 0 || !GameLoop.IsGameReady || !Dispatcher.IsMainThread) return 0;
        var inventory = PlayerInventory();
        if (inventory == null)
        {
            SdkLog.Warn(Source, $"Items.Give({info.name}): no local player");
            return 0;
        }
        return inventory.AddItems(info, count);
    }

    /// <summary>The database the game built last, null before it has one or once it is gone (Unity's null check).</summary>
    public static ItemInfoDatabase? Database => s_database != null ? s_database : null;

    /// <summary>Whether the local player is in a world, with an inventory to give to.</summary>
    public static bool HasPlayer => PlayerInventory() != null;

    /// <summary>
    /// Every item of the game's database with its English name, whatever the player's language: the console speaks
    /// English (main thread: the game's localization).
    /// </summary>
    public static IReadOnlyList<ItemEntry> Catalog()
    {
        ApplyPending();
        var list = Database?.CompleteItemInfoList?.itemInfoList;
        if (list == null) return [];
        // The game's getter loads the fallback (English) dictionary on first use: an English player's game may never
        // have needed it, and its field is null until then.
        var english = LocalizationManager.Instance?.FallbackLocalizedStrings;
        var entries = new List<ItemEntry>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var info = list[i];
            if (info == null) continue;
            var recipe = info.TryCast<CraftBlueprintInfo>();
            string name = EnglishName(english, info);
            // A recipe without a name of its own goes by its result's.
            if (name.Length == 0 && recipe?.result != null) name = EnglishName(english, recipe.result);
            entries.Add(new ItemEntry(info.name, name, info.TryCast<BlueprintInfo>() != null));
        }
        return entries;
    }

    // The item's name in the game's fallback dictionary, which is English (mods' items have their English texts there).
    private static string EnglishName(GameStringDictionary? english, ItemInfo info)
    {
        try
        {
            return english != null && english.TryGetValue(info.GetKey("name"), out string text) && text != null ? text : "";
        }
        catch (Exception)
        {
            return ""; // an item without texts
        }
    }

    // Every change of the registry (each builder call) asks for one apply at the start of a later frame, however many came
    // in between: a building's settings chained in one statement are applied once.
    private static void ApplySoon()
    {
        if (Interlocked.Exchange(ref s_applyQueued, 1) == 1) return;
        Dispatcher.Enqueue(() =>
        {
            Volatile.Write(ref s_applyQueued, 0);
            ContentLog.Guard("added content", ApplyLater);
            ContentLog.Guard("added effects", () => GameEffects.Apply(GameLoop.IsGameReady ? GameManagers.Get<StatusEffectDatabase>() : null));
        }, "SDK.Content");
    }

    // A look-up on the main thread does at once the apply a change asked for, so content added and given in one frame is
    // there to give (the queued apply then finds nothing new).
    private static void ApplyPending()
    {
        if (Volatile.Read(ref s_applyQueued) == 1 && Dispatcher.IsMainThread) ContentLog.Guard("added content", ApplyLater);
    }

    // Content a mod added after the database was built: into the live database (its map and indexes too), unless nothing
    // changed since it was given the mods' content.
    private static void ApplyLater()
    {
        if (Database is not { } database || database.CompleteItemInfoList == null) return;
        if (s_appliedVersion == ContentRegistry.Shared.Version) return;
        Apply(database, built: true);
    }

    private static void Apply(ItemInfoDatabase database, bool built)
    {
        var list = database.CompleteItemInfoList?.itemInfoList;
        if (list == null) return;
        var registry = ContentRegistry.Shared;
        int version = registry.Version;
        var index = ItemIndex.Of(list);
        var added = new List<ItemInfo>();
        foreach (var item in registry.Items.OrderBy(i => i.Id))
        {
            if (MakeItem(index, item) is not { } info) continue;
            var template = index.Find(item.BasedOn);
            GameEquipment.Set(info, item, template, list);
            GameModels.Dress(info, item, template);
            if (Append(index, info)) added.Add(info);
        }
        GameRecipes.CraftingLists? lists = null;
        foreach (var recipe in registry.Recipes.OrderBy(r => r.Id))
        {
            if (GameRecipes.Make(index, recipe) is { } info)
            {
                if (Append(index, info)) added.Add(info);
                GameRecipes.Place(recipe, info, lists ??= new GameRecipes.CraftingLists(index));
            }
        }
        foreach (var building in registry.Buildings.OrderBy(b => b.Id))
        {
            if (GameBuildings.Make(index, building) is { } info)
            {
                if (Append(index, info)) added.Add(info);
                GameRecipes.KnowFromStart(info, lists ??= new GameRecipes.CraftingLists(index));
            }
        }
        BuildingPrefabs.Register();
        if (built)
        {
            // Initialize already ran: the new entries get what it would have given them.
            foreach (var info in added)
            {
                info.databaseIndex = (ushort)IndexOf(list, info);
                database._itemsMap[info.id] = info;
                info.Configure();
            }
        }
        s_appliedVersion = version;
        if (added.Count > 0) SdkLog.Info(Source, $"Items: {added.Count} mod item(s) and recipe(s) in the game's database ({list.Count} entries)");
    }

    /// <summary>The mods' items made in the game, with their copies and their base items (the last database's).</summary>
    public static IEnumerable<(ModItem Item, ItemInfo Info, ItemInfo? Template)> MadeItems()
    {
        var list = Database?.CompleteItemInfoList?.itemInfoList;
        var index = list != null ? ItemIndex.Of(list) : null;
        foreach (var item in ContentRegistry.Shared.Items)
        {
            if (!Made.TryGetValue(item.Id, out var info) || info == null) continue;
            yield return (item, info, index?.Find(item.BasedOn));
        }
    }

    // A mod's item: a copy of its base item, made once per game, with the mod's id, asset name, picture and stack. Its id is
    // its own: the registry leaves out content whose id another took.
    internal static ItemInfo? MakeItem(ItemIndex index, ModItem item)
    {
        if (Made.TryGetValue(item.Id, out var made) && made != null) return made;
        var template = index.Find(item.BasedOn);
        if (template == null || template.TryCast<BlueprintInfo>() != null)
        {
            ContentLog.WarnOnce($"{item}: no game item '{item.BasedOn}' to make it from{(template != null ? " (that is a recipe)" : "")}");
            return null;
        }
        var info = Object.Instantiate(template);
        info.name = item.AssetName;
        info.id = item.Id;
        info.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        if (item.StackSize is { } stack) info.stackSize = stack;
        if (item.IconPath != null && ContentPictures.Picture(item.IconPath) is { } sprite)
        {
            info.icon = sprite;
            info.previewImage = sprite;
        }
        Made[item.Id] = info;
        ContentTexts.Add(ModStringsRegistry.ForModId(item.ModId), "item", item.Name, info);
        return info;
    }

    /// <summary>A recipe's or a building's part in the game's terms; null (warned once) without such an item.</summary>
    internal static ItemInfoQuantity? Quantity(ItemIndex index, RecipePart part, object owner, string forWhat)
    {
        var info = index.Find(part.Item);
        if (info == null)
        {
            ContentLog.WarnOnce($"{owner}: no item '{part.Item}' for its {forWhat}");
            return null;
        }
        var quantity = new ItemInfoQuantity { itemInfo = info };
        quantity.quantity = part.Count;
        return quantity;
    }

    internal static ItemCollection? PlayerInventory()
    {
        try
        {
            return GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        }
        catch (Exception)
        {
            return null; // the game's LocalPlayer throws outside a session
        }
    }

    private static ItemInfo? Find(string assetName)
    {
        ApplyPending();
        var list = Database?.CompleteItemInfoList?.itemInfoList;
        return list == null ? null : ItemIndex.Of(list).Find(assetName);
    }

    private static bool Append(ItemIndex index, ItemInfo info)
    {
        if (IndexOf(index.List, info) >= 0) return false;
        index.List.Add(info);
        index.Appended(info);
        return true;
    }

    internal static int IndexOf(Il2CppSystem.Collections.Generic.List<ItemInfo> list, ItemInfo info)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] != null && list[i].Pointer == info.Pointer) return i;
        }
        return -1;
    }
}

/// <summary>
/// The game's item database by asset name: the first entry of a name as the list holds them, then the mods' content made
/// (<see cref="GameItems.Made"/>). Read once for a list and kept while the list keeps its length, so finding an item never
/// walks the database.
/// </summary>
internal sealed class ItemIndex
{
    private static ItemIndex? s_last;

    private readonly Dictionary<string, ItemInfo> _byName = new(StringComparer.Ordinal);
    private int _count;

    private ItemIndex(Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        List = list;
        _count = list.Count;
        for (int i = 0; i < _count; i++)
        {
            var info = list[i];
            if (info != null) _byName.TryAdd(info.name, info);
        }
    }

    /// <summary>The database's list.</summary>
    public Il2CppSystem.Collections.Generic.List<ItemInfo> List { get; }

    /// <summary>The index of <paramref name="list"/>: the one read last while the list is the same and as long, else a new one.</summary>
    public static ItemIndex Of(Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        if (s_last != null && s_last.List.Pointer == list.Pointer && s_last._count == list.Count) return s_last;
        return s_last = new ItemIndex(list);
    }

    public ItemInfo? Find(string assetName)
    {
        if (_byName.TryGetValue(assetName, out var info) && info != null) return info;
        return GameItems.Made.Values.FirstOrDefault(m => m != null && m.name == assetName);
    }

    /// <summary>An entry was added to the list's end.</summary>
    public void Appended(ItemInfo info)
    {
        _byName.TryAdd(info.name, info);
        _count = List.Count;
    }
}
