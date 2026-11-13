using System;

namespace Mjolnir.SDK.UI;

/// <summary>
/// An on/off setting, the game's "‹ On ›" selector. <see cref="IsChecked"/> binds two-way by default:
/// <c>toggle.Bind(UiToggleSwitch.IsCheckedProperty, config.Fog)</c>.
/// </summary>
public sealed class UiToggleSwitch : HeaderedControl
{
    public static readonly MjolnirProperty<bool> IsCheckedProperty =
        MjolnirProperty.Register<UiToggleSwitch, bool>(nameof(IsChecked), false, DirtyFlags.Value, BindingMode.TwoWay);

    public static readonly MjolnirProperty<string?> OnContentProperty =
        MjolnirProperty.Register<UiToggleSwitch, string?>(nameof(OnContent), null, DirtyFlags.Options);

    public static readonly MjolnirProperty<string?> OffContentProperty =
        MjolnirProperty.Register<UiToggleSwitch, string?>(nameof(OffContent), null, DirtyFlags.Options);

    private ModHandlers<EventHandler<ValueChangedEventArgs<bool>>>? _isCheckedChanged;

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>The word shown when on; null for the game's own ("On" in the player's language).</summary>
    public string? OnContent
    {
        get => GetValue(OnContentProperty);
        set => SetValue(OnContentProperty, value);
    }

    /// <summary>The word shown when off; null for the game's own.</summary>
    public string? OffContent
    {
        get => GetValue(OffContentProperty);
        set => SetValue(OffContentProperty, value);
    }

    /// <summary>Raised after <see cref="IsChecked"/> changed: by the player, by code or by its binding, with the old and the new state.</summary>
    public event EventHandler<ValueChangedEventArgs<bool>>? IsCheckedChanged
    {
        add => ModHandlers<EventHandler<ValueChangedEventArgs<bool>>>.Of(ref _isCheckedChanged, $"{this}.IsCheckedChanged").Add(value);
        remove => _isCheckedChanged?.Remove(value);
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, IsCheckedProperty) && _isCheckedChanged != null)
        {
            var args = new ValueChangedEventArgs<bool>(e.GetOldValue<bool>(), e.GetNewValue<bool>());
            _isCheckedChanged.Raise(h => h(this, args));
        }
    }
}
