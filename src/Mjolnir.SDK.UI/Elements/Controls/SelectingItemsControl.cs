using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// One item of <see cref="ItemsSource"/> chosen: <see cref="UiStepper"/> and
/// <see cref="UiComboBox"/>. An item shows as <see cref="ItemText"/> makes it, else as its text; an enum value by your
/// mod's string <c>&lt;enum&gt;.&lt;value&gt;</c> in the player's language (<c>[Difficulty] Hard = "Сложно"</c> in
/// lang/russian.toml), else by its <c>[Description]</c>, else by its name. <see cref="SelectedIndex"/> and
/// <see cref="SelectedItem"/> follow each other and bind two-way by default; bind <see cref="SelectedItem"/> to an enum
/// setting: <c>selector.ItemsSource = Enum.GetValues&lt;Difficulty&gt;(); selector.Bind(UiStepper.SelectedItemProperty, config.Difficulty)</c>.
/// An ObservableCollection as the source is followed.
/// </summary>
public abstract class SelectingItemsControl : HeaderedControl
{
    public static readonly MjolnirProperty<IEnumerable?> ItemsSourceProperty =
        MjolnirProperty.Register<SelectingItemsControl, IEnumerable?>(nameof(ItemsSource), null, DirtyFlags.Options | DirtyFlags.Value);

    /// <summary>-1 while nothing is selected.</summary>
    public static readonly MjolnirProperty<int> SelectedIndexProperty =
        MjolnirProperty.Register<SelectingItemsControl, int>(nameof(SelectedIndex), -1, DirtyFlags.Value, BindingMode.TwoWay,
                                                          coerce: static (control, index) => Math.Clamp(index, -1, control.Items.Count - 1));

    public static readonly MjolnirProperty<object?> SelectedItemProperty =
        MjolnirProperty.Register<SelectingItemsControl, object?>(nameof(SelectedItem), null, DirtyFlags.Value, BindingMode.TwoWay);

    public static readonly MjolnirProperty<Func<object?, string>?> ItemTextProperty =
        MjolnirProperty.Register<SelectingItemsControl, Func<object?, string>?>(nameof(ItemText), null, DirtyFlags.Options | DirtyFlags.Value);

    private readonly ItemSelection _selection = new();
    private ModHandlers<EventHandler<SelectionChangedEventArgs>>? _selectionChanged;

    private protected SelectingItemsControl() => CoerceValue(SelectedIndexProperty);

    /// <summary>The items to choose from (an array, a list, <c>Enum.GetValues&lt;T&gt;()</c>, an ObservableCollection).</summary>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// The text an item shows, made by a function: <c>season.ItemText = item =&gt;
    /// ((Season)item!).Title</c>. Null (the default) shows an enum value by its name in the player's language or its
    /// <c>[Description]</c>, anything else by its text. If the function throws, your mod is disabled and the item shows
    /// its default text.
    /// </summary>
    public Func<object?, string>? ItemText
    {
        get => GetValue(ItemTextProperty);
        set => SetValue(ItemTextProperty, value);
    }

    /// <summary>Raised after the selection changed: by the player, by code or by a binding.</summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged
    {
        add => ModHandlers<EventHandler<SelectionChangedEventArgs>>.Of(ref _selectionChanged, $"{this}.SelectionChanged").Add(value);
        remove => _selectionChanged?.Remove(value);
    }

    /// <summary>The items as last read from <see cref="ItemsSource"/>.</summary>
    internal IReadOnlyList<object?> Items => _selection.Items;

    /// <summary>The items as the player sees them.</summary>
    internal IReadOnlyList<string> ItemTexts
    {
        get
        {
            var custom = GetValue(ItemTextProperty);
            return Items.Select(item => custom != null && ModCall.Evaluate(custom, item, $"{this} ItemText", out string text)
                                            ? text ?? ""
                                            : DefaultItemText.Of(item, $"{this} item")).ToArray();
        }
    }

    /// <summary>The selector is a list to drop down (a combo box) rather than "‹ value ›".</summary>
    internal abstract bool AsDropdown { get; }

    /// <summary>The player picked an item (called by its view).</summary>
    internal void SetIndexFromView(int index) => SelectedIndex = index;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            ReadItems();
            Dispatcher.Run(ListenToItems);
        }
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            Resync();
        }
        else if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            int index = e.GetNewValue<int>();
            var item = _selection.ItemAt(index);
            if (!_selection.Syncing) _selection.Sync(() => SelectedItem = item);
            RaiseSelectionChanged(index >= 0, item);
        }
        else if (ReferenceEquals(e.Property, SelectedItemProperty) && !_selection.Syncing)
        {
            _selection.Sync(() => SelectedIndex = _selection.IndexOf(e.NewValue));
        }
    }

    private void RaiseSelectionChanged(bool selected, object? item)
    {
        if (_selection.Report(selected, item) is { } args) _selectionChanged?.Raise(h => h(this, args));
    }

    // While on screen, a change of language shows the items again: their default texts may be in the player's language.
    private protected override void Attached()
    {
        ListenToItems();
        Localization.Switched += LanguageSwitched;
    }

    private protected override void Detached()
    {
        ListenToItems();
        Localization.Switched -= LanguageSwitched;
    }

    private void LanguageSwitched() => Dirty(DirtyFlags.Options | DirtyFlags.Value);

    // New items: the selected item keeps its place if it is still there, else the index stays within the list.
    private void Resync()
    {
        if (SelectedItem is { } item) _selection.Sync(() => SelectedIndex = _selection.IndexOf(item));
        else CoerceValue(SelectedIndexProperty);
    }

    private void ReadItems() => _selection.Read(GetStored(ItemsSourceProperty), $"{this} ItemsSource");

    // An ObservableCollection as the source is followed while live (main thread).
    private void ListenToItems()
    {
        if (!_selection.Listen(IsLive ? GetStored(ItemsSourceProperty) as INotifyCollectionChanged : null, OnItemsChanged)) return;
        ReadItems();
        Dirty(DirtyFlags.Options | DirtyFlags.Value);
        Resync();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.Run(() =>
    {
        ReadItems();
        Dirty(DirtyFlags.Options | DirtyFlags.Value);
        Resync();
    });
}

/// <summary>The game's "‹ value ›" selector: the arrows step through a few items.</summary>
public sealed class UiStepper : SelectingItemsControl
{
    internal override bool AsDropdown => false;
}

/// <summary>A list that drops down, for many items (like the game's resolution setting).</summary>
public sealed class UiComboBox : SelectingItemsControl
{
    public static readonly MjolnirProperty<string?> PlaceholderTextProperty =
        MjolnirProperty.Register<UiComboBox, string?>(nameof(PlaceholderText), null, DirtyFlags.Options);

    /// <summary>Shown while nothing is selected.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    internal override bool AsDropdown => true;
}
