using System;
using System.Collections.Generic;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The fluent layer (ControlExtensions): With binds and returns the element, On… subscribes and returns it, a handler
/// given as an Action gets the new value, and one that throws stops neither the caller nor the other handlers.
/// </summary>
[Collection("MainThread")]
public sealed class FluentTests : ViewTestBase
{
    [Fact]
    public void WithBindsAndReturnsTheElement()
    {
        var source = new BindableValue<string>("Hello");
        var text = new UiTextBlock { Header = "Greeting" }.WithBinding(UiTextBlock.TextProperty, source);
        Mount(text);
        Assert.Equal("Hello", text.Text);

        source.Value = "Hi";
        Assert.Equal("Hi", text.Text);
    }

    [Fact]
    public void WithBindsAViewModelPathAndAFunction()
    {
        var model = new Model { Count = 3 };
        int polled = 7;
        var count = new UiTextBlock().WithBinding(UiTextBlock.TextProperty, model, x => x.Count);
        var poll = new UiTextBlock().WithBinding(UiTextBlock.TextProperty, () => polled);
        Mount(new UiStackPanel { Children = { count, poll } });
        Assert.Equal("3", count.Text);
        Assert.Equal("7", poll.Text);
    }

    [Fact]
    public void OnClickRunsTheActionForThePlayersClick()
    {
        int clicks = 0;
        var button = new UiButton { Content = "Go" }.OnClick(() => clicks++);
        button.OnClick();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AThrowingActionStopsNeitherTheClickNorTheOtherHandlers()
    {
        int after = 0;
        var button = new UiButton { Content = "Go" }.OnClick(() => throw new InvalidOperationException("a broken mod")).OnClick(() => after++);
        button.OnClick();
        Assert.Equal(1, after);
    }

    [Fact]
    public void ValueHandlersGetTheNewValue()
    {
        var seen = new List<object?>();
        var slider = new UiSlider { Maximum = 10 }.OnValueChanged(v => seen.Add(v));
        var toggle = new UiToggleSwitch().OnIsCheckedChanged(v => seen.Add(v));
        var text = new UiTextBox().OnTextChanged(v => seen.Add(v));
        var selector = new UiStepper { ItemsSource = new[] { "a", "b" } }.OnSelectionChanged(v => seen.Add(v));

        slider.Value = 4;
        toggle.IsChecked = true;
        text.Text = "typed";
        selector.SelectedIndex = 1;
        Assert.Equal([4.0, true, "typed", "b"], seen);
    }

    private sealed class Model
    {
        public int Count { get; set; }
    }
}
