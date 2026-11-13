using Mjolnir.SDK;
using Mjolnir.SDK.Combat;

[assembly: SdkModule(typeof(CombatModule))]

namespace Mjolnir.SDK.Combat;

/// <summary>
/// The combat module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). From then on
/// <see cref="Combatants"/> lists the game's players, villagers, pets, creatures and dens, reports every blow and death, and
/// mods' damage, healing and kills reach the game; the console gets <c>combatants</c>, <c>hurt</c>, <c>heal</c> and <c>kill</c>.
/// </summary>
internal sealed class CombatModule : ISdkModule
{
    public void Start()
    {
        GameCombat.Connect();
        CombatCommands.Register();
    }
}
