using Mjolnir.SDK;
using Mjolnir.SDK.Stats;

[assembly: SdkModule(typeof(StatsModule))]

namespace Mjolnir.SDK.Stats;

/// <summary>
/// The stats module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). <see cref="CharacterSheet"/> reads
/// the game's characters from then on, and the console gets <c>stats</c>, <c>skill</c>, <c>effects</c>, <c>effect</c> and
/// <c>uneffect</c>. A game method is patched only once a mod listens to experience.
/// </summary>
internal sealed class StatsModule : ISdkModule
{
    public void Start()
    {
        GameStats.Connect();
        StatsCommands.Register();
    }
}
