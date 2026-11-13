using System;
using System.Collections.Generic;
using System.Linq;
using SandSailorStudio;
using Mjolnir.SDK.UI;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "scale" scenario (autopilot.once containing "scale"): how big the game draws its interface, and Mjolnir.SDK.UI's
/// sizes. It ends with both scales back at 100 %, so the platform's file keeps its defaults.
/// <list type="bullet">
/// <item>the screen, every root canvas with its CanvasScaler, and the game's own HUD scale setting and slider;</item>
/// <item>the Mods list as tall as its rows, its rows' narrow selector;</item>
/// <item>InterfaceScale: the platform page's Menus slider at 125 % and the Mjolnir window at 150 % through the API; the
/// game's menus (the main menu and its settings menu, in the world the pause menu, inventory, craft and build menus, the
/// map last, since it ignores Close here) at 150 % keep their own size and at 80 % shrink, with the parts drawn fully on
/// screen at 100 % that are cut (none expected); the HUD factor at 130 % on top of the game's HUD scale with a notification,
/// and at 150 % on top of the game's 1.0 (the largest the player can ask for); both back to 100 %;</item>
/// <item>the example window's Sizes section: a narrow control, inherited font sizes, a padded button in bigger letters,
/// rows drawn at 80 %, a faded line, a paragraph with MaxWidth.</item>
/// </list>
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ScaleScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 2f;
        Log($"Scale: screen {Screen.width}x{Screen.height}, mode {Screen.fullScreenMode}, dpi {Screen.dpi:0}");
        Log($"Scale: HUD scale setting {HudScaleSetting()}");
        Log($"Scale: the settings menu's HUD scale slider {HudScaleSlider()}");
        LogCanvases("main menu");
        var mainMenu = Menu.GetInstance<MainMenu>();

        Click(Find(mainMenu, ModsMain));
        yield return 1.5f;
        var list = Window(n => n == ModsList);
        Log($"Sizes: Mods window {SizeOf(list)} (as tall as its rows, at most 760); mod row {ModRowLayout(ModRowOf(list, "mjolnir.test.uilab"))}");
        Shot("sizes-mods-list");
        yield return AfterShot;

        // The platform's page (Settings beside Close): the Menus slider, applied once the pointer is up (it is: the
        // autopilot holds no button).
        Click(Find(list, "PlatformSettings"));
        yield return 1.5f;
        var page = Window(n => n.StartsWith("Mjolnir_Window_Mjolnir ·", StringComparison.Ordinal));
        Log($"Scale API: platform page {SizeOf(page)}: {Describe(page)}");
        Setting(page, "UI.Menus").GetComponentInChildren<Slider>().value = 125;
        yield return 0.6f;
        Log($"Scale API: Menus 125 % from the page: InterfaceScale.Menus {InterfaceScale.Menus.Value:0.##}; page {ReferenceOf(page)}, " +
            $"Mjolnir window {ReferenceOf(list)} (1536x864 and 1536x864 expected)");
        Shot("api-page-125");
        yield return AfterShot;
        InterfaceScale.Menus.Value = 1.5;
        yield return 0.6f;
        Log($"Scale API: Menus 1.5: Mjolnir window {ReferenceOf(list)} (1280x720 expected), its panel {SizeOf(list)} on a canvas of " +
            $"{Show(list.GetComponent<RectTransform>().rect.size)}");
        Shot("api-window-150");
        yield return AfterShot;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;

        // The game's own menus: never bigger than their own size, smaller with the scale.
        foreach (float wait in Fit(mainMenu.gameObject, "main")) yield return wait;
        Click(Find(mainMenu, "ButtonSettings"));
        yield return 2f;
        var settings = Menu.GetInstance<SettingsMenu>();
        if (settings != null)
        {
            foreach (float wait in Fit(settings.gameObject, "settings")) yield return wait;
        }
        else Log("Fit: settings menu not open");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1.5f;

        foreach (float wait in LoadSave(mainMenu)) yield return wait;
        yield return 3f;
        LogCanvases("world");
        foreach (float wait in FitMenu<GamePauseMenu>("pause")) yield return wait;
        foreach (float wait in FitMenu<InventoryMenu>("inventory")) yield return wait;
        foreach (float wait in FitMenu<CraftMenu>("craft")) yield return wait;
        foreach (float wait in FitMenu<BuildMenu>("build")) yield return wait;

        // The HUD: a factor on top of the game's own HUD scale; a notification shown with it.
        var hud = Menu.GetInstance<PlayerHUDMenu>();
        float game = Settings()?.HUDScaleSetting.Value ?? 1f;
        InterfaceScale.Hud.Value = 1.3;
        yield return 0.6f;
        string message = "UiLab: a notification at 130 %";
        NotificationMenu.Display(NotificationType.GENERIC, ref message, false);
        yield return 1f;
        if (hud != null)
        {
            var initial = hud._initialReferenceResolution;
            Log($"Scale API: HUD 1.3 on the game's {game:0.###}: reference {Show(hud.GetComponent<CanvasScaler>().referenceResolution)} " +
                $"({Show(new Vector2(initial.x / (game * 1.3f), initial.y / (game * 1.3f)))} expected); notifications " +
                $"{CanvasReference(Menu.GetInstance<NotificationMenu>()?.gameObject)} (the same expected)");
        }
        Shot("api-hud-130");
        yield return AfterShot;
        if (hud != null)
        {
            // The largest HUD a player can ask for: the game's own 1.0 (only the canvas: the setting is not touched) times 1.5.
            InterfaceScale.Hud.Value = 1;
            yield return 0.6f;
            var baseline = OnScreen(hud.gameObject, out int drawn);
            InterfaceScale.Hud.Value = 1.5;
            hud.SetHUDScale(1f);
            yield return 0.6f;
            Log($"Scale API: HUD 1.5 on the game's 1.0: reference {Show(hud.GetComponent<CanvasScaler>().referenceResolution)}; " +
                $"{baseline.Count} of {drawn} drawn parts on screen at the game's scale, {Cut(hud.gameObject, baseline)}");
            Shot("api-hud-150-on-1");
            yield return AfterShot;
            hud.SetHUDScale(game);
        }

        InterfaceScale.Menus.Value = 1;
        InterfaceScale.Hud.Value = 1;
        yield return 0.6f;
        Log($"Scale API: back to 1: HUD {(hud != null ? Show(hud.GetComponent<CanvasScaler>().referenceResolution) : "none")}, notifications " +
            $"{CanvasReference(Menu.GetInstance<NotificationMenu>()?.gameObject)}; config {ConfigValues("mjolnir")}");

        // The example window's Sizes section (UiExample, from the pause menu).
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        var pause = Menu.GetInstance<GamePauseMenu>();
        if (pause != null && TryFind(pause.gameObject, ExamplePause) is { } exampleButton)
        {
            Click(exampleButton);
            yield return 1.5f;
            var example = Window(n => n == ExampleWindow);
            var narrow = Find(example, "ExampleNarrowRow");
            ScrollTo(example.GetComponentInChildren<ScrollRect>(), narrow.GetComponent<RectTransform>());
            yield return 0.5f;
            Log($"Sizes: narrow row {ModRowLayout(narrow)} (control from width - 220); paragraph " +
                $"{Find(example, "ExampleMaxWidth").GetComponent<RectTransform>().rect.width:0} wide (600 expected) in a window {SizeOf(example)}");
            var bigText = Find(example, "ExampleBigButton").GetComponentInChildren<TMP_Text>();
            Log($"Sizes: inherited fonts: header {LabelSize(Find(example, "ExampleSmallHeader"))}, toggle {LabelSize(Find(example, "ExampleSmallToggle"))} " +
                $"(15 and 15 expected); big button {bigText?.fontSize:0} (22 expected), its margins {bigText?.margin}");
            var scaled = Find(example, "ExampleScaled").GetComponent<RectTransform>();
            var inner = scaled.Find("Scaled")?.GetComponent<RectTransform>();
            Log($"Sizes: scaled rows {scaled.rect.width:0}x{scaled.rect.height:0}, inside {inner?.rect.width:0}x{inner?.rect.height:0} at " +
                $"{inner?.localScale.x:0.##} (inside = outside / 0.8 wide, outside = inside x 0.8 tall expected); faded alpha " +
                $"{Find(example, "ExampleFaded").GetComponent<CanvasGroup>()?.alpha:0.##} (0.5 expected)");
            Shot("sizes-example");
            yield return AfterShot;
            MenuManager.Instance.BackPressTopMenu();
            yield return 1f;
        }
        else Log($"Sizes: no example pause button (pause menu open: {pause != null && Menu.IsOpen<GamePauseMenu>()})");
        Menu.GetInstance<GamePauseMenu>()?.Close();
        yield return 1.5f;
        foreach (float wait in FitMenu<MapMenu>("map")) yield return wait;

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A game menu opened from code, its fit checked, then closed with its own Close (the map ignores a back press).
    private static IEnumerable<float> FitMenu<T>(string what) where T : Menu
    {
        try
        {
            Menu.Show<T>(null);
        }
        catch (Exception e)
        {
            Log($"Fit: {what} menu not opened: {e.Message}");
        }
        yield return 1.5f;
        var menu = Menu.GetInstance<T>();
        if (menu == null || !Menu.IsOpen<T>())
        {
            Log($"Fit: {what} menu not open");
            yield break;
        }
        foreach (float wait in Fit(menu.gameObject, what)) yield return wait;
        menu.Close();
        yield return 1.5f;
        Log($"Fit: {what} menu closed: {!Menu.IsOpen<T>()}");
    }

    // An open game menu at 150 % (its own size expected) and 80 %: the parts drawn fully on screen at 100 % that are cut.
    private static IEnumerable<float> Fit(GameObject menu, string what)
    {
        InterfaceScale.Menus.Value = 1;
        yield return 0.6f;
        var baseline = OnScreen(menu, out int drawn);
        string own = CanvasReference(menu);
        Log($"Fit: {what} at 100 %: reference {own}; {baseline.Count} of {drawn} drawn parts fully on screen");
        foreach (double scale in new[] { 1.5, 0.8 })
        {
            InterfaceScale.Menus.Value = scale;
            yield return 0.6f;
            Log($"Fit: {what} at {scale * 100:0} %: reference {CanvasReference(menu)} ({(scale > 1 ? own : "its own / 0.8")} expected); {Cut(menu, baseline)}");
            Shot($"fit-{what}-{scale * 100:0}");
            yield return AfterShot;
        }
        InterfaceScale.Menus.Value = 1;
        yield return 0.6f;
    }

    // The drawn parts of a menu (active, visible graphics outside scroll views) that lie fully on screen.
    private static HashSet<IntPtr> OnScreen(GameObject menu, out int drawn)
    {
        var inside = new HashSet<IntPtr>();
        drawn = 0;
        foreach (var graphic in DrawnParts(menu))
        {
            drawn++;
            if (IsOnScreen(graphic)) inside.Add(graphic.Pointer);
        }
        return inside;
    }

    private static string Cut(GameObject menu, HashSet<IntPtr> baseline)
    {
        var cut = DrawnParts(menu).Where(g => baseline.Contains(g.Pointer) && !IsOnScreen(g)).ToList();
        if (cut.Count == 0) return $"none of the {baseline.Count} parts cut";
        string names = string.Join(", ", cut.Take(5).Select(g => $"{g.transform.parent?.name}/{g.name}"));
        return $"{cut.Count} of {baseline.Count} parts cut: {names}{(cut.Count > 5 ? ", …" : "")}";
    }

    private static IEnumerable<Graphic> DrawnParts(GameObject menu) =>
        menu.GetComponentsInChildren<Graphic>(false).Where(g =>
            g.enabled && g.color.a > 0.01f && g.canvasRenderer.GetInheritedAlpha() > 0.01f && g.rectTransform.rect.width > 1 &&
            g.rectTransform.rect.height > 1 && g.GetComponentInParent<RectMask2D>() == null && g.GetComponentInParent<Mask>() == null);

    // On a screen-space overlay canvas world coordinates are screen pixels.
    private static bool IsOnScreen(Graphic graphic)
    {
        var rt = graphic.rectTransform;
        var rect = rt.rect;
        var a = rt.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0));
        var b = rt.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0));
        return Mathf.Min(a.x, b.x) >= -1 && Mathf.Min(a.y, b.y) >= -1 && Mathf.Max(a.x, b.x) <= Screen.width + 1 &&
               Mathf.Max(a.y, b.y) <= Screen.height + 1;
    }

    // The reference resolution of the root canvas an object sits on.
    private static string CanvasReference(GameObject? go) =>
        go?.GetComponentInParent<Canvas>()?.rootCanvas?.GetComponent<CanvasScaler>() is { } scaler ? Show(scaler.referenceResolution) : "none";

    private static string LabelSize(GameObject row) => row.transform.Find("NameLabel")?.GetComponent<TMP_Text>()?.fontSize.ToString("0") ?? "none";

    // Every root canvas that is on: its render mode and order, and how its CanvasScaler sizes it.
    private static void LogCanvases(string where)
    {
        var roots = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.isActiveAndEnabled).OrderBy(c => c.sortingOrder);
        foreach (var canvas in roots)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            string sizing = scaler == null ? "no CanvasScaler"
                : scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    ? $"{scaler.uiScaleMode} {Show(scaler.referenceResolution)} {scaler.screenMatchMode} match {scaler.matchWidthOrHeight:0.##}"
                    : $"{scaler.uiScaleMode} scaleFactor {scaler.scaleFactor:0.##}";
            Log($"Scale, {where}: canvas '{canvas.name}' {canvas.renderMode} order {canvas.sortingOrder}: {sizing}; drawn at {canvas.scaleFactor:0.###}");
        }
    }

    private static SettingsManager? Settings()
    {
        try
        {
            return GameManagers.Get<SettingsManager>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string HudScaleSetting()
    {
        var setting = Settings()?.HUDScaleSetting;
        return setting == null ? "not found" : $"{setting.Value:0.###} (default {setting.DefaultValue:0.###})";
    }

    private static string HudScaleSlider()
    {
        var prefab = MenuManager.Instance.GetPrefab<SettingsMenu>();
        var page = prefab?.GetComponentInChildren<GameplaySettingsTabPage>(true);
        var slider = page?.HUDScaleSliderPanel;
        return slider == null ? "not found" : $"{slider.minValue:0.###} .. {slider.maxValue:0.###}, whole numbers {slider.wholeNumbers}, prefab value {slider.value:0.###}";
    }

    private static string Show(Vector2 v) => $"{v.x:0.#}x{v.y:0.#}";

    // A mod window's reference resolution (its CanvasScaler) and its panel's size.
    private static string ReferenceOf(GameObject window) => window.GetComponent<CanvasScaler>() is { } scaler ? Show(scaler.referenceResolution) : "none";

    private static string SizeOf(GameObject window) => window.transform.Find("Panel")?.GetComponent<RectTransform>() is { } panel ? Show(panel.rect.size) : "no panel";

    // Scrolls a window's scroll view so the element is near its top.
    private static void ScrollTo(ScrollRect? scroll, RectTransform element)
    {
        if (scroll == null) return;
        Canvas.ForceUpdateCanvases();
        float below = -scroll.content.InverseTransformPoint(element.position).y - 40;
        float room = scroll.content.rect.height - scroll.viewport.rect.height;
        scroll.verticalNormalizedPosition = room > 0 ? Mathf.Clamp01(1 - below / room) : 1;
    }
}
