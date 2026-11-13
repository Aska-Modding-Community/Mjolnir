using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using SandSailorStudio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's health bar (<see cref="Theme.ProgressBar"/>): its frame, dark background, filled bar and centred text,
/// driven by the element alone (the game's ProgressBar behaviour is not in the copy). Without the template, a plain bar.
/// </summary>
internal sealed class ProgressBarView : UnityView
{
    private const float RowWidth = 200; // in a row, where nothing stretches it

    private readonly ProgressParts _parts;
    private readonly float _height = 29;

    public ProgressBarView(UiProgressBar bar, RectTransform parent) : base(bar)
    {
        Image? fillImage;
        TMP_Text? text;
        if (Theme.ProgressBar is { } template)
        {
            var copy = SettingRows.Clone(template, parent, DefaultName);
            Root = RectOf(copy);
            fillImage = copy.transform.Find("Fill")?.GetComponent<Image>();
            text = copy.transform.Find("Text")?.GetComponent<TMP_Text>();
            _height = template.GetComponent<RectTransform>().sizeDelta.y;
        }
        else
        {
            Root = ViewFactory.NewRect(parent, DefaultName);
            Root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.4f);
            var fill = ViewFactory.NewRect(Root, "Fill");
            ViewFactory.Stretch(fill, 2);
            fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.color = new Color(0.66f, 0, 0, 1);
            text = ViewFactory.NewText(Root, "Text", "", Theme.TextFont, 18);
            ViewFactory.Stretch(text.rectTransform);
        }
        _parts = new ProgressParts(bar, Root, fillImage, text);
    }

    protected override string DefaultName => "ProgressBar";

    protected override void ApplyFont(double size) => _parts.ApplyFont(size);

    protected override (float Width, float Height) LayoutDefaults => (ParentAxis == Orientation.Horizontal ? RowWidth : -1, _height);

    protected override void ApplyOwn(DirtyFlags dirty) => _parts.Apply(dirty);
}

/// <summary>
/// A ring from the game's HUD (its food ring): the holder and background, the radial fill, the icon in the middle and
/// the text under it. The game's ring is 100 × 100, the view that plus the text's line; a width scales the whole ring
/// (localScale on it, the text included), and the view takes the scaled size in the layout.
/// </summary>
internal sealed class ProgressRingView : UnityView
{
    private const float Size = 100;
    private const float TextLine = 28;

    private readonly UiProgressRing _ring;
    private readonly RectTransform _frame;
    private readonly ProgressParts _parts;
    private readonly Image? _icon;
    private readonly GameObject? _iconFrame;
    private Bitmap? _shownIcon;
    private bool _iconShown = true;

    public ProgressRingView(UiProgressRing ring, RectTransform parent) : base(ring)
    {
        _ring = ring;
        Root = ViewFactory.NewRect(parent, DefaultName);
        RectTransform frame;
        Image? fillImage;
        TMP_Text? text;
        if (Theme.ProgressRing is { } template)
        {
            frame = RectOf(SettingRows.Clone(template, Root, "Ring"));
            fillImage = frame.Find("Fill")?.GetComponent<Image>();
            _icon = frame.Find("Icon")?.GetComponent<Image>();
            _iconFrame = frame.Find("IconFrame")?.gameObject;
            text = frame.Find("Text")?.GetComponent<TMP_Text>();
        }
        else
        {
            frame = ViewFactory.NewRect(Root, "Ring");
            frame.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.4f);
            var fill = ViewFactory.NewRect(frame, "Fill");
            ViewFactory.Stretch(fill, 4);
            fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Radial360;
            fillImage.fillOrigin = 2;
            fillImage.color = new Color(1, 0.42f, 0, 1);
            text = ViewFactory.NewText(frame, "Text", "", Theme.TextFont, 16);
            text.rectTransform.anchorMin = new Vector2(0, 0);
            text.rectTransform.anchorMax = new Vector2(1, 0);
            text.rectTransform.sizeDelta = new Vector2(0, TextLine);
            text.rectTransform.anchoredPosition = new Vector2(0, -TextLine / 2);
        }
        // The ring at the top of the view, its own size (scaled from its top); the text hangs under it, inside the view.
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 1);
        frame.pivot = new Vector2(0.5f, 1);
        frame.sizeDelta = new Vector2(Size, Size);
        frame.anchoredPosition = Vector2.zero;
        _frame = frame;
        _parts = new ProgressParts(ring, Root, fillImage, text);
    }

    protected override string DefaultName => "ProgressRing";

    protected override void ApplyFont(double size) => _parts.ApplyFont(size);

    private float Scale => (float)_ring.Scale;

    protected override (float Width, float Height) LayoutDefaults => (Size * Scale, (Size + TextLine) * Scale);

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        _frame.localScale = new Vector3(Scale, Scale, 1);
    }

    protected override bool FixedSize => true;

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        _parts.Apply(dirty);
        if ((dirty & DirtyFlags.Image) != 0 && _icon != null && (!Equals(_shownIcon, _ring.Icon) || _iconShown != (_ring.Icon != null)))
        {
            _shownIcon = _ring.Icon;
            _iconShown = _ring.Icon != null;
            if (_ring.Icon != null) _icon.sprite = Pictures.SpriteOf(_ring.Icon);
            _icon.gameObject.SetActive(_iconShown);
            _iconFrame?.SetActive(_iconShown);
        }
    }
}

/// <summary>
/// What a bar and a ring show alike (<see cref="ProgressBase"/>): the fill, the text and their colours, the game's own
/// kept for null. Every part is clipped like the rest of the content: the game's HUD has nothing to clip, and its bar's
/// fill is not maskable, so in a window's scroll view it showed below the window (seen in game, 2026-10-22).
/// </summary>
internal sealed class ProgressParts
{
    private readonly ProgressBase _model;
    private readonly Image? _fill;
    private readonly TMP_Text? _text;
    private readonly float _gameTextSize;
    private readonly Color _gameColor;
    private readonly Color _gameTextColor;
    private float? _shownFill;
    private string? _shownText;
    private Color? _shownColor;
    private Color? _shownTextColor;

    public ProgressParts(ProgressBase model, RectTransform root, Image? fill, TMP_Text? text)
    {
        _model = model;
        _fill = fill;
        _text = text;
        foreach (var graphic in root.GetComponentsInChildren<MaskableGraphic>(true))
        {
            graphic.raycastTarget = false;
            graphic.maskable = true;
        }
        if (fill != null) _gameColor = fill.color;
        if (text != null) (_gameTextColor, _gameTextSize) = (text.color, text.fontSize);
    }

    public void ApplyFont(double size)
    {
        if (_text != null) _text.fontSize = UnityView.FontOf(size, _gameTextSize, _gameTextSize);
    }

    public void Apply(DirtyFlags dirty)
    {
        if ((dirty & (DirtyFlags.Value | DirtyFlags.Range)) != 0 && _fill != null)
        {
            float fill = (float)(_model.Percentage / 100);
            if (_shownFill != fill) _fill.fillAmount = (_shownFill = fill).Value;
        }
        if ((dirty & (DirtyFlags.Value | DirtyFlags.Range | DirtyFlags.Text)) != 0 && _text != null)
        {
            string text = _model.ShowProgressText ? _model.ProgressText : "";
            if (_shownText != text) _text.text = _shownText = text;
        }
        if ((dirty & DirtyFlags.Style) != 0 && _fill != null)
        {
            var color = _model.Foreground ?? _gameColor;
            if (_shownColor != color) _fill.color = (_shownColor = color).Value;
        }
        if ((dirty & DirtyFlags.Style) != 0 && _text != null)
        {
            var color = _model.ProgressTextForeground ?? _gameTextColor;
            if (_shownTextColor != color) _text.color = (_shownTextColor = color).Value;
        }
    }
}
