using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The list with selection (UiListBox), the tabs (UiTabControl) and the hover card's rules (ToolTips) on the fake backend.
/// </summary>
[Collection("MainThread")]
public sealed class SelectionTests : ViewTestBase
{
    [Fact]
    public void AListBoxShowsARowPerItemAndSelectsOne()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var list = new UiListBox { ItemsSource = items };
        Assert.Equal(-1, list.SelectedIndex);
        Mount(list);
        Assert.Equal(["a", "b", "c"], list.Rows.Select(r => r.Item));
        Assert.All(list.Rows, r => Assert.False(r.IsSelected));

        SelectionChangedEventArgs? seen = null;
        list.SelectionChanged += (_, e) => seen = e;
        list.Rows[1].OnClick(); // the player
        Assert.Equal((1, "b"), (list.SelectedIndex, list.SelectedItem));
        Assert.Equal([false, true, false], list.Rows.Select(r => r.IsSelected));
        Assert.Equal(["b"], seen!.AddedItems.Cast<object>());

        list.SelectedIndex = 2; // code
        Assert.Equal("c", list.SelectedItem);
        Assert.True(list.Rows[2].IsSelected);
        items.Insert(0, "z"); // new rows: the selected one keeps its selection
        Assert.Equal(["z", "a", "b", "c"], list.Rows.Select(r => r.Item));
        Assert.True(list.Rows.Single(r => (string)r.Item! == "c").IsSelected);
    }

    [Fact]
    public void AListBoxRowHoldsWhatItsTemplateMakes()
    {
        var list = new UiListBox
        {
            ItemsSource = new[] { 1, 2 },
            ItemTemplate = new FuncDataTemplate<int>(n => new UiTextBlock { Text = $"Item {n}" }),
            SelectedItem = 2,
        };
        Mount(list);
        Assert.Equal(["Item 1", "Item 2"], list.Rows.Select(r => ((UiTextBlock)r.Content!).Text));
        Assert.Equal(1, list.SelectedIndex);
        Assert.True(list.Rows[1].IsSelected);
    }

    [Fact]
    public void TabsShowTheSelectedTabsContent()
    {
        var first = new UiTextBlock { Text = "first" };
        var second = new UiTextBlock { Text = "second" };
        var tabs = new UiTabControl
        {
            Items =
            {
                new UiTabItem { Header = "One", Content = first },
                new UiTabItem { Header = "Two", Content = second },
            },
        };
        Assert.Equal(0, tabs.SelectedIndex); // the first by default
        Mount(tabs);
        Assert.Equal(["One", "Two"], tabs.StripButtons.Select(b => b.Header));
        Assert.Equal([true, false], tabs.StripButtons.Select(b => b.IsSelected));
        Assert.NotNull(first.Parent); // shown
        Assert.Null(second.Parent);

        SelectionChangedEventArgs? seen = null;
        tabs.SelectionChanged += (_, e) => seen = e;
        tabs.StripButtons[1].OnClick();
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Null(first.Parent);
        Assert.NotNull(second.Parent);
        Assert.Equal("Two", ((UiTabItem)seen!.AddedItems[0]!).Header);
        Assert.Equal("One", ((UiTabItem)seen.RemovedItems[0]!).Header);
    }

    [Fact]
    public void IconTabsNameThemselvesInTheirHoverCardAndQEStepRound()
    {
        var tabs = new UiTabControl { Items = { new UiTabItem { Header = "One" }, new UiTabItem { Header = "Two" }, new UiTabItem { Header = "Three" } } };
        Mount(tabs);
        Assert.All(tabs.StripButtons, b => Assert.True(b.IsIconTab));
        Assert.Equal("One", tabs.StripButtons[0].ToolTip);
        tabs.Step(-1); // Q on the first: the last
        Assert.Equal(2, tabs.SelectedIndex);
        tabs.Step(1); // E on the last: the first
        Assert.Equal(0, tabs.SelectedIndex);

        tabs.TabStripPlacement = Dock.Left; // a list of categories
        Assert.All(tabs.StripButtons, b => Assert.False(b.IsIconTab));
        Assert.Null(tabs.StripButtons[0].ToolTip); // its header is on it
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void QAndEAreTheOuterTabControls()
    {
        var inner = new UiTabControl { TabStripPlacement = Dock.Left, Items = { new UiTabItem { Header = "Tools" } } };
        var outer = new UiTabControl { Items = { new UiTabItem { Header = "Categories", Content = inner } } };
        Mount(outer);
        Assert.False(outer.IsNested);
        Assert.True(inner.IsNested);
    }

    [Fact]
    public void TabsInAWindowsHeaderLeaveTheirOwnLayoutAndKeepWorking()
    {
        var content = new UiTextBlock { Text = "page" };
        var tabs = new UiTabControl { Items = { new UiTabItem { Header = "One", Content = content }, new UiTabItem { Header = "Two" } } };
        int replaced = 0;
        tabs.StripInHeader = true; // as a main window does with a tab control content, its tabs on top
        tabs.StripReplaced += (_, _) => replaced++;
        var view = Mount(tabs);
        var strip = tabs.Strip;
        Assert.DoesNotContain(view.Children.SelectMany(Flatten), v => ReferenceEquals(v.Element, strip)); // not in its own layout
        Assert.NotNull(content.Parent); // the page still shows
        Mount(strip); // where the window's header holds it
        Assert.Equal(["One", "Two"], tabs.StripButtons.Select(b => b.Header));
        tabs.StripButtons[1].OnClick();
        Assert.Equal(1, tabs.SelectedIndex);

        tabs.TabStripPlacement = Dock.Bottom; // laid out again: a new strip for the window to mount
        Tick();
        Assert.Equal(1, replaced);
        Assert.NotSame(strip, tabs.Strip);
    }

    private static System.Collections.Generic.IEnumerable<FakeView> Flatten(FakeView view) => new[] { view }.Concat(view.Children.SelectMany(Flatten));

    [Fact]
    public void ATabsHeaderChangeReachesItsButton()
    {
        var tab = new UiTabItem { Header = "Old" };
        var tabs = new UiTabControl { Items = { tab } };
        Mount(tabs);
        tab.Header = "New";
        Assert.Equal("New", tabs.StripButtons.Single().Header);
        tabs.Items.Add(new UiTabItem { Header = "Added" });
        Assert.Equal(["New", "Added"], tabs.StripButtons.Select(b => b.Header));
    }

    [Fact]
    public void AToolTipStringIsATitleAndText()
    {
        Assert.Equal(("Wood", "A log from a tree"), ToolTips.Split("Wood\nA log from a tree"));
        Assert.Equal(("Wood", (string?)null), ToolTips.Split(" Wood "));
        Assert.Equal(("Wood", "two\nlines"), ToolTips.Split("Wood\r\ntwo\nlines"));
    }

    [Fact]
    public void TheCardGoesRightOfTheElementOrLeftWithoutRoom()
    {
        // An element 100..200 wide, 400..440 high on a 1920×1080 screen; a 200×80 card.
        var (x, y, right) = ToolTips.Place((100, 400, 200, 440), (200, 80), (1920, 1080), 8);
        Assert.Equal((208f, 420f, true), (x, y, right));
        (x, _, right) = ToolTips.Place((1800, 400, 1900, 440), (200, 80), (1920, 1080), 8);
        Assert.Equal((1792f, false), (x, right)); // no room on the right
        (_, y, _) = ToolTips.Place((100, 0, 200, 20), (200, 80), (1920, 1080), 8);
        Assert.Equal(40f, y); // kept on screen
    }

    [Fact]
    public void AToolTipChangeMarksItsPart()
    {
        var button = new UiButton { Content = "Go" };
        var view = Mount(button);
        int before = view.AppliedCount(DirtyFlags.ToolTip);
        button.ToolTip = "Go\nStarts it";
        Assert.Equal(before + 1, view.AppliedCount(DirtyFlags.ToolTip));
    }
}
