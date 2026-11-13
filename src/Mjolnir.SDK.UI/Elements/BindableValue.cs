using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A value your UI watches: bind properties to it (<c>text.Bind(UiTextBlock.TextProperty, clicks)</c>, converted to the
/// property's type), subscribe to <see cref="Changed"/>. Thread-safe to set; handlers run on the setting thread.
/// </summary>
public sealed class BindableValue<T> : IBindable<T>
{
    private readonly ModHandlers<EventHandler<ValueChangedEventArgs<T>>> _changed = new("BindableValue.Changed");
    private readonly object _lock = new();
    private T _value;

    public BindableValue(T value = default!) => _value = value;

    /// <summary>The value; setting a different one raises <see cref="Changed"/>.</summary>
    public T Value
    {
        get
        {
            lock (_lock) return _value;
        }
        set
        {
            T old;
            lock (_lock)
            {
                old = _value;
                if (EqualityComparer<T>.Default.Equals(old, value)) return;
                _value = value;
            }
            var args = new ValueChangedEventArgs<T>(old, value);
            _changed.Raise(h => h(this, args));
        }
    }

    /// <summary>Raised with the old and the new value. If a handler throws, its mod is disabled.</summary>
    public event EventHandler<ValueChangedEventArgs<T>>? Changed
    {
        add => _changed.Add(value);
        remove => _changed.Remove(value);
    }

    /// <summary>How many handlers listen (unit tests check that live controls let go).</summary>
    internal int HandlerCount => _changed.Count;

    public override string ToString() => $"{Value}";
}
