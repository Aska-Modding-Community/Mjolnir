using Mjolnir.CheatInventory.Views;
using Mjolnir.SDK;
using Mjolnir.SDK.UI;

namespace Mjolnir.CheatInventory;

/// <summary>
/// Example mod: every item of the game in one window, given to the player in the amount asked for, through
/// Mjolnir.SDK.Content's catalog (<c>Items.All</c>, <c>Items.Give</c>) and a window of Mjolnir.SDK.UI, without a single game
/// type or Harmony patch of its own.
/// <list type="bullet">
/// <item><see cref="CatalogWindow"/>: the game's item categories listed on the left, as its journal lists its own; on each
/// page a search that filters as the player types, the category under it, pages, and a row per item with its picture, its
/// name, a field for how many and a button that gives them;</item>
/// <item>the window opens from the pause menu and with a key (F6, rebound in the Mods window).</item>
/// </list>
/// Next to the .dll: <c>lang/english.toml</c> and <c>lang/russian.toml</c>, its texts in the player's language.
/// </summary>
[ModManifest(
    "mjolnir.cheatinventory",
    "Cheat Inventory",
    "1.0.0",
    Description = "Every item of the game in one window, given in the amount you ask for.",
    Author = "Mjolnir",
    Network = NetworkCompatibility.ClientOnly)]
public sealed class CheatInventoryMod : Mod
{
    private InventorySettings _settings = null!;
    private CatalogWindow? _window;

    public override void OnLoad()
    {
        _settings = new InventorySettings(Config);
        // The window reads the game's item database and makes the pictures' sprites, so it is built on first use, not here.
        Menus.Pause.Add(new UiButton().WithBinding(UiButton.ContentProperty, Localization.GetBindable("menu.open")).OnClick(Show));
        
        HotKeys.Add(_settings.WindowKey, () =>
        {
            if (_window is { IsOpen: true } window) window.Close();
            else Show();
        });
    }

    private void Show() => (_window ??= new CatalogWindow(_settings)).Show();
}
