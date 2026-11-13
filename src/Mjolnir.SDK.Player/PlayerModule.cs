using Mjolnir.SDK;
using Mjolnir.SDK.Player;

[assembly: SdkModule(typeof(PlayerModule))]

namespace Mjolnir.SDK.Player;

/// <summary>
/// The player module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). It starts following the local
/// player (two rarely called game methods are patched); the stamina patch waits for the first
/// <see cref="StaminaStat.Draining"/> handler. The console gets <c>pos</c> and <c>tp</c>.
/// </summary>
internal sealed class PlayerModule : ISdkModule
{
    public void Start()
    {
        GamePlayer.Start();
        PlayerCommands.Register();
    }
}
