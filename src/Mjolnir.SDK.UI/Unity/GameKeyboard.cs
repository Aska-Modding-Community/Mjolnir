using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using InputKey = UnityEngine.InputSystem.Key;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's keyboard for <see cref="HotKeys"/>: Unity's Input System (the game uses no legacy Input), key names as its
/// <c>Key</c> enum names them. A text field with the focus (TMP_InputField) means the player is typing.
/// </summary>
internal sealed class GameKeyboard : IKeyboardSource
{
    private static readonly HashSet<InputKey> s_modifiers =
    [
        InputKey.LeftCtrl, InputKey.RightCtrl, InputKey.LeftShift, InputKey.RightShift, InputKey.LeftAlt, InputKey.RightAlt,
        InputKey.LeftMeta, InputKey.RightMeta,
    ];

    private readonly Dictionary<string, InputKey?> _keys = new(StringComparer.OrdinalIgnoreCase);

    private GameKeyboard() { }

    private static Keyboard? Current => Keyboard.current;

    public KeyModifiers Modifiers
    {
        get
        {
            var keyboard = Current;
            if (keyboard == null) return KeyModifiers.None;
            var held = KeyModifiers.None;
            if (keyboard.ctrlKey.isPressed) held |= KeyModifiers.Control;
            if (keyboard.shiftKey.isPressed) held |= KeyModifiers.Shift;
            if (keyboard.altKey.isPressed) held |= KeyModifiers.Alt;
            return held;
        }
    }

    public bool IsTyping
    {
        get
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return false;
            var input = selected.GetComponent<TMP_InputField>();
            return input != null && input.isFocused;
        }
    }

    /// <summary>Whether the left mouse button is held (a slider being dragged); false without a mouse.</summary>
    public static bool PointerHeld() => Mouse.current is { } mouse && mouse.leftButton.isPressed;

    /// <summary>The mouse pointer in screen pixels (null without a mouse), or <see cref="PointerOverride"/> while set.</summary>
    public static UnityEngine.Vector2? PointerPosition() =>
        PointerOverride ?? (Mouse.current is { } mouse ? mouse.position.ReadValue() : null);

    /// <summary>
    /// A pointer position that stands in for the mouse's (tooltips). Only UiLab's autopilot sets it, by reflection, to rest
    /// a pointer on an element without moving the player's cursor.
    /// </summary>
    internal static UnityEngine.Vector2? PointerOverride;

    /// <summary>
    /// A key that counts as pressed in one frame (its name and <c>Time.frameCount</c>). Only UiLab's autopilot sets it, by
    /// reflection, to press a key without the player's keyboard.
    /// </summary>
    internal static (string Key, int Frame)? PressOverride;

    /// <summary>The keyboard, or null in a game without one (a dedicated server).</summary>
    public static GameKeyboard? Create() => Current != null ? new GameKeyboard() : null;

    public bool? WasPressed(string key)
    {
        if (PressOverride is var (pressed, frame) && frame == UnityEngine.Time.frameCount && pressed.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!_keys.TryGetValue(key, out var resolved))
            _keys[key] = resolved = Enum.TryParse<InputKey>(key, ignoreCase: true, out var parsed) && parsed != InputKey.None ? parsed : null;
        if (resolved == null) return null;
        var keyboard = Current;
        return keyboard != null && keyboard[resolved.Value].wasPressedThisFrame;
    }

    public string? FirstPressed()
    {
        var keyboard = Current;
        if (keyboard == null || !keyboard.anyKey.wasPressedThisFrame) return null;
        var keys = keyboard.allKeys;
        for (int i = 0; i < keys.Count; i++)
        {
            KeyControl control = keys[i];
            if (control == null || !control.wasPressedThisFrame || s_modifiers.Contains(control.keyCode)) continue;
            return control.keyCode.ToString();
        }
        return null;
    }
}
