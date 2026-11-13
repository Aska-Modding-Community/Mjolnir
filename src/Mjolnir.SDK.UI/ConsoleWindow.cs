using System;
using System.Linq;
using System.Text;
using System.Threading;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The window of the SDK's <see cref="DevConsole"/>: its lines, the newest in view, and a line to type commands into
/// (Enter runs one, Up and Down go through the ones typed before). The platform's setting <c>[Console] Key</c>
/// (<c>`</c> by default, on the Mjolnir window's settings page) opens and closes it, from the console's own field too
/// when the key types nothing a command needs (<see cref="ConsoleKeys"/>); Esc closes it too.
/// </summary>
internal static class ConsoleWindow
{
    // The lines shown, newest last: a text the game's text component draws in one piece.
    private const int ShownLines = 150;
    private const string InputColor = "#FFB31CFF"; // the amber of the game's highlights
    private const string ErrorColor = "#FF6A4DFF";

    private static ConfigEntry<KeyGesture>? s_key;
    private static UiWindow? s_window;
    private static UiTextBlock? s_text;
    private static ConsoleInput? s_input;
    private static int s_refreshQueued;
    private static int s_closedByKey = -1;

    /// <summary>The key that opens the console, the platform's setting.</summary>
    public static ConfigEntry<KeyGesture> KeyEntry => LazyInitializer.EnsureInitialized(ref s_key, () =>
        PlatformConfig.Config.Bind("Console", "Key", KeyGesture.Parse("Backquote"), "Opens and closes the developer console."));

    /// <summary>The module's start (phase A): the key, and the window following the console's lines.</summary>
    public static void Install()
    {
        HotKeys.Add(KeyEntry, Toggle);
        DevConsole.LinesChanged += () =>
        {
            if (Interlocked.Exchange(ref s_refreshQueued, 1) == 0) Dispatcher.Enqueue(Refresh, "Console window");
        };
        GameLoop.Update += Tick;
    }

    /// <summary>Opens the console, or closes it if it is open (main thread: the key's handler).</summary>
    public static void Toggle()
    {
        // The field gave up its focus as the key closed the console this frame: the hotkey, polled after it, does not
        // open it again (or close it twice).
        if (UnityEngine.Time.frameCount == s_closedByKey) return;
        if (s_window is { IsOpen: true })
        {
            s_window.Close();
            return;
        }
        s_window ??= Build();
        s_window.Show();
    }

    /// <summary>A console line as the window draws it: the player's commands in amber after "> ", errors in red.</summary>
    internal static string Format(ConsoleLine line)
    {
        string text = $"<noparse>{line.Text.Replace("</noparse>", "", StringComparison.OrdinalIgnoreCase)}</noparse>";
        return line.Kind switch
        {
            ConsoleLineKind.Input => $"<color={InputColor}>> {text}</color>",
            ConsoleLineKind.Error => $"<color={ErrorColor}>{text}</color>",
            _ => text,
        };
    }

    /// <summary>The window's text: the newest <see cref="ShownLines"/> lines.</summary>
    internal static string Text(System.Collections.Generic.IReadOnlyList<ConsoleLine> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - ShownLines)))
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(Format(line));
        }
        return text.ToString();
    }

    private static UiWindow Build()
    {
        s_text = new UiTextBlock { Name = "Lines", TextAlignment = TextAlignment.Left, FontSize = 15 };
        s_input = new ConsoleInput(SdkTexts.Get("console.placeholder"));
        var window = new UiWindow
        {
            Title = SdkTexts.Get("console.title"),
            ObjectName = "Console",
            WindowStyle = WindowStyle.Dialog,
            Width = 1000,
            Height = 480,
            Content = s_text,
            Footer = new UiUnityHost(s_input.Build) { Name = "Input", Height = 40 },
            Draggable = true,
            DragEnded = s_input.Focus,
        };
        window.Closed += (_, _) => s_input.LetGo();
        window.Opened += (_, _) =>
        {
            Refresh();
            s_input.Focus();
        };
        return window;
    }

    private static void Refresh()
    {
        Volatile.Write(ref s_refreshQueued, 0);
        if (s_text == null) return;
        s_text.Text = Text(DevConsole.Lines);
        s_window?.ScrollToEnd();
    }

    private static void Tick()
    {
        if (s_window is not { IsOpen: true } window || s_input == null) return;
        bool close = false;
        GameUi.Guard("Console input", () => close = s_input.Tick(CloseKeyPressed));
        if (!close) return;
        s_closedByKey = UnityEngine.Time.frameCount;
        window.Close();
    }

    // The console's key, pressed while the player types in the console's field (where hotkeys wait).
    private static bool CloseKeyPressed() =>
        HotKeys.Keyboard is { } keyboard && ConsoleKeys.ClosesWhileTyping(KeyEntry.Value) && HotKeys.IsPressed(keyboard, KeyEntry.Value);
}
