using Mjolnir.SDK;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.UI;

namespace Mjolnir.CheatInventory.Views;

/// <summary>
/// The item window: the game's top item categories listed on the left, as its journal lists its own, the first tab every
/// item; each tab a <see cref="CatalogPage"/>. It reads the game's item database, which the game has from the main menu on,
/// so the mod builds it on first use.
/// </summary>
internal sealed class CatalogWindow : UiWindow
{
    public CatalogWindow(InventorySettings settings)
    {
        Title = Localization.Get("window.title");
        Width = 1240;
        Height = 980;
        var tabs = new UiTabControl
        {
            Name = "CatalogCategories",
            TabStripPlacement = Dock.Left,
            FontSize = 16,
            Items = { new UiTabItem { Header = Localization.Get("category.all"), Content = new CatalogPage(null, settings) } },
        };
        foreach (var category in Items.Categories)
            tabs.Items.Add(new UiTabItem { Header = category.DisplayName, Content = new CatalogPage(category, settings) });
        Content = tabs;
    }
}
