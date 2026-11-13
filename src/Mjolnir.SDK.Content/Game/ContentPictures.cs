using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mjolnir.SDK.Content;

/// <summary>The pictures of the mods' content (items, buildings, effects), from their files.</summary>
internal static class ContentPictures
{
    // A picture file, read once; kept through scene loads (the game unloads unused assets on each).
    private static readonly Dictionary<string, Sprite?> s_pictures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A picture of a mod's, from its file: once, and for the whole game.</summary>
    public static Sprite? Picture(string path)
    {
        if (s_pictures.TryGetValue(path, out var known)) return known;
        Sprite? sprite = null;
        try
        {
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            else SdkLog.Warn(ContentLog.Source, $"Items: {path} is not a PNG or JPG image");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SdkLog.Warn(ContentLog.Source, $"Items: {path} could not be read ({e.Message})");
        }
        return s_pictures[path] = sprite;
    }
}
