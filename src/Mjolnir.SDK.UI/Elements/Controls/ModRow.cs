using System;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A row of the Mods window (internal: the SDK's own window): a mod's picture, name and status line, the game's
/// "‹ On ›" selector for whether the mod loads at the next start, and left of it a Settings button when the mod has
/// settings (<see cref="SettingsText"/>).
/// </summary>
internal sealed class ModRow : HeaderedControl
{
    public static readonly MjolnirProperty<bool> IsOnProperty =
        MjolnirProperty.Register<ModRow, bool>(nameof(IsOn), true, DirtyFlags.Value);

    // The button appearing or going changes the row's shape: the row is built again.
    public static readonly MjolnirProperty<string?> SettingsTextProperty =
        MjolnirProperty.Register<ModRow, string?>(nameof(SettingsText), null, DirtyFlags.Text,
                                                  reshape: static (old, now) => (old == null) != (now == null) ? DirtyFlags.Rebuild : DirtyFlags.None);

    private ModHandlers<EventHandler>? _toggled;
    private ModHandlers<EventHandler>? _settingsClick;

    /// <summary>Whether the selector shows On.</summary>
    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>The Settings button's text; null for no button.</summary>
    public string? SettingsText
    {
        get => GetValue(SettingsTextProperty);
        set => SetValue(SettingsTextProperty, value);
    }

    /// <summary>Raised when the player flips the selector (<see cref="IsOn"/> already changed); not when code sets it.</summary>
    public event EventHandler? Toggled
    {
        add => ModHandlers<EventHandler>.Of(ref _toggled, $"{this}.Toggled").Add(value);
        remove => _toggled?.Remove(value);
    }

    /// <summary>Raised when the player clicks the Settings button.</summary>
    public event EventHandler? SettingsClick
    {
        add => ModHandlers<EventHandler>.Of(ref _settingsClick, $"{this}.SettingsClick").Add(value);
        remove => _settingsClick?.Remove(value);
    }

    /// <summary>The player flipped the selector (called by its view).</summary>
    internal void SetOnFromView(bool on)
    {
        IsOn = on;
        _toggled?.Raise(h => h(this, EventArgs.Empty));
    }

    /// <summary>The player clicked Settings (called by its view).</summary>
    internal void OnSettingsClick() => _settingsClick?.Raise(h => h(this, EventArgs.Empty));
}
