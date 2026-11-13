using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The controls of the sixth iteration on the fake backend: UiExpander (toggle, events, content as its child, a copy
/// shown again on toggle), UiScrollViewer (content as its child), UiGrid (column definitions, children re-laying out its
/// cells, new columns rebuilding it) and UiProgressRing (the shared ProgressBase: limits, percentage, text, the text's
/// colour; Width as the ring's diameter).
/// </summary>
[Collection("MainThread")]
public sealed class NewControlsTests : ViewTestBase
{
    [Fact]
    public void AnExpanderOpensAndClosesWithItsEvents()
    {
        var seen = new List<string>();
        var content = new UiTextBlock { Text = "inside" };
        var expander = new UiExpander { Header = "Advanced", Content = content };
        expander.Expanded += (_, _) => seen.Add("Expanded");
        expander.Collapsed += (_, _) => seen.Add("Collapsed");
        Assert.False(expander.IsExpanded);
        Assert.Same(expander, content.Parent);
        Assert.Equal([content], expander.LogicalChildren);

        expander.Toggle();
        expander.Toggle();
        expander.IsExpanded = false;
        Assert.Equal(["Expanded", "Collapsed"], seen);
    }

    [Fact]
    public void AnExpandersCopyFollowsItsStateAndContent()
    {
        var expander = new UiExpander { Header = "Advanced", Content = new UiTextBlock { Text = "a" } };
        var view = Mount(expander);
        Assert.Single(view.Children);
        int values = view.AppliedCount(DirtyFlags.Value);

        expander.Toggle();
        Assert.Equal(values + 1, view.AppliedCount(DirtyFlags.Value));
        var other = new UiTextBlock { Text = "b" };
        expander.Content = other;
        Assert.Same(other, view.Children.Single().Element);
    }

    [Fact]
    public void AScrollViewerHoldsItsContent()
    {
        var content = new UiStackPanel { Children = { new UiTextBlock { Text = "a" }, new UiTextBlock { Text = "b" } } };
        var viewer = new UiScrollViewer { Height = 200, Content = content };
        var view = Mount(viewer);
        Assert.Same(viewer, content.Parent);
        Assert.Same(content, view.Children.Single().Element);
    }

    [Theory]
    [InlineData("200,*,2*,Auto", "200 Pixel|1 Star|2 Star|0 Auto")]
    [InlineData(" * , 3* ", "1 Star|3 Star")]
    [InlineData("", "1 Star")]
    [InlineData("wide,-5,0*", "1 Star|1 Star|1 Star")]
    public void ColumnDefinitionsAreReadInTheirSyntax(string text, string columns) =>
        Assert.Equal(columns, string.Join("|", GridLength.ParseList(text).Select(c => $"{c.Value} {c.Unit}")));

    [Fact]
    public void AGridsChildrenReLayOutItsCellsAndNewColumnsRebuildIt()
    {
        var grid = new UiGrid { ColumnDefinitions = "200,*", Children = { new UiTextBlock { Text = "a" }, new UiTextBlock { Text = "b" } } };
        var view = Mount(grid);
        Assert.Equal(2, view.Children.Count);
        int layouts = view.AppliedCount(DirtyFlags.Layout);

        grid.Children.Add(new UiTextBlock { Text = "c" });
        Assert.Equal(3, view.Children.Count);
        Assert.True(view.AppliedCount(DirtyFlags.Layout) > layouts); // cells sized again

        grid.ColumnDefinitions = "*,*,*";
        Assert.True(view.Disposed);
        Assert.Equal(3, ViewOf(grid).Children.Count);
    }

    [Fact]
    public void ARingSharesTheBarsRangeAndText()
    {
        var ring = new UiProgressRing { Maximum = 200, Value = 150, ShowProgressText = true, ProgressTextFormat = "{0:0}/{3:0}" };
        Assert.Equal(75, ring.Percentage);
        Assert.Equal("150/200", ring.ProgressText);
        ring.Maximum = 100;
        Assert.Equal(100, ring.Value); // kept within the limits
        var seen = new List<double>();
        ring.OnValueChanged(v => seen.Add(v)).Value = 40;
        Assert.Equal([40.0], seen);
        Assert.IsAssignableFrom<ProgressBase>(new UiProgressBar());
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(64, 0.64)]
    [InlineData(200, 2)]
    [InlineData(0, 1)]
    public void ARingsWidthIsItsDiameter(double width, double scale) =>
        Assert.Equal(scale, new UiProgressRing { Width = width }.Scale, 6);

    [Fact]
    public void ARingsWidthLaysItOutAgain()
    {
        var ring = new UiProgressRing();
        var view = Mount(ring);
        int layouts = view.AppliedCount(DirtyFlags.Layout);
        ring.Width = 60;
        Assert.Equal(layouts + 1, view.AppliedCount(DirtyFlags.Layout));
    }

    [Fact]
    public void TheTextsColourIsTheGamesUntilSetAndIsAStyleChange()
    {
        var bar = new UiProgressBar();
        var ring = new UiProgressRing();
        Assert.Null(bar.ProgressTextForeground);
        var barView = Mount(bar);
        var ringView = Mount(ring);
        int barStyles = barView.AppliedCount(DirtyFlags.Style), ringStyles = ringView.AppliedCount(DirtyFlags.Style);

        bar.ProgressTextForeground = default(UnityEngine.Color); // a Color made otherwise needs the IL2CPP runtime
        ring.ProgressTextForeground = default(UnityEngine.Color);
        Assert.Equal(barStyles + 1, barView.AppliedCount(DirtyFlags.Style));
        Assert.Equal(ringStyles + 1, ringView.AppliedCount(DirtyFlags.Style));
    }
}
