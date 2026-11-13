using System;
using Mjolnir.SDK;
using Mjolnir.SDK.UI;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// The example's item in the game's main and pause menus, two levels deep, as the game's «Singleplayer» opens «New game /
/// Load game / Back»: «UI Example» opens a level of its own, whose «More» opens another. Each level ends with the game's
/// «Back», and Esc goes one level up too. The texts follow the player's language (bound to Localization.GetBindable). Each menu
/// gets an item of its own.
/// </summary>
internal static class ExampleMenu
{
    public static UiMenuItem Create(Action showWindow, Action showTabs) => new UiMenuItem
    {
        Header = "UI Example",
        Items =
        {
            Item("menu.window").OnClick(showWindow),
            Item("menu.tabs").OnClick(showTabs),
            new UiMenuItem
            {
                Items =
                {
                    Item("menu.hello").OnClick(() => Notify.Message(Localization.Get("message.hello"))),
                    Item("menu.ask").OnClick(async () => Notify.Message(
                        Localization.Get(await Dialog.ConfirmAsync("UI Example", Localization.Get("message.question")) ? "common.yes" : "common.no"))),
                },
            }.WithBinding(UiMenuItem.HeaderProperty, Localization.GetBindable("menu.more")),
        },
    };

    private static UiMenuItem Item(string key) => new UiMenuItem().WithBinding(UiMenuItem.HeaderProperty, Localization.GetBindable(key));
}
