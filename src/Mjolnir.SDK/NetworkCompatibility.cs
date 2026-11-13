namespace Mjolnir.SDK;

/// <summary>
/// Which players of a multiplayer session must run a mod (<see cref="ModManifestAttribute"/>'s <c>Network</c>). The network
/// module's check compares the <see cref="Everyone"/> mods of the host and a joining player, and refuses the player when they differ.
/// </summary>
public enum NetworkCompatibility
{
    /// <summary>Every peer needs it (content, mechanics). The safe default.</summary>
    Everyone,

    /// <summary>Only the player who installed it (camera, interface).</summary>
    ClientOnly,

    /// <summary>Only the host or dedicated server (rules, admin tools).</summary>
    ServerOnly,
}
