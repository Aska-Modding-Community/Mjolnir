using System;
using System.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// How big the game draws its interface, one standard for the player and for mods: <see cref="Menus"/> for every mod
/// window (the Mjolnir window too) and the game's menus and dialogs, <see cref="Hud"/> on top of the game's own HUD scale
/// for the HUD, its notifications and what mods add to it (<see cref="UI.Hud.Add"/>). 1 is the game's own size.
/// <para>Both are the player's settings, on the page behind the Mjolnir window's Settings button, saved in Mjolnir/Config/mjolnir.toml:
/// a mod changes them only when its player asks. A change applies once the mouse button is up (the settings' sliders
/// sit in a window the change resizes); values outside <see cref="Minimum"/>–<see cref="Maximum"/> are kept within.</para>
/// <para>Mod windows need nothing for it: a <see cref="UiWindow"/> is laid out like the game's menus, for 1920×1080, and
/// scales with them. Read the values to size things of your own, or bind them:
/// <c>text.Bind(UiTextBlock.TextProperty, InterfaceScale.Menus.Map(s => $"{s:P0}", t => 1d))</c>.</para>
/// </summary>
public static class InterfaceScale
{
    /// <summary>The smallest scale (75 %).</summary>
    public const double Minimum = UiScale.MinPercent / 100.0;

    /// <summary>The largest scale (150 %).</summary>
    public const double Maximum = UiScale.MaxPercent / 100.0;

    private static IBindable<double>? s_menus;
    private static IBindable<double>? s_hud;

    /// <summary>
    /// Every mod window, and the game's menus and dialogs: 1 for the game's own size. Mod windows follow it over the whole
    /// range and stay inside the screen. The game's menus follow it down but grow no bigger than their own size: they are
    /// laid out to fill the screen, and its settings menu and inventory would be cut already at 1.25.
    /// </summary>
    public static IBindable<double> Menus => LazyInitializer.EnsureInitialized(ref s_menus, () => Factor(UiScale.MenusEntry));

    /// <summary>
    /// The HUD, its notifications and what mods add to the HUD, on top of the game's own HUD scale (its settings' slider,
    /// which keeps working): 1 leaves the game's.
    /// </summary>
    public static IBindable<double> Hud => LazyInitializer.EnsureInitialized(ref s_hud, () => Factor(UiScale.HudEntry));

    // The setting is a percent (its slider shows 75…150); the API speaks factors.
    private static IBindable<double> Factor(IBindable<int> percent) =>
        percent.Map(p => p / 100.0, factor => (int)Math.Round(Math.Clamp(double.IsNaN(factor) ? 1 : factor, Minimum, Maximum) * 100));
}
