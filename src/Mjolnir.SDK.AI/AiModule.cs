using Mjolnir.SDK;
using Mjolnir.SDK.AI;

[assembly: SdkModule(typeof(AiModule))]

namespace Mjolnir.SDK.AI;

/// <summary>
/// The villager module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). It follows the villagers the
/// game spawns and removes and answers its job quests' priority (three game methods are patched), and once the game is
/// ready it gives every villager the mods' <see cref="ResidentJob"/>s. The console gets <c>villager</c> and
/// <c>villagers</c>.
/// </summary>
internal sealed class AiModule : ISdkModule
{
    public void Start()
    {
        GameVillagers.Start();
        VillagerCommands.Register();
    }
}
