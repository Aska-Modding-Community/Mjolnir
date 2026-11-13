using System;
using System.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// The network for the platform's own messages (an animation another player sees), which belong to no mod: the network
/// module connects it as it starts, and without it nothing is sent. Messages reach only the players that passed the mod
/// check. Part of the module contract.
/// </summary>
internal interface IPlatformNetwork
{
    /// <summary>Whether the local player is in a multiplayer session.</summary>
    bool IsMultiplayer { get; }

    /// <summary>Sends to every other checked player; false when there is none (or no session).</summary>
    bool SendToAll(string channel, byte[] data);

    /// <summary>Calls back, on the main thread, with the sender's player id and the data of each message of the channel.</summary>
    void Listen(string channel, Action<int, byte[]> received);
}

internal static class PlatformNetwork
{
    private static IPlatformNetwork? s_game;

    /// <summary>The network module's side, null until it started (or without it).</summary>
    public static IPlatformNetwork? Game
    {
        get => Volatile.Read(ref s_game);
        set => Volatile.Write(ref s_game, value);
    }
}
