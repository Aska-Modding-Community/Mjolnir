namespace Mjolnir.SDK.World;

/// <summary>
/// What the model sees of the game's clock (Game/GameClock.cs; a fake in the unit tests). Read and written on the main
/// thread.
/// </summary>
internal interface IClockSource
{
    /// <summary>Whether a world with a running clock is loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>The hour of the day, from 0 up to 24.</summary>
    float Hour { get; }

    bool IsNight { get; }

    int DaysPassed { get; }

    bool IsRunning { get; }

    void SetHour(float hour);

    void SetRunning(bool running);
}
