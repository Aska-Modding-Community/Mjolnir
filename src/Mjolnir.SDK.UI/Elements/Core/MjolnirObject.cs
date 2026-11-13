using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// An object with <see cref="MjolnirProperty"/> values: the base of every element and of
/// <see cref="UiWindow"/>. Its properties can be set and read from any thread, watched (<see cref="PropertyChanged"/>,
/// <see cref="GetObservable{T}"/>) and bound to a source (<c>Bind</c>: a config entry, a <see cref="BindableValue{T}"/>, a
/// view model property, a function polled every frame, an observable).
/// <list type="bullet">
/// <item>A value set with <see cref="SetValue{T}"/> holds until the property's binding next pushes one (for an object not
/// shown, when it is shown); with a <see cref="BindingMode.TwoWay"/> binding it is also written to the source.</item>
/// <item>A binding follows its source while the object is live (shown somewhere: in a window, the HUD, a menu); a
/// long-lived source such as a config entry therefore never keeps a closed element alive. While not live, reading the
/// property reads a config entry, a <see cref="BindableValue{T}"/> or a view model directly; a polled function or an
/// observable gives the last value it produced.</item>
/// <item><see cref="PropertyChanged"/> and the controls' events run on the thread that made the change: the main thread
/// for the player's changes.</item>
/// </list>
/// </summary>
public abstract class MjolnirObject
{
    private readonly object _sync = new();
    private readonly List<ValueEntry> _entries = []; // sorted by property id
    private ModHandlers<EventHandler<MjolnirPropertyChangedEventArgs>>? _propertyChanged;

    private protected MjolnirObject() { }

    /// <summary>Raised after a property's value changed, with the old and the new value. If a handler throws, its mod is disabled.</summary>
    public event EventHandler<MjolnirPropertyChangedEventArgs>? PropertyChanged
    {
        add => ModHandlers<EventHandler<MjolnirPropertyChangedEventArgs>>.Of(ref _propertyChanged, $"{GetType().Name}.PropertyChanged").Add(value);
        remove => _propertyChanged?.Remove(value);
    }

    /// <summary>Called after a property's value changed, before <see cref="PropertyChanged"/>: override in your own controls.</summary>
    protected virtual void OnPropertyChanged(MjolnirPropertyChangedEventArgs e) { }

    /// <summary>The property's value: set, bound or default.</summary>
    public T GetValue<T>(MjolnirProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        PropertyBinding<T>? readThrough;
        lock (_sync)
        {
            var entry = Find(property);
            if (entry == null) return DefaultOf(property);
            readThrough = entry.Binding is { IsActive: false, CanRead: true } binding && !entry.LocalOverride ? binding : null;
            if (readThrough == null) return entry.Effective;
        }
        // Not live: a pull source is read now (mod code, so outside the lock).
        if (readThrough.TryRead(out var value))
        {
            lock (_sync) return Coerced(property, value);
        }
        lock (_sync) return Find(property) is { } entry ? entry.Effective : DefaultOf(property);
    }

    /// <summary>
    /// Sets the property's value. It holds until the property's binding next pushes a value; a
    /// <see cref="BindingMode.TwoWay"/> binding also writes it to its source (and the source's answer is kept: a config
    /// entry clamps to its range).
    /// </summary>
    public void SetValue<T>(MjolnirProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        PropertyBinding<T>? twoWay;
        lock (_sync)
        {
            twoWay = Find(property)?.Binding is { Mode: BindingMode.TwoWay } binding ? binding : null;
            if (twoWay != null) value = Coerced(property, value);
        }
        if (twoWay != null) value = twoWay.WriteBack(value);
        Store(property, value, null);
    }

    /// <summary>Drops the property's value and its binding: it shows its default again.</summary>
    public void ClearValue<T>(MjolnirProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        PropertyBinding? binding;
        T old, value;
        bool changed;
        lock (_sync)
        {
            var entry = Find(property);
            if (entry == null) return;
            binding = entry.Binding;
            entry.Binding = null;
            entry.HasRaw = false;
            entry.LocalOverride = false;
            changed = Recompute(entry, out old, out value);
        }
        binding?.Dispose();
        if (changed) Raise(property, old, value);
    }

    /// <summary>The property's value now and after every change (an <see cref="IObservable{T}"/> for your own bindings).</summary>
    public IObservable<T> GetObservable<T>(MjolnirProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return new PropertyObservable<T>(this, property);
    }

    /// <summary>
    /// Binds the property to a config entry, a <see cref="BindableValue{T}"/> or any other <see cref="IBindable{T}"/>. Different
    /// types are converted (numbers to numbers, anything to text, text to numbers, enums to and from objects) unless you give
    /// <paramref name="convert"/>/<paramref name="convertBack"/>. Returns the binding: dispose it to stop (the property
    /// keeps its last value). A second binding of the same property replaces the first.
    /// </summary>
    public IDisposable Bind<T, TSource>(MjolnirProperty<T> property, IBindable<TSource> source, BindingMode mode = BindingMode.Default,
                                        Func<TSource, T>? convert = null, Func<T, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        mode = property.Resolve(mode);
        return Attach(new BindableBinding<T, TSource>(this, property, source, mode, BindingConverter<TSource, T>.Create(convert, property),
                                                      mode == BindingMode.TwoWay ? BindingConverter<T, TSource>.Create(convertBack, property) : null));
    }

    /// <summary>
    /// Binds the property to a source that can only be read, one way: a translated text (<c>Localization.GetBindable</c>), a
    /// <c>Map</c> without a way back. Converted as the other bindings are unless you give <paramref name="convert"/>.
    /// </summary>
    public IDisposable Bind<T, TSource>(MjolnirProperty<T> property, IReadOnlyBindable<TSource> source, Func<TSource, T>? convert = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        return Attach(new BindableBinding<T, TSource>(this, property, source, BindingMode.OneWay, BindingConverter<TSource, T>.Create(convert, property), null));
    }

    /// <summary>Binds the property to an observable (another element's <see cref="GetObservable{T}"/>, a reactive stream): one way.</summary>
    public IDisposable Bind<T, TSource>(MjolnirProperty<T> property, IObservable<TSource> source, Func<TSource, T>? convert = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        return Attach(new ObservableBinding<T, TSource>(this, property, source, BindingConverter<TSource, T>.Create(convert, property)));
    }

    /// <summary>
    /// Binds the property to a function, called once per frame while the object is on screen (an open window, the HUD of a
    /// world): <c>health.Bind(UiTextBlock.TextProperty, () => $"{LocalPlayer.Health.Value:0}")</c>. The screen changes
    /// only when the result does. If the function throws, your mod is disabled. A value that changes slowly, or costs something to make
    /// (an interpolated string is a new string on every call), is better polled less often:
    /// <see cref="Bind{T, TSource}(MjolnirProperty{T}, Func{TSource}, TimeSpan, Func{TSource, T})"/>.
    /// </summary>
    public IDisposable Bind<T, TSource>(MjolnirProperty<T> property, Func<TSource> source, Func<TSource, T>? convert = null) =>
        Bind(property, source, TimeSpan.Zero, convert);

    /// <summary>
    /// Binds the property to a function, called at most once per <paramref name="interval"/> while the object is on screen,
    /// and at once when it comes on screen: <c>clock.Bind(UiTextBlock.TextProperty, () => $"{DateTime.Now:HH:mm}",
    /// TimeSpan.FromSeconds(1))</c>. <see cref="TimeSpan.Zero"/> calls it every frame. The screen changes only when the
    /// result does. If the function throws, your mod is disabled.
    /// </summary>
    public IDisposable Bind<T, TSource>(MjolnirProperty<T> property, Func<TSource> source, TimeSpan interval, Func<TSource, T>? convert = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        return Attach(new FuncBinding<T, TSource>(this, property, source, interval, BindingConverter<TSource, T>.Create(convert, property)));
    }

    /// <summary>
    /// Binds the property to a member of a view model, following <see cref="System.ComponentModel.INotifyPropertyChanged"/>
    /// at every step of the path: <c>text.Bind(UiTextBlock.TextProperty, vm, x => x.Player.Name)</c>.
    /// The path is properties and fields only. <see cref="BindingMode.TwoWay"/> needs a writable last member.
    /// </summary>
    public IDisposable Bind<T, TModel, TValue>(MjolnirProperty<T> property, TModel model, Expression<Func<TModel, TValue>> path,
                                               BindingMode mode = BindingMode.Default, Func<TValue, T>? convert = null,
                                               Func<T, TValue>? convertBack = null)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);
        mode = property.Resolve(mode);
        var memberPath = MemberPath.Parse(path, writable: mode == BindingMode.TwoWay);
        return Attach(new PathBinding<T, TValue>(this, property, model, memberPath, mode, BindingConverter<TValue, T>.Create(convert, property),
                                                 mode == BindingMode.TwoWay ? BindingConverter<T, TValue>.Create(convertBack, property) : null));
    }

    /// <summary>Whether the object is shown somewhere now (main thread): its bindings follow their sources only then.</summary>
    internal abstract bool IsLive { get; }

    /// <summary>Whether it is on screen now (an open window, a shown menu): its polled bindings run only then.</summary>
    internal virtual bool IsOnScreen => IsLive;

    /// <summary>The SDK's reaction to a change (refresh the screen, raise the control's own events), before the mod's.</summary>
    private protected virtual void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty) { }

    /// <summary>The stored value, without reading a binding's source: for coercion, which runs under the lock.</summary>
    internal T GetStored<T>(MjolnirProperty<T> property)
    {
        lock (_sync) return Find(property) is { } entry ? entry.Effective : DefaultOf(property);
    }

    /// <summary>Makes the property valid again after something it depends on changed (a slider's limits).</summary>
    internal void CoerceValue<T>(MjolnirProperty<T> property)
    {
        T old, value;
        bool changed;
        lock (_sync) changed = Recompute(GetOrAdd(property), out old, out value);
        if (changed) Raise(property, old, value);
    }

    /// <summary>A value pushed by <paramref name="binding"/> (or set, when null). A push from a replaced binding is dropped.</summary>
    internal void Store<T>(MjolnirProperty<T> property, T value, PropertyBinding<T>? binding)
    {
        T old, now;
        bool changed;
        lock (_sync)
        {
            var entry = GetOrAdd(property);
            if (binding != null && !ReferenceEquals(entry.Binding, binding)) return;
            entry.Raw = value;
            entry.HasRaw = true;
            // A value set over a one-way binding hides its source until the binding pushes again (a two-way one wrote it there).
            entry.LocalOverride = binding == null && entry.Binding is { Mode: not BindingMode.TwoWay };
            changed = Recompute(entry, out old, out now);
        }
        if (changed) Raise(property, old, now);
    }

    /// <summary>A disposed binding lets go of its property (which keeps the last value).</summary>
    internal void RemoveBinding(PropertyBinding binding)
    {
        lock (_sync)
        {
            foreach (var entry in _entries)
            {
                if (ReferenceEquals(entry.CurrentBinding, binding)) entry.DropBinding();
            }
        }
    }

    /// <summary>Starts or stops every binding to match <see cref="IsLive"/> (main thread, when the object went live or not).</summary>
    internal void SyncBindings()
    {
        var bindings = new List<PropertyBinding>();
        lock (_sync)
        {
            foreach (var entry in _entries)
            {
                if (entry.CurrentBinding is { } binding) bindings.Add(binding);
            }
        }
        foreach (var binding in bindings) binding.Sync();
    }

    private IDisposable Attach<T>(PropertyBinding<T> binding)
    {
        var property = (MjolnirProperty<T>)binding.Property;
        if (binding.Mode == BindingMode.OneTime)
        {
            // It replaces a binding of the property, as any other does, then lets go at once.
            PropertyBinding? before;
            lock (_sync)
            {
                var entry = GetOrAdd(property);
                before = entry.Binding;
                entry.Binding = null;
                entry.LocalOverride = false;
            }
            before?.Dispose();
            if (binding.TryRead(out var value)) Store(property, value, null);
            binding.Dispose();
            return binding;
        }
        PropertyBinding? replaced;
        lock (_sync)
        {
            var entry = GetOrAdd(property);
            replaced = entry.Binding;
            entry.Binding = binding;
            entry.LocalOverride = false;
        }
        replaced?.Dispose();
        Dispatcher.Run(binding.Sync); // live already: start now (or on the next frame, from another thread)
        return binding;
    }

    private void Raise<T>(MjolnirProperty<T> property, T old, T value)
    {
        var e = new MjolnirPropertyChangedEventArgs<T>(this, property, old, value);
        Changed(e, property.DirtyFor(old, value));
        if (GetType().Assembly != typeof(MjolnirObject).Assembly)
            ModCall.Run(GetType().Assembly, $"{this} OnPropertyChanged", () => OnPropertyChanged(e));
        _propertyChanged?.Raise(h => h(this, e));
    }

    private T DefaultOf<T>(MjolnirProperty<T> property)
    {
        return property.Coerce != null ? property.Coerce(this, property.DefaultValue) : property.DefaultValue;
    }

    private T Coerced<T>(MjolnirProperty<T> property, T value) => property.Coerce != null ? property.Coerce(this, value) : value;

    private bool Recompute<T>(ValueEntry<T> entry, out T old, out T value)
    {
        old = entry.Effective;
        value = Coerced(entry.Property, entry.HasRaw ? entry.Raw : entry.Property.DefaultValue);
        entry.Effective = value;
        return !EqualityComparer<T>.Default.Equals(old, value);
    }

    private ValueEntry<T>? Find<T>(MjolnirProperty<T> property)
    {
        int i = IndexOf(property.Id);
        return i >= 0 ? (ValueEntry<T>)_entries[i] : null;
    }

    private ValueEntry<T> GetOrAdd<T>(MjolnirProperty<T> property)
    {
        int i = IndexOf(property.Id);
        if (i >= 0) return (ValueEntry<T>)_entries[i];
        var entry = new ValueEntry<T>(property, DefaultOf(property));
        _entries.Insert(~i, entry);
        return entry;
    }

    // Binary search by property id; ~insertion point when absent.
    private int IndexOf(int id)
    {
        int low = 0, high = _entries.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            int at = _entries[mid].Property.Id;
            if (at == id) return mid;
            if (at < id) low = mid + 1;
            else high = mid - 1;
        }
        return ~low;
    }

    private abstract class ValueEntry(MjolnirProperty property)
    {
        public MjolnirProperty Property { get; } = property;
        public abstract PropertyBinding? CurrentBinding { get; }
        public abstract void DropBinding();
    }

    private sealed class ValueEntry<T>(MjolnirProperty<T> property, T effective) : ValueEntry(property)
    {
        public new MjolnirProperty<T> Property { get; } = property;
        public bool HasRaw;
        public bool LocalOverride;
        public T Raw = default!;
        public T Effective = effective;
        public PropertyBinding<T>? Binding;

        public override PropertyBinding? CurrentBinding => Binding;

        public override void DropBinding() => Binding = null;
    }
}
