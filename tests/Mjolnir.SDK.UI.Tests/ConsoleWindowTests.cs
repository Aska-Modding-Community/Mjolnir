using System.Linq;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// The console window's model: how a line is drawn (the player's commands in amber, errors in red, a line's own tags
/// shown as typed), only the newest lines in the text, the commands typed before for Up and Down, and which console keys
/// close the console from its own field.
/// </summary>
public sealed class ConsoleWindowTests
{
    [Fact]
    public void ALineIsDrawnByWhatItIs()
    {
        Assert.Equal("<color=#FFB31CFF>> <noparse>give rope</noparse></color>", ConsoleWindow.Format(new ConsoleLine("give rope", ConsoleLineKind.Input)));
        Assert.Equal("<noparse>Gave 1</noparse>", ConsoleWindow.Format(new ConsoleLine("Gave 1", ConsoleLineKind.Output)));
        Assert.Equal("<color=#FF6A4DFF><noparse>No item</noparse></color>", ConsoleWindow.Format(new ConsoleLine("No item", ConsoleLineKind.Error)));
    }

    [Fact]
    public void ALinesOwnTagsAreShownAsTyped() =>
        Assert.Equal("<noparse><b>x</b></noparse>", ConsoleWindow.Format(new ConsoleLine("<b>x</noparse></b>", ConsoleLineKind.Output)));

    [Fact]
    public void TheTextHoldsTheNewestLines()
    {
        var lines = Enumerable.Range(0, 400).Select(i => new ConsoleLine(i.ToString(), ConsoleLineKind.Output)).ToList();

        string[] shown = ConsoleWindow.Text(lines).Split('\n');

        Assert.Equal(150, shown.Length);
        Assert.Equal("<noparse>250</noparse>", shown[0]);
        Assert.Equal("<noparse>399</noparse>", shown[^1]);
    }

    [Fact]
    public void UpAndDownGoThroughTheCommandsTypedBefore()
    {
        var history = new CommandHistory();
        Assert.Equal("help", history.Add("  help "));
        history.Add("give rope 3");
        history.Add("give rope 3");
        Assert.Null(history.Add("   "));

        Assert.Equal("give rope 3", history.Back("tp 1"));
        Assert.Equal("help", history.Back(""));
        Assert.Null(history.Back(""));
        Assert.Equal("give rope 3", history.Forward());
        Assert.Equal("tp 1", history.Forward());
        Assert.Null(history.Forward());
    }

    [Theory]
    [InlineData("Backquote", true)]
    [InlineData("Shift+Backquote", true)]
    [InlineData("F1", true)]
    [InlineData("F12", true)]
    [InlineData("Ctrl+K", true)]
    [InlineData("Alt+Digit1", true)]
    [InlineData("K", false)]
    [InlineData("Shift+K", false)]
    [InlineData("Space", false)]
    [InlineData("Digit1", false)]
    [InlineData("Fire", false)]
    [InlineData("None", false)]
    public void TheConsolesKeyClosesItFromItsFieldWhenItTypesNothingACommandNeeds(string gesture, bool closes) =>
        Assert.Equal(closes, ConsoleKeys.ClosesWhileTyping(KeyGesture.Parse(gesture)));

    [Fact]
    public void AGestureIsPressedWithExactlyItsModifiers()
    {
        var keyboard = new FakeKeyboard();
        keyboard.Pressed.Add("Backquote");

        Assert.True(HotKeys.IsPressed(keyboard, KeyGesture.Parse("Backquote")));
        keyboard.Modifiers = KeyModifiers.Shift;
        Assert.False(HotKeys.IsPressed(keyboard, KeyGesture.Parse("Backquote")));
        Assert.True(HotKeys.IsPressed(keyboard, KeyGesture.Parse("Shift+Backquote")));
        Assert.False(HotKeys.IsPressed(keyboard, KeyGesture.None));
        Assert.False(HotKeys.IsPressed(keyboard, KeyGesture.Parse("NoSuchKey")));
    }

    [Fact]
    public void DownComesBackToWhatWasBeingTyped()
    {
        var history = new CommandHistory();
        history.Add("pos");

        Assert.Equal("pos", history.Back("tp 10"));
        Assert.Equal("tp 10", history.Forward());
    }
}
