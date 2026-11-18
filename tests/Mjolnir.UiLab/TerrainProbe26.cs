using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe26" scenario: what takes a placed bush away once it is gathered to the end. It places a berry bush and
/// a cabbage, logs every destruction of a placed object (its level and the game's stack) and the gathering's count after
/// each charge, then gathers both to the end and reads them a few seconds later. It changes the save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe26()
    {
        Log($"Probe26: {Try(() => $"{UiLab.Instance.Harmony.CreateClassProcessor(typeof(Probe26Patches)).Patch()?.Count ?? 0} method(s) patched")}");
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe26: no world");
            yield break;
        }
        var spot = PlaceSpot(character.transform.position);
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(spot.x, spot.z - 6);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        foreach (var kind in new[] { "bush_sloe_berries", "cabbage" })
        {
            var at = spot + new Vector3(kind == "cabbage" ? 3 : 0, 0, 0);
            var placed = NaturalObjects.Place(kind, at);
            yield return 2f;
            var item = Objects(at, 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_World_", StringComparison.Ordinal) == true);
            var gather = item?.gameObject != null ? item.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
            Log($"Probe26: {kind}: placed {placed != null}, gather {gather != null}, count {Try(() => gather?.CheckAvailableItemCount())} of {Try(() => gather?.CheckMaximumItemCount())}, per charge {gather?.itemCountPerCharge}");
            for (int charge = 0; charge < 8 && placed != null && placed.GatherCount > 0; charge++)
            {
                int got = placed.Gather(1);
                yield return 1f;
                Log($"Probe26: {kind}: a charge gave {got}, count now {Try(() => gather?.CheckAvailableItemCount())}, the item destroyed {item?.Destroyed}, its object active {item?.gameObject?.activeInHierarchy}");
            }
            yield return 4f;
            Log($"Probe26: {kind} 4 s after the last charge: SDK {NaturalObjects.Nearest(at, 1.5f)?.ToString() ?? "none"}");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}

[HarmonyPatch]
internal static class Probe26Patches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryItemInstance), nameof(InventoryItemInstance.Destroy))]
    private static void BeforeDestroy(InventoryItemInstance __instance, ref bool silent, ref InstanceDestructionLevel level)
    {
        try
        {
            var name = __instance.Descriptor?.itemInfo?.name;
            if (name == null || !name.StartsWith("Mjolnir_World_", StringComparison.Ordinal)) return;
            string stack = new Il2CppSystem.Diagnostics.StackTrace().ToString();
            UiLab.Instance.Log.Info($"Probe26: '{name}' destroyed, silent {silent}, level {level}; stack: {string.Join(" | ", stack.Split(new[] { (char)10 }, StringSplitOptions.RemoveEmptyEntries).Take(10).Select(l => l.Trim()))}");
        }
        catch (Exception e)
        {
            UiLab.Instance.Log.Info($"Probe26: the destroy log failed: {e.Message}");
        }
    }
}
