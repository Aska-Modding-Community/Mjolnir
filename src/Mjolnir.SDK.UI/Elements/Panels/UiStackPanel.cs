using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Children in a column (the default) or a row, <see cref="Spacing"/> apart. The children can change at any time on the
/// main thread; whatever is on screen follows.
/// </summary>
public sealed class UiStackPanel : Panel
{
    public static readonly MjolnirProperty<Orientation> OrientationProperty =
        MjolnirProperty.Register<UiStackPanel, Orientation>(nameof(Orientation), Orientation.Vertical, DirtyFlags.Rebuild);

    /// <summary>10 by default, the spacing of the game's own lists.</summary>
    public static readonly MjolnirProperty<double> SpacingProperty =
        MjolnirProperty.Register<UiStackPanel, double>(nameof(Spacing), 10, DirtyFlags.Layout);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Space between children.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}
