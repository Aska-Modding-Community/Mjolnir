using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mjolnir.SDK;

/// <summary>
/// One run of a command: the words typed after its name, and its answers in the console. The readers throw a
/// <see cref="DevCommandException"/> for a word that is missing or not a number: the console shows it with the command's
/// usage.
/// </summary>
public sealed class DevCommandContext
{
    internal DevCommandContext(DevCommand command, string line, IReadOnlyList<string> arguments)
    {
        Command = command;
        Line = line;
        Arguments = arguments;
    }

    public DevCommand Command { get; }

    /// <summary>The whole line as the player typed it.</summary>
    public string Line { get; }

    /// <summary>The words after the command's name (double quotes keep spaces in one).</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>An answer in the console.</summary>
    public void Print(string text) => DevConsole.AddLine(text ?? "", ConsoleLineKind.Output);

    /// <summary>An answer in the console's error colour (the command still counts as run).</summary>
    public void Error(string text) => DevConsole.AddLine(text ?? "", ConsoleLineKind.Error);

    /// <summary>The word at <paramref name="index"/> (0 is the first after the name).</summary>
    public string GetString(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : throw new DevCommandException($"Argument {index + 1} is missing.");

    /// <summary>The words from <paramref name="index"/> on, joined by spaces (a name of several words without quotes).</summary>
    public string GetRest(int index) =>
        index >= 0 && index < Arguments.Count ? string.Join(" ", Arguments.Skip(index)) : throw new DevCommandException($"Argument {index + 1} is missing.");

    /// <summary>The whole number at <paramref name="index"/>.</summary>
    public int GetInt(int index) =>
        int.TryParse(GetString(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new DevCommandException($"Argument {index + 1} is not a whole number: '{Arguments[index]}'.");

    /// <summary>The whole number at <paramref name="index"/>, or <paramref name="fallback"/> when there is none.</summary>
    public int GetInt(int index, int fallback) => index < Arguments.Count ? GetInt(index) : fallback;

    /// <summary>The number at <paramref name="index"/> (a point or a comma before the fraction).</summary>
    public float GetFloat(int index) =>
        TryNumber(GetString(index), out float value) ? value : throw new DevCommandException($"Argument {index + 1} is not a number: '{Arguments[index]}'.");

    /// <summary>The number at <paramref name="index"/>, or <paramref name="fallback"/> when there is none.</summary>
    public float GetFloat(int index, float fallback) => index < Arguments.Count ? GetFloat(index) : fallback;

    internal static bool TryNumber(string text, out float value)
    {
        if (!text.Contains('.')) text = text.Replace(',', '.');
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }
}
