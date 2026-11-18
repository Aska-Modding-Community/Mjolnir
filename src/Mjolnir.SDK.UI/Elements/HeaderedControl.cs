namespace Mjolnir.SDK.UI;

/// <summary>
/// A control that can show as a row of the game's settings: with a <see cref="Header"/>, the header on the left, the
/// control on the right, an optional <see cref="Description"/> under the header and an optional <see cref="Icon"/> left
/// of both; without a header, the bare control.
/// </summary>
public abstract class HeaderedControl : Control
{
    public static readonly MjolnirProperty<string?> HeaderProperty =
        MjolnirProperty.Register<HeaderedControl, string?>(nameof(Header), null, DirtyFlags.Label,
                                                       reshape: static (old, now) => (old == null) != (now == null) ? DirtyFlags.Rebuild : DirtyFlags.None);

    public static readonly MjolnirProperty<string?> DescriptionProperty =
        MjolnirProperty.Register<HeaderedControl, string?>(nameof(Description), null, DirtyFlags.Label,
                                                       reshape: static (old, now) => string.IsNullOrWhiteSpace(old) != string.IsNullOrWhiteSpace(now)
                                                                                        ? DirtyFlags.Rebuild : DirtyFlags.None);

    public static readonly MjolnirProperty<bool> ToolTipOnIconProperty =
        MjolnirProperty.Register<HeaderedControl, bool>(nameof(ToolTipOnIcon), false, DirtyFlags.ToolTip);

    // A picture appearing or going changes the row's shape (the label moves): the row is built again.
    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<HeaderedControl, Bitmap?>(nameof(Icon), null, DirtyFlags.Label,
                                                       reshape: static (old, now) => (old == null) != (now == null) ? DirtyFlags.Rebuild : DirtyFlags.None);

    // The width of the control part is fixed when the row is built: a new one builds the row again.
    public static readonly MjolnirProperty<double> ContentWidthProperty =
        MjolnirProperty.Register<HeaderedControl, double>(nameof(ContentWidth), double.NaN, DirtyFlags.Rebuild);

    /// <summary>The tile in place of a missing picture (the Mods window's rows); internal: mods give a picture.</summary>
    internal static readonly MjolnirProperty<Monogram?> MonogramProperty =
        MjolnirProperty.Register<HeaderedControl, Monogram?>(nameof(Monogram), null, DirtyFlags.Label,
                                                         reshape: static (old, now) => old.HasValue != now.HasValue ? DirtyFlags.Rebuild : DirtyFlags.None);

    private protected HeaderedControl() { }

    /// <summary>The text left of the control; null for the bare control.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// A text under the header (rows only; the first line of the text). It wraps over up to three lines, the row growing
    /// for them, and a longer one is cut with an ellipsis.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Whether <see cref="Control.ToolTip"/> belongs to the row's <see cref="Icon"/> rather than to the whole row:
    /// the card then shows only while the pointer is on the picture, and stands beside it, as the game's own card of
    /// an item stands beside the slot under the pointer. Without a picture the row behaves as it always does.
    /// </summary>
    public bool ToolTipOnIcon
    {
        get => GetValue(ToolTipOnIconProperty);
        set => SetValue(ToolTipOnIconProperty, value);
    }

    /// <summary>
    /// A picture left of the header (rows only): a square as tall as the row allows, 48 px in a row with a description,
    /// the picture's proportions kept (<c>row.Icon = new Bitmap(path)</c>).
    /// </summary>
    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// The width of a row's control part (the right part: selector, slider, list, field, button), in reference pixels; NaN
    /// (the default) for the game's 380. A narrow control leaves the header and the description more room; the game's
    /// control parts keep the size of their arrows and buttons, their widest part takes the difference. Rows only; a bare
    /// control is sized by <see cref="Control.Width"/>.
    /// </summary>
    public double ContentWidth
    {
        get => GetValue(ContentWidthProperty);
        set => SetValue(ContentWidthProperty, value);
    }

    internal Monogram? Monogram
    {
        get => GetValue(MonogramProperty);
        set => SetValue(MonogramProperty, value);
    }

    public override string ToString() => GetStored(HeaderProperty) is { } header ? $"{GetType().Name} '{header}'" : base.ToString();
}
