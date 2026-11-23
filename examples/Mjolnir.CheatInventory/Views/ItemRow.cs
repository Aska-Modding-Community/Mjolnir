using System;
using System.Globalization;
using Mjolnir.SDK;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.UI;

namespace Mjolnir.CheatInventory.Views;

/// <summary>
/// One item: the game's row with the item's picture, its name and its category, a field for how many (a full stack to
/// start with), and a button that gives them to the player, answered by the game's own notification with the item's
/// picture. The item's text shows in the game's hover card.
/// </summary>
internal sealed class ItemRow : UiUserControl
{
    private const int MostAtOnce = 9999;

    public ItemRow(ItemDefinition item)
    {
        Name = $"Item {item.AssetName}";
        var count = new UiTextBox
        {
            Name = "Count",
            Header = item.DisplayName,
            // The stack only where items stack: "up to 1 in a stack" says nothing.
            Description = item.StackSize > 1
                ? Localization.Get("row.details", item.Category?.DisplayName ?? "", item.StackSize)
                : item.Category?.DisplayName ?? "",
            Icon = item.Icon,
            // A full stack to start with: that is what a cheat window is asked for, and the field says how many fit.
            Text = Math.Clamp(item.StackSize, 1, MostAtOnce).ToString(CultureInfo.InvariantCulture),
            ContentType = TextInputContentType.Digits,
            MaxLength = 4,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            ContentWidth = 120,
            // The game's own card of the item, shown for its picture as the inventory shows it for a slot.
            ToolTip = new UiItemCard { AssetName = item.AssetName },
            ToolTipOnIcon = true,
        };
        var give = new UiButton { Name = "Give", Content = Localization.Get("row.give"), Margin = new Thickness(0, 10, 0, 0) }
            .WithBinding(Control.IsEnabledProperty, () => Items.CanGive, TimeSpan.FromSeconds(0.5));
        give.Click += (_, _) => Give(item, count);
        Content = new UiGrid { ColumnDefinitions = "*,200", ColumnSpacing = 12, Children = { count, give } };
    }

    private static void Give(ItemDefinition item, UiTextBox count)
    {
        int asked = int.TryParse(count.Text, out int n) ? Math.Clamp(n, 1, MostAtOnce) : 1;
        count.Text = asked.ToString(CultureInfo.InvariantCulture);
        int given = Items.Give(item, asked);
        string text = given == asked ? Localization.Get("given.all", given, item.DisplayName)
                    : given > 0 ? Localization.Get("given.some", given, item.DisplayName, asked - given)
                    : Localization.Get("given.none", item.DisplayName);
        Notify.Message(text, item.Icon);
    }
}
