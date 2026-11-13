using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A <see cref="UiWrapPanel"/>: a UGUI GridLayoutGroup, its cells the panel's item size or the largest child's own,
/// measured again as children come; as tall as its lines at the width its panel gives it.
/// </summary>
internal sealed class WrapPanelView : UnityView
{
    private readonly UiWrapPanel _panel;
    private readonly GridLayoutGroup _grid;

    public WrapPanelView(UiWrapPanel panel, RectTransform parent) : base(panel)
    {
        _panel = panel;
        Root = ViewFactory.NewRect(parent, DefaultName);
        _grid = Root.gameObject.AddComponent<GridLayoutGroup>();
        _grid.constraint = GridLayoutGroup.Constraint.Flexible;
    }

    protected override string DefaultName => "WrapPanel";

    public override Orientation ChildAxis => _panel.Orientation;

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        bool rows = _panel.Orientation == Orientation.Horizontal;
        _grid.startAxis = rows ? GridLayoutGroup.Axis.Horizontal : GridLayoutGroup.Axis.Vertical;
        float item = (float)Math.Max(0, _panel.ItemSpacing), line = (float)Math.Max(0, _panel.LineSpacing);
        _grid.spacing = rows ? new Vector2(item, line) : new Vector2(line, item);
        _grid.childAlignment = (_panel.ItemsAlignment, rows) switch
        {
            (WrapPanelItemsAlignment.Center, true) => TextAnchor.UpperCenter,
            (WrapPanelItemsAlignment.End, true) => TextAnchor.UpperRight,
            (WrapPanelItemsAlignment.Center, false) => TextAnchor.MiddleLeft,
            (WrapPanelItemsAlignment.End, false) => TextAnchor.LowerLeft,
            _ => TextAnchor.UpperLeft,
        };
        Measure();
    }

    public override void Place(IElementView child, int index)
    {
        base.Place(child, index);
        Measure();
    }

    // The cell: the panel's item size, else the largest child's preferred size (each child's LayoutElement holds it).
    private void Measure()
    {
        float width = (float)_panel.ItemWidth, height = (float)_panel.ItemHeight;
        if (float.IsNaN(width) || float.IsNaN(height))
        {
            float widest = 0, tallest = 0;
            for (int i = 0; i < Root.childCount; i++)
            {
                var child = Root.GetChild(i).TryCast<RectTransform>();
                if (child == null || !child.gameObject.activeSelf) continue;
                widest = Math.Max(widest, LayoutUtility.GetPreferredWidth(child));
                tallest = Math.Max(tallest, LayoutUtility.GetPreferredHeight(child));
            }
            if (float.IsNaN(width)) width = widest;
            if (float.IsNaN(height)) height = tallest;
        }
        _grid.cellSize = new Vector2(Math.Max(1, width), Math.Max(1, height));
    }
}

/// <summary>
/// A <see cref="UiSlot"/>: a clone of the game's item slot (Theme.Slot), its size: the picture in full colour (the template's
/// desaturating material removed), the count in the corner with its wing, the fill strip, and the slot's highlight in
/// amber while selected. Hover brightens the slot's background; a click goes to the element.
/// </summary>
internal sealed class SlotView : UnityView
{
    private const float PlainSize = 60;
    private readonly UiSlot _slot;
    private readonly float _size = PlainSize;
    private readonly Image? _icon, _fill;
    private readonly GameObject? _strip, _countPanel, _highlight;
    private readonly TMP_Text? _count;
    private readonly float _countSize;
    private Bitmap? _shownIcon;
    private bool _iconShown;
    private int? _shownCount = -1;
    private double? _shownFill = -1;
    private bool? _shownSelected;

    public SlotView(UiSlot slot, RectTransform parent) : base(slot)
    {
        _slot = slot;
        Image? background;
        if (Theme.Slot is { } template)
        {
            var go = Object.Instantiate(template, parent, false);
            go.name = DefaultName;
            go.SetActive(true);
            Root = RectOf(go);
            _size = Math.Max(Root.sizeDelta.x, 1);
            background = ViewFactory.FindDeep(Root, "Background")?.GetComponent<Image>();
            _icon = ViewFactory.FindDeep(Root, "Icon")?.GetComponent<Image>();
            _fill = ViewFactory.FindDeep(Root, "Fill")?.GetComponent<Image>();
            _strip = ViewFactory.FindDeep(Root, "Strip")?.gameObject;
            _count = ViewFactory.FindDeep(Root, "Count")?.GetComponent<TMP_Text>();
            _countPanel = ViewFactory.FindDeep(Root, "CountPanel")?.gameObject ?? _count?.gameObject;
            _highlight = ViewFactory.FindDeep(Root, "Highlight")?.gameObject;
            if (_icon != null) _icon.material = null; // the template greys its picture out (UIDesaturated)
            if (_highlight?.GetComponent<Image>() is { } frame) frame.color = SelectionColors.Amber;
        }
        else
        {
            Root = ViewFactory.NewRect(parent, DefaultName);
            background = Root.gameObject.AddComponent<Image>();
            background.color = new Color(0.506f, 0.596f, 0.565f, 1f); // the game's slot, #819890
            _icon = ViewFactory.NewRect(Root, "Icon").gameObject.AddComponent<Image>();
            ViewFactory.Stretch(_icon.rectTransform, 4);
            _count = ViewFactory.NewText(Root, "Count", "", Theme.TextFont, 12);
            _count.alignment = TextAlignmentOptions.BottomRight;
            ViewFactory.Stretch(_count.rectTransform, 3);
            _countPanel = _count.gameObject;
        }
        _countSize = _count != null ? _count.fontSize : 12;
        _highlight?.SetActive(false);
        Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
        Clickable(Root.gameObject, background, new Color(0.88f, 0.88f, 0.88f, 1f), new Color(0.72f, 0.72f, 0.72f, 1f), _slot.OnClick);
    }

    protected override string DefaultName => "Slot";

    protected override (float Width, float Height) LayoutDefaults => (_size, _size);

    protected override bool FixedSize => true;

    protected override void ApplyFont(double size)
    {
        if (_count != null) _count.fontSize = FontOf(size, _countSize, _countSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Image) != 0 && _icon != null && (!ReferenceEquals(_shownIcon, _slot.Icon) || _iconShown != (_slot.Icon != null)))
        {
            _shownIcon = _slot.Icon;
            var sprite = Pictures.SpriteOf(_slot.Icon);
            _iconShown = sprite != null;
            _icon.sprite = sprite;
            _icon.color = Color.white;
            _icon.preserveAspect = true;
            _icon.enabled = _iconShown;
        }
        if ((dirty & DirtyFlags.Text) != 0 && _shownCount != _slot.Count)
        {
            _shownCount = _slot.Count;
            if (_count != null && _slot.Count is { } count) _count.text = count.ToString(Localization.Culture);
            _countPanel?.SetActive(_slot.Count != null);
        }
        if ((dirty & DirtyFlags.Value) == 0) return;
        if (_shownFill != _slot.FillFraction)
        {
            _shownFill = _slot.FillFraction;
            if (_fill != null && _slot.FillFraction is { } fill) _fill.fillAmount = (float)fill;
            _strip?.SetActive(_slot.FillFraction != null);
        }
        if (_shownSelected != _slot.IsSelected)
        {
            _shownSelected = _slot.IsSelected;
            _highlight?.SetActive(_slot.IsSelected);
        }
    }

}

/// <summary>
/// A <see cref="UiCard"/>: a clone of the deeds page's god card (Theme.Card) as tall as its portrait, title and
/// subtitle, scaled as a whole by the element's Width (inside the view, which lays out the scaled size); the portrait's
/// orange highlight while selected. Hover brightens the card's background; a click goes to the element.
/// </summary>
internal sealed class CardView : UnityView
{
    private const float Bottom = 22;
    private readonly UiCard _card;
    private readonly RectTransform _frame;
    private readonly Image? _image;
    private readonly TMP_Text? _title, _subtitle;
    private readonly GameObject? _highlight;
    private readonly float _height, _titleSize, _subtitleSize;
    private Bitmap? _shownImage;
    private bool _imageShown;
    private string? _shownTitle, _shownSubtitle;
    private bool? _shownSelected;

    public CardView(UiCard card, RectTransform parent) : base(card)
    {
        _card = card;
        Root = ViewFactory.NewRect(parent, DefaultName);
        Image? background;
        if (Theme.Card is { } template)
        {
            var go = Object.Instantiate(template, Root, false);
            go.name = "Card";
            go.SetActive(true);
            _frame = RectOf(go);
            background = ViewFactory.FindDeep(_frame, "Background")?.GetComponent<Image>();
            _image = ViewFactory.FindDeep(_frame, "Image")?.GetComponent<Image>();
            _title = ViewFactory.FindDeep(_frame, "Title")?.GetComponent<TMP_Text>();
            _subtitle = ViewFactory.FindDeep(_frame, "Subtitle")?.GetComponent<TMP_Text>();
            _highlight = _image != null ? _image.transform.parent.Find("Highlight")?.gameObject : null;
            _height = ContentHeight(_frame);
        }
        else
        {
            _frame = ViewFactory.NewRect(Root, "Card");
            background = _frame.gameObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.13f, 0.13f, 0.95f);
            var column = _frame.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(12, 12, 22, 22);
            column.spacing = 15;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = false;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            _image = ViewFactory.NewRect(_frame, "Image").gameObject.AddComponent<Image>();
            _image.rectTransform.sizeDelta = new Vector2(163, 230);
            _title = ViewFactory.NewText(_frame, "Title", "", Theme.TitleFont, 25);
            _title.rectTransform.sizeDelta = new Vector2(156, 40);
            _subtitle = ViewFactory.NewText(_frame, "Subtitle", "", Theme.TextFont, 19);
            _subtitle.rectTransform.sizeDelta = new Vector2(156, 32);
            _height = 22 + 230 + 15 + 40 + 15 + 32 + Bottom;
        }
        _titleSize = _title != null ? _title.fontSize : 25;
        _subtitleSize = _subtitle != null ? _subtitle.fontSize : 19;
        _highlight?.SetActive(false);
        // The card at the top of the view, its own size, scaled from its top.
        _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 1);
        _frame.pivot = new Vector2(0.5f, 1);
        _frame.sizeDelta = new Vector2((float)UiCard.GameWidth, _height);
        _frame.anchoredPosition = Vector2.zero;
        Clickable(_frame.gameObject, background, new Color(0.9f, 0.9f, 0.9f, 1f), new Color(0.75f, 0.75f, 0.75f, 1f), _card.OnClick);
    }

    protected override string DefaultName => "Card";

    private float Scale => (float)_card.Scale;

    protected override (float Width, float Height) LayoutDefaults => ((float)UiCard.GameWidth * Scale, _height * Scale);

    protected override bool FixedSize => true;

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        _frame.localScale = new Vector3(Scale, Scale, 1);
    }

    protected override void ApplyFont(double size)
    {
        if (_title != null) _title.fontSize = FontOf(size, _titleSize, _titleSize);
        if (_subtitle != null) _subtitle.fontSize = FontOf(size, _subtitleSize, _titleSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Image) != 0 && _image != null && (!ReferenceEquals(_shownImage, _card.Image) || _imageShown != (_card.Image != null)))
        {
            _shownImage = _card.Image;
            var sprite = Pictures.SpriteOf(_card.Image);
            _imageShown = sprite != null;
            _image.sprite = sprite;
            _image.preserveAspect = true;
            _image.enabled = _imageShown;
        }
        if ((dirty & DirtyFlags.Text) != 0)
        {
            if (_title != null && _shownTitle != _card.Title) _title.text = _shownTitle = _card.Title;
            if (_subtitle != null && _shownSubtitle != _card.Subtitle)
            {
                _subtitle.text = _shownSubtitle = _card.Subtitle;
                _subtitle.gameObject.SetActive(_card.Subtitle.Length > 0);
            }
        }
        if ((dirty & DirtyFlags.Value) != 0 && _shownSelected != _card.IsSelected)
        {
            _shownSelected = _card.IsSelected;
            _highlight?.SetActive(_card.IsSelected);
        }
    }

    // The game's card is taller than what it shows (499 for a 265 portrait, its name and a line): the view is as tall as
    // its column's shown parts, its top padding and as much again below.
    private static float ContentHeight(RectTransform card)
    {
        var column = card.GetComponentInChildren<VerticalLayoutGroup>(true);
        if (column == null) return card.sizeDelta.y;
        float height = column.padding.top + Bottom;
        int shown = 0;
        var parent = column.transform;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i).TryCast<RectTransform>();
            if (child == null || !child.gameObject.activeSelf) continue;
            height += child.sizeDelta.y;
            shown++;
        }
        return height + column.spacing * Math.Max(0, shown - 1);
    }
}
