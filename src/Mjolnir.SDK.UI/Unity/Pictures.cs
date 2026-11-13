using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The sprite a <see cref="Bitmap"/> shows (<see cref="UiImage"/>, a row's <see cref="HeaderedControl.Icon"/>). A file is
/// read once per path and its texture kept for the rest of the game; a missing or broken file is logged once and shows
/// nothing. A texture's sprite is made once per bitmap. What the SDK makes is kept through the game's scene loads,
/// which unload unused assets (<see cref="HideFlags.DontUnloadUnusedAsset"/>); a sprite or texture destroyed all the same
/// is made again.
/// </summary>
internal static class Pictures
{
    private static readonly Dictionary<string, Sprite?> s_files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<Bitmap, Sprite> s_textures = new();

    public static Sprite? SpriteOf(Bitmap? source) =>
        source == null ? null : source.Sprite ?? (source.Path != null ? FromFile(source.Path) : FromTexture(source));

    private static Sprite? FromTexture(Bitmap source)
    {
        var texture = source.Texture;
        if (texture == null) return null;
        if (s_textures.TryGetValue(source, out var cached) && cached != null) return cached;
        Keep(texture);
        var sprite = SpriteOf(texture);
        if (sprite != null) s_textures.AddOrUpdate(source, sprite);
        return sprite;
    }

    private static Sprite? SpriteOf(Texture2D texture)
    {
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        if (sprite != null) Keep(sprite);
        return sprite;
    }

    private static Sprite? FromFile(string path)
    {
        // A cached sprite the game destroyed (Unity's null) is read again; a file that failed stays null.
        if (s_files.TryGetValue(path, out var cached) && (cached is null || cached != null)) return cached;
        Texture2D? texture = null;
        try
        {
            var loaded = new Texture2D(2, 2);
            if (ImageConversion.LoadImage(loaded, File.ReadAllBytes(path))) texture = loaded;
            else SdkLog.Warn(GameUi.Source, $"Bitmap: {path} is not a PNG or JPG image");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SdkLog.Warn(GameUi.Source, $"Bitmap: {path} could not be read ({e.Message})");
        }
        Sprite? sprite = null;
        if (texture != null)
        {
            texture.filterMode = FilterMode.Bilinear;
            Keep(texture);
            sprite = SpriteOf(texture);
        }
        s_files[path] = sprite;
        return sprite;
    }

    private static void Keep(UnityEngine.Object asset) => asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
}
