using System;
using System.Collections.Generic;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The property store behind every element: defaults, set, clear, coercion, change events.</summary>
[Collection("MainThread")]
public sealed class PropertyTests : ViewTestBase
{
    [Fact]
    public void DefaultsComeFromTheRegistration()
    {
        var text = new UiTextBlock();
        Assert.Equal("", text.Text);
        Assert.True(double.IsNaN(text.Width));
        Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, new UiButton().HorizontalAlignment);
        Assert.Equal(-1, new UiStepper().SelectedIndex);
        Assert.Equal(BindingMode.TwoWay, UiSlider.ValueProperty.DefaultBindingMode);
        Assert.Equal(BindingMode.OneWay, UiTextBlock.TextProperty.DefaultBindingMode);
    }

    [Fact]
    public void ChangesRaisePropertyChangedOnceWithBothValues()
    {
        var text = new UiTextBlock();
        var seen = new List<MjolnirPropertyChangedEventArgs>();
        text.PropertyChanged += (_, e) => seen.Add(e);

        text.Text = "a";
        text.Text = "a"; // equal: silent
        text.Text = "b";

        Assert.Equal(2, seen.Count);
        Assert.Same(UiTextBlock.TextProperty, seen[1].Property);
        Assert.Equal("a", seen[1].GetOldValue<string>());
        Assert.Equal("b", ((MjolnirPropertyChangedEventArgs<string>)seen[1]).NewValue);
        Assert.Same(text, seen[1].Sender);
    }

    [Fact]
    public void ClearValueBringsTheDefaultBack()
    {
        var button = new UiButton { HorizontalAlignment = HorizontalAlignment.Left };
        button.ClearValue(Control.HorizontalAlignmentProperty);
        Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
    }

    [Fact]
    public void CoercionKeepsTheValueAskedFor()
    {
        var slider = new UiSlider { Value = 5 };
        var seen = new List<double>();
        slider.ValueChanged += (_, e) => seen.Add(e.NewValue);

        slider.Maximum = 3;
        Assert.Equal(3, slider.Value);
        slider.Maximum = 10;
        Assert.Equal(5, slider.Value);
        Assert.Equal([3, 5], seen);
    }

    [Fact]
    public void InitializerOrderDoesNotMatter()
    {
        var before = new UiSlider { Value = 150, Maximum = 200 };
        var after = new UiSlider { Maximum = 200, Value = 150 };
        Assert.Equal(150, before.Value);
        Assert.Equal(150, after.Value);
        Assert.Equal(0, new UiSlider { Minimum = -5 }.Value);
        Assert.Equal(2, new UiSlider { Minimum = 2 }.Value);
    }

    [Fact]
    public void SelectedIndexStaysWithinTheItems()
    {
        var selector = new UiStepper { ItemsSource = new[] { "a", "b" }, SelectedIndex = 7 };
        Assert.Equal(1, selector.SelectedIndex);
        Assert.Equal("b", selector.SelectedItem);
    }

    [Fact]
    public void ARegisteredNameIsTakenOnce()
    {
        Assert.NotNull(Counter.CountProperty); // registered by Counter's static constructor
        Assert.Throws<ArgumentException>(() => MjolnirProperty.Register<Counter, int>(nameof(Counter.Count)));
        Assert.Throws<ArgumentException>(() => MjolnirProperty.Register<Counter, int>("Mode", 0, BindingMode.Default));
    }

    [Fact]
    public void YourOwnPropertiesWorkLikeTheControls()
    {
        var counter = new Counter();
        var seen = new List<int>();
        counter.PropertyChanged += (_, e) => seen.Add(e.GetNewValue<int>());
        var source = new BindableValue<int>(3);

        counter.Count = 1;
        counter.Bind(Counter.CountProperty, source);
        Mount(counter);
        source.Value = 4;

        Assert.Equal(4, counter.Count);
        Assert.Equal([1, 3, 4], seen);
        Assert.Equal([1, 3, 4], counter.Overridden);
    }

    [Fact]
    public void GetObservableGivesTheValueNowAndEveryChange()
    {
        var text = new UiTextBlock { Text = "a" };
        var seen = new List<string>();
        var subscription = text.GetObservable(UiTextBlock.TextProperty).Subscribe(new Observer<string>(seen.Add));

        text.Text = "b";
        subscription.Dispose();
        text.Text = "c";

        Assert.Equal(["a", "b"], seen);
    }

    [Fact]
    public void ACoercedElementChangeComesFromAnyThread()
    {
        Tick(); // this thread is now the main thread
        var slider = new UiSlider();
        var view = Mount(slider);
        int applied = view.AppliedCount(DirtyFlags.Value);

        var worker = new System.Threading.Thread(() => slider.Value = 42);
        worker.Start();
        worker.Join();

        Assert.Equal(42, slider.Value);
        Assert.Equal(applied, view.AppliedCount(DirtyFlags.Value));
        Tick();
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Value));
    }

    internal sealed class Counter : UiUserControl
    {
        public static readonly MjolnirProperty<int> CountProperty = MjolnirProperty.Register<Counter, int>(nameof(Count));

        public List<int> Overridden { get; } = [];

        public int Count
        {
            get => GetValue(CountProperty);
            set => SetValue(CountProperty, value);
        }

        protected override void OnPropertyChanged(MjolnirPropertyChangedEventArgs e)
        {
            if (e.Property == CountProperty) Overridden.Add(e.GetNewValue<int>());
        }
    }
}

/// <summary>An observer from a function, for the tests.</summary>
internal sealed class Observer<T>(Action<T> next) : IObserver<T>
{
    public void OnNext(T value) => next(value);

    public void OnError(Exception error) { }

    public void OnCompleted() { }
}
