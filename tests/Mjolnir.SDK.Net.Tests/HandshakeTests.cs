using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Mjolnir.SDK.Net.Tests.CompatibilityTests;

namespace Mjolnir.SDK.Net.Tests;

/// <summary>A fake session: players by id, each with its own <see cref="NetSession"/>, packets delivered at once.</summary>
internal sealed class FakeNetwork
{
    public double Time;
    public readonly Dictionary<int, NetSession> Sessions = [];
    public readonly Dictionary<int, FakeEnd> Ends = [];
    public string? HostAnnounces = "0.3.0";

    public NetSession Join(int id, IReadOnlyList<ModStamp> mods, string platform = "0.3.0", bool hostHasMjolnir = true)
    {
        var end = new FakeEnd(this, id);
        Ends[id] = end;
        var session = new NetSession(end, platform, mods, () => Time);
        Sessions[id] = session;
        if (id != 0 && hostHasMjolnir && Sessions.TryGetValue(0, out var host)) host.PeerJoined(id);
        if (!hostHasMjolnir) end.NoMjolnirHost = true;
        session.Start();
        return session;
    }

    /// <summary>A player without Mjolnir joins: the host sees it, it sends nothing.</summary>
    public void JoinWithoutMjolnir(int id) => Sessions[0].PeerJoined(id);

    public void Advance(double seconds)
    {
        Time += seconds;
        foreach (var session in Sessions.Values.ToList()) session.Tick();
    }

    public void Leave(int id)
    {
        Sessions.Remove(id);
        foreach (var session in Sessions.Values) session.PeerLeft(id);
    }
}

internal sealed class FakeEnd(FakeNetwork network, int id) : INetTransport
{
    public bool NoMjolnirHost;
    public readonly List<int> Kicked = [];
    public bool Left;
    public readonly List<(int To, PacketKind Kind)> Sent = [];

    public int LocalId => id;
    public int HostId => 0;
    public bool IsHost => id == 0;
    public string? HostPlatform => NoMjolnirHost ? null : network.HostAnnounces;

    public void Send(int player, byte[] packet)
    {
        Sent.Add((player, Packets.Read(packet)!.Kind));
        if (network.Sessions.TryGetValue(player, out var target)) Assert.True(target.Receive(packet));
        else throw new InvalidOperationException($"player {id} sent a packet to player {player}, who has no Mjolnir");
    }

    public void Kick(int player)
    {
        Kicked.Add(player);
        network.Leave(player);
    }

    public void Leave() => Left = true;
}

/// <summary>
/// The handshake between a host and its clients: matching mods are accepted and announced, others refused with the
/// differences, players without Mjolnir removed only when the session needs mods, and messages only between checked players.
/// </summary>
public class HandshakeTests
{
    private static readonly ModStamp[] HostMods = [Shared("items"), Shared("stats", "2.0.0"), Own("minimap", NetworkCompatibility.ClientOnly)];

    [Fact]
    public void AClientWithTheSameModsIsAcceptedAndBothSeeEachOther()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        var client = net.Join(1, [Shared("items"), Shared("stats", "2.0")]);

        Assert.Equal(SessionRole.Host, host.Role);
        Assert.Equal(SessionRole.Accepted, client.Role);
        Assert.Equal([0, 1], host.Verified);
        Assert.Equal([0, 1], client.Verified);
        Assert.Null(client.Refusal);
    }

    [Fact]
    public void AClientWithDifferentModsIsRefusedLeavesAndIsRemovedAfterAMoment()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        var client = net.Join(1, [Shared("stats", "1.0.0"), Shared("cheats")]);

        Assert.Equal(SessionRole.Refused, client.Role);
        Assert.True(net.Ends[1].Left);
        var report = client.Refusal!;
        Assert.Equal(["items"], report.MissingOnClient.Select(m => m.Id));
        Assert.Equal(["cheats"], report.MissingOnHost.Select(m => m.Id));
        Assert.Equal("stats", Assert.Single(report.VersionMismatches).Name.Split(' ')[0].ToLowerInvariant());
        Assert.DoesNotContain(1, host.Verified);

        net.Advance(NetSession.KickDelay - 0.5);
        Assert.Empty(net.Ends[0].Kicked);
        net.Advance(1);
        Assert.Equal([1], net.Ends[0].Kicked);
    }

    [Fact]
    public void ARefusedClientsOwnModsDoNotMatter()
    {
        var net = new FakeNetwork();
        net.Join(0, [Shared("items")]);
        var client = net.Join(1, [Shared("items"), Own("hud", NetworkCompatibility.ClientOnly), Own("server", NetworkCompatibility.ServerOnly)]);
        Assert.Equal(SessionRole.Accepted, client.Role);
    }

    [Fact]
    public void AnotherMjolnirVersionIsRefused()
    {
        var net = new FakeNetwork();
        net.Join(0, HostMods);
        var client = net.Join(1, HostMods, platform: "0.4.0");
        Assert.True(client.Refusal!.PlatformMismatch);
    }

    [Fact]
    public void APlayerWithoutMjolnirIsRemovedWhenTheSessionNeedsMods()
    {
        var net = new FakeNetwork();
        net.Join(0, HostMods);
        net.JoinWithoutMjolnir(1);
        net.Advance(NetSession.HelloTimeout - 1);
        Assert.Empty(net.Ends[0].Kicked);
        net.Advance(1);
        Assert.Equal([1], net.Ends[0].Kicked);
    }

    [Fact]
    public void APlayerWithoutMjolnirPlaysOnWhenNoModNeedsIt()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, [Own("minimap", NetworkCompatibility.ClientOnly)]);
        net.JoinWithoutMjolnir(1);
        net.Advance(NetSession.HelloTimeout + 5);
        Assert.Empty(net.Ends[0].Kicked);
        Assert.DoesNotContain(1, host.Verified);
        Assert.False(host.Send(1, 7, [1])); // never sent a packet it would take for the game's data
    }

    [Fact]
    public void AClientOfAHostWithoutMjolnirLeavesOnlyWithModsEveryoneNeeds()
    {
        var withShared = new FakeNetwork().Join(1, [Shared("items")], hostHasMjolnir: false);
        Assert.Equal(SessionRole.Refused, withShared.Role);
        Assert.True(withShared.Refusal!.HostWithoutMjolnir);

        var withOwn = new FakeNetwork().Join(1, [Own("minimap", NetworkCompatibility.ClientOnly)], hostHasMjolnir: false);
        Assert.Equal(SessionRole.Vanilla, withOwn.Role);
        Assert.Null(withOwn.Refusal);
    }

    [Fact]
    public void AHostThatNeverAnswersSendsTheClientAway()
    {
        var net = new FakeNetwork();
        var end = new FakeEnd(net, 1);
        var client = new NetSession(end, "0.3.0", [Shared("items")], () => net.Time);
        net.Sessions[0] = new NetSession(new FakeEnd(net, 0), "0.3.0", [], () => net.Time); // receives, never started: no answer
        client.Start();
        net.Time += NetSession.AnswerTimeout;
        client.Tick();
        Assert.True(client.Refusal!.HostDidNotAnswer);
        Assert.True(end.Left);
    }

    [Fact]
    public void AThirdPlayerIsAnnouncedToTheOthersAndItsLeavingToo()
    {
        var net = new FakeNetwork();
        net.Join(0, HostMods);
        var first = net.Join(1, HostMods);
        var gone = new List<int>();
        first.PeerGone += gone.Add;
        var second = net.Join(2, HostMods);
        Assert.Equal([0, 1, 2], first.Verified);
        Assert.Equal([0, 1, 2], second.Verified);

        net.Leave(2);
        Assert.Equal([0, 1], first.Verified);
        Assert.Equal([2], gone);
    }

    [Fact]
    public void MessagesFlowOnlyBetweenCheckedPlayers()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        var client = net.Join(1, HostMods);
        var received = new List<(int From, uint Channel, byte[] Data)>();
        host.Message += (from, channel, data) => received.Add((from, channel, data));

        Assert.True(client.Send(0, 5, [9, 8]));
        var (from, channelId, bytes) = Assert.Single(received);
        Assert.Equal((1, 5u), (from, channelId));
        Assert.Equal([9, 8], bytes);
        Assert.False(client.Send(1, 5, [1])); // not to itself
        Assert.False(client.Send(7, 5, [1])); // nor to an unknown player
    }

    [Fact]
    public void AMessageFromAnUncheckedPlayerIsTakenButNotDelivered()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        int delivered = 0;
        host.Message += (_, _, _) => delivered++;
        Assert.True(host.Receive(Packets.ChannelMessage(3, 1, [1])));
        Assert.Equal(0, delivered);
    }

    [Fact]
    public void TheGamesOwnDataIsLeftToTheGame()
    {
        var host = new FakeNetwork().Join(0, HostMods);
        Assert.False(host.Receive([1, 2, 3, 4, 5, 6, 7]));
        Assert.True(host.Receive([(byte)'A', (byte)'K', (byte)'L', (byte)'N', 77, 1])); // another format: still not the game's
    }

    [Fact]
    public void AnAnswerFromAnyoneButTheHostIsIgnored()
    {
        var net = new FakeNetwork { HostAnnounces = "0.3.0" };
        var end = new FakeEnd(net, 1);
        var client = new NetSession(end, "0.3.0", [Shared("items")], () => net.Time);
        net.Sessions[0] = new NetSession(new FakeEnd(net, 0), "0.3.0", [], () => net.Time);
        client.Start();
        client.Receive(Packets.Refuse(2, "0.3.0", []));
        client.Receive(Packets.Accept(2));
        Assert.Equal(SessionRole.Waiting, client.Role);
    }

    [Fact]
    public void AHelloInTheHostsNameNeverRemovesTheHost()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        Assert.True(host.Receive(Packets.Hello(0, "0.4.0", [Shared("cheats")])));
        net.Advance(NetSession.KickDelay + 1);
        Assert.Empty(net.Ends[0].Kicked);
        Assert.Empty(net.Ends[0].Sent);
        Assert.Equal(SessionRole.Host, host.Role);
    }

    [Fact]
    public void AHelloOfAPlayerThatPassedIsIgnored()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        net.Join(1, HostMods);
        int sent = net.Ends[0].Sent.Count;
        Assert.True(host.Receive(Packets.Hello(1, "0.3.0", [Shared("cheats")])));
        net.Advance(NetSession.KickDelay + 1);
        Assert.Equal(sent, net.Ends[0].Sent.Count);
        Assert.Empty(net.Ends[0].Kicked);
        Assert.Contains(1, host.Verified);
    }

    [Fact]
    public void APlayerAcceptedAfterARefusalIsNotRemoved()
    {
        var net = new FakeNetwork();
        var host = net.Join(0, HostMods);
        net.Join(1, [Shared("cheats")]);
        Assert.True(host.Receive(Packets.Hello(1, "0.3.0", HostMods)));
        Assert.Contains(1, host.Verified);
        net.Advance(NetSession.KickDelay + 1);
        Assert.Empty(net.Ends[0].Kicked);
    }
}
