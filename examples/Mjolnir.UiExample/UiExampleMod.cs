using Mjolnir.SDK;
using Mjolnir.SDK.UI;
using Mjolnir.UiExample.Views;
using UnityEngine;

namespace Mjolnir.UiExample;

/// <summary>
/// Example mod: the game UI through Mjolnir.SDK.UI, built in code, without a single game type or
/// Harmony patch of its own. The mod class only wires the parts together:
/// <list type="bullet">
/// <item><see cref="ExampleSettings"/>: three config entries; the SDK shows them in the game's Mods window (main or pause
/// menu → Mods → UI Example → Settings), and the example's own window binds to the same entries;</item>
/// <item><see cref="ExampleHud"/>: a HUD column with the configured line (hidden when switched off) and a clock, and a
/// HUD marker over a point in the world;</item>
/// <item><see cref="ExampleMenu"/>: «UI Example» in the main and pause menus, a level of its own two deep, opening
/// <see cref="ExampleWindow"/> (every element, its state in <see cref="ViewModels.ExampleViewModel"/>) and
/// <see cref="TabsWindow"/> (tabs, a list with selection, tooltips); a hotkey (F7, rebound in the Mods window) opens the
/// first too;</item>
/// <item><see cref="ExampleTabs"/>: a tab of the example's own in the game's settings menu (its settings) and in the
/// journal (a page of the camp's supplies), after the game's tabs.</item>
/// </list>
/// Next to the .dll: <c>icon.png</c>, the mod's picture in the Mods window, found by its name with no setting needed; and
/// <c>lang/english.toml</c>, <c>lang/russian.toml</c>, the texts the SDK's Localization shows in the player's language.
/// </summary>
[ModManifest(
    "mjolnir.uiexample",
    "UI Example",
    "2.0.0",
    Description = "Every element of Mjolnir.SDK.UI, built in code.",
    Author = "Mjolnir")]
public sealed class UiExampleMod : Mod
{
    private ExampleSettings _settings = null!;
    private ExampleWindow? _window;
    private TabsWindow? _tabs;
    private UiTabItem _settingsTab = null!;
    private UiTabItem _journalTab = null!;

    public override void OnLoad()
    {
        _settings = new ExampleSettings(Config);

        // HUD: bound texts follow the config and the clock by themselves; nothing to update by hand.
        Hud.Add(new ExampleHud(_settings), HudAnchor.TopLeft);
        Hud.Ready += (s, e) => Notify.Banner("UI Example", Localization.Get("banner.ready"), _settings.BannerSeconds.Value);

        // A HUD element over a point in the world: 6 m ahead of the camera when the world's HUD came up. It follows the
        // point as the camera moves and hides while the point is behind it or off the screen.
        Vector3? start = null;
        Hud.Ready += (s, e) => start = Camera.main is { } camera ? camera.transform.position + camera.transform.forward * 6 : null;
        var marker = new UiTextBlock { Name = "ExampleMarker", FontSize = 18 }
            .WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("marker"))
            .WithBinding(Control.IsVisibleProperty, () => start != null);
        Hud.AddAt(marker, () => start ?? default);

        // Menus: the windows make textures, which need the running game, so they are built on first use, not here.
        Menus.Main.Add(ExampleMenu.Create(ShowWindow, ShowTabs));
        Menus.Pause.Add(ExampleMenu.Create(ShowWindow, ShowTabs));

        // Tabs of the example's own in the game's settings menu and journal: the game makes those windows anew each time
        // they open, and the SDK adds the tabs to each.
        _settingsTab = Menus.Settings.Add(ExampleTabs.Settings(_settings));
        _journalTab = Menus.Journal.Add(ExampleTabs.Journal());

        // A hotkey from a setting: rebinding it in the Mods window takes effect at once.
        HotKeys.Add(_settings.WindowKey, () =>
        {
            var window = _window ??= new ExampleWindow(_settings, Log);
            if (window.IsOpen) window.Close();
            else window.Show();
        });
    }

    // Textures need the running game: the tabs get their icons now (a change of icon reaches a tab already shown).
    public override void OnGameReady()
    {
        _settingsTab.Icon = Icons.Grid;
        _journalTab.Icon = Icons.Diamond;
    }

    private void ShowWindow() => (_window ??= new ExampleWindow(_settings, Log)).Show();

    private void ShowTabs() => (_tabs ??= new TabsWindow()).Show();
}
