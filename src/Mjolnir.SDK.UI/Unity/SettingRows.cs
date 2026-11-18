using System;
using System.Collections.Generic;
using System.Linq;
using SandSailorStudio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Helpers that make the game's settings rows (<see cref="Theme"/>) fit a mod's layout. A row is the label left and the
/// control right on the game's frame. The game lays its rows out for a fixed width (997), so a clone is re-anchored to
/// stretch: the label takes what the control leaves. A bare control is the same clone with the label and the frame
/// hidden, as wide as the control part.
/// </summary>
internal static class SettingRows
{
    public const float LabelInset = 27;        // the game's label inset in its settings rows
    public const float ContentWidth = 380;     // the right part of a row, where the game puts its controls
    private const float DescriptionHeight = 20; // added below the label of a row with a description
    public const int DescriptionLines = 3;      // a description wraps over at most so many lines, the row growing for them
    private const float IconSize = 48;          // a row's picture at most: what a row with a description has room for
    private const float IconPad = 6;            // above and below the picture
    public const float IconLeft = 14;           // from the row's left edge to the picture
    private const float IconGap = 12;           // from the picture to the label
    // The game's value-text mint, a little dimmer than the label: readable on the row's light and dark (hovered) states.
    private static readonly Color DescriptionColor = new(0.776f, 0.902f, 0.855f, 0.9f);

    /// <summary>
    /// A clone of <paramref name="template"/> as a row (with a label) or a bare control (label null);
    /// <paramref name="height"/> is the row's height, for its LayoutElement. The control part is
    /// <paramref name="contentWidth"/> wide (<see cref="HeaderedControl.ContentWidth"/>, <see cref="FitControl"/>). A row's
    /// <paramref name="padding"/> (<see cref="Control.Padding"/>): Left is where the label starts, Right keeps the control off
    /// the right edge, Top and Bottom make the row taller.
    /// </summary>
    public static GameObject NewRow(Transform parent, GameObject template, string name, string? label, string? description,
                                    out TMP_Text? labelText, out TMP_Text? descriptionText, out float height,
                                    float contentWidth = ContentWidth, Thickness? padding = null)
    {
        var row = Clone(template, parent, name);
        var size = template.GetComponent<RectTransform>().sizeDelta;
        height = size.y;
        FitControl(row.transform, size.x, contentWidth);
        var pad = padding ?? default;
        float left = padding.HasValue ? (float)pad.Left : LabelInset, right = (float)pad.Right;
        float top = (float)pad.Top, bottom = (float)pad.Bottom;
        labelText = row.transform.Find("NameLabel")?.GetComponent<TMP_Text>();
        descriptionText = null;
        if (label == null)
        {
            // Bare: only the control part is shown.
            for (int i = 0; i < row.transform.childCount; i++)
            {
                var child = row.transform.GetChild(i).TryCast<RectTransform>();
                if (child != null && (child.name == "NameLabel" || IsBackground(child))) child.gameObject.SetActive(false);
            }
            labelText = null;
            return row;
        }
        if (right > 0) ShiftControl(row.transform, -right);
        if (top + bottom > 0)
        {
            KeepControlHeights(row.transform, height); // the control keeps its height, centred in the taller row
            height += top + bottom;
        }
        if (labelText != null)
        {
            var rect = labelText.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-(contentWidth + right), -top);
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.text = label;
            if (!string.IsNullOrWhiteSpace(description))
            {
                KeepControlHeights(row.transform, size.y);
                height += DescriptionHeight;
                rect.offsetMin = new Vector2(left, bottom + DescriptionHeight - 4);
                descriptionText = Description(row.transform, description);
                var hint = descriptionText.rectTransform;
                hint.anchorMin = Vector2.zero;
                hint.anchorMax = new Vector2(1, 0);
                hint.pivot = Vector2.zero;
                hint.offsetMin = new Vector2(left, bottom + 6);
                hint.offsetMax = new Vector2(-(contentWidth + right), bottom + 6 + DescriptionHeight);
            }
        }
        return row;
    }

    /// <summary>
    /// Makes room for a picture left of a row's label (<see cref="HeaderedControl.Icon"/>): a square as tall as the row
    /// of <paramref name="height"/> allows, at most 48, with the label and the description moved right of it. Returns
    /// the picture and a centred text on it, for a letter tile.
    /// </summary>
    public static (Image Picture, TMP_Text Letter) AddIcon(Transform row, float height, TMP_Text? label, TMP_Text? description,
                                                          float left = IconLeft)
    {
        float size = Math.Min(IconSize, height - 2 * IconPad);
        var rect = ViewFactory.NewRect(row, "Icon");
        rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = new Vector2(left, 0);
        var picture = rect.gameObject.AddComponent<Image>();
        picture.raycastTarget = false;
        picture.preserveAspect = true;

        var letter = ViewFactory.NewText(rect, "Letter", "", Theme.TitleFont, Mathf.Round(size * 0.55f));
        ViewFactory.Stretch(letter.rectTransform);
        letter.color = new Color(1f, 1f, 1f, 0.92f);
        letter.raycastTarget = false;

        float inset = left + size + IconGap;
        foreach (var text in new[] { label, description })
        {
            if (text != null) text.rectTransform.offsetMin = new Vector2(inset, text.rectTransform.offsetMin.y);
        }
        return (picture, letter);
    }

    /// <summary>
    /// The left edge of a row's control part, as an anchor and an offset from it: those of its leftmost part that is not
    /// the label, the description, the picture or a background, compared at the game's row width
    /// <paramref name="templateWidth"/>. Something anchored there sits left of the control at any row width.
    /// </summary>
    public static (float Anchor, float Offset) ControlLeft(Transform row, float templateWidth)
    {
        float width = templateWidth > 0 ? templateWidth : 997;
        (float Anchor, float Offset) edge = (1, -ContentWidth);
        float leftmost = float.MaxValue;
        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).TryCast<RectTransform>();
            if (child == null || !child.gameObject.activeSelf || child.name is "NameLabel" or "Description" or "Icon" || IsBackground(child)) continue;
            float left = child.anchorMin.x * width + child.offsetMin.x;
            if (left >= leftmost) continue;
            leftmost = left;
            edge = (child.anchorMin.x, child.offsetMin.x);
        }
        return edge;
    }

    /// <summary>A row's control width: its <see cref="HeaderedControl.ContentWidth"/>, or the game's.</summary>
    public static float ContentWidthOf(HeaderedControl control) =>
        control.ContentWidth is var width && width > 0 && !double.IsInfinity(width) ? (float)width : ContentWidth;

    /// <summary>
    /// Moves the game's control parts of a fresh clone (not the label, the description, the picture or a background) from
    /// the template's control part (the right <see cref="ContentWidth"/> of a row <paramref name="templateWidth"/> wide) into
    /// one <paramref name="width"/> wide, anchored to the row's right edge: arrows and buttons keep their size, the widest
    /// parts (a selector's value and marks, a slider, a list's box) take the difference (<see cref="MapX"/>).
    /// </summary>
    public static void FitControl(Transform row, float templateWidth, float width)
    {
        if (Math.Abs(width - ContentWidth) < 0.5f) return;
        float rowWidth = templateWidth > 0 ? templateWidth : 997;
        var parts = new List<(RectTransform Rect, float Left, float Right)>();
        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).TryCast<RectTransform>();
            if (child == null || child.name is "NameLabel" or "Description" or "Icon" || IsBackground(child)) continue;
            // Edges as offsets from the row's right edge (negative), at the game's row width.
            float left = child.anchorMin.x * rowWidth + child.offsetMin.x - rowWidth;
            float right = child.anchorMax.x * rowWidth + child.offsetMax.x - rowWidth;
            parts.Add((child, left, right));
        }
        if (parts.Count == 0) return;
        var (stretchLeft, stretchRight) = Stretchy(parts.Select(p => (p.Left, p.Right)).ToList());
        float delta = width - ContentWidth;
        foreach (var (rect, left, right) in parts)
        {
            rect.anchorMin = new Vector2(1, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1, rect.anchorMax.y);
            rect.offsetMin = new Vector2(MapX(left, stretchLeft, stretchRight, delta), rect.offsetMin.y);
            rect.offsetMax = new Vector2(MapX(right, stretchLeft, stretchRight, delta), rect.offsetMax.y);
        }
    }

    /// <summary>Moves the control parts of a fresh clone sideways by <paramref name="dx"/> (a row's right padding).</summary>
    public static void ShiftControl(Transform row, float dx)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).TryCast<RectTransform>();
            if (child == null || child.name is "NameLabel" or "Description" or "Icon" || IsBackground(child)) continue;
            child.offsetMin = new Vector2(child.offsetMin.x + dx, child.offsetMin.y);
            child.offsetMax = new Vector2(child.offsetMax.x + dx, child.offsetMax.y);
        }
    }

    /// <summary>
    /// The span of the parts that take a change of width: those at least half as wide as the widest (a selector's value
    /// and its marks, not its arrows). No Unity: unit-tested.
    /// </summary>
    internal static (float Left, float Right) Stretchy(IReadOnlyList<(float Left, float Right)> parts)
    {
        float widest = parts.Max(p => p.Right - p.Left);
        var wide = parts.Where(p => p.Right - p.Left >= widest / 2).ToList();
        return (wide.Min(p => p.Left), wide.Max(p => p.Right));
    }

    /// <summary>
    /// Where an edge <paramref name="x"/> (an offset from the right edge) goes when the control part grows by
    /// <paramref name="delta"/>: right of the stretchy span it stays, left of it it moves by the whole change, inside it in
    /// proportion. No Unity: unit-tested.
    /// </summary>
    internal static float MapX(float x, float stretchLeft, float stretchRight, float delta)
    {
        if (x >= stretchRight) return x;
        if (x <= stretchLeft) return x - delta;
        float span = stretchRight - stretchLeft;
        return span <= 0 ? x : x - delta * (stretchRight - x) / span;
    }

    // The frame, tile, gradient and highlight images fill the row both ways; the controls do not.
    private static bool IsBackground(RectTransform rect) =>
        rect.anchorMin.x == 0 && rect.anchorMin.y == 0 && rect.anchorMax.x == 1 && rect.anchorMax.y == 1;

    // The game's controls stretch with their row (the selector's arrows, value and position marks). In a row made taller
    // for a description they keep the template's height, centred; backgrounds (stretched both ways) still fill the row.
    private static void KeepControlHeights(Transform row, float height)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).TryCast<RectTransform>();
            if (child == null || child.name == "NameLabel" || IsBackground(child)) continue;
            Vector2 min = child.anchorMin, max = child.anchorMax;
            if (min.y != 0 || max.y != 1) continue;
            float bottom = child.offsetMin.y;
            float size = height + child.offsetMax.y - bottom;
            child.anchorMin = new Vector2(min.x, 0.5f);
            child.anchorMax = new Vector2(max.x, 0.5f);
            child.sizeDelta = new Vector2(child.sizeDelta.x, size);
            child.anchoredPosition = new Vector2(child.anchoredPosition.x, bottom + child.pivot.y * size - height / 2);
        }
    }

    public static TMP_Text Description(Transform parent, string description)
    {
        var hint = ViewFactory.NewText(parent, "Description", FirstLine(description), Theme.TextFont, 14);
        hint.color = DescriptionColor;
        hint.alignment = TextAlignmentOptions.Left;
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.verticalAlignment = VerticalAlignmentOptions.Top;
        hint.raycastTarget = false;
        return hint;
    }

    /// <summary>
    /// How much taller a row grows for a description that wraps: its text <paramref name="preferred"/> tall at the width it
    /// has, one line <paramref name="line"/> tall, at most <see cref="DescriptionLines"/> lines (a longer one is cut with an
    /// ellipsis). No Unity: unit-tested.
    /// </summary>
    internal static float DescriptionGrowth(float preferred, float line)
    {
        if (!(line > 0) || !(preferred > line)) return 0;
        int lines = Math.Min(DescriptionLines, (int)Math.Ceiling(preferred / line - 0.25f));
        return Math.Max(0, lines - 1) * line;
    }

    public static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text.Trim() : text[..end].Trim() + "…";
    }

    public static GameObject Clone(GameObject template, Transform parent, string name)
    {
        var row = Object.Instantiate(template, parent, false);
        row.name = name;
        row.SetActive(true);
        return row;
    }

    /// <summary>The slider row without its slider is the base of the text, text-input and button rows.</summary>
    public static void RemoveControl(GameObject row)
    {
        var slider = row.GetComponentInChildren<Slider>(true);
        if (slider != null) Object.DestroyImmediate(slider.gameObject);
        var value = row.transform.Find("valueLabel");
        if (value != null) Object.DestroyImmediate(value.gameObject);
    }

    public static void PlaceRight(RectTransform rect, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(-LabelInset, 0);
    }

    /// <summary>Keeps the distance from the right edge it had in the game's row of the given width.</summary>
    public static void PinRight(RectTransform rect, float rowWidth)
    {
        float fromRight = rowWidth * (1 - rect.anchorMin.x) - rect.anchoredPosition.x;
        rect.anchorMin = new Vector2(1, rect.anchorMin.y);
        rect.anchorMax = new Vector2(1, rect.anchorMax.y);
        rect.anchoredPosition = new Vector2(-fromRight, rect.anchoredPosition.y);
    }

    /// <summary>Sets up the game's "‹ value ›" selector (IncreaseDecreasePanel) for a list of options.</summary>
    public static void SetupSelector(IncreaseDecreasePanel panel, IReadOnlyList<string> options)
    {
        panel.MinValue = 0;
        panel.MaxValue = Math.Max(0, options.Count - 1); // also rebuilds the position marks
        panel.Step = 1;
        panel.CanCycle = true;
        panel.AutomaticValueText = false;
    }

    /// <summary>A plain text line standing in for a control whose game template is missing (after a game update).</summary>
    public static TextMeshProUGUI Plain(Transform parent, string name)
    {
        var text = ViewFactory.NewText(parent, name, "", Theme.TextFont, 18);
        text.alignment = TextAlignmentOptions.Left;
        text.raycastTarget = false;
        return text;
    }
}
