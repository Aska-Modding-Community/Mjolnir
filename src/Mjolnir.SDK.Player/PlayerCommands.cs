using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The player module's console commands (<see cref="DevConsole"/>): <c>pos</c> shows where the player stands, <c>tp</c>
/// moves them, onto the ground at <c>x z</c> or exactly to <c>x y z</c>. A coordinate <c>~</c> is the player's own,
/// <c>~10</c> ten metres from it.
/// </summary>
internal static class PlayerCommands
{
    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(PlayerCommands).Assembly;
        DevConsole.AddFor(platform, "pos", "Shows where the player stands (x y z, y up).", Pos);
        DevConsole.AddFor(platform, "tp", "Teleports the player: onto the ground at x z, or exactly to x y z. ~ is the player's own coordinate, ~10 ten metres from it.", Tp, "<x> [y] <z>");
    }

    /// <summary>A coordinate typed into <c>tp</c>: a number, or <c>~</c> and an optional distance from <paramref name="own"/>.</summary>
    internal static float Coordinate(string word, float own)
    {
        if (word.StartsWith('~'))
        {
            if (word.Length == 1) return own;
            if (DevCommandContext.TryNumber(word[1..], out float offset)) return own + offset;
        }
        else if (DevCommandContext.TryNumber(word, out float value)) return value;
        throw new DevCommandException($"Not a coordinate: '{word}'.");
    }

    internal static string Format(float x, float y, float z) =>
        string.Create(CultureInfo.InvariantCulture, $"{x:0.0} {y:0.0} {z:0.0}");

    private static void Pos(DevCommandContext context)
    {
        if (!LocalPlayer.IsSpawned)
        {
            context.Error("No player in the world.");
            return;
        }
        var (x, y, z) = LocalPlayer.PositionNow;
        context.Print($"Position {Format(x, y, z)}");
    }

    private static void Tp(DevCommandContext context)
    {
        if (context.Arguments.Count is not (2 or 3)) throw new DevCommandException("tp takes x z, or x y z.");
        if (!LocalPlayer.IsSpawned)
        {
            context.Error("No player in the world.");
            return;
        }
        var (ownX, ownY, ownZ) = LocalPlayer.PositionNow;
        float x = Coordinate(context.Arguments[0], ownX);
        float z = Coordinate(context.Arguments[^1], ownZ);
        if (context.Arguments.Count == 3)
        {
            float y = Coordinate(context.Arguments[1], ownY);
            LocalPlayer.TeleportTo(x, y, z);
            context.Print($"Teleported to {Format(x, y, z)}");
            return;
        }
        context.Print(string.Create(CultureInfo.InvariantCulture, $"Teleporting onto the ground at {x:0.0} {z:0.0}"));
        _ = Land(x, z);
    }

    // The answer comes once the world there has streamed in (on the main thread: the SDK's synchronization context). The
    // place is where the character was sent: it stands there from the game's next physics step.
    private static async Task Land(float x, float z)
    {
        if (await LocalPlayer.LandAsync(x, z) is { } landed) DevConsole.Print($"On the ground at {Format(landed.X, landed.Y, landed.Z)}");
        else DevConsole.AddLine(string.Create(CultureInfo.InvariantCulture, $"No ground found at {x:0.0} {z:0.0}."), ConsoleLineKind.Error);
    }
}
