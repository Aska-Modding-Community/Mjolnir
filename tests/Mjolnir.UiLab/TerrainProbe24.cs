using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using VegetationPackage = AwesomeTechnologies.VegetationSystem.VegetationPackagePro;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe24" scenario: when the world's kinds can be read for kinds of world items of the SDK's own. A second
/// prefix on the item database's build (the content module patches it already) logs, each time the game builds the
/// database, the scene, whether the vegetation system's packages are loaded and what they hold; a postfix on a world
/// item's set-up logs when the save's world items come up. It changes nothing in the save.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe24()
    {
        int patched = 0;
        string error = Try(() =>
        {
            patched = UiLab.Instance.Harmony.CreateClassProcessor(typeof(Probe24Patches)).Patch()?.Count ?? 0;
            return "";
        });
        Log($"Probe24: {patched} method(s) patched {error}");
        Probe24Patches.Report("at the probe's start");
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Probe24Patches.Report("in the main menu");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        Probe24Patches.Report("in the world");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}

[HarmonyPatch]
internal static class Probe24Patches
{
    private static int s_items;

    public static void Report(string when)
    {
        var packages = Resources.FindObjectsOfTypeAll<VegetationPackage>();
        var names = new HashSet<string>();
        int prefabs = 0;
        foreach (var package in packages)
        {
            var list = package?.VegetationInfoList;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                if (list[i] == null || !names.Add(list[i].Name)) continue;
                if (list[i].VegetationPrefab != null) prefabs++;
            }
        }
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        UiLab.Instance.Log.Info($"Probe24: {when}: scene '{SceneManager.GetActiveScene().name}', biomes manager {GameManagers.Get<BiomesManager>() != null}, {packages.Length} vegetation packages, {names.Count} kinds ({prefabs} with a model; willow {names.Contains("tree_willow")}, shore boulder {names.Contains("rock_shoreline_big")}, lupins {names.Contains("flowers_lupins")}), item database {(db != null ? db.CompleteItemInfoList?.itemInfoList?.Count : null)} entries, world items set up so far {s_items}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemInfoDatabase), nameof(ItemInfoDatabase.Initialize))]
    private static void BeforeDatabase()
    {
        try
        {
            Report("the item database being built");
        }
        catch (Exception e)
        {
            UiLab.Instance.Log.Info($"Probe24: the report failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryItemInstance), nameof(InventoryItemInstance.Initialize))]
    private static void AfterWorldItem()
    {
        s_items++;
        if (s_items is 1 or 100 or 1000) UiLab.Instance.Log.Info($"Probe24: world item {s_items} set up, scene '{SceneManager.GetActiveScene().name}'");
    }
}
