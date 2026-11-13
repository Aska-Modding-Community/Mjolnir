using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A section that opens and closes: a <see cref="Header"/> row the player clicks, with an arrow,
/// and the <see cref="Content"/> below it while <see cref="IsExpanded"/>, on the panel the journal shows under a
/// category's header. For mods with many settings: a group each,
/// closed until wanted. <see cref="IsExpanded"/> binds two-way by default, so a config entry can remember it.
/// </summary>
/// <example><code>
/// var advanced = new UiExpander { Header = "Advanced", Content = new UiStackPanel { Children = { SettingControls.For(config.Speed) } } };
/// </code></example>
public sealed class UiExpander : Control
{
    public static readonly MjolnirProperty<string?> HeaderProperty =
        MjolnirProperty.Register<UiExpander, string?>(nameof(Header), null, DirtyFlags.Label);

    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<UiExpander, Control?>(nameof(Content), null, DirtyFlags.Children);

    public static readonly MjolnirProperty<bool> IsExpandedProperty =
        MjolnirProperty.Register<UiExpander, bool>(nameof(IsExpanded), false, DirtyFlags.Value, BindingMode.TwoWay);

    private ModHandlers<EventHandler>? _expanded;
    private ModHandlers<EventHandler>? _collapsed;

    /// <summary>The title of the clickable row.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>What shows below the header while the expander is open.</summary>
    public Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Open (the content shows) or closed; closed by default.</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Raised after it opened: by the player, by code or by a binding.</summary>
    public event EventHandler? Expanded
    {
        add => ModHandlers<EventHandler>.Of(ref _expanded, $"{this}.Expanded").Add(value);
        remove => _expanded?.Remove(value);
    }

    /// <summary>Raised after it closed.</summary>
    public event EventHandler? Collapsed
    {
        add => ModHandlers<EventHandler>.Of(ref _collapsed, $"{this}.Collapsed").Add(value);
        remove => _collapsed?.Remove(value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => OwnChild(Content);

    /// <summary>The player clicked the header (its view calls this).</summary>
    internal void Toggle() => IsExpanded = !IsExpanded;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        ContentChanged(e, ContentProperty);
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, IsExpandedProperty))
            (e.GetNewValue<bool>() ? _expanded : _collapsed)?.Raise(h => h(this, EventArgs.Empty));
    }

    public override string ToString() => GetStored(HeaderProperty) is { } header ? $"UiExpander '{header}'" : "UiExpander";
}
