using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A scrolling area of its own, vertical: its <see cref="Content"/> scrolls inside the
/// viewer's <see cref="Control.Height"/> (300 by default) when taller, with the game's scrollbar. The mouse wheel scrolls
/// the viewer it is over. For long lists in a window that has other things to show.
/// </summary>
public sealed class UiScrollViewer : Control
{
    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<UiScrollViewer, Control?>(nameof(Content), null, DirtyFlags.Children);

    /// <summary>What scrolls.</summary>
    public Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => OwnChild(Content);

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        ContentChanged(e, ContentProperty);
        base.Changed(e, dirty);
    }
}
