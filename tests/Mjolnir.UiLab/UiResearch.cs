using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Mjolnir.UiLab;

/// <summary>
/// The "uiresearch" scenario (autopilot.once containing "uiresearch"): the game's interface parts Mjolnir.SDK.UI could
/// build on, dumped (shots/ui-*.txt) and screenshot.
/// <list type="bullet">
/// <item>the main menu's levels: its tab groups, buttons and pages at the top, in «Singleplayer» and in «Load game», and
/// where each back press goes;</item>
/// <item>the prefabs of the tutorial/journal window, the build menu, the inventory, the HUD and the notifications;</item>
/// <item>in the world: the tutorial/journal window with each of its tabs, the build menu, the inventory and the live HUD.</item>
/// </list>
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> UiResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var mainMenu = Menu.GetInstance<MainMenu>();
        LogTabs("main menu", mainMenu.gameObject);
        Dump(mainMenu.gameObject, "main-menu");

        // The main menu's own levels.
        Click(Find(mainMenu, "ButtonSingleplayer"));
        yield return 1.5f;
        LogTabs("singleplayer", mainMenu.gameObject);
        Shot("ui-singleplayer");
        yield return AfterShot;
        Dump(mainMenu.gameObject, "main-menu-singleplayer");
        Click(Find(mainMenu, "ButtonLoadGame"));
        yield return 2f;
        LogTabs("load game", mainMenu.gameObject);
        Shot("ui-load-game");
        yield return AfterShot;
        Dump(mainMenu.gameObject, "main-menu-load");
        for (int i = 1; i <= 2; i++)
        {
            MenuManager.Instance.BackPressTopMenu();
            yield return 1.5f;
            LogTabs($"after back press {i}", mainMenu.gameObject);
        }
        Shot("ui-after-back");
        yield return AfterShot;

        var manager = MenuManager.Instance;
        Dump(manager.GetPrefab<TutorialMenu>()?.gameObject, "prefab-TutorialMenu");
        Dump(manager.GetPrefab<BuildMenu>()?.gameObject, "prefab-BuildMenu");
        Dump(manager.GetPrefab<InventoryMenu>()?.gameObject, "prefab-InventoryMenu");
        Dump(manager.GetPrefab<PlayerHUDMenu>()?.gameObject, "prefab-PlayerHUD");
        Dump(manager.GetPrefab<NotificationMenu>()?.gameObject, "prefab-NotificationMenu");

        foreach (float wait in LoadSave(mainMenu)) yield return wait;
        yield return 3f;
        Dump(Menu.GetInstance<PlayerHUDMenu>()?.gameObject, "hud");

        // The journal (PlayerMenu: tutorials, journal, deeds, issues, resources, statistics) and each of its tabs.
        foreach (float wait in OpenForResearch<PlayerMenu>("journal")) yield return wait;
        var journal = Menu.GetInstance<PlayerMenu>();
        if (journal != null && Menu.IsOpen<PlayerMenu>())
        {
            var tabs = journal.MainTabGroup;
            int count = tabs?.buttons?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var tab = tabs!.buttons![i];
                Click(tab.gameObject);
                yield return 1.5f;
                LogTabs($"journal tab {i} '{tab.name}'", journal.gameObject);
                Shot($"ui-journal-{i}");
                yield return AfterShot;
            }
            Dump(journal.gameObject, "journal");
            journal.Close();
            yield return 1.5f;
        }

        foreach (float wait in OpenForResearch<BuildMenu>("build")) yield return wait;
        if (Menu.GetInstance<BuildMenu>() is { } build && Menu.IsOpen<BuildMenu>())
        {
            Dump(build.gameObject, "build-menu");
            build.Close();
            yield return 1.5f;
        }
        foreach (float wait in OpenForResearch<InventoryMenu>("inventory")) yield return wait;
        if (Menu.GetInstance<InventoryMenu>() is { } inventory && Menu.IsOpen<InventoryMenu>())
        {
            Dump(inventory.gameObject, "inventory");
            inventory.Close();
            yield return 1.5f;
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A game menu opened from code for a look (a menu that wants parameters is logged).
    private static IEnumerable<float> OpenForResearch<T>(string what) where T : Menu
    {
        try
        {
            Menu.Show<T>(null);
        }
        catch (Exception e)
        {
            Log($"UiResearch: {what} not opened: {e.Message}");
        }
        yield return 2f;
        Log($"UiResearch: {what} open {Menu.IsOpen<T>()}");
        if (!Menu.IsOpen<T>()) yield break;
        Shot($"ui-{what}");
        yield return AfterShot;
    }

    // Every tab group under a root: its buttons, the selected one and each button's page and whether it is shown.
    private static void LogTabs(string where, GameObject root)
    {
        var groups = root.GetComponentsInChildren<TabGroup>(true);
        foreach (var group in groups)
        {
            var buttons = group.buttons == null ? [] : Enumerable.Range(0, group.buttons.Count).Select(i => group.buttons[i]).ToList();
            string list = string.Join(", ", buttons.Select(b =>
                $"{b?.name}{(b != null && b.gameObject.activeInHierarchy ? "" : " (hidden)")}→{b?.page?.name ?? "-"}{(b?.page != null && b.page.gameObject.activeInHierarchy ? "*" : "")}"));
            Log($"UiResearch {where}: tab group '{group.name}'{(group.gameObject.activeInHierarchy ? "" : " (hidden)")} selected " +
                $"'{group.selectedButton?.name}': {list}");
        }
        var active = root.GetComponentsInChildren<Button>(false).Select(b => b.name);
        Log($"UiResearch {where}: active buttons {string.Join(", ", active)}; menu stack {MenuManager.Instance.MenuStackCount}");
    }

    private static void Dump(GameObject? root, string name) => HierarchyDump.Write(root, Path.Combine(ShotsDir, $"ui-{name}.txt"));
}
