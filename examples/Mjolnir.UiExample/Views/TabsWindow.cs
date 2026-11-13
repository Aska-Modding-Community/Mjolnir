using System.Collections.ObjectModel;
using Mjolnir.SDK;
using Mjolnir.SDK.UI;
using Mjolnir.UiExample.ViewModels;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// A window laid out like the game's journal: icon tabs on top (Q and E switch them, a tab's name shows when the pointer
/// rests on it), a list whose selected row the details beside it follow, categories listed on the left, tooltips, and the
/// game's slots and cards in grids. Its texts are in the player's language (lang/*.toml). It makes textures (the tab
/// icons), so the mod builds it on first use.
/// </summary>
internal sealed class TabsWindow : UiWindow
{
    private readonly ObservableCollection<Supply> _supplies;

    public TabsWindow()
    {
        Title = Localization.Get("tabs.title");
        Width = 1000;
        Height = 640;
        _supplies =
        [
            Supply("wood", 24),
            Supply("stone", 12),
            Supply("berries", 40),
            Supply("flint", 3),
        ];
        Content = new UiTabControl
        {
            Name = "ExampleTabs",
            Items =
            {
                new UiTabItem { Header = Localization.Get("tabs.supplies"), Icon = Icons.Diamond, Content = SuppliesTab() },
                new UiTabItem { Header = Localization.Get("tabs.categories"), Icon = Icons.Circle, Content = CategoriesTab() },
                new UiTabItem { Header = Localization.Get("tabs.tips"), Icon = Icons.Cross, Content = ToolTipsTab() },
                new UiTabItem { Header = Localization.Get("tabs.tiles"), Icon = Icons.Grid, Content = TilesTab() },
            },
        };
    }

    private static Supply Supply(string key, int amount) => new(Localization.Get($"supply.{key}"), amount, Localization.Get($"supply.{key}_text"));

    // A list with selection beside the selected row's details, which follow it through the list's SelectedItem.
    private Control SuppliesTab()
    {
        var list = new UiListBox
        {
            Name = "ExampleList",
            FontSize = 18,
            ItemsSource = _supplies,
            ItemTemplate = new FuncDataTemplate<Supply>(s => new UiGrid
            {
                ColumnDefinitions = "*,Auto",
                Children =
                {
                    new UiTextBlock { Text = s.Name, TextAlignment = TextAlignment.Left },
                    new UiTextBlock { Text = $"× {s.Amount}", TextAlignment = TextAlignment.Right },
                },
            }),
            SelectedIndex = 0,
        };
        var selected = list.GetObservable(UiListBox.SelectedItemProperty);
        var details = new UiStackPanel
        {
            Name = "ExampleDetails",
            Spacing = 12,
            Children =
            {
                new UiSectionHeader { Name = "ExampleSupplyName" }.WithBinding(UiSectionHeader.TextProperty, selected, s => (s as Supply)?.Name ?? ""),
                new UiTextBlock { Name = "ExampleSupplyText", FontSize = 18 }.WithBinding(UiTextBlock.TextProperty, selected, s => (s as Supply)?.Text ?? ""),
                new UiProgressBar { Name = "ExampleSupplyStock", Maximum = 50, ShowProgressText = true, ProgressTextFormat = "{0:0}/{3:0}" }
                    .WithBinding(UiProgressBar.ValueProperty, selected, s => (double)((s as Supply)?.Amount ?? 0)),
                // The list follows the collection; selecting by code moves the details too.
                new UiButton { Name = "AddSupply", Width = 260, FontSize = 18 }.WithBinding(UiButton.ContentProperty, Localization.GetBindable("tabs.add")).OnClick(() =>
                {
                    var found = new Supply(Localization.Get("supply.found", _supplies.Count + 1), 1, Localization.Get("supply.found_text"));
                    _supplies.Add(found);
                    list.SelectedItem = found;
                }),
            },
        };
        return new UiGrid { ColumnDefinitions = "380,*", ColumnSpacing = 32, Children = { list, details } };
    }

    // Tabs listed on the left, as the journal's categories: a picture and a name, amber while selected. Q and E stay the
    // outer tabs'. Their names take the tab control's FontSize.
    private static Control CategoriesTab() => new UiTabControl
    {
        Name = "ExampleCategories",
        TabStripPlacement = Dock.Left,
        FontSize = 16, // the tabs' names (the game's are 12)
        Items =
        {
            Category("tools", Icons.Diamond),
            Category("weapons", Icons.Cross),
            Category("food", null), // without a picture, its name starts where the picture would be
        },
    };

    private static UiTabItem Category(string key, Bitmap? icon) => new()
    {
        Header = Localization.Get($"category.{key}"),
        Icon = icon,
        Content = new UiTextBlock { FontSize = 18, Text = Localization.Get($"category.{key}_text") },
    };

    // The supplies again, as the game's slots in a grid: an items control whose panel is a wrap panel, over the list's own
    // collection (a find added on the first tab shows here too); and cards. A click selects one of each.
    private Control TilesTab()
    {
        UiSlot? pickedSlot = null;
        UiCard? pickedCard = null;
        var picked = new UiTextBlock { Name = "ExampleTilesPicked", FontSize = 18 }.WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("tiles.hint"));
        var slots = new UiItemsControl
        {
            Name = "ExampleSlots",
            ItemsSource = _supplies,
            ItemsPanel = new FuncTemplate<Panel>(() => new UiWrapPanel { ItemsAlignment = WrapPanelItemsAlignment.Center }),
            ItemTemplate = new FuncDataTemplate<Supply>(supply =>
            {
                var slot = new UiSlot { Icon = PictureOf(supply), Count = supply.Amount, FillFraction = supply.Amount / 50.0, ToolTip = $"{supply.Name}\n{supply.Text}" };
                return slot.OnClick(() =>
                {
                    if (pickedSlot != null) pickedSlot.IsSelected = false;
                    (pickedSlot = slot).IsSelected = true;
                    picked.Text = Localization.Get("tiles.picked", supply.Name);
                });
            }),
        };

        UiCard Card(string key, Bitmap? picture)
        {
            var card = new UiCard
            {
                Name = $"ExampleCard_{key}", Width = 150, Image = picture,
                Title = Localization.Get($"card.{key}"), Subtitle = Localization.Get($"card.{key}_text"),
            };
            return card.OnClick(() =>
            {
                if (pickedCard != null) pickedCard.IsSelected = false;
                (pickedCard = card).IsSelected = true;
                picked.Text = Localization.Get("tiles.picked", card.Title);
            });
        }

        var cards = new UiWrapPanel
        {
            Name = "ExampleCards", ItemsAlignment = WrapPanelItemsAlignment.Center, ItemSpacing = 16,
            Children = { Card("smith", Icons.Diamond), Card("hunter", Icons.Cross), Card("farmer", Icons.Circle) },
        };
        return new UiStackPanel { Spacing = 16, Children = { picked, slots, cards } };
    }

    private static Bitmap? PictureOf(Supply supply) => (supply.Name.Length % 3) switch
    {
        0 => Icons.Diamond,
        1 => Icons.Circle,
        _ => Icons.Cross,
    };

    // Tooltips: a string's first line is the card's amber title and the rest its text; an element shows itself in the card.
    private static Control ToolTipsTab()
    {
        var plain = new UiButton { Name = "ExampleToolTip", Width = 320, FontSize = 18, ToolTip = Localization.Get("tips.wood") }
            .WithBinding(UiButton.ContentProperty, Localization.GetBindable("tips.button"));
        var row = new UiToggleSwitch { Name = "ExampleToolTipRow", IsChecked = true, ToolTip = Localization.Get("tips.row_tip") }
            .WithBinding(HeaderedControl.HeaderProperty, Localization.GetBindable("tips.row"));
        var rich = new UiButton
        {
            Name = "ExampleRichToolTip",
            FontSize = 18,
            Width = 320,
            ToolTip = new UiStackPanel
            {
                Spacing = 6,
                Children =
                {
                    new UiTextBlock { FontSize = 14, Text = Localization.Get("tips.village") },
                    new UiProgressBar { Maximum = 1, Value = 0.7, ShowProgressText = true },
                },
            },
        }.WithBinding(UiButton.ContentProperty, Localization.GetBindable("tips.rich"));
        var hint = new UiTextBlock { FontSize = 18, Opacity = 0.7 }.WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("tips.hint"));
        return new UiStackPanel { Spacing = 16, Children = { hint, plain, row, rich } };
    }
}
