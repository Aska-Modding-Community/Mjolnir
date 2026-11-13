using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>Tiles and cards (UiSlot, UiCard) and the grid that holds them (UiWrapPanel) on the fake backend.</summary>
[Collection("MainThread")]
public sealed class TilesTests : ViewTestBase
{
    [Fact]
    public void ASlotShowsNoCountOrStripUntilGivenOneAndKeepsItsFillInRange()
    {
        var slot = new UiSlot();
        Assert.Equal((null, null, false), (slot.Count, slot.FillFraction, slot.IsSelected));
        slot.FillFraction = 1.4;
        Assert.Equal(1, slot.FillFraction);
        slot.FillFraction = -0.2;
        Assert.Equal(0, slot.FillFraction);
        slot.FillFraction = null;
        Assert.Null(slot.FillFraction);
    }

    [Fact]
    public void ASlotsPartsMarkWhatTheyChange()
    {
        var slot = new UiSlot();
        var view = Mount(slot);
        int image = view.AppliedCount(DirtyFlags.Image), text = view.AppliedCount(DirtyFlags.Text), value = view.AppliedCount(DirtyFlags.Value);
        slot.Count = 12;
        slot.FillFraction = 0.5;
        slot.IsSelected = true;
        Assert.Equal(text + 1, view.AppliedCount(DirtyFlags.Text));
        Assert.Equal(value + 2, view.AppliedCount(DirtyFlags.Value));
        Assert.Equal(image, view.AppliedCount(DirtyFlags.Image));
    }

    [Fact]
    public void AClickOnASlotOrCardRaisesClickThenRunsTheCommandWhichDimsIt()
    {
        int clicks = 0, runs = 0;
        bool can = true;
        var command = new Command(() => runs++, () => can);
        var slot = new UiSlot { Command = command };
        var card = new UiCard { Title = "Thor", Command = command };
        slot.Click += (_, _) => clicks++;
        card.Click += (_, _) => clicks++;
        Mount(new UiStackPanel { Children = { slot, card } });
        slot.OnClick();
        card.OnClick();
        Assert.Equal((2, 2), (clicks, runs));

        can = false;
        command.RaiseCanExecuteChanged();
        Assert.False(slot.IsEffectivelyEnabled);
        Assert.False(card.IsEffectivelyEnabled);
        slot.OnClick();
        Assert.Equal((3, 2), (clicks, runs)); // the click is heard, the command does not run
    }

    [Fact]
    public void ACardsWidthScalesIt()
    {
        var card = new UiCard { Title = "Skadi", Subtitle = "Hunting" };
        Assert.Equal(1, card.Scale);
        card.Width = 93.5;
        Assert.Equal(0.5, card.Scale, 3);
        Assert.Equal("UiCard 'Skadi'", card.ToString());
    }

    [Fact]
    public void AWrapPanelCellsAreTheLargestChildsUnlessGivenAndNewChildrenMeasureAgain()
    {
        var panel = new UiWrapPanel();
        Assert.True(double.IsNaN(panel.ItemWidth));
        Assert.True(double.IsNaN(panel.ItemHeight));
        Assert.Equal((10, 10), (panel.ItemSpacing, panel.LineSpacing));
        Assert.Equal((Orientation.Horizontal, WrapPanelItemsAlignment.Start), (panel.Orientation, panel.ItemsAlignment));
        var view = Mount(panel);
        int layout = view.AppliedCount(DirtyFlags.Layout);
        panel.Children.Add(new UiSlot());
        Tick();
        Assert.True(view.AppliedCount(DirtyFlags.Layout) > layout);
    }

    [Fact]
    public void AListsItemsCanSitInAWrapPanel()
    {
        var items = new ObservableCollection<int> { 3, 7, 1 };
        var list = new UiItemsControl
        {
            ItemsSource = items,
            ItemsPanel = new FuncTemplate<Panel>(() => new UiWrapPanel { ItemWidth = 64, ItemHeight = 64 }),
            ItemTemplate = new FuncDataTemplate<int>(n => new UiSlot { Count = n }),
        };
        var view = Mount(list);
        var panel = Assert.Single(view.Children);
        Assert.IsType<UiWrapPanel>(panel.Element);
        Assert.Equal([3, 7, 1], panel.Children.Select(c => ((UiSlot)c.Element).Count));
        items.Add(5);
        Tick();
        Assert.Equal(4, panel.Children.Count);
    }
}
