using System;
using System.Windows.Input;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A card, as the game shows the gods on its deeds page: a portrait, a title and a subtitle under it, and the orange
/// frame of a selected card. Its size is the game's (187 wide); a <see cref="Control.Width"/> scales the whole card. A
/// click raises <see cref="Click"/>, then runs <see cref="Command"/>. In a row or grid: a <see cref="UiWrapPanel"/>.
/// </summary>
public sealed class UiCard : Control
{
    public static readonly MjolnirProperty<Bitmap?> ImageProperty =
        MjolnirProperty.Register<UiCard, Bitmap?>(nameof(Image), null, DirtyFlags.Image);

    public static readonly MjolnirProperty<string> TitleProperty =
        MjolnirProperty.Register<UiCard, string>(nameof(Title), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<string> SubtitleProperty =
        MjolnirProperty.Register<UiCard, string>(nameof(Subtitle), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<bool> IsSelectedProperty =
        MjolnirProperty.Register<UiCard, bool>(nameof(IsSelected), false, DirtyFlags.Value);

    public static readonly MjolnirProperty<ICommand?> CommandProperty =
        MjolnirProperty.Register<UiCard, ICommand?>(nameof(Command), null, DirtyFlags.Enabled);

    public static readonly MjolnirProperty<object?> CommandParameterProperty =
        MjolnirProperty.Register<UiCard, object?>(nameof(CommandParameter), null, DirtyFlags.Enabled);

    private readonly CommandSource _command;

    public UiCard() => _command = new CommandSource(this, () => Command, () => CommandParameter);

    /// <summary>The portrait, kept in its proportions; null shows none.</summary>
    public Bitmap? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>The name under the portrait.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The line under the name.</summary>
    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Whether the card shows the orange frame of a selected one.</summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>What a click does; while its CanExecute is false the card is dimmed.</summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Passed to the command's CanExecute and Execute.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Raised when the player clicks the card.</summary>
    public event EventHandler? Click
    {
        add => _command.AddClick(value);
        remove => _command.RemoveClick(value);
    }

    /// <summary>The game's card width; a <see cref="Control.Width"/> scales the card from it.</summary>
    internal const double GameWidth = 187;

    /// <summary>How much the card is scaled: Width over the game's width, 1 without a Width.</summary>
    internal double Scale => GetValue(WidthProperty) is var width && width > 0 ? width / GameWidth : 1;

    internal override bool ShownEnabled => IsEnabled && _command.CanExecute;

    /// <summary>The player clicked (called by its view): the Click handlers, then the command.</summary>
    internal void OnClick() => _command.Click();

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        _command.PropertyChanged(e, CommandProperty, CommandParameterProperty);
        base.Changed(e, dirty);
    }

    private protected override void Attached() => _command.Listen();

    private protected override void Detached() => _command.Listen();

    public override string ToString() => $"UiCard '{GetStored(TitleProperty)}'";
}
