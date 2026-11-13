using System;
using System.Collections;
using System.Reflection;
using System.Windows.Input;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>A live copy of an element was built: its Unity object, for raw Unity work (<see cref="Control.Loaded"/>).</summary>
public sealed class LoadedEventArgs(RectTransform rectTransform) : EventArgs
{
    /// <summary>The element's own object (look up what you need each time instead of keeping it: the game rebuilds the HUD and menus).</summary>
    public RectTransform RectTransform { get; } = rectTransform;
}

/// <summary>A slider's or a progress bar's value changed.</summary>
public sealed class RangeValueChangedEventArgs(double oldValue, double newValue) : ValueChangedEventArgs<double>(oldValue, newValue);

/// <summary>The selection of a <see cref="UiStepper"/>, a <see cref="UiComboBox"/>, a <see cref="UiListBox"/> or a <see cref="UiTabControl"/> changed.</summary>
public sealed class SelectionChangedEventArgs(IList removedItems, IList addedItems) : EventArgs
{
    /// <summary>The items no longer selected.</summary>
    public IList RemovedItems { get; } = removedItems;

    /// <summary>The items selected now.</summary>
    public IList AddedItems { get; } = addedItems;
}

/// <summary>The text of a <see cref="UiTextBox"/> changed.</summary>
public sealed class TextChangedEventArgs(string oldText, string newText) : ValueChangedEventArgs<string>(oldText, newText)
{
    /// <summary>The text before; the same as <see cref="ValueChangedEventArgs{T}.OldValue"/>.</summary>
    public string OldText => OldValue;

    /// <summary>The text now; the same as <see cref="ValueChangedEventArgs{T}.NewValue"/>.</summary>
    public string NewText => NewValue;
}

/// <summary>
/// A command for <see cref="UiButton.Command"/>: what a click does, and whether it can be done now (the button is
/// dimmed while it cannot). Call <see cref="RaiseCanExecuteChanged"/> when that may have changed. Any other
/// <see cref="ICommand"/> works too (CommunityToolkit's RelayCommand, ReactiveUI's ReactiveCommand…).
/// </summary>
public sealed class Command : ICommand, IHasOwner
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly ModHandlers<EventHandler> _changed = new("Command.CanExecuteChanged");

    public Command(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute == null ? null : _ => canExecute();
        Owner = execute.Method.DeclaringType?.Assembly;
    }

    /// <summary>A command taking the button's <see cref="UiButton.CommandParameter"/>.</summary>
    public Command(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        Owner = execute.Method.DeclaringType?.Assembly;
    }

    /// <summary>The mod that wrote the command's functions: a throw from them disables it.</summary>
    internal Assembly? Owner { get; }

    Assembly? IHasOwner.Owner => Owner;

    public event EventHandler? CanExecuteChanged
    {
        add => _changed.Add(value);
        remove => _changed.Remove(value);
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _execute(parameter);
    }

    /// <summary>Tells the buttons using this command to ask <see cref="CanExecute"/> again.</summary>
    public void RaiseCanExecuteChanged() => _changed.Raise(h => h(this, EventArgs.Empty));

    /// <summary>How many buttons listen (unit tests check that live buttons let go).</summary>
    internal int HandlerCount => _changed.Count;
}

/// <summary>An SDK object holding a mod's functions (a command, a data template), so a throw from them blames that mod.</summary>
internal interface IHasOwner
{
    Assembly? Owner { get; }
}
