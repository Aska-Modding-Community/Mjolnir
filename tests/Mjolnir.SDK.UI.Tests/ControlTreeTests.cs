using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The element model and its live tree, without Unity: mounting, changes, reconciling children by identity, events and
/// commands, items controls, user controls, selectors, layout plans, the Mods page and changes from other threads.
/// </summary>
[Collection("MainThread")]
public sealed class ControlTreeTests : ViewTestBase
{
    [Fact]
    public void MountingBuildsEveryElementOnceInOrder()
    {
        var a = new UiTextBlock { Text = "a" };
        var b = new UiButton { Content = "b" };
        var panel = new UiStackPanel { Children = { a, null!, b } };
        var view = Mount(panel);

        Assert.Equal([a, b], view.Children.Select(c => c.Element));
        Assert.All(Backend.Views, v => Assert.Equal(1, v.CreatedCalls));
        Assert.Equal(DirtyFlags.All, view.Applied[0]);
    }

    [Fact]
    public void ChildrenChangesAreReconciledByIdentity()
    {
        var a = new UiTextBlock { Text = "a" };
        var b = new UiTextBlock { Text = "b" };
        var c = new UiTextBlock { Text = "c" };
        var panel = new UiStackPanel { Children = { a, b } };
        var view = Mount(panel);
        var viewOfA = ViewOf(a);

        panel.Children.Add(c);
        panel.Children.Remove(b);
        panel.Children.Move(1, 0); // c, a

        Assert.Equal([c, a], view.Children.Select(v => v.Element));
        Assert.Same(viewOfA, ViewOf(a)); // kept, not rebuilt
        Assert.True(Backend.Views.Single(v => ReferenceEquals(v.Element, b)).Disposed);
    }

    [Fact]
    public void AnElementHasOneParent()
    {
        var shared = new UiTextBlock();
        _ = new UiStackPanel { Children = { shared } };
        Assert.Throws<InvalidOperationException>(() => new UiStackPanel { Children = { shared } });
    }

    [Fact]
    public void ValueEventsComeFromCodeBindingsAndThePlayer()
    {
        var seen = new List<(double Old, double New)>();
        var slider = new UiSlider { Maximum = 10 };
        slider.ValueChanged += (_, e) => seen.Add((e.OldValue, e.NewValue));
        var view = Mount(slider);

        slider.Value = 3;
        slider.IsSnapToTickEnabled = true;
        slider.TickFrequency = 2;
        slider.SetFromView(6.9); // the player's move snaps to the ticks

        Assert.Equal([(0, 3), (3, 6)], seen);
        Assert.True(view.AppliedCount(DirtyFlags.Value) >= 3);
    }

    [Fact]
    public void TextChangesWhenThePlayerFinishesEditing()
    {
        var seen = new List<string>();
        var box = new UiTextBox { Text = "a" };
        box.TextChanged += (_, e) => seen.Add($"{e.OldText}>{e.NewText}");
        Mount(box);

        box.SetFromView("b");
        box.SetFromView("b");
        Assert.Equal(["a>b"], seen);
    }

    [Fact]
    public void EveryClickHandlerRunsEvenIfOneThrows()
    {
        int count = 0;
        EventHandler throwing = (_, _) => throw new InvalidOperationException("first");
        EventHandler counting = (_, _) => count++;
        var button = new UiButton { Content = "Go" };
        button.Click += throwing + counting; // a combined handler is kept as its parts
        button.Click += counting;
        button.OnClick();
        Assert.Equal(2, count);
    }

    [Fact]
    public void ACommandDimsTheButtonAndRunsWithItsParameter()
    {
        bool can = false;
        object? ran = null;
        var command = new Command(p => ran = p, _ => can);
        var button = new UiButton { Command = command, CommandParameter = "p" };
        Assert.Equal(0, command.HandlerCount);
        var view = Mount(button);
        Assert.Equal(1, command.HandlerCount);
        Assert.False(button.ShownEnabled);
        Assert.True(button.IsEnabled); // the command never writes IsEnabled

        button.OnClick();
        Assert.Null(ran);

        can = true;
        int applied = view.AppliedCount(DirtyFlags.Enabled);
        command.RaiseCanExecuteChanged();
        Assert.True(button.ShownEnabled);
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Enabled));
        button.OnClick();
        Assert.Equal("p", ran);

        Host.UnmountAll();
        Assert.Equal(0, command.HandlerCount);
    }

    [Fact]
    public void AnItemsControlFollowsItsCollection()
    {
        var items = new ObservableCollection<string> { "a", "b" };
        int built = 0;
        var list = new UiItemsControl
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>(s =>
            {
                built++;
                return new UiTextBlock { Text = s };
            }),
        };
        var view = Mount(list);
        var panel = view.Children.Single();
        Assert.Equal(2, panel.Children.Count);

        items.Add("c");
        items.RemoveAt(0);
        items.Move(1, 0); // c, b
        Assert.Equal(["c", "b"], panel.Children.Select(v => ((UiTextBlock)v.Element).Text));
        Assert.Equal(3, built); // existing items were not rebuilt

        items.Clear();
        Assert.Empty(ViewOf(list).Children.Single().Children);
    }

    [Fact]
    public void AnItemsControlShowsItemsWithoutATemplateAsText()
    {
        var list = new UiItemsControl
        {
            ItemsSource = new object[] { 1, "two" },
            ItemsPanel = new FuncTemplate<Panel>(() => new UiStackPanel { Orientation = Orientation.Horizontal }),
        };
        var panel = Mount(list).Children.Single();
        Assert.Equal(Orientation.Horizontal, ((UiStackPanel)panel.Element).Orientation);
        Assert.Equal(["1", "two"], panel.Children.Select(v => ((UiTextBlock)v.Element).Text));
    }

    [Fact]
    public void AUserControlShowsItsContentAndReplacesIt()
    {
        var control = new Greeting();
        var view = Mount(control);
        var first = view.Children.Single();
        Assert.Equal("Hello", ((UiTextBlock)first.Element).Text);

        control.Content = new UiTextBlock { Text = "Bye" };
        Assert.True(first.Disposed);
        Assert.Equal("Bye", ((UiTextBlock)view.Children.Single().Element).Text);
    }

    [Fact]
    public void AHeaderAppearingRebuildsTheCopyAndKeepsItsBinding()
    {
        var source = new BindableValue<bool>();
        var toggle = new UiToggleSwitch();
        toggle.Bind(UiToggleSwitch.IsCheckedProperty, source);
        var panel = new UiStackPanel { Children = { toggle } };
        var view = Mount(panel);
        var bare = ViewOf(toggle);

        toggle.Header = "Fog";
        Assert.True(bare.Disposed);
        Assert.Same(ViewOf(toggle), view.Children.Single());
        Assert.Equal(1, source.HandlerCount);
        source.Value = true;
        Assert.True(toggle.IsChecked);
    }

    [Fact]
    public void AnIconAppearingRebuildsTheRowAndAnotherOneDoesNot()
    {
        var row = new UiButton { Header = "My Mod", Content = "Settings" };
        var view = Mount(new UiStackPanel { Children = { row } });
        var plain = ViewOf(row);

        row.Icon = new Bitmap("a.png");
        var withIcon = ViewOf(row);
        Assert.True(plain.Disposed); // the label moves right of the picture: a new copy
        Assert.Same(withIcon, view.Children.Single());

        row.Icon = new Bitmap("b.png");
        Assert.Same(withIcon, ViewOf(row)); // same shape: only the label part refreshes
        Assert.Equal(2, withIcon.AppliedCount(DirtyFlags.Label)); // when built, and for the new picture

        row.Icon = null;
        Assert.True(withIcon.Disposed);
    }

    [Fact]
    public void AModWithoutAPictureGetsALetterTileOfItsOwnColour()
    {
        var withPicture = new UiTextBlock { Header = "UI Example" };
        ModsWindow.ShowIcon(withPicture, "C:/Mods/UiExample/icon.png", "UI Example", "mjolnir.uiexample");
        Assert.Equal("C:/Mods/UiExample/icon.png", withPicture.Icon?.Path);
        Assert.Null(withPicture.Monogram);

        var without = new UiTextBlock { Header = "Infinite Stamina" };
        ModsWindow.ShowIcon(without, null, "Infinite Stamina", "mjolnir.infinitestamina");
        Assert.Null(without.Icon);
        Assert.Equal(Monogram.For("Infinite Stamina", "mjolnir.infinitestamina"), without.Monogram);
    }

    [Fact]
    public void AMonogramIsTheFirstLetterOnAStableHue()
    {
        Assert.Equal("I", Monogram.For("Infinite Stamina", "a").Letter);
        Assert.Equal("Ä", Monogram.For("  äsk mod", "a").Letter); // the first letter or digit, upper case
        Assert.Equal("7", Monogram.For("[7 Days]", "a").Letter);
        Assert.Equal("?", Monogram.For("--", "a").Letter);

        var hue = Monogram.For("x", "mjolnir.uiexample").Hue;
        Assert.InRange(hue, 0f, 1f);
        Assert.NotEqual(1f, hue);
        Assert.Equal(hue, Monogram.For("y", "MJOLNIR.UiExample").Hue); // ids compare case-insensitively
        Assert.NotEqual(hue, Monogram.For("x", "mjolnir.infinitestamina").Hue);
    }

    [Fact]
    public void AProgressBarKeepsItsValueInsideItsLimits()
    {
        var bar = new UiProgressBar { Maximum = 200, Value = 250 };
        var seen = new List<double>();
        bar.ValueChanged += (_, e) => seen.Add(e.NewValue);
        Assert.Equal((200d, 100d), (bar.Value, bar.Percentage));

        bar.Maximum = 500; // the value asked for comes back
        Assert.Equal((250d, 50d), (bar.Value, bar.Percentage));
        bar.Value = -5;
        Assert.Equal(0, bar.Percentage);
        Assert.Equal([250d, 0d], seen);

        bar.Minimum = bar.Maximum = 10; // no range: empty, not NaN
        Assert.Equal(0, bar.Percentage);
    }

    [Fact]
    public void AProgressBarFormatsItsTextWithValuePercentageAndLimits()
    {
        var bar = new UiProgressBar { Maximum = 200, Value = 80 };
        Assert.Equal("40%", bar.ProgressText); // the default, {1:0}%
        bar.ProgressTextFormat = "{0:0}/{3:0}";  // the game's own "80/200"
        Assert.Equal("80/200", bar.ProgressText);
        bar.ProgressTextFormat = "{9}";          // broken: the default instead of an exception
        Assert.Equal("40%", bar.ProgressText);
        bar.ProgressTextFormat = null!;
        Assert.Equal("{1:0}%", bar.ProgressTextFormat);
    }

    [Fact]
    public void AProgressBarCopyFollowsItsValueAndText()
    {
        var bar = new UiProgressBar { ShowProgressText = true };
        var view = Mount(bar);
        int values = view.AppliedCount(DirtyFlags.Value), texts = view.AppliedCount(DirtyFlags.Text);

        bar.Value = 30;
        bar.ProgressTextFormat = "{0:0} pts";
        Assert.Equal(values + 1, view.AppliedCount(DirtyFlags.Value));
        Assert.Equal(texts + 1, view.AppliedCount(DirtyFlags.Text));
        Assert.Equal("30 pts", bar.ProgressText);
    }

    [Fact]
    public void ASelectorKeepsItsItemAndIndexTogether()
    {
        var seen = new List<string>();
        var selector = new UiStepper { ItemsSource = Enum.GetValues<DayOfWeek>() };
        selector.SelectionChanged += (_, e) => seen.Add($"{(e.RemovedItems.Count > 0 ? e.RemovedItems[0] : "-")}>{e.AddedItems[0]}");
        Assert.Equal(Enum.GetNames<DayOfWeek>(), selector.ItemTexts);

        selector.SelectedItem = DayOfWeek.Friday;
        Assert.Equal(5, selector.SelectedIndex);
        selector.SetIndexFromView(1);
        Assert.Equal(DayOfWeek.Monday, selector.SelectedItem);
        Assert.Equal(["->Friday", "Friday>Monday"], seen);

        selector.ItemsSource = new[] { DayOfWeek.Sunday, DayOfWeek.Monday };
        Assert.Equal(1, selector.SelectedIndex); // the selected item keeps its place in the new items
    }

    [Fact]
    public void ASelectorBindsAnEnumSettingByItemNotByPosition()
    {
        var entry = NewConfig().Bind("S", "Day", DayOfWeek.Tuesday);
        var selector = (SelectingItemsControl)SettingControls.For(entry); // seven names: a combo box
        Mount(selector);
        Assert.Equal(2, selector.SelectedIndex);

        selector.SetIndexFromView(5);
        Assert.Equal(DayOfWeek.Friday, entry.Value);
        entry.Value = DayOfWeek.Sunday;
        Assert.Equal(0, selector.SelectedIndex);
    }

    [Fact]
    public void ChangesFromAnotherThreadAreAppliedOnTheNextTickCoalesced()
    {
        var text = new UiTextBlock { Text = "start" };
        var view = Mount(text);
        Tick(); // this thread is now the main thread
        int applied = view.AppliedCount(DirtyFlags.Text);

        // A real thread: Task.Wait may run a task inline on the waiting (main) thread.
        var worker = new Thread(() =>
        {
            for (int i = 1; i <= 100; i++) text.Text = $"v{i}";
        });
        worker.Start();
        worker.Join();
        Assert.Equal(applied, view.AppliedCount(DirtyFlags.Text));

        Tick();
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Text));
        Assert.Equal("v100", text.Text);
    }

    [Fact]
    public void PostedActionsRunOnTheNextTick()
    {
        int ran = 0;
        Dispatcher.MainThread.Post(() => ran++);
        Assert.Equal(0, ran);
        Tick();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void DeadHostsAreDropped()
    {
        var view = Mount(new UiTextBlock());
        Host.Alive = false;
        Tick();
        Assert.True(view.Disposed);
    }

    [Fact]
    public void AnItemsControlShowsItsOwnPropertiesWithoutRebuildingItsItems()
    {
        int built = 0;
        var list = new UiItemsControl { ItemTemplate = new FuncDataTemplate<string>(s => { built++; return new UiTextBlock { Text = s }; }) };
        list.Bind(UiItemsControl.ItemsSourceProperty, new BindableValue<IEnumerable?>(new[] { "a", "b" }));
        var view = Mount(list);
        Assert.Equal(2, built); // once, though the bound source arrived as the list went live
        int visible = view.AppliedCount(DirtyFlags.Visible), layout = view.AppliedCount(DirtyFlags.Layout);

        list.IsVisible = false;
        list.Width = 300;
        Assert.Equal(visible + 1, view.AppliedCount(DirtyFlags.Visible));
        Assert.Equal(layout + 1, view.AppliedCount(DirtyFlags.Layout));
        Assert.Equal(2, built);
    }

    [Fact]
    public void AnElementCannotContainOneItSitsIn()
    {
        var panel = new UiStackPanel();
        var border = new UiBorder { Child = panel };
        Assert.Throws<InvalidOperationException>(() => panel.Children.Add(border));

        // Through a content property it is logged and not shown; mounting ends.
        var inner = new UiBorder();
        var outer = new UiBorder { Child = inner };
        inner.Child = outer;
        var view = Mount(outer);
        Assert.Empty(view.Children.Single().Children);

        // A list cannot show the panel it sits in: the item becomes its text.
        var list = new UiItemsControl();
        var column = new UiStackPanel { Children = { list } };
        list.ItemsSource = new object[] { column };
        var items = Mount(column).Children.Single().Children.Single().Children;
        Assert.Equal("UiStackPanel", ((UiTextBlock)items.Single().Element).Text);
    }

    [Fact]
    public void AThrowingTemplateOrItemTextStaysInsideTheList()
    {
        var list = new UiItemsControl { ItemsSource = new object[] { new BadItem() }, ItemTemplate = new ThrowingTemplate() };
        var items = Mount(list).Children.Single().Children;
        Assert.Equal("", ((UiTextBlock)items.Single().Element).Text);
    }

    [Fact]
    public void SelectionChangedTellsTheItemThatWasSelected()
    {
        var seen = new List<string>();
        var selector = new UiStepper { ItemsSource = new[] { "A", "B", "C" }, SelectedItem = "B" };
        selector.SelectionChanged += (_, e) => seen.Add($"{Join(e.RemovedItems)}>{Join(e.AddedItems)}");

        selector.ItemsSource = new[] { "B", "C" }; // B moves: no change of selection
        Assert.Equal(0, selector.SelectedIndex);
        selector.ItemsSource = new[] { "X", "Y" }; // B is gone
        Assert.Equal(-1, selector.SelectedIndex);
        selector.SelectedIndex = 1;
        Assert.Equal(["B>", ">Y"], seen);
    }

    private static string Join(IList items) => string.Join(",", items.Cast<object?>());

    [Fact]
    public void LayoutPlansFollowAlignmentAndMargin()
    {
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Stretch, true), LayoutPlan.For(new UiTextBlock(), Orientation.Vertical));
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Stretch, true), LayoutPlan.For(new UiButton { Header = "Row" }, Orientation.Vertical));
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Center, false), LayoutPlan.For(new UiButton(), Orientation.Vertical, fixedSize: true));
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Center, false), LayoutPlan.For(new UiTextBlock { Width = 100 }, Orientation.Vertical));
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Center, false), LayoutPlan.For(new UiImage(), Orientation.Horizontal, fixedSize: true));
        Assert.Equal(new LayoutPlan(true, LayoutAlign.Start, false),
                     LayoutPlan.For(new UiButton { HorizontalAlignment = HorizontalAlignment.Left }, Orientation.Vertical));
        Assert.Equal(new LayoutPlan(true, LayoutAlign.Stretch, true), LayoutPlan.For(new UiTextBlock { Margin = new Thickness(8) }, Orientation.Vertical));
        // Along the panel's direction alignment is ignored; across a row it is the vertical one.
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Stretch, true),
                     LayoutPlan.For(new UiTextBlock { HorizontalAlignment = HorizontalAlignment.Right }, Orientation.Horizontal));
        Assert.Equal(new LayoutPlan(true, LayoutAlign.End, false),
                     LayoutPlan.For(new UiTextBlock { VerticalAlignment = VerticalAlignment.Bottom }, Orientation.Horizontal));
    }

    [Fact]
    public void TheModsPageHasAHeaderPerSectionAndABoundControlPerSetting()
    {
        var config = NewConfig();
        var fog = config.Bind("Look", "Fog", true, "Fog on or off");
        config.Bind("Look", "Speed", 1f).WithRange(0f, 5f);
        config.Bind("Name", "Title", "x");

        var page = ModsWindow.SettingsContent(config);
        Assert.Equal(["UiSectionHeader", "UiToggleSwitch", "UiSlider", "UiSectionHeader", "UiTextBox"], page.Children.Select(c => c.GetType().Name));
        var toggle = (UiToggleSwitch)page.Children[1];
        Assert.Equal("Fog", toggle.Header);
        Assert.Equal("Fog on or off", toggle.Description);
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false;
        Assert.False(fog.Value);

        var slider = (UiSlider)page.Children[2];
        Assert.Equal(5, slider.Maximum);
        slider.SetFromView(1.2499);
        Assert.Equal(1.25f, config.Entries.OfType<Config.ConfigEntry<float>>().Single().Value); // rounded to what it shows
    }

    [Fact]
    public void TheModsPageOrdersSettingsNamesThemAndAsksForARestart()
    {
        var config = NewConfig();
        config.Bind("Look", "Late", 1).WithOrder(5);
        var fog = config.Bind("Look", "Fog", true, "Fog on or off\nSecond line").WithDisplayName("Fog of war").WithRestartRequired();
        config.Bind("Look", "Early", "x").WithOrder(-1);
        config.Bind("Look", "Middle", 2.5);

        long now = 0;
        var real = PollClock.Now;
        PollClock.Now = () => now;
        try
        {
            var page = ModsWindow.SettingsContent(config);
            var rows = page.Children.OfType<HeaderedControl>().ToList();
            Assert.Equal(["Early", "Fog of war", "Middle", "Late"], rows.Select(r => r.Header)); // order, then binding order
            Assert.Equal("Fog on or off (applies after a restart)", rows[1].Description);
            Assert.Null(rows[2].Description);

            // The note stands in the footer, before Reset and Close, and keeps its place while hidden.
            var reset = new UiButton { Name = "Reset" };
            var close = new UiButton { Name = "Close" };
            var footer = ModsWindow.SettingsFooter(config, reset, close);
            Assert.Equal("*,220,220", footer.ColumnDefinitions);
            var note = Assert.IsType<UiTextBlock>(footer.Children[0]);
            Assert.Equal("RestartNote", note.Name);
            Assert.Equal([note, reset, close], footer.Children);
            Mount(page);
            Mount(footer);
            Assert.Equal(0, note.Opacity); // nothing changed yet

            fog.Value = false;
            now += PollClock.Ticks(TimeSpan.FromSeconds(1));
            Tick();
            Assert.Equal(1, note.Opacity);

            fog.Value = true; // back as it was when the page was made: no restart needed
            now += PollClock.Ticks(TimeSpan.FromSeconds(1));
            Tick();
            Assert.Equal(0, note.Opacity);
        }
        finally
        {
            PollClock.Now = real;
        }
    }

    [Fact]
    public void ARestartNoteNeedsNoDescription()
    {
        Assert.Equal("Applies after a restart.", SettingControls.DescriptionOf(null, true));
        Assert.Equal("Kept", SettingControls.DescriptionOf("Kept", false));
        Assert.Null(SettingControls.DescriptionOf(null, false));
    }

    private sealed class BadItem
    {
        public override string ToString() => throw new InvalidOperationException("no text");
    }

    private sealed class ThrowingTemplate : IDataTemplate
    {
        public bool Match(object? data) => throw new InvalidOperationException("no match");

        public Control? Build(object? data) => null;
    }

    private sealed class Greeting : UiUserControl
    {
        public Greeting() => Content = new UiTextBlock { Text = "Hello" };
    }
}
