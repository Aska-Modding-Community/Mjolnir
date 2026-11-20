using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The build palette's model and arithmetic, without the game: rows, stepping round the categories, scrolling the tiles.</summary>
public sealed class BuildPaletteTests
{
    [Fact]
    public void TheUpperRowTakesTheLargerHalf()
    {
        Assert.Equal(0, PaletteRules.UpperRow(0));
        Assert.Equal(1, PaletteRules.UpperRow(1));
        Assert.Equal(5, PaletteRules.UpperRow(10));
        Assert.Equal(9, PaletteRules.UpperRow(17)); // the game's 9 over 8
    }

    [Fact]
    public void RowsAsLongAreStaggeredByHalfAStep()
    {
        Assert.Equal(25, PaletteRules.LowerRowShift(5, 5, 50));
        Assert.Equal(0, PaletteRules.LowerRowShift(9, 8, 50)); // centred, a longer upper row staggers them itself
        Assert.Equal(0, PaletteRules.LowerRowShift(1, 0, 50));
    }

    [Fact]
    public void TilesThatFitStandOverTheDiamonds()
    {
        Assert.Equal(200, PaletteRules.FittedLeft(200, 600, 300));  // centred on the diamonds' middle
        Assert.Equal(0, PaletteRules.FittedLeft(200, 600, 50));     // kept inside the view
        Assert.Equal(400, PaletteRules.FittedLeft(200, 600, 590));
        Assert.Equal(0, PaletteRules.FittedLeft(700, 600, 300));    // wider than the view: scrolled from its left
    }

    [Fact]
    public void QAndEGoRoundTheCategories()
    {
        var palette = new BuildPalette();
        palette.SelectCategory(1);                  // none: nothing to choose
        Assert.Null(palette.SelectedCategory);
        var a = new PaletteCategory("A");
        var b = new PaletteCategory("B");
        var c = new PaletteCategory("C");
        palette.Categories.AddRange([a, b, c]);
        palette.SelectCategory(1);                  // none chosen yet: the first
        Assert.Same(a, palette.SelectedCategory);
        palette.SelectCategory(-1);
        Assert.Same(c, palette.SelectedCategory);
        palette.SelectCategory(1);
        Assert.Same(a, palette.SelectedCategory);
        palette.SelectCategory(2);
        Assert.Same(c, palette.SelectedCategory);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void TheWheelScrollsATileANotchWithinTheTiles()
    {
        // Ten tiles of 90 (spacing in) in a view 664 wide: 236 beyond it.
        Assert.Equal(90, PaletteRules.Scroll(0, -1, 90, 900, 664));
        Assert.Equal(236, PaletteRules.Scroll(180, -1, 90, 900, 664));
        Assert.Equal(0, PaletteRules.Scroll(60, 1, 90, 900, 664));
        Assert.Equal(0, PaletteRules.Scroll(0, -3, 90, 500, 664)); // all in view: no scrolling
    }
}
