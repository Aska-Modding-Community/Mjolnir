using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using SandSailorStudio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>A column or row: a layout group. A window's root column takes over the scroll content instead of nesting.</summary>
internal sealed class PanelView : UnityView
{
    private readonly UiStackPanel _panel;
    private readonly HorizontalOrVerticalLayoutGroup _group;
    private readonly bool _adopted;
    private readonly float _extraPadding;
    private readonly Orientation _orientation;

    public PanelView(UiStackPanel panel, RectTransform parent, bool adopt, ViewHost host) : base(panel)
    {
        _panel = panel;
        _adopted = adopt;
        _orientation = panel.Orientation; // a new orientation rebuilds the view
        if (adopt)
        {
            Root = parent;
            _group = parent.GetComponent<VerticalLayoutGroup>() ?? (HorizontalOrVerticalLayoutGroup)parent.gameObject.AddComponent<VerticalLayoutGroup>();
            _extraPadding = host.RootPadding;
        }
        else
        {
            Root = ViewFactory.NewRect(parent, DefaultName);
            _group = _orientation == Orientation.Vertical
                ? Root.gameObject.AddComponent<VerticalLayoutGroup>()
                : Root.gameObject.AddComponent<HorizontalLayoutGroup>();
        }
        // Every child carries its size in a LayoutElement (or its text's preferred size); stretched children are flexible.
        ViewFactory.ChildSized(_group);
    }

    public override Orientation ChildAxis => _orientation;

    protected override string DefaultName => "StackPanel";

    protected override void ApplyLayout()
    {
        var margin = new Thickness();
        if (_adopted) margin = _panel.Margin; // the window's own padding takes the root column's margin
        else base.ApplyLayout();
        _group.padding = ViewFactory.Offset(margin, _extraPadding);
        _group.spacing = (float)_panel.Spacing;
        // Children aligned Center sit in the middle across the panel; along it they start at the beginning.
        _group.childAlignment = _orientation == Orientation.Vertical ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
    }

    public override void Dispose()
    {
        if (_adopted) return; // the window's scroll content stays
        base.Dispose();
    }
}

/// <summary>A user control or an items control: a column holding its content (or its items' panel).</summary>
internal sealed class ContentView : UnityView
{
    public ContentView(Control element, RectTransform parent) : base(element)
    {
        Root = ViewFactory.NewRect(parent, DefaultName);
        ViewFactory.Column(Root, align: TextAnchor.UpperCenter);
    }

    protected override string DefaultName => Element.GetType().Name;
}

/// <summary>A border: padding around its child, an optional plain background.</summary>
internal sealed class BorderView : UnityView
{
    private readonly UiBorder _border;
    private readonly VerticalLayoutGroup _group;
    private Image? _background;

    public BorderView(UiBorder border, RectTransform parent) : base(border)
    {
        _border = border;
        Root = ViewFactory.NewRect(parent, DefaultName);
        _group = ViewFactory.Column(Root, align: TextAnchor.UpperCenter);
    }

    protected override string DefaultName => "Border";

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        _group.padding = ViewFactory.Offset(_border.Padding ?? default);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Style) == 0) return;
        var color = _border.Background;
        if (color == null && _background == null) return;
        _background ??= Root.gameObject.AddComponent<Image>();
        _background.raycastTarget = false;
        _background.enabled = color != null;
        if (color is { } c) _background.color = c;
    }
}

/// <summary>
/// A scaled child (<see cref="UiLayoutTransformControl"/>): this view's rect takes the width its panel gives; inside it a rect
/// laid out for that width divided by the scale (a column that fits its content's height) is drawn at the scale, and this
/// view prefers the scaled height. Checked every frame, since the child's height follows its content.
/// </summary>
internal sealed class LayoutTransformView : UnityView
{
    private readonly UiLayoutTransformControl _control;
    private readonly RectTransform _inner;
    private readonly LayoutElement _size;
    private readonly Action _check;
    private Vector2 _shown = new(-1, -1);

    public LayoutTransformView(UiLayoutTransformControl control, RectTransform parent) : base(control)
    {
        _control = control;
        Root = ViewFactory.NewRect(parent, DefaultName);
        _inner = ViewFactory.NewRect(Root, "Scaled");
        _inner.anchorMin = _inner.anchorMax = new Vector2(0, 1);
        _inner.pivot = new Vector2(0, 1);
        ViewFactory.Column(_inner, align: TextAnchor.UpperCenter);
        var fit = _inner.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _size = Root.gameObject.AddComponent<LayoutElement>();
        _check = Follow;
        LiveTree.EveryFrame(_check, true);
    }

    public override RectTransform Container => _inner;

    protected override string DefaultName => "LayoutTransform";

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        float scale = (float)_control.Scale;
        _inner.localScale = new Vector3(scale, scale, 1);
        _shown = new Vector2(-1, -1);
        Follow();
    }

    // The child laid out for the width this view got, its height made room for once scaled.
    private void Follow()
    {
        if (Root == null) return;
        double scale = _control.Scale;
        float width = UiLayoutTransformControl.ChildWidth(Root.rect.width, scale);
        float height = UiLayoutTransformControl.OwnHeight(_inner.rect.height, scale);
        var now = new Vector2(width, height);
        if (now == _shown) return;
        _shown = now;
        _inner.sizeDelta = new Vector2(width, _inner.sizeDelta.y);
        if (double.IsNaN(_control.Height)) _size.preferredHeight = height;
    }

    public override void Dispose()
    {
        LiveTree.EveryFrame(_check, false);
        base.Dispose();
    }
}

/// <summary>
/// An expander: a settings row as its clickable header (the row's hover and sounds kept), the game's selector arrow on
/// the right (pointing right while closed, down while open), and a column below for the content, hidden while closed.
/// </summary>
internal sealed class ExpanderView : UnityView
{
    private readonly UiExpander _expander;
    private readonly GameObject _body;
    private readonly RectTransform _content;
    private readonly TMP_Text? _label;
    private readonly float _headerSize = 18;
    private readonly RectTransform? _arrow;
    private string? _shownHeader;
    private bool? _shownExpanded;

    public ExpanderView(UiExpander expander, RectTransform parent) : base(expander)
    {
        _expander = expander;
        Root = ViewFactory.NewRect(parent, DefaultName);
        // The panel starts under the header, as the journal's.
        ViewFactory.Column(Root, Theme.ExpanderPanel != null ? 0 : 8, TextAnchor.UpperCenter);

        GameObject header;
        float height = 40;
        if (Theme.SliderRow != null)
        {
            header = SettingRows.NewRow(Root, Theme.SliderRow, "Header", "", null, out _label, out _, out height);
            SettingRows.RemoveControl(header);
            if (_label != null) _label.rectTransform.offsetMax = new Vector2(-48, 0); // up to the arrow
        }
        else
        {
            var text = ViewFactory.NewText(Root, "Header", "", Theme.TitleFont, 20);
            text.alignment = TextAlignmentOptions.Left;
            header = text.gameObject;
            _label = text;
        }
        if (_label != null) _headerSize = _label.fontSize;
        var size = header.GetComponent<LayoutElement>() ?? header.AddComponent<LayoutElement>();
        size.preferredHeight = height;
        size.flexibleWidth = 1;

        if (Theme.Arrow != null)
        {
            _arrow = ViewFactory.NewRect(header.transform, "Arrow");
            var image = _arrow.gameObject.AddComponent<Image>();
            image.sprite = Theme.Arrow;
            image.preserveAspect = true;
            image.raycastTarget = false;
            SettingRows.PlaceRight(_arrow, 18, 18);
            // Turned about its middle, in the same place: about PlaceRight's pivot on its right edge, a turn moved the
            // arrow up while closed and out to the right while open (seen in game, 2026-10-23).
            _arrow.pivot = new Vector2(0.5f, 0.5f);
            _arrow.anchoredPosition -= new Vector2(9, 0);
        }

        // A clear area over the whole row takes the clicks: the row already has a Selectable of the game's (its hover and
        // gamepad selection), and Unity allows one per object. Hover still reaches the row (enter and exit go up).
        var hit = ViewFactory.NewRect(header.transform, "Hit");
        ViewFactory.Stretch(hit);
        hit.SetAsLastSibling();
        hit.gameObject.AddComponent<Image>().color = Color.clear;
        var button = hit.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => GameUi.Guard($"{expander} click", expander.Toggle))));
        Listen(button.onClick);

        // The body on the journal's panel under a category's header (its tiles 10 from the edge; texts a little more),
        // else indented under the header. The panel is a child the layout ignores: a sliced image is a layout element
        // whose preferred height is its sprite's borders (300 here), which would make the body that tall.
        var body = ViewFactory.NewRect(Root, "Content");
        _body = body.gameObject;
        var frame = ViewFactory.Column(body, 0, TextAnchor.UpperCenter);
        if (Theme.ExpanderPanel != null)
        {
            var panel = ViewFactory.NewRect(body, "Panel");
            ViewFactory.Stretch(panel);
            panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Theme.ExpanderPanel.ApplyTo(panel.gameObject);
            frame.padding = new RectOffset(16, 16, 12, 12);
        }
        else frame.padding = new RectOffset(24, 0, 0, 0);
        _content = ViewFactory.NewRect(body, "Items");
        ViewFactory.Column(_content, 10, TextAnchor.UpperCenter);
    }

    public override RectTransform Container => _content;

    protected override string DefaultName => "Expander";

    protected override void ApplyFont(double size)
    {
        if (_label != null) _label.fontSize = FontOf(size, _headerSize, _headerSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0 && _label != null && _shownHeader != _expander.Header)
            _label.text = _shownHeader = _expander.Header ?? "";
        if ((dirty & DirtyFlags.Value) != 0 && _shownExpanded != _expander.IsExpanded)
        {
            bool open = _expander.IsExpanded;
            _shownExpanded = open;
            _body.SetActive(open);
            // The sprite points up (the game turns its selector's buttons): right while closed, down while open.
            if (_arrow != null) _arrow.localEulerAngles = new Vector3(0, 0, open ? 180 : -90);
        }
    }
}

/// <summary>A scroll viewer: the settings menu's scroll view (frame, background, scrollbar) at the viewer's height.</summary>
internal sealed class ScrollViewerView : UnityView
{
    private readonly RectTransform _content;

    public ScrollViewerView(UiScrollViewer viewer, RectTransform parent) : base(viewer)
    {
        (Root, _, _content) = ViewFactory.NewScrollView(parent, DefaultName, padding: 8, spacing: 10);
    }

    public override RectTransform Container => _content;

    protected override string DefaultName => "ScrollViewer";

    protected override (float Width, float Height) LayoutDefaults => (-1, 300);
}

/// <summary>
/// A grid: a column of rows, each a row of cells; a child sits in the cell of its index, the cell as wide as its column
/// (a LayoutElement: pixels fixed, star shares flexible, auto the content's). Cells no child is in go when the children
/// change.
/// </summary>
internal sealed class GridView : UnityView
{
    private readonly UiGrid _grid;
    private readonly VerticalLayoutGroup _rowsGroup;
    private readonly List<RectTransform> _rows = [];
    private readonly List<RectTransform> _cells = [];

    public GridView(UiGrid grid, RectTransform parent) : base(grid)
    {
        _grid = grid;
        Root = ViewFactory.NewRect(parent, DefaultName);
        _rowsGroup = Root.gameObject.AddComponent<VerticalLayoutGroup>();
        _rowsGroup.childControlWidth = true;
        _rowsGroup.childControlHeight = true;
        _rowsGroup.childForceExpandWidth = true;
        _rowsGroup.childForceExpandHeight = false;
        _rowsGroup.childAlignment = TextAnchor.UpperLeft;
    }

    protected override string DefaultName => "Grid";

    public override void Place(IElementView child, int index)
    {
        var cell = CellAt(index, _grid.Columns);
        var rect = ((UnityView)child).Outer;
        if (rect.parent != cell) rect.SetParent(cell, false);
    }

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        var columns = _grid.Columns;
        _rowsGroup.spacing = (float)_grid.RowSpacing;
        // Cells past the last child go (with rows left empty), the rest take their column's width again.
        int count = _grid.Children.Count;
        // Out of the layout first: Destroy only happens at the end of the frame.
        for (int i = _cells.Count - 1; i >= count; i--)
        {
            _cells[i].SetParent(Theme.Holder, false);
            Object.Destroy(_cells[i].gameObject);
            _cells.RemoveAt(i);
        }
        int rows = (count + columns.Count - 1) / columns.Count;
        for (int r = _rows.Count - 1; r >= rows; r--)
        {
            _rows[r].SetParent(Theme.Holder, false);
            Object.Destroy(_rows[r].gameObject);
            _rows.RemoveAt(r);
        }
        foreach (var row in _rows) row.GetComponent<HorizontalLayoutGroup>().spacing = (float)_grid.ColumnSpacing;
        for (int i = 0; i < _cells.Count; i++) Size(_cells[i], columns[i % columns.Count]);
    }

    private RectTransform CellAt(int index, IReadOnlyList<GridLength> columns)
    {
        while (_cells.Count <= index)
        {
            int at = _cells.Count;
            var row = RowAt(at / columns.Count);
            var cell = ViewFactory.NewRect(row, "Cell");
            ViewFactory.Column(cell, align: TextAnchor.UpperCenter);
            Size(cell, columns[at % columns.Count]);
            _cells.Add(cell);
        }
        return _cells[index];
    }

    private RectTransform RowAt(int index)
    {
        while (_rows.Count <= index)
        {
            var row = ViewFactory.NewRect(Root, "Row");
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true; // cells of a row are as tall as its tallest
            group.childAlignment = TextAnchor.UpperLeft;
            group.spacing = (float)_grid.ColumnSpacing;
            _rows.Add(row);
        }
        return _rows[index];
    }

    private static void Size(RectTransform cell, GridLength column)
    {
        var size = cell.GetComponent<LayoutElement>() ?? cell.gameObject.AddComponent<LayoutElement>();
        switch (column.Unit)
        {
            case GridUnit.Pixel:
                size.minWidth = size.preferredWidth = (float)column.Value;
                size.flexibleWidth = 0;
                break;
            case GridUnit.Star:
                size.minWidth = 0;
                size.preferredWidth = 0;
                size.flexibleWidth = (float)column.Value;
                break;
            default:
                size.minWidth = -1;
                size.preferredWidth = -1;
                size.flexibleWidth = 0;
                break;
        }
    }
}
