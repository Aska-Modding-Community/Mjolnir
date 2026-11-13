using Mjolnir.SDK;
using Mjolnir.SDK.Content;

[assembly: SdkModule(typeof(ContentModule))]

namespace Mjolnir.SDK.Content;

/// <summary>
/// The content module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). It connects the SDK's
/// <see cref="Localization"/> to the game (it patches the game's language loading, so the mods' strings are in the
/// game's dictionaries from the first language the game loads) and puts the mods' <see cref="Items"/>,
/// <see cref="Recipes"/> and <see cref="Buildings"/> into the game's item database. The console gets <c>give</c> and
/// <c>items</c>.
/// </summary>
internal sealed class ContentModule : ISdkModule
{
    public void Start()
    {
        GameLocalization.Start();
        GameItems.Start();
        ItemCatalog.Shared.Source = new GameCatalog();
        ContentCommands.Register();
    }
}
