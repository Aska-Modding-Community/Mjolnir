using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.UI;

namespace Mjolnir.CheatInventory.Views;

/// <summary>
/// The items of one top category (or of every one): a search that filters as the player types, the category under it to
/// narrow them to, the number found and pages, and an <see cref="ItemRow"/> per item of the page, sorted by name.
/// </summary>
internal sealed class CatalogPage : UiUserControl
{
    private readonly ItemCategory? _root;
    private readonly InventorySettings _settings;
    private readonly UiTextBox _search;
    private readonly UiComboBox _category;
    private readonly UiTextBlock _found = new() { Name = "Found", FontSize = 18, TextAlignment = TextAlignment.Left };
    private readonly UiTextBlock _pageText = new() { Name = "PageNumber", FontSize = 18, TextAlignment = TextAlignment.Center };
    private readonly UiButton _previous = new() { Name = "PreviousPage", Content = Localization.Get("page.previous") };
    private readonly UiButton _next = new() { Name = "NextPage", Content = Localization.Get("page.next") };
    private readonly UiItemsControl _rows;
    private List<ItemDefinition> _matches = [];
    private int _page;

    public CatalogPage(ItemCategory? root, InventorySettings settings)
    {
        _root = root;
        _settings = settings;
        Name = $"Page {root?.AssetName ?? "All"}";

        _search = new UiTextBox
        {
            Name = "Search",
            Header = Localization.Get("search.header"),
            PlaceholderText = Localization.Get("search.placeholder"),
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            TextAlignment = TextAlignment.Left,
            ContentWidth = 420,
        };
        _search.TextChanged += (_, _) => Filter();

        // Narrower: one of the categories under this one (under every top one on the first tab).
        var choices = new List<Choice> { new(root, Localization.Get(root == null ? "category.every" : "category.whole", root?.DisplayName ?? "")) };
        choices.AddRange((root?.Children ?? Items.Categories).Select(c => new Choice(c, c.DisplayName)));
        _category = new UiComboBox
        {
            Name = "Category",
            Header = Localization.Get("category.header"),
            ItemsSource = choices,
            ItemText = c => ((Choice)c!).Text,
            SelectedIndex = 0,
            ContentWidth = 420,
        };
        _category.SelectionChanged += (_, _) => Filter();

        _previous.Click += (_, _) => ShowPage(_page - 1);
        _next.Click += (_, _) => ShowPage(_page + 1);
        _rows = new UiItemsControl
        {
            Name = "Rows",
            ItemTemplate = new FuncDataTemplate<ItemDefinition>(item => new ItemRow(item)),
        };

        Content = new UiStackPanel
        {
            Spacing = 8,
            Children =
            {
                _search,
                _category,
                new UiGrid { ColumnDefinitions = "*,160,120,160", ColumnSpacing = 8, Children = { _found, _previous, _pageText, _next } },
                // Items are given only to a player in a world; in the main menu the window shows what there is.
                new UiTextBlock { Name = "WorldOnly", FontSize = 16, TextAlignment = TextAlignment.Left, Text = Localization.Get("world.only") }
                    .WithBinding(Control.IsVisibleProperty, () => !Items.CanGive, TimeSpan.FromSeconds(0.5)),
                _rows,
            },
        };
        Filter();
    }

    // The items of the category chosen whose name has the words, sorted by name; the first page of them.
    private void Filter()
    {
        var category = (_category.SelectedItem as Choice)?.Category ?? _root;
        string query = _search.Text.Trim();
        var culture = Localization.Culture;
        _matches = Items.All
            .Where(item => category == null || category.Contains(item.Category))
            .Where(item => query.Length == 0 ||
                           culture.CompareInfo.IndexOf(item.DisplayName, query, CompareOptions.IgnoreCase) >= 0 ||
                           item.AssetName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.DisplayName, StringComparer.Create(culture, ignoreCase: true))
            .ToList();
        ShowPage(0);
    }

    private void ShowPage(int page)
    {
        int size = Math.Max(1, _settings.PageSize.Value), pages = Math.Max(1, (_matches.Count + size - 1) / size);
        _page = Math.Clamp(page, 0, pages - 1);
        _rows.ItemsSource = _matches.Skip(_page * size).Take(size).ToList();
        _found.Text = Localization.Get("found", _matches.Count);
        _pageText.Text = Localization.Get("page.number", _page + 1, pages);
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = _page < pages - 1;
    }

    /// <summary>A line of the category list: the category, or null for every one, and its text.</summary>
    private sealed record Choice(ItemCategory? Category, string Text);
}
