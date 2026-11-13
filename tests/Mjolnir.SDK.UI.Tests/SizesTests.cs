using System;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// Sizes: an element's LayoutElement sizes with maximums (LayoutPlan.Sizes), a window's size on screen (WindowFit), a row's
/// control parts fitted to another width (SettingRows.MapX), the interface scale (UiScale), and the new properties.
/// </summary>
[Collection("MainThread")]
public sealed class SizesTests : ViewTestBase
{
    private static LayoutSizes Sizes(Control element, Orientation panel = Orientation.Vertical, (float, float)? own = null,
                                     (float, float)? measured = null)
    {
        var plan = LayoutPlan.For(element, panel);
        return LayoutPlan.Sizes(element, panel, plan.Fill, own ?? (-1, 40), measured ?? (-1, -1));
    }

    [Fact]
    public void AStretchedElementFillsAColumnAndKeepsItsHeight()
    {
        var s = Sizes(new UiTextBlock());
        Assert.Equal((-1f, 1f), (s.PreferredWidth, s.FlexibleWidth));
        Assert.Equal((40f, 40f, 0f), (s.PreferredHeight, s.MinHeight, s.FlexibleHeight));
    }

    [Fact]
    public void AMaxWidthStretchesUpToItAndCentresBeyond()
    {
        var text = new UiTextBlock { MaxWidth = 600 };
        Assert.Equal(new LayoutPlan(false, LayoutAlign.Center, false), LayoutPlan.For(text, Orientation.Vertical));
        var s = Sizes(text);
        Assert.Equal((600f, 0f), (s.PreferredWidth, s.FlexibleWidth)); // UGUI gives min(room, 600)
    }

    [Fact]
    public void AMaxCapsASizeTheElementKnowsAndMinWins()
    {
        Assert.Equal(200f, Sizes(new UiButton { Width = 300, MaxWidth = 200 }).PreferredWidth);
        Assert.Equal(500f, Sizes(new UiTextBlock { MinWidth = 500, MaxWidth = 200 }).PreferredWidth);
        var row = Sizes(new UiToggleSwitch { Header = "Row", MaxHeight = 30 });
        Assert.Equal((30f, 30f), (row.PreferredHeight, row.MinHeight));
        Assert.Equal(50f, Sizes(new UiTextBlock { MaxHeight = 50 }, own: (-1, -1), measured: (-1, 120)).PreferredHeight); // a long text, cut
        Assert.Equal(-1f, Sizes(new UiTextBlock { MaxHeight = 50 }, own: (-1, -1)).PreferredHeight); // not measured: its own
    }

    [Fact]
    public void InARowTheAxesSwap()
    {
        var image = new UiImage { MaxHeight = 80 };
        Assert.Equal(LayoutAlign.Center, LayoutPlan.For(image, Orientation.Horizontal).Across);
        var s = Sizes(image, Orientation.Horizontal, own: (64, -1));
        Assert.Equal((80f, 0f), (s.PreferredHeight, s.FlexibleHeight));
        Assert.Equal(48f, Sizes(new UiImage { MaxWidth = 48 }, Orientation.Horizontal, own: (64, -1)).PreferredWidth);
    }

    [Fact]
    public void AWindowKeepsItsSizeInsideTheScreen()
    {
        var none = (0d, 0d);
        var any = (double.PositiveInfinity, double.PositiveInfinity);
        Assert.Equal((760f, 440f), WindowFit.Size(760, 440, none, any, SizeToContent.Manual, 104, -1, (1920, 1080)));
        // At 150 % on 1080p the canvas is 1280×720 reference pixels.
        Assert.Equal((1100f, 656f), WindowFit.Size(1100, 760, none, any, SizeToContent.Manual, 104, -1, (1280, 720)));
        Assert.Equal(1216f, WindowFit.Size(1400, 760, none, any, SizeToContent.Manual, 104, -1, (1280, 720)).Width);
        // Not laid out yet: no screen to keep inside.
        Assert.Equal((1100f, 760f), WindowFit.Size(1100, 760, none, any, SizeToContent.Manual, 104, -1, (0, 0)));
    }

    [Fact]
    public void ADraggedWindowStaysInsideTheScreen()
    {
        // The console (1000 × 480) on 1080p: up to 428 across and 268 along from the middle, 32 kept at the edges.
        Assert.Equal((100f, -50f), WindowFit.Offset((100, -50), (1000, 480), (1920, 1080)));
        Assert.Equal((428f, -268f), WindowFit.Offset((1000, -900), (1000, 480), (1920, 1080)));
        // A window as big as the screen allows stays in the middle; a bigger scale brings a moved one back in.
        Assert.Equal((0f, 0f), WindowFit.Offset((300, 300), (1856, 1016), (1920, 1080)));
        Assert.Equal((76f, 88f), WindowFit.Offset((428, 268), (1000, 480), (1216, 720)));
        // Not laid out yet: as asked.
        Assert.Equal((900f, 900f), WindowFit.Offset((900, 900), (1000, 480), (0, 0)));
    }

    [Fact]
    public void AWindowCanTakeTheHeightOfItsContent()
    {
        var none = (0d, 0d);
        Assert.Equal(404f, WindowFit.Size(1000, 440, none, (double.PositiveInfinity, 760), SizeToContent.Height, 104, 300, (1920, 1080)).Height);
        Assert.Equal(760f, WindowFit.Size(1000, 440, none, (double.PositiveInfinity, 760), SizeToContent.Height, 104, 1000, (1920, 1080)).Height);
        Assert.Equal(440f, WindowFit.Size(1000, 440, none, (double.PositiveInfinity, 760), SizeToContent.Height, 104, -1, (1920, 1080)).Height);
        Assert.Equal((500f, 300f), WindowFit.Size(100, 100, (500, 300), (400, 200), SizeToContent.Manual, 104, -1, (1920, 1080))); // Min wins
    }

    [Fact]
    public void ANarrowerControlShrinksOnlyItsWidestParts()
    {
        // A selector at the game's width: the left arrow, the value and its marks, the right arrow (offsets from the right edge).
        (float Left, float Right)[] parts = [(-367, -332), (-332, -35), (-320, -47), (-35, 0)];
        var (left, right) = SettingRows.Stretchy(parts);
        Assert.Equal((-332f, -35f), (left, right)); // the value and the marks, not the arrows
        float delta = 240 - SettingRows.ContentWidth;
        var mapped = parts.Select(p => (SettingRows.MapX(p.Left, left, right, delta), SettingRows.MapX(p.Right, left, right, delta))).ToArray();
        Assert.Equal((-227f, -192f), mapped[0]); // the left arrow keeps its 35
        Assert.Equal((-192f, -35f), mapped[1]);  // the value takes the 140
        Assert.Equal((-35f, 0f), mapped[3]);     // the right arrow stays
        Assert.True(mapped[2].Item1 > mapped[1].Item1 && mapped[2].Item2 < mapped[1].Item2); // the marks stay inside the value
    }

    [Fact]
    public void TheScalesApplyOnceThePointerIsUp()
    {
        UiScale.ResetForTests();
        try
        {
            Assert.Equal((false, false), UiScale.Update(100, 100, pointerHeld: false));
            Assert.Equal((false, false), UiScale.Update(125, 100, pointerHeld: true)); // still dragging the slider
            Assert.Equal(1f, UiScale.Menus);
            Assert.Equal((true, false), UiScale.Update(125, 100, pointerHeld: false));
            Assert.Equal(1.25f, UiScale.Menus);
            Assert.Equal((false, true), UiScale.Update(125, 130, pointerHeld: false)); // only the HUD
            Assert.Equal(1.3f, UiScale.Hud, 4);
            Assert.Equal((true, false), UiScale.Update(500, 130, pointerHeld: false));
            Assert.Equal(1.5f, UiScale.Menus); // kept within the range
        }
        finally
        {
            UiScale.ResetForTests();
        }
    }

    [Fact]
    public void AGameMenuGrowsNoBiggerThanItsOwnSize()
    {
        UiScale.ResetForTests();
        try
        {
            UiScale.Update(150, 100, pointerHeld: false);
            Assert.Equal((1.5f, 1f), (UiScale.MenuScale(modWindow: true), UiScale.MenuScale(modWindow: false)));
            UiScale.Update(80, 100, pointerHeld: false);
            Assert.Equal((0.8f, 0.8f), (UiScale.MenuScale(modWindow: true), UiScale.MenuScale(modWindow: false))); // smaller: both
        }
        finally
        {
            UiScale.ResetForTests();
        }
    }

    [Fact]
    public void TheApiSpeaksFactorsAndKeepsThePlayersPercents()
    {
        var menus = InterfaceScale.Menus;
        int before = UiScale.MenusEntry.Value;
        try
        {
            menus.Value = 1.25;
            Assert.Equal(125, UiScale.MenusEntry.Value);
            Assert.Equal(1.25, menus.Value);
            menus.Value = 9;
            Assert.Equal(150, UiScale.MenusEntry.Value); // kept within the range
            menus.Value = double.NaN;
            Assert.Equal(100, UiScale.MenusEntry.Value);
            Assert.Equal((0.75, 1.5), (InterfaceScale.Minimum, InterfaceScale.Maximum));
            Assert.Equal(1.0, InterfaceScale.Hud.Value);
        }
        finally
        {
            UiScale.MenusEntry.Value = before;
        }
    }

    [Fact]
    public void TheNewSizesDefaultToTheGamesAndRefreshTheRightParts()
    {
        var button = new UiButton { Header = "Row" };
        Assert.True(double.IsPositiveInfinity(button.MaxWidth));
        Assert.True(double.IsNaN(button.ContentWidth) && double.IsNaN(button.FontSize));
        Assert.Null(button.Padding);
        Assert.Equal(1, button.Opacity);
        var view = Mount(button);
        int styles = view.AppliedCount(DirtyFlags.Style), enabled = view.AppliedCount(DirtyFlags.Enabled);

        button.FontSize = 20;
        button.Opacity = 0.5;
        Assert.Equal(styles + 1, view.AppliedCount(DirtyFlags.Style));
        Assert.Equal(enabled + 1, view.AppliedCount(DirtyFlags.Enabled));
        button.Opacity = 3;
        Assert.Equal(1, button.Opacity); // kept within 0..1

        button.ContentWidth = 240; // fixed when a row is built: the row is built again
        Assert.True(view.Disposed);
        var row = ViewOf(button);
        button.Padding = new Thickness(40, 4); // a row's padding too
        Assert.True(row.Disposed);
    }

    [Fact]
    public void APaddingRestylesATextThatIsNotARow()
    {
        var text = new UiTextBlock { Text = "a" };
        var view = Mount(text);
        int styles = view.AppliedCount(DirtyFlags.Style), layouts = view.AppliedCount(DirtyFlags.Layout);
        text.Padding = new Thickness(8);
        Assert.False(view.Disposed);
        Assert.Equal((styles + 1, layouts + 1), (view.AppliedCount(DirtyFlags.Style), view.AppliedCount(DirtyFlags.Layout)));
    }

    [Fact]
    public void AFontSizeIsInheritedAndReachesEverythingInside()
    {
        var inner = new UiTextBlock { Text = "inner" };
        var own = new UiTextBlock { Text = "own", FontSize = 30 };
        var panel = new UiStackPanel { Children = { inner, own } };
        Assert.True(double.IsNaN(inner.FontSize)); // the game's sizes
        var view = Mount(panel);
        var innerView = view.Children[0];
        int styles = innerView.AppliedCount(DirtyFlags.Style);

        panel.FontSize = 16;
        Assert.Equal((16.0, 30.0), (inner.FontSize, own.FontSize)); // its own wins
        Assert.True(double.IsNaN(inner.GetValue(Control.FontSizeProperty))); // inherited, not its own
        Assert.Equal(styles + 1, innerView.AppliedCount(DirtyFlags.Style));

        panel.Children.Remove(inner);
        Assert.True(double.IsNaN(inner.FontSize)); // out of the panel: the game's again
    }

    [Fact]
    public void AScaleContainerLaysItsChildOutUnscaledAndTakesTheScaledHeight()
    {
        var child = new UiTextBlock { Text = "a" };
        var scaled = new UiLayoutTransformControl { Scale = 0.8, Child = child };
        Assert.Same(scaled, child.Parent);
        Assert.Equal([child], scaled.LogicalChildren);
        Assert.Equal(1000f, UiLayoutTransformControl.ChildWidth(800, 0.8), 3);
        Assert.Equal(80f, UiLayoutTransformControl.OwnHeight(100, 0.8), 3);
        scaled.Scale = 0;
        Assert.Equal(1, scaled.Scale);
        scaled.Scale = 50;
        Assert.Equal(10, scaled.Scale);
        var view = Mount(scaled);
        Assert.Same(child, view.Children.Single().Element);
    }

    [Fact]
    public void TheScalesArePlatformSettings()
    {
        var menus = UiScale.MenusEntry;
        var hud = UiScale.HudEntry;
        Assert.Equal(("UI", "Menus", 100), (menus.Section, menus.Key, menus.DefaultValue));
        Assert.Equal(("UI", "Hud", 100), (hud.Section, hud.Key, hud.DefaultValue));
        Assert.EndsWith("mjolnir.toml", PlatformConfig.Config.FilePath);
        Assert.Contains(PlatformConfig.Config.Entries, e => ReferenceEquals(e, menus));
        Assert.Equal("Interface scale", SettingTexts.NameOf("UI", "Menus", null, SdkTexts.Strings));
        Assert.Equal("HUD scale", SettingTexts.NameOf("UI", "Hud", null, SdkTexts.Strings));
    }
}
