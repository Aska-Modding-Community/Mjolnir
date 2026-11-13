using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;
using static Mjolnir.SDK.Net.Tests.CompatibilityTests;

namespace Mjolnir.SDK.Net.Tests;

/// <summary>
/// The public side: a mod's channels, the players that passed the check and their events, messages between two players.
/// Shares the static <see cref="Network"/> with no other test class.
/// </summary>
[Collection("Network")]
public class ChannelsTests : IDisposable
{
    public ChannelsTests() => Network.OwnerOf = _ => "author.mod";

    public void Dispose()
    {
        Network.SetSession(null);
        Network.OwnerOf = a => ModStringsRegistry.ForAssembly(a)?.ModId;
    }

    [Fact]
    public void AChannelIsTheSameObjectForTheSameName()
    {
        var chat = Network.Channel("chat");
        Assert.Same(chat, Network.Channel("chat"));
        Assert.Equal(("chat", "author.mod"), (chat.Name, chat.ModId));
        Assert.NotSame(chat, Network.Channel("trade"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("a/b")]
    public void AChannelNeedsAPlainName(string name) => Assert.Throws<ArgumentException>(() => Network.Channel(name));

    [Fact]
    public void OnlyAModOpensAChannel()
    {
        Network.OwnerOf = _ => null;
        Assert.Throws<InvalidOperationException>(() => Network.Channel("chat"));
    }

    [Fact]
    public void OutOfASessionNothingIsSent()
    {
        Assert.False(Network.IsMultiplayer);
        Assert.Empty(Network.Players);
        Assert.Equal(0, Network.Channel("chat").SendToAll([1]));
        Assert.False(Network.Channel("chat").SendToHost([1]));
    }

    [Fact]
    public void PlayersAreAnnouncedAsTheyPassAndLeaveWhenTheSessionEnds()
    {
        var joined = new List<string>();
        var left = new List<string>();
        EventHandler<NetPlayerEventArgs> onJoined = (_, e) => joined.Add(e.Player.ToString());
        EventHandler<NetPlayerEventArgs> onLeft = (_, e) => left.Add(e.Player.ToString());
        Network.PlayerJoined += onJoined;
        Network.PlayerLeft += onLeft;
        try
        {
            var net = new FakeNetwork();
            net.Join(0, [Shared("items")]);
            var client = new NetSession(new FakeEnd(net, 1), "0.3.0", [Shared("items")], () => net.Time);
            net.Sessions[1] = client;
            Network.SetSession(client); // this process plays player 1
            net.Sessions[0].PeerJoined(1);
            client.Start();

            Assert.Equal(["player 0 (host)", "player 1 (local)"], joined);
            Assert.True(Network.IsMultiplayer);
            Assert.False(Network.IsHost);
            Assert.Equal(1, Network.LocalPlayer!.Id);

            Network.SetSession(null);
            Assert.Equal(["player 0 (host)", "player 1 (local)"], left); // the local player last
            Assert.Empty(Network.Players);
        }
        finally
        {
            Network.PlayerJoined -= onJoined;
            Network.PlayerLeft -= onLeft;
        }
    }

    [Fact]
    public void AMessageReachesTheSameChannelOnTheOtherSide()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, [Shared("items")]);
        net.Join(1, [Shared("items")]);
        Network.SetSession(host); // this process plays the host
        host.PeerJoined(1); // already verified: no change
        foreach (int id in host.Verified) typeof(Network).GetMethod("OnVerified", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [id]);

        var chat = Network.Channel("chat");
        var got = new List<(int, byte[])>();
        chat.Received += (_, e) => got.Add((e.Sender.Id, e.Data));
        net.Sessions[1].Send(0, chat.Id, [4, 2]); // player 1's mod sends on its channel of the same name

        var (sender, data) = Assert.Single(got);
        Assert.Equal(1, sender);
        Assert.Equal([4, 2], data);
        Assert.True(Network.IsHost);
        Assert.Equal(1, chat.SendToAll([7]));
    }

    [Fact]
    public void AMessageLargerThanTheLimitIsRefused() =>
        Assert.Throws<ArgumentException>(() => Network.Channel("chat").SendToAll(new byte[Packets.MaxPayload + 1]));
}

/// <summary>
/// Guards the Mjolnir.SDK.Net module: its public signatures use only .NET and the SDK's types, it reaches Core only through
/// Mjolnir.SDK and needs no other module, its events follow the convention, and only its tests see its internals.
/// </summary>
public class NetArchitectureTests
{
    private static readonly Assembly Net = typeof(Network).Assembly;

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Net, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Net, ApiSurface.IsGame));

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Net));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Net));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Net.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    [InlineData("Mjolnir.SDK.Stats")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Net.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Net.Tests"], ApiSurface.FriendsOf(Net));

    // Discover only: EnsureStarted runs once per process and patches the game.
    [Fact]
    public void TheSdkFindsTheNetworkModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(Net.Location)!;
        Assert.Contains(typeof(NetModule), SdkModules.Discover(folder));
    }
}
