using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The console window's line to type into: the game's rename field across the window's footer. Enter runs the line
/// (<see cref="DevConsole.Execute"/>) and keeps the field focused for the next one; Up and Down go through the lines run
/// before (<see cref="History"/>); the console's key closes the window from the field too. While the field has the focus
/// the game's keys stand aside (<see cref="TextFields"/>): the SDK focuses it itself, past the game's own way in.
/// </summary>
internal sealed class ConsoleInput(string placeholder)
{
    private TMP_InputField? _field;
    private int _focusIn = -1;
    private string _lastText = "";
    private bool _typing;

    public CommandHistory History { get; } = new();

    /// <summary>Builds the field into the footer (the window's <see cref="UiUnityHost"/>).</summary>
    public void Build(RectTransform parent)
    {
        if (Theme.InputField == null) return;
        var go = Object.Instantiate(Theme.InputField, parent, false);
        go.name = "Field";
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var helper = go.GetComponent<SSSGame.UI.InputFieldHelper>();
        if (helper != null) helper.MinCharactersCount = 0;
        _field = go.GetComponentInChildren<TMP_InputField>(true);
        _field.characterLimit = 0;
        _field.lineType = TMP_InputField.LineType.SingleLine;
        TextFields.Add(_field);
        _field.SetTextWithoutNotify("");
        // A command line reads from the left (the game's rename field centres its text).
        if (_field.textComponent != null) _field.textComponent.alignment = TextAlignmentOptions.Left;
        if (_field.placeholder != null && _field.placeholder.TryCast<TMP_Text>() is { } hint)
        {
            hint.text = placeholder;
            hint.alignment = TextAlignmentOptions.Left;
        }
        _field.onSubmit.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(new Action<string>(Submit)));
    }

    /// <summary>Focuses the field on the next frame (the window just opened, or a line was run).</summary>
    public void Focus() => _focusIn = 1;

    /// <summary>
    /// Every frame while the window is open: the focus, Up and Down while the field has it, and the console's key
    /// (<paramref name="closeKey"/>) pressed while it has it: hotkeys wait while the player types, so the field gives up
    /// its focus and its text as it was before the key, and the window is to close (true).
    /// </summary>
    public bool Tick(Func<bool> closeKey)
    {
        if (_field == null) return false;
        if (_focusIn >= 0 && _focusIn-- == 0)
        {
            _field.Select();
            _field.ActivateInputField();
        }
        TextFields.Hold(ref _typing, _field.isFocused);
        if (!_field.isFocused) return false;
        if (closeKey())
        {
            // The key's character is in the text if the field took it before this tick; the last frame's text has none.
            _field.DeactivateInputField();
            _field.SetTextWithoutNotify(_lastText);
            return true;
        }
        var keyboard = Keyboard.current;
        string? recalled = keyboard == null ? null
            : keyboard.upArrowKey.wasPressedThisFrame ? History.Back(_field.text)
            : keyboard.downArrowKey.wasPressedThisFrame ? History.Forward()
            : null;
        if (recalled != null)
        {
            _field.text = recalled;
            _field.caretPosition = recalled.Length;
            _field.stringPosition = recalled.Length;
        }
        _lastText = _field.text;
        return false;
    }

    /// <summary>The window closed: the typing context goes (the field lost the focus with it).</summary>
    public void LetGo() => TextFields.Hold(ref _typing, false);

    private void Submit(string line)
    {
        if (_field == null) return;
        _field.SetTextWithoutNotify("");
        _lastText = "";
        if (History.Add(line) is { } command) DevConsole.Execute(command);
        Focus(); // Enter took the focus away
    }
}
