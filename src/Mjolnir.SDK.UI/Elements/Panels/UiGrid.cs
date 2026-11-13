using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Children in rows of columns: <see cref="ColumnDefinitions"/> sets the columns, and the
/// children fill the cells left to right, row after row, each cell as wide as its column. For forms and tables:
/// <c>new UiGrid { ColumnDefinitions = "200,*", Children = { label, value, label2, value2 } }</c>.
/// </summary>
public sealed class UiGrid : Panel
{
    /// <summary>"*,*" by default: two columns sharing the width.</summary>
    public static readonly MjolnirProperty<string> ColumnDefinitionsProperty =
        MjolnirProperty.Register<UiGrid, string>(nameof(ColumnDefinitions), "*,*", DirtyFlags.Rebuild); // other columns: cells built again

    public static readonly MjolnirProperty<double> RowSpacingProperty =
        MjolnirProperty.Register<UiGrid, double>(nameof(RowSpacing), 10, DirtyFlags.Layout);

    public static readonly MjolnirProperty<double> ColumnSpacingProperty =
        MjolnirProperty.Register<UiGrid, double>(nameof(ColumnSpacing), 10, DirtyFlags.Layout);

    /// <summary>
    /// The columns, comma-separated: <c>200</c> pixels, <c>*</c> a share of the width left (<c>2*</c> two
    /// shares), <c>Auto</c> each cell its content's width (Auto cells of different rows do not line up). A part that is
    /// none of these counts as <c>*</c>.
    /// </summary>
    public string ColumnDefinitions
    {
        get => GetValue(ColumnDefinitionsProperty);
        set => SetValue(ColumnDefinitionsProperty, value);
    }

    /// <summary>Space between rows.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>Space between columns.</summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>The columns of <see cref="ColumnDefinitions"/>, at least one.</summary>
    internal IReadOnlyList<GridLength> Columns => GridLength.ParseList(GetValue(ColumnDefinitionsProperty));

    // A change of children re-lays out the cells too: the view drops the ones no child is in.
    internal override void Dirty(DirtyFlags dirty) => base.Dirty((dirty & DirtyFlags.Children) != 0 ? dirty | DirtyFlags.Layout : dirty);
}

/// <summary>How wide a grid column is: pixels, a share of the width left (star), or its content's width (auto).</summary>
internal readonly record struct GridLength(double Value, GridUnit Unit)
{
    public static readonly GridLength Star = new(1, GridUnit.Star);

    /// <summary>Column definitions: <c>200</c>, <c>*</c>, <c>2*</c>, <c>Auto</c>, comma-separated; never empty.</summary>
    public static IReadOnlyList<GridLength> ParseList(string? text)
    {
        var columns = new List<GridLength>();
        foreach (string part in (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            columns.Add(Parse(part));
        if (columns.Count == 0) columns.Add(Star);
        return columns;
    }

    public static GridLength Parse(string part)
    {
        if (part.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return new GridLength(0, GridUnit.Auto);
        if (part.EndsWith('*'))
        {
            string share = part[..^1].Trim();
            if (share.Length == 0) return Star;
            return double.TryParse(share, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n > 0 ? new GridLength(n, GridUnit.Star) : Star;
        }
        return double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double pixels) && pixels >= 0
            ? new GridLength(pixels, GridUnit.Pixel)
            : Star;
    }
}

internal enum GridUnit
{
    Pixel,
    Star,
    Auto,
}
