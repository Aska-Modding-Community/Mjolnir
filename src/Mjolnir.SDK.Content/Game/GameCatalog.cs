using System;
using System.Collections.Generic;
using SandSailorStudio.Inventory;
using UnityEngine;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The game side of the item catalog (<see cref="Items.All"/>): the entries of the game's item database, the categories
/// they are filed in, their texts and pictures. A fault reading the game is
/// logged once and reads as nothing, never reaching the mod that asked.
/// </summary>
internal sealed class GameCatalog : IItemCatalogSource
{
    private readonly Dictionary<int, ItemCategoryInfo> _categories = [];
    private readonly HashSet<string> _warned = [];

    public long Stamp => List() is { } list ? HashCode.Combine(GameItems.Database!.Pointer, list.Count) : 0;

    public IReadOnlyList<CatalogEntry> Entries()
    {
        var entries = new List<CatalogEntry>();
        Try("reading the item database", () =>
        {
            if (List() is not { } list) return;
            for (int i = 0; i < list.Count; i++)
            {
                var info = list[i];
                if (info == null) continue;
                var storage = info.storageClass;
                var category = info.category;
                entries.Add(new CatalogEntry(info.id, info.name, info.TryCast<BlueprintInfo>() != null, storage != null ? storage.name : null,
                                             info.stackSize, category != null ? category.id : 0));
            }
        });
        return entries;
    }

    // Every category an entry is filed in and the ones above it; a category among its own parents ends its chain.
    public IReadOnlyList<CatalogCategory> Categories()
    {
        var categories = new List<CatalogCategory>();
        Try("reading the item categories", () =>
        {
            _categories.Clear();
            if (List() is not { } list) return;
            for (int i = 0; i < list.Count; i++)
            {
                for (var c = list[i]?.category; c != null && !_categories.ContainsKey(c.id); c = c.parent)
                {
                    _categories[c.id] = c;
                    var parent = c.parent;
                    categories.Add(new CatalogCategory(c.id, c.name, parent != null ? parent.id : 0));
                }
            }
        });
        return categories;
    }

    public string? DisplayName(int id) => Read("an item's name", () => Info(id)?.Name);

    public string? Description(int id) => Read("an item's description", () => Info(id)?.Description);

    public Sprite? Icon(int id) => Read("an item's picture", () => Info(id) is { } info && info.icon != null ? info.icon : null);

    public string? CategoryName(int id) => Read("a category's name", () => _categories.GetValueOrDefault(id) is { } c && c != null ? c.Name : null);

    public int Give(int id, int count) => Info(id) is { } info ? GameItems.Give(info, count) : 0;

    public bool CanGive => GameItems.HasPlayer;

    private static Il2CppSystem.Collections.Generic.List<ItemInfo>? List() => GameItems.Database?.CompleteItemInfoList?.itemInfoList;

    private static ItemInfo? Info(int id) => GameItems.Database?.GetItemInfoFromID(id);

    private T? Read<T>(string what, Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            Warn(what, e);
            return null;
        }
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Warn(what, e);
        }
    }

    private void Warn(string what, Exception e)
    {
        bool first;
        lock (_warned) first = _warned.Add(what);
        if (first) SdkLog.Warn(GameLocalization.Source, $"Items: {what} failed: {e.Message}");
    }
}
