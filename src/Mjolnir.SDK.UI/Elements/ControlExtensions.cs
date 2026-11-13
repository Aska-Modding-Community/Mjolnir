using System;
using System.Linq.Expressions;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// An element built in one expression. An object initializer can set
/// properties but cannot bind one or subscribe to an event; these methods do, and return the element itself:
/// <code>
/// var hello = new UiButton { Content = "Say hello" }.OnClick(() => Notify.Message("Hi"));
/// var clicks = new UiTextBlock { Header = "Clicks" }.WithBinding(UiTextBlock.TextProperty, vm, x => x.Clicks);
/// </code>
/// A binding made here lasts as long as the element: use <see cref="MjolnirObject"/>'s Bind for an
/// <see cref="IDisposable"/> that ends it earlier. A handler given as an <see cref="Action"/> is still your mod's: if it
/// throws, your mod is disabled.
/// </summary>
public static class ControlExtensions
{
    /// <summary>Binds <paramref name="property"/> to a config entry or any other <see cref="IBindable{T}"/> (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TSource>(this TElement element, MjolnirProperty<T> property, IBindable<TSource> source,
                                                             BindingMode mode = BindingMode.Default, Func<TSource, T>? convert = null,
                                                             Func<T, TSource>? convertBack = null)
        where TElement : MjolnirObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, source, mode, convert, convertBack);
        return element;
    }

    /// <summary>Binds <paramref name="property"/> to a source that can only be read, one way (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TSource>(this TElement element, MjolnirProperty<T> property, IReadOnlyBindable<TSource> source,
                                                             Func<TSource, T>? convert = null)
        where TElement : MjolnirObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, source, convert);
        return element;
    }

    /// <summary>Binds <paramref name="property"/> to an observable, one way (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TSource>(this TElement element, MjolnirProperty<T> property, IObservable<TSource> source,
                                                             Func<TSource, T>? convert = null)
        where TElement : MjolnirObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, source, convert);
        return element;
    }

    /// <summary>Binds <paramref name="property"/> to a function, polled every frame while on screen (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TSource>(this TElement element, MjolnirProperty<T> property, Func<TSource> source,
                                                             Func<TSource, T>? convert = null)
        where TElement : MjolnirObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, source, convert);
        return element;
    }

    /// <summary>Binds <paramref name="property"/> to a function, polled at most once per <paramref name="interval"/> while on screen (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TSource>(this TElement element, MjolnirProperty<T> property, Func<TSource> source,
                                                             TimeSpan interval, Func<TSource, T>? convert = null)
        where TElement : MjolnirObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, source, interval, convert);
        return element;
    }

    /// <summary>Binds <paramref name="property"/> to a member of a view model (see Bind).</summary>
    public static TElement WithBinding<TElement, T, TModel, TValue>(this TElement element, MjolnirProperty<T> property, TModel model,
                                                                    Expression<Func<TModel, TValue>> path, BindingMode mode = BindingMode.Default,
                                                                    Func<TValue, T>? convert = null, Func<T, TValue>? convertBack = null)
        where TElement : MjolnirObject
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Bind(property, model, path, mode, convert, convertBack);
        return element;
    }

    /// <summary>Calls <paramref name="click"/> when the player clicks the button (<see cref="UiButton.Click"/>).</summary>
    public static UiButton OnClick(this UiButton button, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        button.Click += (_, _) => Run(click, $"{button}.Click");
        return button;
    }

    /// <summary>Subscribes <paramref name="handler"/> to <see cref="UiButton.Click"/>.</summary>
    public static UiButton OnClick(this UiButton button, EventHandler handler)
    {
        button.Click += handler;
        return button;
    }

    /// <summary>Calls <paramref name="click"/> when the player clicks the menu item (<see cref="UiMenuItem.Click"/>).</summary>
    public static UiMenuItem OnClick(this UiMenuItem item, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        item.Click += (_, _) => Run(click, $"{item}.Click");
        return item;
    }

    /// <summary>Subscribes <paramref name="handler"/> to <see cref="UiMenuItem.Click"/>.</summary>
    public static UiMenuItem OnClick(this UiMenuItem item, EventHandler handler)
    {
        item.Click += handler;
        return item;
    }

    /// <summary>Calls <paramref name="click"/> when the player clicks the slot (<see cref="UiSlot.Click"/>).</summary>
    public static UiSlot OnClick(this UiSlot slot, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        slot.Click += (_, _) => Run(click, $"{slot}.Click");
        return slot;
    }

    /// <summary>Calls <paramref name="click"/> when the player clicks the card (<see cref="UiCard.Click"/>).</summary>
    public static UiCard OnClick(this UiCard card, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        card.Click += (_, _) => Run(click, $"{card}.Click");
        return card;
    }

    /// <summary>Calls <paramref name="changed"/> with the new value after it changed (<see cref="UiSlider.ValueChanged"/>).</summary>
    public static UiSlider OnValueChanged(this UiSlider slider, Action<double> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        slider.ValueChanged += (_, e) => Run(changed, $"{slider}.ValueChanged", e.NewValue);
        return slider;
    }

    /// <summary>Calls <paramref name="changed"/> with the new value after it changed (<see cref="ProgressBase.ValueChanged"/>).</summary>
    public static TProgress OnValueChanged<TProgress>(this TProgress bar, Action<double> changed) where TProgress : ProgressBase
    {
        ArgumentNullException.ThrowIfNull(changed);
        bar.ValueChanged += (_, e) => Run(changed, $"{bar}.ValueChanged", e.NewValue);
        return bar;
    }

    /// <summary>Calls <paramref name="changed"/> with the new state after it changed (<see cref="UiToggleSwitch.IsCheckedChanged"/>).</summary>
    public static UiToggleSwitch OnIsCheckedChanged(this UiToggleSwitch toggle, Action<bool> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        toggle.IsCheckedChanged += (_, e) => Run(changed, $"{toggle}.IsCheckedChanged", e.NewValue);
        return toggle;
    }

    /// <summary>Calls <paramref name="changed"/> with the selected item after the selection changed (<see cref="SelectingItemsControl.SelectionChanged"/>).</summary>
    public static TSelector OnSelectionChanged<TSelector>(this TSelector selector, Action<object?> changed) where TSelector : SelectingItemsControl
    {
        ArgumentNullException.ThrowIfNull(changed);
        selector.SelectionChanged += (_, _) => Run(changed, $"{selector}.SelectionChanged", selector.SelectedItem);
        return selector;
    }

    /// <summary>Calls <paramref name="changed"/> with the new text after it changed (<see cref="UiTextBox.TextChanged"/>).</summary>
    public static UiTextBox OnTextChanged(this UiTextBox textBox, Action<string> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        textBox.TextChanged += (_, e) => Run(changed, $"{textBox}.TextChanged", e.NewText);
        return textBox;
    }

    /// <summary>Calls <paramref name="loaded"/> with the Unity object of every live copy once it is built (<see cref="Control.Loaded"/>).</summary>
    public static TElement OnLoaded<TElement>(this TElement element, Action<RectTransform> loaded) where TElement : Control
    {
        ArgumentNullException.ThrowIfNull(loaded);
        element.Loaded += (_, e) => Run(loaded, $"{element}.Loaded", e.RectTransform);
        return element;
    }

    // The handler is the mod's and runs as its own code: a throw blames the mod that wrote it.
    private static void Run(Action handler, string what) => ModCall.Run(handler.Method.DeclaringType?.Assembly, what, handler);

    private static void Run<TArg>(Action<TArg> handler, string what, TArg arg) =>
        ModCall.Run(handler.Method.DeclaringType?.Assembly, what, handler, arg);
}
