using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A <see cref="UiKeyHint"/>: a row of the key, in the game's key pictures (the glyph sprite asset its HUD prompts use),
/// and the action's name, in the HUD prompts' fonts and colours (Theme.KeyHintKey/KeyHintName).
/// </summary>
internal sealed class KeyHintView : UnityView
{
    private readonly UiKeyHint _hint;
    private readonly TextMeshProUGUI _key;
    private readonly TextMeshProUGUI _name;
    private readonly TMP_SpriteAsset? _glyphs;
    private readonly float _keySize, _nameSize;
    private string? _shownKey, _shownName;

    public KeyHintView(UiKeyHint hint, RectTransform parent) : base(hint)
    {
        _hint = hint;
        Root = ViewFactory.NewRect(parent, DefaultName);
        ViewFactory.Row(Root, 8, TextAnchor.MiddleLeft);
        _key = (Theme.KeyHintKey ?? new TextLook(Theme.TextFont, 26, Color.white)).NewText(Root, "Key");
        _name = (Theme.KeyHintName ?? new TextLook(Theme.TextFont, 18, new Color(0.776f, 0.902f, 0.855f))).NewText(Root, "Name");
        _keySize = _key.fontSize;
        _nameSize = _name.fontSize;
        foreach (var text in new[] { _key, _name })
        {
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }
        try
        {
            _glyphs = SSSGame.InputManager.GetCurrentGlyphsSpriteAsset();
        }
        catch (System.Exception e)
        {
            SdkLog.Warn(GameUi.Source, $"Key pictures not found, keys are written out: {e.Message}");
        }
        if (_glyphs != null) _key.spriteAsset = _glyphs;
    }

    protected override string DefaultName => "KeyHint";

    protected override void ApplyFont(double size)
    {
        _name.fontSize = FontOf(size, _nameSize, _nameSize);
        _key.fontSize = FontOf(size, _keySize, _nameSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Value) != 0)
        {
            string key = KeyGlyphs.Text(_hint.Gesture, name => _glyphs != null && _glyphs.GetSpriteIndexFromName(name) >= 0);
            if (key != _shownKey)
            {
                _key.text = _shownKey = key;
                _key.gameObject.SetActive(key.Length > 0);
            }
        }
        if ((dirty & DirtyFlags.Text) != 0 && _shownName != _hint.Text)
        {
            _name.text = _shownName = _hint.Text;
            _name.gameObject.SetActive(_hint.Text.Length > 0);
        }
    }
}
