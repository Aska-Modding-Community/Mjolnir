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

/// <summary>Builds UI pieces from the game's own elements.</summary>
internal static class ViewFactory
{
    /// <summary>
    /// Makes a clone of a game element ours: removes localization (it would overwrite our text) and tab logic (it would
    /// switch the game's menu pages), and drops the persistent onClick listeners of every button in it.
    /// </summary>
    public static GameObject Strip(GameObject clone)
    {
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            string type = behaviour.GetIl2CppType().FullName;
            if (type.Contains("Localiz") || type.Contains("TabButton") || type.EndsWith("EventTrigger"))
                Object.DestroyImmediate(behaviour);
        }
        foreach (var button in clone.GetComponentsInChildren<Button>(true))
            button.onClick = new Button.ButtonClickedEvent();
        foreach (var slider in clone.GetComponentsInChildren<Slider>(true))
            slider.onValueChanged = new Slider.SliderEvent();
        foreach (var dropdown in clone.GetComponentsInChildren<TMP_Dropdown>(true))
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        foreach (var input in clone.GetComponentsInChildren<TMP_InputField>(true))
            input.onEndEdit = new TMP_InputField.SubmitEvent();
        return clone;
    }

    /// <summary>
    /// Removes only the localization of a clone (it would overwrite our text), on its own object or everything in it: for
    /// a clone that keeps the game's tab logic (a mod's tab in the game's windows).
    /// </summary>
    public static void StripLocalization(GameObject clone, bool children = true)
    {
        foreach (var behaviour in children ? clone.GetComponentsInChildren<MonoBehaviour>(true) : clone.GetComponents<MonoBehaviour>())
        {
            if (behaviour.GetIl2CppType().FullName.Contains("Localiz", StringComparison.Ordinal)) Object.DestroyImmediate(behaviour);
        }
    }

    /// <summary>A clone of a game button with our name, label and click handler.</summary>
    public static GameObject CloneButton(Button template, Transform parent, string name, string label, Action onClick)
    {
        var go = Strip(Object.Instantiate(template.gameObject, parent, false));
        go.name = name;
        go.GetComponent<Button>().onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onClick));
        SetLabel(go, label);
        go.SetActive(true);
        return go;
    }

    public static void SetLabel(GameObject button, string label)
    {
        var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.text = label;
    }

    public static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return rect;
    }

    public static void Stretch(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    public static TextMeshProUGUI NewText(Transform parent, string name, string text, TMP_FontAsset? font, float size)
    {
        var label = NewRect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    /// <summary>
    /// A scroll view: the settings menu's (its frame, background and scrollbar; the game's rows removed), or without it a
    /// plain one whose wheel still scrolls. The content is a column that grows with its children, the scrollbar shows
    /// only when the content is taller than the view.
    /// </summary>
    public static (RectTransform Area, ScrollRect Scroll, RectTransform Content) NewScrollView(RectTransform parent, string name, int padding, float spacing)
    {
        RectTransform area;
        ScrollRect scroll;
        RectTransform content;
        if (Theme.ScrollView != null)
        {
            var view = Object.Instantiate(Theme.ScrollView, parent, false);
            view.name = name;
            view.SetActive(true);
            area = view.GetComponent<RectTransform>();
            scroll = view.GetComponent<ScrollRect>();
            content = scroll.content;
            // Short content needs no scrollbar; its viewport then takes the scrollbar's place.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4;
            // The settings menu's scrollbar reaches 22 px below its area, as its viewport does in the prefab. The ScrollRect
            // fits the viewport to the area (it drives it while the scrollbar hides and shows), but drives the scrollbar's
            // height only beside a horizontal one: as cloned, the bar ran out under the frame. It ends with the area here.
            if (scroll.verticalScrollbar != null && scroll.verticalScrollbar.GetComponent<RectTransform>() is { } bar)
            {
                bar.sizeDelta = new Vector2(bar.sizeDelta.x, 0);
                bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, 0);
            }
        }
        else
        {
            area = NewRect(parent, name);
            scroll = area.gameObject.AddComponent<ScrollRect>();
            var viewport = NewRect(area, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = NewRect(viewport, "Content");
            content.gameObject.AddComponent<VerticalLayoutGroup>();
            content.gameObject.AddComponent<ContentSizeFitter>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
        }
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true; // children carry their height in a LayoutElement, texts in their preferred size
        layout.childForceExpandWidth = false; // stretched children are flexible, others keep their width (LayoutPlan)
        layout.childForceExpandHeight = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return (area, scroll, content);
    }

    /// <summary>
    /// A column on <paramref name="rect"/> whose children carry their own size (a LayoutElement, a text's preferred size)
    /// and are not stretched by the group: stretched elements are flexible instead (<see cref="LayoutPlan"/>).
    /// </summary>
    public static VerticalLayoutGroup Column(RectTransform rect, float spacing = 0, TextAnchor align = TextAnchor.UpperLeft) =>
        ChildSized(rect.gameObject.AddComponent<VerticalLayoutGroup>(), spacing, align);

    /// <summary>A row on <paramref name="rect"/>, its children sized as in <see cref="Column"/>.</summary>
    public static HorizontalLayoutGroup Row(RectTransform rect, float spacing = 0, TextAnchor align = TextAnchor.UpperLeft) =>
        ChildSized(rect.gameObject.AddComponent<HorizontalLayoutGroup>(), spacing, align);

    /// <summary>Sets an existing group as <see cref="Column"/> does (one the view takes over, of either direction).</summary>
    public static T ChildSized<T>(T group, float spacing = 0, TextAnchor align = TextAnchor.UpperLeft) where T : HorizontalOrVerticalLayoutGroup
    {
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        group.childAlignment = align;
        group.spacing = spacing;
        return group;
    }

    /// <summary>A layout group's padding for <paramref name="space"/> (negative sides as 0), each side plus <paramref name="extra"/>.</summary>
    public static RectOffset Offset(Thickness space, float extra = 0) =>
        new(Pixels(space.Left, extra), Pixels(space.Right, extra), Pixels(space.Top, extra), Pixels(space.Bottom, extra));

    /// <summary>TextMeshPro's margins for a <see cref="Control.Padding"/> (null: <paramref name="game"/>, the text's own).</summary>
    public static Vector4 TextMargin(Thickness? padding, Vector4 game) =>
        padding is { } p ? new Vector4((float)p.Left, (float)p.Top, (float)p.Right, (float)p.Bottom) : game;

    private static int Pixels(double value, float extra) => (int)Math.Round(Math.Max(0, value) + extra);

    /// <summary>Whether a GameObject with this name exists anywhere under <paramref name="root"/> (not only children).</summary>
    public static Transform? FindDeep(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
}
