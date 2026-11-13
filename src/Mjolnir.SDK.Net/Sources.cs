namespace Mjolnir.SDK.Net;

/// <summary>
/// What the session sees of the game's network (Game/GameNetwork.cs; a fake in the unit tests). Players are the game's
/// player ids; the host is the session's master. Used on the main thread.
/// </summary>
internal interface INetTransport
{
    /// <summary>The local player's id.</summary>
    int LocalId { get; }

    /// <summary>The host's player id.</summary>
    int HostId { get; }

    /// <summary>Whether the local player is the host (the session's master, a dedicated server too).</summary>
    bool IsHost { get; }

    /// <summary>The Mjolnir version the host announced in the session's properties; null when the host has no Mjolnir.</summary>
    string? HostPlatform { get; }

    /// <summary>Sends a packet to one player, reliably and in order.</summary>
    void Send(int player, byte[] packet);

    /// <summary>The host removes a player from the session.</summary>
    void Kick(int player);

    /// <summary>The local player leaves the session.</summary>
    void Leave();
}
