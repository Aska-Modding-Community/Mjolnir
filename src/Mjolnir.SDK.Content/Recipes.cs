using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Recipes of a mod's own, made by hand (the build menu's crafting) or at the game's crafting stations (a workshop, an
/// armorsmith…), where the player crafts and the station's villagers make what they are asked for. A recipe is made
/// from one of the game's recipes: the same kind of recipe, in the same section of the menus, and made where the game
/// makes that recipe, unless <see cref="ModRecipe.MadeAt"/> names the places. A recipe made by hand is known from the start:
/// a new game's player starts with it, and a player of an older save gets it when the world loads; a station knows the
/// recipes of its crafting tables, whatever the player knows.
/// </summary>
/// <remarks>
/// Items, recipes and buildings are entries of the game's item database, found there by id and by asset name. One whose id or
/// asset name falls on content added before it (of this mod or another: the asset names of mod <c>a_b</c>'s item <c>c</c>
/// and of mod <c>a</c>'s item <c>b_c</c> are one) is left out of the game: an error in the log names both, and the mod goes
/// on. Another name gives another id and asset name.
/// </remarks>
public static class Recipes
{
    /// <summary>
    /// Adds a recipe that makes <paramref name="quantity"/> of <paramref name="result"/>; give its ingredients with
    /// <see cref="ModRecipe.Needs(string, int)"/>. It is made from one of the game's recipes (<paramref name="basedOn"/>,
    /// by asset name; the rope's by default): its kind and section of the menus, and where it is made (the rope: by hand
    /// and at the workshop of every level), unless <see cref="ModRecipe.MadeAt"/> names the places. Adding a recipe of the
    /// same item again gives the recipe already added.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModRecipe Add(ModItem result, int quantity = 1, string? basedOn = null) =>
        ContentRegistry.Shared.AddRecipe(CallingMod.Of(Assembly.GetCallingAssembly()), result, quantity, basedOn ?? ModRecipe.DefaultBase);
}

/// <summary>A recipe of a mod, added with <see cref="Recipes.Add"/>.</summary>
public sealed class ModRecipe
{
    /// <summary>
    /// The game's recipe a mod's recipe is made from by default: the rope's (the materials, made by hand and at the
    /// workshop).
    /// </summary>
    public const string DefaultBase = "Item_Binders_BasicRope";

    /// <summary>The place <see cref="MadeAt"/> takes for crafting by hand, in the build menu's crafting.</summary>
    public const string ByHand = "hand";

    private readonly object _lock = new();
    private readonly List<RecipePart> _parts = [];
    private readonly List<string> _places = [];
    private readonly Action _changed;

    internal ModRecipe(string modId, ModItem result, int quantity, string basedOn, Action changed)
    {
        _changed = changed;
        ModId = modId;
        Result = result;
        Quantity = quantity;
        BasedOn = basedOn;
        Id = ContentIds.RecipeId(modId, result.Name);
        AssetName = ContentIds.RecipeAssetName(modId, result.Name);
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>What it makes.</summary>
    public ModItem Result { get; }

    /// <summary>How many it makes.</summary>
    public int Quantity { get; }

    /// <summary>The game's recipe it is made from, by asset name.</summary>
    public string BasedOn { get; }

    /// <summary>Its id in the game (a recipe is an item of the game too).</summary>
    public int Id { get; }

    /// <summary>Its asset name in the game.</summary>
    public string AssetName { get; }

    /// <summary>What it takes, in the order given.</summary>
    public IReadOnlyList<RecipePart> Parts
    {
        get
        {
            lock (_lock) return [.. _parts];
        }
    }

    /// <summary>
    /// Where it is made, in the order given to <see cref="MadeAt"/>: <see cref="ByHand"/> and structures by asset name; empty
    /// while it is made where the game makes the recipe it is based on.
    /// </summary>
    public IReadOnlyList<string> Places
    {
        get
        {
            lock (_lock) return [.. _places];
        }
    }

    /// <summary>
    /// Makes the recipe at <paramref name="place"/> instead of where the game makes the recipe it is based on; call it
    /// again for each further place. A place is <see cref="ByHand"/>, or a structure of the game by asset name, such as
    /// <c>Item_Structures_WorkshopL0</c>: its crafting tables, and those of the structures it is upgraded to (the
    /// workshop's every level). A station knows its recipes as it is built or loaded, so places are given in
    /// <see cref="Mod.OnLoad"/>. Returns the recipe.
    /// </summary>
    public ModRecipe MadeAt(string place)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(place);
        lock (_lock)
        {
            if (_places.Contains(place, StringComparer.Ordinal)) return this;
            _places.Add(place);
        }
        _changed();
        return this;
    }

    /// <summary>Adds an ingredient by asset name (a game item such as <c>Item_Wood_Sticks</c>); returns the recipe.</summary>
    public ModRecipe Needs(string item, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        lock (_lock) _parts.Add(new RecipePart(item, count));
        _changed(); // the game side gives a recipe made already its new part
        return this;
    }

    /// <summary>Adds a mod's item as an ingredient; returns the recipe.</summary>
    public ModRecipe Needs(ModItem item, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Needs(item.AssetName, count);
    }

    public override string ToString() => $"{ModId}.{Result.Name} recipe";
}

/// <summary>An ingredient of a recipe: an item by asset name, and how many.</summary>
public readonly record struct RecipePart(string Item, int Count);
