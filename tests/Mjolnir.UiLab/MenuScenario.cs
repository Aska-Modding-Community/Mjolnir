using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SandSailorStudio.UI;
using SSSGame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameContextMenu = SSSGame.UI.ContextMenu;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using MenuShortcuts = SSSGame.UI.MenuShortcutsManager;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The UI scenario's menu levels, tabs, list and tooltips (examples/Mjolnir.UiExample's «UI Example» item and its tabs
/// window): two levels down and back up by Esc and «Back» in the main menu and the pause menu, the tabs, the list's
/// selection, and the hover cards, with a pointer rested on elements through the SDK's pointer override (the player's
/// cursor never moves).
/// </summary>
internal static partial class Autopilot
{
    private const string ExampleMain = "Mjolnir_Main_Mjolnir.UiExample_0";

    // The SDK's stand-in for the mouse position (GameKeyboard.PointerOverride, internal): set by reflection.
    private static readonly FieldInfo? s_pointer = typeof(Mjolnir.SDK.UI.UiWindow).Assembly
        .GetType("Mjolnir.SDK.UI.GameKeyboard")?.GetField("PointerOverride", BindingFlags.Static | BindingFlags.NonPublic);

    // Main menu: «UI Example» → its level → «More» → its level; Esc goes up one, «Back» another. Then the tabs window.
    private static IEnumerable<float> MainMenuLevels(MainMenu mainMenu)
    {
        GlyphResearch();
        Log($"Main menu levels: {Levels(mainMenu)}");
        Click(Find(mainMenu, ExampleMain));
        yield return 1f;
        Log($"UI Example opened: {Levels(mainMenu)} (its level expected, the game's buttons hidden); items {Items(mainMenu, ExampleMain + "_Level")}");
        Shot("main-level");
        yield return AfterShot;
        Click(Find(mainMenu, ExampleMain + "_2"));
        yield return 1f;
        Log($"More opened: {Levels(mainMenu)}; items {Items(mainMenu, ExampleMain + "_2_Level")}");
        Shot("main-level-2");
        yield return AfterShot;
        MenuManager.Instance.BackPressTopMenu(); // Esc
        yield return 1f;
        Log($"After Esc: {Levels(mainMenu)} (UI Example's level expected)");
        Click(Find(mainMenu, ExampleMain + "_Level_Back"));
        yield return 1f;
        Log($"After Back: {Levels(mainMenu)} (the game's buttons expected)");

        Click(Find(mainMenu, ExampleMain));
        yield return 1f;
        Click(Find(mainMenu, ExampleMain + "_1"));
        yield return 1.5f;
        foreach (float wait in TabsWindow()) yield return wait;
        MenuManager.Instance.BackPressTopMenu(); // closes the window
        yield return 1f;
        MenuManager.Instance.BackPressTopMenu(); // the level
        yield return 1f;
        Log($"After the tabs window and two Esc: {Levels(mainMenu)} (the game's buttons expected)");
    }

    // Pause: the same levels in the place of the pause menu's buttons; Esc (the game's pause key) goes up a level first.
    private static IEnumerable<float> PauseLevels()
    {
        var pause = Menu.GetInstance<GamePauseMenu>();
        Click(Find(pause, ExamplePause));
        yield return 1f;
        Log($"Pause, UI Example opened: game buttons shown {Visible(Find(pause.transform, "ButtonSettings", inactive: true))}; {Levels(pause)}; " +
            $"items {Items(pause, ExamplePause + "_Level")}");
        Shot("pause-level");
        yield return AfterShot;
        Click(Find(pause, ExamplePause + "_2"));
        yield return 1f;
        Log($"Pause, More opened: {Levels(pause)}");
        Shot("pause-level-2");
        yield return AfterShot;
        PauseKey();
        yield return 1f;
        Log($"After the pause key: pause open {Menu.IsOpen<GamePauseMenu>()}; {Levels(pause)} (UI Example's level expected)");
        PauseKey();
        yield return 1f;
        Log($"After the pause key again: pause open {Menu.IsOpen<GamePauseMenu>()}; game buttons shown " +
            $"{Visible(Find(pause.transform, "ButtonSettings", inactive: true))}; {Levels(pause)} (the game's buttons expected)");
        Click(Find(pause, ExamplePause));
        yield return 1f;
        Click(Find(pause, ExamplePause + "_Level_Back"));
        yield return 1f;
        Log($"Pause after Back: game buttons shown {Visible(Find(pause.transform, "ButtonSettings", inactive: true))}; {Levels(pause)}");
    }

    // The tabs window: its icon tabs, the list and its details, a tab's hover card, the categories on the left, tooltips.
    private static IEnumerable<float> TabsWindow()
    {
        var window = Object.FindObjectsByType<GameContextMenu>(FindObjectsSortMode.None)
                           .FirstOrDefault(m => TryFind(m.gameObject, "ExampleTabs") != null)?.gameObject
                     ?? throw new InvalidOperationException("the tabs window is not open");
        var tabs = Find(window, "ExampleTabs");
        var list = Find(window, "ExampleList");
        Log($"Tabs window '{window.name}': tabs {Tabs(window, "IconTab")}");
        Log($"List: {Rows(list)}; details {Details(window)}");
        Shot("tabs-list");
        yield return AfterShot;
        Click(RowObjects(list)[2]);
        yield return 0.5f;
        Log($"Third row clicked: {Rows(list)}; details {Details(window)}");
        Click(Find(window, "AddSupply"));
        yield return 0.5f;
        Log($"A find added: {Rows(list)}; details {Details(window)} (the new row selected expected)");
        Shot("tabs-list-changed");
        yield return AfterShot;
        HierarchyDump.Write(RowObjects(list).FirstOrDefault(), System.IO.Path.Combine(ShotsDir, "ui-copy-listrow.txt"));

        var strip = Named(window, "IconTab");
        foreach (float wait in Hover(strip[1])) yield return wait;
        Log($"Pointer on the second tab: {Card(strip[1])}");
        Shot("tooltip-tab");
        yield return AfterShot;
        Click(strip[1]);
        yield return 0.5f;
        var categories = Find(window, "ExampleCategories");
        Log($"Second tab: tabs {Tabs(window, "IconTab")}; categories {Tabs(categories, "ListTab")}");
        Click(Named(categories, "ListTab")[2]);
        yield return 0.5f;
        Log($"Third category: {Tabs(categories, "ListTab")}; content '{Texts(categories, "ListTab")}'");
        Shot("tabs-categories");
        yield return AfterShot;
        // Our copies beside the journal they are cloned from, for their look (pictures, fonts, colours).
        HierarchyDump.Write(Named(categories, "ListTab")[0], System.IO.Path.Combine(ShotsDir, "ui-copy-listtab.txt"));
        HierarchyDump.Write(Named(window, "IconTab")[0], System.IO.Path.Combine(ShotsDir, "ui-copy-icontab.txt"));
        HierarchyDump.Write(MenuManager.Instance.GetPrefab<SSSGame.UI.PlayerMenu>()?.gameObject, System.IO.Path.Combine(ShotsDir, "ui-journal.txt"));

        Click(strip[2]);
        yield return 0.5f;
        var plain = Find(window, "ExampleToolTip");
        foreach (float wait in Hover(plain)) yield return wait;
        Log($"Pointer on a button: {Card(plain)}");
        Shot("tooltip-text");
        yield return AfterShot;
        var rich = Find(window, "ExampleRichToolTip");
        foreach (float wait in Hover(rich)) yield return wait;
        Log($"Pointer on another: {Card(rich)}");
        Shot("tooltip-rich");
        yield return AfterShot;
        Point(null);
        yield return 0.5f;
        Log($"Pointer back to the mouse: {Card(rich)} (hidden expected while the mouse is elsewhere)");

        // The tiles tab: the supplies as the game's slots in a wrap panel (5 since a find was added), and three cards.
        Click(strip[3]);
        yield return 0.5f;
        var slots = Find(window, "ExampleSlots");
        Log($"Slots: {Slots(slots)}");
        Log($"Cards: {Cards(window)}");
        Shot("tiles");
        yield return AfterShot;
        Click(Named(slots, "Slot")[1]);
        Click(Find(Find(window, "ExampleCard_hunter").transform, "Card", inactive: false).gameObject);
        yield return 0.5f;
        Log($"After clicking the second slot and the hunter: {Slots(slots)}; {Cards(window)}; " +
            $"'{Find(window, "ExampleTilesPicked").GetComponentInChildren<TMP_Text>()?.text}'");
        var first = Named(slots, "Slot")[0];
        foreach (float wait in Hover(first)) yield return wait;
        Log($"Pointer on the first slot: {Card(first)}");
        Shot("tiles-picked");
        yield return AfterShot;
        Point(null);
        HierarchyDump.Write(first, System.IO.Path.Combine(ShotsDir, "ui-copy-slot.txt"));
        HierarchyDump.Write(Find(window, "ExampleCard_hunter"), System.IO.Path.Combine(ShotsDir, "ui-copy-card.txt"));
        Click(strip[0]);
        yield return 0.5f;
    }

    // A wrap panel's slots: [x] selected (the amber highlight), the count, the fill strip, the size and where each sits
    // in the panel (its rows and middle).
    private static string Slots(GameObject root)
    {
        var panel = root.GetComponentsInChildren<GridLayoutGroup>(false).FirstOrDefault();
        string grid = panel == null ? "no grid" : $"cell {panel.cellSize.x:0}x{panel.cellSize.y:0} in {panel.GetComponent<RectTransform>().rect.width:0}x{panel.GetComponent<RectTransform>().rect.height:0}";
        return grid + ": " + string.Join(" | ", Named(root, "Slot").Select(slot =>
        {
            var t = slot.transform;
            bool selected = t.Find("Highlight")?.gameObject.activeSelf == true;
            var count = SlotPart(t, "CountPanel");
            string countText = count != null && count.gameObject.activeSelf ? SlotPart(t, "Count")?.GetComponent<TMP_Text>()?.text ?? "?" : "-";
            var strip = SlotPart(t, "Strip");
            string fill = strip != null && strip.gameObject.activeSelf ? SlotPart(t, "Fill")?.GetComponent<Image>()?.fillAmount.ToString("0.00") ?? "?" : "-";
            var icon = SlotPart(t, "Icon")?.GetComponent<Image>();
            var rect = slot.GetComponent<RectTransform>();
            return $"[{(selected ? "x" : " ")}] count {countText} fill {fill} icon {(icon != null && icon.enabled ? icon.sprite?.rect.width.ToString("0") : "none")} " +
                   $"{rect.rect.width:0}x{rect.rect.height:0} at {rect.anchoredPosition.x:0},{rect.anchoredPosition.y:0}";
        }));
    }

    private static Transform? SlotPart(Transform slot, string name) =>
        slot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // The cards: [x] selected (the portrait's highlight), title and subtitle, the laid-out size and the card's scale.
    private static string Cards(GameObject window) =>
        string.Join(" | ", window.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("ExampleCard_", StringComparison.Ordinal)).Select(root =>
        {
            var card = root.Find("Card");
            bool selected = card != null && SlotPart(card, "Highlight")?.gameObject.activeSelf == true;
            string title = card != null ? SlotPart(card, "Title")?.GetComponent<TMP_Text>()?.text ?? "" : "";
            string subtitle = card != null ? SlotPart(card, "Subtitle")?.GetComponent<TMP_Text>()?.text ?? "" : "";
            var rect = root.GetComponent<RectTransform>().rect;
            return $"[{(selected ? "x" : " ")}] '{title}' / '{subtitle}' {rect.width:0}x{rect.height:0} scale {card?.localScale.x:0.00}";
        }));

    // The sprites the game draws keys with (UiKeyHint's KeyGlyphs table): the glyph sprite asset's names. (Its
    // InputManager.GetGlyphNameForBind throws on a binding made in code.)
    private static void GlyphResearch()
    {
        try
        {
            var asset = SSSGame.InputManager.GetCurrentGlyphsSpriteAsset();
            var names = new List<string>();
            if (asset != null)
            {
                for (int i = 0; i < asset.spriteCharacterTable.Count; i++) names.Add(asset.spriteCharacterTable[i].name);
            }
            Log($"Glyph sprites '{asset?.name}' ({names.Count}): {string.Join(", ", names)}");
        }
        catch (Exception e)
        {
            Log($"Glyph research failed: {e}");
        }
    }

    // Rests the pointer on the middle of an element for longer than the tooltip's delay.
    private static IEnumerable<float> Hover(GameObject element)
    {
        var rect = element.GetComponent<RectTransform>();
        Point(RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)));
        yield return 0.8f;
    }

    private static void Point(Vector2? position)
    {
        if (s_pointer == null) Log("No pointer override in Mjolnir.SDK.UI: tooltips are not checked");
        s_pointer?.SetValue(null, position);
    }

    // The game's pause key (Esc in the world): MenuShortcutsManager.Pause_started, which the SDK's prefix takes first.
    private static void PauseKey()
    {
        var shortcuts = Object.FindAnyObjectByType<MenuShortcuts>();
        if (shortcuts == null) Log("No MenuShortcutsManager: the pause key is not pressed");
        else shortcuts.Pause_started(new UnityEngine.InputSystem.InputAction.CallbackContext()); // a wrapped struct: default is null
    }

    // The menu levels on screen: the game's pages (TabPage) and the SDK's levels (…_Level).
    private static string Levels(Component root) =>
        string.Join(", ", root.GetComponentsInChildren<Transform>(true)
                              .Where(t => (t.GetComponent<TabPage>() != null || t.name.EndsWith("_Level", StringComparison.Ordinal)) && Visible(t))
                              .Select(t => t.name));

    // The buttons of a level, in order, with their texts.
    private static string Items(Component root, string level) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == level) is { } page
            ? string.Join(" | ", Enumerable.Range(0, page.childCount).Select(page.GetChild).Where(c => c.gameObject.activeSelf)
                                           .Select(c => $"{c.name} '{c.GetComponentInChildren<TMP_Text>(true)?.text}'"))
            : "none";

    // Active, and no CanvasGroup above it fading it out (the pause menu's buttons are hidden that way).
    private static bool Visible(Transform t)
    {
        if (!t.gameObject.activeInHierarchy) return false;
        for (var at = t; at != null; at = at.parent)
        {
            var group = at.GetComponent<CanvasGroup>();
            if (group != null && group.alpha < 0.01f) return false;
        }
        return true;
    }

    private static Transform Find(Transform root, string name, bool inactive) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && (inactive || t.gameObject.activeInHierarchy))
        ?? throw new InvalidOperationException($"no '{name}' under '{root.name}'");

    private static List<GameObject> Named(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(false).Where(t => t.name == name).Select(t => t.gameObject).ToList();

    // A tab strip: each tab's state ([x] selected) and size; a list tab's title too.
    private static string Tabs(GameObject tabs, string kind) =>
        string.Join(" ", Named(tabs, kind).Select(tab =>
        {
            var rect = tab.GetComponent<RectTransform>().rect;
            bool selected = kind == "IconTab"
                ? tab.transform.Find("Frame_Selected")?.gameObject.activeSelf == true
                : tab.transform.Find("Frame")?.GetComponent<Image>() is { } frame && frame.color.r > 0.9f && frame.color.b < 0.4f;
            string title = kind == "ListTab" ? $" '{tab.transform.Find("Title")?.GetComponent<TMP_Text>()?.text}'" : "";
            return $"[{(selected ? "x" : " ")}]{title} {rect.width:0}x{rect.height:0}";
        }));

    private static List<GameObject> RowObjects(GameObject list) => Named(list, "ListBoxItem");

    // A list's rows: [x] for the selected one (its amber frame shown), and their texts.
    private static string Rows(GameObject list) =>
        string.Join(" | ", RowObjects(list).Select(row =>
        {
            bool selected = row.transform.Find("ToggleEffect")?.gameObject.activeSelf == true;
            string texts = string.Join(" ", row.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text).Where(t => t.Length > 0));
            var rect = row.GetComponent<RectTransform>().rect;
            return $"[{(selected ? "x" : " ")}] {texts} {rect.width:0}x{rect.height:0}";
        }));

    private static string Details(GameObject window) =>
        $"'{Find(window, "ExampleSupplyName").GetComponentInChildren<TMP_Text>()?.text}', stock {Progress(Find(window, "ExampleSupplyStock"))}";

    // The texts shown under a tab control, its strip's excluded.
    private static string Texts(GameObject tabs, string strip) =>
        string.Join(" / ", tabs.GetComponentsInChildren<TMP_Text>(false)
                               .Where(t => t.GetComponentsInParent<Transform>(true).All(p => p.name != strip))
                               .Select(t => t.text).Where(t => t.Length > 0));

    // The hover card on screen: its texts, its size, and where it sits beside the element.
    private static string Card(GameObject element)
    {
        var card = Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Mjolnir_ToolTip");
        if (card == null) return "no card";
        string texts = string.Join(" / ", card.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text.Replace("\n", " ⏎ ")).Where(t => t.Length > 0));
        var e = element.GetComponent<RectTransform>();
        float elementRight = e.TransformPoint(new Vector3(e.rect.xMax, 0, 0)).x;
        float cardLeft = card.TransformPoint(new Vector3(card.rect.xMin, 0, 0)).x;
        float cardY = card.TransformPoint(card.rect.center).y, elementY = e.TransformPoint(e.rect.center).y;
        return $"'{texts}', {card.rect.width:0}x{card.rect.height:0}, {cardLeft - elementRight:0} px right of the element, " +
               $"middle {cardY - elementY:0} px off; bars {card.GetComponentsInChildren<Transform>(false).Count(t => t.name == "Fill")}";
    }
}
