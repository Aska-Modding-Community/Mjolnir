using System;
using System.Windows.Input;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A slot, as the game shows an item in its journal and inventory: the slot's frame with a picture, the count in its
/// corner, a strip that fills up (durability, freshness, progress) and the amber frame of a selected slot. The game's
/// size by default (the inventory's 70 × 70); Width and Height size it. A click raises <see cref="Click"/>, then runs <see cref="Command"/>; give it a
/// <see cref="Control.ToolTip"/> for what it holds. In a grid: a <see cref="UiWrapPanel"/>.
/// </summary>
public sealed class UiSlot : Control
{
    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<UiSlot, Bitmap?>(nameof(Icon), null, DirtyFlags.Image);

    public static readonly MjolnirProperty<int?> CountProperty =
        MjolnirProperty.Register<UiSlot, int?>(nameof(Count), null, DirtyFlags.Text);

    public static readonly MjolnirProperty<double?> FillFractionProperty =
        MjolnirProperty.Register<UiSlot, double?>(nameof(FillFraction), null, DirtyFlags.Value,
                                                coerce: static (_, value) => value is { } v ? Math.Clamp(v, 0, 1) : null);

    public static readonly MjolnirProperty<bool> IsSelectedProperty =
        MjolnirProperty.Register<UiSlot, bool>(nameof(IsSelected), false, DirtyFlags.Value);

    public static readonly MjolnirProperty<ICommand?> CommandProperty =
        MjolnirProperty.Register<UiSlot, ICommand?>(nameof(Command), null, DirtyFlags.Enabled);

    public static readonly MjolnirProperty<object?> CommandParameterProperty =
        MjolnirProperty.Register<UiSlot, object?>(nameof(CommandParameter), null, DirtyFlags.Enabled);

    private readonly CommandSource _command;

    public UiSlot() => _command = new CommandSource(this, () => Command, () => CommandParameter);

    /// <summary>The picture in the slot; null for an empty slot.</summary>
    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The number in the slot's corner; null (the default) shows none.</summary>
    public int? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>How full the strip along the slot's top is, 0 to 1; null (the default) shows no strip.</summary>
    public double? FillFraction
    {
        get => GetValue(FillFractionProperty);
        set => SetValue(FillFractionProperty, value);
    }

    /// <summary>Whether the slot shows the amber frame of a selected one.</summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>What a click does; while its CanExecute is false the slot is dimmed.</summary>
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

    /// <summary>Raised when the player clicks the slot.</summary>
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
}
