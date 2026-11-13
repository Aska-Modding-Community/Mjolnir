using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mjolnir.SDK.UI;

/// <summary>A game text's look, to give our own texts: font, size, colour.</summary>
internal sealed record TextLook(TMP_FontAsset? Font, float Size, Color Color)
{
    public static TextLook? Of(TMP_Text? text) => text == null ? null : new TextLook(text.font, text.fontSize, text.color);

    public TextMeshProUGUI NewText(Transform parent, string name)
    {
        var text = ViewFactory.NewText(parent, name, "", Font, Size);
        text.color = Color;
        return text;
    }
}

/// <summary>
/// A game image's look, to give our own objects: the sprite with its slicing (type, pixelsPerUnitMultiplier, fill), the
/// colour. Without the multiplier a sliced border is drawn several times too thick.
/// </summary>
internal sealed record ImageLook(Sprite? Sprite, Image.Type Type, float Ppu, bool FillCenter, Color Color)
{
    public static ImageLook? Of(Image? image) =>
        image == null ? null : new ImageLook(image.sprite, image.type, image.pixelsPerUnitMultiplier, image.fillCenter, image.color);

    /// <summary>The look on the object's image (one is added if it has none), which takes no pointer.</summary>
    public Image ApplyTo(GameObject target)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = Sprite;
        image.type = Type;
        image.pixelsPerUnitMultiplier = Ppu;
        image.fillCenter = FillCenter;
        image.color = Color;
        image.raycastTarget = false;
        return image;
    }
}
