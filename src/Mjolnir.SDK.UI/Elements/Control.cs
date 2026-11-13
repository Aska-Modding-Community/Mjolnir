using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The base of every element of a mod's UI: a plain C# object describing what to show, built in code
/// (<c>var text = new UiTextBlock(); text.Text = "Hi"; panel.Children.Add(text);</c>). The SDK turns it
/// into the game's own UI wherever it is shown (a <see cref="UiWindow"/>, the HUD, a game menu) and keeps every live copy
/// in step with it: change a property, from any thread, and the screen follows (on the next frame from other threads).
/// An element sits in one place at a time.
/// </summary>
public abstract class Control : MjolnirObject
{
    public static readonly MjolnirProperty<string?> NameProperty =
        MjolnirProperty.Register<Control, string?>(nameof(Name), null, DirtyFlags.Name);

    public static readonly MjolnirProperty<bool> IsVisibleProperty =
        MjolnirProperty.Register<Control, bool>(nameof(IsVisible), true, DirtyFlags.Visible);

    public static readonly MjolnirProperty<bool> IsEnabledProperty =
        MjolnirProperty.Register<Control, bool>(nameof(IsEnabled), true, DirtyFlags.Enabled);

    public static readonly MjolnirProperty<double> WidthProperty =
        MjolnirProperty.Register<Control, double>(nameof(Width), double.NaN, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> HeightProperty =
        MjolnirProperty.Register<Control, double>(nameof(Height), double.NaN, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> MinWidthProperty =
        MjolnirProperty.Register<Control, double>(nameof(MinWidth), 0, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> MinHeightProperty =
        MjolnirProperty.Register<Control, double>(nameof(MinHeight), 0, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> MaxWidthProperty =
        MjolnirProperty.Register<Control, double>(nameof(MaxWidth), double.PositiveInfinity, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> MaxHeightProperty =
        MjolnirProperty.Register<Control, double>(nameof(MaxHeight), double.PositiveInfinity, DirtyFlags.Layout);

    // Inherited: a change also reaches everything inside (FontChanged).
    public static readonly MjolnirProperty<double> FontSizeProperty =
        MjolnirProperty.Register<Control, double>(nameof(FontSize), double.NaN, DirtyFlags.Style);

    public static readonly MjolnirProperty<Thickness?> PaddingProperty =
        MjolnirProperty.Register<Control, Thickness?>(nameof(Padding), null, DirtyFlags.Style | DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> OpacityProperty =
        MjolnirProperty.Register<Control, double>(nameof(Opacity), 1, DirtyFlags.Enabled,
                                                  coerce: static (_, value) => double.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1));

    public static readonly MjolnirProperty<object?> ToolTipProperty =
        MjolnirProperty.Register<Control, object?>(nameof(ToolTip), null, DirtyFlags.ToolTip);

    public static readonly MjolnirProperty<Thickness> MarginProperty =
        MjolnirProperty.Register<Control, Thickness>(nameof(Margin), default, DirtyFlags.Layout);

    public static readonly MjolnirProperty<HorizontalAlignment> HorizontalAlignmentProperty =
        MjolnirProperty.Register<Control, HorizontalAlignment>(nameof(HorizontalAlignment), HorizontalAlignment.Stretch, DirtyFlags.Layout);

    public static readonly MjolnirProperty<VerticalAlignment> VerticalAlignmentProperty =
        MjolnirProperty.Register<Control, VerticalAlignment>(nameof(VerticalAlignment), VerticalAlignment.Stretch, DirtyFlags.Layout);

    private readonly List<LiveNode> _nodes = [];
    private ModHandlers<EventHandler<LoadedEventArgs>>? _loaded;

    private protected Control() { }

    /// <summary>Name of the Unity object (for your own lookups and for debugging); a default per kind otherwise.</summary>
    public string? Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>Shown or hidden. A hidden element takes no space.</summary>
    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Whether the player can use it; a disabled element is dimmed, and so is everything in it (see
    /// <see cref="IsEffectivelyEnabled"/>).
    /// </summary>
    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Width in reference pixels (the UI is laid out for 1920×1080); NaN (the default) for the element's own.</summary>
    public double Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>Height in reference pixels; NaN (the default) for the element's own.</summary>
    public double Height
    {
        get => GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    public double MinWidth
    {
        get => GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    public double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// The widest the element gets, in reference pixels; infinity (the default) for no limit. Across a column a stretched
    /// element grows up to it and is centred beyond; along a row it caps a width the element knows (a row, a
    /// picture, a bar). <see cref="MinWidth"/> wins over it.
    /// </summary>
    public double MaxWidth
    {
        get => GetValue(MaxWidthProperty);
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// The tallest the element gets, in reference pixels; infinity (the default) for no limit. Along a column it caps a
    /// height the element knows (a row, a picture, a bar, a scroll viewer, a text, which is then cut with an ellipsis); a
    /// panel's height is its content's: put tall content in a <see cref="UiScrollViewer"/>. Across a row a stretched
    /// element grows up to it. <see cref="MinHeight"/> wins over it.
    /// </summary>
    public double MaxHeight
    {
        get => GetValue(MaxHeightProperty);
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// The size of the element's letters, in reference pixels, inherited: unset (NaN), it is the nearest
    /// container's, and NaN all the way up means the game's own sizes. Set it on a panel to size every text in it. The
    /// element's main text gets the size and its other texts follow in proportion: a row's header and its description and
    /// value, a button's text (which then no longer shrinks to fit: give a bigger one room with
    /// <see cref="Height"/>). Reading it gives the inherited value; <c>GetValue(FontSizeProperty)</c> the element's own.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty) is var own && !double.IsNaN(own) ? own : Parent?.FontSize ?? double.NaN;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Space between the element's frame and its content, in reference pixels; null
    /// (the default) for the game's own. A text or a button: around its text. A text box: around the text it edits. A
    /// row: Left is where its picture or header starts, Right keeps the control off the row's right edge, Top and Bottom
    /// make the row taller (a new padding builds the row again). A <see cref="UiBorder"/> has its own padding.
    /// </summary>
    public Thickness? Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// How opaque the element and everything in it are drawn, 0 (invisible, still taking its place) to 1 (the default);
    /// a disabled element's dimming multiplies it.
    /// </summary>
    public double Opacity
    {
        get => GetValue(OpacityProperty);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>
    /// What the game's hover card shows beside the element while the pointer rests on it: a
    /// string, its first line as the card's amber title and the rest under it (<c>"Wood\nA log from a tree"</c>), or an
    /// element of its own, shown in the card as it is (it must not be shown anywhere else). Null (the default) for none.
    /// </summary>
    public object? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    /// <summary>Space around the element.</summary>
    public Thickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Where the element sits across a column: stretched over its width (the default; an element with a size of its own,
    /// such as a standalone button or a picture, stays centred), or at its own width on the left, in the centre or on the
    /// right. Ignored in a row.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => GetValue(HorizontalAlignmentProperty);
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>Where the element sits across a row (its height). Ignored in a column.</summary>
    public VerticalAlignment VerticalAlignment
    {
        get => GetValue(VerticalAlignmentProperty);
        set => SetValue(VerticalAlignmentProperty, value);
    }

    /// <summary>
    /// The way to raw Unity: raised with the Unity object of every live copy once it is built (a window builds once, the
    /// HUD once per world). If a handler throws, its mod is disabled.
    /// </summary>
    public event EventHandler<LoadedEventArgs>? Loaded
    {
        add => ModHandlers<EventHandler<LoadedEventArgs>>.Of(ref _loaded, $"{GetType().Name}.Loaded").Add(value);
        remove => _loaded?.Remove(value);
    }

    /// <summary>
    /// Whether the player can use it, all things considered: <see cref="IsEnabled"/>,
    /// for a button its command's CanExecute, and every element it sits in enabled. A disabled panel disables what is
    /// in it, though their own <see cref="IsEnabled"/> stays true.
    /// </summary>
    public bool IsEffectivelyEnabled
    {
        get
        {
            for (var element = this; element != null; element = element.Parent)
            {
                if (!element.ShownEnabled) return false;
            }
            return true;
        }
    }

    /// <summary>Whether the player can use it as far as the element itself goes: <see cref="IsEnabled"/>, and for a button its command's CanExecute.</summary>
    internal virtual bool ShownEnabled => IsEnabled;

    /// <summary>
    /// How its enabled state shows: <c>Usable</c> blocks the player's input when false; <c>Dimmed</c> is true only for the
    /// outermost disabled element, since a disabled element's dimming and blocking reach everything in it (a CanvasGroup
    /// multiplies down): a disabled button in a disabled panel is dimmed once, not twice.
    /// </summary>
    internal (bool Usable, bool Dimmed) EnabledLook => (ShownEnabled, !ShownEnabled && (Parent?.IsEffectivelyEnabled ?? true));

    /// <summary>Its own enabled state changed (IsEnabled, a command's CanExecute): it and everything in it show it again.</summary>
    internal void EnabledChanged()
    {
        Dirty(DirtyFlags.Enabled);
        foreach (var child in LogicalChildren) child.EnabledChanged();
    }

    /// <summary>Its font size changed, or the one it inherits: it and everything in it show their texts again.</summary>
    internal void FontChanged()
    {
        Dirty(DirtyFlags.Style);
        foreach (var child in LogicalChildren) child.FontChanged();
    }

    /// <summary>The element this one sits in (a panel, a border, a user control), if any.</summary>
    internal Control? Parent { get; private set; }

    /// <summary>The live copies (one per host showing it). Main thread.</summary>
    internal IReadOnlyList<LiveNode> Nodes => _nodes;

    /// <summary>The children to show, in order (panels, lists, user controls).</summary>
    internal virtual IReadOnlyList<Control> LogicalChildren => [];

    internal override bool IsLive => _nodes.Count > 0;

    internal override bool IsOnScreen
    {
        get
        {
            foreach (var node in _nodes)
            {
                if (node.Host.IsActive) return true;
            }
            return false;
        }
    }

    /// <summary>Marks what changed for the live copies; a kind may widen it (a grid re-lays out its cells with its children).</summary>
    internal virtual void Dirty(DirtyFlags dirty) => LiveTree.MarkDirty(this, dirty);

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        if (ReferenceEquals(e.Property, IsEnabledProperty)) EnabledChanged();
        else if (ReferenceEquals(e.Property, FontSizeProperty)) FontChanged();
        // A row lays its parts out for its padding when it is built.
        else if (ReferenceEquals(e.Property, PaddingProperty) && this is HeaderedControl { Header: not null }) Dirty(DirtyFlags.Rebuild);
        else Dirty(dirty);
    }

    internal void AddNode(LiveNode node)
    {
        _nodes.Add(node);
        if (_nodes.Count != 1) return;
        SyncBindings(); // values are in before the first copy is built
        Attached();
    }

    internal void RemoveNode(LiveNode node)
    {
        if (!_nodes.Remove(node) || _nodes.Count != 0) return;
        Detached();
        SyncBindings();
    }

    /// <summary>The first live copy appeared: subscribe to sources (collections, commands). Main thread.</summary>
    private protected virtual void Attached() { }

    /// <summary>The last live copy went: let go of sources, so nothing long-lived keeps this element. Main thread.</summary>
    private protected virtual void Detached() { }

    internal void RaiseLoaded(UnityEngine.RectTransform root) => _loaded?.Raise(h => h(this, new LoadedEventArgs(root)));

    /// <summary>
    /// Makes <paramref name="child"/> this element's child: an element has one parent at most, and cannot contain itself or
    /// an element it sits in (mounting such a cycle would never end).
    /// </summary>
    internal void Adopt(Control child)
    {
        if (child.Parent != null && child.Parent != this)
            throw new InvalidOperationException($"{child} is already in {child.Parent}; an element can be in one place only");
        if (IsSelfOrInside(child)) throw new InvalidOperationException($"{this} cannot contain {child}: {child} contains it");
        bool moved = child.Parent != this;
        child.Parent = this;
        if (moved && !double.IsNaN(FontSize)) child.FontChanged(); // it inherits another size now
    }

    /// <summary>Whether this element is <paramref name="ancestor"/> or sits somewhere inside it.</summary>
    internal bool IsSelfOrInside(Control ancestor)
    {
        for (var element = this; element != null; element = element.Parent)
        {
            if (ReferenceEquals(element, ancestor)) return true;
        }
        return false;
    }

    /// <summary>Adopts a child set through a property (content, child), logging instead of throwing: the value is stored already.</summary>
    internal bool TryAdopt(Control child)
    {
        try
        {
            Adopt(child);
            return true;
        }
        catch (InvalidOperationException e)
        {
            SdkLog.Error(GameUi.Source, $"{e.Message}; not shown");
            return false;
        }
    }

    internal static void Release(Control child)
    {
        bool inherited = child.Parent is { } parent && !double.IsNaN(parent.FontSize);
        child.Parent = null;
        if (inherited) child.FontChanged();
    }

    /// <summary>Releases the old child of a content property and adopts the new one (main thread or not: only Parent changes).</summary>
    private protected void ContentChanged(MjolnirPropertyChangedEventArgs<Control?> e)
    {
        if (e.OldValue is { } old && old.Parent == this) Release(old);
        if (e.NewValue is { } now) TryAdopt(now);
    }

    /// <summary>The content property's change, when <paramref name="e"/> is of <paramref name="property"/>.</summary>
    private protected void ContentChanged(MjolnirPropertyChangedEventArgs e, MjolnirProperty<Control?> property)
    {
        if (ReferenceEquals(e.Property, property) && e is MjolnirPropertyChangedEventArgs<Control?> content) ContentChanged(content);
    }

    /// <summary>
    /// The child of a content property as a list of children: none when it is not held here (one that could not be
    /// adopted, being elsewhere or containing this element, is shown nowhere).
    /// </summary>
    private protected IReadOnlyList<Control> OwnChild(Control? child) => child is { } held && held.Parent == this ? [held] : [];

    // Descriptions (event names, log lines) read stored values only: reading a bound property while the element is not
    // live reads its source, whose own description would read the property again, without end.
    public override string ToString() => GetStored(NameProperty) is { } name ? $"{GetType().Name} '{name}'" : GetType().Name;
}
