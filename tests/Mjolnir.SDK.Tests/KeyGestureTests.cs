using System;
using System.Collections.Generic;
using System.IO;
using Mjolnir.SDK.Config;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>A key with its modifiers: reading and writing its text, None, equality, and as a config setting.</summary>
public class KeyGestureTests
{
    [Theory]
    [InlineData("F8", "F8", KeyModifiers.None)]
    [InlineData("Ctrl+Shift+F8", "F8", KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(" control + alt + a ", "a", KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("Shift+Backquote", "Backquote", KeyModifiers.Shift)]
    public void AGestureIsReadFromItsText(string text, string key, KeyModifiers modifiers)
    {
        var gesture = KeyGesture.Parse(text);
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("  none ")]
    public void EmptyTextOrNoneIsNone(string text)
    {
        Assert.True(KeyGesture.TryParse(text, out var gesture));
        Assert.True(gesture.IsNone);
        Assert.Equal(KeyGesture.None, gesture);
    }

    [Theory]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]
    [InlineData("Hyper+F8")]
    public void OtherTextIsNoGesture(string text)
    {
        Assert.False(KeyGesture.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyGesture.Parse(text));
    }

    [Fact]
    public void AGestureWritesTheTextItIsReadFrom()
    {
        Assert.Equal("Ctrl+Shift+Alt+F8", new KeyGesture("F8", KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Control).ToString());
        Assert.Equal("None", KeyGesture.None.ToString());
        Assert.Equal(KeyGesture.Parse("Ctrl+F8"), KeyGesture.Parse(KeyGesture.Parse("Ctrl+F8").ToString()));
    }

    [Fact]
    public void TheKeysCaseDoesNotMatterAndNoneHasNoModifiers()
    {
        Assert.Equal(KeyGesture.Parse("Ctrl+f8"), KeyGesture.Parse("ctrl+F8"));
        Assert.Equal(KeyGesture.Parse("Ctrl+f8").GetHashCode(), KeyGesture.Parse("ctrl+F8").GetHashCode());
        Assert.NotEqual(KeyGesture.Parse("F8"), KeyGesture.Parse("Shift+F8"));
        Assert.Equal(KeyModifiers.None, new KeyGesture("", KeyModifiers.Control).Modifiers);
        Assert.Equal("", default(KeyGesture).Key);
    }

    [Fact]
    public void AGestureSettingIsWrittenAsItsTextAndReadBack()
    {
        string path = Path.Combine(Path.GetTempPath(), "mjolnir-key-tests", Guid.NewGuid().ToString("N") + ".toml");
        try
        {
            var config = ModConfig.Load(path, "Test", _ => { });
            var toggle = config.Bind("Keys", "Toggle", KeyGesture.Parse("F8"), "Opens the window.");
            toggle.Value = KeyGesture.Parse("Ctrl+Shift+F9");
            config.SaveIfChanged();
            string text = File.ReadAllText(path);
            Assert.Contains("Toggle = \"Ctrl+Shift+F9\"", text);
            Assert.Contains("# Default: \"F8\"", text);

            var warnings = new List<string>();
            var again = ModConfig.Load(path, "Test", warnings.Add).Bind("Keys", "Toggle", KeyGesture.Parse("F8"));
            Assert.Equal(KeyGesture.Parse("Ctrl+Shift+F9"), again.Value);
            Assert.Empty(warnings);

            File.WriteAllText(path, "[Keys]\nToggle = \"Hyper+F8\"\n");
            var broken = ModConfig.Load(path, "Test", warnings.Add).Bind("Keys", "Toggle", KeyGesture.Parse("F8"));
            Assert.Equal(KeyGesture.Parse("F8"), broken.Value);
            Assert.Contains(warnings, w => w.Contains("not a valid KeyGesture"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
