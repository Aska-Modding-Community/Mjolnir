using Mjolnir.SDK;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A key and what it does, as the game's HUD prompts show them («[Space] Jump»): <see cref="Gesture"/> drawn as the
/// game's own key pictures (modifiers first: Ctrl + F7), <see cref="Text"/> beside it. A key the game has no picture for
/// is written out. The mouse's buttons are <c>LeftButton</c>, <c>RightButton</c> and <c>MiddleButton</c>, its wheel
/// <c>Wheel</c> (written out in the player's language: the game has no picture of it), modifiers allowed:
/// <c>KeyGesture.Parse("Shift+Wheel")</c>. Bind the gesture to a hotkey setting and the hint follows a rebinding:
/// <c>hint.Bind(UiKeyHint.GestureProperty, settings.ToggleKey)</c>. On the HUD or in a window.
/// </summary>
public sealed class UiKeyHint : Control
{
    public static readonly MjolnirProperty<KeyGesture> GestureProperty =
        MjolnirProperty.Register<UiKeyHint, KeyGesture>(nameof(Gesture), KeyGesture.None, DirtyFlags.Value);

    public static readonly MjolnirProperty<string> TextProperty =
        MjolnirProperty.Register<UiKeyHint, string>(nameof(Text), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    /// <summary>The keys to show; <see cref="KeyGesture.None"/> shows the text alone.</summary>
    public KeyGesture Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>What the keys do.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
