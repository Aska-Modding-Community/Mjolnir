using System;
using System.Collections.Generic;
using Mjolnir.SDK.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A <see cref="BuildPalette"/> on screen: a copy of the build menu's look (<see cref="Theme.BuildMenu"/>) on a canvas of
/// its own, filled with the palette's categories and items. The copy has no raycaster and none of the game's scripts:
/// while open it reads the mouse and the keys itself every frame, because a capture of the player's controls switches the
/// interface's input off and the HUD's canvas takes no pointer.
/// </summary>
internal sealed class BuildPaletteView
{
    /// <summary>The canvas's sorting order: over the HUD and its quick bar, under the game's menus (which take the screen).</summary>
    private const int SortingOrder = 20;

    /// <summary>A diamond under the pointer and a chosen one are drawn larger, as the game's tweens do.</summary>
    private const float HoverScale = 1.1f, SelectedScale = 1.2f;

    private static readonly List<BuildPaletteView> s_open = [];
    private static bool s_ticking;

    private readonly BuildPalette _palette;
    private readonly List<(RectTransform Rect, PaletteCategory Category, Image? Frame, GameObject? Highlight, bool Upper)> _diamonds = [];
    private readonly List<(RectTransform Rect, PaletteItem Item, GameObject? Hover, GameObject? Selected)> _tiles = [];
    private GameObject? _root;
    private Canvas? _canvas;
    private RectTransform? _row1, _row2, _content, _viewport, _close, _labelDiv;
    private TMP_Text? _label;
    private HorizontalLayoutGroup? _contentLayout, _row1Layout, _row2Layout;
    private float _scroll;
    private bool _placeAgain;
    private object? _hover;

    public BuildPaletteView(BuildPalette palette) => _palette = palette;

    public bool IsOpen => _root != null && _root.activeSelf;

    public void Open()
    {
        if (_root == null && !Create()) return;
        _root!.SetActive(true);
        _scroll = 0;
        Build();
        if (!s_open.Contains(this)) s_open.Add(this);
        EnsureTicking();
    }

    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        s_open.Remove(this);
        _hover = null;
        _palette.RaiseClosed();
    }

    public void Destroy()
    {
        bool wasOpen = IsOpen;
        s_open.Remove(this);
        if (_root != null) Object.Destroy(_root);
        _root = null;
        if (wasOpen) _palette.RaiseClosed();
    }

    /// <summary>Whether the point (screen pixels) is over any open palette's diamonds, tiles, label or ✕.</summary>
    public static bool AnyOver(Vector2 point)
    {
        foreach (var view in s_open)
            if (view.IsOpen && view.HitTest(point) != null) return true;
        return false;
    }

    private bool Create()
    {
        Theme.Load();
        if (Theme.BuildMenu == null)
        {
            SdkLog.Warn(GameUi.Source, "BuildPalette: the build menu's look is missing, no palette shown");
            return false;
        }
        _root = Object.Instantiate(Theme.BuildMenu);
        _root.name = $"Mjolnir_BuildPalette_{_palette.Owner.GetName().Name}";
        _canvas = _root.GetComponent<Canvas>();
        if (_canvas != null)
        {
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = SortingOrder;
        }
        var root = _root.transform;
        _row1 = Rect(root.Find("Panel/Categories/PanelCategories/Row1"));
        _row2 = Rect(root.Find("Panel/Categories/PanelCategories/Row2"));
        _close = Rect(root.Find("Panel/Categories/Close"));
        _labelDiv = Rect(root.Find("Panel/Categories/CategoryDisplayDiv"));
        _label = _labelDiv?.GetComponentInChildren<TMP_Text>(true);
        _content = Rect(ViewFactory.FindDeep(root, "Content"));
        _viewport = Rect(ViewFactory.FindDeep(root, "Viewport"));
        _contentLayout = _content?.GetComponent<HorizontalLayoutGroup>();
        // The game's rows are laid for its 17 categories from their left; a palette's fewer stand centred, the lower row
        // between the upper's diamonds (the shift in Build), on the same middle.
        _row1Layout = _row1?.GetComponent<HorizontalLayoutGroup>();
        _row2Layout = _row2?.GetComponent<HorizontalLayoutGroup>();
        foreach (var layout in new[] { _row1Layout, _row2Layout })
        {
            if (layout == null) continue;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.padding.left = layout.padding.right = 0;
        }
        if (_row1 != null && _row2 != null)
        {
            _row2.offsetMin = new Vector2(_row1.offsetMin.x, _row2.offsetMin.y);
            _row2.offsetMax = new Vector2(_row1.offsetMax.x, _row2.offsetMax.y);
        }
        if (_content != null)
        {
            // Scrolled by the view itself (the copy has no ScrollRect): pinned to the view's left edge, as wide as its tiles.
            _content.anchorMin = new Vector2(0, 0);
            _content.anchorMax = new Vector2(0, 1);
            _content.pivot = new Vector2(0, 0.5f);
            if (_content.GetComponent<ContentSizeFitter>() is { } fitter)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
        }
        if (_row1 == null || _row2 == null || _content == null)
            SdkLog.Warn(GameUi.Source, $"BuildPalette: parts missing (rows {_row1 != null}/{_row2 != null}, tiles {_content != null})");
        return true;
    }

    /// <summary>Makes the diamonds and the tiles again from the palette's lists.</summary>
    public void Build()
    {
        if (_root == null) return;
        _diamonds.Clear();
        _tiles.Clear();
        _hover = null;
        Clear(_row1);
        Clear(_row2);
        Clear(_content);
        var categories = _palette.Categories;
        int upper = PaletteRules.UpperRow(categories.Count);
        for (int i = 0; i < categories.Count; i++) AddDiamond(categories[i], i < upper ? _row1 : _row2, i < upper);
        if (_row2Layout != null)
        {
            // A centred row moves by half of its left padding.
            float shift = PaletteRules.LowerRowShift(upper, categories.Count - upper, DiamondStep());
            _row2Layout.padding.left = (int)Math.Round(shift * 2);
        }
        if (_row1 != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_row1);
        if (_row2 != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_row2);
        PlaceClose();
        var category = _palette.SelectedCategory;
        if (_label != null) _label.text = category?.DisplayName ?? "";
        if (_labelDiv != null) _labelDiv.gameObject.SetActive(category != null);
        if (category != null)
            foreach (var item in category.Items) AddTile(item);
        if (_content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        _scroll = PaletteRules.Scroll(_scroll, 0, 0, ContentWidth(), ViewWidth());
        Place();
        Paint();
        _placeAgain = true;
    }

    private void AddDiamond(PaletteCategory category, RectTransform? row, bool upper)
    {
        if (row == null || Theme.BuildCategory == null) return;
        var diamond = Object.Instantiate(Theme.BuildCategory, row, false);
        diamond.name = $"Category {category.DisplayName}";
        var icon = diamond.transform.Find("Image")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = Pictures.SpriteOf(category.Icon);
            icon.enabled = icon.sprite != null;
            icon.preserveAspect = true;
        }
        // The menu colours a diamond's frame (its tab's image): clear as it is, orange under the pointer, white chosen. The
        // dark diamond under it keeps its own colours.
        var frame = diamond.transform.Find("Background/Frame1")?.GetComponent<Image>();
        var highlight = diamond.transform.Find("Background/Highlight")?.gameObject;
        _diamonds.Add((diamond.GetComponent<RectTransform>(), category, frame, highlight, upper));
    }

    private void AddTile(PaletteItem item)
    {
        if (_content == null || Theme.BuildTile == null) return;
        var tile = Object.Instantiate(Theme.BuildTile, _content, false);
        tile.name = $"Item {item.DisplayName}";
        var t = tile.transform;
        if (t.Find("Container/Background/Image Div/Image")?.GetComponent<Image>() is { } picture)
        {
            picture.sprite = Pictures.SpriteOf(item.Picture);
            picture.enabled = picture.sprite != null;
            picture.preserveAspect = true;
        }
        if (t.Find("Container/TextDiv/Text")?.GetComponent<TMP_Text>() is { } text) text.text = item.DisplayName;
        _tiles.Add((tile.GetComponent<RectTransform>(), item, t.Find("Container/Background/Tween")?.gameObject,
            t.Find("Container/Background/ItemHighlight")?.gameObject));
    }

    // The diamonds' and tiles' looks: chosen, under the pointer, as they are.
    private void Paint()
    {
        var (normal, highlight, selected) = Theme.BuildCategoryColors;
        foreach (var (rect, category, frame, glow, _) in _diamonds)
        {
            bool chosen = category == _palette.SelectedCategory, over = _hover == category;
            if (frame != null) frame.color = chosen ? selected : over ? highlight : normal;
            if (glow != null) glow.SetActive(chosen || over);
            float scale = chosen ? SelectedScale : over ? HoverScale : 1;
            rect.localScale = new Vector3(scale, scale, 1);
        }
        foreach (var (_, item, hover, chosen) in _tiles)
        {
            if (hover != null) hover.SetActive(_hover == item && item != _palette.SelectedItem);
            if (chosen != null) chosen.SetActive(item == _palette.SelectedItem);
        }
    }

    private void Place()
    {
        if (_content == null) return;
        float content = ContentWidth(), view = ViewWidth();
        float x = content >= view || _viewport == null ? -_scroll : PaletteRules.FittedLeft(content, view, DiamondsMiddle());
        _content.anchoredPosition = new Vector2(x, _content.anchoredPosition.y);
    }

    // The middle of the diamonds, in the view's units from its left edge: tiles that fit stand over it.
    private float DiamondsMiddle()
    {
        if (_viewport == null || _diamonds.Count == 0) return ViewWidth() / 2;
        // In the view's own space: right after a build the canvas may not have taken its scale yet, which moves the world
        // positions of the view and the diamonds alike.
        float left = float.MaxValue, right = float.MinValue;
        foreach (var (rect, _, _, _, _) in _diamonds)
        {
            float x = _viewport.InverseTransformPoint(rect.position).x;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
        return (left + right) / 2 - _viewport.rect.xMin;
    }

    // The close button right after the upper row's last diamond, as the game's stands after its full row.
    private void PlaceClose()
    {
        if (_close == null || _row1 == null) return;
        RectTransform? last = null;
        foreach (var (rect, _, _, _, upper) in _diamonds)
            if (upper && (last == null || rect.position.x > last.position.x)) last = rect;
        if (last == null) return;
        float scale = _row1.lossyScale.x;
        float closeRight = _close.rect.width * _close.lossyScale.x * (_close.pivot.x - 0.5f);
        // In the game the button's middle is about 0.45 of a step past the last diamond's.
        _close.position = new Vector3(last.position.x + 0.45f * DiamondStep() * scale + closeRight, _close.position.y, _close.position.z);
    }

    private float DiamondStep() =>
        (Theme.BuildCategory != null ? Theme.BuildCategory.GetComponent<RectTransform>().rect.width : 35) + (_row1Layout != null ? _row1Layout.spacing : 15);

    private float ContentWidth() => _content != null ? LayoutUtility.GetPreferredWidth(_content) : 0;

    private float ViewWidth() => _viewport != null ? _viewport.rect.width : 0;

    private float TileStep() =>
        (_tiles.Count > 0 ? _tiles[0].Rect.rect.width : 80) + (_contentLayout != null ? _contentLayout.spacing : 0);

    /// <summary>What is under the point: a category, an item, the ✕ (this view), the label (the label's rect); null for nothing.</summary>
    private object? HitTest(Vector2 point)
    {
        var camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        if (_close != null && RectTransformUtility.RectangleContainsScreenPoint(_close, point, camera)) return this;
        foreach (var (rect, category, _, _, _) in _diamonds)
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera)) return category;
        if (_viewport == null || RectTransformUtility.RectangleContainsScreenPoint(_viewport, point, camera))
            foreach (var (rect, item, _, _) in _tiles)
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera)) return item;
        if (_labelDiv != null && _labelDiv.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_labelDiv, point, camera)) return _labelDiv;
        return null;
    }

    private void Update()
    {
        if (_root == null || _palette.IsOwnerDisabled)
        {
            // The world went (the copy with it) or the mod was disabled.
            s_open.Remove(this);
            if (_root != null) _root.SetActive(false);
            _palette.RaiseClosed();
            return;
        }
        if (_placeAgain)
        {
            // Once more a frame after a build, with the canvas's scale applied.
            _placeAgain = false;
            PlaceClose();
            Place();
        }
        // Repainted when what is hovered changes: a category or an item (the close button and the label have no hover look).
        var hit = HitTest(MouseInput.Position);
        var hover = hit is PaletteCategory or PaletteItem ? hit : null;
        bool repaint = !Equals(hover, _hover);
        _hover = hover;

        if (KeyboardInput.WasPressed("Q")) _palette.SelectCategory(-1);
        else if (KeyboardInput.WasPressed("E")) _palette.SelectCategory(1);
        if (!IsOpen) return;

        float wheel = MouseInput.Wheel;
        if (wheel != 0 && hit != null)
        {
            _scroll = PaletteRules.Scroll(_scroll, wheel, TileStep(), ContentWidth(), ViewWidth());
            Place();
        }
        if (MouseInput.WasPressed(MouseButton.Left))
        {
            switch (hit)
            {
                case BuildPaletteView:
                    Close();
                    return;
                case PaletteCategory category when category != _palette.SelectedCategory:
                    _palette.SelectedCategory = category;
                    _scroll = 0;
                    Build();
                    return;
                case PaletteItem item:
                    Close();
                    _palette.RaiseChosen(item);
                    return;
            }
        }
        if (repaint) Paint();
    }

    private static void EnsureTicking()
    {
        if (s_ticking) return;
        s_ticking = true;
        Dispatcher.Hook();
        GameLoop.Update += Tick;
    }

    private static void Tick()
    {
        if (s_open.Count == 0) return;
        foreach (var view in s_open.ToArray())
        {
            try
            {
                if (view.IsOpen || view._root == null) view.Update();
            }
            catch (Exception e)
            {
                SdkLog.Error(GameUi.Source, $"BuildPalette: {e}");
                s_open.Remove(view);
            }
        }
    }

    private static RectTransform? Rect(Transform? t) => t != null ? t.GetComponent<RectTransform>() : null;

    private static void Clear(Transform? parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
