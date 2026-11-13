using System;
using System.Globalization;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A number on a slider from <see cref="Minimum"/> to <see cref="Maximum"/>, its value shown on the right.
/// <see cref="Value"/> binds two-way by default and converts to the source's type:
/// <c>slider.Bind(UiSlider.ValueProperty, config.Villagers)</c> with an int setting. For whole steps set
/// <see cref="IsSnapToTickEnabled"/> and <see cref="TickFrequency"/> = 1.
/// </summary>
public sealed class UiSlider : HeaderedControl
{
    public static readonly MjolnirProperty<double> MinimumProperty =
        MjolnirProperty.Register<UiSlider, double>(nameof(Minimum), 0, DirtyFlags.Range | DirtyFlags.Value);

    public static readonly MjolnirProperty<double> MaximumProperty =
        MjolnirProperty.Register<UiSlider, double>(nameof(Maximum), 100, DirtyFlags.Range | DirtyFlags.Value);

    /// <summary>Kept inside the limits; the value asked for is remembered, so widening the limits again brings it back.</summary>
    public static readonly MjolnirProperty<double> ValueProperty =
        MjolnirProperty.Register<UiSlider, double>(nameof(Value), 0, DirtyFlags.Value, BindingMode.TwoWay, coerce: static (slider, value) => slider.Clamp(value));

    public static readonly MjolnirProperty<double> TickFrequencyProperty =
        MjolnirProperty.Register<UiSlider, double>(nameof(TickFrequency), 0, DirtyFlags.Range | DirtyFlags.Value);

    public static readonly MjolnirProperty<bool> IsSnapToTickEnabledProperty =
        MjolnirProperty.Register<UiSlider, bool>(nameof(IsSnapToTickEnabled), false, DirtyFlags.Range | DirtyFlags.Value);

    public static readonly MjolnirProperty<string?> ValueFormatProperty =
        MjolnirProperty.Register<UiSlider, string?>(nameof(ValueFormat), null, DirtyFlags.Value);

    private ModHandlers<EventHandler<RangeValueChangedEventArgs>>? _valueChanged;

    public UiSlider() => CoerceValue(ValueProperty); // stored, so a change of the limits reports the value moving

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

    /// <summary>The step the player's moves snap to while <see cref="IsSnapToTickEnabled"/>.</summary>
    public double TickFrequency
    {
        get => GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public bool IsSnapToTickEnabled
    {
        get => GetValue(IsSnapToTickEnabledProperty);
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    /// <summary>A .NET number format for the value shown on the right; by default "0" for whole steps, else "0.##".</summary>
    public string? ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>Raised after <see cref="Value"/> changed: by the player, by code or by its binding.</summary>
    public event EventHandler<RangeValueChangedEventArgs>? ValueChanged
    {
        add => ModHandlers<EventHandler<RangeValueChangedEventArgs>>.Of(ref _valueChanged, $"{this}.ValueChanged").Add(value);
        remove => _valueChanged?.Remove(value);
    }

    /// <summary>The slider moves in whole steps: the game's slider can snap by itself.</summary>
    internal bool WholeSteps => IsSnapToTickEnabled && TickFrequency == 1 && Minimum % 1 == 0;

    /// <summary>The value as shown on the right.</summary>
    internal string Show(double value) =>
        value.ToString(ValueFormat ?? (IsSnapToTickEnabled && TickFrequency % 1 == 0 && TickFrequency > 0 ? "0" : "0.##"), CultureInfo.CurrentCulture);

    /// <summary>The player moved the slider (called by its view): snapped to the ticks, then set.</summary>
    internal void SetFromView(double value) => Value = Snap(value);

    internal double Snap(double value)
    {
        double tick = TickFrequency, min = Math.Min(Minimum, Maximum);
        return IsSnapToTickEnabled && tick > 0 ? min + Math.Round((value - min) / tick, MidpointRounding.AwayFromZero) * tick : value;
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
