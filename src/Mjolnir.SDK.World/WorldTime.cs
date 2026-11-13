using System;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.World;

/// <summary>
/// The clock of the loaded world. An hour runs from 0 (midnight) up to 24; the game's villagers follow it through their
/// schedules, so setting it to a working hour sends them to work. The clock is the host's: a client's change goes to the
/// host through the game's own message and comes back with the world. Read it on the main thread; another thread gets
/// the value last seen there, and its changes run on the next frame.
/// </summary>
/// <example><code>
/// if (WorldTime.IsNight) WorldTime.Hour = 9;
/// </code></example>
public static class WorldTime
{
    private const float HoursInADay = 24f;
    private static float s_hour;
    private static bool s_isNight;
    private static int s_daysPassed;
    private static bool s_isRunning;

    /// <summary>The game's clock: the game's, set by the module's start; a fake in the unit tests.</summary>
    internal static IClockSource? Clock { get; set; }

    /// <summary>Whether a world is loaded, so the clock can be read and set. False in the main menu.</summary>
    public static bool IsAvailable => Live() != null;

    /// <summary>
    /// The hour of the day, from 0 up to 24 (9.5 is half past nine). Setting it moves the clock at once and keeps the day;
    /// an hour outside the day wraps around it, as a clock does (25 is 1). Without a world it does nothing and reads 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a number.</exception>
    public static float Hour
    {
        get
        {
            if (Live() is { } clock) s_hour = clock.Hour;
            return s_hour;
        }
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "An hour is a number.");
            float hour = Wrap(value);
            Dispatcher.Run(() => Live()?.SetHour(hour));
        }
    }

    /// <summary>Whether it is night in the world, as the game counts it.</summary>
    public static bool IsNight
    {
        get
        {
            if (Live() is { } clock) s_isNight = clock.IsNight;
            return s_isNight;
        }
    }

    /// <summary>The days that passed in this world since it began.</summary>
    public static int DaysPassed
    {
        get
        {
            if (Live() is { } clock) s_daysPassed = clock.DaysPassed;
            return s_daysPassed;
        }
    }

    /// <summary>
    /// Whether the clock runs. Stopping it holds the hour, the sun and the villagers' schedules where they are; nothing
    /// else in the world stops. Set it back to true when done.
    /// </summary>
    public static bool IsRunning
    {
        get
        {
            if (Live() is { } clock) s_isRunning = clock.IsRunning;
            return s_isRunning;
        }
        set => Dispatcher.Run(() => Live()?.SetRunning(value));
    }

    /// <summary>An hour as a clock shows it: inside the day.</summary>
    internal static float Wrap(float hour)
    {
        float wrapped = hour % HoursInADay;
        return wrapped < 0 ? wrapped + HoursInADay : wrapped;
    }

    private static IClockSource? Live() => Dispatcher.IsMainThread && Clock is { IsAvailable: true } clock ? clock : null;

    /// <summary>Forgets the clock and what it last showed, between unit tests.</summary>
    internal static void ResetForTests()
    {
        Clock = null;
        s_hour = 0;
        s_isNight = false;
        s_daysPassed = 0;
        s_isRunning = false;
    }
}
