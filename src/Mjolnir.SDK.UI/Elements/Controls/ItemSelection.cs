using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// What a list and a selector share about their items: the items as last read from the mod's source (read once per
/// change, never under the element's lock), the selected index and item kept in step without echoing each other, the
/// source followed while live, and <c>SelectionChanged</c> told once per change of what is selected.
/// </summary>
internal sealed class ItemSelection
{
    private readonly object _reportLock = new();
    private object?[] _items = [];
    private int _syncing;
    private bool _reported;
    private object? _reportedItem;
    private INotifyCollectionChanged? _listening;

    /// <summary>The items as last read.</summary>
    public IReadOnlyList<object?> Items => Volatile.Read(ref _items);

    /// <summary>True while one of the two selection properties is being set from the other.</summary>
    public bool Syncing => Volatile.Read(ref _syncing) != 0;

    /// <summary>Reads the items again: enumerating them is the collection's code, so a throw blames its mod and gives none.</summary>
    public void Read(IEnumerable? source, string what) => Volatile.Write(ref _items, ModCall.ItemsOf(source, what));

    public object? ItemAt(int index)
    {
        var items = Items;
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    public int IndexOf(object? item)
    {
        var items = Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (Equals(items[i], item)) return i;
        }
        return -1;
    }

    /// <summary>Sets one selection property from the other: the change it causes does not come back.</summary>
    public void Sync(Action action)
    {
        Interlocked.Increment(ref _syncing);
        try
        {
            action();
        }
        finally
        {
            Interlocked.Decrement(ref _syncing);
        }
    }

    /// <summary>
    /// What to tell of a selection now (<paramref name="selected"/>, <paramref name="item"/>): what was selected before
    /// (kept here: after new items, the old index may hold another item) and what is now. Null when nothing changed; the
    /// same item at another place in new items is no change.
    /// </summary>
    public SelectionChangedEventArgs? Report(bool selected, object? item)
    {
        object?[] removed, added;
        lock (_reportLock)
        {
            if (_reported == selected && (!selected || Equals(_reportedItem, item))) return null;
            removed = _reported ? [_reportedItem] : [];
            added = selected ? [item] : [];
            _reported = selected;
            _reportedItem = selected ? item : null;
        }
        return new SelectionChangedEventArgs(removed, added);
    }

    /// <summary>
    /// Follows <paramref name="wanted"/> (an ObservableCollection while live, else null), main thread. True when what is
    /// followed changed, so the items should be read again.
    /// </summary>
    public bool Listen(INotifyCollectionChanged? wanted, NotifyCollectionChangedEventHandler handler)
    {
        if (ReferenceEquals(wanted, _listening)) return false;
        if (_listening != null) _listening.CollectionChanged -= handler;
        _listening = wanted;
        if (wanted != null) wanted.CollectionChanged += handler;
        return true;
    }
}
