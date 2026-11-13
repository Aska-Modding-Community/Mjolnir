using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// One child with space around it inside (<see cref="Control.Padding"/>, none by default) and an optional background.
/// </summary>
public sealed class UiBorder : Control
{
    public static readonly MjolnirProperty<Control?> ChildProperty =
        MjolnirProperty.Register<UiBorder, Control?>(nameof(Child), null, DirtyFlags.Children);

    public static readonly MjolnirProperty<Color?> BackgroundProperty =
        MjolnirProperty.Register<UiBorder, Color?>(nameof(Background), null, DirtyFlags.Style);

    public Control? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>A plain colour behind the child; null for none.</summary>
    public Color? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => OwnChild(Child);

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        ContentChanged(e, ChildProperty);
        base.Changed(e, dirty);
    }
}
