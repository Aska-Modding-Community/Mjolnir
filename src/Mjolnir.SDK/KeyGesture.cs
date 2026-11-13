using System;
using System.Collections.Generic;

namespace Mjolnir.SDK;

/// <summary>The modifier keys held with a key; left and right count the same.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
}

/// <summary>
/// A key with the modifiers held with it, such as Ctrl+Shift+F8: a hotkey, and a config setting
/// the player can rebind in the Mods window (<c>config.Bind("Keys", "Toggle", KeyGesture.Parse("F8"))</c>). The key is
/// named as Unity's Input System names it: <c>F8</c>, <c>A</c>, <c>Digit1</c>, <c>Backquote</c>, <c>Space</c>,
/// <c>LeftArrow</c>, <c>Numpad0</c>…; case does not matter. <see cref="None"/> is no key, never pressed.
/// </summary>
public readonly record struct KeyGesture
{
    private static readonly (string Name, KeyModifiers Modifier)[] s_modifierNames =
        [("Ctrl", KeyModifiers.Control), ("Control", KeyModifiers.Control), ("Shift", KeyModifiers.Shift), ("Alt", KeyModifiers.Alt)];

    /// <param name="key">The key's Input System name (<c>F8</c>); empty for <see cref="None"/>.</param>
    /// <param name="modifiers">The modifiers held with it.</param>
    public KeyGesture(string key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key?.Trim() ?? "";
        Modifiers = Key.Length == 0 ? KeyModifiers.None : modifiers;
    }

    /// <summary>No key: a hotkey that is never pressed (a setting the player cleared).</summary>
    public static KeyGesture None => default;

    /// <summary>The key's Input System name; empty for <see cref="None"/>.</summary>
    public string Key
    {
        get => field ?? "";
        init;
    }

    public KeyModifiers Modifiers { get; init; }

    /// <summary>Whether this is <see cref="None"/>.</summary>
    public bool IsNone => Key.Length == 0;

    /// <summary>
    /// A gesture from its text: modifiers and the key joined by <c>+</c> (<c>Ctrl+Shift+F8</c>, <c>Alt+A</c>, <c>F8</c>);
    /// empty text or <c>None</c> is <see cref="None"/>. Throws <see cref="FormatException"/> on anything else.
    /// </summary>
    public static KeyGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a key gesture such as Ctrl+Shift+F8");

    /// <summary><see cref="Parse"/> without throwing.</summary>
    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        gesture = None;
        if (text == null) return false;
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = trimmed.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var modifier = Array.Find(s_modifierNames, m => m.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase)).Modifier;
            if (modifier == KeyModifiers.None) return false;
            modifiers |= modifier;
        }
        string key = parts[^1];
        if (key.Length == 0 || IsModifierName(key)) return false;
        gesture = new KeyGesture(key, modifiers);
        return true;
    }

    /// <summary>The gesture as <see cref="Parse"/> reads it: <c>Ctrl+Shift+F8</c>; <c>None</c> for <see cref="None"/>.</summary>
    public override string ToString()
    {
        if (IsNone) return "None";
        var parts = new List<string>(4);
        if ((Modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>Whether two gestures are the same keys: the key name's case does not matter.</summary>
    public bool Equals(KeyGesture other) => Modifiers == other.Modifiers && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Modifiers, StringComparer.OrdinalIgnoreCase.GetHashCode(Key));

    private static bool IsModifierName(string name) => Array.Exists(s_modifierNames, m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
