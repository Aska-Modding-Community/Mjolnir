using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The pure rules of a key hint's key (<see cref="UiKeyHint"/>; no Unity: unit-tested). The game draws keys with the
/// sprite asset KbdMouseGlyphs, its sprites named as the keys read: "F7", "Space", "Left Shift", "Numpad 1", "Control".
/// </summary>
internal static class KeyGlyphs
{
    private static readonly Dictionary<string, string> s_names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UpArrow"] = "Up Arrow", ["DownArrow"] = "Down Arrow", ["LeftArrow"] = "Left Arrow", ["RightArrow"] = "Right Arrow",
        ["PageUp"] = "Page Up", ["PageDown"] = "Page Down", ["NumLock"] = "Num Lock", ["CapsLock"] = "Caps Lock",
        ["ScrollLock"] = "Scroll Lock", ["PrintScreen"] = "Print Screen", ["ContextMenu"] = "Menu",
        ["LeftShift"] = "Left Shift", ["RightShift"] = "Right Shift", ["LeftCtrl"] = "Left Control", ["RightCtrl"] = "Right Control",
        ["LeftAlt"] = "Left Alt", ["RightAlt"] = "Right Alt", ["AltGr"] = "Alt Gr",
        ["LeftMeta"] = "Left System", ["LeftWindows"] = "Left System", ["LeftCommand"] = "Left System", ["LeftApple"] = "Left System",
        ["RightMeta"] = "Right System", ["RightWindows"] = "Right System", ["RightCommand"] = "Right System", ["RightApple"] = "Right System",
        ["NumpadPeriod"] = "Numpad .", ["NumpadDivide"] = "Numpad /", ["NumpadMultiply"] = "Numpad *", ["NumpadMinus"] = "Numpad -",
        ["NumpadPlus"] = "Numpad +", ["NumpadEnter"] = "Numpad Enter", ["NumpadEquals"] = "Numpad Equals",
        ["Quote"] = "'", ["Semicolon"] = ";", ["Comma"] = ",", ["Period"] = ".", ["Slash"] = "/", ["Backslash"] = "\\",
        ["LeftBracket"] = "[", ["RightBracket"] = "]", ["Minus"] = "-", ["Equals"] = "=", ["Backquote"] = "`",
        // The mouse, named as MouseButton is: the game has pictures of its buttons, not of its wheel.
        ["LeftButton"] = "Left Button", ["RightButton"] = "Right Button", ["MiddleButton"] = "Middle Button",
        ["ForwardButton"] = "Forward", ["BackButton"] = "Back",
    };

    /// <summary>The mouse wheel's name in a gesture: it has no picture, so a hint writes it out in the player's language.</summary>
    public const string Wheel = "Wheel";

    /// <summary>The sprite name of an Input System key name (<see cref="KeyGesture.Key"/>): <c>Digit1</c> → "1", <c>LeftShift</c> → "Left Shift".</summary>
    public static string SpriteName(string key)
    {
        if (key.Length == 1 && char.IsLetter(key[0])) return key.ToUpperInvariant();
        if (key.Length == 6 && key.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && char.IsDigit(key[5])) return key[5..];
        if (key.Length == 7 && key.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) && char.IsDigit(key[6])) return "Numpad " + key[6];
        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24) return "F" + f;
        if (s_names.TryGetValue(key, out string? name)) return name;
        return key.Length > 0 ? char.ToUpperInvariant(key[0]) + key[1..] : key; // Space, Enter, Escape… are named as the keys
    }

    /// <summary>
    /// A gesture's keys as the hint shows them: the modifiers (Ctrl, Shift, Alt, in <see cref="KeyGesture.ToString"/>'s
    /// order), then the key, joined by "+"; each the game's picture <c>&lt;sprite name="…"&gt;</c> when its sprite asset
    /// <paramref name="has"/> it, else its name in text. Empty for <see cref="KeyGesture.None"/>.
    /// </summary>
    public static string Text(KeyGesture gesture, Func<string, bool> has, string? wheel = null)
    {
        if (gesture.IsNone) return "";
        var parts = new List<string>(4);
        if ((gesture.Modifiers & KeyModifiers.Control) != 0) parts.Add("Control");
        if ((gesture.Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((gesture.Modifiers & KeyModifiers.Alt) != 0) parts.Add("Left Alt");
        bool isWheel = gesture.Key.Equals(Wheel, StringComparison.OrdinalIgnoreCase);
        parts.Add(isWheel ? "" : SpriteName(gesture.Key));
        // The wheel's word is set smaller, at about the height of the pictures' own letters.
        return string.Join(" + ", parts.Select((p, i) => isWheel && i == parts.Count - 1 ? $"<size=60%><voffset=0.4em>{wheel ?? Wheel}</voffset></size>" : has(p) ? $"<sprite name=\"{p}\">" : p));
    }
}
