using System;
using System.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// <see cref="MjolnirObject.GetObservable{T}"/>: the value now, then every change. A subscriber's OnNext is mod code: a throw
/// blames the observer's mod.
/// </summary>
internal sealed class PropertyObservable<T>(MjolnirObject owner, MjolnirProperty<T> property) : IObservable<T>
{
    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var assembly = observer.GetType().Assembly;
        string what = $"{owner} {property.Name} observer";
        EventHandler<MjolnirPropertyChangedEventArgs> handler = (_, e) =>
        {
            if (ReferenceEquals(e.Property, property)) ModCall.Run(assembly, what, () => observer.OnNext(e.GetNewValue<T>()));
        };
        owner.PropertyChanged += handler;
        ModCall.Run(assembly, what, () => observer.OnNext(owner.GetValue(property)));
        return new Subscription(() => owner.PropertyChanged -= handler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
