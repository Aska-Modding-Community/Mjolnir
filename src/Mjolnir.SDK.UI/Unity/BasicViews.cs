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

/// <summary>A paragraph of text.</summary>
internal sealed class TextView : UnityView
{
    private readonly UiTextBlock _text;
    private readonly TextMeshProUGUI _label;
    private readonly TextAlignment _defaultAlign;
    private string? _shown;

    public TextView(UiTextBlock text, RectTransform parent, TextAlignment defaultAlign) : base(text)
    {
        _text = text;
        _defaultAlign = defaultAlign;
        _label = ViewFactory.NewText(parent, DefaultName, "", Theme.TextFont, 22);
        _label.raycastTarget = false;
        Root = _label.rectTransform;
    }

    protected override string DefaultName => "TextBlock";

    private bool Limited => LayoutPlan.IsLimit(_text.MaxWidth) || LayoutPlan.IsLimit(_text.MaxHeight);

    protected override void ApplyFont(double size) => _label.fontSize = FontOf(size, 22, 22);

    // Its own size, for a maximum along its panel to cap (TextMeshPro measures at its current width).
    protected override (float Width, float Height) Measured => Limited ? (_label.preferredWidth, _label.preferredHeight) : (-1, -1);

    protected override void ApplyLayout()
    {
        base.ApplyLayout();
        _label.overflowMode = LayoutPlan.IsLimit(_text.MaxHeight) ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Text) != 0 && _shown != _text.Text)
        {
            _label.text = _shown = _text.Text;
            if (Limited) ApplyLayout(); // a new text, a new size to cap
        }
        if ((dirty & DirtyFlags.Style) != 0)
        {
            _label.margin = ViewFactory.TextMargin(_text.Padding, Vector4.zero);
            if (_text.Foreground is { } color) _label.color = color;
            _label.alignment = TmpAlignment(_text.TextAlignment ?? _defaultAlign);
            // The game's default does not wrap.
            _label.textWrappingMode = _text.TextWrapping == TextWrapping.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        }
    }
}

/// <summary>A section title.</summary>
internal sealed class HeaderView : UnityView
{
    private readonly UiSectionHeader _header;
    private readonly TMP_Text _label;
    private readonly float _height = 50;
    private readonly float _gameSize;
    private string? _shown;

    public HeaderView(UiSectionHeader header, RectTransform parent) : base(header)
    {
        _header = header;
        if (Theme.HeaderRow != null)
        {
            var row = SettingRows.Clone(Theme.HeaderRow, parent, DefaultName);
            Root = RectOf(row);
            _height = Theme.HeaderRow.GetComponent<RectTransform>().sizeDelta.y;
            _label = row.GetComponentInChildren<TMP_Text>(true);
            var rect = _label.rectTransform;
            rect.anchorMin = new Vector2(0, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1, rect.anchorMax.y);
            rect.sizeDelta = new Vector2(-2 * SettingRows.LabelInset, rect.sizeDelta.y);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Ellipsis;
        }
        else
        {
            var text = ViewFactory.NewText(parent, DefaultName, "", Theme.TitleFont, 26);
            Root = text.rectTransform;
            _label = text;
        }
        _gameSize = _label.fontSize;
    }

    protected override string DefaultName => "SectionHeader";

    protected override void ApplyFont(double size) => _label.fontSize = FontOf(size, _gameSize, _gameSize);

    protected override (float Width, float Height) LayoutDefaults => (-1, _height);

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Text) != 0 && _shown != _header.Text) _label.text = _shown = _header.Text;
    }
}

/// <summary>A standalone button in the game's dialog style.</summary>
internal sealed class ButtonView : UnityView
{
    private readonly UiButton _button;
    private readonly ButtonText? _label;
    private string? _shown;

    public ButtonView(UiButton button, RectTransform parent) : base(button)
    {
        _button = button;
        if (Theme.DialogButton != null)
        {
            var go = ViewFactory.CloneButton(Theme.DialogButton, parent, DefaultName, "", button.OnClick);
            Root = RectOf(go);
            Listen(go.GetComponent<Button>().onClick);
            _label = ButtonText.Of(go);
        }
        else
        {
            var text = ViewFactory.NewText(parent, DefaultName, "", Theme.TitleFont, 20);
            var plain = text.gameObject.AddComponent<Button>();
            plain.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(button.OnClick)));
            Listen(plain.onClick);
            Root = text.rectTransform;
        }
    }

    protected override string DefaultName => "Button";

    protected override (float Width, float Height) LayoutDefaults => (300, 36);

    protected override bool FixedSize => true;

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Text) != 0 && _shown != _button.Content) ViewFactory.SetLabel(Root.gameObject, _shown = _button.Content);
        if ((dirty & DirtyFlags.Style) != 0) _label?.Pad(_button.Padding);
    }

    protected override void ApplyFont(double size) => _label?.Apply(size);
}

/// <summary>A picture.</summary>
internal sealed class ImageView : UnityView
{
    private readonly UiImage _image;
    private readonly Image _graphic;
    private Vector2 _size = new(64, 64);

    public ImageView(UiImage image, RectTransform parent) : base(image)
    {
        _image = image;
        Root = ViewFactory.NewRect(parent, DefaultName);
        _graphic = Root.gameObject.AddComponent<Image>();
        _graphic.raycastTarget = false;
    }

    protected override string DefaultName => "Image";

    protected override (float Width, float Height) LayoutDefaults => (_size.x, _size.y);

    protected override bool FixedSize => true;

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Image) == 0) return;
        var sprite = Pictures.SpriteOf(_image.Source);
        _graphic.sprite = sprite;
        _graphic.color = Color.white;
        _graphic.preserveAspect = _image.Stretch == Stretch.Uniform;
        if (sprite != null) _size = new Vector2(sprite.rect.width, sprite.rect.height);
        ApplyLayout();
    }
}

/// <summary>An empty object for the mod's own Unity UI.</summary>
internal sealed class UnityHostView : UnityView
{
    private readonly UiUnityHost _host;

    public UnityHostView(UiUnityHost host, RectTransform parent) : base(host)
    {
        _host = host;
        Root = ViewFactory.NewRect(parent, DefaultName);
    }

    protected override string DefaultName => "UnityHost";

    public override void Created()
    {
        ModCall.InvokeEach(_host.Build, Root, $"{_host} build");
        base.Created();
    }
}
