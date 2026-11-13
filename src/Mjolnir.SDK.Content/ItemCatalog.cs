using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.Content;

/// <summary>
/// An item of the game a player can carry, a mod's own included (<see cref="Items.All"/>): what it is called, how it looks,
/// how many stack and where the game files it. One object per item for the whole game, so items compare with ==.
/// </summary>
/// <remarks>
/// Its texts and picture are read from the game on the main thread; on another thread they are the ones last read there.
/// </remarks>
public sealed class ItemDefinition
{
    private readonly ItemCatalog _catalog;
    private string? _displayName;
    private string? _description;

    internal ItemDefinition(ItemCatalog catalog, CatalogEntry entry)
    {
        _catalog = catalog;
        Id = entry.Id;
        AssetName = entry.AssetName;
        StackSize = entry.StackSize;
    }

    /// <summary>The game's id of the item, the one a save keeps (negative for a mod's own).</summary>
    public int Id { get; }

    /// <summary>The game's name of the item (<c>Item_Wood_Sticks</c>, a mod's <see cref="ModItem.AssetName"/>), the same in every language.</summary>
    public string AssetName { get; }

    /// <summary>Its name in the player's language, else its asset name.</summary>
    public string DisplayName => Read(ref _displayName, source => source.DisplayName(Id)) is { Length: > 0 } name ? name : AssetName;

    /// <summary>What it is, in the player's language; "" without a text.</summary>
    public string Description => Read(ref _description, source => source.Description(Id)) ?? "";

    /// <summary>Its picture in the inventory, null for an item without one. Main thread.</summary>
    public Sprite? Icon => Dispatcher.IsMainThread ? _catalog.Source?.Icon(Id) : null;

    /// <summary>How many fit in one slot of the inventory.</summary>
    public int StackSize { get; internal set; }

    /// <summary>Where the game files it (<c>Food</c> under <c>Resources</c>); null for an item the game files nowhere (none does today).</summary>
    public ItemCategory? Category { get; internal set; }

    /// <summary>The mod that added the item, null for the game's own.</summary>
    public string? ModId { get; internal set; }

    /// <summary>The asset name.</summary>
    public override string ToString() => AssetName;

    private string? Read(ref string? last, Func<IItemCatalogSource, string?> read)
    {
        if (!Dispatcher.IsMainThread || _catalog.Source is not { } source) return Volatile.Read(ref last);
        string? now = read(source);
        Volatile.Write(ref last, now);
        return now;
    }
}

/// <summary>
/// A category the game files items in (<see cref="Items.Categories"/>): one of the six it shows at the top (Armor, Bags,
/// Resources, Tools, Weapons, Decorations) or one under them (Food, Helmets…). Only categories that hold items a player can
/// carry are listed. One object per category, so categories compare with ==.
/// </summary>
public sealed class ItemCategory
{
    private readonly ItemCatalog _catalog;
    private string? _displayName;

    internal ItemCategory(ItemCatalog catalog, CatalogCategory category)
    {
        _catalog = catalog;
        Id = category.Id;
        AssetName = category.AssetName;
    }

    /// <summary>The game's id of the category.</summary>
    public int Id { get; }

    /// <summary>The game's name of the category (<c>Categ_Resources_Food</c>), the same in every language.</summary>
    public string AssetName { get; }

    /// <summary>Its name in the player's language, else its asset name.</summary>
    public string DisplayName
    {
        get
        {
            if (Dispatcher.IsMainThread && _catalog.Source is { } source) Volatile.Write(ref _displayName, source.CategoryName(Id));
            return Volatile.Read(ref _displayName) is { Length: > 0 } name ? name : AssetName;
        }
    }

    /// <summary>The category it is under, null for one of the top ones.</summary>
    public ItemCategory? Parent { get; internal set; }

    /// <summary>The top category it is under, itself for a top one.</summary>
    public ItemCategory Root => Parent?.Root ?? this;

    /// <summary>The categories under it that hold items, in the game's order.</summary>
    public IReadOnlyList<ItemCategory> Children { get; internal set; } = [];

    /// <summary>Whether it is this category or one under it.</summary>
    public bool Contains(ItemCategory? category)
    {
        for (var c = category; c != null; c = c.Parent)
        {
            if (c == this) return true;
        }
        return false;
    }

    /// <summary>The asset name.</summary>
    public override string ToString() => AssetName;
}

/// <summary>An entry of the game's item database as the catalog reads it (a blueprint, a world's tree and a map's mark are entries too).</summary>
internal readonly record struct CatalogEntry(int Id, string AssetName, bool IsBlueprint, string? StorageClass, int StackSize, int CategoryId);

/// <summary>A category of the game's database: its id, its asset name and its parent's id (0 for none).</summary>
internal readonly record struct CatalogCategory(int Id, string AssetName, int ParentId);

/// <summary>What the catalog sees of the game (Game/GameCatalog.cs; a fake in the unit tests). Called on the main thread.</summary>
internal interface IItemCatalogSource
{
    /// <summary>Changes when the game's database is built again or an entry is added to it.</summary>
    long Stamp { get; }

    /// <summary>Every entry of the database, in its order; empty before the game has one.</summary>
    IReadOnlyList<CatalogEntry> Entries();

    /// <summary>Every category the entries are filed in, with their parents.</summary>
    IReadOnlyList<CatalogCategory> Categories();

    string? DisplayName(int id);

    string? Description(int id);

    Sprite? Icon(int id);

    string? CategoryName(int id);

    /// <summary>Gives the local player items; how many went in.</summary>
    int Give(int id, int count);

    /// <summary>Whether there is a local player with an inventory.</summary>
    bool CanGive { get; }
}

/// <summary>
/// The items a player can carry, out of the game's item database. The database also holds blueprints, the world's trees,
/// stones and bushes (storage class <c>HarvestItem</c>), marks on the map and the population, none of which goes into an
/// inventory: an item is an entry that is no blueprint, has a storage class that is not <c>HarvestItem</c>, and stacks at
/// least one.
/// </summary>
internal sealed class ItemCatalog(Func<int, string?> modOf)
{
    /// <summary>The storage class of what grows or lies in the world to be harvested, not carried.</summary>
    internal const string WorldStorage = "HarvestItem";

    private readonly object _lock = new();
    private readonly Dictionary<int, ItemDefinition> _items = [];
    private readonly Dictionary<int, ItemCategory> _categories = [];
    private Dictionary<string, ItemDefinition> _byAsset = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ItemDefinition> _all = [];
    private IReadOnlyList<ItemCategory> _roots = [];
    private long? _stamp;

    /// <summary>The game's catalog, its mods found by the content registry.</summary>
    public static ItemCatalog Shared { get; } = new(id => ContentRegistry.Shared.ItemById(id)?.ModId);

    /// <summary>The game's side, set by the module's start.</summary>
    public IItemCatalogSource? Source { get; set; }

    public IReadOnlyList<ItemDefinition> All
    {
        get
        {
            Refresh();
            return Volatile.Read(ref _all);
        }
    }

    public IReadOnlyList<ItemCategory> Roots
    {
        get
        {
            Refresh();
            return Volatile.Read(ref _roots);
        }
    }

    public ItemDefinition? Find(string assetName)
    {
        Refresh();
        return Volatile.Read(ref _byAsset).GetValueOrDefault(assetName);
    }

    public int Give(ItemDefinition item, int count) =>
        count > 0 && Dispatcher.IsMainThread && Source is { } source ? source.Give(item.Id, count) : 0;

    public bool CanGive => Dispatcher.IsMainThread && Source is { CanGive: true };

    /// <summary>Whether an entry is an item a player can carry.</summary>
    internal static bool IsCarried(CatalogEntry entry) =>
        !entry.IsBlueprint && entry.StorageClass is { Length: > 0 } storage && storage != WorldStorage && entry.StackSize > 0;

    /// <summary>Unit tests: forget what was read.</summary>
    internal void Reset()
    {
        lock (_lock)
        {
            _items.Clear();
            _categories.Clear();
            _byAsset = new(StringComparer.OrdinalIgnoreCase);
            _all = [];
            _roots = [];
            _stamp = null;
        }
    }

    // Reads the game again when its database changed (main thread); other threads see the last lists.
    private void Refresh()
    {
        if (!Dispatcher.IsMainThread || Source is not { } source) return;
        long stamp = source.Stamp;
        if (stamp == _stamp) return;
        var entries = source.Entries().Where(IsCarried).ToList();
        var categories = source.Categories();
        lock (_lock)
        {
            var byId = categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var c in byId.Values)
            {
                if (!_categories.ContainsKey(c.Id)) _categories[c.Id] = new ItemCategory(this, c);
            }
            foreach (var c in byId.Values) _categories[c.Id].Parent = ParentOf(c, byId);

            var all = new List<ItemDefinition>(entries.Count);
            var byAsset = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!_items.TryGetValue(entry.Id, out var item)) _items[entry.Id] = item = new ItemDefinition(this, entry);
                item.StackSize = entry.StackSize;
                item.Category = _categories.GetValueOrDefault(entry.CategoryId);
                item.ModId = entry.Id < 0 ? modOf(entry.Id) : null;
                all.Add(item);
                byAsset.TryAdd(entry.AssetName, item);
            }

            // Only categories with items under them, by their ids: the game's own order (Armor, Bags, Resources…).
            var used = all.Where(i => i.Category != null).SelectMany(i => Chain(i.Category!)).Distinct().OrderBy(c => c.Id).ToList();
            foreach (var category in used) category.Children = used.Where(c => c.Parent == category).ToList();
            Volatile.Write(ref _byAsset, byAsset);
            Volatile.Write(ref _all, all);
            Volatile.Write(ref _roots, used.Where(c => c.Parent == null).ToList());
            _stamp = stamp;
        }
    }

    // A category's parent; one among its own parents (the game's blueprint categories are) is a top one.
    private ItemCategory? ParentOf(CatalogCategory category, Dictionary<int, CatalogCategory> byId)
    {
        var seen = new HashSet<int> { category.Id };
        for (int id = category.ParentId; id != 0 && byId.TryGetValue(id, out var parent); id = parent.ParentId)
        {
            if (!seen.Add(id)) return null;
        }
        return category.ParentId != 0 ? _categories.GetValueOrDefault(category.ParentId) : null;
    }

    // The category and the ones above it, the top one first.
    private static IEnumerable<ItemCategory> Chain(ItemCategory category)
    {
        var chain = new List<ItemCategory>();
        for (var c = category; c != null && !chain.Contains(c); c = c.Parent) chain.Insert(0, c);
        return chain;
    }
}
