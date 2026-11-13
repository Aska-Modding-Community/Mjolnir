using Mjolnir.SDK;
using Mjolnir.SDK.Config;

namespace Mjolnir.UiExample;

/// <summary>
/// The example's settings, Mjolnir/Config/mjolnir.uiexample.toml: bound once, in OnLoad, and handed to whatever shows
/// them. The keys name them in the file; the Mods window shows the display names, sections in binding order, and in
/// Russian the names and descriptions from lang/russian.toml ([config.HUD.ShowText] name, description…).
/// </summary>
internal sealed class ExampleSettings(ModConfig config)
{
    public ConfigEntry<bool> ShowHudLine { get; } =
        config.Bind("HUD", "ShowText", true, "Show the example's line in the HUD.").WithDisplayName("Show the HUD line");

    public ConfigEntry<string> HudText { get; } =
        config.Bind("HUD", "Text", "UI Example is running", "What the HUD line says.").WithDisplayName("HUD text");

    public ConfigEntry<float> BannerSeconds { get; } =
        config.Bind("Banner", "Seconds", 3f, "How long the welcome banner stays.").WithRange(1f, 10f).WithDisplayName("Banner seconds");

    // A key the player can rebind in the Mods window (a UiHotKeyBox row): Esc cancels, Backspace clears it.
    public ConfigEntry<KeyGesture> WindowKey { get; } =
        config.Bind("Keys", "Window", KeyGesture.Parse("F7"), "Opens and closes the example's window.").WithDisplayName("Window key");
}
