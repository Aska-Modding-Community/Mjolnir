using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GameContextMenu = SSSGame.UI.ContextMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>The game's amber of a selected tab, row or card, and the dark tone of an icon on it.</summary>
internal static class SelectionColors
{
    public static readonly Color Amber = new(1f, 0.698f, 0.11f, 1f);   // #FFB21C
    public static readonly Color OnAmber = new(0.23f, 0.16f, 0.05f, 1f);
}

/// <summary>
/// A row of a <see cref="UiListBox"/>: a clone of the «Load game» page's save-slot row (its frames, the amber
/// ToggleEffect while selected), the row's content laid out inside it, in a child of its own after the frames (as the
/// first child it was drawn under them, seen in game, 2026-10-24). A click selects the row.
/// </summary>
internal sealed class ListBoxItemView : UnityView
{
    private readonly ListBoxItem _row;
    private readonly GameObject? _selected;
    private readonly Toggle? _toggle;
    private readonly Image? _plain;
    private readonly RectTransform _content;
    private bool? _shownSelected;

    public ListBoxItemView(ListBoxItem row, RectTransform parent) : base(row)
    {
        _row = row;
        if (Theme.ListRow is { } template)
        {
            var go = Object.Instantiate(template, parent, false);
            go.name = DefaultName;
            go.SetActive(true);
            Root = RectOf(go);
            // The game's frames fill the row; the layout below places only the content.
            for (int i = 0; i < Root.childCount; i++)
            {
                var child = Root.GetChild(i).gameObject;
                (child.GetComponent<LayoutElement>() ?? child.AddComponent<LayoutElement>()).ignoreLayout = true;
            }
            _selected = Root.Find("ToggleEffect")?.gameObject;
            _toggle = go.GetComponent<Toggle>();
        }
        else
        {
            Root = ViewFactory.NewRect(parent, DefaultName);
            _plain = Root.gameObject.AddComponent<Image>();
            _plain.color = new Color(0.2f, 0.22f, 0.21f, 0.9f);
            _toggle = Root.gameObject.AddComponent<Toggle>();
            _toggle.targetGraphic = _plain;
        }
        if (_toggle != null)
        {
            _toggle.group = null;
            _toggle.onValueChanged = new Toggle.ToggleEvent();
            _toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(_ =>
            {
                if (Applying) return;
                GameUi.Guard($"{Element} click", () =>
                {
                    _row.OnClick();
                    _shownSelected = null;
                    Apply(DirtyFlags.Value); // a click on the selected row keeps it selected
                });
            })));
            Listen(_toggle.onValueChanged);
        }
        // The row is as tall as its content plus the padding; the content spans the row.
        var frame = Root.gameObject.AddComponent<VerticalLayoutGroup>();
        frame.padding = new RectOffset(16, 16, 12, 12);
        frame.childControlWidth = true;
        frame.childControlHeight = true;
        frame.childForceExpandWidth = true;
        frame.childForceExpandHeight = false;
        _content = ViewFactory.NewRect(Root, "Content");
        ViewFactory.Column(_content, align: TextAnchor.MiddleLeft);
    }

    protected override string DefaultName => "ListBoxItem";

    public override RectTransform Container => _content;

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Value) == 0 || _shownSelected == _row.IsSelected) return;
        _shownSelected = _row.IsSelected;
        _selected?.SetActive(_row.IsSelected);
        _toggle?.SetIsOnWithoutNotify(_row.IsSelected);
        if (_plain != null) _plain.color = _row.IsSelected ? SelectionColors.Amber * 0.6f : new Color(0.2f, 0.22f, 0.21f, 0.9f);
    }
}

/// <summary>
/// A tab of a <see cref="UiTabControl"/>: the journal's icon tab (its icon from the tab's picture, amber with
/// Frame_Selected while selected) or its list button (icon, title, an amber frame while selected).
/// </summary>
internal sealed class TabStripButtonView : UnityView
{
    private static readonly Color UnselectedFrame = new(0.776f, 0.902f, 0.855f, 0.25f); // #C6E6DA40
    private readonly TabStripButton _tab;
    private readonly bool _iconTab;
    private readonly Image? _background, _icon, _shadow, _frame;
    private readonly GameObject? _frameSelected;
    private readonly TMP_Text? _title;
    private readonly Color _backgroundColor, _iconColor, _frameColor;
    private readonly Vector2 _size;
    private readonly float _titleSize;
    private string? _shownTitle;
    private object? _shownIcon = new();
    private bool? _shownSelected;

    public TabStripButtonView(TabStripButton tab, RectTransform parent) : base(tab)
    {
        _tab = tab;
        _iconTab = tab.IsIconTab;
        var template = _iconTab ? Theme.IconTab : Theme.ListTab;
        if (template != null)
        {
            var go = Object.Instantiate(template, parent, false);
            go.name = DefaultName;
            go.SetActive(true);
            Root = RectOf(go);
            _size = Root.sizeDelta;
            _background = Root.Find("Background")?.GetComponent<Image>();
            _icon = Root.Find("Icon")?.GetComponent<Image>();
            _shadow = Root.Find("Icon_Shadow")?.GetComponent<Image>();
            _frame = Root.Find("Frame")?.GetComponent<Image>();
            _frameSelected = Root.Find("Frame_Selected")?.gameObject;
            _title = Root.Find("Title")?.GetComponent<TMP_Text>();
            if (_icon != null && _icon.transform.Find("Icon_Selected") is { } selectedIcon) Object.DestroyImmediate(selectedIcon.gameObject);
            if (Root.Find("NewStatus") is { } news) news.gameObject.SetActive(false);
            if (_iconTab && _title != null) _title.gameObject.SetActive(false);
            if (!_iconTab && _frame != null)
            {
                // The prefab's solid frame is opaque and lies over the picture; the game's script tints it at run time
                // (#C6E6DA40, amber while selected). The picture goes over it.
                _frame.color = UnselectedFrame;
                if (_icon != null) _icon.transform.SetSiblingIndex(_frame.transform.GetSiblingIndex());
            }
        }
        else
        {
            Root = ViewFactory.NewRect(parent, DefaultName);
            _size = _iconTab ? new Vector2(95, 80) : new Vector2(294, 42);
            _background = Root.gameObject.AddComponent<Image>();
            _background.color = new Color(0.5f, 0.55f, 0.53f, 1f);
            _title = ViewFactory.NewText(Root, "Title", "", Theme.TextFont, 16);
            ViewFactory.Stretch(_title.rectTransform);
        }
        _backgroundColor = _background != null ? _background.color : Color.white;
        _iconColor = _icon != null ? _icon.color : Color.white;
        _frameColor = _frame != null ? _frame.color : Color.white;
        _titleSize = _title != null ? _title.fontSize : 16;
        Clickable(Root.gameObject, _background, new Color(0.88f, 0.88f, 0.88f, 1f), new Color(0.72f, 0.72f, 0.72f, 1f), _tab.OnClick);
    }

    protected override string DefaultName => _iconTab ? "IconTab" : "ListTab";

    protected override (float Width, float Height) LayoutDefaults => (_size.x, _size.y);

    protected override bool FixedSize => true;

    // A list button's name follows the tab control's FontSize (the game's: Njord 12, which Cyrillic draws smaller).
    protected override void ApplyFont(double size)
    {
        if (!_iconTab && _title != null) _title.fontSize = FontOf(size, _titleSize, _titleSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Text) != 0 && !_iconTab && _title != null && _shownTitle != _tab.Header) _title.text = _shownTitle = _tab.Header;
        if ((dirty & DirtyFlags.Image) != 0 && !ReferenceEquals(_shownIcon, _tab.Icon))
        {
            _shownIcon = _tab.Icon;
            var sprite = Pictures.SpriteOf(_tab.Icon);
            foreach (var image in new[] { _icon, _shadow })
            {
                if (image == null) continue;
                image.sprite = sprite;
                image.preserveAspect = true;
                image.enabled = sprite != null; // the game's script switches a list button's picture on with its sprite
                image.gameObject.SetActive(sprite != null);
            }
            // A list button without a picture starts its title where the picture would be.
            if (!_iconTab && _title != null && sprite == null) _title.rectTransform.offsetMin = new Vector2(16, _title.rectTransform.offsetMin.y);
        }
        if ((dirty & DirtyFlags.Value) != 0 && _shownSelected != _tab.IsSelected)
        {
            bool selected = _tab.IsSelected;
            _shownSelected = selected;
            if (_iconTab)
            {
                _frameSelected?.SetActive(selected);
                if (_background != null) _background.color = selected ? SelectionColors.Amber : _backgroundColor;
                if (_icon != null) _icon.color = selected ? SelectionColors.OnAmber : _iconColor;
            }
            else
            {
                // A list button, as the journal's: its solid frame amber, the picture dark on it, the name keeps its colour.
                if (_frame != null) _frame.color = selected ? SelectionColors.Amber : _frameColor;
                else if (_background != null) _background.color = selected ? SelectionColors.Amber : _backgroundColor;
                if (_icon != null) _icon.color = selected ? SelectionColors.OnAmber : _iconColor;
            }
        }
    }
}

/// <summary>
/// A <see cref="UiTabControl"/>: a column holding its tabs and content. Q and E switch tabs while the menu holding it (a
/// mod window) is the top of the game's menu stack and the player is not typing, as in the game's journal; a tab control
/// inside another one's content leaves them to the outer one.
/// </summary>
internal sealed class TabControlView : UnityView
{
    private static GameKeyboard? s_keyboard;
    private readonly UiTabControl _tabs;
    private readonly Action _keys;

    public TabControlView(UiTabControl tabs, RectTransform parent) : base(tabs)
    {
        _tabs = tabs;
        Root = ViewFactory.NewRect(parent, DefaultName);
        ViewFactory.Column(Root, align: TextAnchor.UpperCenter);
        _keys = Keys;
        LiveTree.EveryFrame(_keys, true);
    }

    protected override string DefaultName => "TabControl";

    private void Keys()
    {
        if (Root == null || !Root.gameObject.activeInHierarchy || _tabs.IsNested) return;
        s_keyboard ??= GameKeyboard.Create();
        if (s_keyboard == null || s_keyboard.IsTyping || !OnTop()) return;
        if (s_keyboard.WasPressed("Q") == true) _tabs.Step(-1);
        else if (s_keyboard.WasPressed("E") == true) _tabs.Step(1);
    }

    // The menu holding the tab control is the top of the game's menu stack (another window or a dialog over it is not).
    private bool OnTop()
    {
        var menu = Root.GetComponentInParent<GameContextMenu>();
        var stack = SandSailorStudio.UI.MenuManager.Instance?._menuStack;
        return menu != null && stack != null && stack.Count > 0 && stack.Peek() is { } top && top.Pointer == menu.Pointer;
    }

    public override void Dispose()
    {
        LiveTree.EveryFrame(_keys, false);
        base.Dispose();
    }
}
