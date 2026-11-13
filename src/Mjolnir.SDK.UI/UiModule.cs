using Mjolnir.SDK;
using Mjolnir.SDK.UI;

[assembly: SdkModule(typeof(UiModule))]

namespace Mjolnir.SDK.UI;

/// <summary>
/// The UI module's entry: Mjolnir.SDK starts it when the first mod attaches, or with no mod loaded when Core starts the SDK
/// (phase A). It adds the SDK's Mods window to the main and pause menus and the console window's key; the rest of the UI
/// facade installs itself on first use.
/// </summary>
internal sealed class UiModule : ISdkModule
{
    public void Start()
    {
        // The platform's interface and HUD scales: bound now, so its page lists them and the file gets them.
        _ = UiScale.MenusEntry;
        _ = UiScale.HudEntry;
        ModsWindow.EnsureInstalled();
        ConsoleWindow.Install();
    }
}
