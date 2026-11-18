using System;
using System.Collections.Generic;
using System.IO;
using Mjolnir.SDK.Config;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>A keyboard for the tests: the keys "pressed this frame", the modifiers held, whether a text field has the focus.</summary>
internal sealed class FakeKeyboard : IKeyboardSource
{
    public HashSet<string> Pressed { get; } = new(StringComparer.OrdinalIgnoreCase);
    public KeyModifiers Modifiers { get; set; }
    public bool IsTyping { get; set; }

    public bool? WasPressed(string key) => key.Equals("NoSuchKey", StringComparison.OrdinalIgnoreCase) ? null : Pressed.Contains(key);

    public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Released { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool? IsHeld(string key) => key.Equals("NoSuchKey", StringComparison.OrdinalIgnoreCase) ? null : Held.Contains(key);

    public bool? WasReleased(string key) => key.Equals("NoSuchKey", StringComparison.OrdinalIgnoreCase) ? null : Released.Contains(key);

    public string? FirstPressed()
    {
        foreach (string key in Pressed) return key;
        return null;
    }

    public void Press(string key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Pressed.Clear();
        Pressed.Add(key);
        Modifiers = modifiers;
        HotKeys.Poll(this);
        Pressed.Clear();
    }
}

/// <summary>
/// HotKeys on a fake keyboard: a gesture fires with exactly its modifiers, not while the player types; a bindable
/// gesture follows its source; removing stops it; a throwing handler stops no other; and UiHotKeyBox records the next
/// key (Esc cancels, Backspace clears) without firing hotkeys that frame. A KeyGesture setting gets a UiHotKeyBox.
/// </summary>
[Collection("MainThread")]
public sealed class HotKeysTests : ViewTestBase, IDisposable
{
    private readonly FakeKeyboard _keyboard = new();

    public HotKeysTests() => HotKeys.ResetForTests();

    void IDisposable.Dispose()
    {
        HotKeys.ResetForTests();
        Dispose();
    }

    [Fact]
    public void AGestureFiresWithExactlyItsModifiers()
    {
        int plain = 0, ctrl = 0;
        using var a = HotKeys.Add(KeyGesture.Parse("F8"), () => plain++);
        using var b = HotKeys.Add(KeyGesture.Parse("Ctrl+F8"), () => ctrl++);

        _keyboard.Press("F8");
        _keyboard.Press("F8", KeyModifiers.Control);
        _keyboard.Press("F8", KeyModifiers.Control | KeyModifiers.Shift);
        _keyboard.Press("F9");
        Assert.Equal((1, 1), (plain, ctrl));
    }

    [Fact]
    public void NothingFiresWhileThePlayerTypes()
    {
        int fired = 0;
        using var hotkey = HotKeys.Add(KeyGesture.Parse("F"), () => fired++);
        _keyboard.IsTyping = true;
        _keyboard.Press("F");
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ABindableGestureFollowsItsSourceAndRemovingStopsIt()
    {
        int fired = 0;
        var gesture = new BindableValue<KeyGesture>(KeyGesture.Parse("F8"));
        var hotkey = HotKeys.Add(gesture, () => fired++);
        _keyboard.Press("F8");
        gesture.Value = KeyGesture.Parse("F9");
        _keyboard.Press("F8");
        _keyboard.Press("F9");
        gesture.Value = KeyGesture.None;
        _keyboard.Press("F9");
        hotkey.Dispose();
        gesture.Value = KeyGesture.Parse("F8");
        _keyboard.Press("F8");
        Assert.Equal(2, fired);
    }

    [Fact]
    public void AThrowingHandlerStopsNoOtherAndAKeyThatIsNoKeyNeverFires()
    {
        int fired = 0;
        using var broken = HotKeys.Add(KeyGesture.Parse("F8"), () => throw new InvalidOperationException("a broken mod"));
        using var other = HotKeys.Add(KeyGesture.Parse("F8"), () => fired++);
        using var unknown = HotKeys.Add(KeyGesture.Parse("NoSuchKey"), () => fired += 100);
        _keyboard.Press("F8");
        _keyboard.Press("NoSuchKey");
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ABoxRecordsTheNextKeyWithItsModifiersWithoutFiringIt()
    {
        int fired = 0;
        using var hotkey = HotKeys.Add(KeyGesture.Parse("Ctrl+K"), () => fired++);
        var seen = new List<KeyGesture>();
        var box = new UiHotKeyBox { Header = "Toggle", Gesture = KeyGesture.Parse("F8") };
        Mount(box);
        box.GestureChanged += (_, e) => seen.Add(e.NewValue);
        Assert.Equal("F8", box.ShownText);

        box.StartRecording();
        Assert.True(box.IsRecording);
        Assert.Equal("Press a key…", box.ShownText);
        _keyboard.Press("K", KeyModifiers.Control);
        Assert.False(box.IsRecording);
        Assert.Equal(KeyGesture.Parse("Ctrl+K"), box.Gesture);
        Assert.Equal(0, fired); // the key that set the gesture
        Assert.Equal([KeyGesture.Parse("Ctrl+K")], seen);

        _keyboard.Press("K", KeyModifiers.Control);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ABoxThatLeftTheScreenStopsRecording()
    {
        var box = new UiHotKeyBox { Gesture = KeyGesture.Parse("F8") };
        Mount(box);
        box.StartRecording();
        Host.Active = false; // its window closed by a click
        _keyboard.Press("W");
        Assert.False(box.IsRecording);
        Assert.Equal(KeyGesture.Parse("F8"), box.Gesture);
    }

    [Fact]
    public void EscCancelsAndBackspaceClears()
    {
        var box = new UiHotKeyBox { Gesture = KeyGesture.Parse("F8") };
        Mount(box);
        box.StartRecording();
        _keyboard.Press("Escape");
        Assert.False(box.IsRecording);
        Assert.Equal(KeyGesture.Parse("F8"), box.Gesture);

        box.StartRecording();
        _keyboard.Press("Backspace");
        Assert.True(box.Gesture.IsNone);
        Assert.Equal("None", box.ShownText);
    }

    [Fact]
    public void AGestureSettingGetsAHotKeyBoxBoundBothWays()
    {
        var config = ModConfig.Load(Path.Combine(Path.GetTempPath(), "mjolnir-ui-tests", Guid.NewGuid().ToString("N") + ".toml"), "Test", _ => { });
        var entry = config.Bind("Keys", "Toggle", KeyGesture.Parse("F8"));
        var box = Assert.IsType<UiHotKeyBox>(SettingControls.For(entry));
        Mount(box);
        Assert.Equal(KeyGesture.Parse("F8"), box.Gesture);

        box.StartRecording();
        _keyboard.Press("F9", KeyModifiers.Alt);
        Assert.Equal(KeyGesture.Parse("Alt+F9"), entry.Value);
        Assert.Equal(SettingKind.HotKey, SettingKinds.Of(entry));
    }
}
