using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Mjolnir.SDK.UI;

/// <summary>
/// An item of the game's main menu or pause menu, added with <see cref="MenuButtons.Add(UiMenuItem)"/>:
/// a button in the menu's own look. An item with <see cref="Items"/> opens a level of its own, as the game's «Singleplayer»
/// opens «New game / Load game / Back»: its items, then the game's «Back», which returns to the level above, as Esc does.
/// Items may hold items again, as deep as you like. A click raises <see cref="Click"/>, then runs <see cref="Command"/>.
/// </summary>
/// <example><code>
/// var tools = new UiMenuItem { Header = "My mod" };
/// tools.Items.Add(new UiMenuItem { Header = "Start" }.OnClick(Start));
/// tools.Items.Add(new UiMenuItem { Header = "Settings", Items = { new UiMenuItem { Header = "Reset" } } });
/// Menus.Main.Add(tools);
/// </code></example>
public sealed class UiMenuItem : Control
{
    public static readonly MjolnirProperty<string> HeaderProperty =
        MjolnirProperty.Register<UiMenuItem, string>(nameof(Header), "", DirtyFlags.Text, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<ICommand?> CommandProperty =
        MjolnirProperty.Register<UiMenuItem, ICommand?>(nameof(Command), null, DirtyFlags.Enabled);

    public static readonly MjolnirProperty<object?> CommandParameterProperty =
        MjolnirProperty.Register<UiMenuItem, object?>(nameof(CommandParameter), null, DirtyFlags.Enabled);

    private readonly CommandSource _command;
    private bool _opensLevel;

    public UiMenuItem()
    {
        Items = new Controls(this);
        _command = new CommandSource(this, () => Command, () => CommandParameter);
    }

    /// <summary>The text on the item's button.</summary>
    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// The items of the level this item opens, in order; none (the default) for an item that only acts. Only
    /// <see cref="UiMenuItem"/>s are shown here. Change them on the main thread.
    /// </summary>
    public Controls Items { get; }

    /// <summary>What a click does; while its CanExecute is false the item is dimmed.</summary>
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

    /// <summary>Raised when the player clicks the item (an item with <see cref="Items"/> also opens its level).</summary>
    public event EventHandler? Click
    {
        add => _command.AddClick(value);
        remove => _command.RemoveClick(value);
    }

    /// <summary>Whether the item opens a level: it has items.</summary>
    internal bool OpensLevel => Items.Count > 0;

    /// <summary>The items of its level that a menu shows (other elements in <see cref="Items"/> are skipped).</summary>
    internal IReadOnlyList<UiMenuItem> LevelItems => Items.OfType<UiMenuItem>().ToArray();

    // A menu builds menu items only: anything else in Items is not shown.
    internal override IReadOnlyList<Control> LogicalChildren => Items.All(item => item is UiMenuItem) ? Items : LevelItems;

    internal override bool ShownEnabled => IsEnabled && _command.CanExecute;

    /// <summary>The player clicked (called by its view): the Click handlers, then the command.</summary>
    internal void OnClick() => _command.Click();

    // An item that starts or stops opening a level is a different button in the game's menu: it is built again.
    internal override void Dirty(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Children) != 0 && OpensLevel != _opensLevel)
        {
            _opensLevel = OpensLevel;
            dirty |= DirtyFlags.Rebuild;
        }
        base.Dirty(dirty);
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        _command.PropertyChanged(e, CommandProperty, CommandParameterProperty);
        base.Changed(e, dirty);
    }

    private protected override void Attached()
    {
        _opensLevel = OpensLevel;
        _command.Listen();
    }

    private protected override void Detached() => _command.Listen();

    public override string ToString() => $"UiMenuItem '{GetStored(HeaderProperty)}'";
}
