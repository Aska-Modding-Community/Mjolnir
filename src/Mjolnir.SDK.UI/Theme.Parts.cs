using System;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio.Localization;
using SandSailorStudio.UI;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Context = SSSGame.InputContext.Context;
using GameInput = SSSGame.InputManager;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>How each of the game's parts is found and copied: by type and structure, never by position.</summary>
internal static partial class Theme
{
    // The confirm dialog: its chrome, fonts, sorting order and button.
    private static void LoadDialogParts()
    {
        try
        {
            var prefab = MenuManager.Instance?.GetPrefab<ConfirmActionMenu>();
            if (prefab == null)
            {
                SdkLog.Warn(GameUi.Source, "ConfirmActionMenu prefab not found: mod windows use plain colours");
                return;
            }
            var images = prefab.GetComponentsInChildren<Image>(true);
            var dialog = images.FirstOrDefault(i => i.name == "Window_Dark");
            DialogChrome = Keep(dialog?.transform);
            TitleFont = prefab.TitleRect?.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
            TextFont = prefab.MessageRect?.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
            var canvas = prefab.GetComponent<Canvas>();
            if (canvas != null) DialogSortingOrder = canvas.sortingOrder;

            var confirm = prefab.ConfirmButton?.GetComponentInChildren<Button>(true);
            if (confirm != null) DialogButton = Keep(confirm.transform)?.GetComponent<Button>();
            SdkLog.Info(GameUi.Source, $"Theme: window '{dialog?.sprite?.name}', fonts '{TitleFont?.name}'/'{TextFont?.name}', " +
                                       $"button {(DialogButton != null ? "found" : "missing")}, dialog sorting order {DialogSortingOrder}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Theme not loaded, mod windows use plain colours: {e}");
        }
    }

    // The main windows' chrome from the settings menu (its frame with corners, the X, what it puts behind itself, the
    // ornaments of its tab bar) and the inventory's header plate.
    private static void LoadWindowParts()
    {
        try
        {
            var settings = MenuManager.Instance?.GetPrefab<GameSettingsMenu>();
            if (settings != null)
            {
                var images = settings.GetComponentsInChildren<Image>(true);
                // The frame with its corner pieces: the image holding a Frame and the diamond of the X.
                MenuChrome = Keep(images.FirstOrDefault(i => i.transform.Find("Frame") != null && i.transform.Find("CornerClose") != null)?.transform);
                MenuCloseButton = Keep(settings.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "ButtonClose")?.transform);
                MenuBlur = Keep(images.FirstOrDefault(i => i.material != null && i.material.shader != null && i.material.shader.name.Contains("Blur"))?.transform);
                MenuVignette = Keep(images.FirstOrDefault(i => i.sprite != null && i.sprite.name.StartsWith("radial_shadow", StringComparison.Ordinal))?.transform);
                HeaderLeft = Keep(images.FirstOrDefault(i => i.name == "Ornament1")?.transform);
                HeaderRight = Keep(images.FirstOrDefault(i => i.name == "Ornament2")?.transform);
            }
            var inventory = MenuManager.Instance?.GetPrefab<InventoryMenu>();
            var plate = inventory?.GetComponentsInChildren<HorizontalLayoutGroup>(true)
                                 .FirstOrDefault(g => g.GetComponent<Image>() != null && g.GetComponentInParent<TabGroup>(true) != null);
            if (plate != null)
            {
                var copy = Keep(plate.transform);
                if (copy != null)
                {
                    for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
                    Object.DestroyImmediate(copy.GetComponent<HorizontalLayoutGroup>());
                    Object.DestroyImmediate(copy.GetComponent<ContentSizeFitter>());
                }
                HeaderPlate = copy;
            }
            SdkLog.Info(GameUi.Source, $"Window parts: frame {Has(MenuChrome)}, close {Has(MenuCloseButton)}, blur {Has(MenuBlur)}, vignette {Has(MenuVignette)}, " +
                                       $"ornaments {Has(HeaderLeft)}/{Has(HeaderRight)}, plate {Has(HeaderPlate)}, dialog {Has(DialogChrome)}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Window parts not loaded, mod windows use the dialog's look: {e}");
        }
    }

    // The journal (PlayerMenu): its icon tab (a TabButtonTooltip with an Icon), a list button (a tab with Icon, Title and
    // Frame, wider than tall) and the hover card (TooltipDetailsTabPage), found by type and structure.
    private static void LoadJournalParts()
    {
        try
        {
            var journal = MenuManager.Instance?.GetPrefab<PlayerMenu>();
            if (journal != null)
            {
                var tab = journal.GetComponentsInChildren<TabButtonTooltip>(true)
                                 .FirstOrDefault(t => t.transform.Find("Icon") != null && t.transform.Find("Frame_Selected") != null);
                IconTab = KeepPart(tab?.transform, "TabButton", "ItemHighlightBehaviour", "MouseSelect");
                var list = journal.GetComponentsInChildren<TabButton>(true)
                                  .FirstOrDefault(t => t.TryCast<TabButtonTooltip>() == null && t.transform.Find("Icon") != null &&
                                                       t.transform.Find("Title") != null && t.transform.Find("Frame") != null &&
                                                       t.GetComponent<RectTransform>() is { } r && r.sizeDelta.x > 2 * r.sizeDelta.y);
                ListTab = KeepPart(list?.transform, "TabButton", "ItemHighlightBehaviour", "MouseSelect");
                var card = journal.GetComponentsInChildren<TooltipDetailsTabPage>(true).FirstOrDefault();
                ToolTipCard = KeepPart(card?.transform, "TabPage", "PositionConstraint");
                // A plain slot first (no name beside it, no tier mark); in practice the inventory's (ItemInventoryThumb, 70×70).
                Slot = KeepSlot(journal.GetComponentsInChildren<ItemThumbnailPanel>(true)
                                       .Where(p => p.icon != null && p.count != null && p.background != null &&
                                                   p.GetComponent<RectTransform>() is { } r && r.sizeDelta.x <= 100)
                                       .OrderBy(p => (p.label != null ? 2 : 0) + (p.tier != null ? 1 : 0))
                                       .FirstOrDefault());
                Card = KeepCard(journal.GetComponentsInChildren<DeedCategoryPanel>(true).FirstOrDefault());
                ItemCard = KeepItemCard(journal.GetComponentsInChildren<JournalItemDetailsTabPage>(true)
                                               .FirstOrDefault()?.detailsPanel);
                // A category's panel: the sliced image stretched over its dropdown's Content, behind the tiles.
                var dropdown = journal.GetComponentsInChildren<DropdownElement>(true).FirstOrDefault(d => d.Content != null);
                var content = dropdown?.Content;
                JournalPanel = Keep(journal.GetComponentsInChildren<Image>(true)
                                          .FirstOrDefault(i => i.sprite != null && i.sprite.name == "UI_WindowDefault_Inventory" &&
                                                               Math.Abs(i.pixelsPerUnitMultiplier - 2) < 0.01f && i.transform.childCount > 0 &&
                                                               i.transform.GetChild(0).GetComponent<Image>()?.type == Image.Type.Tiled)?.transform);
                ExpanderPanel = ImageLook.Of(content?.GetComponentsInChildren<Image>(true)
                                                    .FirstOrDefault(i => i.type == Image.Type.Sliced && i.transform.parent == content.transform &&
                                                                         i.rectTransform.anchorMin == Vector2.zero && i.rectTransform.anchorMax == Vector2.one));
            }
            SdkLog.Info(GameUi.Source, $"Journal parts: icon tab {Has(IconTab)}, list button {Has(ListTab)}, hover card {Has(ToolTipCard)}, "
                                     + $"item card {Has(ItemCard)}, " +
                                       $"slot {Has(Slot)}, card {Has(Card)}, panel {(ExpanderPanel != null ? $"'{ExpanderPanel.Sprite?.name}' ppu {ExpanderPanel.Ppu}" : "MISSING")}, " +
                                       $"page panel {Has(JournalPanel)}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Journal parts not loaded, tabs and hover cards are plain: {e}");
        }
    }

    // A save-slot row of the main menu's «Load game» page (SaveSlotPanel), without the game's script and its texts.
    private static void LoadListRow()
    {
        s_listRowTried = true;
        try
        {
            var menu = MenuManager.Instance?.GetPrefab<SSSGame.MainMenu>() ?? Menu.GetInstance<SSSGame.MainMenu>();
            var slot = menu?.GetComponentsInChildren<SaveSlotPanel>(true).FirstOrDefault();
            if (slot == null)
            {
                s_listRowTried = false; // try again once a main menu is there
                return;
            }
            s_listRow = KeepPart(slot.transform, "SaveSlotPanel", "SelectableEvents", "UIEventWidgetUser");
            if (s_listRow != null)
            {
                foreach (string part in new[] { "SaveInfoGroup", "DeleteButton", "UploadToCloud", "Name" })
                {
                    if (s_listRow.transform.Find(part) is { } child) Object.DestroyImmediate(child.gameObject);
                }
            }
            SdkLog.Info(GameUi.Source, $"List row: {(s_listRow != null ? "yes" : "MISSING")}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"List row not loaded, list rows are plain: {e}");
        }
    }

    // An item slot: its parts found through the panel's fields and renamed by role, then the game's scripts removed (the
    // fill strip is the filled image on the picture, the game's decay or durability bar).
    private static GameObject? KeepSlot(ItemThumbnailPanel? source)
    {
        if (source == null) return null;
        var copy = Keep(source.transform);
        var panel = copy?.GetComponent<ItemThumbnailPanel>();
        if (copy == null || panel == null) return null;
        Rename(panel.background, "Background");
        Rename(panel.count, "Count");
        if (panel.count != null && panel.count.transform.parent != copy.transform) panel.count.transform.parent.name = "CountPanel";
        var fill = panel.icon?.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.type == Image.Type.Filled);
        if (fill != null)
        {
            fill.name = "Fill";
            fill.transform.parent.name = "Strip";
        }
        Rename(panel.icon, "Icon");
        // What a slot of ours never shows: the item's name and texts beside it, tier, cooking, knowledge, equipped, new.
        var strip = fill != null ? fill.transform.parent.gameObject : null;
        foreach (var part in new Component?[] { panel.label, panel.description, panel.lore, panel.availability, panel.cooked, panel.tier,
                                                panel.durability, panel.knowledgePercentage, panel.equipStatus, panel.newStatus })
        {
            if (part != null && part.gameObject != strip && part.transform != copy.transform) part.gameObject.SetActive(false);
        }
        // The tier marks of the game's items (a gold frame, the numeral in the corner) are not a plain slot's.
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (t.name is "Tier" or "TierPanelHolder") t.gameObject.SetActive(false);
        }
        RemoveScripts(copy, "ItemThumbnailPanel", "ItemHighlightBehaviour", "MouseSelect", "SubSelectable", "IconTextPairComponent", "TweenAnimator");
        return copy;
    }

    // A god card: its parts found through the panel's fields and renamed by role, the lore and progress lines hidden,
    // the game's scripts removed.
    private static GameObject? KeepCard(DeedCategoryPanel? source)
    {
        if (source == null) return null;
        var copy = Keep(source.transform);
        var panel = copy?.GetComponent<DeedCategoryPanel>();
        if (copy == null || panel == null) return null;
        Rename(panel.icon, "Image");
        Rename(panel.nameLabel, "Title");
        Rename(panel.descriptionLabel, "Subtitle");
        if (panel.loreLabel != null) panel.loreLabel.gameObject.SetActive(false);
        if (panel.progressLabel != null) panel.progressLabel.gameObject.SetActive(false);
        RemoveScripts(copy, "DeedCategoryPanel", "ItemHighlightBehaviour", "TweenAnimator");
        return copy;
    }

    // The game's item card: its ItemDetailsPanel stays — an ItemInfo given to it fills the whole card — while what
    // belongs to the journal's page goes. The card places itself beside the slot it was opened from, which the view
    // switches off; the inventory's key hints (use, drop, quick bar, split) mean nothing outside the inventory.
    private static GameObject? KeepItemCard(ItemDetailsPanel? source)
    {
        if (source == null) return null;
        var copy = Keep(source.transform);
        var panel = copy?.GetComponent<ItemDetailsPanel>();
        if (copy == null || panel == null) return null;
        panel.AutoPosition = false;
        if (panel.tooltipsParent != null) panel.tooltipsParent.SetActive(false);
        if (panel.indicatorArrow != null) panel.indicatorArrow.gameObject.SetActive(false);
        RemoveScripts(copy, "PositionConstraint", "TabPage");
        return copy;
    }

    private static void Rename(Component? part, string name)
    {
        if (part != null) part.gameObject.name = name;
    }

    private static void RemoveScripts(GameObject copy, params string[] scripts)
    {
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (scripts.Contains(behaviour.GetIl2CppType().Name)) Object.DestroyImmediate(behaviour);
        }
    }

    // A template that keeps the game's look but none of its behaviour: the named scripts go, by the end of their type name.
    private static GameObject? KeepPart(Transform? source, params string[] scripts)
    {
        var copy = Keep(source);
        if (copy == null) return null;
        foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true))
        {
            string type = behaviour.GetIl2CppType().Name;
            if (scripts.Any(s => type.EndsWith(s, StringComparison.Ordinal) || type.Contains(s, StringComparison.Ordinal))) Object.DestroyImmediate(behaviour);
        }
        return copy;
    }

    private static void LoadHudParts()
    {
        try
        {
            var hud = MenuManager.Instance?.GetPrefab<PlayerHUDMenu>();
            var player = hud?.GetComponentsInChildren<PlayerWidget>(true).FirstOrDefault();
            ProgressBar = KeepBar(player?.healthBar);
            ProgressRing = KeepRing(player?.foodBar);
            var prompt = hud?.GetComponentsInChildren<SSSGame.UI.GlyphDisplayComponent>(true)
                             .FirstOrDefault(g => g.KeyTargetText != null && g.NameTargetText != null);
            KeyHintKey = TextLook.Of(prompt?.KeyTargetText);
            KeyHintName = TextLook.Of(prompt?.NameTargetText);
            SdkLog.Info(GameUi.Source, $"HUD parts: progress bar {Has(ProgressBar)}, ring {Has(ProgressRing)}, " +
                                       $"key prompt {(KeyHintKey != null && KeyHintName != null ? "yes" : "MISSING")}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"HUD parts not loaded, progress bars are plain: {e}");
        }
    }

    // A ring's frame (FoodFrame: holder, background, the fill with the game's ProgressBar, the icon and its frame, the text),
    // its parts found through the component's fields and renamed by role; the game's components removed, as for a bar.
    private static GameObject? KeepRing(SSSGame.ProgressBar? ring)
    {
        var frame = ring?.transform.parent;
        if (ring?.bar == null || frame == null || ring.bar.transform.parent != frame) return null;
        int fill = ring.bar.transform.GetSiblingIndex();
        int icon = ring.icon != null && ring.icon.transform.parent == frame ? ring.icon.transform.GetSiblingIndex() : -1;
        int text = ring.text != null && ring.text.parent == frame ? ring.text.GetSiblingIndex() : -1;
        var copy = Keep(frame);
        if (copy == null) return null;
        foreach (var behaviour in copy.GetComponentsInChildren<SSSGame.ProgressBar>(true)) Object.DestroyImmediate(behaviour);
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour.GetIl2CppType().Name == "TweenAnimator") Object.DestroyImmediate(behaviour);
        }
        copy.transform.GetChild(fill).name = "Fill";
        if (icon >= 0)
        {
            copy.transform.GetChild(icon).name = "Icon";
            // The frame drawn around the icon (iconFrame…) is the next of the same size, if any.
            if (icon + 1 < copy.transform.childCount && copy.transform.GetChild(icon + 1).name.StartsWith("iconFrame", StringComparison.OrdinalIgnoreCase))
                copy.transform.GetChild(icon + 1).name = "IconFrame";
        }
        if (text >= 0) copy.transform.GetChild(text).name = "Text";
        return copy;
    }

    // A bar's frame (the parent of the game's ProgressBar), its parts found through the component's fields and renamed by
    // role, then the component removed: the copy is made under the inactive holder, so the game's Awake never ran on it.
    private static GameObject? KeepBar(SSSGame.ProgressBar? bar)
    {
        var frame = bar?.transform.parent;
        if (bar?.bar == null || frame == null || bar.bar.transform.parent != frame) return null;
        int fill = bar.bar.transform.GetSiblingIndex();
        int text = bar.text != null && bar.text.parent == frame ? bar.text.GetSiblingIndex() : -1;
        int trail = bar.followBar != null && bar.followBar.transform.parent == frame ? bar.followBar.transform.GetSiblingIndex() : -1;
        var copy = Keep(frame);
        if (copy == null) return null;
        foreach (var behaviour in copy.GetComponentsInChildren<SSSGame.ProgressBar>(true)) Object.DestroyImmediate(behaviour);
        copy.transform.GetChild(fill).name = "Fill";
        if (text >= 0) copy.transform.GetChild(text).name = "Text";
        if (trail >= 0)
        {
            var lagging = copy.transform.GetChild(trail);
            lagging.name = "Trail";
            lagging.gameObject.SetActive(false);
        }
        return copy;
    }

    private static void LoadSettingsParts()
    {
        try
        {
            var settings = MenuManager.Instance?.GetPrefab<GameSettingsMenu>();
            if (settings != null)
            {
                SliderRow = Keep(FindSliderRow(settings.transform));
                SelectorRow = Keep(FindSelectorRow(settings.transform));
                Arrow = SelectorRow == null ? null : ViewFactory.FindDeep(SelectorRow.transform, "Increase")?.GetComponentInChildren<Image>(true)?.sprite;
                DropdownRow = Keep(FindDropdownRow(settings.transform));
                FixCheckboxFrame(DropdownRow);
                FixDropdownList(DropdownRow);
                HeaderRow = Keep(FindHeaderRow(settings.transform));
                ScrollView = Keep(FindScrollView(settings.transform));
                var content = ScrollView?.GetComponent<ScrollRect>()?.content;
                if (content != null)
                {
                    // Only the frame is wanted: the game's own rows go.
                    for (int i = content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(content.GetChild(i).gameObject);
                }
            }
            var rename = MenuManager.Instance?.GetPrefab<InputFieldConfirmActionMenu>();
            InputField = Keep(rename?.InputFieldHelper?.transform);
            SdkLog.Info(GameUi.Source, $"Settings parts: slider {Has(SliderRow)}, selector {Has(SelectorRow)}, dropdown {Has(DropdownRow)}, " +
                                       $"header {Has(HeaderRow)}, scroll view {Has(ScrollView)}, text input {Has(InputField)}, " +
                                       $"arrow {Has(Arrow)}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Settings parts not loaded, mod windows use plain controls: {e}");
        }
    }

    // A settings row is a frame with a "NameLabel" text on the left and the control on the right.
    private static bool IsRow(Transform row) => row.Find("NameLabel")?.GetComponent<TMP_Text>() != null;

    // A slider row that shows its value ("valueLabel" next to the slider), like the camera field of view rows.
    private static Transform? FindSliderRow(Transform root) =>
        root.GetComponentsInChildren<Slider>(true).Select(s => s.transform.parent)
            .FirstOrDefault(r => r != null && IsRow(r) && r.Find("valueLabel") is { } value && value.gameObject.activeSelf);

    // A "‹ value ›" selector with position marks under the value, like the HUD filter rows.
    private static Transform? FindSelectorRow(Transform root) =>
        root.GetComponentsInChildren<IncreaseDecreasePanel>(true)
            .FirstOrDefault(p => p.CreatePositionVisualizer && p.ValueText != null && IsRow(p.transform))?.transform;

    private static Transform? FindDropdownRow(Transform root) =>
        root.GetComponentsInChildren<TMP_Dropdown>(true).Select(d => d.transform).FirstOrDefault(IsRow);

    // The game's list item draws its checkbox's frame a few pixels right of the box: in our copy of the template the
    // frame goes where the checkmark is, so it lies on the box.
    private static void FixCheckboxFrame(GameObject? row)
    {
        if (row == null) return;
        foreach (var part in row.GetComponentsInChildren<Transform>(true))
        {
            if (part.name != "Item CheckmarkFrame") continue;
            var frame = part.TryCast<RectTransform>();
            var mark = part.parent?.Find("Item Checkmark")?.TryCast<RectTransform>();
            if (frame == null) continue;
            frame.anchoredPosition = new Vector2(mark?.anchoredPosition.x ?? 0, frame.anchoredPosition.y);
        }
    }

    // The box of a dropdown lays its children out (VerticalLayoutGroup + ContentSizeFitter), and the open list is one
    // of them: the box grew to the list's height, so its dark background and frame were drawn round the whole list,
    // which sat inside them in the game's own narrower width (seen by the user, 16.11.2026). In our copy the list's
    // template is taken out of that layout, and the list TMP instantiates from it inherits that: the box keeps the
    // field's height. The template's anchors are left alone — TMP places the open list by them, and a stretched
    // template put the list over the field; its width follows the box in <c>DropdownView</c>.
    private static void FixDropdownList(GameObject? row)
    {
        var template = row?.GetComponent<TMP_Dropdown>()?.template;
        if (template == null) return;
        var element = template.GetComponent<LayoutElement>();
        if (element == null) element = template.gameObject.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
    }

    // A section title ("Camera Categ Label" → "Camera").
    private static Transform? FindHeaderRow(Transform root) =>
        root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name.Contains("Categ", StringComparison.Ordinal) && t.GetComponentInChildren<TMP_Text>(true) != null);

    // A settings page's scroll view: a vertical layout of rows and a scrollbar (not a dropdown's list).
    private static Transform? FindScrollView(Transform root) =>
        root.GetComponentsInChildren<ScrollRect>(true)
            .FirstOrDefault(s => s.verticalScrollbar != null && s.content != null && s.content.GetComponent<VerticalLayoutGroup>() != null &&
                                 s.GetComponentInParent<TMP_Dropdown>(true) == null)?.transform;

    // For the log lines: whether a part was found.
    private static string Has(Object? part) => part != null ? "yes" : "MISSING";

    // A hidden, stripped copy that the mod windows clone from.
    private static GameObject? Keep(Transform? source)
    {
        if (source == null) return null;
        var copy = ViewFactory.Strip(Object.Instantiate(source.gameObject, Holder, false));
        copy.name = source.name;
        copy.SetActive(true); // stays hidden under the inactive holder; clones come out active
        return copy;
    }
}
