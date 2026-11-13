using System;
using System.Collections.Generic;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Player.Tests;

/// <summary>
/// The stamina cost before it is spent (<see cref="StaminaStat.Draining"/>, what the facade's Character.DrainStamina
/// prefix asks): only the local player's, handlers in order, cancelling, never below 0, a throwing handler.
/// </summary>
[Collection("Player")]
public sealed class StaminaDrainingTests : IDisposable
{
    private readonly FakePlayer _player = new();
    private readonly List<EventHandler<StaminaDrainingEventArgs>> _handlers = [];

    public StaminaDrainingTests()
    {
        Dispatcher.ResetForTests();
        LocalPlayer.ResetForTests();
        LocalPlayer.Track(_player);
    }

    public void Dispose()
    {
        foreach (var handler in _handlers) LocalPlayer.Stamina.Draining -= handler;
        LocalPlayer.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void WithoutHandlersTheCostIsUntouched()
    {
        float amount = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(_player.Id, ref amount, StaminaUse.Default));
        Assert.Equal(10f, amount);
    }

    [Fact]
    public void HandlersChangeTheCostInTheOrderTheyWereAdded()
    {
        StaminaDrainingEventArgs? seen = null;
        float seenAmount = 0;
        Add((_, e) => e.Amount *= 2);
        Add((s, e) =>
        {
            seen = e;
            seenAmount = e.Amount;
            Assert.Same(LocalPlayer.Stamina, s);
        });

        float amount = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(_player.Id, ref amount, StaminaUse.Harvest));
        Assert.Equal(20f, amount);
        Assert.Equal(20f, seenAmount);
        Assert.Equal(StaminaUse.Harvest, seen!.Use);
    }

    [Fact]
    public void ACancelledCostSpendsNothing()
    {
        Add((_, e) => e.Cancel = true);
        float amount = 10f;
        Assert.False(LocalPlayer.Stamina.Drain(_player.Id, ref amount, StaminaUse.Combat));
    }

    [Fact]
    public void OtherCharactersAreNotReported()
    {
        int calls = 0;
        Add((_, e) =>
        {
            calls++;
            e.Amount = 0;
        });

        float villager = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(new FakePlayer().Id, ref villager, StaminaUse.Default));
        Assert.Equal(10f, villager);

        LocalPlayer.Track(null);
        float noPlayer = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(0, ref noPlayer, StaminaUse.Default));
        Assert.True(LocalPlayer.Stamina.Drain(_player.Id, ref noPlayer, StaminaUse.Default));
        Assert.Equal(10f, noPlayer);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(float.NaN, 10f)]
    [InlineData(float.PositiveInfinity, 10f)]
    public void ACostIsNeverBelowZeroAndANonNumberKeepsTheGames(float set, float expected)
    {
        Add((_, e) => e.Amount = set);
        float amount = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(_player.Id, ref amount, StaminaUse.Default));
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void AThrowingHandlerDoesNotStopTheOthers()
    {
        Add((_, _) => throw new InvalidOperationException("a broken mod"));
        Add((_, e) => e.Amount /= 2);
        float amount = 10f;
        Assert.True(LocalPlayer.Stamina.Drain(_player.Id, ref amount, StaminaUse.Default));
        Assert.Equal(5f, amount);
    }

    [Fact]
    public void TheEventArgsCanBeMadeForAModsOwnTests()
    {
        var args = new StaminaDrainingEventArgs(3f, StaminaUse.Combat);
        Assert.Equal(3f, args.Amount);
        Assert.Equal(StaminaUse.Combat, args.Use);
        Assert.False(args.Cancel);
    }

    private void Add(EventHandler<StaminaDrainingEventArgs> handler)
    {
        _handlers.Add(handler);
        LocalPlayer.Stamina.Draining += handler;
    }
}
