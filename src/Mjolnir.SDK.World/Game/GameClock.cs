using System;
using Mjolnir.SDK.Threading;
using SSSGame.Weather;

namespace Mjolnir.SDK.World;

/// <summary>
/// The model's view of the game's clock, its weather system. A change goes through the game's own
/// messages, so it reaches the host from a client too.
/// </summary>
internal sealed class GameClock : IClockSource
{
    private const string Source = "SDK.World";

    /// <summary>Connects <see cref="WorldTime"/> to the game (the module's start, phase A: no Unity objects yet).</summary>
    public static void Connect() => WorldTime.Clock = new GameClock();

    // The weather system of a loaded world: the main menu's own scene has none that is spawned on the network.
    private static WeatherSystem? Weather
    {
        get
        {
            if (!GameLoop.IsGameReady || !Dispatcher.IsMainThread) return null;
            try
            {
                var weather = WeatherSystem.Instance;
                return weather != null && weather.Object != null ? weather : null;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Source, $"The clock cannot be read: {e.Message}");
                return null;
            }
        }
    }

    public bool IsAvailable => Weather != null;

    public float Hour => Weather?.TimeOfDay ?? 0;

    public bool IsNight => Weather?.IsNight ?? false;

    public int DaysPassed => Weather?.GetDaysPassed() ?? 0;

    public bool IsRunning => Weather?.TimeRunningEnabled ?? false;

    public void SetHour(float hour) => Weather?.SetGameTime(hour);

    public void SetRunning(bool running)
    {
        if (Weather is { } weather && weather.TimeRunningEnabled != running) weather.ToggleTimePassing();
    }
}
