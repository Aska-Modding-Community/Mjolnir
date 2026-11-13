using System;
using System.Threading;
using Mjolnir.SDK.Config;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The settings behind <see cref="InterfaceScale"/> and the scales applied now (no Unity: unit-tested): the platform's
/// [UI] Menus and [UI] Hud in Mjolnir/Config/mjolnir.toml, in percent. <see cref="ScaleService"/> applies them to the game.
/// A change is taken once the player lets go of the mouse button: the settings' sliders sit in a window the change resizes.
/// The game's own menus never grow past their size: they are laid out to fill a 16:9 screen, and the settings menu and the
/// inventory are cut already at 125 % (UiLab "scale"). Mod windows keep inside the screen at any scale.
/// </summary>
internal static class UiScale
{
    public const int MinPercent = 75;
    public const int MaxPercent = 150;
    public const float ReferenceWidth = 1920;
    public const float ReferenceHeight = 1080;

    private static ConfigEntry<int>? s_menus;
    private static ConfigEntry<int>? s_hud;

    /// <summary>The game's menus and dialogs and every mod window, in percent (the UI module's start binds it).</summary>
    public static ConfigEntry<int> MenusEntry => LazyInitializer.EnsureInitialized(ref s_menus, () =>
        PlatformConfig.Config.Bind("UI", "Menus", 100, "The size of mod windows and of the game's menus and dialogs, in percent. The game's menus grow no bigger than 100 %.")
                      .WithRange(MinPercent, MaxPercent));

    /// <summary>The HUD on top of the game's own HUD scale, in percent (the UI module's start binds it).</summary>
    public static ConfigEntry<int> HudEntry => LazyInitializer.EnsureInitialized(ref s_hud, () =>
        PlatformConfig.Config.Bind("UI", "Hud", 100, "The size of the HUD and its notifications on top of the game's own HUD scale, in percent.")
                      .WithRange(MinPercent, MaxPercent));

    /// <summary>The menus' scale applied now (1 = the game's own).</summary>
    public static float Menus { get; private set; } = 1;

    /// <summary>The scale of a menu: a mod window takes the menus' scale, a game menu at most 1 (it would be cut).</summary>
    public static float MenuScale(bool modWindow) => modWindow ? Menus : Math.Min(Menus, 1f);

    /// <summary>The HUD's factor applied now, on top of the game's own HUD scale.</summary>
    public static float Hud { get; private set; } = 1;

    /// <summary>A percent as a scale, kept within the settings' range.</summary>
    public static float ScaleOf(int percent) => Math.Clamp(percent, MinPercent, MaxPercent) / 100f;

    /// <summary>
    /// Once a frame: takes changed settings once the pointer is up. Says which scale changed, so only what it touches is
    /// laid out again.
    /// </summary>
    public static (bool Menus, bool Hud) Update(int menusPercent, int hudPercent, bool pointerHeld)
    {
        if (pointerHeld) return (false, false);
        float menus = ScaleOf(menusPercent), hud = ScaleOf(hudPercent);
        bool menusChanged = Math.Abs(menus - Menus) > 0.0001f, hudChanged = Math.Abs(hud - Hud) > 0.0001f;
        Menus = menus;
        Hud = hud;
        return (menusChanged, hudChanged);
    }

    /// <summary>Back to 100 % (tests).</summary>
    internal static void ResetForTests() => (Menus, Hud) = (1, 1);
}
