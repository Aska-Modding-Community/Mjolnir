using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>How a binding moves values between its source and a property.</summary>
public enum BindingMode
{
    /// <summary>
    /// The property's own default: <see cref="TwoWay"/> for the values a player edits (IsChecked, Slider.Value,
    /// SelectedIndex/SelectedItem, TextBox.Text), <see cref="OneWay"/> for everything else.
    /// </summary>
    Default,

    /// <summary>The source sets the property, and keeps setting it whenever it changes.</summary>
    OneWay,

    /// <summary>Like <see cref="OneWay"/>, and the property's changes (the player's, or code's) are written to the source.</summary>
    TwoWay,

    /// <summary>The source's value is read once, when the binding is made.</summary>
    OneTime,
}

/// <summary>
/// A property of a UI object that can be set, read, watched and bound. Every
/// control property has one (<c>UiTextBlock.TextProperty</c>) next to its plain CLR property (<c>Text</c>):
/// <c>text.Bind(UiTextBlock.TextProperty, config.Title)</c>. Give your own <see cref="UiUserControl"/> bindable
/// properties with <see cref="Register{TOwner, T}(string, T, BindingMode)"/>.
/// </summary>
public abstract class MjolnirProperty
{
    private static readonly ConcurrentDictionary<(Type Owner, string Name), MjolnirProperty> s_registered = new();
    private static int s_nextId;

    private protected MjolnirProperty(string name, Type propertyType, Type ownerType, BindingMode defaultBindingMode, DirtyFlags affects)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        DefaultBindingMode = defaultBindingMode;
        Affects = affects;
        Id = Interlocked.Increment(ref s_nextId);
    }

    public string Name { get; }

    /// <summary>The type of the property's value.</summary>
    public Type PropertyType { get; }

    /// <summary>The class that registered it.</summary>
    public Type OwnerType { get; }

    /// <summary>What <see cref="BindingMode.Default"/> means for this property.</summary>
    public BindingMode DefaultBindingMode { get; }

    /// <summary>Order of the property in an object's value store.</summary>
    internal int Id { get; }

    /// <summary>What a change of the property refreshes on screen.</summary>
    internal DirtyFlags Affects { get; }

    /// <summary>
    /// Registers a property of <typeparamref name="TOwner"/> (a <see cref="UiUserControl"/> or <see cref="UiWindow"/>
    /// subclass of yours). Keep the result in a <c>public static readonly</c> field named after it
    /// (<c>CountProperty</c>) and wrap it in a CLR property calling <see cref="MjolnirObject.GetValue{T}"/> and
    /// <see cref="MjolnirObject.SetValue{T}"/>. Its changes raise <see cref="MjolnirObject.PropertyChanged"/>; what they show is up to you.
    /// </summary>
    public static MjolnirProperty<T> Register<TOwner, T>(string name, T defaultValue = default!, BindingMode defaultBindingMode = BindingMode.OneWay)
        where TOwner : MjolnirObject =>
        Register<TOwner, T>(name, defaultValue, DirtyFlags.None, defaultBindingMode);

    /// <summary>The SDK's controls: what a change refreshes, coercion, and changes of an element's shape.</summary>
    /// <param name="name">The CLR property's name.</param>
    /// <param name="defaultValue">The value of an object that never set it.</param>
    /// <param name="affects">What a change refreshes on screen.</param>
    /// <param name="defaultBindingMode">What <see cref="BindingMode.Default"/> means for the property.</param>
    /// <param name="coerce">Makes a value valid against the owner's other properties (a slider's value against its limits).
    /// It reads them with <see cref="MjolnirObject.GetStored{T}"/> and runs under the owner's lock: SDK code only.</param>
    /// <param name="reshape">Extra refresh for a change from the old value to the new one (<see cref="DirtyFlags.Rebuild"/>
    /// when a header appears).</param>
    internal static MjolnirProperty<T> Register<TOwner, T>(string name, T defaultValue, DirtyFlags affects,
                                                      BindingMode defaultBindingMode = BindingMode.OneWay,
                                                      Func<TOwner, T, T>? coerce = null, Func<T, T, DirtyFlags>? reshape = null)
        where TOwner : MjolnirObject
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (defaultBindingMode == BindingMode.Default)
            throw new ArgumentException($"{typeof(TOwner).Name}.{name}: a property's default binding mode must be OneWay, TwoWay or OneTime");
        var property = new MjolnirProperty<T>(name, typeof(TOwner), defaultValue, defaultBindingMode, affects,
                                         coerce == null ? null : (owner, value) => coerce((TOwner)owner, value), reshape);
        if (!s_registered.TryAdd((typeof(TOwner), name), property))
            throw new ArgumentException($"{typeof(TOwner).Name} already has a property named {name}");
        return property;
    }

    /// <summary>The mode a binding of this property uses for <paramref name="mode"/>.</summary>
    internal BindingMode Resolve(BindingMode mode) => mode == BindingMode.Default ? DefaultBindingMode : mode;

    public override string ToString() => $"{OwnerType.Name}.{Name}";
}

/// <summary>A <see cref="MjolnirProperty"/> holding values of type <typeparamref name="T"/>.</summary>
public sealed class MjolnirProperty<T> : MjolnirProperty
{
    internal MjolnirProperty(string name, Type ownerType, T defaultValue, BindingMode defaultBindingMode, DirtyFlags affects,
                        Func<MjolnirObject, T, T>? coerce, Func<T, T, DirtyFlags>? reshape)
        : base(name, typeof(T), ownerType, defaultBindingMode, affects)
    {
        DefaultValue = defaultValue;
        Coerce = coerce;
        Reshape = reshape;
    }

    /// <summary>The value of an object that never set it.</summary>
    public T DefaultValue { get; }

    internal Func<MjolnirObject, T, T>? Coerce { get; }

    internal Func<T, T, DirtyFlags>? Reshape { get; }

    /// <summary>What a change from <paramref name="old"/> to <paramref name="value"/> refreshes on screen.</summary>
    internal DirtyFlags DirtyFor(T old, T value) => Reshape == null ? Affects : Affects | Reshape(old, value);
}
