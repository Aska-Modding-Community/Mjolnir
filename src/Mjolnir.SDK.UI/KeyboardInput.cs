using System;
using System.Collections.Generic;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The keyboard, read wherever the player is: which keys are held, went down or came up this frame. Keys are named as in a
/// <see cref="KeyGesture"/> (Unity's Input System names: "W", "LeftShift", "Digit1", "F7", "Escape"). For a key that runs
/// your code when pressed, <see cref="HotKeys"/> is simpler; this is for keys held over time, such as moving a camera of
/// your own. Read it on the main thread (in <c>OnUpdate</c>), elsewhere every key is up. It reads the device itself, so it
/// answers while the game's controls are captured (<see cref="PlayerControls"/>).
/// </summary>
/// <example><code>
/// if (!KeyboardInput.IsTyping)
/// {
///     if (KeyboardInput.IsPressed("W")) pivot += forward * speed * Time.deltaTime;
///     bool fast = (KeyboardInput.Modifiers &amp; KeyModifiers.Shift) != 0;
/// }
/// </code></example>
public static class KeyboardInput
{
    private static readonly HashSet<string> s_warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the game has a keyboard.</summary>
    public static bool IsAvailable => Live() != null;

    /// <summary>The modifiers held now (Ctrl, Shift, Alt, either side).</summary>
    public static KeyModifiers Modifiers => Live() is { } source ? source.Modifiers : KeyModifiers.None;

    /// <summary>Whether the player types in a text field now: their keys are meant for it.</summary>
    public static bool IsTyping => Live() is { } source && source.IsTyping;

    /// <summary>Whether the key is held down now.</summary>
    /// <param name="key">The key's name; a name that is no key is never held (and is logged once).</param>
    public static bool IsPressed(string key) => Read(key, (source, k) => source.IsHeld(k));

    /// <summary>Whether the key went down this frame.</summary>
    /// <param name="key">The key's name; a name that is no key never goes down (and is logged once).</param>
    public static bool WasPressed(string key) => Read(key, (source, k) => source.WasPressed(k));

    /// <summary>Whether the key came up this frame.</summary>
    /// <param name="key">The key's name; a name that is no key never comes up (and is logged once).</param>
    public static bool WasReleased(string key) => Read(key, (source, k) => source.WasReleased(k));

    private static bool Read(string key, Func<IKeyboardSource, string, bool?> read)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Live() is not { } source) return false;
        bool? state = read(source, key);
        if (state != null) return state.Value;
        bool first;
        lock (s_warned) first = s_warned.Add(key);
        if (first) SdkLog.Warn(GameUi.Source, $"KeyboardInput: '{key}' is not a key of Unity's Input System");
        return false;
    }

    // The keyboard HotKeys read too: the game's, once it is ready, or a fake in unit tests.
    private static IKeyboardSource? Live()
    {
        if (!Dispatcher.IsMainThread) return null;
        if (HotKeys.Keyboard == null && GameLoop.IsGameReady)
        {
            try
            {
                HotKeys.Keyboard = GameKeyboard.Create();
            }
            catch (Exception e)
            {
                SdkLog.Error(GameUi.Source, $"KeyboardInput: reading the keyboard failed: {e}");
            }
        }
        return HotKeys.Keyboard;
    }
}
