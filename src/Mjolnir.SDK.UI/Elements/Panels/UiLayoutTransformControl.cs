using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Draws its child bigger or smaller and makes room for it (a scale only: no
/// rotation or skew). The child is laid out for the width this control gets divided by <see cref="Scale"/>, then drawn at
/// that scale, and this control is as tall as the scaled child: rows, texts, pictures and buttons inside all scale
/// together. <c>new UiLayoutTransformControl { Scale = 0.8, Child = compactRows }</c>.
/// </summary>
public sealed class UiLayoutTransformControl : Control
{
    public static readonly MjolnirProperty<Control?> ChildProperty =
        MjolnirProperty.Register<UiLayoutTransformControl, Control?>(nameof(Child), null, DirtyFlags.Children);

    public static readonly MjolnirProperty<double> ScaleProperty =
        MjolnirProperty.Register<UiLayoutTransformControl, double>(nameof(Scale), 1, DirtyFlags.Layout,
                                                                   coerce: static (_, value) => double.IsNaN(value) || value <= 0 ? 1 : Math.Clamp(value, 0.1, 10));

    /// <summary>What is drawn scaled.</summary>
    public Control? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>How much bigger (above 1) or smaller (below 1) the child is drawn; 1 by default, kept within 0.1–10.</summary>
    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => OwnChild(Child);

    /// <summary>The width the child is laid out for when this control gets <paramref name="width"/> (no Unity: unit-tested).</summary>
    internal static float ChildWidth(float width, double scale) => width / (float)scale;

    /// <summary>The height this control takes for a child <paramref name="childHeight"/> tall as laid out (no Unity: unit-tested).</summary>
    internal static float OwnHeight(float childHeight, double scale) => childHeight * (float)scale;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        ContentChanged(e, ChildProperty);
        base.Changed(e, dirty);
    }
}
