using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>An element with <see cref="Children"/> laid out by it.</summary>
public abstract class Panel : Control
{
    private protected Panel() => Children = new Controls(this);

    /// <summary>The elements in the panel, in order. Change them on the main thread (from elsewhere, inside <see cref="Dispatcher.Post(Action)">Dispatcher.MainThread.Post</see>).</summary>
    public Controls Children { get; }

    internal override IReadOnlyList<Control> LogicalChildren => Children;
}
