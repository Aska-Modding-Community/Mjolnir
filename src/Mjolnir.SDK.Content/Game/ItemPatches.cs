using System;
using HarmonyLib;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SSSGame;

namespace Mjolnir.SDK.Content;

/// <summary>Hidden patches behind <see cref="Items"/> and <see cref="Buildings"/> (the hybrid Harmony rule: mods never see them).</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class ItemPatches
{
    // Each time the game builds its item database (the main menu, every world), before it reads the list.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemInfoDatabase), nameof(ItemInfoDatabase.Initialize))]
    private static void BeforeInitialize(ItemInfoDatabase __instance)
    {
        try
        {
            // The mods' effects first: items' effects (on hit, when eaten) take their tables.
            GameEffects.Apply(GameManagers.Get<StatusEffectDatabase>());
            GameItems.BeforeDatabase(__instance);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Items: adding the mods' items failed: {e}");
        }
    }

    // A build site starting (a building placed, or loaded with a save), before its parts take their costs.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BuildSite), nameof(BuildSite.Initialize))]
    private static void BeforeBuildSite(BuildSite __instance, Structure parent)
    {
        try
        {
            GameBuildings.BeforeBuildSite(__instance, parent);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Items: a mod building's cost failed: {e}");
        }
    }

    // A structure's cost as the build menu shows it.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NodeTemplate), nameof(NodeTemplate.FillManifest))]
    private static bool BeforeFillManifest(NodeTemplate __instance, ItemManifest manifest)
    {
        try
        {
            return !GameBuildings.FillCost(__instance, manifest);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Items: a mod building's shown cost failed: {e}");
            return true;
        }
    }
}
