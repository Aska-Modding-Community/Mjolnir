using System;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SSSGame;
using SSSGame.Network;
using SSSGame.UI;

namespace Mjolnir.SDK.Net;

/// <summary>The network module's hidden patches, listed on <see cref="GameNetwork"/>.</summary>
[HarmonyPatch]
internal static class NetPatches
{
    [HarmonyPatch(typeof(NetworkSession), nameof(NetworkSession._SetUpSessionProperties))]
    [HarmonyPrefix]
    private static void SessionProperties(Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty> propperties) =>
        Guard("session properties", () => GameNetwork.AnnounceSession(propperties));

    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.Fusion_Simulation_ICallbacks_PlayerJoined))]
    [HarmonyPostfix]
    private static void Joined(NetworkRunner __instance, PlayerRef player) => Guard("player joined", () => GameNetwork.PlayerJoined(__instance, player));

    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.Fusion_Simulation_ICallbacks_PlayerLeft))]
    [HarmonyPostfix]
    private static void Left(NetworkRunner __instance, PlayerRef player) => Guard("player left", () => GameNetwork.PlayerLeft(__instance, player));

    // Returning false keeps a Mjolnir packet from the game: its world streaming would take it for its own data. A packet
    // with the header is kept from it even when reading it failed. The game's own packets pass with nothing made for them.
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.Fusion_Simulation_ICallbacks_OnReliableData))]
    [HarmonyPrefix]
    private static bool Data(NetworkRunner __instance, Il2CppStructArray<byte> dataArray)
    {
        if (!GameNetwork.HasMagic(dataArray)) return true;
        Guard("reliable data", () => GameNetwork.Received(__instance, dataArray));
        return false;
    }

    [HarmonyPatch(typeof(NetworkSession), nameof(NetworkSession.OnShutdown))]
    [HarmonyPostfix]
    private static void Shutdown(ShutdownReason shutdownReason) => Guard("shutdown", () => GameNetwork.End($"the session shut down ({shutdownReason})"));

    // A refused client sees why instead of the game's own message about leaving.
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.DisplayDisconnectionMessage))]
    [HarmonyPrefix]
    private static bool DisconnectionMessage()
    {
        bool ours = false;
        Guard("disconnection message", () =>
        {
            ours = GameNetwork.HasPendingRefusal;
            if (ours) GameNetwork.ShowPendingRefusal();
        });
        return !ours;
    }

    // The main menu shows its last disconnection reason as it activates (MainMenu._disconnectReason), past
    // DisplayDisconnectionMessage: a pending refusal clears it first.
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.OnActivate))]
    [HarmonyPrefix]
    private static void MenuActivating() => Guard("main menu activation", () =>
    {
        if (GameNetwork.HasPendingRefusal) GameNetwork.ForgetDisconnectReason();
    });

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
    [HarmonyPostfix]
    private static void MenuStarted() => Guard("main menu", GameNetwork.ShowPendingRefusal);

    private static void Guard(string what, Action action) => GameGuard.Run(NetTexts.Source, what, action);
}
