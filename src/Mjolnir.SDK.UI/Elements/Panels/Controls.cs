using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The children of a panel. Supports collection initializers (<c>Children = { a, b }</c>), a sequence
/// (<c>Children = { header, rows.Select(…) }</c>) and nulls, which are skipped. Change it on the main thread (from
/// elsewhere, through <see cref="Dispatcher.Post(Action)">Dispatcher.MainThread.Post</see>).
/// </summary>
public sealed class Controls : Collection<Control>
{
    private readonly Control _owner;

    internal Controls(Control owner) => _owner = owner;

    /// <summary>Adds several children in order (nulls skipped). A bare <c>null</c> in an initializer lands here and adds nothing.</summary>
    public void Add(IEnumerable<Control?>? elements)
    {
        if (elements == null) return;
        foreach (var element in elements.ToList()) Add(element!);
    }

    /// <summary>Adds several children in order.</summary>
    public void AddRange(IEnumerable<Control?> elements) => Add(elements);

    /// <summary>Moves a child to another position (its copies are moved, not rebuilt).</summary>
    public void Move(int oldIndex, int newIndex)
    {
        CheckThread();
        var item = this[oldIndex];
        base.RemoveItem(oldIndex);
        base.InsertItem(newIndex, item);
        _owner.Dirty(DirtyFlags.Children);
    }

    protected override void InsertItem(int index, Control item)
    {
        if (item == null) return;
        CheckThread();
        CheckNotHere(item, -1);
        _owner.Adopt(item);
        base.InsertItem(index, item);
        _owner.Dirty(DirtyFlags.Children);
    }

    protected override void SetItem(int index, Control item)
    {
        ArgumentNullException.ThrowIfNull(item);
        CheckThread();
        var old = this[index];
        if (ReferenceEquals(old, item)) return;
        CheckNotHere(item, index);
        _owner.Adopt(item);
        Control.Release(old);
        base.SetItem(index, item);
        _owner.Dirty(DirtyFlags.Children);
    }

    protected override void RemoveItem(int index)
    {
        CheckThread();
        Control.Release(this[index]);
        base.RemoveItem(index);
        _owner.Dirty(DirtyFlags.Children);
    }

    protected override void ClearItems()
    {
        CheckThread();
        foreach (var item in this) Control.Release(item);
        base.ClearItems();
        _owner.Dirty(DirtyFlags.Children);
    }

    // An element sits in one place: twice in one panel, its copies would be built twice and one left behind.
    private void CheckNotHere(Control item, int except)
    {
        for (int i = 0; i < Count; i++)
        {
            if (i != except && ReferenceEquals(this[i], item))
                throw new InvalidOperationException($"{item} is in {_owner} already; an element can be in one place only");
        }
    }

    private static void CheckThread()
    {
        if (!Dispatcher.IsMainThread)
            throw new InvalidOperationException("change UI children on the main thread (from other threads, inside Dispatcher.MainThread.Post)");
    }
}
