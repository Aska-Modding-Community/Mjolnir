using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SSSGame.Controllers;
using UnityEngine;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// How much of a player's own walking goes through while an animation holds it: <c>PlayerDrive.FixedUpdate</c> reads the input
/// into <c>_direction</c> and sends it with
/// <c>SendMovementCommand</c>, so a prefix there scales the direction. The game keeps the movement and sends it over the
/// network as ever. Patched once an animation first holds a player.
/// </summary>
[HarmonyPatch]
internal static class MovementHold
{
    // By the drive's pointer; the drive's object is kept beside its factor, so its pointer is not taken by another while held
    // and a destroyed drive is told and dropped.
    private static readonly Dictionary<IntPtr, (PlayerDrive Drive, float Factor)> s_factors = [];
    private static bool s_patched;

    /// <summary>The factor for that player's drive, 1 to let go. Main thread.</summary>
    public static void Set(PlayerDrive drive, float factor)
    {
        DropDestroyed();
        if (factor >= 1f)
        {
            s_factors.Remove(drive.Pointer);
            return;
        }
        if (!s_patched)
        {
            s_patched = true;
            try
            {
                int count = new Harmony("mjolnir.sdk.animation").CreateClassProcessor(typeof(MovementHold)).Patch()?.Count ?? 0;
                SdkLog.Info(GameAnimations.Source, $"Animations: the player's walking held ({count} method patched)");
            }
            catch (Exception e)
            {
                SdkLog.Error(GameAnimations.Source, $"Animations: patching the player's walking failed: {e}");
            }
        }
        s_factors[drive.Pointer] = (drive, Math.Clamp(factor, 0f, 1f));
    }

    private static void DropDestroyed()
    {
        if (s_factors.Count == 0) return;
        foreach (var gone in s_factors.Where(f => f.Value.Drive == null).Select(f => f.Key).ToList()) s_factors.Remove(gone);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerDrive), nameof(PlayerDrive.SendMovementCommand))]
    private static void Prefix(PlayerDrive __instance)
    {
        try
        {
            if (s_factors.Count > 0 && s_factors.TryGetValue(__instance.Pointer, out var held))
                __instance._direction = __instance._direction * held.Factor;
        }
        catch (Exception e)
        {
            SdkLog.Error(GameAnimations.Source, $"Animations: holding the player's walking failed: {e}");
        }
    }
}
