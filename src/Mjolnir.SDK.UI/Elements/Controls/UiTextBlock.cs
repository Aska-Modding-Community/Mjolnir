using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Text. Without a <see cref="HeaderedControl.Header"/>, a paragraph (rich text tags work); with one, a settings row
/// with the header left and this text right (a status, a count). Bind <see cref="TextProperty"/> to keep it current:
/// <c>text.Bind(UiTextBlock.TextProperty, () => $"HP {hp}")</c>.
/// </summary>
public sealed class UiTextBlock : HeaderedControl
{
    public static readonly MjolnirProperty<string> TextProperty =
        MjolnirProperty.Register<UiTextBlock, string>(nameof(Text), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<Color?> ForegroundProperty =
        MjolnirProperty.Register<UiTextBlock, Color?>(nameof(Foreground), null, DirtyFlags.Style);

    public static readonly MjolnirProperty<TextAlignment?> TextAlignmentProperty =
        MjolnirProperty.Register<UiTextBlock, TextAlignment?>(nameof(TextAlignment), null, DirtyFlags.Style);

    public static readonly MjolnirProperty<TextWrapping> TextWrappingProperty =
        MjolnirProperty.Register<UiTextBlock, TextWrapping>(nameof(TextWrapping), TextWrapping.Wrap, DirtyFlags.Style);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Text colour; null for the game's.</summary>
    public Color? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Alignment of the lines; null for the place's own (paragraphs centred in a window, HUD texts by their corner).</summary>
    public TextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>Whether long lines wrap (by default they do).</summary>
    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }
}

/// <summary>A section title, like "Camera" in the game's settings.</summary>
public sealed class UiSectionHeader : Control
{
    public static readonly MjolnirProperty<string> TextProperty =
        MjolnirProperty.Register<UiSectionHeader, string>(nameof(Text), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public override string ToString() => $"{nameof(UiSectionHeader)} '{GetStored(TextProperty)}'";
}
