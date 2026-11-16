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

/// <summary>Builds the Unity side of elements from the game's own UI (<see cref="Theme"/>, <see cref="SettingRows"/>).</summary>
internal sealed class UnityBackend : IViewBackend
{
    public static readonly UnityBackend Instance = new();

    public IElementView Create(Control element, IElementView? parent, ViewHost host)
    {
        var parentView = parent as UnityView;
        var container = parentView?.Container ?? throw new InvalidOperationException($"{element}: no parent to build in");
        UnityView view = element switch
        {
            UiStackPanel panel when host.AdoptsRoot(panel, parent) => new PanelView(panel, container, adopt: true, host),
            UiStackPanel panel => new PanelView(panel, container, adopt: false, host),
            UiGrid grid => new GridView(grid, container),
            UiUserControl or UiItemsControl or UiListBox => new ContentView(element, container),
            ListBoxItem row => new ListBoxItemView(row, container),
            UiTabControl tabs => new TabControlView(tabs, container),
            TabStripButton tab => new TabStripButtonView(tab, container),
            UiKeyHint hint => new KeyHintView(hint, container),
            UiWrapPanel wrap => new WrapPanelView(wrap, container),
            UiSlot slot => new SlotView(slot, container),
            UiCard card => new CardView(card, container),
            UiItemCard item => new ItemCardView(item, container),
            UiBorder border => new BorderView(border, container),
            UiLayoutTransformControl transform => new LayoutTransformView(transform, container),
            UiExpander expander => new ExpanderView(expander, container),
            UiScrollViewer viewer => new ScrollViewerView(viewer, container),
            UiTextBlock { Header: not null } text => new TextRowView(text, container),
            UiTextBlock text => new TextView(text, container, host.TextAlign(parent)),
            UiSectionHeader header => new HeaderView(header, container),
            UiButton { Header: not null } button => new ButtonRowView(button, container),
            UiHotKeyBox box => new HotKeyBoxView(box, container),
            UiButton button => new ButtonView(button, container),
            UiToggleSwitch toggle => new SelectorView(toggle, container,
                                                      () => [toggle.OffContent ?? Theme.Loc("common.off", "Off"), toggle.OnContent ?? Theme.Loc("common.on", "On")],
                                                      () => toggle.IsChecked ? 1 : 0, i => toggle.IsChecked = i == 1),
            ModRow row => new ModRowView(row, container),
            UiSlider slider => new SliderView(slider, container),
            SelectingItemsControl { AsDropdown: true } choice => new DropdownView(choice, container),
            SelectingItemsControl choice => new SelectorView(choice, container, () => choice.ItemTexts, () => choice.SelectedIndex, choice.SetIndexFromView),
            UiTextBox box => new TextBoxView(box, container),
            UiImage image => new ImageView(image, container),
            UiProgressBar bar => new ProgressBarView(bar, container),
            UiProgressRing ring => new ProgressRingView(ring, container),
            UiUnityHost unityHost => new UnityHostView(unityHost, container),
            _ => throw new NotSupportedException($"{element.GetType().Name} has no Unity view"),
        };
        view.ParentAxis = parentView.ChildAxis;
        return view;
    }
}

/// <summary>
/// One live Unity copy of an element. Pulls the element's state when told what changed and sets Unity only when the
/// shown value differs (every string set crosses into IL2CPP). Listeners it added are removed on dispose, so no closure
/// keeps game objects alive across the two heaps. An element with a margin or its own alignment sits in a slot
/// (<see cref="LayoutPlan"/>): <see cref="Outer"/> is then the slot, which its panel places, shows and hides.
/// </summary>
internal abstract class UnityView : IElementView
{
    private readonly List<UnityEventBase> _listened = [];
    private LayoutElement? _layout;
    private CanvasGroup? _group;
    private RectTransform? _slot;
    private bool? _shownVisible;
    private (bool Usable, float Alpha)? _shownEnabled;

    protected UnityView(Control element) => Element = element;

    public Control Element { get; }
    public RectTransform Root { get; protected set; } = null!;

    /// <summary>What the parent places: the slot, or the element's own object.</summary>
    public RectTransform Outer => _slot ?? Root;

    /// <summary>Where children go (panels).</summary>
    public virtual RectTransform Container => Root;

    /// <summary>The direction of the panel this view sits in.</summary>
    public Orientation ParentAxis { get; set; }

    /// <summary>The direction its children are laid out in.</summary>
    public virtual Orientation ChildAxis => Orientation.Vertical;

    protected abstract string DefaultName { get; }

    /// <summary>The Unity object's name: the element's <see cref="Control.Name"/>, or the kind's default.</summary>
    protected virtual string ObjectName => Element.Name ?? DefaultName;

    /// <summary>Preferred width and height of this kind of element (-1: the content's own).</summary>
    protected virtual (float Width, float Height) LayoutDefaults => (-1, -1);

    /// <summary>Keeps its own size when stretched (a standalone button, a picture, a bare game control laid out for its width).</summary>
    protected virtual bool FixedSize => false;

    /// <summary>
    /// True while the view itself sets its controls. Unity raises value events for programmatic changes too (setting a
    /// slider's min or max clamps its value and fires onValueChanged), and those must not reach the model as the
    /// player's: a bound config entry would be overwritten.
    /// </summary>
    protected bool Applying { get; private set; }

    public void Apply(DirtyFlags dirty)
    {
        Applying = true;
        try
        {
            ApplyAll(dirty);
        }
        finally
        {
            Applying = false;
        }
    }

    private void ApplyAll(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Name) != 0) Root.name = ObjectName;
        if ((dirty & DirtyFlags.Layout) != 0) ApplyLayout();
        if ((dirty & DirtyFlags.Visible) != 0 && _shownVisible != Element.IsVisible)
        {
            _shownVisible = Element.IsVisible;
            Outer.gameObject.SetActive(Element.IsVisible);
        }
        if ((dirty & DirtyFlags.Enabled) != 0)
        {
            // Its opacity, times the dimming of the outermost disabled element.
            var (usable, dimmed) = Element.EnabledLook;
            float alpha = (float)Element.Opacity * (dimmed ? 0.5f : 1);
            if (_shownEnabled != (usable, alpha))
            {
                _shownEnabled = (usable, alpha);
                if (_group != null || !usable || alpha < 1)
                {
                    _group ??= Root.GetComponent<CanvasGroup>() ?? Root.gameObject.AddComponent<CanvasGroup>();
                    _group.interactable = usable;
                    _group.alpha = alpha;
                }
            }
        }
        if ((dirty & DirtyFlags.Style) != 0) ApplyFont(Element.FontSize);
        if ((dirty & DirtyFlags.ToolTip) != 0) ToolTipService.Track(this, Element.ToolTip != null);
        ApplyOwn(dirty);
    }

    protected virtual void ApplyOwn(DirtyFlags dirty) { }

    /// <summary>Its texts at the inherited <see cref="Control.FontSize"/> (NaN: the game's sizes).</summary>
    protected virtual void ApplyFont(double size) { }

    /// <summary>A text's size: the game's when <paramref name="size"/> is NaN, else in proportion to the main text's game size.</summary>
    internal static float FontOf(double size, float game, float mainGame) =>
        double.IsNaN(size) || size <= 0 ? game : (float)size * game / mainGame;

    /// <summary>The content's own size when the element has a maximum along its panel (-1: not measured); a text measures itself.</summary>
    protected virtual (float Width, float Height) Measured => (-1, -1);

    protected virtual void ApplyLayout()
    {
        var plan = LayoutPlan.For(Element, ParentAxis, FixedSize);
        UseSlot(plan);
        // Across the panel: stretched (up to a maximum) or its own size; along it, always its own size.
        var sizes = LayoutPlan.Sizes(Element, ParentAxis, plan.Fill, LayoutDefaults, Measured);
        _layout ??= Root.GetComponent<LayoutElement>() ?? Root.gameObject.AddComponent<LayoutElement>();
        _layout.preferredWidth = sizes.PreferredWidth;
        _layout.preferredHeight = sizes.PreferredHeight;
        _layout.minWidth = sizes.MinWidth;
        _layout.minHeight = sizes.MinHeight;
        _layout.flexibleWidth = sizes.FlexibleWidth;
        _layout.flexibleHeight = sizes.FlexibleHeight;
    }

    // An element with a margin or its own alignment gets a slot: a wrapper whose padding is the margin and whose
    // alignment is the element's (a UGUI layout group has one of each for all its children).
    private void UseSlot(LayoutPlan plan)
    {
        if (plan.Slot && _slot == null)
        {
            var parent = Root.parent;
            int index = Root.GetSiblingIndex();
            bool active = Root.gameObject.activeSelf;
            _slot = ViewFactory.NewRect(parent, Root.name + "_Slot");
            _slot.SetSiblingIndex(index);
            ViewFactory.Column(_slot);
            Root.SetParent(_slot, false);
            Root.gameObject.SetActive(true);
            _slot.gameObject.SetActive(active);
        }
        else if (!plan.Slot && _slot != null)
        {
            var slot = _slot;
            _slot = null;
            Root.SetParent(slot.parent, false);
            Root.SetSiblingIndex(slot.GetSiblingIndex());
            Root.gameObject.SetActive(slot.gameObject.activeSelf);
            slot.SetParent(Theme.Holder, false);
            Object.Destroy(slot.gameObject);
        }
        if (_slot == null) return;

        var margin = Element.Margin;
        var layout = _slot.GetComponent<VerticalLayoutGroup>();
        layout.padding = ViewFactory.Offset(margin);
        bool vertical = ParentAxis == Orientation.Vertical;
        layout.childAlignment = (plan.Across, vertical) switch
        {
            (LayoutAlign.End, true) => TextAnchor.UpperRight,
            (LayoutAlign.Center, true) => TextAnchor.UpperCenter,
            (_, true) => TextAnchor.UpperLeft,
            (LayoutAlign.End, false) => TextAnchor.LowerLeft,
            (LayoutAlign.Center, false) => TextAnchor.MiddleLeft,
            _ => TextAnchor.UpperLeft,
        };
        // The slot takes the whole width of a column (the height of a row); its child sits in it by its alignment.
        var slotLayout = _slot.GetComponent<LayoutElement>() ?? _slot.gameObject.AddComponent<LayoutElement>();
        slotLayout.flexibleWidth = vertical ? 1 : 0;
        slotLayout.flexibleHeight = vertical ? 0 : 1;
    }

    public virtual void Place(IElementView child, int index)
    {
        var rect = ((UnityView)child).Outer;
        if (rect.parent != Container) rect.SetParent(Container, false);
        if (rect.GetSiblingIndex() != index) rect.SetSiblingIndex(index);
    }

    public virtual void Created() => Element.RaiseLoaded(Root);

    public virtual void Dispose()
    {
        ToolTipService.Track(this, false);
        foreach (var e in _listened) e.RemoveAllListeners();
        _listened.Clear();
        if (Root == null) return;
        // Out of the layout now: Destroy only happens at the end of the frame.
        var outer = Outer;
        outer.SetParent(Theme.Holder, false);
        Object.Destroy(outer.gameObject);
    }

    protected void Listen(UnityEventBase unityEvent) => _listened.Add(unityEvent);

    /// <summary>
    /// Makes <paramref name="go"/> a tile the player clicks (a tab, a slot, a card): tinted as the game's tiles are,
    /// <paramref name="normal"/>, white under the pointer and <paramref name="pressed"/>, over <paramref name="target"/>;
    /// a click while the view applies the model is not the player's.
    /// </summary>
    protected void Clickable(GameObject go, Graphic? target, Color normal, Color pressed, Action click)
    {
        var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = target;
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = Color.white;
        colors.selectedColor = normal;
        colors.pressedColor = pressed;
        button.colors = colors;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
        {
            if (!Applying) GameUi.Guard($"{Element} click", click);
        })));
        Listen(button.onClick);
    }

    protected static RectTransform RectOf(GameObject go) => go.GetComponent<RectTransform>();

    protected static TextAlignmentOptions TmpAlignment(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Left => TextAlignmentOptions.Left,
        TextAlignment.Right => TextAlignmentOptions.Right,
        TextAlignment.Justify => TextAlignmentOptions.Justified,
        _ => TextAlignmentOptions.Center,
    };
}

/// <summary>An existing object of a host (a window's scroll content, a HUD corner, a menu's button group) to build into.</summary>
internal sealed class ContainerView : UnityView
{
    public ContainerView(RectTransform rect) : base(new UiStackPanel()) => Root = rect;

    protected override string DefaultName => Root.name;

    protected override void ApplyLayout() { }

    public override void Created() { }

    public override void Dispose() { } // the host's object, not ours
}

/// <summary>
/// A cloned game button's text and how the game sizes it: its dialog buttons size their text to fit (TextMeshPro's auto
/// size; seen in game, 2026-10-24: setting fontSize alone changed nothing). A <see cref="Control.FontSize"/> of its own
/// turns that off; NaN gives the game's sizing back.
/// </summary>
internal sealed class ButtonText
{
    private readonly TextMeshProUGUI _label;
    private readonly bool _gameAuto;
    private readonly float _gameSize;
    private readonly Vector4 _gameMargin;

    private ButtonText(TextMeshProUGUI label)
    {
        _label = label;
        _gameAuto = label.enableAutoSizing;
        _gameSize = label.fontSize;
        _gameMargin = label.margin;
    }

    /// <summary>The space around the text (<see cref="Control.Padding"/>; null: the game's).</summary>
    public void Pad(Thickness? padding) => _label.margin = ViewFactory.TextMargin(padding, _gameMargin);

    public static ButtonText? Of(GameObject button) =>
        button.GetComponentInChildren<TextMeshProUGUI>(true) is { } label ? new ButtonText(label) : null;

    public void Apply(double size)
    {
        if (size > 0 && !double.IsInfinity(size))
        {
            _label.enableAutoSizing = false;
            _label.fontSize = (float)size;
        }
        else
        {
            _label.enableAutoSizing = _gameAuto;
            _label.fontSize = _gameSize;
        }
    }
}
