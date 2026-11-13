using System;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The size a <see cref="UiWindow"/> gets on screen (no Unity: unit-tested): its Width and Height (or, with
/// <see cref="SizeToContent.Height"/>, its title and content), within its Min/Max (Min wins), and always
/// inside the screen: the canvas's area in reference pixels (which depends on the screen and on <see cref="UiScale"/>),
/// less a margin on each side. Content taller than the window scrolls.
/// </summary>
internal static class WindowFit
{
    /// <summary>Kept free between a window and the screen's edge, on each side.</summary>
    public const float ScreenMargin = 32;

    /// <param name="width">The window's Width.</param>
    /// <param name="height">The window's Height (with <see cref="SizeToContent.Manual"/>).</param>
    /// <param name="min">MinWidth and MinHeight.</param>
    /// <param name="max">MaxWidth and MaxHeight.</param>
    /// <param name="sizeToContent">Whether the height follows the content.</param>
    /// <param name="chrome">The window's height besides its content: the title and the scroll view's frame.</param>
    /// <param name="content">The content's height (-1: not known yet).</param>
    /// <param name="canvas">The canvas's size in reference pixels (0: not known yet).</param>
    public static (float Width, float Height) Size(double width, double height, (double Width, double Height) min,
                                                   (double Width, double Height) max, SizeToContent sizeToContent, float chrome,
                                                   float content, (float Width, float Height) canvas)
    {
        double w = width;
        double h = sizeToContent == SizeToContent.Height && content >= 0 ? chrome + content : height;
        w = Limit(w, min.Width, max.Width);
        h = Limit(h, min.Height, max.Height);
        if (canvas.Width > 0) w = Math.Min(w, canvas.Width - 2 * ScreenMargin);
        if (canvas.Height > 0) h = Math.Min(h, canvas.Height - 2 * ScreenMargin);
        return ((float)Math.Max(1, w), (float)Math.Max(1, h));
    }

    /// <summary>
    /// Where a window the player dragged stands: its offset from the screen's middle, kept so that the whole window stays
    /// inside the canvas less <see cref="ScreenMargin"/> (a bigger screen scale or window brings it back in).
    /// </summary>
    /// <param name="offset">The offset the drag asks for, in reference pixels.</param>
    /// <param name="size">The window's size.</param>
    /// <param name="canvas">The canvas's size (0: not known yet: the offset as asked).</param>
    public static (float X, float Y) Offset((float X, float Y) offset, (float Width, float Height) size, (float Width, float Height) canvas)
    {
        if (canvas.Width <= 0 || canvas.Height <= 0) return offset;
        float x = Math.Max(0, (canvas.Width - size.Width) / 2 - ScreenMargin);
        float y = Math.Max(0, (canvas.Height - size.Height) / 2 - ScreenMargin);
        return (Math.Clamp(offset.X, -x, x), Math.Clamp(offset.Y, -y, y));
    }

    // The maximum first, then the minimum, which wins.
    private static double Limit(double value, double min, double max)
    {
        if (double.IsNaN(value)) value = 0;
        if (!double.IsNaN(max)) value = Math.Min(value, max);
        if (!double.IsNaN(min)) value = Math.Max(value, min);
        return value;
    }
}
