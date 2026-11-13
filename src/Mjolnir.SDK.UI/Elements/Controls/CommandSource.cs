using System;
using System.Reflection;
using System.Windows.Input;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The click and the command of a clickable element (a button, a menu item, a slot, a card): its Click handlers, then its
/// command, listened to while the element is live only, so a long-lived command never keeps a closed element; its
/// CanExecute kept current by CanExecuteChanged and shown through the element's enabled state. The command's code is the
/// mod's: every call goes through <see cref="ModCall"/>.
/// </summary>
internal sealed class CommandSource(Control owner, Func<ICommand?> command, Func<object?> parameter)
{
    private readonly object _lock = new();
    private ICommand? _listening;
    private volatile bool _canExecute = true;
    private ModHandlers<EventHandler>? _click;

    /// <summary>The element's Click event: each handler runs on its own, one that throws disables its mod.</summary>
    public void AddClick(EventHandler? handler) => ModHandlers<EventHandler>.Of(ref _click, $"{owner}.Click").Add(handler);

    public void RemoveClick(EventHandler? handler) => _click?.Remove(handler);

    /// <summary>
    /// The player clicked (the element's view): the Click handlers, then the command, if the element shows it can run (the
    /// state kept current by CanExecuteChanged; a <see cref="UI.Command"/> checks CanExecute itself too).
    /// </summary>
    public void Click()
    {
        _click?.Raise(h => h(owner, EventArgs.Empty));
        Execute();
    }

    /// <summary>The element's property changed: when it is the command or its parameter, the new one is listened to.</summary>
    public void PropertyChanged(MjolnirPropertyChangedEventArgs e, MjolnirProperty commandProperty, MjolnirProperty parameterProperty)
    {
        if (ReferenceEquals(e.Property, commandProperty) || ReferenceEquals(e.Property, parameterProperty)) Changed();
    }

    /// <summary>Whether the command can run now (true without one).</summary>
    public bool CanExecute => _canExecute;

    /// <summary>The command or its parameter changed: listen to the new one, check it again.</summary>
    public void Changed()
    {
        Dispatcher.Run(Listen);
        _canExecute = command() is not { } now || Check(now, parameter());
    }

    /// <summary>The element went live or stopped being live (main thread).</summary>
    public void Listen()
    {
        ICommand? wanted = owner.IsLive ? command() : null, old;
        lock (_lock)
        {
            old = _listening;
            if (ReferenceEquals(old, wanted)) return;
            _listening = wanted;
        }
        if (old != null) ModCall.Run(OwnerOf(old), $"{owner} Command", () => old.CanExecuteChanged -= OnCanExecuteChanged);
        if (wanted != null)
        {
            ModCall.Run(OwnerOf(wanted), $"{owner} Command", () => wanted.CanExecuteChanged += OnCanExecuteChanged);
            OnCanExecuteChanged(null, EventArgs.Empty);
        }
    }

    /// <summary>Runs the command after a click, if the element shows it can run (a <see cref="UI.Command"/> checks CanExecute itself too).</summary>
    public void Execute()
    {
        if (command() is not { } now || !_canExecute) return;
        object? argument = parameter();
        ModCall.Run(OwnerOf(now), $"{owner} Command", () => now.Execute(argument));
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        bool can = command() is not { } now || Check(now, parameter());
        if (can == _canExecute) return;
        _canExecute = can;
        owner.EnabledChanged();
    }

    private bool Check(ICommand now, object? argument) =>
        ModCall.TryRun(OwnerOf(now), $"{owner} Command.CanExecute", () => now.CanExecute(argument), out bool can) && can;

    private static Assembly? OwnerOf(ICommand now) => now is IHasOwner own ? own.Owner : now.GetType().Assembly;
}
