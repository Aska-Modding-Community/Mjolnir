namespace Mjolnir.SDK.UI;

/// <summary>
/// A slot of the game's quick bar, the row 1–0 at the bottom of the HUD: its frame with a picture, the key under it and the
/// game's orange frame of the slot in hand. For a tool of a mod's own with modes chosen by number: put a row of them in
/// the quick bar's place (<see cref="Hud.AddInPlaceOf{T}"/>), the mode in use <see cref="IsSelected"/>. The game's size
/// (80 × 80, the key below). It shows only: the HUD takes no clicks.
/// </summary>
/// <example><code>
/// var bar = new UiStackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
/// bar.Children.Add(new UiQuickSlot { Icon = new Bitmap(path), Key = "1", IsSelected = true });
/// Hud.AddInPlaceOf(bar, HudPart.QuickBar);
/// </code></example>
public sealed class UiQuickSlot : Control
{
    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<UiQuickSlot, Bitmap?>(nameof(Icon), null, DirtyFlags.Image);

    public static readonly MjolnirProperty<string> KeyProperty =
        MjolnirProperty.Register<UiQuickSlot, string>(nameof(Key), "", DirtyFlags.Text);

    public static readonly MjolnirProperty<bool> IsSelectedProperty =
        MjolnirProperty.Register<UiQuickSlot, bool>(nameof(IsSelected), false, DirtyFlags.Value);

    /// <summary>The picture in the slot; null for an empty slot.</summary>
    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The key written under the slot, as the game writes 1–0; empty for none.</summary>
    public string Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value ?? "");
    }

    /// <summary>Whether the slot shows the game's frame of the slot in hand.</summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}
