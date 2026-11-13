using System;
using System.Globalization;

namespace Mjolnir.SDK.World;

/// <summary>
/// The world module's console command (<see cref="DevConsole"/>): <c>time</c> shows the clock; with a word or an hour it
/// sets it, or stops and starts it.
/// </summary>
internal static class WorldCommands
{
    /// <summary>The hour of <c>time day</c>: the villagers' schedules are at work.</summary>
    public const float DayHour = 9f;

    /// <summary>The hour of <c>time night</c>: their schedules are asleep.</summary>
    public const float NightHour = 23f;

    // The platform's own command, whichever mod's code happens to be running.
    public static void Register() =>
        DevConsole.AddFor(typeof(WorldCommands).Assembly, "time", "Shows the world's clock, sets the hour, stops it or runs it again.", Time, "[day|night|stop|run|<hour>|<hh:mm>]");

    private static void Time(DevCommandContext context)
    {
        if (!WorldTime.IsAvailable)
        {
            context.Error("No world loaded.");
            return;
        }
        if (context.Arguments.Count > 0) Apply(context, context.GetString(0));
        else Show(context, WorldTime.Hour);
    }

    private static void Apply(DevCommandContext context, string word)
    {
        switch (word.ToLowerInvariant())
        {
            case "day":
                Set(context, DayHour);
                break;
            case "night":
                Set(context, NightHour);
                break;
            case "stop":
                WorldTime.IsRunning = false;
                context.Print("The clock stopped.");
                break;
            case "run":
                WorldTime.IsRunning = true;
                context.Print("The clock runs.");
                break;
            default:
                Set(context, ParseHour(word) ?? throw new DevCommandException($"'{word}' is no hour: 9, 14.5 or 14:30."));
                break;
        }
    }

    // The clock shows the new hour only once the game has applied it, so the answer says the hour asked for.
    private static void Set(DevCommandContext context, float hour)
    {
        WorldTime.Hour = hour;
        Show(context, hour);
    }

    private static void Show(DevCommandContext context, float hour)
    {
        string running = WorldTime.IsRunning ? "" : ", stopped";
        context.Print($"Day {WorldTime.DaysPassed + 1}, {Format(hour)}{running}.");
    }

    /// <summary>An hour typed as 14, 14.5 or 14:30; null for anything else.</summary>
    public static float? ParseHour(string text)
    {
        int colon = text.IndexOf(':');
        if (colon < 0)
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float hour) && hour >= 0 && hour <= 24 ? WorldTime.Wrap(hour) : null;
        return int.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) &&
               int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) &&
               hours < 24 && minutes < 60
            ? hours + minutes / 60f
            : null;
    }

    /// <summary>An hour as a clock shows it: 14:30.</summary>
    public static string Format(float hour)
    {
        int minutes = (int)MathF.Round(WorldTime.Wrap(hour) * 60f) % (24 * 60);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
    }
}
