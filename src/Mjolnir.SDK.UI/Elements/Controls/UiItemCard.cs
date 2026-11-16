namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's own card of an item, as the inventory shows under the pointer: the picture, the name, the kind, the tier
/// and the item's attributes, in the game's look. The item is named by <see cref="AssetName"/>, and the card fills
/// itself from the game's database — the item need not be anywhere in the world.
/// <para>As a hover card over a row or a slot: <c>row.ToolTip = new UiItemCard { AssetName = item.AssetName };</c>,
/// where the name is an <c>ItemDefinition.AssetName</c>, a <c>ModItem.AssetName</c> or a game name such as
/// <c>Item_Wood_Sticks</c>. A tooltip that is a card is shown as it is, without the hover card's own frame around it.</para>
/// <para>What the card leaves out: the durability of a particular item, which belongs to one in a player's hands, and
/// the inventory's key hints (use, drop, quick bar), which mean nothing outside it.</para>
/// </summary>
public sealed class UiItemCard : Control
{
    public static readonly MjolnirProperty<string?> AssetNameProperty =
        MjolnirProperty.Register<UiItemCard, string?>(nameof(AssetName), null, DirtyFlags.Value);

    /// <summary>
    /// The game's name of the item (<c>Item_Wood_Sticks</c>, a mod's <c>Item_&lt;mod&gt;_&lt;name&gt;</c>); null or a
    /// name the game's database does not have shows an empty card, and the name is written to the log once.
    /// </summary>
    public string? AssetName
    {
        get => GetValue(AssetNameProperty);
        set => SetValue(AssetNameProperty, value);
    }

    public override string ToString() => $"UiItemCard '{GetStored(AssetNameProperty)}'";
}
