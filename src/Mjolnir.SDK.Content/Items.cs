using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Items of a mod's own. An item is made from one of the game's: a kind's plain item (<see cref="ItemKind"/>), or one named
/// (<c>basedOn</c>, its asset name such as <c>Item_Materials_BasicRope</c>): its model in the world, its kind, size and
/// sounds; the mod gives it a name, an
/// id, texts and a picture, and if it likes a model of its own from an AssetBundle (<see cref="ModItem.WithModel"/>). Its texts
/// come from the mod's language files, <c>[item.&lt;name&gt;] name</c>,
/// <c>description</c> and <c>lore</c>, in the player's language, else in English. Its id is the same for every
/// player and start (a hash of the mod id and the name). Items are added in <see cref="Mod.OnLoad"/> and go into the
/// game's item database each time the game builds it (the main menu and every world).
/// </summary>
/// <remarks>
/// Items, recipes and buildings are entries of the game's item database, found there by id and by asset name. One whose id or
/// asset name falls on content added before it (of this mod or another: the asset names of mod <c>a_b</c>'s item <c>c</c>
/// and of mod <c>a</c>'s item <c>b_c</c> are one) is left out of the game: an error in the log names both, and the mod goes
/// on. Another name gives another id and asset name.
/// </remarks>
/// <example><code>
/// var twine = Items.Add("twine", basedOn: "Item_Materials_BasicRope", icon: "items/twine.png", stackSize: 20);
/// Recipes.Add(twine, quantity: 2).Needs("Item_Wood_Sticks", 2).Needs("Item_Materials_BasicRope", 1);
/// var spool = Items.Add("twine_spool", basedOn: "Item_Materials_BasicRope", icon: "items/twine_spool.png")
///                  .WithModel("bundles/models", "TwineSpool");
/// Recipes.Add(spool).Needs(twine, 5).Needs("Item_Wood_Sticks", 1).MadeAt("Item_Structures_WorkshopL0");
/// </code></example>
public static class Items
{
    /// <summary>
    /// Adds an item of the calling mod. <paramref name="name"/> is letters, digits and underscores, unique in the mod;
    /// <paramref name="icon"/> a PNG or JPG file relative to the mod's folder (the base item's picture without one);
    /// <paramref name="stackSize"/> how many fit in one slot (the base item's without one). Adding the same name again
    /// gives the item already added.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or the name is taken by an
    /// item of the mod made from another game item.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModItem Add(string name, string basedOn, string? icon = null, int? stackSize = null) =>
        ContentRegistry.Shared.AddItem(CallingMod.Of(Assembly.GetCallingAssembly()), name, basedOn, icon, stackSize);

    /// <summary>
    /// Adds an item of the calling mod of a kind (a material, food, a weapon, a piece of armour: <see cref="ItemKind"/>),
    /// standing on a plain game item of that kind, so the mod needs no game item's name; the rest as
    /// <see cref="Add(string, string, string?, int?)"/>. Then give it what makes it the mod's: its damage
    /// (<see cref="ModItem.WithDamage"/>), armour and other attributes (<see cref="ModItem.WithAttribute"/>), and effects
    /// (<see cref="ModItem.AppliesOnHit(string, TimeSpan, float)"/>, <see cref="ModItem.AppliesWhileEquipped(string)"/>,
    /// <see cref="ModItem.AppliesWhenEaten(string, TimeSpan)"/>).
    /// </summary>
    /// <example><code>
    /// var sword = Items.Add("rune_sword", ItemKind.OneHandedWeapon, icon: "items/rune_sword.png")
    ///                  .WithDamage(14, 18)
    ///                  .AppliesOnHit("Effect_Burning", TimeSpan.FromSeconds(4), chance: 0.25f);
    /// var hood = Items.Add("rune_hood", ItemKind.Helmet).WithArmor(40).AppliesWhileEquipped(blessing);
    /// </code></example>
    /// <exception cref="ArgumentException">As <see cref="Add(string, string, string?, int?)"/>.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModItem Add(string name, ItemKind kind, string? icon = null, int? stackSize = null) =>
        ContentRegistry.Shared.AddItem(CallingMod.Of(Assembly.GetCallingAssembly()), name, ItemKinds.BaseOf(kind), icon, stackSize, kind);

    /// <summary>
    /// Gives the local player <paramref name="count"/> of a mod's item; how many went into the inventory (0 off the
    /// main thread, outside a world, or without room).
    /// </summary>
    public static int Give(ModItem item, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(item);
        return GameItems.Give(item.AssetName, count);
    }

    /// <summary>
    /// Gives the local player <paramref name="count"/> of an item by its asset name (a game item such as
    /// <c>Item_Wood_Sticks</c>, or a mod item's <see cref="ModItem.AssetName"/>); how many went into the inventory.
    /// </summary>
    public static int Give(string item, int count = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        return GameItems.Give(item, count);
    }

    /// <summary>Gives the local player <paramref name="count"/> of an item of <see cref="All"/>; how many went into the inventory.</summary>
    public static int Give(ItemDefinition item, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemCatalog.Shared.Give(item, count);
    }

    /// <summary>
    /// Every item of the game a player can carry, the mods' own included, in the game's order: no blueprints, no trees or
    /// stones of the world, no marks on the map. Empty before the game has its item database (it has one in the main menu
    /// already); one object per item. Main thread: another thread gets the list last read there.
    /// </summary>
    /// <example><code>
    /// foreach (var food in Items.All.Where(i => i.Category?.AssetName == "Categ_Resources_Food")) Log.Info(food.DisplayName);
    /// </code></example>
    public static IReadOnlyList<ItemDefinition> All => ItemCatalog.Shared.All;

    /// <summary>The top categories of <see cref="All"/> (Armor, Bags, Resources, Tools, Weapons, Decorations), each with the ones under it.</summary>
    public static IReadOnlyList<ItemCategory> Categories => ItemCatalog.Shared.Roots;

    /// <summary>An item of <see cref="All"/> by its asset name, in any case; null when there is none. Main thread.</summary>
    public static ItemDefinition? Find(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);
        return ItemCatalog.Shared.Find(assetName);
    }

    /// <summary>Whether items can be given now: the local player is in a world, with an inventory. Main thread.</summary>
    public static bool CanGive => ItemCatalog.Shared.CanGive;
}
