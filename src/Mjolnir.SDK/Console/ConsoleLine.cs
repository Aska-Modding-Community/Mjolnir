namespace Mjolnir.SDK;

/// <summary>What a console line is: the player's command, an answer, an error.</summary>
internal enum ConsoleLineKind
{
    Input,
    Output,
    Error,
}

internal readonly record struct ConsoleLine(string Text, ConsoleLineKind Kind);
