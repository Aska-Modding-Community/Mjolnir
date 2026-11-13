using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A list the player picks one item from: one row per item of <see cref="ItemsSource"/>, made by
/// <see cref="ItemTemplate"/> (the item's text without one), each in the frame of the game's save-slot rows, the selected
/// one framed in amber. A click selects. <see cref="SelectedIndex"/> and <see cref="SelectedItem"/> follow each other and
/// bind two-way by default; an ObservableCollection as the source is followed. Put the details of the selected item
/// beside it for a list-and-details page.
/// </summary>
public sealed class UiListBox : Control
{
    public static readonly MjolnirProperty<IEnumerable?> ItemsSourceProperty =
        MjolnirProperty.Register<UiListBox, IEnumerable?>(nameof(ItemsSource), null, DirtyFlags.None);

    public static readonly MjolnirProperty<IDataTemplate?> ItemTemplateProperty =
        MjolnirProperty.Register<UiListBox, IDataTemplate?>(nameof(ItemTemplate), null, DirtyFlags.None);

    /// <summary>-1 while nothing is selected.</summary>
    public static readonly MjolnirProperty<int> SelectedIndexProperty =
        MjolnirProperty.Register<UiListBox, int>(nameof(SelectedIndex), -1, DirtyFlags.None, BindingMode.TwoWay,
                                                coerce: static (list, index) => Math.Clamp(index, -1, list._selection.Items.Count - 1));

    public static readonly MjolnirProperty<object?> SelectedItemProperty =
        MjolnirProperty.Register<UiListBox, object?>(nameof(SelectedItem), null, DirtyFlags.None, BindingMode.TwoWay);

    private readonly ItemSelection _selection = new();
    private readonly UiItemsControl _rows;
    private ModHandlers<EventHandler<SelectionChangedEventArgs>>? _selectionChanged;

    public UiListBox()
    {
        _rows = new UiItemsControl { ItemTemplate = new RowTemplate(this) };
        Adopt(_rows);
        CoerceValue(SelectedIndexProperty);
    }

    /// <summary>The items (an array, a list, an ObservableCollection).</summary>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Makes the content of an item's row (a <see cref="FuncDataTemplate{T}"/>); the item's text without one.</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
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

    /// <summary>Raised after the selection changed: by the player, by code or by a binding.</summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged
    {
        add => ModHandlers<EventHandler<SelectionChangedEventArgs>>.Of(ref _selectionChanged, $"{this}.SelectionChanged").Add(value);
        remove => _selectionChanged?.Remove(value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => [_rows];

    /// <summary>The rows built now (main thread): one per item, in order.</summary>
    internal IReadOnlyList<ListBoxItem> Rows
    {
        get
        {
            var rows = new List<ListBoxItem>();
            foreach (var panel in _rows.LogicalChildren)
            {
                foreach (var child in panel.LogicalChildren)
                {
                    if (child is ListBoxItem row) rows.Add(row);
                }
            }
            return rows;
        }
    }

    /// <summary>The player clicked a row (called by its view).</summary>
    internal void SelectFromView(object? item) => SelectedItem = item;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            ReadItems();
            Dispatcher.Run(ListenToItems);
            _rows.ItemsSource = e.NewValue as IEnumerable;
            Resync();
        }
        else if (ReferenceEquals(e.Property, ItemTemplateProperty))
        {
            _rows.ItemTemplate = new RowTemplate(this); // the rows are made again
        }
        else if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            int index = e.GetNewValue<int>();
            var item = _selection.ItemAt(index);
            if (!_selection.Syncing) _selection.Sync(() => SelectedItem = item);
            ShowSelection();
            if (_selection.Report(index >= 0, item) is { } args) _selectionChanged?.Raise(h => h(this, args));
        }
        else if (ReferenceEquals(e.Property, SelectedItemProperty) && !_selection.Syncing)
        {
            _selection.Sync(() => SelectedIndex = _selection.IndexOf(e.NewValue));
        }
    }

    // An ObservableCollection as the source is followed while live: its rows follow it (the items control), and so does
    // the selection, the selected item keeping its place if it is still there.
    private protected override void Attached() => ListenToItems();

    private protected override void Detached() => ListenToItems();

    // Rows follow the selection on the main thread (they are elements of the live tree).
    private void ShowSelection() => Dispatcher.Run(() =>
    {
        var selected = SelectedItem;
        foreach (var row in Rows) row.IsSelected = Equals(row.Item, selected) && SelectedIndex >= 0;
    });

    // New items: the selected item keeps its place if it is still there (nothing is selected once it went), else the
    // index stays within the list.
    private void Resync()
    {
        if (SelectedItem is { } item) _selection.Sync(() => SelectedIndex = _selection.IndexOf(item));
        else CoerceValue(SelectedIndexProperty);
    }

    private void ReadItems() => _selection.Read(GetStored(ItemsSourceProperty), $"{this} ItemsSource");

    private void ListenToItems()
    {
        if (!_selection.Listen(IsLive ? GetStored(ItemsSourceProperty) as INotifyCollectionChanged : null, OnItemsChanged)) return;
        ReadItems();
        Resync();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.Run(() =>
    {
        ReadItems();
        Resync();
    });

    // A row's content must be new: an element shown elsewhere, the list itself or one it sits in cannot be (a template
    // giving an element the list sits in would make the rows contain the list, without end).
    private bool CanHold(Control content) => content.Parent == null && !IsSelfOrInside(content);

    // Each item in a row of its own: the mod's template (a throw blames that mod) or the item's text inside.
    private sealed class RowTemplate(UiListBox list) : IDataTemplate, IHasOwner
    {
        Assembly? IHasOwner.Owner => typeof(UiListBox).Assembly;

        public bool Match(object? data) => true;

        public Control Build(object? data)
        {
            var template = list.GetValue(ItemTemplateProperty);
            Control? content = null;
            if (template != null)
            {
                var owner = template is IHasOwner own ? own.Owner : template.GetType().Assembly;
                if (ModCall.TryRun(owner, $"{list} ItemTemplate", () => template.Match(data), out bool match) && match)
                    ModCall.TryRun(owner, $"{list} ItemTemplate", () => template.Build(data), out content);
            }
            if (content != null && !list.CanHold(content))
            {
                SdkLog.Error(GameUi.Source, $"{list} ItemTemplate gave {content}, which is shown elsewhere or holds the list; the item's text is shown instead");
                content = null;
            }
            content ??= data is Control element && list.CanHold(element) ? element : new UiTextBlock { Text = ModCall.TextOf(data, $"{list} item") };
            return new ListBoxItem(list, data) { Content = content, IsSelected = list.SelectedIndex >= 0 && Equals(list.SelectedItem, data) };
        }
    }
}

/// <summary>A row of a <see cref="UiListBox"/>: its content in the save-slot frame.</summary>
internal sealed class ListBoxItem : Control
{
    public static readonly MjolnirProperty<bool> IsSelectedProperty =
        MjolnirProperty.Register<ListBoxItem, bool>(nameof(IsSelected), false, DirtyFlags.Value);

    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<ListBoxItem, Control?>(nameof(Content), null, DirtyFlags.Children);

    private readonly UiListBox _list;

    public ListBoxItem(UiListBox list, object? item)
    {
        _list = list;
        Item = item;
    }

    public object? Item { get; }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => OwnChild(Content);

    /// <summary>The player clicked the row (called by its view).</summary>
    public void OnClick() => _list.SelectFromView(Item);

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        ContentChanged(e, ContentProperty);
        base.Changed(e, dirty);
    }
}
