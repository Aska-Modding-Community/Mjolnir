using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// One binding of a property to a source, and the <see cref="IDisposable"/> that <c>Bind</c> returns. It follows its
/// source only while its object is live (<see cref="Sync"/>, main thread): a config entry never keeps a closed element
/// alive through the handler it would hold. Disposing it (from any thread, any number of times) lets go of the property,
/// which keeps its last value.
/// </summary>
internal abstract class PropertyBinding(MjolnirObject owner, MjolnirProperty property, BindingMode mode) : IDisposable
{
    private int _disposed;
    private volatile bool _active;

    public MjolnirObject Owner { get; } = owner;
    public MjolnirProperty Property { get; } = property;
    public BindingMode Mode { get; } = mode;

    /// <summary>Following its source now (main thread state, readable anywhere).</summary>
    public bool IsActive => _active;

    public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    /// <summary>Whether its source can be read on demand (an IBindable, a view model), so a property not live reads it through.</summary>
    public virtual bool CanRead => false;

    /// <summary>Called every frame while on screen (function bindings).</summary>
    public virtual bool IsPolled => false;

    public virtual void Poll() { }

    /// <summary>Starts following the source and pushes its current value.</summary>
    protected abstract void Subscribe();

    protected abstract void Unsubscribe();

    /// <summary>Main thread: active exactly while not disposed and its object is live.</summary>
    public void Sync()
    {
        bool want = !IsDisposed && Owner.IsLive;
        if (want == _active) return;
        _active = want;
        try
        {
            if (want) Subscribe();
            else Unsubscribe();
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"{this}: {(want ? "starting" : "stopping")} the binding failed: {e}");
        }
        if (IsPolled) LiveTree.SetPolled(this, want);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Owner.RemoveBinding(this);
        Dispatcher.Run(Sync);
    }

    public override string ToString() => $"{Owner} {Property.Name} binding";
}

/// <summary>A binding that pushes values of the property's type.</summary>
internal abstract class PropertyBinding<T>(MjolnirObject owner, MjolnirProperty<T> property, BindingMode mode) : PropertyBinding(owner, property, mode)
{
    public new MjolnirProperty<T> Property { get; } = property;

    /// <summary>The source's value now, converted; false if it cannot be read or converted.</summary>
    public virtual bool TryRead(out T value)
    {
        value = default!;
        return false;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the source (two-way) and returns what the property should keep: the source's
    /// value read back, so a config entry's clamp shows. Its own echo is ignored.
    /// </summary>
    public virtual T WriteBack(T value) => value;

    protected void Push(T value) => Owner.Store(Property, value, this);
}

/// <summary>A config entry, a <see cref="BindableValue{T}"/>, any <see cref="IBindable{T}"/>, or a read-only one (one way).</summary>
internal sealed class BindableBinding<T, TSource>(MjolnirObject owner, MjolnirProperty<T> property, IReadOnlyBindable<TSource> source, BindingMode mode,
                                                  BindingConverter<TSource, T> convert, BindingConverter<T, TSource>? convertBack)
    : PropertyBinding<T>(owner, property, mode)
{
    private readonly Assembly _sourceOwner = source.GetType().Assembly;
    private readonly string _what = $"{owner} {property.Name} source";
    private int _writing;

    public override bool CanRead => true;

    protected override void Subscribe()
    {
        source.Changed += OnSourceChanged;
        if (TryRead(out var value)) Push(value);
    }

    protected override void Unsubscribe() => source.Changed -= OnSourceChanged;

    public override bool TryRead(out T value)
    {
        value = default!;
        return ModCall.TryRun(_sourceOwner, _what, () => source.Value, out var raw) && convert.TryConvert(raw, out value);
    }

    public override T WriteBack(T value)
    {
        if (convertBack != null && source is IBindable<TSource> writable && convertBack.TryConvert(value, out var back))
        {
            Interlocked.Increment(ref _writing);
            try
            {
                ModCall.Run(_sourceOwner, _what, () => writable.Value = back);
            }
            finally
            {
                Interlocked.Decrement(ref _writing);
            }
        }
        // The source has the last word: a clamped or rejected value shows as the source keeps it.
        return TryRead(out var now) ? now : value;
    }

    private void OnSourceChanged(object? sender, ValueChangedEventArgs<TSource> e)
    {
        if (Volatile.Read(ref _writing) > 0) return; // our own write: WriteBack reads it back
        if (convert.TryConvert(e.NewValue, out var converted)) Push(converted);
    }
}

/// <summary>An <see cref="IObservable{T}"/>: subscribed while live.</summary>
internal sealed class ObservableBinding<T, TSource>(MjolnirObject owner, MjolnirProperty<T> property, IObservable<TSource> source,
                                                    BindingConverter<TSource, T> convert)
    : PropertyBinding<T>(owner, property, BindingMode.OneWay), IObserver<TSource>
{
    private readonly Assembly _sourceOwner = source.GetType().Assembly;
    private readonly string _what = $"{owner} {property.Name} observable";
    private IDisposable? _subscription;

    protected override void Subscribe()
    {
        if (ModCall.TryRun(_sourceOwner, _what, () => source.Subscribe(this), out var subscription))
            Interlocked.Exchange(ref _subscription, subscription)?.Dispose();
    }

    protected override void Unsubscribe()
    {
        var subscription = Interlocked.Exchange(ref _subscription, null);
        if (subscription != null) ModCall.Run(_sourceOwner, _what, subscription.Dispose);
    }

    public void OnNext(TSource value)
    {
        if (IsActive && convert.TryConvert(value, out var converted)) Push(converted);
    }

    public void OnError(Exception error) => SdkLog.Warn(GameUi.Source, $"{this}: the observable failed: {error.Message}");

    public void OnCompleted() { }
}

/// <summary>A function polled every frame while its object is on screen; the property changes only when the result does.</summary>
internal sealed class FuncBinding<T, TSource>(MjolnirObject owner, MjolnirProperty<T> property, Func<TSource> source, TimeSpan interval,
                                              BindingConverter<TSource, T> convert)
    : PropertyBinding<T>(owner, property, BindingMode.OneWay)
{
    private readonly string _what = $"{owner} {property.Name} function";
    private readonly long _interval = PollClock.Ticks(interval);
    private long _due;
    private bool _has;
    private TSource _last = default!;

    public override bool IsPolled => true;

    protected override void Subscribe()
    {
        _has = false;
        Read(); // the value is there before the first copy shows it
    }

    protected override void Unsubscribe() { }

    /// <summary>Every frame while on screen: reads the function once its interval has passed.</summary>
    public override void Poll()
    {
        if (_interval > 0 && PollClock.Now() < _due) return;
        Read();
    }

    private void Read()
    {
        if (_interval > 0) _due = PollClock.Now() + _interval;
        if (!ModCall.Evaluate(source, _what, out var value)) return;
        if (_has && EqualityComparer<TSource>.Default.Equals(value, _last)) return;
        _has = true;
        _last = value;
        if (convert.TryConvert(value, out var converted)) Push(converted);
    }
}

/// <summary>
/// The clock of polled bindings, in <see cref="Stopwatch"/> ticks: Elements/ keeps away from Unity's time, and unit tests
/// replace <see cref="Now"/> to step time by hand.
/// </summary>
internal static class PollClock
{
    public static Func<long> Now = Stopwatch.GetTimestamp;

    /// <summary><paramref name="interval"/> in clock ticks.</summary>
    public static long Ticks(TimeSpan interval) => (long)(interval.TotalSeconds * Stopwatch.Frequency);
}
