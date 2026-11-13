using System;
using UnityEngine;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// White shapes made in code, for tab icons: the game tints them (grey, dark on the amber of a selected tab). A mod would
/// rather ship PNG files next to its .dll and use <c>new Bitmap(path)</c>. Textures need the running game: these are made
/// on first use.
/// </summary>
internal static class Icons
{
    private const int Size = 64;
    private static Texture2D? s_diamond, s_circle, s_cross, s_grid;

    public static Texture2D Diamond => s_diamond ??= Shape((x, y) => Size * 0.42f - (Math.Abs(x) + Math.Abs(y)));

    public static Texture2D Circle => s_circle ??= Shape((x, y) => Size * 0.38f - MathF.Sqrt(x * x + y * y));

    public static Texture2D Cross => s_cross ??= Shape((x, y) =>
        Math.Min(Size * 0.4f - Math.Max(Math.Abs(x), Math.Abs(y)), Size * 0.11f - Math.Min(Math.Abs(x), Math.Abs(y))));

    // Four squares in a square, a gap between them.
    public static Texture2D Grid => s_grid ??= Shape((x, y) =>
    {
        float ax = Math.Abs(x), ay = Math.Abs(y), gap = Size * 0.06f, edge = Size * 0.4f;
        return Math.Min(Math.Min(ax - gap, edge - ax), Math.Min(ay - gap, edge - ay));
    });

    // inside(x, y): how far a pixel's centre lies inside the shape, in pixels from the middle; edges get a pixel of fade.
    private static Texture2D Shape(Func<float, float, float> inside)
    {
        // Kept: the game unloads unused assets on each scene load, and the tab icons are made before the main menu loads.
        var texture = new Texture2D(Size, Size) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                float alpha = Math.Clamp(inside(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f) + 0.5f, 0f, 1f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return texture;
    }
}
