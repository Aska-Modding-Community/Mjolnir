using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The Mods window's list on the fake backend: a row per installed mod (loaded or not) with its status, the Off/On
/// selector writing the player's choice for the next start, the footer's note
/// while a change waits for a restart, and the questions about mods that cannot load without one another.
/// </summary>
[Collection("MainThread")]
public sealed class ModsListTests : ViewTestBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-modslist-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _asked = [];
    private bool _answer;

    private static readonly InstalledMod Running = InstalledMod.Make("run", "Runner", "1.0", InstalledState.Running, description: "Does things");
    private static readonly InstalledMod Off = InstalledMod.Make("off", "Off Mod", "1.0", InstalledState.TurnedOff);
    private static readonly InstalledMod Missing = InstalledMod.Make("miss", "Misser", "1.0", InstalledState.NotLoaded,
                                                                     new InstalledProblem(InstalledProblemKind.Missing, "gone", ">=1.0"));
    private static readonly InstalledMod NeedsOff = InstalledMod.Make("needy", "Needy", "1.0", InstalledState.NotLoaded,
                                                                      new InstalledProblem(InstalledProblemKind.DependencyTurnedOff, "off"),
                                                                      dependencies: [new ModDependencyInfo("off", "", false)]);
    private static readonly InstalledMod Failed = InstalledMod.Make("fail", "Failer", "1.0", InstalledState.Failed);

    void IDisposable.Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        Dispose();
    }

    private InstalledMods Mods(InstalledMod[] mods, params string[] turnedOff) =>
        new(mods, Path.Combine(_root, "mod-list.json"), turnedOff);

    private UiStackPanel List(InstalledMods mods) =>
        ModsWindow.ListContent(mods, "Settings", (title, text, yes, no) =>
        {
            _asked.Add(text);
            return Task.FromResult(_answer);
        });

    private static ModRow RowOf(UiStackPanel list, string id) => list.Children.OfType<ModRow>().Single(r => r.Name == $"Mod {id}");

    [Fact]
    public void EveryInstalledModHasARowWithWhatBecameOfIt()
    {
        var list = List(Mods([Running, Off, Missing, NeedsOff, Failed], "off"));
        Assert.Equal(["Mod run", "Mod off", "Mod miss", "Mod needy", "Mod fail"], list.Children.OfType<ModRow>().Select(r => r.Name));
        Assert.Equal("Does things", RowOf(list, "run").Description);
        Assert.Equal("Turned off", RowOf(list, "off").Description);
        Assert.Contains("Not loaded: needs gone >=1.0, which is not installed", RowOf(list, "miss").Description);
        Assert.Contains("Not loaded: needs Off Mod, which is turned off", RowOf(list, "needy").Description); // by its name
        Assert.Contains("Failed to load", RowOf(list, "fail").Description);
        Assert.Contains("<color=", RowOf(list, "miss").Description); // problems in amber
        Assert.Equal([true, false, true, true, true], list.Children.OfType<ModRow>().Select(r => r.IsOn));
        Assert.All(list.Children.OfType<ModRow>(), r => Assert.Null(r.SettingsText)); // none runs with settings here
        Assert.StartsWith("Runner  <color=", RowOf(list, "run").Header); // the version after the name, dimmed
        Assert.StartsWith("<alpha=#80>Off Mod  <color=", RowOf(list, "off").Header); // a mod that is off: all of it dimmer
    }

    [Fact]
    public void TheRowsComeWithoutAHeaderAndATurnedOnModIsNotDimmed()
    {
        var mods = Mods([Running, Off, Missing], "off");
        var list = List(mods);
        Assert.All(list.Children, c => Assert.IsType<ModRow>(c)); // nothing but the rows: they are plainly mods
        RowOf(list, "off").SetOnFromView(true);
        Assert.StartsWith("Off Mod  <color=", RowOf(list, "off").Header); // not dimmed any more
    }

    [Fact]
    public void TheSelectorWritesTheChoiceAndTheListSaysWhatARestartWillChange()
    {
        var mods = Mods([Running, Off], "off");
        var list = List(mods);
        var settings = new UiButton { Name = "PlatformSettings" };
        var close = new UiButton { Name = "Close" };
        var footer = ModsWindow.ListFooter(mods, settings, close);
        Assert.Equal("*,220,220", footer.ColumnDefinitions); // the note takes the room the buttons leave
        var note = Assert.IsType<UiTextBlock>(footer.Children[0]);
        Assert.Equal("RestartNote", note.Name);
        Assert.Equal([note, settings, close], footer.Children); // the platform's Settings beside Close
        Assert.Equal((220.0, 36.0), (close.Width, note.Height)); // as tall as the button
        Assert.Equal((18.0, 18.0), (note.FontSize, close.FontSize)); // the footer's readable letters, inherited
        long now = 1;
        var real = PollClock.Now;
        PollClock.Now = () => now;
        try
        {
            Mount(list);
            Mount(footer);
            Assert.Equal(0, note.Opacity); // hidden, its place kept: the window never changes size for it
            Assert.True(note.IsVisible);

            RowOf(list, "run").SetOnFromView(false);
            Assert.False(mods.IsEnabled(Running));
            Assert.Contains("Turns off after a restart", RowOf(list, "run").Description);
            Assert.Contains("\"run\"", File.ReadAllText(Path.Combine(_root, "mod-list.json")));
            now += PollClock.Ticks(TimeSpan.FromSeconds(1));
            Tick();
            Assert.Equal(1, note.Opacity);

            RowOf(list, "run").SetOnFromView(true);
            RowOf(list, "off").SetOnFromView(true);
            Assert.Equal("Does things", RowOf(list, "run").Description);
            Assert.Contains("Turns on after a restart", RowOf(list, "off").Description);
            RowOf(list, "off").SetOnFromView(false);
            now += PollClock.Ticks(TimeSpan.FromSeconds(1));
            Tick();
            Assert.Equal(0, note.Opacity); // back as at this start
            Assert.Empty(_asked);
        }
        finally
        {
            PollClock.Now = real;
        }
    }

    [Fact]
    public void TurningOffAModOthersNeedAsksFirst()
    {
        var lib = InstalledMod.Make("lib", "Lib", "1.0", InstalledState.Running);
        var user = InstalledMod.Make("user", "User", "1.0", InstalledState.Running, dependencies: [new ModDependencyInfo("lib", "", false)]);
        var mods = Mods([lib, user]);
        var list = List(mods);

        _answer = false;
        RowOf(list, "lib").SetOnFromView(false);
        Assert.Equal(["User cannot load without Lib. Turn off Lib anyway?"], _asked);
        Assert.True(RowOf(list, "lib").IsOn); // put back
        Assert.True(mods.IsEnabled(lib));

        _answer = true;
        RowOf(list, "lib").SetOnFromView(false);
        Assert.False(mods.IsEnabled(lib));
        Assert.True(mods.IsEnabled(user)); // keeps the player's choice; it comes back with Lib
    }

    [Fact]
    public void TurningOnAModThatNeedsTurnedOffOnesOffersToTurnThemOnToo()
    {
        var mods = Mods([Off, NeedsOff], "off", "needy");
        var list = List(mods);

        _answer = false;
        RowOf(list, "needy").SetOnFromView(true);
        Assert.Equal(["Needy cannot load without Off Mod, which is turned off. Turn on Off Mod too?"], _asked);
        Assert.True(mods.IsEnabled(NeedsOff));
        Assert.False(mods.IsEnabled(Off)); // only this mod

        RowOf(list, "needy").SetOnFromView(false);
        _answer = true;
        RowOf(list, "needy").SetOnFromView(true);
        Assert.True(mods.IsEnabled(Off));
        Assert.True(RowOf(list, "off").IsOn);
        Assert.Contains("Turns on after a restart", RowOf(list, "off").Description);
    }

    [Fact]
    public void AChoiceThatCannotBeWrittenPutsTheSelectorBack()
    {
        var mods = new InstalledMods([Running], "", []);
        var list = List(mods);
        RowOf(list, "run").SetOnFromView(false);
        Assert.True(RowOf(list, "run").IsOn);
        Assert.Equal("Does things", RowOf(list, "run").Description);
    }

    [Fact]
    public void EveryReasonHasAText()
    {
        var mods = Mods([Running]);
        foreach (var kind in Enum.GetValues<InstalledProblemKind>())
        {
            string text = ModsWindow.ReasonOf(mods, new InstalledProblem(kind, "run", ">=2.0", "1.0"));
            Assert.DoesNotContain("mods.reason", text); // a missing key comes back as itself
        }
        Assert.Equal("needs Runner >=2.0, but 1.0 is installed",
                     ModsWindow.ReasonOf(mods, new InstalledProblem(InstalledProblemKind.WrongVersion, "run", ">=2.0", "1.0")));
        Assert.Equal("needs gone, which is not installed",
                     ModsWindow.ReasonOf(mods, new InstalledProblem(InstalledProblemKind.Missing, "gone", "*")));
    }

    [Fact]
    public void ThePlatformIsTheTitleNotARow()
    {
        Assert.StartsWith("Mjolnir  <size=70%><color=", ModsWindow.ListTitle); // its version beside its name, smaller
        Assert.EndsWith($"{ModsWindow.PlatformVersion}</color></size>", ModsWindow.ListTitle);
        var list = List(Mods([Running]));
        Assert.Equal(["Mod run"], list.Children.Select(c => c.Name)); // only the mods
        Assert.Equal(ModsWindow.RowContentWidth, RowOf(list, "run").ContentWidth); // the Off/On selector leaves room
    }

    [Fact]
    public void AnEmptyListSaysWhereModsGo()
    {
        var list = List(Mods([]));
        var none = Assert.IsType<UiTextBlock>(Assert.Single(list.Children));
        Assert.Equal("NoMods", none.Name);
        Assert.Contains("Mjolnir/Mods", none.Text);
        Assert.DoesNotContain(List(Mods([Running])).Children, c => c.Name == "NoMods");
    }

    [Fact]
    public void ARowIsAViewOfItsOwn()
    {
        var list = List(Mods([Running]));
        Mount(list);
        Assert.Single(Backend.Views, v => v.Element is ModRow && !v.Disposed);
    }
}
