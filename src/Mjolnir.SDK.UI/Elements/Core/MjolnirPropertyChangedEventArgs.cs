using System;

namespace Mjolnir.SDK.UI;

/// <summary>A property of a <see cref="MjolnirObject"/> changed.</summary>
public abstract class MjolnirPropertyChangedEventArgs : EventArgs
{
    private protected MjolnirPropertyChangedEventArgs(MjolnirObject sender, MjolnirProperty property)
    {
        Sender = sender;
        Property = property;
    }

    /// <summary>The object whose property changed.</summary>
    public MjolnirObject Sender { get; }

    public MjolnirProperty Property { get; }

    /// <summary>The value before, boxed; <see cref="GetOldValue{T}"/> for a typed one.</summary>
    public object? OldValue => BoxedOld;

    /// <summary>The value now, boxed; <see cref="GetNewValue{T}"/> for a typed one.</summary>
    public object? NewValue => BoxedNew;

    private protected abstract object? BoxedOld { get; }
    private protected abstract object? BoxedNew { get; }

    public T GetOldValue<T>() => this is MjolnirPropertyChangedEventArgs<T> typed ? typed.OldValue : (T)BoxedOld!;

    public T GetNewValue<T>() => this is MjolnirPropertyChangedEventArgs<T> typed ? typed.NewValue : (T)BoxedNew!;
}

/// <summary>A change of a <see cref="MjolnirProperty{T}"/>, with typed values.</summary>
public sealed class MjolnirPropertyChangedEventArgs<T> : MjolnirPropertyChangedEventArgs
{
    internal MjolnirPropertyChangedEventArgs(MjolnirObject sender, MjolnirProperty<T> property, T oldValue, T newValue) : base(sender, property)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public new MjolnirProperty<T> Property { get; }

    public new T OldValue { get; }

    public new T NewValue { get; }

    private protected override object? BoxedOld => OldValue;
    private protected override object? BoxedNew => NewValue;
}
