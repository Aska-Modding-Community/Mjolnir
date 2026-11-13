using HarmonyLib;
using SSSGame;

namespace Mjolnir.SDK.AI;

/// <summary>Hidden patches behind <see cref="Interactions"/> (the hybrid Harmony rule).</summary>
[HarmonyPatch]
internal static class InteractionPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.End))]
    private static void Ended(InteractionSession __instance, InteractionSessionState __0) =>
        GameVillagers.Guard("an interaction's use", () => GameInteractions.PlayerSessionEnded(__instance, __0));

    // A structure's view set up in the world (not the placement preview, which has no structure): its kind's interactions.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructurePreview), nameof(StructurePreview.Setup))]
    private static void BuildingSetUp(Structure __0)
    {
        if (__0 != null) GameVillagers.Guard("a building's interactions", () => GameInteractions.BuildingSetUp(__0));
    }
}
