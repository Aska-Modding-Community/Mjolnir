using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.UI;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.UI;
using GameContextMenu = SSSGame.UI.ContextMenu;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "windows" scenario (autopilot.once containing "windows"): the game's window chrome beside a mod window's, for
/// Mjolnir.SDK.UI's window styles. In the main menu: the prefabs of the settings menu, the inventory and the confirm dialog
/// dumped (shots/ui-*.txt), the settings menu opened (its tab bar in the frame), dumped live and screenshot, then the
/// Mods window (a mod window) the same; then the game quits.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> WindowsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var manager = MenuManager.Instance;
        Dump(manager.GetPrefab<GameSettingsMenu>()?.gameObject, "prefab-SettingsMenu");
        Dump(manager.GetPrefab<InventoryMenu>()?.gameObject, "prefab-InventoryMenu");
        Dump(manager.GetPrefab<ConfirmActionMenu>()?.gameObject, "prefab-ConfirmActionMenu");
        Dump(manager.GetPrefab<CraftMenu>()?.gameObject, "prefab-CraftMenu");

        var mainMenu = Menu.GetInstance<MainMenu>();
        Click(Find(mainMenu, "ButtonSettings"));
        yield return 2f;
        var settings = Menu.GetInstance<GameSettingsMenu>();
        if (settings != null)
        {
            LogTabs("settings", settings.gameObject);
            Dump(settings.gameObject, "settings-live");
            Shot("windows-settings");
            yield return AfterShot;
            settings.Close();
            yield return 2f;
        }
        else Log("Windows: the settings menu did not open");

        Click(Find(mainMenu, ModsMain));
        yield return 1.5f;
        var mods = TryWindow(n => n == ModsList);
        if (mods != null)
        {
            Log($"Windows: mods {WindowLayout(mods)}");
            Dump(mods, "mods-window");
            Shot("windows-mods");
            yield return AfterShot;
            MenuManager.Instance.BackPressTopMenu();
            yield return 1f;
        }
        // Mod windows in each style: the example's window (a title on the plate), its tabs window (the tabs in the header),
        // and UiLab's second window (the dialog style).
        Click(Find(mainMenu, ExampleMain));
        yield return 1f;
        foreach (var (item, name) in new[] { ("_0", "example"), ("_1", "tabs") })
        {
            Click(Find(mainMenu, ExampleMain + item));
            yield return 1.5f;
            var window = Object.FindObjectsByType<GameContextMenu>(FindObjectsSortMode.None)
                               .FirstOrDefault(m => m.name.StartsWith("Mjolnir_Window_", System.StringComparison.Ordinal));
            if (window != null) Log($"Windows: {name} {WindowLayout(window.gameObject)}");
            Shot($"windows-{name}");
            yield return AfterShot;
            if (window != null) HierarchyDump.Write(window.gameObject, System.IO.Path.Combine(ShotsDir, $"ui-window-{name}.txt"));
            if (name == "example" && window != null)
            {
                Click(Find(window.gameObject, "Close")); // the X in the corner
                yield return 1.5f;
                Log($"Windows: after the X, example window open {TryWindow(n => n == window.name) != null}");
            }
            else
            {
                MenuManager.Instance.BackPressTopMenu();
                yield return 1.5f;
            }
        }
        MenuManager.Instance.BackPressTopMenu(); // the level
        yield return 1f;
        UiLab.Second.Show();
        yield return 1.5f;
        Shot("windows-dialog");
        yield return AfterShot;
        UiLab.Second.Close();
        yield return 1.5f;

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The "expander" scenario: the example window's expander opened above its scroll viewer, both measured in the
    // window's scroll content (top and bottom of each, from the content's top), screenshot; then the game quits.
    private static IEnumerator<float> ExpanderResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var mainMenu = Menu.GetInstance<MainMenu>();
        Click(Find(mainMenu, ExampleMain));
        yield return 1f;
        Click(Find(mainMenu, ExampleMain + "_0"));
        yield return 1.5f;
        var window = TryWindow(n => n == ExampleWindow);
        if (window == null)
        {
            Log("Expander: the example window did not open");
            yield break;
        }
        var scroll = window.GetComponentInChildren<ScrollRect>();
        var expander = Find(window, "ExampleExpander").GetComponent<RectTransform>();
        float top = -scroll.content.InverseTransformPoint(expander.TransformPoint(new Vector3(0, expander.rect.yMax, 0))).y;
        float room = scroll.content.rect.height - scroll.viewport.rect.height;
        scroll.verticalNormalizedPosition = room > 0 ? 1 - Mathf.Clamp01((top - 40) / room) : 1;
        yield return 0.5f;
        Log($"Expander closed: {Spans(scroll, window)}");
        Click(expander.Find("Header/Hit").gameObject);
        yield return 1f;
        Log($"Expander open: {Spans(scroll, window)}");
        Shot("expander-open");
        yield return AfterShot;
        Find(window, "ExampleScroll").GetComponent<ScrollRect>().verticalNormalizedPosition = 0; // the list at its end
        yield return 0.5f;
        Shot("scrollviewer-end");
        yield return AfterShot;
        HierarchyDump.Write(expander.gameObject, System.IO.Path.Combine(ShotsDir, "ui-expander.txt"));
        HierarchyDump.Write(Find(window, "ExampleScroll"), System.IO.Path.Combine(ShotsDir, "ui-scrollviewer.txt"));
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // Where the expander, its content, the scroll viewer, its viewport and its scrollbar sit, from the scroll content's top.
    private static string Spans(ScrollRect scroll, GameObject window)
    {
        string Span(Transform? t, string name)
        {
            var rect = t?.GetComponent<RectTransform>();
            if (rect == null) return $"{name} none";
            float top = -scroll.content.InverseTransformPoint(rect.TransformPoint(new Vector3(0, rect.rect.yMax, 0))).y;
            float bottom = -scroll.content.InverseTransformPoint(rect.TransformPoint(new Vector3(0, rect.rect.yMin, 0))).y;
            return $"{name} {top:0}..{bottom:0}{(rect.gameObject.activeInHierarchy ? "" : " (hidden)")}";
        }
        var expander = Find(window, "ExampleExpander").transform;
        var viewer = Find(window, "ExampleScroll").transform;
        var inner = viewer.GetComponent<ScrollRect>();
        return $"{Span(expander, "expander")}, {Span(expander.Find("Header"), "header")}, {Span(expander.Find("Content"), "content")}, " +
               $"{Span(viewer, "viewer")}, {Span(inner.viewport, "viewport")}, {Span(inner.verticalScrollbar?.transform, "scrollbar")}";
    }

    // A mod window's chrome: the panel's size, the header (the plate or the tabs) and where it sits, the X.
    private static string WindowLayout(GameObject window)
    {
        var panel = window.transform.Find("Panel")?.GetComponent<RectTransform>();
        if (panel == null) return "no panel";
        string Part(string name)
        {
            var part = panel.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == name);
            return part == null ? $"{name} none" : $"{name} {(part.gameObject.activeInHierarchy ? "" : "(hidden) ")}{part.rect.width:0}x{part.rect.height:0} at {part.anchoredPosition.x:0},{part.anchoredPosition.y:0}";
        }
        return $"panel {panel.rect.width:0}x{panel.rect.height:0}; {Part("Chrome")}; {Part("Header")}; {Part("Plate")}; {Part("Tabs")}; {Part("Subtitle")}; {Part("Close")}; {Part("Scroll")}";
    }
}
