using System;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The shared part of <see cref="UiProgressBar"/> and <see cref="UiProgressRing"/>: a value
/// between <see cref="Minimum"/> and <see cref="Maximum"/>, an optional text and a colour.
/// </summary>
public abstract class ProgressBase : Control
{
    public static readonly MjolnirProperty<double> MinimumProperty =
        MjolnirProperty.Register<ProgressBase, double>(nameof(Minimum), 0, DirtyFlags.Range | DirtyFlags.Value);

    public static readonly MjolnirProperty<double> MaximumProperty =
        MjolnirProperty.Register<ProgressBase, double>(nameof(Maximum), 100, DirtyFlags.Range | DirtyFlags.Value);

    /// <summary>Kept inside the limits; the value asked for is remembered, so widening the limits again brings it back.</summary>
    public static readonly MjolnirProperty<double> ValueProperty =
        MjolnirProperty.Register<ProgressBase, double>(nameof(Value), 0, DirtyFlags.Value, coerce: static (bar, value) => bar.Clamp(value));

    public static readonly MjolnirProperty<bool> ShowProgressTextProperty =
        MjolnirProperty.Register<ProgressBase, bool>(nameof(ShowProgressText), false, DirtyFlags.Text);

    public static readonly MjolnirProperty<string> ProgressTextFormatProperty =
        MjolnirProperty.Register<ProgressBase, string>(nameof(ProgressTextFormat), DefaultFormat, DirtyFlags.Text,
                                                     coerce: static (_, value) => string.IsNullOrEmpty(value) ? DefaultFormat : value);

    public static readonly MjolnirProperty<Color?> ForegroundProperty =
        MjolnirProperty.Register<ProgressBase, Color?>(nameof(Foreground), null, DirtyFlags.Style);

    public static readonly MjolnirProperty<Color?> ProgressTextForegroundProperty =
        MjolnirProperty.Register<ProgressBase, Color?>(nameof(ProgressTextForeground), null, DirtyFlags.Style);

    private const string DefaultFormat = "{1:0}%";

    private ModHandlers<EventHandler<RangeValueChangedEventArgs>>? _valueChanged;

    private protected ProgressBase() => CoerceValue(ValueProperty); // stored, so a change of the limits reports the value moving

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Whether the bar shows <see cref="ProgressTextFormat"/> on it (off by default).</summary>
    public bool ShowProgressText
    {
        get => GetValue(ShowProgressTextProperty);
        set => SetValue(ShowProgressTextProperty, value);
    }

    /// <summary>
    /// The text on the bar, a .NET format with {0} the value, {1} the percentage, {2} the minimum and {3} the maximum:
    /// <c>"{1:0}%"</c> by default, <c>"{0:0}/{3:0}"</c> for the game's own "200/200". A broken format shows the default.
    /// </summary>
    public string ProgressTextFormat
    {
        get => GetValue(ProgressTextFormatProperty);
        set => SetValue(ProgressTextFormatProperty, value);
    }

    /// <summary>The fill's colour; null for the game's own (the health bar's red, the food ring's orange).</summary>
    public Color? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The colour of the text (<see cref="ShowProgressText"/>); null for the game's own (a mint like its HUD's numbers).</summary>
    public Color? ProgressTextForeground
    {
        get => GetValue(ProgressTextForegroundProperty);
        set => SetValue(ProgressTextForegroundProperty, value);
    }

    /// <summary>How full the bar is, 0 to 100 (0 when <see cref="Minimum"/> and <see cref="Maximum"/> are equal).</summary>
    public double Percentage
    {
        get
        {
            double low = Math.Min(Minimum, Maximum), high = Math.Max(Minimum, Maximum);
            return high > low ? (Value - low) / (high - low) * 100 : 0;
        }
    }

    /// <summary>Raised after <see cref="Value"/> changed: by code or by its binding.</summary>
    public event EventHandler<RangeValueChangedEventArgs>? ValueChanged
    {
        add => ModHandlers<EventHandler<RangeValueChangedEventArgs>>.Of(ref _valueChanged, $"{this}.ValueChanged").Add(value);
        remove => _valueChanged?.Remove(value);
    }

    /// <summary>The text the bar shows: <see cref="ProgressTextFormat"/> applied, in the player's culture.</summary>
    internal string ProgressText
    {
        get
        {
            object[] values = [Value, Percentage, Minimum, Maximum];
            try
            {
                return string.Format(Localization.Culture, ProgressTextFormat, values);
            }
            catch (FormatException)
            {
                return string.Format(Localization.Culture, DefaultFormat, values);
            }
        }
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, MinimumProperty) || ReferenceEquals(e.Property, MaximumProperty))
            CoerceValue(ValueProperty);
        else if (ReferenceEquals(e.Property, ValueProperty) && _valueChanged != null)
        {
            var args = new RangeValueChangedEventArgs(e.GetOldValue<double>(), e.GetNewValue<double>());
            _valueChanged.Raise(h => h(this, args));
        }
    }

    // Under the store's lock: reads the stored limits only.
    private double Clamp(double value) => Ranges.Clamp(value, GetStored(MinimumProperty), GetStored(MaximumProperty));
}

/// <summary>
/// A bar filled from <see cref="ProgressBase.Minimum"/> to <see cref="ProgressBase.Maximum"/> by
/// <see cref="ProgressBase.Value"/>, in the look of the game's health bar: its frame, dark
/// background and bar, with the text centred on it. It stretches across its panel and is as tall as the game's bar
/// (29 px). For the HUD (durability, heat, a mod's own meter) and for windows:
/// <c>bar.Bind(UiProgressBar.ValueProperty, () => tool.Durability, TimeSpan.FromSeconds(0.25))</c>.
/// </summary>
public sealed class UiProgressBar : ProgressBase
{
}

/// <summary>
/// A ring filled clockwise from the top, in the look of the game's food, water and warmth
/// rings in the HUD: an optional <see cref="Icon"/> in its middle and the text under it. <see cref="Control.Width"/> is
/// its diameter, 100 by default (the game's): the ring, its icon and its text scale together, and it takes that width
/// and the height of the ring and the text. For a mod's own meter next to the game's:
/// <c>new UiProgressRing { Width = 64, Icon = new Bitmap(path), ShowProgressText = true }</c>.
/// </summary>
public sealed class UiProgressRing : ProgressBase
{
    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<UiProgressRing, Bitmap?>(nameof(Icon), null, DirtyFlags.Image);

    /// <summary>The game's ring: its diameter when <see cref="Control.Width"/> is not set.</summary>
    internal const double GameDiameter = 100;

    /// <summary>How much the game's ring is scaled: <see cref="Control.Width"/> over its diameter, 1 without a width.</summary>
    internal double Scale => GetValue(WidthProperty) is var width && width > 0 ? width / GameDiameter : 1;

    /// <summary>The picture in the ring's middle; null for none.</summary>
    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
