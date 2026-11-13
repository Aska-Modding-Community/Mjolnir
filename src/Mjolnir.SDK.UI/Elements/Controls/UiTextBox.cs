using System;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A single-line text input, the game's rename field. The player's text arrives when they finish (Enter or leaving the
/// field), or at every keystroke with <see cref="UpdateSourceTrigger"/> set to <see cref="UI.UpdateSourceTrigger.PropertyChanged"/>
/// (a search field). <see cref="Text"/> binds two-way by default; bound to a number setting it converts, and text that is
/// not a number goes back to the setting's value.
/// </summary>
public sealed class UiTextBox : HeaderedControl
{
    public static readonly MjolnirProperty<string> TextProperty =
        MjolnirProperty.Register<UiTextBox, string>(nameof(Text), "", DirtyFlags.Value, BindingMode.TwoWay, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<int> MaxLengthProperty =
        MjolnirProperty.Register<UiTextBox, int>(nameof(MaxLength), 0, DirtyFlags.Range);

    public static readonly MjolnirProperty<string?> PlaceholderTextProperty =
        MjolnirProperty.Register<UiTextBox, string?>(nameof(PlaceholderText), null, DirtyFlags.Range);

    public static readonly MjolnirProperty<TextInputContentType> ContentTypeProperty =
        MjolnirProperty.Register<UiTextBox, TextInputContentType>(nameof(ContentType), TextInputContentType.Normal, DirtyFlags.Range);

    public static readonly MjolnirProperty<TextAlignment?> TextAlignmentProperty =
        MjolnirProperty.Register<UiTextBox, TextAlignment?>(nameof(TextAlignment), null, DirtyFlags.Style);

    public static readonly MjolnirProperty<UpdateSourceTrigger> UpdateSourceTriggerProperty =
        MjolnirProperty.Register<UiTextBox, UpdateSourceTrigger>(nameof(UpdateSourceTrigger), UpdateSourceTrigger.LostFocus, DirtyFlags.None);

    private ModHandlers<EventHandler<TextChangedEventArgs>>? _textChanged;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The most characters the player can type; 0 for no limit.</summary>
    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>Shown greyed while the field is empty.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>What the player can type: any text, a whole number, a decimal number.</summary>
    public TextInputContentType ContentType
    {
        get => GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    /// <summary>Where the text stands in the field; null for the game's own (centred, as its rename field). A search field reads better on the left.</summary>
    public TextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>When the player's text reaches <see cref="Text"/> (and the binding's source): when they finish, the default, or as they type.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger
    {
        get => GetValue(UpdateSourceTriggerProperty);
        set => SetValue(UpdateSourceTriggerProperty, value);
    }

    /// <summary>
    /// Raised after <see cref="Text"/> changed: when the player finishes editing (or at every change they make, with
    /// <see cref="UI.UpdateSourceTrigger.PropertyChanged"/>), or by code or a binding.
    /// </summary>
    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => ModHandlers<EventHandler<TextChangedEventArgs>>.Of(ref _textChanged, $"{this}.TextChanged").Add(value);
        remove => _textChanged?.Remove(value);
    }

    /// <summary>The player finished editing, or typed with <see cref="UI.UpdateSourceTrigger.PropertyChanged"/> (called by its view).</summary>
    internal void SetFromView(string text) => Text = text;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, TextProperty) && _textChanged != null)
        {
            var args = new TextChangedEventArgs(e.GetOldValue<string>(), e.GetNewValue<string>());
            _textChanged.Raise(h => h(this, args));
        }
    }
}

/// <summary>When a <see cref="UiTextBox"/>'s text reaches its <see cref="UiTextBox.Text"/> and the source it is bound to.</summary>
public enum UpdateSourceTrigger
{
    /// <summary>When the player finishes: Enter, or leaving the field.</summary>
    LostFocus,

    /// <summary>At every change the player makes, as they type: a search field filtering a list.</summary>
    PropertyChanged,
}
