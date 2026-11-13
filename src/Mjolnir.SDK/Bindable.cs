using System;
using System.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// A value that can be read and watched: the source of a one-way binding (<c>title.Bind(UiTextBlock.TextProperty,
/// Localization.GetBindable("title"))</c>). Implemented by <see cref="TranslatedText"/> and by every <see cref="IBindable{T}"/>;
/// adapt one to another type with <see cref="BindableExtensions.Map{TSource, TResult}(IReadOnlyBindable{TSource}, Func{TSource, TResult})"/>.
/// </summary>
public interface IReadOnlyBindable<T>
{
    /// <summary>The current value.</summary>
    T Value { get; }

    /// <summary>Raised after the value changed, with the old and the new value; the sender is the bindable.</summary>
    event EventHandler<ValueChangedEventArgs<T>>? Changed;
}

/// <summary>
/// A value that can be read, written and watched: the source of a binding either way (<c>toggle.Bind(UiToggleSwitch.IsCheckedProperty, entry)</c>). Implemented by
/// <see cref="Config.ConfigEntry{T}"/> (a setting) and <c>Mjolnir.SDK.UI.BindableValue&lt;T&gt;</c> (state of your own UI); adapt one to
/// another type with <see cref="BindableExtensions.Map{TSource, TResult}(IBindable{TSource}, Func{TSource, TResult}, Func{TResult, TSource})"/>.
/// </summary>
public interface IBindable<T> : IReadOnlyBindable<T>
{
    /// <summary>The current value. Setting it raises <see cref="IReadOnlyBindable{T}.Changed"/> if it differs.</summary>
    new T Value { get; set; }
}

/// <summary>
/// A value changed (<see cref="IReadOnlyBindable{T}.Changed"/>, and every event named after a property and <c>Changed</c>): what
/// it was and what it is now.
/// </summary>
public class ValueChangedEventArgs<T>(T oldValue, T newValue) : EventArgs
{
    public T OldValue { get; } = oldValue;
    public T NewValue { get; } = newValue;
}

/// <summary>Helpers for <see cref="IBindable{T}"/>.</summary>
public static class BindableExtensions
{
    /// <summary>
    /// The same value seen as another type, both ways: an enum as an option index, an int as a slider's float, a number
    /// as text. Reading converts with <paramref name="to"/>, writing with <paramref name="from"/>.
    /// </summary>
    /// <example><code>text.Bind(UiTextBlock.TextProperty, config.Villagers.Map(v => $"{v} villagers", s => int.Parse(s)))</code></example>
    public static IBindable<TResult> Map<TSource, TResult>(this IBindable<TSource> source, Func<TSource, TResult> to, Func<TResult, TSource> from)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(from);
        return new MappedBindable<TSource, TResult>(source, to, from);
    }

    /// <summary>The same value seen as another type, for reading only: a number as text, a stat as a fraction.</summary>
    /// <example><code>label.Bind(UiTextBlock.TextProperty, config.Villagers.Map(v => $"{v} villagers"))</code></example>
    public static IReadOnlyBindable<TResult> Map<TSource, TResult>(this IReadOnlyBindable<TSource> source, Func<TSource, TResult> to)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(to);
        return new MappedReadOnlyBindable<TSource, TResult>(source, to);
    }
}

/// <summary>
/// <see cref="BindableExtensions"/>' read-only Map: subscribes to its source only while someone listens to it, so a
/// short-lived mapping never keeps itself alive through a long-lived source (a config entry). The conversion is mod code and
/// runs through <see cref="ModCall"/>: a throwing one blames its mod. It is no <see cref="IBindable{T}"/>, so a binding to it
/// goes one way.
/// </summary>
internal class MappedReadOnlyBindable<TSource, TResult>(IReadOnlyBindable<TSource> source, Func<TSource, TResult> to)
    : IReadOnlyBindable<TResult>
{
    private readonly object _lock = new();
    private EventHandler<ValueChangedEventArgs<TResult>>? _changed;

    public TResult Value => ModCall.Evaluate(to, source.Value, "Map conversion", out var value) ? value : default!;

    public event EventHandler<ValueChangedEventArgs<TResult>>? Changed
    {
        add
        {
            if (value == null) return;
            lock (_lock)
            {
                if (_changed == null) source.Changed += OnSourceChanged;
                _changed += value;
            }
        }
        remove
        {
            if (value == null) return;
            lock (_lock)
            {
                _changed -= value;
                if (_changed == null) source.Changed -= OnSourceChanged;
            }
        }
    }

    // The old value is converted too; one that does not convert any more is reported as the default.
    private void OnSourceChanged(object? sender, ValueChangedEventArgs<TSource> e)
    {
        var handlers = Volatile.Read(ref _changed);
        if (handlers == null || !ModCall.Evaluate(to, e.NewValue, "Map conversion", out var now)) return;
        var old = ModCall.Evaluate(to, e.OldValue, "Map conversion", out var before) ? before : default!;
        ModCall.InvokeEach(handlers, this, new ValueChangedEventArgs<TResult>(old, now), "Map.Changed");
    }
}

/// <summary><see cref="BindableExtensions"/>' Map both ways: a write converts with <c>from</c> and sets the source.</summary>
internal sealed class MappedBindable<TSource, TResult>(IBindable<TSource> source, Func<TSource, TResult> to, Func<TResult, TSource> from)
    : MappedReadOnlyBindable<TSource, TResult>(source, to), IBindable<TResult>
{
    public new TResult Value
    {
        get => base.Value;
        set
        {
            if (ModCall.Evaluate(from, value, "Map conversion", out var converted)) source.Value = converted;
        }
    }
}
