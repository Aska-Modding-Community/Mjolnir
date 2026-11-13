using System;
using HarmonyLib;
using SSSGame;

namespace Mjolnir.SDK.Player;

/// <summary>
/// When the game sets or unsets a player's character (PlayerManager.SetPlayer/UnsetPlayer): a few
/// calls per session, for every player, so the facade looks up whether the local character changed.
/// </summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class SpawnPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.SetPlayer))]
    private static void PlayerSet() => GamePlayer.Guard("PlayerManager.SetPlayer", GamePlayer.RefreshLocalPlayer);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.UnsetPlayer))]
    private static void PlayerUnset() => GamePlayer.Guard("PlayerManager.UnsetPlayer", GamePlayer.RefreshLocalPlayer);
}

/// <summary>
/// Every stamina cost of a character, before it is spent: Character.DrainStamina is the only place the game subtracts it
/// (virtual, called only through the vtable, so never inlined). Villagers spend through it too, so
/// it is patched only once a mod listens to <see cref="StaminaStat.Draining"/>.
/// </summary>
[HarmonyPatch]
internal static class StaminaPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.DrainStamina), typeof(float), typeof(DrainStaminaUsage))]
    private static bool DrainStamina(Character __instance, ref float value, DrainStaminaUsage usage)
    {
        try
        {
            return LocalPlayer.Stamina.Drain(__instance.Pointer, ref value, (StaminaUse)(int)usage);
        }
        catch (Exception e)
        {
            SdkLog.Error(GamePlayer.Source, $"Character.DrainStamina failed: {e}");
            return true;
        }
    }
}
