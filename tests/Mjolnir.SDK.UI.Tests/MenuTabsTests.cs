using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// Mod tabs in the game's settings menu and journal (MenuTabs): the bookkeeping behind Add and Remove, and the pure rules
/// of a tab (its order after the game's tabs, the letter without a picture, the hover card's second line). What the tabs
/// look like and how the game switches to them is checked in game (UiLab).
/// </summary>
[Collection("MainThread")]
public sealed class MenuTabsTests : ViewTestBase
{
    private static readonly System.Reflection.Assembly Owner = typeof(MenuTabsTests).Assembly;

    [Fact]
    public void TabsAreNamedPerMenuAndModInOrder()
    {
        var settings = new MenuTabs("Settings", MenuTabs.PageKind.Settings);
        var journal = new MenuTabs("Journal", MenuTabs.PageKind.Journal);
        var first = new UiTabItem { Header = "First" };
        var second = new UiTabItem { Header = "Second" };
        settings.Register(first, Owner);
        settings.Register(second, Owner);
        journal.Register(new UiTabItem { Header = "Notes" }, Owner);

        Assert.Equal([first, second], settings.Entries.Select(e => e.Element));
        Assert.Equal(["Mjolnir_Settings_Mjolnir.SDK.UI.Tests_0", "Mjolnir_Settings_Mjolnir.SDK.UI.Tests_1"], settings.Entries.Select(e => e.ObjectName));
        Assert.Equal("Mjolnir_Journal_Mjolnir.SDK.UI.Tests_0", journal.Entries.Single().ObjectName);
    }

    [Fact]
    public void AnItemAddedAgainStaysOneTab()
    {
        var tabs = new MenuTabs("Settings", MenuTabs.PageKind.Settings);
        var tab = new UiTabItem { Header = "Mine" };
        tabs.Register(tab, Owner);
        tabs.Register(tab, Owner);
        Assert.Single(tabs.Entries);
    }

    [Fact]
    public void ARemovedTabCanComeBackUnderANewName()
    {
        var tabs = new MenuTabs("Journal", MenuTabs.PageKind.Journal);
        var tab = new UiTabItem { Header = "Mine" };
        tabs.Register(tab, Owner);
        Assert.True(tabs.Remove(tab));
        Assert.False(tabs.Remove(tab)); // already out
        Assert.Empty(tabs.Entries);

        tabs.Register(tab, Owner);
        // The counter keeps counting: a new tab never takes the name of one still being destroyed.
        Assert.Equal("Mjolnir_Journal_Mjolnir.SDK.UI.Tests_1", tabs.Entries.Single().ObjectName);
    }

    [Fact]
    public void ModTabsComeAfterTheGamesOwn()
    {
        // The game counts its tabs' order from 0 (the settings menu 0..4, the journal 0..7).
        Assert.True(MenuTabRules.FirstOrder > 100);
    }

    [Theory]
    [InlineData("My mod", "M")]
    [InlineData("  42 days", "4")]
    [InlineData("ёлка", "Ё")]
    [InlineData("— notes", "N")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void ATabWithoutAPictureShowsItsHeadersFirstLetter(string? header, string letter) =>
        Assert.Equal(letter, MenuTabRules.Initial(header));

    [Fact]
    public void TheHoverCardNamesTheModThatAddedTheTab()
    {
        Assert.Equal("UI Example", MenuTabRules.Details("UI Example", "Mjolnir.UiExample"));
        Assert.Equal("Mjolnir.UiExample", MenuTabRules.Details(null, "Mjolnir.UiExample"));
        Assert.Equal("Mjolnir.UiExample", MenuTabRules.Details(" ", "Mjolnir.UiExample"));
        Assert.Equal("", MenuTabRules.Details(null, null));
    }
}
