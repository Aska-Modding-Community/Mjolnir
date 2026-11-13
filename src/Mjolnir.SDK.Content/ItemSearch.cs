using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.SDK.Content;

/// <summary>An item as the console's <c>give</c> and <c>items</c> look it up: its asset name and its English name.</summary>
internal readonly record struct ItemEntry(string Asset, string Name, bool IsRecipe);

/// <summary>
/// What the player typed into <c>give</c> or <c>items</c>, found among the game's items (pure). The first rule that
/// finds anything wins: the asset name (with or without its <c>Item_</c>), then the English name (the items before the
/// recipes that make them: a recipe without a name of its own goes by its result's, and is found by its asset name), then
/// every item whose asset name or English name contains the words. Case never matters. The console speaks English: the
/// names of the player's language are not looked at.
/// </summary>
internal static class ItemSearch
{
    private const string AssetPrefix = "Item_";

    public static IReadOnlyList<ItemEntry> Find(IReadOnlyList<ItemEntry> items, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var byAsset = items.Where(i => string.Equals(i.Asset, query, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(i.Asset, AssetPrefix + query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byAsset.Count > 0) return byAsset;
        var byName = items.Where(i => string.Equals(i.Name, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Any(i => !i.IsRecipe)) return [.. byName.Where(i => !i.IsRecipe)];
        if (byName.Count > 0) return byName;
        return Containing(items, query);
    }

    /// <summary>Every item whose asset name or English name contains the words (<c>items</c> lists them all).</summary>
    public static IReadOnlyList<ItemEntry> Containing(IReadOnlyList<ItemEntry> items, string words)
    {
        words = words.Trim();
        if (words.Length == 0) return [];
        return [.. items.Where(i => i.Asset.Contains(words, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(words, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The words of <c>give</c>: an item's name, which may be several words without quotes, and a count after it when the
    /// last word is a whole number (1 without one).
    /// </summary>
    public static (string Item, int Count) GiveArguments(IReadOnlyList<string> words)
    {
        if (words.Count >= 2 && int.TryParse(words[^1], out int count)) return (string.Join(" ", words.Take(words.Count - 1)), count);
        return (string.Join(" ", words), 1);
    }
}
