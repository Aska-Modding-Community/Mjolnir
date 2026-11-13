using System;

namespace Mjolnir.SDK.Content;

/// <summary>
/// What kind of thing an item is (<see cref="Items.Add(string, ItemKind, string?, int?)"/>): where it goes, how it is carried,
/// held or worn, and what it does. Each kind stands on a plain item of the game of that kind, whose picture, look,
/// sounds and numbers the item has until the mod gives its own; for an
/// item like one particular game item (an iron axe, a longbow) give that item as <c>basedOn</c> instead.
/// </summary>
public enum ItemKind
{
    /// <summary>A small material for crafting and building, many to a slot (the game's rope).</summary>
    Material,

    /// <summary>A long or heavy material carried on the back, one at a time (the game's plank).</summary>
    LargeMaterial,

    /// <summary>Food, eaten from the inventory (the game's berries); see <see cref="ModItem.AppliesWhenEaten(string, TimeSpan)"/>.</summary>
    Food,

    /// <summary>A one-handed weapon, held in the right hand with a shield beside it (the game's iron sword).</summary>
    OneHandedWeapon,

    /// <summary>A two-handed weapon (the game's two-handed iron sword).</summary>
    TwoHandedWeapon,

    /// <summary>A shield, held in the left hand (the game's crude round shield).</summary>
    Shield,

    /// <summary>A bow, held in both hands and shooting arrows (the game's simple bow).</summary>
    Bow,

    /// <summary>Arrows, worn in the quiver (the game's wooden arrows).</summary>
    Arrow,

    /// <summary>A helmet or a hood (the game's cloth hood).</summary>
    Helmet,

    /// <summary>A shirt or body armour (the game's cloth shirt).</summary>
    Chest,

    /// <summary>Trousers (the game's cloth trousers).</summary>
    Pants,

    /// <summary>Boots (the game's cloth boots).</summary>
    Boots,

    /// <summary>Gloves (the game's cloth gloves).</summary>
    Gloves,

    /// <summary>A cape (the game's cloth cape).</summary>
    Cape,
}

/// <summary>The game items the kinds stand on, and what each kind can be given. Pure: unit-tested.</summary>
internal static class ItemKinds
{
    public static string BaseOf(ItemKind kind) => kind switch
    {
        ItemKind.Material => "Item_Materials_BasicRope",
        ItemKind.LargeMaterial => "Item_Wood_Plank",
        ItemKind.Food => "Item_Food_Berries",
        ItemKind.OneHandedWeapon => "Item_OneHanded_1HIronSword",
        ItemKind.TwoHandedWeapon => "Item_TwoHanded_2HIronSword",
        ItemKind.Shield => "Item_Shields_CrudeRoundShield",
        ItemKind.Bow => "Item_Bows_SimpleBow",
        ItemKind.Arrow => "Item_Arrows_WoodArrows",
        ItemKind.Helmet => "Item_Helmets_Cloth0",
        ItemKind.Chest => "Item_Chest_Cloth0",
        ItemKind.Pants => "Item_Pants_Cloth0",
        ItemKind.Boots => "Item_Boots_Cloth0",
        ItemKind.Gloves => "Item_Gloves_Cloth0",
        ItemKind.Cape => "Item_Cape_Cloth1",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an item kind"),
    };

    /// <summary>Weapons: they deal damage (a shield's bash and an arrow's shot too).</summary>
    public static bool IsWeapon(ItemKind kind) => kind is ItemKind.OneHandedWeapon or ItemKind.TwoHandedWeapon or ItemKind.Shield or ItemKind.Bow or ItemKind.Arrow;

    /// <summary>Weapons that strike with the item itself, so its blow can carry an effect.</summary>
    public static bool Strikes(ItemKind kind) => kind is ItemKind.OneHandedWeapon or ItemKind.TwoHandedWeapon or ItemKind.Shield;

    /// <summary>What is held or worn.</summary>
    public static bool IsEquipment(ItemKind kind) => IsWeapon(kind) || IsWorn(kind);

    /// <summary>What is worn.</summary>
    public static bool IsWorn(ItemKind kind) => kind is ItemKind.Helmet or ItemKind.Chest or ItemKind.Pants or ItemKind.Boots or ItemKind.Gloves or ItemKind.Cape;
}
