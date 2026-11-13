using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>Makes the element for an item of a <see cref="UiItemsControl"/>.</summary>
public interface IDataTemplate
{
    /// <summary>Whether this template is for <paramref name="data"/>.</summary>
    bool Match(object? data);

    /// <summary>A new element showing <paramref name="data"/>.</summary>
    Control? Build(object? data);
}

/// <summary>A template from a function, for items of type <typeparamref name="T"/>.</summary>
public sealed class FuncDataTemplate<T> : IDataTemplate, IHasOwner
{
    private readonly Func<T, Control?> _build;

    public FuncDataTemplate(Func<T, Control?> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    /// <summary>The mod that wrote the function: a throw from it disables that mod.</summary>
    Assembly? IHasOwner.Owner => _build.Method.DeclaringType?.Assembly;

    public bool Match(object? data) => data is T || (data == null && default(T) == null);

    public Control? Build(object? data) => _build((T)data!);
}

/// <summary>Makes a new element of type <typeparamref name="TElement"/> each time: a list's panel.</summary>
public sealed class FuncTemplate<TElement> where TElement : Control
{
    private readonly Func<TElement> _build;

    public FuncTemplate(Func<TElement> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    public TElement Build() => _build();

    internal Func<TElement> Function => _build;
}

/// <summary>
/// One element per item of <see cref="ItemsSource"/>, made by <see cref="ItemTemplate"/> (a text with the item's
/// text without one; items that are elements themselves are shown as they are), in a column or the panel of
/// <see cref="ItemsPanel"/>. An ObservableCollection (any INotifyCollectionChanged) is followed: additions, removals,
/// moves and resets; changes from other threads arrive on the next frame. Items are built when the list is first shown.
/// </summary>
public sealed class UiItemsControl : Control
{
    // These three rebuild the items (which marks the children changed); nothing else of the view depends on them.
    public static readonly MjolnirProperty<IEnumerable?> ItemsSourceProperty =
        MjolnirProperty.Register<UiItemsControl, IEnumerable?>(nameof(ItemsSource), null, DirtyFlags.None);

    public static readonly MjolnirProperty<IDataTemplate?> ItemTemplateProperty =
        MjolnirProperty.Register<UiItemsControl, IDataTemplate?>(nameof(ItemTemplate), null, DirtyFlags.None);

    public static readonly MjolnirProperty<FuncTemplate<Panel>?> ItemsPanelProperty =
        MjolnirProperty.Register<UiItemsControl, FuncTemplate<Panel>?>(nameof(ItemsPanel), null, DirtyFlags.None);

    private Panel? _panel;
    private INotifyCollectionChanged? _listening;
    private bool _attached; // main thread: between Attached and Detached

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>The panel holding the items: <c>new FuncTemplate&lt;Panel&gt;(() => new UiStackPanel { Orientation = Orientation.Horizontal })</c>. A column by default.</summary>
    public FuncTemplate<Panel>? ItemsPanel
    {
        get => GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    internal override IReadOnlyList<Control> LogicalChildren => _panel != null ? [_panel] : [];

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        if (ReferenceEquals(e.Property, ItemsSourceProperty) || ReferenceEquals(e.Property, ItemTemplateProperty) ||
            ReferenceEquals(e.Property, ItemsPanelProperty))
            Dispatcher.Run(Rebuild); // children change on the main thread
        base.Changed(e, dirty);
    }

    // Bindings push their values before Attached (Control.AddNode), so the items are built once, here.
    private protected override void Attached()
    {
        _attached = true;
        Rebuild();
    }

    private protected override void Detached()
    {
        _attached = false;
        Listen(null);
        DropPanel();
    }

    // Main thread: the panel and every item made again from the current properties.
    private void Rebuild()
    {
        if (!_attached) return;
        Listen(GetValue(ItemsSourceProperty) as INotifyCollectionChanged);
        DropPanel();
        var template = GetValue(ItemsPanelProperty);
        Panel? panel = null;
        if (template != null && !ModCall.Evaluate(template.Function, $"{this} ItemsPanel", out panel)) panel = null;
        _panel = panel ?? new UiStackPanel();
        if (!TryAdopt(_panel))
        {
            _panel = new UiStackPanel();
            Adopt(_panel);
        }
        foreach (var item in Snapshot()) _panel.Children.Add(Make(item));
        Dirty(DirtyFlags.Children);
    }

    private void DropPanel()
    {
        if (_panel == null) return;
        _panel.Children.Clear();
        Release(_panel);
        _panel = null;
    }

    private object?[] Snapshot() => ModCall.ItemsOf(GetValue(ItemsSourceProperty), $"{this} ItemsSource");

    // The template, the item's text and the collection are mod code: a throw blames that mod and shows an empty text.
    private Control Make(object? item)
    {
        var template = GetValue(ItemTemplateProperty);
        if (template != null)
        {
            var owner = template is IHasOwner own ? own.Owner : template.GetType().Assembly;
            if (ModCall.TryRun(owner, $"{this} ItemTemplate", () => template.Match(item), out bool match) && match)
            {
                return ModCall.TryRun(owner, $"{this} ItemTemplate", () => template.Build(item), out var built) && built != null && CanHold(built)
                    ? built
                    : new UiTextBlock();
            }
        }
        if (item is Control element && CanHold(element)) return element;
        return new UiTextBlock { Text = ModCall.TextOf(item, $"{this} item") };
    }

    // An element elsewhere, this list itself or one of its ancestors cannot be an item.
    private bool CanHold(Control element) => element.Parent == null && !IsSelfOrInside(element);

    private void Listen(INotifyCollectionChanged? source)
    {
        if (ReferenceEquals(source, _listening)) return;
        if (_listening != null) _listening.CollectionChanged -= OnCollectionChanged;
        _listening = source;
        if (source != null) source.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(Rebuild); // the collection changed elsewhere: start over from it on the main thread
            return;
        }
        if (_panel == null) return;
        var children = _panel.Children;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null && e.NewStartingIndex >= 0:
                    for (int i = 0; i < e.NewItems.Count; i++) children.Insert(e.NewStartingIndex + i, Make(e.NewItems[i]));
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems != null && e.OldStartingIndex >= 0:
                    for (int i = 0; i < e.OldItems.Count; i++) children.RemoveAt(e.OldStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0 && e.OldItems?.Count == 1:
                    children.Move(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                default:
                    Rebuild();
                    break;
            }
        }
        catch (ArgumentException)
        {
            Rebuild(); // indices out of step with what we built: start over
        }
    }
}
