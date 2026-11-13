using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SandSailorStudio.Localization;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Localization;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "research" scenario (autopilot.once containing "research"): dumps the game's settings controls and screenshots
/// every settings tab, the material for cloning sliders, selectors, dropdowns, input fields and scroll views; then loads
/// the save and dumps the world's HUD (PlayerHUDMenu: its widgets, the bars and rings of SSSGame.ProgressBar), the
/// material for the HUD controls.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> Research()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        FindLocalizationKeys();
        var manager = MenuManager.Instance;
        HierarchyDump.Write(manager.GetPrefab<InputFieldConfirmActionMenu>()?.gameObject, Path.Combine(ShotsDir, "prefab-InputFieldConfirmActionMenu.txt"));
        HierarchyDump.Write(manager.GetPrefab<GameSettingsMenu>()?.gameObject, Path.Combine(ShotsDir, "prefab-SettingsMenu.txt"));

        // Open the settings the way the player does, from the main menu's own button if there is one.
        var mainMenu = Menu.GetInstance<MainMenu>();
        Log("Main menu buttons: " + string.Join(", ", mainMenu.GetComponentsInChildren<Button>(false).Select(b => b.name)));
        var settingsButton = mainMenu.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name.Contains("Settings"));
        if (settingsButton != null) Click(settingsButton.gameObject);
        else Menu.Show<GameSettingsMenu>(null);
        yield return 2f;
        var settings = Menu.GetInstance<GameSettingsMenu>();
        Log($"Settings open {Menu.IsOpen<GameSettingsMenu>()}");

        var tabs = settings.MainTabGroup?.buttons;
        int count = tabs?.Count ?? 0;
        Log($"Settings tabs: {count}");
        for (int i = 0; i < count; i++)
        {
            var tab = tabs![i];
            Click(tab.gameObject);
            yield return 1.5f;
            Shot($"settings-{i}-{tab.name}");
            yield return AfterShot;
            var scrolls = settings.GetComponentsInChildren<ScrollRect>(false);
            Log($"Tab {i} '{tab.name}': scroll views {string.Join(", ", scrolls.Select(s => $"{s.name} ({s.content?.rect.height:0} in {s.viewport?.rect.height:0})"))}");
            if (scrolls.Length > 0)
            {
                foreach (var scroll in scrolls) scroll.verticalNormalizedPosition = 0;
                yield return 0.5f;
                Shot($"settings-{i}-{tab.name}-bottom");
                yield return AfterShot;
            }

            var video = settings.GetComponentsInChildren<VideoSettingsTabPage>(false).FirstOrDefault();
            if (video?.ResolutionDropDown != null)
            {
                foreach (var scroll in scrolls) scroll.verticalNormalizedPosition = 1;
                video.ResolutionDropDown.Show();
                yield return 1f;
                Shot("dropdown-open");
                yield return AfterShot;
                var list = video.ResolutionDropDown.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Dropdown List")
                           ?? GameObject.Find("Dropdown List")?.transform;
                HierarchyDump.Write(list?.gameObject, Path.Combine(ShotsDir, "live-DropdownList.txt"));
                video.ResolutionDropDown.Hide();
                yield return 1f;
            }
        }
        HierarchyDump.Write(settings.gameObject, Path.Combine(ShotsDir, "live-SettingsMenu.txt"));
        settings.Close();
        yield return 1.5f;

        // The HUD: its prefab, if the menu list has one (a template without a world), then the save's live HUD.
        GameObject? hudPrefab = null;
        try
        {
            hudPrefab = manager.GetPrefab<PlayerHUDMenu>()?.gameObject;
        }
        catch (Exception e)
        {
            Log($"HUD prefab: {e.GetType().Name}: {e.Message}");
        }
        Log($"HUD prefab from the menu list: {(hudPrefab != null ? hudPrefab.name : "none")}");
        HierarchyDump.Write(hudPrefab, Path.Combine(ShotsDir, "prefab-PlayerHUDMenu.txt"));
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 6f; // the welcome banner goes, the bars settle
        var hud = Menu.GetInstance<PlayerHUDMenu>();
        Shot("hud");
        yield return AfterShot;
        HierarchyDump.Write(hud?.gameObject, Path.Combine(ShotsDir, "live-PlayerHUDMenu.txt"));
        LogHudWidgets(hud);
        Log("Research done");
        if (QuitWhenDone) Application.Quit();
    }

    // Which HUD widget holds which bar: PlayerWidget's five ProgressBars by field, and every widget with its state.
    private static void LogHudWidgets(PlayerHUDMenu? hud)
    {
        if (hud == null)
        {
            Log("HUD: no PlayerHUDMenu instance");
            return;
        }
        foreach (var widget in hud.GetComponentsInChildren<HUDWidget>(true))
            Log($"HUD widget {widget.GetIl2CppType().Name} '{PathOf(hud.transform, widget.transform)}' active={widget.gameObject.activeInHierarchy}");
        var player = hud.GetComponentsInChildren<PlayerWidget>(true).FirstOrDefault();
        if (player == null)
        {
            Log("HUD: no PlayerWidget");
            return;
        }
        foreach (var (field, bar) in new[] { ("healthBar", player.healthBar), ("staminaBar", player.staminaBar), ("foodBar", player.foodBar),
                                             ("waterBar", player.waterBar), ("warmthBar", player.warmthBar) })
        {
            Log($"PlayerWidget.{field} = '{(bar != null ? PathOf(hud.transform, bar.transform) : "-")}'" +
                (bar?.bar != null ? $": fill {bar.bar.type} {bar.bar.fillMethod} {bar.bar.fillAmount:0.###}" : ""));
        }
    }

    private static string PathOf(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (var at = t; at != null && at != root; at = at.parent) parts.Add(at.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // Keys of the game's own strings that mod settings can reuse, found by their text in the loaded languages.
    private static void FindLocalizationKeys()
    {
        string[] wanted =
        [
            "Вкл.", "Откл.", "Сбросить", "Закрыть", "Моды", "Применить", "По умолчанию", "Да", "Нет", "Отмена", "Настройки", "Назад",
            "On", "Off", "Reset", "Close", "Mods", "Apply", "Default", "Yes", "No", "Cancel", "Settings", "Back",
        ];
        var localization = LocalizationManager.Instance;
        foreach (var (name, strings) in new[] { ("game", localization?.LocalizedStrings), ("fallback", localization?.FallbackLocalizedStrings) })
        {
            if (strings == null)
            {
                Log($"Localization {name}: no strings");
                continue;
            }
            var found = new Dictionary<string, List<string>>();
            foreach (var pair in strings)
            {
                string text = pair.Value?.Trim() ?? "";
                if (Array.IndexOf(wanted, text) < 0) continue;
                if (!found.TryGetValue(text, out var keys)) found[text] = keys = [];
                keys.Add(pair.Key);
            }
            Log($"Localization {name}: {strings.Count} strings");
            foreach (var (text, keys) in found)
                Log($"  '{text}': {string.Join(", ", keys.OrderBy(k => k.Length).Take(8))}{(keys.Count > 8 ? $" (+{keys.Count - 8})" : "")}");
        }
    }
}

/// <summary>A GameObject tree with what matters for cloning: components, layout, sprites, fonts, control settings.</summary>
internal static class HierarchyDump
{
    private static readonly HashSet<string> s_plain = ["Transform", "RectTransform", "CanvasRenderer"];

    public static void Write(GameObject? root, string path)
    {
        if (root == null)
        {
            UiLab.Instance.Log.Warn($"Nothing to dump for {Path.GetFileName(path)}");
            return;
        }
        var text = new StringBuilder();
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // "0.5", not "0,5" next to the list commas
        try
        {
            Walk(root.transform, 0, text);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
        File.WriteAllText(path, text.ToString());
        UiLab.Instance.Log.Info($"Dumped {root.name} to {path}");
    }

    private static void Walk(Transform t, int depth, StringBuilder text)
    {
        string indent = new(' ', depth * 2);
        var components = t.GetComponents<Component>().Where(c => c != null).ToList();
        text.Append(indent).Append(t.name);
        if (!t.gameObject.activeSelf) text.Append(" (off)");
        text.Append(" [").Append(string.Join(", ", components.Select(c => c.GetIl2CppType().Name).Where(n => !s_plain.Contains(n)))).Append(']');
        var rect = t.TryCast<RectTransform>();
        if (rect != null) text.Append($" a{V(rect.anchorMin)}-{V(rect.anchorMax)} p{V(rect.pivot)} s{V(rect.sizeDelta)} @{V(rect.anchoredPosition)}");
        text.Append('\n');
        foreach (var c in components)
        {
            string? detail;
            try
            {
                detail = Detail(c);
            }
            catch (Exception e)
            {
                detail = $"{c.GetIl2CppType().Name}: {e.GetType().Name}";
            }
            if (detail != null) text.Append(indent).Append("  · ").Append(detail).Append('\n');
        }
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, text);
    }

    private static string? Detail(Component c)
    {
        if (c.TryCast<TMP_Text>() is { } tmp)
            return $"text \"{Short(tmp.text)}\" font {tmp.font?.name} {tmp.fontSize:0.#} {tmp.fontStyle} {C(tmp.color)} {tmp.alignment} autosize={tmp.enableAutoSizing}" +
                   (tmp.enableAutoSizing ? $" {tmp.fontSizeMin:0.#}..{tmp.fontSizeMax:0.#}" : "") + (tmp.enabled ? "" : " enabled=False") +
                   $" margin={tmp.margin.x:0},{tmp.margin.y:0},{tmp.margin.z:0},{tmp.margin.w:0}";
        if (c.TryCast<Image>() is { } image)
            return $"image {image.sprite?.name ?? "-"}{SpriteSize(image.sprite)} {image.type} {C(image.color)} raycast={image.raycastTarget}" +
                   (image.enabled ? "" : " enabled=False") +
                   (image.type == Image.Type.Sliced && image.pixelsPerUnitMultiplier != 1 ? $" ppu={image.pixelsPerUnitMultiplier:0.##}" : "") +
                   (image.type == Image.Type.Sliced && !image.fillCenter ? " fillCenter=False" : "") +
                   (image.sprite != null && image.sprite.pixelsPerUnit != 100 ? $" spritePpu={image.sprite.pixelsPerUnit:0.##}" : "") +
                   (image.maskable ? "" : " maskable=False") +
                   (image.type == Image.Type.Filled
                       ? $" fill {image.fillMethod} {image.fillAmount:0.###} origin={image.fillOrigin} cw={image.fillClockwise}" : "") +
                   (image.material != null && image.material.name != "Default UI Material"
                       ? $" material={image.material.name} shader={image.material.shader?.name}" : "");
        if (c.TryCast<SSSGame.ProgressBar>() is { } progress)
            return $"progressBar bar={progress.bar?.name} icon={progress.icon?.name} text={progress.text?.name} follow={progress.followBar?.name} " +
                   $"flashBelow={progress.FlashingThreshold} followMin={progress.FollowMinThreshHold} tween={progress.thresholdTween?.name} " +
                   $"range={progress._min}..{progress._max} units='{progress._units}' rangeSet={progress._rangeSet}";
        if (c.TryCast<Slider>() is { } slider)
            return $"slider {slider.minValue}..{slider.maxValue} whole={slider.wholeNumbers} value={slider.value} {slider.direction} " +
                   $"fill={slider.fillRect?.name} handle={slider.handleRect?.name} listeners={slider.onValueChanged.GetPersistentEventCount()}";
        if (c.TryCast<Toggle>() is { } toggle)
            return $"toggle on={toggle.isOn} graphic={toggle.graphic?.name} group={toggle.group?.name}";
        if (c.TryCast<Scrollbar>() is { } bar)
            return $"scrollbar {bar.direction} size={bar.size:0.##} handle={bar.handleRect?.name}";
        if (c.TryCast<TMP_Dropdown>() is { } dropdown)
            return $"dropdown options={dropdown.options.Count} caption={dropdown.captionText?.name} item={dropdown.itemText?.name} template={dropdown.template?.name}";
        if (c.TryCast<TMP_InputField>() is { } input)
            return $"input \"{Short(input.text)}\" text={input.textComponent?.name} placeholder={input.placeholder?.name} {input.contentType} " +
                   $"limit={input.characterLimit} viewport={input.textViewport?.name}";
        if (c.TryCast<Button>() is { } button)
            return $"button {button.transition} target={button.targetGraphic?.name} listeners={button.onClick.GetPersistentEventCount()}" +
                   string.Concat(Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                                           .Select(i => $" [{button.onClick.GetPersistentTarget(i)?.name}.{button.onClick.GetPersistentMethodName(i)}]"));
        if (c.TryCast<ScrollRect>() is { } scroll)
            return $"scroll content={scroll.content?.name} viewport={scroll.viewport?.name} h={scroll.horizontal} v={scroll.vertical} {scroll.movementType} " +
                   $"sensitivity={scroll.scrollSensitivity} vbar={scroll.verticalScrollbar?.name} {scroll.verticalScrollbarVisibility} spacing={scroll.verticalScrollbarSpacing}";
        if (c.TryCast<HorizontalOrVerticalLayoutGroup>() is { } layout)
            return $"layout spacing={layout.spacing} pad={P(layout.padding)} {layout.childAlignment} control={layout.childControlWidth}/{layout.childControlHeight} " +
                   $"expand={layout.childForceExpandWidth}/{layout.childForceExpandHeight}";
        if (c.TryCast<GridLayoutGroup>() is { } grid)
            return $"grid cell={V(grid.cellSize)} spacing={V(grid.spacing)} pad={P(grid.padding)} {grid.constraint} {grid.constraintCount}";
        if (c.TryCast<LayoutElement>() is { } element)
            return $"element min={element.minWidth}x{element.minHeight} pref={element.preferredWidth}x{element.preferredHeight} " +
                   $"flex={element.flexibleWidth}x{element.flexibleHeight} ignore={element.ignoreLayout}";
        if (c.TryCast<ContentSizeFitter>() is { } fitter)
            return $"fitter h={fitter.horizontalFit} v={fitter.verticalFit}";
        if (c.TryCast<IncreaseDecreasePanel>() is { } panel)
            return $"incdec {panel.MinValue}..{panel.MaxValue} step={panel.Step} value={panel.Value} cycle={panel.CanCycle} autoText={panel.AutomaticValueText} " +
                   $"valueText={panel.ValueText?.name} inc={panel.IncreaseButton?.name} dec={panel.DecreaseButton?.name} input={panel.InputField?.name} " +
                   $"hideEnds={panel.HideButtonsOnEnds} positions={panel.CreatePositionVisualizer} listeners={panel.OnValueChanged?.GetPersistentEventCount()}";
        if (c.TryCast<TabGroup>() is { } tabGroup)
            return $"tabGroup buttons={tabGroup.buttons?.Count} selected={tabGroup.selectedButton?.name} autoSelect={tabGroup.AutoSelectButton}";
        if (c.TryCast<TabButton>() is { } tabButton)
            return $"tabButton group={tabButton.group?.name} page={tabButton.page?.name} order={tabButton.order} hover={tabButton.TabSelectOnHover} selected={tabButton.selected}";
        if (c.TryCast<TabPage>() is { } tabPage)
            return $"tabPage title={tabPage.title?.name} subTitle={tabPage.subTitle?.name} autoPosition={tabPage.AutoPosition} left={tabPage.placeOnLeft} arrow={tabPage.indicatorArrow?.name}";
        if (c.TryCast<LocalizedText>() is { } localized)
            return $"loc key={localized.key}";
        if (c.TryCast<CanvasScaler>() is { } scaler)
            return $"scaler {scaler.uiScaleMode} ref={V(scaler.referenceResolution)} {scaler.screenMatchMode} match={scaler.matchWidthOrHeight:0.##} refPpu={scaler.referencePixelsPerUnit:0.##}";
        if (c.TryCast<Canvas>() is { } canvas)
            return $"canvas {canvas.renderMode} order={canvas.sortingOrder} override={canvas.overrideSorting}";
        if (c.TryCast<CanvasGroup>() is { } group)
            return $"group alpha={group.alpha} interactable={group.interactable} raycasts={group.blocksRaycasts}";
        if (c.TryCast<GamepadScrollBehaviour>() is { } gamepad)
            return $"gamepadScroll {gamepad.scrollMode} speed={gamepad.scrollSpeed} steps={gamepad.nrOfScrollSteps}";
        if (c.TryCast<Mask>() is { } mask)
            return $"mask showGraphic={mask.showMaskGraphic}";
        return null;
    }

    private static string Short(string? s)
    {
        s = (s ?? "").Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= 60 ? s : s[..60] + "…";
    }

    private static string V(Vector2 v) => $"({v.x:0.##}, {v.y:0.##})";

    // A sprite's size, and its 9-slice border when it has one.
    private static string SpriteSize(Sprite? sprite)
    {
        if (sprite == null) return "";
        var border = sprite.border;
        string sliced = border == Vector4.zero ? "" : $" border=({border.x:0},{border.y:0},{border.z:0},{border.w:0})";
        return $" {sprite.rect.width:0}x{sprite.rect.height:0}{sliced}";
    }

    private static string C(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    private static string P(RectOffset p) => $"{p.left},{p.right},{p.top},{p.bottom}";
}
