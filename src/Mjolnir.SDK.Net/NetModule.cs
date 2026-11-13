using Mjolnir.SDK;
using Mjolnir.SDK.Net;

[assembly: SdkModule(typeof(NetModule))]

namespace Mjolnir.SDK.Net;

/// <summary>
/// The network module's entry: Mjolnir.SDK starts it in phase A. From then on every multiplayer session is checked for
/// matching mods, and mods' channels carry messages between the players that passed.
/// </summary>
internal sealed class NetModule : ISdkModule
{
    public void Start()
    {
        GameNetwork.Install();
        PlatformNetwork.Game = new PlatformChannels();
    }
}

/// <summary>The platform's messages (Mjolnir.SDK's <see cref="IPlatformNetwork"/>) on the platform's own channels.</summary>
internal sealed class PlatformChannels : IPlatformNetwork
{
    public bool IsMultiplayer => Network.IsMultiplayer;

    public bool SendToAll(string channel, byte[] data) => Network.PlatformChannel(channel).SendToAll(data) > 0;

    public void Listen(string channel, System.Action<int, byte[]> received) => Network.PlatformChannel(channel).PlatformReceived = received;
}
