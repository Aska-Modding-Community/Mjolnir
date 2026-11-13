using System;
using System.Text;
using Xunit;
using static Mjolnir.SDK.Net.Tests.CompatibilityTests;

namespace Mjolnir.SDK.Net.Tests;

/// <summary>Mjolnir's packets: told apart from the game's data, read back as written, and never trusted beyond their length.</summary>
public class PacketsTests
{
    [Fact]
    public void ModsGoThereAndBack()
    {
        ModStamp[] mods = [Shared("items", "1.2.0"), Own("minimap", NetworkCompatibility.ClientOnly), new("ру.мод", "Мод", "1", NetworkCompatibility.ServerOnly)];
        var hello = Packets.Read(Packets.Hello(4, "0.3.0", mods))!;
        Assert.Equal(PacketKind.Hello, hello.Kind);
        Assert.Equal("0.3.0", hello.Platform);
        Assert.Equal(mods, hello.Mods);
        Assert.Equal(4, hello.Sender);
        Assert.Equal(PacketKind.Refuse, Packets.Read(Packets.Refuse(0, "0.3.0", mods))!.Kind);
    }

    [Fact]
    public void PeersAndAcceptGoThereAndBack()
    {
        Assert.Equal([0, 2, 5], Packets.Read(Packets.Peers(0, [0, 2, 5]))!.Players);
        Assert.Equal((PacketKind.Accept, 0), (Packets.Read(Packets.Accept(0))!.Kind, Packets.Read(Packets.Accept(0))!.Sender));
    }

    [Fact]
    public void AChannelMessageCarriesItsBytes()
    {
        var packet = Packets.Read(Packets.ChannelMessage(7, 42, [1, 2, 3]))!;
        Assert.Equal((PacketKind.Channel, 42u, 7), (packet.Kind, packet.Channel, packet.Sender));
        Assert.Equal([1, 2, 3], packet.Payload);
    }

    [Fact]
    public void AMessageLargerThanTheLimitIsRefused() =>
        Assert.Throws<ArgumentException>(() => Packets.ChannelMessage(0, 1, new byte[Packets.MaxPayload + 1]));

    [Fact]
    public void TheGamesDataIsNotOurs()
    {
        byte[] game = [0, 12, 7, 1, 1, 1, 1];
        Assert.False(Packets.HasMagic(game));
        Assert.Null(Packets.Read(game));
    }

    [Fact]
    public void AnotherFormatHasTheHeaderButIsNotRead()
    {
        byte[] data = Packets.Accept(0);
        data[4] = 99;
        Assert.True(Packets.HasMagic(data));
        Assert.False(Packets.IsOurs(data));
        Assert.Null(Packets.Read(data));
    }

    [Fact]
    public void ACutPacketReadsAsNothing()
    {
        byte[] hello = Packets.Hello(1, "0.3.0", [Shared("items")]);
        for (int length = 10; length < hello.Length; length++) Assert.Null(Packets.Read(hello[..length]));
    }

    [Fact]
    public void ACountBeyondThePacketIsRefused()
    {
        byte[] peers = Packets.Peers(0, [1]);
        BitConverter.GetBytes(int.MaxValue).CopyTo(peers, 10);
        Assert.Null(Packets.Read(peers));
    }

    [Fact]
    public void ABrokenStringLengthReadsAsNothing()
    {
        byte[] hello = Packets.Hello(1, "0.3.0", [Shared("items")]);
        byte[] broken = [.. hello[..10], 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, .. hello[10..]]; // no 7-bit length ends so
        Assert.Null(Packets.Read(broken));
    }

    [Fact]
    public void AStringThatIsNoUtf8ReadsAsNothing()
    {
        byte[] hello = Packets.Hello(1, "0.3.0", [Shared("items")]);
        hello[11] = 0xFF; // the platform's first character
        Assert.Null(Packets.Read(hello));
    }

    [Fact]
    public void AChannelMessageLongerThanItsPacketIsRefused()
    {
        byte[] message = Packets.ChannelMessage(1, 5, [1, 2, 3]);
        BitConverter.GetBytes(Packets.MaxPayload).CopyTo(message, 14);
        Assert.Null(Packets.Read(message));
        BitConverter.GetBytes(-1).CopyTo(message, 14);
        Assert.Null(Packets.Read(message));
    }

    [Fact]
    public void AnUnknownKindReadsAsNothing()
    {
        byte[] accept = Packets.Accept(0);
        accept[5] = 200;
        Assert.Null(Packets.Read(accept));
    }

    [Fact]
    public void AChannelsIdIsTheSameEverywhere()
    {
        Assert.Equal(Packets.ChannelId("author.mod/chat"), Packets.ChannelId("author.mod/chat"));
        Assert.NotEqual(Packets.ChannelId("author.mod/chat"), Packets.ChannelId("author.mod/chat2"));
        Assert.Equal(0xE40C292Cu, Packets.ChannelId("a")); // FNV-1a of "a"
        Assert.Equal("AKLN", Encoding.ASCII.GetString(Packets.Accept(0), 0, 4));
    }
}
