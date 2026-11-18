using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Keys that run your code wherever the player is: in the world, in the game's menus, in your windows. Not while the
/// player types in a text field, and not while a <see cref="UiHotKeyBox"/> records a new key. A gesture matches only
/// with exactly its modifiers held: a Ctrl+F8 hotkey does not fire on F8, nor F8 on Ctrl+F8.
/// </summary>
/// <example><code>
/// var toggle = Config.Bind("Keys", "ToggleWindow", KeyGesture.Parse("F8"), "Opens and closes the window.");
/// HotKeys.Add(toggle, () => window.Show()); // follows the setting when the player rebinds it in the Mods window
/// </code></example>
public static class HotKeys
{
    private static readonly object s_lock = new();
    private static readonly HashSet<string> s_warned = new(StringComparer.OrdinalIgnoreCase);
    private static Registration[] s_registrations = [];
    private static Recording? s_recording;
    private static int s_ticking;

    /// <summary>Calls <paramref name="pressed"/> on the main thread whenever <paramref name="gesture"/> is pressed. Dispose the result to stop.</summary>
    public static IDisposable Add(KeyGesture gesture, Action pressed) => Register(() => gesture, pressed);

    /// <summary>
    /// The same for a gesture that can change, such as a config setting the player rebinds in the Mods window: the key
    /// pressed is the setting's value at that moment.
    /// </summary>
    public static IDisposable Add(IReadOnlyBindable<KeyGesture> gesture, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        return Register(() => gesture.Value, pressed);
    }

    /// <summary>The keyboard read every frame (the game's; a fake in unit tests).</summary>
    internal static IKeyboardSource? Keyboard { get; set; }

    /// <summary>
    /// Records the next key the player presses (a <see cref="UiHotKeyBox"/> was clicked): Esc cancels, Backspace and
    /// Delete clear the gesture. A new recording cancels the one before, and so does its box leaving the screen
    /// (<paramref name="onScreen"/> false: its window closed by a click), so a later key never rebinds it unseen.
    /// </summary>
    internal static void Record(Action<KeyGesture> recorded, Action cancelled, Func<bool> onScreen)
    {
        EnsureTicking();
        Recording? previous;
        lock (s_lock)
        {
            previous = s_recording;
            s_recording = new Recording(recorded, cancelled, onScreen);
        }
        previous?.Cancelled();
    }

    /// <summary>Stops a recording without a key (its box went away).</summary>
    internal static void StopRecording(Action cancelled)
    {
        lock (s_lock)
        {
            if (s_recording?.Cancelled != cancelled) return;
            s_recording = null;
        }
        cancelled();
    }

    /// <summary>
    /// One frame of the keyboard (main thread): a recording takes the first key pressed, else every gesture pressed now
    /// runs its handler, unless the player is typing.
    /// </summary>
    internal static void Poll(IKeyboardSource keyboard)
    {
        Recording? recording;
        lock (s_lock) recording = s_recording;
        if (recording != null && !recording.OnScreen())
        {
            lock (s_lock)
            {
                if (s_recording == recording) s_recording = null;
            }
            recording.Cancelled();
            recording = null;
        }
        if (recording != null)
        {
            string? key = keyboard.FirstPressed();
            if (key == null) return;
            lock (s_lock)
            {
                if (s_recording == recording) s_recording = null;
            }
            if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase)) recording.Cancelled();
            else if (key.Equals("Backspace", StringComparison.OrdinalIgnoreCase) || key.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                recording.Recorded(KeyGesture.None);
            else recording.Recorded(new KeyGesture(key, keyboard.Modifiers));
            return; // the key that set a gesture does not also fire it
        }
        if (keyboard.IsTyping) return;
        var modifiers = keyboard.Modifiers;
        foreach (var registration in Volatile.Read(ref s_registrations))
        {
            if (!ModCall.TryRun(registration.Owner, "HotKeys gesture", registration.Gesture, out var gesture) || gesture.IsNone) continue;
            if (gesture.Modifiers != modifiers) continue;
            bool? pressed = keyboard.WasPressed(gesture.Key);
            if (pressed == null)
            {
                bool first;
                lock (s_warned) first = s_warned.Add(gesture.Key);
                if (first) SdkLog.Warn(GameUi.Source, $"HotKeys: '{gesture.Key}' is not a key of Unity's Input System; {gesture} never fires");
                continue;
            }
            if (pressed.Value) ModCall.Run(registration.Owner, $"HotKeys {gesture}", registration.Pressed);
        }
    }

    /// <summary>Whether <paramref name="gesture"/> went down this frame with exactly its modifiers held.</summary>
    internal static bool IsPressed(IKeyboardSource keyboard, KeyGesture gesture) =>
        !gesture.IsNone && gesture.Modifiers == keyboard.Modifiers && keyboard.WasPressed(gesture.Key) == true;

    /// <summary>Unit tests: no gestures, no recording, no keyboard.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_registrations = [];
            s_recording = null;
        }
        Keyboard = null;
    }

    private static IDisposable Register(Func<KeyGesture> gesture, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        var registration = new Registration(gesture, pressed, pressed.Method.DeclaringType?.Assembly);
        lock (s_lock) s_registrations = [.. s_registrations, registration];
        EnsureTicking();
        return new Removal(registration);
    }

    private static void Remove(Registration registration)
    {
        lock (s_lock) s_registrations = Array.FindAll(s_registrations, r => r != registration);
    }

    // The first hotkey or recording starts the per-frame poll; in the game it reads the game's keyboard.
    private static void EnsureTicking()
    {
        if (Interlocked.Exchange(ref s_ticking, 1) == 1) return;
        GameUi.EnsureInstalled();
        Dispatcher.Hook();
        GameLoop.Update += Tick;
    }

    private static void Tick()
    {
        if (!GameLoop.IsGameReady) return;
        try
        {
            Keyboard ??= GameKeyboard.Create();
            if (Keyboard != null) Poll(Keyboard);
        }
        catch (Exception e)
        {
            Keyboard = null;
            SdkLog.Error(GameUi.Source, $"HotKeys: reading the keyboard failed: {e}");
        }
    }

    private sealed record Registration(Func<KeyGesture> Gesture, Action Pressed, Assembly? Owner);

    private sealed record Recording(Action<KeyGesture> Recorded, Action Cancelled, Func<bool> OnScreen);

    private sealed class Removal(Registration registration) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) Remove(registration);
        }
    }
}

/// <summary>The keyboard as <see cref="HotKeys"/> reads it once a frame (Unity/GameKeyboard.cs in the game, a fake in tests).</summary>
internal interface IKeyboardSource
{
    /// <summary>The modifiers held now.</summary>
    KeyModifiers Modifiers { get; }

    /// <summary>Whether a text field has the focus: hotkeys wait.</summary>
    bool IsTyping { get; }

    /// <summary>Whether the key (an Input System name) went down this frame; null for a name that is no key.</summary>
    bool? WasPressed(string key);

    /// <summary>Whether the key is held down now; null for a name that is no key.</summary>
    bool? IsHeld(string key);

    /// <summary>Whether the key came up this frame; null for a name that is no key.</summary>
    bool? WasReleased(string key);

    /// <summary>The name of a key other than a modifier that went down this frame, if any.</summary>
    string? FirstPressed();
}
