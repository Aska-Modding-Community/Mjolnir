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

/// <summary>Base of a settings row or bare control built from a game template, with its header, description and icon.</summary>
internal abstract class RowView : UnityView
{
    private string? _shownHeader;
    private string? _shownDescription;
    private Image? _icon;
    private TMP_Text? _iconLetter;
    private object? _shownIcon; // the Bitmap or the Monogram on show

    protected RowView(HeaderedControl control) : base(control) => Control = control;

    protected HeaderedControl Control { get; }
    protected TMP_Text? LabelText { get; set; }
    protected TMP_Text? DescriptionText { get; set; }
    protected float RowHeight { get; set; } = 40;
    protected bool Bare { get; set; }

    /// <summary>The width of the row's control part (<see cref="HeaderedControl.ContentWidth"/>, fixed when the row is built).</summary>
    protected float ContentWidth { get; private set; } = SettingRows.ContentWidth;

    /// <summary>The room for a control of our own in the control part (a field, a value, a button).</summary>
    protected float Inner => Math.Max(40, ContentWidth - 2 * SettingRows.LabelInset);

    private float _labelSize = 18, _descriptionSize = 14;
    private Action? _fitDescription;
    private float _descriptionGrowth;  // how much taller the row is for its wrapped description
    private (float Width, string Text, float Size) _fitted;

    protected override (float Width, float Height) LayoutDefaults => Bare ? (ContentWidth, RowHeight) : (-1, RowHeight);

    protected override bool FixedSize => Bare;

    protected void ShowLabel()
    {
        string? header = Control.Header, description = Control.Description;
        if (LabelText != null && header != null && _shownHeader != header) LabelText.text = _shownHeader = header;
        if (DescriptionText != null && description != null && _shownDescription != description)
            DescriptionText.text = _shownDescription = SettingRows.FirstLine(description);
        ShowIcon();
    }

    // A description wraps over up to three lines and the row grows for them, measured each frame against the width the
    // row got (it is known only once laid out, and follows the window's).
    private void FitDescription()
    {
        if (Root == null || DescriptionText == null || LabelText == null) return;
        var hint = DescriptionText.rectTransform;
        float width = hint.rect.width;
        string text = DescriptionText.text;
        float size = DescriptionText.fontSize;
        if (width <= 0 || _fitted == (width, text, size)) return;
        _fitted = (width, text, size);
        float line = DescriptionText.GetPreferredValues("Ag", 10000, 0).y;
        float preferred = DescriptionText.GetPreferredValues(text, width, 0).y;
        float growth = SettingRows.DescriptionGrowth(preferred, line);
        float delta = growth - _descriptionGrowth;
        if (Math.Abs(delta) < 0.5f) return;
        _descriptionGrowth = growth;
        hint.offsetMax += new Vector2(0, delta);
        LabelText.rectTransform.offsetMin += new Vector2(0, delta);
        RowHeight += delta;
        ApplyLayout();
    }

    public override void Dispose()
    {
        if (_fitDescription != null) LiveTree.EveryFrame(_fitDescription, false);
        base.Dispose();
    }

    // The header at the size, the description in proportion.
    protected override void ApplyFont(double size)
    {
        if (LabelText != null) LabelText.fontSize = FontOf(size, _labelSize, _labelSize);
        if (DescriptionText != null) DescriptionText.fontSize = FontOf(size, _descriptionSize, _labelSize);
    }

    /// <summary>A text of the control part at the size: the main text of a bare control, else in proportion to the header.</summary>
    protected float PartFontOf(double size, float game) => FontOf(size, game, Bare ? game : _labelSize);

    // The picture, or else the letter tile on its own colour (muted, like the game's frames).
    internal override RectTransform? IconArea => _icon != null ? _icon.rectTransform : null;

    private void ShowIcon()
    {
        if (_icon == null || _iconLetter == null) return;
        Bitmap? picture = Control.Icon;
        Monogram? tile = Control.Monogram;
        object? wanted = (object?)picture ?? tile;
        if (Equals(wanted, _shownIcon)) return;
        _shownIcon = wanted;
        if (picture != null)
        {
            _icon.sprite = Pictures.SpriteOf(picture);
            _icon.color = Color.white;
            _iconLetter.text = "";
        }
        else if (tile is { } monogram)
        {
            _icon.sprite = null;
            _icon.color = Color.HSVToRGB(monogram.Hue, 0.45f, 0.42f);
            _iconLetter.text = monogram.Letter;
        }
    }

    /// <summary>
    /// A row with the dialog's button on the right in place of the template's control (a button row, a hotkey row); the
    /// plain text line without the game's parts. Sets <see cref="UnityView.Root"/>.
    /// </summary>
    protected (GameObject? Button, ButtonText? Text) BuildButtonRow(RectTransform parent, Action click)
    {
        var row = BuildRow(parent, Theme.SliderRow);
        Root = RectOf(row);
        if (Theme.SliderRow == null || Theme.DialogButton == null) return (null, null);
        SettingRows.RemoveControl(row);
        var clone = ViewFactory.CloneButton(Theme.DialogButton, row.transform, "Button", "", click);
        Listen(clone.GetComponent<Button>().onClick);
        SettingRows.PlaceRight(RectOf(clone), Math.Min(200, Inner), 30);
        return (clone, ButtonText.Of(clone));
    }

    /// <summary>A row from <paramref name="template"/>, or a plain text line when the game no longer has it.</summary>
    protected GameObject BuildRow(RectTransform parent, GameObject? template)
    {
        string? header = Control.Header, description = Control.Description;
        if (template == null)
        {
            var plain = SettingRows.Plain(parent, DefaultName);
            LabelText = plain;
            return plain.gameObject;
        }
        ContentWidth = SettingRows.ContentWidthOf(Control);
        var row = SettingRows.NewRow(parent, template, DefaultName, header, description, out var labelText, out var descriptionText,
                                     out float height, ContentWidth, Control.Padding);
        LabelText = labelText;
        DescriptionText = descriptionText;
        if (labelText != null) _labelSize = labelText.fontSize;
        if (descriptionText != null) _descriptionSize = descriptionText.fontSize;
        RowHeight = height;
        _descriptionGrowth = 0;
        _fitted = default;
        if (descriptionText != null) LiveTree.EveryFrame(_fitDescription ??= FitDescription, true);
        else if (_fitDescription != null) LiveTree.EveryFrame(_fitDescription, false);
        Bare = header == null;
        // The slot exists only with a picture or a tile: one appearing or going rebuilds the row (HeaderedControl.IconProperty).
        if (!Bare && (Control.Icon != null || Control.Monogram != null))
            (_icon, _iconLetter) = SettingRows.AddIcon(row.transform, height, labelText, descriptionText,
                                                       Control.Padding is { } padding ? (float)padding.Left : SettingRows.IconLeft);
        return row;
    }
}

/// <summary>A settings row with a text on the right (a value, a status).</summary>
internal sealed class TextRowView : RowView
{
    private readonly UiTextBlock _text;
    private readonly TMP_Text? _value;
    private string? _shown;

    public TextRowView(UiTextBlock text, RectTransform parent) : base(text)
    {
        _text = text;
        var row = BuildRow(parent, Theme.SliderRow);
        Root = RectOf(row);
        if (Theme.SliderRow == null) return;
        SettingRows.RemoveControl(row);
        var value = ViewFactory.NewText(row.transform, "Value", "", LabelText != null ? LabelText.font : Theme.TextFont, 18);
        value.alignment = TextAlignmentOptions.Right;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        value.overflowMode = TextOverflowModes.Ellipsis;
        value.raycastTarget = false;
        SettingRows.PlaceRight(value.rectTransform, Inner, 30);
        _value = value;
    }

    protected override string DefaultName => "InfoRow";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        if (_value != null) _value.fontSize = PartFontOf(size, 18);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & (DirtyFlags.Text | DirtyFlags.Label)) != 0 && _shown != _text.Text)
        {
            _shown = _text.Text;
            if (_value != null) _value.text = _shown;
            else if (LabelText != null) LabelText.text = $"{_text.Header}: {_shown}"; // plain fallback
        }
    }
}

/// <summary>A settings row with a button on the right.</summary>
internal sealed class ButtonRowView : RowView
{
    private readonly UiButton _button;
    private readonly GameObject? _clone;
    private readonly ButtonText? _label;
    private string? _shown;

    public ButtonRowView(UiButton button, RectTransform parent) : base(button)
    {
        _button = button;
        (_clone, _label) = BuildButtonRow(parent, button.OnClick);
    }

    protected override string DefaultName => "ButtonRow";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        _label?.Apply(double.IsNaN(size) ? double.NaN : PartFontOf(size, 16));
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Style) != 0) _label?.Pad(_button.Padding);
        if ((dirty & DirtyFlags.Text) != 0 && _shown != _button.Content)
        {
            _shown = _button.Content;
            if (_clone != null) ViewFactory.SetLabel(_clone, _shown);
        }
    }
}

/// <summary>A settings row with a button on the right that shows a key gesture and records a new one when clicked.</summary>
internal sealed class HotKeyBoxView : RowView
{
    private readonly UiHotKeyBox _box;
    private readonly GameObject? _clone;
    private string? _shown;

    public HotKeyBoxView(UiHotKeyBox box, RectTransform parent) : base(box)
    {
        _box = box;
        (_clone, _text) = BuildButtonRow(parent, box.StartRecording);
    }

    private readonly ButtonText? _text;

    protected override string DefaultName => "HotKeyRow";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        _text?.Apply(double.IsNaN(size) ? double.NaN : PartFontOf(size, 16));
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Value) != 0 && _shown != _box.ShownText)
        {
            _shown = _box.ShownText;
            if (_clone != null) ViewFactory.SetLabel(_clone, _shown);
        }
    }
}

/// <summary>The game's "‹ value ›" selector: toggle switches (Off/On) and selectors.</summary>
internal class SelectorView : RowView
{
    private readonly Func<IReadOnlyList<string>> _options;
    private readonly Func<int> _index;
    private readonly IncreaseDecreasePanel? _panel;
    private readonly TMP_Text? _value;
    private IReadOnlyList<string> _shownOptions = [];
    private int _shownIndex = -2;
    private float _valueSize = 17;

    public SelectorView(HeaderedControl control, RectTransform parent, Func<IReadOnlyList<string>> options, Func<int> index,
                        Action<int> fromView) : base(control)
    {
        _options = options;
        _index = index;
        var row = BuildRow(parent, Theme.SelectorRow);
        Root = RectOf(row);
        _panel = row.GetComponent<IncreaseDecreasePanel>();
        if (_panel == null) return;
        _value = _panel.ValueText;
        if (_value != null) _valueSize = _value.fontSize;
        _panel.OnValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<int>>(new Action<int>(i =>
        {
            if (Applying) return;
            GameUi.Guard($"{Element} value", () =>
            {
                _shownIndex = i;
                ShowText();
                fromView(i);
            });
        })));
        Listen(_panel.OnValueChanged);
    }

    protected override string DefaultName => Control is UiToggleSwitch ? "ToggleSwitch" : "Stepper";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        if (_value != null) _value.fontSize = PartFontOf(size, _valueSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Options) != 0)
        {
            _shownOptions = _options();
            if (_panel != null) SettingRows.SetupSelector(_panel, _shownOptions);
            _shownIndex = -2;
        }
        if ((dirty & (DirtyFlags.Value | DirtyFlags.Options)) != 0)
        {
            int index = _index();
            if (index == _shownIndex) return;
            _shownIndex = index;
            if (index >= 0) _panel?.SetValue(index, true);
            ShowText();
        }
    }

    private void ShowText()
    {
        string text = _shownIndex >= 0 && _shownIndex < _shownOptions.Count ? _shownOptions[_shownIndex] : "";
        if (_value != null) _value.text = text;
        else if (_panel == null && LabelText != null) LabelText.text = $"{Control.Header}: {text}"; // plain fallback
    }
}

/// <summary>
/// A row of the Mods window: the game's Off/On selector row (picture, name, status line) with a Settings button left of
/// the selector when the mod has settings. The button is anchored where the selector's leftmost part is, so it follows
/// the selector at any row width; the name and the status line end before it.
/// </summary>
internal sealed class ModRowView : SelectorView
{
    private const float ButtonWidth = 170;
    private const float Gap = 12;
    private readonly ModRow _row;
    private readonly GameObject? _settings;
    private string? _shownSettings;

    public ModRowView(ModRow row, RectTransform parent)
        : base(row, parent, () => [Theme.Loc("common.off", "Off"), Theme.Loc("common.on", "On")], () => row.IsOn ? 1 : 0,
               i => row.SetOnFromView(i == 1))
    {
        _row = row;
        if (row.SettingsText == null || row.Header == null || Theme.DialogButton == null || Theme.SelectorRow == null) return;
        var (anchor, left) = SettingRows.ControlLeft(Root, Theme.SelectorRow.GetComponent<RectTransform>().sizeDelta.x);
        _settings = ViewFactory.CloneButton(Theme.DialogButton, Root, "Settings", "", row.OnSettingsClick);
        Listen(_settings.GetComponent<Button>().onClick);
        var rect = RectOf(_settings);
        rect.anchorMin = rect.anchorMax = new Vector2(anchor, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(ButtonWidth, 30);
        rect.anchoredPosition = new Vector2(left - Gap, 0);
        var text = _settings.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.fontSize = 16;
        foreach (var label in new[] { LabelText, DescriptionText })
        {
            if (label == null) continue;
            var area = label.rectTransform;
            area.anchorMax = new Vector2(anchor, area.anchorMax.y);
            area.offsetMax = new Vector2(left - 2 * Gap - ButtonWidth, area.offsetMax.y);
        }
    }

    protected override string DefaultName => "ModRow";

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        base.ApplyOwn(dirty);
        if ((dirty & DirtyFlags.Text) != 0 && _settings != null && _shownSettings != _row.SettingsText)
            ViewFactory.SetLabel(_settings, _shownSettings = _row.SettingsText ?? "");
    }
}

/// <summary>A slider with its value on the right.</summary>
internal sealed class SliderView : RowView
{
    private readonly UiSlider _model;
    private readonly Slider? _slider;
    private readonly TMP_Text? _value;
    private double _shown = double.NaN;

    public SliderView(UiSlider model, RectTransform parent) : base(model)
    {
        _model = model;
        var row = BuildRow(parent, Theme.SliderRow);
        Root = RectOf(row);
        _slider = row.GetComponentInChildren<Slider>(true);
        if (_slider == null) return;
        _value = row.transform.Find("valueLabel")?.GetComponent<TMP_Text>();
        if (_value != null)
        {
            _valueSize = _value.fontSize;
            // The game's value label is narrow: let long numbers shrink instead of spilling out.
            _value.fontSizeMax = _value.fontSize;
            _value.fontSizeMin = 8;
            _value.enableAutoSizing = true;
        }
        _slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(v =>
        {
            if (Applying) return;
            GameUi.Guard($"{Element} value", () =>
            {
                _model.SetFromView(v);
                // The model's answer (snapped, or clamped by a bound setting) goes back to the slider.
                _shown = double.NaN;
                Apply(DirtyFlags.Value);
            });
        })));
        Listen(_slider.onValueChanged);
    }

    private float _valueSize = 14;

    protected override string DefaultName => "Slider";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        if (_value != null) _value.fontSizeMax = PartFontOf(size, _valueSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Range) != 0 && _slider != null)
        {
            _slider.minValue = (float)Math.Min(_model.Minimum, _model.Maximum);
            _slider.maxValue = (float)Math.Max(_model.Minimum, _model.Maximum);
            _slider.wholeNumbers = _model.WholeSteps;
            _shown = double.NaN;
        }
        if ((dirty & (DirtyFlags.Value | DirtyFlags.Range)) != 0)
        {
            double number = _model.Value;
            if (number.Equals(_shown)) return;
            _shown = number;
            _slider?.SetValueWithoutNotify((float)number);
            ShowText(number);
        }
    }

    private void ShowText(double number)
    {
        if (_value != null) _value.text = _model.Show(number);
        else if (_slider == null && LabelText != null) LabelText.text = $"{_model.Header}: {_model.Show(number)}";
    }
}

/// <summary>A dropdown list, its box pinned to the row's right edge.</summary>
internal sealed class DropdownView : RowView
{
    private readonly SelectingItemsControl _model;
    private readonly TMP_Dropdown? _dropdown;
    private TMP_Text? _placeholder;
    private int _shownIndex = -2;

    public DropdownView(SelectingItemsControl model, RectTransform parent) : base(model)
    {
        _model = model;
        var row = BuildRow(parent, Theme.DropdownRow);
        Root = RectOf(row);
        _dropdown = row.GetComponent<TMP_Dropdown>();
        if (_dropdown == null) return;
        if (_dropdown.captionText != null) _captionSize = _dropdown.captionText.fontSize;
        // The game centres its dropdown box in a fixed-width row; pin it to the right edge instead. (TryCast, not "is":
        // Il2CppInterop wraps Find's result as the declared Transform, whatever the native object is.)
        var box = row.transform.Find("Dropdown")?.TryCast<RectTransform>();
        if (box != null)
        {
            SettingRows.PinRight(box, Theme.DropdownRow!.GetComponent<RectTransform>().sizeDelta.x);
            // The open list is a copy of the template, and the template is anchored to the box's top-left corner. It
            // is given the box's width and put right under the field, because the box's layout group no longer places
            // it (Theme.FixDropdownList takes it out, or the box would grow to the list's height and draw its dark
            // background round the whole list — seen by the user, 16.11.2026).
            var template = _dropdown.template;
            if (template != null && Math.Abs(template.anchorMin.x - template.anchorMax.x) < 0.01f
                                 && Math.Abs(template.anchorMin.y - template.anchorMax.y) < 0.01f)
            {
                float width = box.rect.width, height = template.rect.height, field = box.rect.height;
                template.offsetMin = new Vector2(0, -field - height);
                template.offsetMax = new Vector2(width, -field);
            }
        }
        _dropdown.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<int>>(new Action<int>(i =>
        {
            if (Applying) return;
            GameUi.Guard($"{Element} value", () =>
            {
                _shownIndex = i;
                _model.SetIndexFromView(i);
            });
        })));
        Listen(_dropdown.onValueChanged);
    }

    private float _captionSize = 18;

    protected override string DefaultName => "ComboBox";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        if (_dropdown?.captionText != null) _dropdown.captionText.fontSize = PartFontOf(size, _captionSize);
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Options) != 0)
        {
            if (_dropdown != null)
            {
                _dropdown.ClearOptions();
                var options = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (string option in _model.ItemTexts) options.Add(option);
                _dropdown.AddOptions(options);
                if (_placeholder != null) _placeholder.text = (_model as UiComboBox)?.PlaceholderText ?? "";
            }
            _shownIndex = -2;
        }
        if ((dirty & (DirtyFlags.Value | DirtyFlags.Options)) != 0)
        {
            int index = _model.SelectedIndex;
            if (index == _shownIndex) return;
            _shownIndex = index;
            if (_dropdown != null)
            {
                if (index < 0) EnsurePlaceholder();
                _dropdown.SetValueWithoutNotify(index);
                _dropdown.RefreshShownValue();
            }
            else if (LabelText != null)
            {
                var texts = _model.ItemTexts;
                LabelText.text = $"{_model.Header}: {(index >= 0 && index < texts.Count ? texts[index] : "")}";
            }
        }
    }

    // TMP lets the value be -1 (nothing selected) only with a placeholder: a dimmed copy of the caption.
    private void EnsurePlaceholder()
    {
        if (_placeholder != null || _dropdown == null || _dropdown.captionText == null) return;
        if (_dropdown.placeholder != null)
        {
            _placeholder = _dropdown.placeholder.TryCast<TMP_Text>();
            if (_placeholder != null) _placeholder.text = (_model as UiComboBox)?.PlaceholderText ?? "";
            return;
        }
        var caption = _dropdown.captionText;
        var copy = Object.Instantiate(caption.gameObject, caption.transform.parent, false);
        copy.name = "Placeholder";
        _placeholder = copy.GetComponent<TMP_Text>();
        _placeholder.text = (_model as UiComboBox)?.PlaceholderText ?? "";
        var color = _placeholder.color;
        _placeholder.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
        _dropdown.placeholder = _placeholder;
    }
}

/// <summary>A text input (the game's rename field) on the right of a row.</summary>
internal sealed class TextBoxView : RowView
{
    private readonly UiTextBox _box;
    private readonly TMP_InputField? _input;
    private string? _shown;

    public TextBoxView(UiTextBox box, RectTransform parent) : base(box)
    {
        _box = box;
        var row = BuildRow(parent, Theme.SliderRow);
        Root = RectOf(row);
        if (Theme.SliderRow == null || Theme.InputField == null) return;
        SettingRows.RemoveControl(row);
        var field = Object.Instantiate(Theme.InputField, row.transform, false);
        field.name = "Input";
        SettingRows.PlaceRight(RectOf(field), Inner, 30);
        var helper = field.GetComponent<SSSGame.UI.InputFieldHelper>();
        if (helper != null) helper.MinCharactersCount = 0;
        _input = field.GetComponentInChildren<TMP_InputField>(true);
        TextFields.Add(_input);
        if (_input.textComponent != null) (_textSize, _textAlign) = (_input.textComponent.fontSize, _input.textComponent.alignment);
        if (_input.textViewport != null) _viewport = (_input.textViewport.offsetMin, _input.textViewport.offsetMax);
        _input.onEndEdit.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(new Action<string>(s =>
        {
            if (Applying) return;
            GameUi.Guard($"{Element} text", () =>
            {
                _box.SetFromView(s);
                // The model's answer (a number setting refusing the text) goes back to the field.
                _shown = null;
                Apply(DirtyFlags.Value);
            });
        })));
        Listen(_input.onEndEdit);
        _input.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(new Action<string>(s =>
        {
            if (Applying || _box.UpdateSourceTrigger != UpdateSourceTrigger.PropertyChanged) return;
            _shown = s; // the field shows it already: the model's text must not be set back under the caret
            GameUi.Guard($"{Element} text", () => _box.SetFromView(s));
        })));
        Listen(_input.onValueChanged);
    }

    private float _textSize = 18;
    private TextAlignmentOptions _textAlign = TextAlignmentOptions.Center;
    private (Vector2 Min, Vector2 Max)? _viewport;

    public override void Dispose()
    {
        if (_input != null) TextFields.Remove(_input);
        base.Dispose();
    }

    protected override string DefaultName => "TextBox";

    protected override void ApplyFont(double size)
    {
        base.ApplyFont(size);
        if (_input == null) return;
        float text = PartFontOf(size, _textSize);
        if (_input.textComponent != null) _input.textComponent.fontSize = text;
        if (_input.placeholder != null && _input.placeholder.TryCast<TMP_Text>() is { } placeholder) placeholder.fontSize = text;
    }

    protected override void ApplyOwn(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Label) != 0) ShowLabel();
        if ((dirty & DirtyFlags.Style) != 0 && _input?.textViewport is { } area && _viewport is { } game)
        {
            // Around the text it edits: the text area's distance from the field's frame.
            area.offsetMin = _box.Padding is { } p ? new Vector2((float)p.Left, (float)p.Bottom) : game.Min;
            area.offsetMax = _box.Padding is { } q ? new Vector2(-(float)q.Right, -(float)q.Top) : game.Max;
        }
        if ((dirty & DirtyFlags.Style) != 0 && _input != null)
        {
            var align = _box.TextAlignment is { } a ? TmpAlignment(a) : _textAlign;
            if (_input.textComponent != null) _input.textComponent.alignment = align;
            if (_input.placeholder != null && _input.placeholder.TryCast<TMP_Text>() is { } placeholder) placeholder.alignment = align;
        }
        if ((dirty & DirtyFlags.Range) != 0 && _input != null)
        {
            _input.characterLimit = _box.MaxLength;
            _input.contentType = _box.ContentType switch
            {
                TextInputContentType.Digits => TMP_InputField.ContentType.IntegerNumber,
                TextInputContentType.Number => TMP_InputField.ContentType.DecimalNumber,
                _ => TMP_InputField.ContentType.Standard,
            };
            if (_input.placeholder != null && _input.placeholder.TryCast<TMP_Text>() is { } placeholder) placeholder.text = _box.PlaceholderText ?? "";
        }
        if ((dirty & DirtyFlags.Value) != 0 && _shown != _box.Text)
        {
            _shown = _box.Text;
            if (_input != null) _input.SetTextWithoutNotify(_shown);
            else if (LabelText != null) LabelText.text = $"{_box.Header}: {_shown}";
        }
    }
}
