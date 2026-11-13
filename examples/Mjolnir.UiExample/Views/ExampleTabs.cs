using Mjolnir.SDK;
using Mjolnir.SDK.UI;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// The example's tabs in the game's own windows: a tab of the settings menu with the example's settings (the rows the
/// Mods window shows too, bound to the same entries), and a page of the journal with the camp's supplies. Built in
/// <c>OnLoad</c>, which may not make textures: the mod gives them their icons once the game runs. Their headers follow
/// the player's language.
/// </summary>
internal static class ExampleTabs
{
    public static UiTabItem Settings(ExampleSettings settings) => new UiTabItem
    {
        Name = "ExampleSettingsTab",
        Content = new UiStackPanel
        {
            Spacing = 15,
            Children =
            {
                SettingControls.For(settings.ShowHudLine),
                SettingControls.For(settings.HudText),
                SettingControls.For(settings.BannerSeconds),
                SettingControls.For(settings.WindowKey),
            },
        },
    }.WithBinding(UiTabItem.HeaderProperty, Localization.GetBindable("tab.settings"));

    public static UiTabItem Journal() => new UiTabItem
    {
        Name = "ExampleJournalTab",
        Content = new UiStackPanel
        {
            Spacing = 12,
            Children =
            {
                new UiTextBlock { TextWrapping = TextWrapping.Wrap }.WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("tab.journal_intro")),
                new UiGrid
                {
                    Name = "ExampleSupplies",
                    ColumnDefinitions = "*,120",
                    Children =
                    {
                        Cell("supply.wood"), Amount(24),
                        Cell("supply.stone"), Amount(12),
                        Cell("supply.berries"), Amount(40),
                        Cell("supply.flint"), Amount(3),
                    },
                },
            },
        },
    }.WithBinding(UiTabItem.HeaderProperty, Localization.GetBindable("tab.journal"));

    private static UiTextBlock Cell(string key) =>
        new UiTextBlock { TextAlignment = TextAlignment.Left }.WithBinding(UiTextBlock.TextProperty, Localization.GetBindable(key));

    private static UiTextBlock Amount(int amount) => new() { Text = amount.ToString(), TextAlignment = TextAlignment.Right };
}
