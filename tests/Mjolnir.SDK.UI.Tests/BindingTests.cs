using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// Bindings: every source kind and mode, write-back, replacement and disposal, the live-only
/// subscription, view model paths, and the automatic conversions.
/// </summary>
[Collection("MainThread")]
public sealed class BindingTests : ViewTestBase
{
    [Fact]
    public void AOneWayBindingFollowsItsSourceWhileLiveAndReadsItThroughOtherwise()
    {
        var source = new BindableValue<string>("a");
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, source);
        Assert.Equal("a", text.Text);
        Assert.Equal(0, source.HandlerCount);

        source.Value = "b";
        Assert.Equal("b", text.Text); // read through, still no handler
        Assert.Equal(0, source.HandlerCount);

        var view = Mount(text);
        Assert.Equal(1, source.HandlerCount);
        int applied = view.AppliedCount(DirtyFlags.Text);
        source.Value = "c";
        Assert.Equal("c", text.Text);
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Text));

        Host.UnmountAll();
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public void ATwoWayBindingWritesBackWithoutALoop()
    {
        var source = new BindableValue<bool>();
        int raised = 0;
        source.Changed += (_, _) => raised++;
        var toggle = new UiToggleSwitch();
        toggle.Bind(UiToggleSwitch.IsCheckedProperty, source); // IsChecked binds two-way by default
        Mount(toggle);

        toggle.IsChecked = true;
        Assert.True(source.Value);
        source.Value = false;
        Assert.False(toggle.IsChecked);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void AReadOnlySourceBindsOneWayWhereThePropertyBindsTwoWay()
    {
        var source = new BindableValue<bool>();
        IReadOnlyBindable<bool> readOnly = source.Map(v => v); // a Map without a way back
        var toggle = new UiToggleSwitch();
        var changes = new List<(bool, bool)>();
        toggle.IsCheckedChanged += (_, e) => changes.Add((e.OldValue, e.NewValue));
        toggle.Bind(UiToggleSwitch.IsCheckedProperty, readOnly); // IsChecked binds two-way by default
        Mount(toggle);
        Assert.Equal(1, source.HandlerCount);

        source.Value = true;
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false; // the player's change goes nowhere
        Assert.True(source.Value);
        Assert.Equal([(false, true), (true, false)], changes);

        Host.UnmountAll();
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public void AValueSetOverAOneWayBindingHoldsUntilTheNextPush()
    {
        var source = new BindableValue<string>("a");
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, source);
        Mount(text);

        text.Text = "local";
        Assert.Equal("local", text.Text);
        Assert.Equal("a", source.Value);
        source.Value = "b";
        Assert.Equal("b", text.Text);
    }

    [Fact]
    public void ASecondBindingReplacesTheFirstAndDisposingKeepsTheValue()
    {
        var first = new BindableValue<string>("first");
        var second = new BindableValue<string>("second");
        var text = new UiTextBlock();
        Mount(text);

        text.Bind(UiTextBlock.TextProperty, first);
        Assert.Equal(1, first.HandlerCount);
        var binding = text.Bind(UiTextBlock.TextProperty, second);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal("second", text.Text);

        binding.Dispose();
        binding.Dispose();
        Assert.Equal(0, second.HandlerCount);
        second.Value = "changed";
        Assert.Equal("second", text.Text);
    }

    [Fact]
    public void ASettingsClampGoesBackToTheSlider()
    {
        var entry = NewConfig().Bind("S", "Count", 5).WithRange(0, 10);
        var slider = new UiSlider();
        slider.Bind(UiSlider.ValueProperty, entry); // int setting, double slider
        Mount(slider);

        slider.SetFromView(60);
        Assert.Equal(10, entry.Value);
        Assert.Equal(10, slider.Value);
    }

    [Fact]
    public void ACoercedValueIsNeverWrittenBack()
    {
        var source = new BindableValue<double>(50);
        var slider = new UiSlider();
        slider.Bind(UiSlider.ValueProperty, source);
        Mount(slider);

        slider.Maximum = 10;
        Assert.Equal(10, slider.Value);
        Assert.Equal(50, source.Value);
        slider.Maximum = 100;
        Assert.Equal(50, slider.Value);
    }

    [Fact]
    public void OneTimeReadsOnceAndNeverSubscribes()
    {
        var source = new BindableValue<string>("once");
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, source, BindingMode.OneTime);
        Mount(text);

        source.Value = "later";
        Assert.Equal("once", text.Text);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public void AFunctionIsPolledOnlyWhileOnScreenAndAppliedOnlyOnChange()
    {
        int n = 0;
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, () => $"n={n}");
        Assert.Equal("", text.Text); // not evaluated before it is shown
        var view = Mount(text);
        Assert.Equal("n=0", text.Text);
        int applied = view.AppliedCount(DirtyFlags.Text);

        Tick();
        Assert.Equal(applied, view.AppliedCount(DirtyFlags.Text));

        n = 1;
        Tick();
        Assert.Equal("n=1", text.Text);
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Text));

        Host.Active = false;
        n = 2;
        Tick();
        Assert.Equal("n=1", text.Text);
    }

    [Fact]
    public void AFunctionWithAnIntervalIsReadAtMostThatOftenAndAtOnceWhenShown()
    {
        long now = 1000;
        long second = PollClock.Ticks(TimeSpan.FromSeconds(1));
        var real = PollClock.Now;
        PollClock.Now = () => now;
        try
        {
            int calls = 0, n = 0;
            var text = new UiTextBlock();
            text.Bind(UiTextBlock.TextProperty, () => { calls++; return $"n={n}"; }, TimeSpan.FromSeconds(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => text.Bind(UiTextBlock.TextProperty, () => "", TimeSpan.FromSeconds(-1)));
            Mount(text);
            Assert.Equal(("n=0", 1), (text.Text, calls)); // read when it comes on screen

            n = 1;
            now += second / 2;
            Tick();
            Assert.Equal(("n=0", 1), (text.Text, calls)); // half a second: not read

            now += second / 2;
            Tick();
            Assert.Equal(("n=1", 2), (text.Text, calls));
            Tick();
            Assert.Equal(2, calls);
        }
        finally
        {
            PollClock.Now = real;
        }
    }

    [Fact]
    public void AFunctionCanBringItsElementBack()
    {
        bool shown = false;
        var text = new UiTextBlock();
        text.Bind(Control.IsVisibleProperty, () => shown);
        Mount(text);
        Assert.False(text.IsVisible);

        shown = true;
        Tick();
        Assert.True(text.IsVisible);
    }

    [Fact]
    public void AThrowingFunctionStaysInsideTheTick()
    {
        var text = new UiTextBlock();
        text.Bind<string, string>(UiTextBlock.TextProperty, () => throw new InvalidOperationException("boom"));
        Mount(text);
        Tick();
        Assert.Equal("", text.Text);
    }

    [Fact]
    public void AnObservableIsSubscribedWhileLiveAndConverted()
    {
        var slider = new UiSlider { Value = 2 };
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, slider.GetObservable(UiSlider.ValueProperty)); // double → string
        Mount(text);
        Assert.Equal("2", text.Text);

        slider.Value = 3.5;
        Assert.Equal(3.5.ToString(CultureInfo.CurrentCulture), text.Text);

        Host.UnmountAll();
        slider.Value = 9;
        Assert.Equal(3.5.ToString(CultureInfo.CurrentCulture), text.Text);
    }

    [Fact]
    public void ABindingMadeOnAnotherThreadStartsOnTheNextTick()
    {
        var source = new BindableValue<string>("from source");
        var text = new UiTextBlock();
        Mount(text);
        Tick(); // this thread is now the main thread

        var worker = new Thread(() => text.Bind(UiTextBlock.TextProperty, source));
        worker.Start();
        worker.Join();
        Assert.Equal(0, source.HandlerCount);

        Tick();
        Assert.Equal(1, source.HandlerCount);
        Assert.Equal("from source", text.Text);
    }

    [Fact]
    public void TextThatIsNotANumberGoesBackToTheSetting()
    {
        var entry = NewConfig().Bind("S", "Count", 5);
        var box = (UiTextBox)SettingControls.For(entry);
        Mount(box);
        Assert.Equal("5", box.Text);

        box.SetFromView("abc");
        Assert.Equal(5, entry.Value);
        Assert.Equal("5", box.Text);

        box.SetFromView(" 12 ");
        Assert.Equal(12, entry.Value);
        Assert.Equal("12", box.Text);
    }

    /// <summary>
    /// A description (an event's name, a log line) reads stored values only. It used to read a bound Content through its
    /// source, whose own description read the Content again: a stack overflow that killed the game.
    /// </summary>
    [Fact]
    public void DescribingAnElementNeverReadsItsBindings()
    {
        var label = new BindableValue<string>("Go");
        var button = new UiButton();
        button.Bind(UiButton.ContentProperty, label);
        button.Click += (_, _) => { }; // the event is named after the button
        Assert.Equal("UiButton ''", button.ToString());
        Assert.Equal("Go", button.Content);

        var slider = new UiSlider();
        slider.Bind(HeaderedControl.HeaderProperty, label);
        slider.Bind(Control.NameProperty, label);
        slider.ValueChanged += (_, _) => { };
        Assert.Equal("UiSlider", slider.ToString());
    }

    [Fact]
    public void AValueSetWhileNotShownHoldsUntilTheBindingStarts()
    {
        var source = new BindableValue<string>("a");
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, source);
        text.Text = "local";
        source.Value = "b";
        Assert.Equal("local", text.Text);

        Mount(text);
        Assert.Equal("b", text.Text);
    }

    [Fact]
    public void AnImpossibleConversionThrowsWhenBinding() =>
        Assert.Throws<ArgumentException>(() => new UiTextBlock().Bind(UiTextBlock.FontSizeProperty, new BindableValue<DateTime>()));

    [Fact]
    public void ConversionsFollowTheRules()
    {
        Assert.Equal(3, Convert<double, int>(2.5));
        Assert.Equal(-3, Convert<double, int>(-2.5));
        Assert.Equal(255, Convert<int, byte>(1000)); // saturating
        Assert.Equal(42, Convert<string, int>(" 42 "));
        Assert.False(AutoConvert<string, int>.Converter!("abc", out _));
        Assert.False(AutoConvert<string, double>.Converter!("NaN", out _));
        Assert.Equal(DayOfWeek.Friday, Convert<object, DayOfWeek>(5));
        Assert.Equal(DayOfWeek.Monday, Convert<object, DayOfWeek>("monday"));
        Assert.Equal(DayOfWeek.Monday, Convert<string, DayOfWeek>("Monday"));
        Assert.Equal(DayOfWeek.Tuesday, Convert<int, DayOfWeek>(2));
        Assert.Equal(5, Convert<DayOfWeek, int>(DayOfWeek.Friday));
        Assert.Equal(DayOfWeek.Sunday, Convert<DayOfWeek, object>(DayOfWeek.Sunday));
        Assert.False(Convert<bool?, bool>(null));
        Assert.True(Convert<string, bool>("True"));
        Assert.Null(AutoConvert<DateTime, double>.Converter);
        Assert.False(AutoConvert<Huge, int>.Converter!(Huge.Big, out _)); // too big for an int: refused, not thrown
        Assert.Equal(12, Convert<object, int>("12"));

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            Assert.Equal(1.5f, Convert<string, float>("1.5"));
            Assert.Equal(1.5f, Convert<string, float>("1,5"));
            Assert.Equal("1,5", Convert<double, string>(1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void AViewModelPathIsFollowedAtEveryStep()
    {
        var first = new Player { Name = "A" };
        var model = new Model { Player = first };
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, model, m => m.Player!.Name);
        Assert.Equal("A", text.Text); // read through before it is shown
        Assert.Equal(0, first.Listeners);

        Mount(text);
        Assert.Equal(1, first.Listeners);
        Assert.Equal(1, model.Listeners);
        first.Name = "B";
        Assert.Equal("B", text.Text);

        var second = new Player { Name = "C" };
        model.Player = second;
        Assert.Equal("C", text.Text);
        Assert.Equal(0, first.Listeners);
        Assert.Equal(1, second.Listeners);

        model.Player = null;
        Assert.Equal("", text.Text);

        Host.UnmountAll();
        Assert.Equal(0, model.Listeners);
    }

    [Fact]
    public void AViewModelPathBindsTwoWay()
    {
        var model = new Model();
        var toggle = new UiToggleSwitch();
        toggle.Bind(UiToggleSwitch.IsCheckedProperty, model, m => m.Fog);
        Mount(toggle);

        toggle.IsChecked = true;
        Assert.True(model.Fog);
        model.Fog = false;
        Assert.False(toggle.IsChecked);

        var count = new UiTextBlock();
        count.Bind(UiTextBlock.TextProperty, model, m => m.Clicks); // int → string
        Mount(count);
        model.Clicks = 3;
        Assert.Equal("3", count.Text);
    }

    [Fact]
    public void ABadPathThrowsWhenBinding()
    {
        var model = new Model();
        Assert.Throws<ArgumentException>(() => new UiToggleSwitch().Bind(UiToggleSwitch.IsCheckedProperty, model, m => m.ReadOnly));
        Assert.Throws<ArgumentException>(() => new UiTextBlock().Bind(UiTextBlock.TextProperty, model, m => m.ToString()));
        // One way, a read-only member is fine.
        new UiToggleSwitch().Bind(UiToggleSwitch.IsCheckedProperty, model, m => m.ReadOnly, BindingMode.OneWay);
    }

    private enum Huge : long
    {
        Big = 1L << 40,
    }

    private static TTo Convert<TFrom, TTo>(TFrom value)
    {
        Assert.True(AutoConvert<TFrom, TTo>.Converter!(value, out var result));
        return result;
    }

    private abstract class Notifying : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;

        public int Listeners => _changed?.GetInvocationList().Length ?? 0;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        protected void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            _changed?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed class Player : Notifying
    {
        private string _name = "";

        public string Name
        {
            get => _name;
            set => Set(ref _name, value, nameof(Name));
        }
    }

    private sealed class Model : Notifying
    {
        private Player? _player;
        private bool _fog;
        private int _clicks;

        public Player? Player
        {
            get => _player;
            set => Set(ref _player, value, nameof(Player));
        }

        public bool Fog
        {
            get => _fog;
            set => Set(ref _fog, value, nameof(Fog));
        }

        public int Clicks
        {
            get => _clicks;
            set => Set(ref _clicks, value, nameof(Clicks));
        }

        public bool ReadOnly => true;
    }
}
