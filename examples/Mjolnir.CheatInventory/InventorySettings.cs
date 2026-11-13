using Mjolnir.SDK;
using Mjolnir.SDK.Config;

namespace Mjolnir.CheatInventory;

/// <summary>
/// The example's settings, Mjolnir/Config/mjolnir.cheatinventory.toml; the Mods window shows them too, in Russian with the
/// names and descriptions of lang/russian.toml.
/// </summary>
internal sealed class InventorySettings(ModConfig config)
{
    public ConfigEntry<KeyGesture> WindowKey { get; } =
        config.Bind("Keys", "Window", KeyGesture.Parse("F6"), "Opens and closes the item window.").WithDisplayName("Window key");

    public ConfigEntry<int> PageSize { get; } =
        config.Bind("Window", "PageSize", 12, "How many items a page of the window shows.").WithRange(6, 30).WithDisplayName("Items per page");
}
