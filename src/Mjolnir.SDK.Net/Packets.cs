using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mjolnir.SDK.Net;

/// <summary>What a Mjolnir packet carries.</summary>
internal enum PacketKind : byte
{
    /// <summary>Client to host: the client's platform version and mods.</summary>
    Hello = 1,

    /// <summary>Host to client: the client's mods match; it may exchange messages from now on.</summary>
    Accept = 2,

    /// <summary>Host to client: the mods do not match; the host's platform version and mods follow, the client leaves.</summary>
    Refuse = 3,

    /// <summary>Host to every accepted client: the players who passed the handshake.</summary>
    Peers = 4,

    /// <summary>A mod's message on one of its channels.</summary>
    Channel = 5,
}

/// <summary>One decoded packet; the fields its kind uses are set.</summary>
internal sealed record Packet(PacketKind Kind, int Sender)
{
    public string Platform { get; init; } = "";
    public IReadOnlyList<ModStamp> Mods { get; init; } = [];
    public IReadOnlyList<int> Players { get; init; } = [];
    public uint Channel { get; init; }
    public byte[] Payload { get; init; } = [];
}

/// <summary>
/// The wire format of Mjolnir's packets, which share the game's reliable data with its own world streaming: a header
/// "AKLN", the format's version, the kind and the sender's player id. The game's packets never start so (their first byte
/// is a player's id), and the receiving side takes only packets with this header and version; the rest go on to the game
/// untouched. The sender is in the packet because the game's runner does not tell it in shared mode: it reported a
/// client's packet as the host's own (seen with two machines, 2026-11-07), and the game writes its own sender into its
/// packets too.
/// </summary>
internal static class Packets
{
    public const byte FormatVersion = 2;
    private static readonly byte[] Magic = "AKLN"u8.ToArray();
    private const int HeaderSize = 10;

    // The longest platform version, mod id, name or version a packet may carry: far beyond any real one.
    private const int MaxText = 4096;

    /// <summary>The largest payload a channel message may carry.</summary>
    public const int MaxPayload = 64 * 1024;

    /// <summary>Whether the data is a Mjolnir packet of a format this side reads.</summary>
    public static bool IsOurs(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[..4].SequenceEqual(Magic) && data[4] == FormatVersion;

    /// <summary>Whether the data starts with the header, whatever the format's version.</summary>
    public static bool HasMagic(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual(Magic);

    public static byte[] Hello(int sender, string platform, IReadOnlyCollection<ModStamp> mods) => Write(PacketKind.Hello, sender, w => WriteMods(w, platform, mods));

    public static byte[] Accept(int sender) => Write(PacketKind.Accept, sender, _ => { });

    public static byte[] Refuse(int sender, string platform, IReadOnlyCollection<ModStamp> mods) => Write(PacketKind.Refuse, sender, w => WriteMods(w, platform, mods));

    public static byte[] Peers(int sender, IReadOnlyCollection<int> players) => Write(PacketKind.Peers, sender, w =>
    {
        w.Write(players.Count);
        foreach (int id in players) w.Write(id);
    });

    public static byte[] ChannelMessage(int sender, uint channel, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload) throw new ArgumentException($"a message carries at most {MaxPayload} bytes, this one {payload.Length}");
        byte[] copy = payload.ToArray();
        return Write(PacketKind.Channel, sender, w =>
        {
            w.Write(channel);
            w.Write(copy.Length);
            w.Write(copy);
        });
    }

    /// <summary>
    /// The packet, or null for data that is not one this side reads: another format, or a body that is cut, broken or made
    /// to harm (the data comes from another machine, so every read stays inside it and nothing it holds throws).
    /// </summary>
    public static Packet? Read(byte[] data)
    {
        if (!IsOurs(data)) return null;
        var kind = (PacketKind)data[5];
        int sender = BitConverter.ToInt32(data, 6);
        return RemoteReader.TryRead(data, HeaderSize, r => ReadBody(r, kind, sender), out var packet) ? packet : null;
    }

    private static Packet? ReadBody(RemoteReader reader, PacketKind kind, int sender)
    {
        switch (kind)
        {
            case PacketKind.Hello:
            case PacketKind.Refuse:
                var (platform, mods) = ReadMods(reader);
                return new Packet(kind, sender) { Platform = platform, Mods = mods };
            case PacketKind.Accept:
                return new Packet(kind, sender);
            case PacketKind.Peers:
                int count = reader.ReadCount(4);
                var players = new int[count];
                for (int i = 0; i < count; i++) players[i] = reader.ReadInt32();
                return new Packet(kind, sender) { Players = players };
            case PacketKind.Channel:
                uint channel = reader.ReadUInt32();
                int length = reader.ReadInt32();
                if (length < 0 || length > MaxPayload) return null;
                return new Packet(kind, sender) { Channel = channel, Payload = reader.ReadBytes(length) };
            default:
                return null;
        }
    }

    private static byte[] Write(PacketKind kind, int sender, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write((byte)kind);
            writer.Write(sender);
            body(writer);
        }
        return stream.ToArray();
    }

    private static void WriteMods(BinaryWriter w, string platform, IReadOnlyCollection<ModStamp> mods)
    {
        w.Write(platform);
        w.Write(mods.Count);
        foreach (var mod in mods)
        {
            w.Write(mod.Id);
            w.Write(mod.Name);
            w.Write(mod.Version);
            w.Write((byte)mod.Network);
        }
    }

    private static (string Platform, ModStamp[] Mods) ReadMods(RemoteReader r)
    {
        string platform = r.ReadString(MaxText);
        int count = r.ReadCount(4); // three strings of a length byte at least, and the mode's byte
        var mods = new ModStamp[count];
        for (int i = 0; i < count; i++)
            mods[i] = new ModStamp(r.ReadString(MaxText), r.ReadString(MaxText), r.ReadString(MaxText), (NetworkCompatibility)r.ReadByte());
        return (platform, mods);
    }

    /// <summary>A channel's id: FNV-1a of its full name, the same on every machine.</summary>
    public static uint ChannelId(string fullName) => Fnv1a.OfUtf8(fullName);
}
