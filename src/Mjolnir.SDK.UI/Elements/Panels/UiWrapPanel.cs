namespace Mjolnir.SDK.UI;

/// <summary>Where a <see cref="UiWrapPanel"/>'s lines sit in it.</summary>
public enum WrapPanelItemsAlignment
{
    Start,
    Center,
    End,
}

/// <summary>
/// Children side by side, wrapping onto the next line when the room runs out: a grid of slots or
/// cards, as the game's journal and build menu lay out theirs. Every child gets a cell of <see cref="ItemWidth"/> ×
/// <see cref="ItemHeight"/>, by default the largest child's own size. As a list's panel:
/// <c>ItemsPanel = new FuncTemplate&lt;Panel&gt;(() => new UiWrapPanel())</c>.
/// </summary>
public sealed class UiWrapPanel : Panel
{
    public static readonly MjolnirProperty<Orientation> OrientationProperty =
        MjolnirProperty.Register<UiWrapPanel, Orientation>(nameof(Orientation), Orientation.Horizontal, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> ItemWidthProperty =
        MjolnirProperty.Register<UiWrapPanel, double>(nameof(ItemWidth), double.NaN, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> ItemHeightProperty =
        MjolnirProperty.Register<UiWrapPanel, double>(nameof(ItemHeight), double.NaN, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> ItemSpacingProperty =
        MjolnirProperty.Register<UiWrapPanel, double>(nameof(ItemSpacing), 10, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> LineSpacingProperty =
        MjolnirProperty.Register<UiWrapPanel, double>(nameof(LineSpacing), 10, DirtyFlags.Layout);

    public static readonly MjolnirProperty<WrapPanelItemsAlignment> ItemsAlignmentProperty =
        MjolnirProperty.Register<UiWrapPanel, WrapPanelItemsAlignment>(nameof(ItemsAlignment), WrapPanelItemsAlignment.Start, DirtyFlags.Layout);

    public UiWrapPanel() { }

    /// <summary>Horizontal (the default) fills a row and wraps to the next; vertical fills a column first.</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Every cell's width; NaN (the default) for the widest child's own width.</summary>
    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>Every cell's height; NaN (the default) for the tallest child's own height.</summary>
    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>Room between the cells of a line (10 by default, the game's grids').</summary>
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>Room between the lines (10 by default).</summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <summary>Where the lines sit across the panel: at its start (the default), in its middle or at its end.</summary>
    public WrapPanelItemsAlignment ItemsAlignment
    {
        get => GetValue(ItemsAlignmentProperty);
        set => SetValue(ItemsAlignmentProperty, value);
    }

    // New children may change the largest child: the cells are measured again.
    internal override void Dirty(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Children) != 0) dirty |= DirtyFlags.Layout;
        base.Dirty(dirty);
    }
}
