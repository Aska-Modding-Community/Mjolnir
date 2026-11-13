using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// What the live tree and the model do with mistakes and mod code that changes the tree: an element twice in a panel, a
/// template giving an element the list sits in, a rebuild from a Loaded handler, a copy that cannot be built, a throwing
/// ToString in a binding, a one-time binding over another, a list's items changing under its selection, a tab whose
/// content is shown elsewhere.
/// </summary>
[Collection("MainThread")]
public sealed class TreeFaultsTests : ViewTestBase
{
    [Fact]
    public void AnElementCannotBeTwiceInAPanel()
    {
        var text = new UiTextBlock();
        var panel = new UiStackPanel { Children = { text } };
        Assert.Throws<InvalidOperationException>(() => panel.Children.Add(text));
        Assert.Throws<InvalidOperationException>(() => panel.Children.Insert(0, text));
        var other = new UiTextBlock();
        panel.Children.Add(other);
        Assert.Throws<InvalidOperationException>(() => panel.Children[1] = text);
        Assert.Equal([text, other], panel.Children);

        panel.Children[0] = text; // the same element in its own place is no change
        Assert.Equal([text, other], panel.Children);
    }

    [Fact]
    public void AListBoxTemplateGivingAnAncestorShowsTheItemsText()
    {
        var list = new UiListBox { ItemsSource = new[] { "a", "b" } };
        var root = new UiStackPanel { Children = { list } };
        list.ItemTemplate = new FuncDataTemplate<string>(_ => root); // would contain the list it sits in
        Mount(root); // no endless mount

        Assert.Equal(2, list.Rows.Count);
        Assert.All(list.Rows, row => Assert.IsType<UiTextBlock>(row.Content));
        Assert.Equal(["a", "b"], list.Rows.Select(row => ((UiTextBlock)row.Content!).Text));
        Assert.Null(root.Parent);
    }

    [Fact]
    public void AListBoxRowShowsNoContentItCouldNotAdopt()
    {
        var root = new UiStackPanel();
        var row = new ListBoxItem(new UiListBox(), "a");
        root.Children.Add(row);
        row.Content = root; // refused (logged): the row sits in it
        Assert.Empty(row.LogicalChildren);
    }

    [Fact]
    public void ALoadedHandlerThatRebuildsItsElementLeavesOneCopyInPlace()
    {
        var button = new UiButton { Content = "Go" };
        button.Loaded += (_, _) => button.Header = "A row now"; // a new shape: the copy is built again
        var panel = new UiStackPanel { Children = { new UiTextBlock(), button } };
        var view = Mount(panel);

        var node = panel.Nodes.Single();
        var buttonNode = node.Children[1];
        Assert.Same(button, buttonNode.Element);
        Assert.False(((FakeView)buttonNode.View).Disposed);
        Assert.Same(ViewOf(button), view.Children[1]);
        Assert.Equal(2, view.Children.Count);
        Assert.Single(button.Nodes);
    }

    [Fact]
    public void ALoadedHandlerThatRebuildsARootLeavesTheHostsRootInPlace()
    {
        var text = new UiTextBlock { Text = "a" };
        text.Loaded += (_, _) => text.Header = "Info";
        Mount(text);
        var root = Host.Roots.Single();
        Assert.Same(text, root.Element);
        Assert.False(((FakeView)root.View).Disposed);
        Assert.Single(text.Nodes);
        Assert.Same(ViewOf(text), Root.Children.Single());
    }

    [Fact]
    public void ACopyThatCannotBeBuiltLeavesNothingBehind()
    {
        var bad = new UiTextBlock { Name = "bad" };
        var good = new UiTextBlock { Name = "good" };
        var panel = new UiStackPanel { Children = { good, bad } };
        Backend.Refuses = element => ReferenceEquals(element, bad);
        Assert.Throws<InvalidOperationException>(() => Host.Mount(panel, Root));

        Assert.Empty(panel.Nodes);
        Assert.Empty(good.Nodes);
        Assert.All(Backend.Views, v => Assert.True(v.Disposed));
        Assert.Empty(Host.Roots);
    }

    [Fact]
    public void AChildThatCannotBeBuiltIsLeftOutAndTheOthersShow()
    {
        var panel = new UiStackPanel();
        var view = Mount(panel);
        var bad = new UiTextBlock { Name = "bad" };
        var good = new UiTextBlock { Name = "good" };
        Backend.Refuses = element => ReferenceEquals(element, bad);
        panel.Children.Add(bad); // logged, not thrown into the mod
        panel.Children.Add(good);

        Assert.Equal([good], view.Children.Select(c => c.Element));
        Assert.Empty(bad.Nodes);
        Assert.Single(good.Nodes);
    }

    [Fact]
    public void AThrowingToStringInABindingKeepsThePreviousText()
    {
        var source = new BindableValue<object?>(new Broken());
        var text = new UiTextBlock { Text = "before" };
        text.Bind(UiTextBlock.TextProperty, source);
        Mount(text);
        Assert.Equal("before", text.Text);

        source.Value = 12.5;
        Assert.Equal(12.5.ToString(CultureInfo.CurrentCulture), text.Text);
        source.Value = new Broken(); // the mod's fault stays out of the setter
        Assert.Equal(12.5.ToString(CultureInfo.CurrentCulture), text.Text);
    }

    [Fact]
    public void AThrowingPollLeavesTheOtherPollsOfTheFrame()
    {
        var count = 0;
        var broken = new UiTextBlock();
        broken.Bind(Control.IsVisibleProperty, () => new Unequal(), _ => true); // its Equals runs outside the mod's call
        var counted = new UiTextBlock();
        counted.Bind(UiTextBlock.TextProperty, () => (++count).ToString(CultureInfo.InvariantCulture));
        Mount(new UiStackPanel { Children = { broken, counted } });
        int before = count;
        Tick();
        Assert.Equal(before + 1, count);
    }

    [Fact]
    public void AOneTimeBindingReplacesTheBindingBefore()
    {
        var source = new BindableValue<string>("followed");
        var text = new UiTextBlock();
        text.Bind(UiTextBlock.TextProperty, source);
        Mount(text);
        Assert.Equal("followed", text.Text);

        text.Bind(UiTextBlock.TextProperty, new BindableValue<string>("once"), BindingMode.OneTime);
        Assert.Equal("once", text.Text);
        source.Value = "no longer followed";
        Assert.Equal("once", text.Text);
    }

    [Fact]
    public void AListBoxSelectionFollowsItsItems()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var list = new UiListBox { ItemsSource = items, SelectedItem = "c" };
        Mount(list);
        Assert.Equal(2, list.SelectedIndex);
        var seen = new List<SelectionChangedEventArgs>();
        list.SelectionChanged += (_, e) => seen.Add(e);

        items.RemoveAt(0); // the selected item moved
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("c", list.SelectedItem);
        Assert.Empty(seen); // the same item at another place is no change

        items.Remove("c"); // the selected item went
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(["c"], seen.Single().RemovedItems.Cast<object>());
        Assert.All(list.Rows, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public void ATabWhoseContentIsShownElsewhereShowsAnEmptyPage()
    {
        var first = new UiTextBlock { Text = "first" };
        var elsewhere = new UiTextBlock { Text = "elsewhere" };
        _ = new UiStackPanel { Children = { elsewhere } };
        var tabs = new UiTabControl
        {
            Items =
            {
                new UiTabItem { Header = "One", Content = first },
                new UiTabItem { Header = "Two", Content = elsewhere },
            },
        };
        Mount(tabs);
        Assert.Same(tabs, PageOf(first));

        tabs.SelectedIndex = 1;
        Assert.Null(first.Parent); // the first tab's page is not left on show
        Assert.NotSame(tabs, PageOf(elsewhere));
    }

    // The tab control an element's page belongs to, if any.
    private static UiTabControl? PageOf(Control element)
    {
        for (var at = element.Parent; at != null; at = at.Parent)
        {
            if (at is UiTabControl tabs) return tabs;
        }
        return null;
    }

    private sealed class Unequal
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("no equality");

        public override int GetHashCode() => 0;
    }

    private sealed class Broken
    {
        public override string ToString() => throw new InvalidOperationException("no text");
    }
}
