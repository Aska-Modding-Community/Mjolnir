using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A reusable piece of UI of your own: derive, build the elements in the constructor and set
/// <see cref="Content"/>. Setting it again replaces what is shown.
/// </summary>
[SuppressMessage("ApiDesign", "RS0022", Justification = "Made to be derived by mods: its protected members are API")]
public class UiUserControl : Control
{
    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<UiUserControl, Control?>(nameof(Content), null, DirtyFlags.Children);

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
