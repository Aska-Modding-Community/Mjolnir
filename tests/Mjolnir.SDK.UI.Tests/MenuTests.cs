using System;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// Menu items with levels of their own (UiMenuItem) on the fake backend, the pause menu's level stack, and the button
/// registry holding menu items.
/// </summary>
[Collection("MainThread")]
public sealed class MenuTests : ViewTestBase
{
    [Fact]
    public void AnItemWithItemsOpensALevelAndItsItemsAreItsChildren()
    {
        var start = new UiMenuItem { Header = "Start" };
        var reset = new UiMenuItem { Header = "Reset" };
        var tools = new UiMenuItem { Header = "My mod", Items = { start, new UiMenuItem { Header = "Settings", Items = { reset } } } };
        Assert.True(tools.OpensLevel);
        Assert.False(start.OpensLevel);
        Assert.Same(tools, start.Parent);
        Assert.Equal(["Start", "Settings"], tools.LevelItems.Select(i => i.Header));

        var view = Mount(tools);
        Assert.Equal(2, view.Children.Count); // the level's items are built in its level
        Assert.Single(view.Children[1].Children); // and deeper
    }

    [Fact]
    public void OnlyMenuItemsAreShownInALevel()
    {
        var item = new UiMenuItem { Header = "A", Items = { new UiMenuItem { Header = "B" }, new UiTextBlock { Text = "not a menu item" } } };
        Assert.Equal(["B"], item.LevelItems.Select(i => i.Header));
        Assert.Single(Mount(item).Children);
    }

    [Fact]
    public void AnItemThatStartsOrStopsOpeningALevelIsBuiltAgain()
    {
        var item = new UiMenuItem { Header = "A" };
        var view = Mount(item);
        item.Items.Add(new UiMenuItem { Header = "B" }); // a level appears: a different button in the game's menu
        Assert.True(view.Disposed);
        var again = ViewOf(item);
        item.Items.Add(new UiMenuItem { Header = "C" }); // still a level: kept
        Assert.False(again.Disposed);
        item.Items.Clear();
        Assert.True(again.Disposed);
    }

    [Fact]
    public void AClickRaisesClickThenRunsTheCommandWhichDimsTheItem()
    {
        int clicks = 0, runs = 0;
        bool can = true;
        var command = new Command(() => runs++, () => can);
        var item = new UiMenuItem { Header = "Go", Command = command };
        item.Click += (_, _) => clicks++;
        Mount(item);
        item.OnClick();
        Assert.Equal((1, 1), (clicks, runs));

        can = false;
        command.RaiseCanExecuteChanged();
        Assert.False(item.IsEffectivelyEnabled); // dimmed in the menu
        item.OnClick();
        Assert.Equal((2, 1), (clicks, runs)); // the click is heard, the command does not run
    }

    [Fact]
    public void TheFluentOnClickWorksOnMenuItems()
    {
        int clicks = 0;
        var item = new UiMenuItem { Header = "Go" }.OnClick(() => clicks++);
        item.OnClick();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ThePauseMenusLevelsGoUpOneAtATime()
    {
        var levels = new LevelStack<string>();
        Assert.Null(levels.Current); // the menu's own buttons
        Assert.False(levels.Back()); // Esc is the game's
        levels.Open("tools");
        levels.Open("settings");
        Assert.Equal("settings", levels.Current);
        Assert.True(levels.Back());
        Assert.Equal("tools", levels.Current);
        levels.Open("settings");
        levels.Forget("tools"); // its item went: it and what it opened close
        Assert.Null(levels.Current);
        levels.Open("tools");
        levels.Clear(); // the menu closed
        Assert.Null(levels.Current);
    }

    [Fact]
    public void TheRegistryNamesMenuItemsLikeButtons()
    {
        var registry = new ButtonRegistry("Main");
        var button = registry.Add(new UiButton { Content = "button" }, typeof(MenuTests).Assembly);
        var item = registry.Add(new UiMenuItem { Header = "item" }, typeof(MenuTests).Assembly);
        Assert.Equal(("Mjolnir_Main_Mjolnir.SDK.UI.Tests_0", "Mjolnir_Main_Mjolnir.SDK.UI.Tests_1"), (button.ObjectName, item.ObjectName));
        Assert.True(registry.Remove(item.Element));
        Assert.Single(registry.Active());
    }
}
