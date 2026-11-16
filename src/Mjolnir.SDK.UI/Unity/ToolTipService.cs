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

    /// <summary>Half the box the card keeps clear round the pointer, in reference pixels.</summary>
    private const float PointerReach = 10;

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
        // A card that brings a panel of its own settles its size a frame later (its texts measure themselves), and
        // the element under the pointer may move: where the card stands is worked out again while it shows.
        else if (s_shown != null) GameUi.Guard("tooltip placement", () => Place(hovered, s_shown));
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
            if (!RectTransformUtility.RectangleContainsScreenPoint(AreaOf(view), pointer, null)) continue;
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

    // What the card belongs to: a row's picture when the row says so, else the whole element.
    private static RectTransform AreaOf(UnityView view) =>
        view.Element is HeaderedControl { ToolTipOnIcon: true } && view.IconArea is { } icon && icon != null ? icon : view.Root;

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
        s_shown = card;
        Place(view, card);
    }

    // Where the card stands: beside what it belongs to, on the side with room for it.
    private static void Place(UnityView view, Card card)
    {
        if (view.Root == null || card.Root == null) return;
        var canvas = view.Root.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(card.Root);
        // On an overlay canvas, world coordinates are screen pixels.
        var min = view.Root.TransformPoint(view.Root.rect.min);
        var max = view.Root.TransformPoint(view.Root.rect.max);
        float scale = canvas.scaleFactor;
        var size = card.Root.rect.size * scale;
        // A card that belongs to a row's picture stands beside the picture, as the game's own item card stands
        // beside its slot. Any other card stands beside the pointer: beside the whole element it would land at the
        // far edge of a row as wide as the window. Without a pointer the element itself is what it stands beside.
        var own = AreaOf(view);
        var at = GameKeyboard.PointerPosition();
        float reach = PointerReach * scale;
        (float Left, float Bottom, float Right, float Top) area;
        if (!ReferenceEquals(own, view.Root))
        {
            var iconMin = own.TransformPoint(own.rect.min);
            var iconMax = own.TransformPoint(own.rect.max);
            area = (iconMin.x, iconMin.y, iconMax.x, iconMax.y);
        }
        else area = at is { } p ? (p.x - reach, p.y - reach, p.x + reach, p.y + reach) : (min.x, min.y, max.x, max.y);
        var (x, y, right) = ToolTips.Place(area, (size.x, size.y), (Screen.width, Screen.height), Gap * scale);
        card.Root.pivot = new Vector2(right ? 0 : 1, 0.5f);
        card.Root.position = new UnityEngine.Vector3(x, y, card.Root.position.z);
        card.PointArrow(right);
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
        private readonly List<Image> _chrome = [];
        private readonly LayoutElement? _frame;
        private readonly HorizontalOrVerticalLayoutGroup? _stack;
        private readonly RectOffset? _padding;
        private readonly float _frameWidth;
        private readonly TipHost _host;
        private Control? _mounted;

        private Card(RectTransform root, TMP_Text? title, TMP_Text? text, RectTransform? arrow)
        {
            Root = root;
            _title = title;
            _text = text;
            _arrow = arrow;
            // The card's own frame, taken before the host's objects exist: a tip that brings its own panel (an item
            // card) is shown bare, or the game's card would draw a second frame round it.
            foreach (var image in root.GetComponentsInChildren<Image>(true)) _chrome.Add(image);
            // The frame's own width and padding, for the same reason: a bare card is as wide as what it holds.
            _frame = root.GetComponent<LayoutElement>();
            _frameWidth = _frame != null ? _frame.preferredWidth : -1;
            _stack = root.GetComponent<VerticalLayoutGroup>();
            _padding = _stack != null ? _stack.padding : null;
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
                Chrome(element is not UiItemCard);
                _title?.gameObject.SetActive(false);
                _text?.gameObject.SetActive(false);
                _content.gameObject.SetActive(true);
                _mounted = element;
                GameUi.Guard($"{element} tooltip", () =>
                {
                    var node = _host.Mount(element, _contentView);
                    // A tip that brings a panel of its own sets the width too: the game's card is 180 wide inside,
                    // and a narrower box would leave the panel hanging out of what the card is placed by.
                    float wide = node.View is UnityView view ? view.Natural.Width : -1;
                    _content.sizeDelta = new Vector2(Math.Max(180, wide), 0);
                });
                return;
            }
            Chrome(true);
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

        // The card's own frame and arrow, on or off. Without them it is only what it holds: no padding round it
        // and no width of its own, or the panel inside would sit in a box larger than itself.
        private void Chrome(bool shown)
        {
            foreach (var image in _chrome)
            {
                if (image != null) image.enabled = shown;
            }
            if (_arrow != null) _arrow.gameObject.SetActive(shown);
            if (_frame != null) _frame.preferredWidth = shown ? _frameWidth : -1;
            if (_stack != null && _padding != null) _stack.padding = shown ? _padding : new RectOffset(0, 0, 0, 0);
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
