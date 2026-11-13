using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The hover cards of <see cref="Control.ToolTip"/>. Hover is checked once a frame while an element with a tooltip is on
/// screen: the pointer inside the element's rectangle, and the topmost UI object under it the element's own or one on
/// the element's canvas (a window or dialog over it hides the card). No pointer component is added to the elements, so
/// scrolling and dragging pass through as before. After <see cref="ToolTips.Delay"/> the game's hover card (the journal's
/// TabDetails, one per canvas) shows beside the innermost such element, and goes when the pointer leaves it.
/// </summary>
internal static class ToolTipService
{
    private const float Gap = 8;
    private static readonly List<UnityView> s_views = [];
    private static readonly Dictionary<IntPtr, Card> s_cards = [];
    private static readonly Action s_tick = Tick;
    private static UnityView? s_hovered;
    private static float s_since;
    private static Card? s_shown;

    /// <summary>A view whose element has a tooltip (or no longer has one, or is gone).</summary>
    public static void Track(UnityView view, bool on)
    {
        if (on == s_views.Contains(view)) return;
        if (on) s_views.Add(view);
        else if (s_views.Remove(view) && ReferenceEquals(view, s_hovered))
        {
            Hide();
            s_hovered = null;
        }
        LiveTree.EveryFrame(s_tick, s_views.Count > 0);
    }

    private static void Tick()
    {
        var hovered = Hovered();
        if (!ReferenceEquals(hovered, s_hovered))
        {
            Hide();
            s_hovered = hovered;
            s_since = Time.unscaledTime;
        }
        if (hovered == null) return;
        if (s_shown == null && Time.unscaledTime - s_since >= ToolTips.Delay) Show(hovered);
        else if (s_shown != null && !ReferenceEquals(s_shown.Tip, hovered.Element.ToolTip)) Show(hovered); // the tip changed
    }

    // The innermost tracked element under the pointer that nothing from another canvas covers.
    private static UnityView? Hovered()
    {
        if (GameKeyboard.PointerPosition() is not { } pointer) return null;
        // The raycast (every UI object under the pointer) only when the pointer is over a shown element with a tip.
        GameObject? top = null;
        bool cast = false;
        UnityView? best = null;
        int bestDepth = -1;
        foreach (var view in s_views)
        {
            var root = view.Root;
            if (root == null || !root.gameObject.activeInHierarchy || view.Element.ToolTip == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(root, pointer, null)) continue;
            if (!cast)
            {
                top = TopObjectAt(pointer);
                cast = true;
            }
            if (top != null && !top.transform.IsChildOf(root) && !SameCanvas(top.transform, root)) continue;
            int depth = Depth(root);
            if (depth > bestDepth)
            {
                best = view;
                bestDepth = depth;
            }
        }
        return best;
    }

    private static GameObject? TopObjectAt(Vector2 pointer)
    {
        var events = EventSystem.current;
        if (events == null) return null;
        var data = new PointerEventData(events) { position = pointer };
        var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        events.RaycastAll(data, results);
        return results.Count > 0 ? results[0].gameObject : null;
    }

    private static bool SameCanvas(Transform a, Transform b) =>
        a.GetComponentInParent<Canvas>()?.rootCanvas is { } ca && b.GetComponentInParent<Canvas>()?.rootCanvas is { } cb && ca.Pointer == cb.Pointer;

    private static int Depth(Transform t)
    {
        int depth = 0;
        for (var at = t; at != null; at = at.parent) depth++;
        return depth;
    }

    private static void Show(UnityView view)
    {
        Hide();
        var canvas = view.Root.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null || CardOn(canvas) is not { } card) return;
        card.Fill(view.Element.ToolTip);
        card.Root.gameObject.SetActive(true);
        card.Root.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(card.Root);
        // On an overlay canvas, world coordinates are screen pixels.
        var min = view.Root.TransformPoint(view.Root.rect.min);
        var max = view.Root.TransformPoint(view.Root.rect.max);
        float scale = canvas.scaleFactor;
        var size = card.Root.rect.size * scale;
        var (x, y, right) = ToolTips.Place((min.x, min.y, max.x, max.y), (size.x, size.y), (Screen.width, Screen.height), Gap * scale);
        card.Root.pivot = new Vector2(right ? 0 : 1, 0.5f);
        card.Root.position = new UnityEngine.Vector3(x, y, card.Root.position.z);
        card.PointArrow(right);
        s_shown = card;
    }

    private static void Hide()
    {
        if (s_shown != null) s_shown.Close();
        s_shown = null;
    }

    private static Card? CardOn(Canvas canvas)
    {
        if (s_cards.TryGetValue(canvas.Pointer, out var card) && card.Root != null) return card;
        foreach (var dead in new List<IntPtr>(s_cards.Keys))
        {
            if (s_cards[dead].Root == null) s_cards.Remove(dead);
        }
        card = Card.Create(canvas.transform);
        if (card != null) s_cards[canvas.Pointer] = card;
        return card;
    }

    /// <summary>One canvas's hover card: the game's TabDetails (Title, Text), or a plain one, and a host for an element tip.</summary>
    private sealed class Card
    {
        private readonly TMP_Text? _title;
        private readonly TMP_Text? _text;
        private readonly RectTransform _content;
        private readonly RectTransform? _arrow;
        private readonly TipHost _host;
        private Control? _mounted;

        private Card(RectTransform root, TMP_Text? title, TMP_Text? text, RectTransform? arrow)
        {
            Root = root;
            _title = title;
            _text = text;
            _arrow = arrow;
            _content = ViewFactory.NewRect(root, "Content");
            _content.sizeDelta = new Vector2(180, 0); // the game's card does not size its children's width
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ViewFactory.Column(_content);
            _host = new TipHost(root);
            _contentView = new ContainerView(_content);
        }

        private readonly ContainerView _contentView;

        public RectTransform Root { get; }

        public object? Tip { get; private set; }

        public static Card? Create(Transform canvas)
        {
            RectTransform root;
            TMP_Text? title = null, text = null;
            RectTransform? arrow = null;
            if (Theme.ToolTipCard is { } template)
            {
                var go = Object.Instantiate(template, canvas, false);
                root = go.GetComponent<RectTransform>();
                title = root.Find("Title")?.GetComponent<TMP_Text>();
                text = root.Find("Text (TMP)")?.GetComponent<TMP_Text>() ?? FirstOtherText(root, title);
                arrow = root.Find("Arrow")?.GetComponent<RectTransform>();
            }
            else
            {
                root = ViewFactory.NewRect(canvas, "ToolTip");
                var image = root.gameObject.AddComponent<Image>();
                image.color = new Color(0.08f, 0.09f, 0.09f, 0.95f);
                var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                title = ViewFactory.NewText(root, "Title", "", Theme.TextFont, 14);
                title.color = SelectionColors.Amber;
                text = ViewFactory.NewText(root, "Text", "", Theme.TextFont, 14);
            }
            root.name = "Mjolnir_ToolTip";
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            // The card never takes the pointer: under it, the element would lose its hover and the card would blink.
            var group = root.gameObject.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            if (title != null) title.alignment = TextAlignmentOptions.Left;
            if (text != null) text.alignment = TextAlignmentOptions.TopLeft;
            root.gameObject.SetActive(false);
            return new Card(root, title, text, arrow);
        }

        private static TMP_Text? FirstOtherText(Transform root, TMP_Text? title)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (title == null || t.Pointer != title.Pointer) return t;
            }
            return null;
        }

        public void Fill(object? tip)
        {
            Tip = tip;
            Unmount();
            if (tip is Control element)
            {
                _title?.gameObject.SetActive(false);
                _text?.gameObject.SetActive(false);
                _content.gameObject.SetActive(true);
                _mounted = element;
                GameUi.Guard($"{element} tooltip", () => _host.Mount(element, _contentView));
                return;
            }
            _content.gameObject.SetActive(false);
            var (title, text) = ToolTips.Split(tip?.ToString() ?? "");
            if (_title != null)
            {
                _title.gameObject.SetActive(true);
                _title.text = title;
            }
            if (_text != null)
            {
                _text.gameObject.SetActive(text != null);
                _text.text = text ?? "";
            }
        }

        public void PointArrow(bool cardOnRight)
        {
            if (_arrow == null) return;
            // The game's arrow sits on the card's left edge, pointing left at its tab.
            _arrow.anchorMin = _arrow.anchorMax = new Vector2(cardOnRight ? 0 : 1, 0.5f);
            _arrow.localScale = new UnityEngine.Vector3(cardOnRight ? 1 : -1, 1, 1);
        }

        public void Close()
        {
            Unmount();
            if (Root != null) Root.gameObject.SetActive(false);
            Tip = null;
        }

        private void Unmount()
        {
            if (_mounted == null) return;
            var element = _mounted;
            _mounted = null;
            GameUi.Guard($"{element} tooltip", () => _host.Unmount(element));
        }
    }

    /// <summary>An element tip's live copy: alive while the card exists, live while it shows.</summary>
    private sealed class TipHost(RectTransform card) : ViewHost(UnityBackend.Instance)
    {
        public override bool IsAlive => card != null;

        public override bool IsActive => card != null && card.gameObject.activeInHierarchy;
    }
}
