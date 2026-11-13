using System.Collections.Generic;
using System.Linq;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "gametabs" scenario (autopilot.once containing "gametabs"): the tabs of the game's settings menu and journal, for
/// mod tabs there. Each menu's tab group, buttons and pages are logged and dumped (shots/ui-*.txt), then a tab of our own
/// is added the way a mod's would be (a clone of the last tab, a page of its own) and watched: its place in the bar, a
/// click, Q/E, hover, a game tab again, the menu closed and opened again. The settings menu in the main menu, the journal
/// and the settings menu in the world; then the game quits.
/// </summary>
internal static partial class Autopilot
{
    private const string ResearchTab = "Mjolnir_ResearchTab";

    private static IEnumerator<float> GameTabsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var mainMenu = Menu.GetInstance<MainMenu>();

        Click(Find(mainMenu, "ButtonSettings"));
        yield return 2f;
        var settings = Menu.GetInstance<GameSettingsMenu>();
        if (settings == null)
        {
            Log("GameTabs: the settings menu did not open");
            yield break;
        }
        LogGroup("settings", settings.MainTabGroup, settings.transform);
        var pages = settings.settingsPages;
        for (int i = 0; i < (pages?.Length ?? 0); i++) LogSettingsPage(pages![i], settings.transform);
        Dump(settings.gameObject, "gametabs-settings");
        foreach (float wait in TryResearchTab("settings", settings.MainTabGroup, settings.transform)) yield return wait;

        long first = settings.Pointer.ToInt64();
        settings.Close();
        yield return 2f;
        Log($"GameTabs settings: after Close the instance is {(settings == null ? "destroyed" : "kept")}, open {Menu.IsOpen<GameSettingsMenu>()}");
        Click(Find(mainMenu, "ButtonSettings"));
        yield return 2f;
        var again = Menu.GetInstance<GameSettingsMenu>();
        if (again != null)
        {
            Log($"GameTabs settings reopened: same instance {again.Pointer.ToInt64() == first}");
            LogGroup("settings reopened", again.MainTabGroup, again.transform);
            Shot("gametabs-settings-reopened");
            yield return AfterShot;
            again.Close();
            yield return 2f;
        }

        foreach (float wait in LoadSave(mainMenu)) yield return wait;
        yield return 3f;
        foreach (float wait in OpenForResearch<PlayerMenu>("journal")) yield return wait;
        var journal = Menu.GetInstance<PlayerMenu>();
        if (journal != null && Menu.IsOpen<PlayerMenu>())
        {
            LogGroup("journal", journal.MainTabGroup, journal.transform);
            Dump(journal.gameObject, "gametabs-journal");
            foreach (float wait in TryResearchTab("journal", journal.MainTabGroup, journal.transform)) yield return wait;
            long journalPointer = journal.Pointer.ToInt64();
            journal.Close();
            yield return 2f;
            Log($"GameTabs journal: after Close the instance is {(journal == null ? "destroyed" : "kept")}, open {Menu.IsOpen<PlayerMenu>()}");
            foreach (float wait in OpenForResearch<PlayerMenu>("journal-again")) yield return wait;
            var journalAgain = Menu.GetInstance<PlayerMenu>();
            if (journalAgain != null && Menu.IsOpen<PlayerMenu>())
            {
                Log($"GameTabs journal reopened: same instance {journalAgain.Pointer.ToInt64() == journalPointer}");
                LogGroup("journal reopened", journalAgain.MainTabGroup, journalAgain.transform);
                journalAgain.Close();
                yield return 2f;
            }
        }

        // The settings menu in the world (the pause menu's Settings opens it): another instance?
        foreach (float wait in OpenForResearch<GameSettingsMenu>("settings-world")) yield return wait;
        var worldSettings = Menu.GetInstance<GameSettingsMenu>();
        if (worldSettings != null && Menu.IsOpen<GameSettingsMenu>())
        {
            Log($"GameTabs settings in the world: same instance as the main menu's {worldSettings.Pointer.ToInt64() == first}");
            LogGroup("settings in the world", worldSettings.MainTabGroup, worldSettings.transform);
            worldSettings.Close();
            yield return 2f;
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A tab of our own, added as a mod's would be: a clone of the last shown tab placed after it, a higher order, and a
    // page of its own beside the game's pages. Then watched.
    private static IEnumerable<float> TryResearchTab(string where, TabGroupControls group, Transform menu)
    {
        var buttons = Buttons(group);
        var template = buttons.LastOrDefault(b => b.gameObject.activeInHierarchy);
        var gamePage = buttons.Select(b => b.page).FirstOrDefault(p => p != null);
        if (template == null || gamePage == null)
        {
            Log($"GameTabs {where}: no tab or page to copy");
            yield break;
        }
        var go = Object.Instantiate(template.gameObject, template.transform.parent, false);
        go.name = ResearchTab;
        go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        var tab = go.GetComponent<TabButton>();
        tab.order = 100;
        tab.selected = false;
        if (tab.TryCast<TabButtonTooltip>() is { } tip)
        {
            tip.OverrideTitle = "Mjolnir research";
            tip.OverrideDetails = "A mod's tab";
        }
        var page = NewResearchPage(gamePage);
        tab.page = page;
        Log($"GameTabs {where}: research tab cloned from '{template.name}' (group field {(tab.group != null && tab.group.Pointer == group.Pointer ? "the live group" : tab.group?.name ?? "null")})");
        yield return 1f;
        LogGroup($"{where} with our tab", group, menu);
        Shot($"gametabs-{where}-added");
        yield return AfterShot;

        Click(go);
        yield return 1.5f;
        LogGroup($"{where} after clicking our tab", group, menu);
        Shot($"gametabs-{where}-selected");
        yield return AfterShot;

        group._SelectNextPage();
        yield return 1.5f;
        Log($"GameTabs {where}: after E (next) selected '{group.selectedButton?.name}'");
        group._SelectPreviousPage();
        yield return 1.5f;
        Log($"GameTabs {where}: after Q (previous) selected '{group.selectedButton?.name}'");

        var hover = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(template.gameObject, hover, ExecuteEvents.pointerEnterHandler);
        yield return 1f;
        Log($"GameTabs {where}: hovering '{template.name}': {Shown(menu)}");
        Shot($"gametabs-{where}-hover-game");
        yield return AfterShot;
        ExecuteEvents.Execute(template.gameObject, hover, ExecuteEvents.pointerExitHandler);
        ExecuteEvents.Execute(go, hover, ExecuteEvents.pointerEnterHandler);
        yield return 1f;
        Log($"GameTabs {where}: hovering ours: {Shown(menu)}");
        Shot($"gametabs-{where}-hover-ours");
        yield return AfterShot;
        ExecuteEvents.Execute(go, hover, ExecuteEvents.pointerExitHandler);

        Click(buttons[0].gameObject);
        yield return 1.5f;
        LogGroup($"{where} after a game tab", group, menu);
        Click(go);
        yield return 1.5f;
        Log($"GameTabs {where}: our tab selected again: '{group.selectedButton?.name}', index {group.SelectedButtonIndex}");
    }

    // An empty page with the game page's rectangle, a frame and a line of text, hidden like the game's pages.
    private static TabPage NewResearchPage(TabPage gamePage)
    {
        var source = gamePage.GetComponent<RectTransform>();
        var go = new GameObject("Mjolnir_ResearchPage", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(source.parent, false);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta;
        rect.anchoredPosition = source.anchoredPosition;
        go.SetActive(false);
        var back = new GameObject("Back", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        back.transform.SetParent(rect, false);
        var backRect = back.GetComponent<RectTransform>();
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = Vector2.one;
        backRect.sizeDelta = Vector2.zero;
        back.AddComponent<Image>().color = new Color(0.2f, 0.35f, 0.3f, 0.5f);
        var text = new GameObject("Text", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        text.transform.SetParent(rect, false);
        var label = text.AddComponent<TextMeshProUGUI>();
        label.text = "Mjolnir research page";
        label.fontSize = 40;
        label.alignment = TextAlignmentOptions.Center;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        return go.AddComponent<TabPage>();
    }

    private static List<TabButton> Buttons(TabGroup group) =>
        group.buttons == null ? [] : Enumerable.Range(0, group.buttons.Count).Select(i => group.buttons[i]).Where(b => b != null).ToList();

    private static void LogGroup(string where, TabGroupControls group, Transform menu)
    {
        if (group == null)
        {
            Log($"GameTabs {where}: no tab group");
            return;
        }
        var bar = group.transform;
        string children = string.Join(", ", Enumerable.Range(0, bar.childCount).Select(i => bar.GetChild(i))
            .Select(c => $"{c.name}{(c.gameObject.activeSelf ? "" : " (off)")} {c.GetComponent<RectTransform>().rect.width:0}x{c.GetComponent<RectTransform>().rect.height:0}@{c.GetComponent<RectTransform>().anchoredPosition.x:0}"));
        var barRect = bar.GetComponent<RectTransform>();
        Log($"GameTabs {where}: group '{PathOf(menu, bar)}' openAtIndex {group.openAtIndex}, order {group.order}, auto select {group.AutoSelectButton}, " +
            $"selected '{group.selectedButton?.name}' (index {group.SelectedButtonIndex}); bar {barRect.rect.width:0}x{barRect.rect.height:0} " +
            $"layout {(bar.GetComponent<HorizontalLayoutGroup>() != null ? "horizontal" : "none")} fitter {(bar.GetComponent<ContentSizeFitter>() != null)}; children: {children}");
        foreach (var b in Buttons(group))
        {
            var rect = b.GetComponent<RectTransform>();
            var tip = b.TryCast<TabButtonTooltip>();
            Log($"GameTabs {where}:   {b.name} [{b.GetIl2CppType().Name}] order {b.order} selected {b.selected} hover-select {b.TabSelectOnHover} " +
                $"{(b.gameObject.activeInHierarchy ? "shown" : "hidden")} sibling {b.transform.GetSiblingIndex()} {rect.rect.width:0}x{rect.rect.height:0} " +
                $"page '{(b.page != null ? PathOf(menu, b.page.transform) : "-")}' [{b.page?.GetIl2CppType().Name}] {(b.page != null && b.page.gameObject.activeInHierarchy ? "shown" : "hidden")}" +
                (tip != null ? $"; title '{tip.titleText}', details '{tip.toolTipDetails}', override '{tip.OverrideTitle}', offset {tip.pagePosOffset}" : ""));
        }
    }

    private static void LogSettingsPage(SettingsTabPage page, Transform menu)
    {
        var rect = page.GetComponent<RectTransform>();
        string Button(Button? b) => b == null ? "-" : PathOf(menu, b.transform);
        Log($"GameTabs settings page '{PathOf(menu, page.transform)}' [{page.GetIl2CppType().Name}] {rect.rect.width:0}x{rect.rect.height:0} " +
            $"a({rect.anchorMin.x},{rect.anchorMin.y})-({rect.anchorMax.x},{rect.anchorMax.y}) @{rect.anchoredPosition}; title '{TextOf(page.title)}', " +
            $"subtitle '{TextOf(page.subTitle)}'; apply {Button(page.applyButton)}, discard {Button(page.discardButton)}, reset {Button(page.resetButton)}");
    }

    private static string TextOf(RectTransform? rect) =>
        rect == null ? "-" : $"{rect.name}: {rect.GetComponentInChildren<TMP_Text>(true)?.text}";

    // What is shown under the menu that could be a hover card: tooltip pages and anything named like one.
    private static string Shown(Transform menu) =>
        string.Join(", ", menu.GetComponentsInChildren<TabPage>(false).Select(p => $"{p.name} [{p.GetIl2CppType().Name}]"));
}
