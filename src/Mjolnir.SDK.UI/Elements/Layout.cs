using System;
using System.Globalization;

namespace Mjolnir.SDK.UI;

/// <summary>Direction of a <see cref="UiStackPanel"/>.</summary>
public enum Orientation
{
    Vertical,
    Horizontal,
}

/// <summary>Where an element sits across a column.</summary>
public enum HorizontalAlignment
{
    Stretch,
    Left,
    Center,
    Right,
}

/// <summary>Where an element sits across a row.</summary>
public enum VerticalAlignment
{
    Stretch,
    Top,
    Center,
    Bottom,
}

/// <summary>Alignment of the lines of a <see cref="UiTextBlock"/>.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>Whether a <see cref="UiTextBlock"/> wraps long lines.</summary>
public enum TextWrapping
{
    NoWrap,
    Wrap,
}

/// <summary>What a <see cref="UiTextBox"/> accepts.</summary>
public enum TextInputContentType
{
    /// <summary>Any text.</summary>
    Normal,

    /// <summary>A whole number.</summary>
    Digits,

    /// <summary>A decimal number.</summary>
    Number,
}

/// <summary>How a <see cref="UiWindow"/> is framed (<see cref="UiWindow.WindowStyle"/>): as the game's main windows or as its dialogs.</summary>
public enum WindowStyle
{
    /// <summary>
    /// The game's main windows, as its settings menu and inventory (the default): a dark grey window in a thin frame
    /// with ornaments in two corners and the X in a third, the world blurred behind it, the title on a plate on the top
    /// edge between two ornaments; a <see cref="UiTabControl"/> content with its tabs on top shows them there instead, the
    /// selected tab's name under them.
    /// </summary>
    Menu,

    /// <summary>The game's dialogs and the panels inside its windows: a darker window in a faint frame, the title inside it.</summary>
    Dialog,
}

/// <summary>
/// Whether a <see cref="UiWindow"/> takes the size of its content. Only the height: a window's
/// rows stretch to its width, so a width of the content means nothing here.
/// </summary>
public enum SizeToContent
{
    /// <summary>The window's own <see cref="UiWindow.Height"/>.</summary>
    Manual,

    /// <summary>As tall as its title and content, up to <see cref="UiWindow.MaxHeight"/> and the screen; taller content scrolls.</summary>
    Height,
}

/// <summary>How a <see cref="UiImage"/> fills its box.</summary>
public enum Stretch
{
    /// <summary>Fills the box, ignoring the picture's proportions.</summary>
    Fill,

    /// <summary>As large as fits while keeping the proportions.</summary>
    Uniform,
}

/// <summary>Space around an element (<see cref="Control.Margin"/>) or inside one (<see cref="Control.Padding"/>), in reference pixels.</summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }

    public Thickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    /// <summary>No space on any side.</summary>
    public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public bool Equals(Thickness other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(Thickness a, Thickness b) => a.Equals(b);

    public static bool operator !=(Thickness a, Thickness b) => !a.Equals(b);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Left},{Top},{Right},{Bottom}");
}

/// <summary>
/// How an element is placed in its panel (no Unity: unit-tested). UGUI layout groups have one alignment and one padding
/// for all their children, so an element that needs its own (a margin, Left/Right across a column, Top/Bottom across a
/// row) gets a slot of its own: a wrapper whose padding is the margin and whose alignment is the element's. Alignment
/// along the panel's direction is ignored. Stretch is every element's default; an element
/// with a size of its own (Width or Height set, a standalone button, a picture, a bare game control) is centred instead,
/// and so is one with a maximum across the panel, beyond it.
/// </summary>
internal readonly record struct LayoutPlan(bool Slot, LayoutAlign Across, bool Fill)
{
    /// <param name="element">The element to place.</param>
    /// <param name="panel">The direction of the panel it sits in.</param>
    /// <param name="fixedSize">The element keeps its own size (a standalone button, a picture, a bare game control).</param>
    public static LayoutPlan For(Control element, Orientation panel, bool fixedSize = false)
    {
        var across = panel == Orientation.Vertical
            ? element.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => LayoutAlign.Start,
                HorizontalAlignment.Right => LayoutAlign.End,
                HorizontalAlignment.Center => LayoutAlign.Center,
                _ => fixedSize || !double.IsNaN(element.Width) || IsLimit(element.MaxWidth) ? LayoutAlign.Center : LayoutAlign.Stretch,
            }
            : element.VerticalAlignment switch
            {
                VerticalAlignment.Top => LayoutAlign.Start,
                VerticalAlignment.Bottom => LayoutAlign.End,
                VerticalAlignment.Center => LayoutAlign.Center,
                _ => fixedSize || !double.IsNaN(element.Height) || IsLimit(element.MaxHeight) ? LayoutAlign.Center : LayoutAlign.Stretch,
            };
        bool slot = !element.Margin.IsEmpty || across is LayoutAlign.Start or LayoutAlign.End;
        return new LayoutPlan(slot, across, across == LayoutAlign.Stretch);
    }

    /// <summary>
    /// The sizes a view gives its LayoutElement (-1: none, the content's own). Across the panel a stretched element is
    /// flexible; one with a maximum there is not, and prefers the maximum: a UGUI layout group gives a child that is not
    /// flexible the smaller of the room and its preferred size, so it grows up to the maximum. Along the panel a maximum
    /// caps a size the element knows: its own (<paramref name="defaults"/>, Width/Height) or what the view measured.
    /// Heights the element knows are also its minimum (rows keep their height), and Min* wins over Max*.
    /// </summary>
    /// <param name="element">The element to size.</param>
    /// <param name="panel">The direction of the panel it sits in.</param>
    /// <param name="fill">Stretched across the panel (<see cref="Fill"/>).</param>
    /// <param name="defaults">The view's own preferred width and height (-1: the content's own).</param>
    /// <param name="measured">The content's own size, as the view measured it when it has a maximum (-1: not measured).</param>
    public static LayoutSizes Sizes(Control element, Orientation panel, bool fill, (float Width, float Height) defaults,
                                    (float Width, float Height) measured = default)
    {
        bool vertical = panel == Orientation.Vertical;
        if (measured == default) measured = (-1, -1);
        var (width, minWidth, flexWidth) = Axis(element.Width, element.MinWidth, element.MaxWidth, defaults.Width, measured.Width,
                                                across: vertical, fill, keepAsMin: false);
        var (height, minHeight, flexHeight) = Axis(element.Height, element.MinHeight, element.MaxHeight, defaults.Height, measured.Height,
                                                   across: !vertical, fill, keepAsMin: true);
        return new LayoutSizes(width, height, minWidth, minHeight, flexWidth, flexHeight);
    }

    private static (float Preferred, float Min, float Flexible) Axis(double size, double min, double max, float own, float measured,
                                                                     bool across, bool fill, bool keepAsMin)
    {
        float preferred = double.IsNaN(size) ? own : (float)size;
        if (IsLimit(max))
        {
            float limit = (float)Math.Max(0, max);
            if (preferred >= 0) preferred = Math.Min(preferred, limit);
            else if (across) preferred = limit;
            else if (measured >= 0) preferred = Math.Min(measured, limit);
        }
        float minimum = min > 0 ? (float)min : -1;
        if (keepAsMin && preferred > 0) minimum = Math.Max(minimum, preferred);
        if (minimum > 0 && preferred >= 0 && preferred < minimum) preferred = minimum;
        float flexible = across && fill ? 1 : 0;
        return (preferred, minimum, flexible);
    }

    /// <summary>A finite maximum (infinity and NaN mean none).</summary>
    public static bool IsLimit(double max) => !double.IsNaN(max) && !double.IsPositiveInfinity(max);
}

/// <summary>What a view puts in its LayoutElement (-1: none).</summary>
internal readonly record struct LayoutSizes(float PreferredWidth, float PreferredHeight, float MinWidth, float MinHeight,
                                            float FlexibleWidth, float FlexibleHeight);

/// <summary>An element's place across its panel.</summary>
internal enum LayoutAlign
{
    Stretch,
    Start,
    Center,
    End,
}
