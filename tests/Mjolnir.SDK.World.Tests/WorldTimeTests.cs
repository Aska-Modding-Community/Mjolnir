using System;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.World.Tests;

/// <summary>
/// The world's clock on a fake: reading it, setting the hour and wrapping it, stopping it, no world, other threads; and
/// the console's time: the words, an hour typed three ways, what it answers.
/// </summary>
[Collection("World")]
public sealed class WorldTimeTests : IDisposable
{
    private readonly FakeClock _clock = new();

    public WorldTimeTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        WorldTime.ResetForTests();
        WorldCommands.Register();
        WorldTime.Clock = _clock;
    }

    public void Dispose()
    {
        WorldTime.ResetForTests();
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void WithoutAWorldTheClockReadsZeroAndNothingIsSet()
    {
        _clock.IsAvailable = false;
        WorldTime.Hour = 9;
        WorldTime.IsRunning = false;
        Assert.False(WorldTime.IsAvailable);
        Assert.Equal(0, WorldTime.Hour);
        Assert.Equal(12f, _clock.Hour);
        Assert.True(_clock.IsRunning);
    }

    [Fact]
    public void TheClockIsReadFromTheGame()
    {
        Assert.True(WorldTime.IsAvailable);
        Assert.Equal(12f, WorldTime.Hour);
        Assert.False(WorldTime.IsNight);
        Assert.Equal(62, WorldTime.DaysPassed);
        Assert.True(WorldTime.IsRunning);
    }

    [Theory]
    [InlineData(9f, 9f)]
    [InlineData(0f, 0f)]
    [InlineData(24f, 0f)]
    [InlineData(25.5f, 1.5f)]
    [InlineData(-1f, 23f)]
    public void AnHourWrapsAroundTheDay(float asked, float set)
    {
        WorldTime.Hour = asked;
        Assert.Equal(set, _clock.Hour);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AnHourIsANumber(float hour) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldTime.Hour = hour);

    [Fact]
    public void StoppingTheClockTwiceChangesItOnce()
    {
        WorldTime.IsRunning = false;
        WorldTime.IsRunning = false;
        Assert.False(_clock.IsRunning);
        Assert.Equal(1, _clock.RunningChanges);
        WorldTime.IsRunning = true;
        Assert.True(_clock.IsRunning);
    }

    [Fact]
    public void AnotherThreadGetsTheLastHourSeenAndSetsItOnTheNextFrame()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        _ = WorldTime.Hour;
        _clock.Hour = 20;
        float seen = -1;
        var worker = new Thread(() =>
        {
            seen = WorldTime.Hour;
            WorldTime.Hour = 7;
        });
        worker.Start();
        worker.Join();
        Assert.Equal(12f, seen);
        Assert.Equal(20f, _clock.Hour);
        Dispatcher.Tick();
        Assert.Equal(7f, _clock.Hour);
    }

    [Theory]
    [InlineData("9", 9f)]
    [InlineData("14.5", 14.5f)]
    [InlineData("14:30", 14.5f)]
    [InlineData("0:05", 1f / 12f)]
    [InlineData("24", 0f)]
    public void AnHourIsTypedThreeWays(string text, float hour) =>
        Assert.Equal(hour, WorldCommands.ParseHour(text)!.Value, 3);

    [Theory]
    [InlineData("noon")]
    [InlineData("25")]
    [InlineData("-1")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("12:")]
    public void AnythingElseIsNoHour(string text) => Assert.Null(WorldCommands.ParseHour(text));

    [Theory]
    [InlineData(0f, "00:00")]
    [InlineData(9.5f, "09:30")]
    [InlineData(23.999f, "00:00")]
    public void AnHourIsShownAsAClockShowsIt(float hour, string shown) => Assert.Equal(shown, WorldCommands.Format(hour));

    [Theory]
    [InlineData("time day", WorldCommands.DayHour)]
    [InlineData("time NIGHT", WorldCommands.NightHour)]
    [InlineData("time 14:30", 14.5f)]
    public void TimeSetsTheClock(string line, float hour)
    {
        DevConsole.Execute(line);
        Assert.Equal(hour, _clock.Hour);
    }

    [Fact]
    public void TimeAnswersWithTheHourAskedFor()
    {
        _clock.Delayed = true; // the game applies a change on a later tick
        DevConsole.Execute("time 14:30");
        Assert.Equal(12f, _clock.Hour);
        Assert.Contains(Lines(), l => l.Contains("Day 63, 14:30.", StringComparison.Ordinal));
    }

    [Fact]
    public void TimeShowsTheClockAndThatItStopped()
    {
        DevConsole.Execute("time stop");
        Assert.False(_clock.IsRunning);
        DevConsole.Execute("time");
        Assert.Contains(Lines(), l => l.Contains("Day 63, 12:00, stopped.", StringComparison.Ordinal));
        DevConsole.Execute("time run");
        Assert.True(_clock.IsRunning);
    }

    [Fact]
    public void TimeWithoutAWorldSaysSo()
    {
        _clock.IsAvailable = false;
        DevConsole.Execute("time day");
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text == "No world loaded.");
    }

    [Fact]
    public void TimeWithAWrongHourKeepsTheClock()
    {
        DevConsole.Execute("time noon");
        Assert.Equal(12f, _clock.Hour);
        Assert.Contains(DevConsole.Lines, l => l.Kind == ConsoleLineKind.Error && l.Text.Contains("is no hour", StringComparison.Ordinal));
    }

    private static string[] Lines() => DevConsole.Lines.Select(l => l.Text).ToArray();

    private sealed class FakeClock : IClockSource
    {
        public bool IsAvailable { get; set; } = true;
        public float Hour { get; set; } = 12;
        public bool IsNight { get; set; }
        public int DaysPassed { get; set; } = 62;
        public bool IsRunning { get; set; } = true;
        public bool Delayed { get; set; }
        public int RunningChanges { get; private set; }

        public void SetHour(float hour)
        {
            if (!Delayed) Hour = hour;
        }

        public void SetRunning(bool running)
        {
            if (IsRunning == running) return;
            IsRunning = running;
            RunningChanges++;
        }
    }
}
