using System.Collections.Generic;
using Mjolnir.SDK;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>A key hint's keys (UiKeyHint): Input System key names to the game's glyph sprites, and the text it shows.</summary>
[Collection("MainThread")]
public sealed class KeyHintTests : ViewTestBase
{
    // A few of KbdMouseGlyphs' sprite names, as the game has them.
    private static readonly HashSet<string> s_sprites = ["F7", "E", "1", "Numpad 1", "Space", "Left Shift", "Up Arrow", "`", "Control", "Shift", "Left Alt", "Left Button", "Right Button", "Middle Button"];

    [Theory]
    [InlineData("F7", "F7")]
    [InlineData("f12", "F12")]
    [InlineData("e", "E")]
    [InlineData("Digit1", "1")]
    [InlineData("Numpad1", "Numpad 1")]
    [InlineData("NumpadEnter", "Numpad Enter")]
    [InlineData("Space", "Space")]
    [InlineData("escape", "Escape")]
    [InlineData("LeftShift", "Left Shift")]
    [InlineData("LeftCtrl", "Left Control")]
    [InlineData("UpArrow", "Up Arrow")]
    [InlineData("PageDown", "Page Down")]
    [InlineData("Backquote", "`")]
    [InlineData("LeftWindows", "Left System")]
    public void AKeyNamesItsGlyphAsTheGameDoes(string key, string sprite) => Assert.Equal(sprite, KeyGlyphs.SpriteName(key));

    [Fact]
    public void AGestureShowsItsModifiersThenItsKeyAsPictures()
    {
        Assert.Equal("<sprite name=\"F7\">", KeyGlyphs.Text(KeyGesture.Parse("F7"), s_sprites.Contains));
        Assert.Equal("<sprite name=\"Control\"> + <sprite name=\"Shift\"> + <sprite name=\"E\">",
                     KeyGlyphs.Text(KeyGesture.Parse("Shift+Ctrl+E"), s_sprites.Contains));
        Assert.Equal("<sprite name=\"Left Alt\"> + F20", KeyGlyphs.Text(KeyGesture.Parse("Alt+F20"), s_sprites.Contains)); // no picture: written out
        Assert.Equal("", KeyGlyphs.Text(KeyGesture.None, s_sprites.Contains));
    }

    [Fact]
    public void TheMouseShowsItsButtonsAsPicturesAndItsWheelAsAWord()
    {
        Assert.Equal("<sprite name=\"Left Button\">", KeyGlyphs.Text(KeyGesture.Parse("LeftButton"), s_sprites.Contains));
        Assert.Equal("<sprite name=\"Shift\"> + <sprite name=\"Left Button\">", KeyGlyphs.Text(KeyGesture.Parse("Shift+LeftButton"), s_sprites.Contains));
        Assert.Equal("<sprite name=\"Middle Button\">", KeyGlyphs.Text(KeyGesture.Parse("MiddleButton"), s_sprites.Contains));
        Assert.Equal("<sprite name=\"Control\"> + <size=60%><voffset=0.4em>колесо</voffset></size>", KeyGlyphs.Text(KeyGesture.Parse("Ctrl+Wheel"), s_sprites.Contains, "колесо"));
        Assert.Equal("<size=60%><voffset=0.4em>Wheel</voffset></size>", KeyGlyphs.Text(KeyGesture.Parse("Wheel"), s_sprites.Contains));
    }

    [Fact]
    public void AHintsGestureAndTextMarkTheirParts()
    {
        var hint = new UiKeyHint { Gesture = KeyGesture.Parse("F7"), Text = "Open" };
        var view = Mount(hint);
        int value = view.AppliedCount(DirtyFlags.Value), text = view.AppliedCount(DirtyFlags.Text);
        var key = new BindableValue<KeyGesture>(KeyGesture.Parse("F8"));
        hint.Bind(UiKeyHint.GestureProperty, key); // a hotkey setting: the hint follows a rebinding
        key.Value = KeyGesture.Parse("Ctrl+F8");
        hint.Text = "Close";
        Assert.Equal(KeyGesture.Parse("Ctrl+F8"), hint.Gesture);
        Assert.True(view.AppliedCount(DirtyFlags.Value) > value);
        Assert.Equal(text + 1, view.AppliedCount(DirtyFlags.Text));
    }
}
