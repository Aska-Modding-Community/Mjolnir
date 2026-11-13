namespace Mjolnir.SDK.UI;

/// <summary>The pure rules of an element over a point in the world (<see cref="Hud.AddAt{T}(T, System.Func{UnityEngine.Vector3}, UnityEngine.Vector2)"/>; no Unity: unit-tested).</summary>
internal static class HudMarkers
{
    /// <summary>How far past the screen's edge a point may lie before its element hides, in screen pixels.</summary>
    public const float Margin = 64;

    /// <summary>
    /// Where the element goes on the HUD's canvas (ScreenSpaceOverlay, its units the screen's divided by its scale
    /// factor), from the centre: the point's screen position moved by <paramref name="offset"/>, in reference pixels.
    /// Hidden when the point is behind the camera or more than <see cref="Margin"/> off the screen.
    /// </summary>
    /// <param name="screen">The point from Camera.WorldToScreenPoint: pixels from the bottom left, and the depth.</param>
    /// <param name="size">The screen's size in pixels.</param>
    /// <param name="scale">The canvas's scale factor.</param>
    /// <param name="offset">How far the element sits from the point, in reference pixels (y up).</param>
    public static (bool Visible, float X, float Y) Place((float X, float Y, float Depth) screen, (float Width, float Height) size, float scale,
                                                          (float X, float Y) offset)
    {
        bool visible = screen.Depth > 0 &&
                       screen.X >= -Margin && screen.X <= size.Width + Margin &&
                       screen.Y >= -Margin && screen.Y <= size.Height + Margin;
        if (scale <= 0) scale = 1;
        return (visible, (screen.X - size.Width / 2) / scale + offset.X, (screen.Y - size.Height / 2) / scale + offset.Y);
    }
}
