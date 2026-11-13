using System;
using HarmonyLib;
using SSSGame.Controllers;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// Around the game's own camera update each frame: <c>PlayerDrive.LateUpdate</c> (its own code, not folded with others',
/// Mjolnir.CodeMap) calls <c>InputManager.UpdateCamera</c>, the field of view's delta, the state switches and the zoom.
/// </summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class CameraPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerDrive), nameof(PlayerDrive.LateUpdate))]
    private static void BeforeGame(PlayerDrive __instance)
    {
        if (IsLocal(__instance)) GameCameraSource.Guard("the camera before the game's update", CameraDirector.BeforeGame);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerDrive), nameof(PlayerDrive.LateUpdate))]
    private static void AfterGame(PlayerDrive __instance)
    {
        if (IsLocal(__instance)) GameCameraSource.Guard("the camera after the game's update", CameraDirector.AfterGame);
    }

    private static bool s_found;

    // Every player's drive runs LateUpdate; only the local one moves the camera. In the game's shared mode the local player
    // has the state authority over its own drive, not the input authority (seen in game: the input authority's check never
    // let the update through, 2026-11-12).
    private static bool IsLocal(PlayerDrive drive)
    {
        try
        {
            bool state = drive.HasStateAuthority, input = drive.HasInputAuthority;
            if (!s_found && (state || input))
            {
                s_found = true;
                SdkLog.Info(GameCameraSource.Source, $"The local player's camera update found (state authority {state}, input authority {input})");
            }
            return state || input;
        }
        catch (Exception)
        {
            return false; // a drive whose authority cannot be told moves no camera of ours
        }
    }
}
