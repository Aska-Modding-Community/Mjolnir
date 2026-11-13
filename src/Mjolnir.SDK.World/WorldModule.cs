using Mjolnir.SDK;
using Mjolnir.SDK.World;

[assembly: SdkModule(typeof(WorldModule))]

namespace Mjolnir.SDK.World;

/// <summary>
/// The world module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). <see cref="WorldTime"/> reads the
/// game's clock from then on, and the console gets <c>time</c>. No game method is patched.
/// </summary>
internal sealed class WorldModule : ISdkModule
{
    public void Start()
    {
        GameClock.Connect();
        WorldCommands.Register();
    }
}
