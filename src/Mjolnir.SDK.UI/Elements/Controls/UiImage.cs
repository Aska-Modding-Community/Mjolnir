using System;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A picture for <see cref="UiImage.Source"/>: a texture or sprite of yours (converted implicitly), or an image file read
/// when it is first shown (<c>new Bitmap(Path.Combine(folder, "icon.png"))</c>; textures cannot be made before the game
/// is up). The game unloads unused assets when it loads a scene: a texture or sprite made in code before that (in
/// <see cref="Mod.OnGameReady"/>) or kept from one world to the next needs <c>hideFlags = HideFlags.DontUnloadUnusedAsset</c>,
/// else it shows nothing later. The SDK keeps the pictures it reads from files.
/// </summary>
public sealed class Bitmap
{
    /// <summary>A PNG or JPG file, read on first show. A missing or broken file shows nothing and is logged.</summary>
    public Bitmap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    private Bitmap(Texture2D texture) => Texture = texture;

    private Bitmap(Sprite sprite) => Sprite = sprite;

    // "is null", not "== null": Unity's == asks the engine, and a texture can be converted before it exists.
    public static implicit operator Bitmap?(Texture2D? texture) => texture is null ? null : new Bitmap(texture);

    public static implicit operator Bitmap?(Sprite? sprite) => sprite is null ? null : new Bitmap(sprite);

    internal string? Path { get; }
    internal Texture2D? Texture { get; }
    internal Sprite? Sprite { get; }

    public override string ToString() => Path ?? (Sprite is not null ? "sprite" : "texture");
}

/// <summary>A picture. Without <see cref="Control.Width"/>/<see cref="Control.Height"/> it takes the picture's size.</summary>
public sealed class UiImage : Control
{
    public static readonly MjolnirProperty<Bitmap?> SourceProperty =
        MjolnirProperty.Register<UiImage, Bitmap?>(nameof(Source), null, DirtyFlags.Image | DirtyFlags.Layout);

    public static readonly MjolnirProperty<Stretch> StretchProperty =
        MjolnirProperty.Register<UiImage, Stretch>(nameof(Stretch), Stretch.Uniform, DirtyFlags.Image);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>How the picture fills its box: keeping its proportions (the default) or not.</summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }
}

/// <summary>
/// Your own Unity UI: <paramref name="build"/> gets an empty RectTransform in the layout for every live copy (add
/// UnityEngine.UI components, your own MonoBehaviours…). Give it a size with <see cref="Control.Height"/> or your own
/// LayoutElement. If <paramref name="build"/> throws, your mod is disabled.
/// </summary>
public sealed class UiUnityHost(Action<RectTransform> build) : Control
{
    internal Action<RectTransform> Build { get; } = build ?? throw new ArgumentNullException(nameof(build));
}
