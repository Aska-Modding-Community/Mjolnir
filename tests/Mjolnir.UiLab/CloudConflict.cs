using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio.Platform;
using SSSGame;
using SSSGame.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The autopilot answers the game's cloud conflict by itself, as the user asked (2026-10-31): the local save stays. Restoring
/// a save after a run leaves it newer than the cloud's copy of that run, and the dialog would hold the game at its loading
/// screen. The answer is the dialog's own "keep the local version" (ResolveConflict(true)). The "cloud" scenario only starts
/// the game to the main menu and quits, so the restored save reaches the cloud.
/// </summary>
[HarmonyPatch]
internal static class CloudConflict
{
    public static int Answered;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameState_Menu), nameof(GameState_Menu._onReadConflict))]
    [HarmonyPatch(typeof(HostTabPage), nameof(HostTabPage._onReadConflict))]
    private static bool KeepLocal(PlatformAPI.CloudSyncConflict __0)
    {
        Answered++;
        UiLab.Instance.Log.Info($"Cloud conflict over '{__0.SessionID}': local {__0.LocalTimestamp}, cloud {__0.CloudTimestamp}; the local save stays");
        __0.ResolveConflict(true);
        return false;
    }
}

internal static partial class Autopilot
{
    private static IEnumerator<float> CloudScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 15f;
        Log($"Autopilot done: {CloudConflict.Answered} cloud conflict(s) answered");
        Application.Quit();
    }
}
