using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A palette of a mod's own in the look of the game's build menu: the categories as its diamonds in two rows, the chosen
/// category's name under them, its items as tiles in a strip above them, and the ✕. The game's own build menu is not
/// touched: the palette is a copy of its look, shown in its place while open.
/// </summary>
/// <remarks>
/// The palette takes the mouse itself, also while the player's controls are captured (<see cref="PlayerControls"/>): a
/// diamond chooses its category, a tile is chosen (<see cref="ItemChosen"/>, and the palette closes), the ✕ closes it, the
/// wheel scrolls the tiles; Q and E step through the categories, as in the game. While open, <see cref="MouseInput.IsOverInterface"/>
/// is true over its diamonds and tiles. Which key opens it, and Esc, are the mod's. Use it on the main thread.
/// </remarks>
/// <example><code>
/// var palette = new BuildPalette();
/// var trees = new PaletteCategory("Trees", treeIcon);
/// trees.Items.Add(new PaletteItem("Willow", willowPicture) { Tag = "tree_willow" });
/// palette.Categories.Add(trees);
/// palette.ItemChosen += (_, e) => Take((string)e.Item.Tag!);
/// // in OnUpdate: if (KeyboardInput.WasPressed("B")) palette.Toggle();
/// </code></example>
public sealed class BuildPalette : IDisposable
{
    private readonly Assembly _owner;
    private ModHandlers<EventHandler<PaletteItemEventArgs>>? _itemChosen;
    private ModHandlers<EventHandler>? _closed;
    private BuildPaletteView? _view;
    private bool _disposed;

    /// <summary>A palette of the calling mod, closed and without categories.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public BuildPalette() => _owner = CallingMod.Of(Assembly.GetCallingAssembly());

    /// <summary>The categories, in the order of their diamonds: the first half in the upper row, the rest in the lower.</summary>
    public List<PaletteCategory> Categories { get; } = [];

    /// <summary>The category whose items show; the first one when not set.</summary>
    public PaletteCategory? SelectedCategory { get; set; }

    /// <summary>The item framed as chosen; set by a click on its tile, and by the mod for the tool in hand.</summary>
    public PaletteItem? SelectedItem { get; set; }

    /// <summary>Whether the palette is on screen.</summary>
    public bool IsOpen => _view is { IsOpen: true };

    /// <summary>A tile was clicked; the palette has closed.</summary>
    public event EventHandler<PaletteItemEventArgs>? ItemChosen
    {
        add => ModHandlers<EventHandler<PaletteItemEventArgs>>.Of(ref _itemChosen, "BuildPalette.ItemChosen").Add(value);
        remove => _itemChosen?.Remove(value);
    }

    /// <summary>The palette closed: by its ✕, by a chosen tile, or by <see cref="Close"/>.</summary>
    public event EventHandler? Closed
    {
        add => ModHandlers<EventHandler>.Of(ref _closed, "BuildPalette.Closed").Add(value);
        remove => _closed?.Remove(value);
    }

    /// <summary>Shows the palette with the selected category's items. Nothing happens without a world or off the main thread.</summary>
    public void Open()
    {
        if (_disposed || !Usable("Open")) return;
        if (SelectedCategory == null || !Categories.Contains(SelectedCategory)) SelectedCategory = Categories.Count > 0 ? Categories[0] : null;
        _view ??= new BuildPaletteView(this);
        _view.Open();
    }

    /// <summary>Takes the palette off the screen.</summary>
    public void Close()
    {
        if (_view is not { IsOpen: true } view || !Usable("Close")) return;
        view.Close();
    }

    /// <summary>Opens a closed palette, closes an open one.</summary>
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>Shows the categories and items again after they were changed while the palette is open.</summary>
    public void Refresh()
    {
        if (_view is { IsOpen: true } view && Usable("Refresh")) view.Build();
    }

    /// <summary>Chooses the category <paramref name="step"/> places on (Q is −1, E is +1), going round at the ends.</summary>
    public void SelectCategory(int step)
    {
        if (Categories.Count == 0) return;
        int index = SelectedCategory is { } current ? Categories.IndexOf(current) : -1;
        SelectedCategory = Categories[PaletteRules.Step(Math.Max(index, 0), index < 0 ? 0 : step, Categories.Count)];
        Refresh();
    }

    /// <summary>Closes the palette and destroys its objects.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Dispatcher.IsMainThread) Release();
        else Dispatcher.MainThread.Post(Release);
    }

    internal Assembly Owner => _owner;

    internal bool IsOwnerDisabled => ModCall.IsDisabled(_owner);

    internal void RaiseChosen(PaletteItem item)
    {
        SelectedItem = item;
        var args = new PaletteItemEventArgs(item);
        _itemChosen?.Raise(handler => handler(this, args));
    }

    internal void RaiseClosed() => _closed?.Raise(handler => handler(this, EventArgs.Empty));

    private void Release()
    {
        _view?.Destroy();
        _view = null;
    }

    private static bool Usable(string what) => GameLoop.CanUseUnity($"BuildPalette.{what}", GameUi.Source);
}

/// <summary>A category of a <see cref="BuildPalette"/>: a diamond with its icon, and the items it shows.</summary>
/// <param name="displayName">The name shown under the diamonds while the category is chosen.</param>
/// <param name="icon">The diamond's icon; the game's are 40 × 40, light figures on clear.</param>
public sealed class PaletteCategory(string displayName, Bitmap? icon = null)
{
    /// <summary>The name shown under the diamonds while the category is chosen.</summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>The diamond's icon.</summary>
    public Bitmap? Icon { get; set; } = icon;

    /// <summary>The items, as tiles from left to right.</summary>
    public List<PaletteItem> Items { get; } = [];

    /// <summary>Anything of the mod's own that this category stands for.</summary>
    public object? Tag { get; set; }

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}

/// <summary>An item of a <see cref="PaletteCategory"/>: a tile with a picture and a name.</summary>
/// <param name="displayName">The name on the tile.</param>
/// <param name="picture">The tile's picture, about 4 : 3 (74 × 55 on the tile).</param>
public sealed class PaletteItem(string displayName, Bitmap? picture = null)
{
    /// <summary>The name on the tile.</summary>
    public string DisplayName { get; set; } = displayName;

    /// <summary>The tile's picture.</summary>
    public Bitmap? Picture { get; set; } = picture;

    /// <summary>Anything of the mod's own that this item stands for.</summary>
    public object? Tag { get; set; }

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}

/// <summary>The item a palette's tile stands for.</summary>
public sealed class PaletteItemEventArgs(PaletteItem item) : EventArgs
{
    /// <summary>The item chosen.</summary>
    public PaletteItem Item { get; } = item;
}

/// <summary>The palette's arithmetic, without the game.</summary>
internal static class PaletteRules
{
    /// <summary>How many diamonds go into the upper row: the larger half, as the game's 9 over 8.</summary>
    public static int UpperRow(int count) => (count + 1) / 2;

    /// <summary>
    /// How far the lower row's diamonds are moved right of the upper's, both centred, so that they stand between them as
    /// the game's do: half a step when the rows are as long (a longer upper row staggers them by itself), else none.
    /// </summary>
    public static float LowerRowShift(int upper, int lower, float step) => upper == lower && lower > 0 ? step / 2 : 0;

    /// <summary>
    /// Where the strip's left edge goes in the view (0 its left edge) when it fits: its middle at <paramref name="middle"/>
    /// (the diamonds' middle, in the view's units), kept inside the view; a strip wider than the view starts at 0.
    /// </summary>
    public static float FittedLeft(float content, float view, float middle) =>
        content >= view ? 0 : Math.Clamp(middle - content / 2, 0, view - content);

    /// <summary>The index <paramref name="step"/> places from <paramref name="index"/>, going round.</summary>
    public static int Step(int index, int step, int count) => count <= 0 ? 0 : ((index + step) % count + count) % count;

    /// <summary>
    /// How far the tiles are scrolled after the wheel turned <paramref name="notches"/> (away from the player is left), one
    /// tile (<paramref name="tile"/>, spacing included) a notch, kept between none and the content's width beyond the view.
    /// </summary>
    public static float Scroll(float offset, float notches, float tile, float content, float view) =>
        Math.Clamp(offset - notches * tile, 0, Math.Max(0, content - view));
}
