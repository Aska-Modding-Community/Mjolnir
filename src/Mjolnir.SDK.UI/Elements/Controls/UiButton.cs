using System;
using System.Windows.Input;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A button in the game's style. Without a <see cref="HeaderedControl.Header"/> a standalone button (300×36 by default,
/// centred); with one, a settings row with the header left and the button right. A click raises <see cref="Click"/>, then
/// runs <see cref="Command"/>, whose CanExecute dims the button while false.
/// </summary>
public sealed class UiButton : HeaderedControl
{
    public static readonly MjolnirProperty<string> ContentProperty =
        MjolnirProperty.Register<UiButton, string>(nameof(Content), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<ICommand?> CommandProperty =
        MjolnirProperty.Register<UiButton, ICommand?>(nameof(Command), null, DirtyFlags.Enabled);

    public static readonly MjolnirProperty<object?> CommandParameterProperty =
        MjolnirProperty.Register<UiButton, object?>(nameof(CommandParameter), null, DirtyFlags.Enabled);

    private readonly CommandSource _command;

    public UiButton() => _command = new CommandSource(this, () => Command, () => CommandParameter);

    /// <summary>The text on the button.</summary>
    public string Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>What a click does (a <see cref="Command"/> or any ICommand); while its CanExecute is false the button is dimmed.</summary>
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

    /// <summary>Raised when the player clicks. Each handler runs on its own: one that throws disables its mod, the others still run.</summary>
    public event EventHandler? Click
    {
        add => _command.AddClick(value);
        remove => _command.RemoveClick(value);
    }

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

    public override string ToString() =>
        GetStored(HeaderProperty) is { } header ? $"UiButton '{header}'" : $"UiButton '{GetStored(ContentProperty)}'";
}
