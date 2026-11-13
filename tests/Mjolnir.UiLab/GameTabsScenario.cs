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

namespace Mjolnir.UiLab;

/// <summary>
/// The UI scenario's check of mod tabs in the game's own windows (Menus.Settings, Menus.Journal), with UiExample's two
/// tabs: in the settings menu (main menu) and in the journal (in the world), each found by its object name, clicked, its
/// page and the window's other parts logged, its hover card read, Q/E around it, screenshots.
/// </summary>
internal static partial class Autopilot
{
    private const string ExampleSettingsTab = "Mjolnir_Settings_Mjolnir.UiExample_0";
    private const string ExampleJournalTab = "Mjolnir_Journal_Mjolnir.UiExample_0";

    private static IEnumerable<float> SettingsTabs(MainMenu mainMenu)
    {
        Click(Find(mainMenu, "ButtonSettings"));
        yield return 2f;
        var settings = Menu.GetInstance<GameSettingsMenu>();
        var tab = settings == null ? null : FindTab(settings.transform, ExampleSettingsTab);
        if (settings == null || tab == null)
        {
            Log($"Settings tabs: the example's tab is missing (settings open {settings != null})");
            settings?.Close(); // the rest of the scenario goes on from the main menu
            yield return 2f;
            yield break;
        }
        var group = settings.MainTabGroup;
        Log($"Settings tabs: {TabOrder(group)} (the game's four, then '{ExampleSettingsTab}' at 1000 expected); {TabLook(tab)}");
        Click(tab.gameObject);
        yield return 1.5f;
        var page = tab.GetComponents<TabButton>().FirstOrDefault(b => b.page != null && b.group == group)?.page;
        var first = settings.settingsPages[0];
        Log($"Settings tabs: after a click selected '{group.selectedButton?.name}', page {Shown(page)}, title '{PageTitle(page)}', " +
            $"row 'Setting HUD.ShowText' {(page != null && page.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Setting HUD.ShowText"))}; " +
            $"Apply/Discard/Reset shown {first.applyButton.gameObject.activeSelf}/{first.discardButton.gameObject.activeSelf}/{first.resetButton.gameObject.activeSelf} (all false expected)");
        Shot("settings-mod-tab");
        yield return AfterShot;
        foreach (float wait in HoverCard(tab.gameObject, settings.transform, "settings")) yield return wait;
        group._SelectPreviousPage();
        yield return 1.5f;
        Log($"Settings tabs: after Q selected '{group.selectedButton?.name}' (GameplayTabButton expected); Apply/Discard/Reset shown " +
            $"{first.applyButton.gameObject.activeSelf}/{first.discardButton.gameObject.activeSelf}/{first.resetButton.gameObject.activeSelf} (as the game had them)");
        group._SelectNextPage();
        yield return 1.5f;
        Log($"Settings tabs: after E selected '{group.selectedButton?.name}' (ours expected)");
        settings.Close();
        yield return 2f;
    }

    private static IEnumerable<float> JournalTabs()
    {
        foreach (float wait in OpenForResearch<PlayerMenu>("journal-for-tabs")) yield return wait;
        var journal = Menu.GetInstance<PlayerMenu>();
        var tab = journal == null ? null : FindTab(journal.transform, ExampleJournalTab);
        if (journal == null || tab == null)
        {
            Log($"Journal tabs: the example's tab is missing (journal open {journal != null})");
            journal?.Close();
            yield return 1.5f;
            yield break;
        }
        var group = journal.MainTabGroup;
        Log($"Journal tabs: {TabOrder(group)}; {TabLook(tab)}");
        Click(tab.gameObject);
        yield return 1.5f;
        var page = tab.GetComponents<TabButton>().FirstOrDefault(b => b.page != null && b.group == group)?.page;
        var supplies = page?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ExampleSupplies");
        Log($"Journal tabs: after a click selected '{group.selectedButton?.name}', page {Shown(page)}, title '{PageTitle(page)}', " +
            $"window {(page?.transform.Find("Chrome") != null)}, panel {(page?.transform.Find("Panel") != null)}, supplies grid {(supplies != null ? $"{supplies.GetComponent<RectTransform>().rect.width:0} wide" : "missing")}");
        Shot("journal-mod-tab");
        yield return AfterShot;
        foreach (float wait in HoverCard(tab.gameObject, journal.transform, "journal")) yield return wait;
        group._SelectPreviousPage();
        yield return 1.5f;
        Log($"Journal tabs: after Q selected '{group.selectedButton?.name}', our page {Shown(page)} (hidden expected)");
        journal.Close();
        yield return 1.5f;
    }

    private static Transform? FindTab(Transform menu, string name) =>
        menu.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy);

    private static string TabOrder(TabGroup group) =>
        string.Join(", ", Enumerable.Range(0, group.buttons?.Count ?? 0).Select(i => group.buttons![i]).Where(b => b != null)
                                    .Select(b => $"{b.name} {b.order}"));

    // What the tab shows: its picture (or letter) and the hover card's texts set on it.
    private static string TabLook(Transform tab)
    {
        var icon = tab.Find("Icon")?.GetComponent<Image>();
        var letter = tab.Find("Title")?.GetComponent<TMP_Text>();
        var hover = tab.GetComponent<TabButtonTooltip>();
        return $"icon {(icon != null && icon.gameObject.activeSelf ? icon.sprite?.name ?? "none" : "hidden")}, " +
               $"letter {(letter != null && letter.gameObject.activeSelf ? $"'{letter.text}'" : "hidden")}, " +
               $"hover card '{hover?.OverrideTitle}' / '{hover?.OverrideDetails}'";
    }

    private static string Shown(TabPage? page) => page == null ? "missing" : page.gameObject.activeInHierarchy ? "shown" : "hidden";

    private static string PageTitle(TabPage? page) => page?.transform.Find("Title")?.GetComponent<TMP_Text>()?.text ?? "-";

    // The game's hover card (TabDetails) while the pointer rests on the tab: its texts.
    private static IEnumerable<float> HoverCard(GameObject tab, Transform menu, string where)
    {
        var data = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(tab, data, ExecuteEvents.pointerEnterHandler);
        yield return 1f;
        var card = menu.GetComponentsInChildren<TooltipDetailsTabPage>(false).FirstOrDefault();
        string texts = card == null ? "none" : string.Join(" | ", card.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text.Trim()).Where(t => t.Length > 0));
        Log($"{where} tabs: hover card over ours: {texts} (the header and UI Example expected)");
        Shot($"{where}-mod-tab-hover");
        yield return AfterShot;
        ExecuteEvents.Execute(tab, data, ExecuteEvents.pointerExitHandler);
    }
}
