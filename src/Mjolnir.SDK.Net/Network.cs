using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.Net;

/// <summary>
/// Multiplayer for mods: the players of the session whose mods match the host's, and message channels between them.
/// <para>
/// Every session is checked whether or not a mod uses this class: a client joining a host is compared with it by the mods
/// every player needs (<see cref="NetworkCompatibility.Everyone"/>, the default of <see cref="ModManifestAttribute.Network"/>),
/// and a client whose mods differ does not join; it is told which mods to turn on or off, and which versions differ.
/// </para>
/// <para>
/// Messages reach only <see cref="Players"/>: players that passed that check. A player without Mjolnir is never sent one.
/// Everything here is used on the game's main thread; a message sent from another thread goes out on the next frame, and
/// the events are raised on the main thread.
/// </para>
/// </summary>
public static partial class Network
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<int, NetPlayer> s_players = [];
    private static readonly Dictionary<uint, NetChannel> s_channels = [];
    private static ModHandlers<EventHandler<NetPlayerEventArgs>>? s_joined, s_left;
    private static NetSession? s_session;

    /// <summary>Whether the local player is in a multiplayer session (hosting one counts, single player does not).</summary>
    public static bool IsMultiplayer => Volatile.Read(ref s_session) != null;

    /// <summary>Whether the local player hosts the session (a dedicated server does too).</summary>
    public static bool IsHost => s_session?.Role == SessionRole.Host;

    /// <summary>The local player while in a multiplayer session whose check it passed; null otherwise.</summary>
    public static NetPlayer? LocalPlayer
    {
        get
        {
            lock (s_lock) return s_players.Values.FirstOrDefault(p => p.IsLocal);
        }
    }

    /// <summary>The players that passed the mod check, the local one included, by id.</summary>
    public static IReadOnlyList<NetPlayer> Players
    {
        get
        {
            lock (s_lock) return [.. s_players.Values.OrderBy(p => p.Id)];
        }
    }

    /// <summary>A player passed the mod check: from now on messages reach it. The local player is announced too.</summary>
    public static event EventHandler<NetPlayerEventArgs>? PlayerJoined
    {
        add => ModHandlers<EventHandler<NetPlayerEventArgs>>.Of(ref s_joined, "Network.PlayerJoined").Add(value);
        remove => s_joined?.Remove(value);
    }

    /// <summary>A player that had passed the mod check left the session (the local one too, when the session ends).</summary>
    public static event EventHandler<NetPlayerEventArgs>? PlayerLeft
    {
        add => ModHandlers<EventHandler<NetPlayerEventArgs>>.Of(ref s_left, "Network.PlayerLeft").Add(value);
        remove => s_left?.Remove(value);
    }

    /// <summary>
    /// The calling mod's channel of this name: the same object for the same name, and the same channel on every player's
    /// machine that runs the mod. Its messages reach only that mod's channel of the same name.
    /// </summary>
    /// <param name="name">Letters, digits, <c>_</c>, <c>-</c> and <c>.</c>, unique within the mod.</param>
    /// <exception cref="ArgumentException">The name is empty or has other characters.</exception>
    /// <exception cref="InvalidOperationException">The caller is not a mod.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static NetChannel Channel(string name)
    {
        if (string.IsNullOrEmpty(name) || !ValidName().IsMatch(name))
            throw new ArgumentException($"a channel's name is letters, digits, '_', '-' and '.'; got '{name}'", nameof(name));
        string modId = OwnerOf(CallingMod.Of(Assembly.GetCallingAssembly()))
                       ?? throw new InvalidOperationException("only a mod can open a network channel");
        string fullName = $"{modId}/{name}";
        uint id = Packets.ChannelId(fullName);
        lock (s_lock)
        {
            if (s_channels.TryGetValue(id, out var known))
            {
                if (known.FullName == fullName) return known;
                throw new ArgumentException($"the channel '{fullName}' has the same id as '{known.FullName}': choose another name", nameof(name));
            }
            var channel = new NetChannel(modId, name, id);
            s_channels[id] = channel;
            return channel;
        }
    }

    /// <summary>
    /// The platform's own channel of this name (an animation another player sees): owned by Mjolnir, whatever mod caused the
    /// message, so it reaches every checked player.
    /// </summary>
    internal static NetChannel PlatformChannel(string name)
    {
        string fullName = $"{ModDiscoveryReservedId}/{name}";
        uint id = Packets.ChannelId(fullName);
        lock (s_lock)
        {
            if (s_channels.TryGetValue(id, out var known)) return known;
            var channel = new NetChannel(ModDiscoveryReservedId, name, id);
            s_channels[id] = channel;
            return channel;
        }
    }

    // Core reserves this id for the platform: no mod has it.
    private const string ModDiscoveryReservedId = "mjolnir";

    /// <summary>The mod an assembly belongs to (tests replace it).</summary>
    internal static Func<Assembly, string?> OwnerOf { get; set; } = assembly => ModStringsRegistry.ForAssembly(assembly)?.ModId;

    internal static NetSession? Session => Volatile.Read(ref s_session);

    /// <summary>The game side starts a session (the local player joined one) or ends it (null).</summary>
    internal static void SetSession(NetSession? session)
    {
        var old = Interlocked.Exchange(ref s_session, session);
        if (old != null)
        {
            old.PeerVerified -= OnVerified;
            old.PeerGone -= OnGone;
            old.Message -= OnMessage;
            List<NetPlayer> gone;
            lock (s_lock)
            {
                gone = [.. s_players.Values.OrderBy(p => p.IsLocal)]; // the local player goes last
                s_players.Clear();
            }
            foreach (var player in gone) s_left?.Raise(h => h(null, new NetPlayerEventArgs(player)));
        }
        if (session == null) return;
        session.PeerVerified += OnVerified;
        session.PeerGone += OnGone;
        session.Message += OnMessage;
    }

    internal static NetPlayer? Find(int id)
    {
        lock (s_lock) return s_players.GetValueOrDefault(id);
    }

    private static void OnVerified(int id)
    {
        var session = Session;
        if (session == null) return;
        var player = new NetPlayer(id, isHost: session.HostId == id, isLocal: session.LocalId == id);
        lock (s_lock) s_players[id] = player;
        s_joined?.Raise(h => h(null, new NetPlayerEventArgs(player)));
    }

    private static void OnGone(int id)
    {
        NetPlayer? player;
        lock (s_lock)
        {
            if (!s_players.Remove(id, out player)) return;
        }
        s_left?.Raise(h => h(null, new NetPlayerEventArgs(player)));
    }

    private static void OnMessage(int from, uint channelId, byte[] payload)
    {
        NetChannel? channel;
        NetPlayer? sender;
        lock (s_lock)
        {
            s_channels.TryGetValue(channelId, out channel);
            sender = s_players.GetValueOrDefault(from);
        }
        if (channel == null || sender == null) return; // a mod this side does not run, or a player not checked
        channel.Deliver(sender, payload);
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex ValidName();
}

/// <summary>A player of the session that passed the mod check.</summary>
public sealed class NetPlayer
{
    internal NetPlayer(int id, bool isHost, bool isLocal)
    {
        Id = id;
        IsHost = isHost;
        IsLocal = isLocal;
    }

    /// <summary>The game's id of the player in this session.</summary>
    public int Id { get; }

    /// <summary>Whether the player hosts the session.</summary>
    public bool IsHost { get; }

    /// <summary>Whether this is the local player.</summary>
    public bool IsLocal { get; }

    public override string ToString() => $"player {Id}{(IsHost ? " (host)" : "")}{(IsLocal ? " (local)" : "")}";
}

/// <summary>A player for <see cref="Network.PlayerJoined"/> and <see cref="Network.PlayerLeft"/>.</summary>
public sealed class NetPlayerEventArgs(NetPlayer player) : EventArgs
{
    public NetPlayer Player { get; } = player;
}

/// <summary>A message that arrived on a <see cref="NetChannel"/>.</summary>
public sealed class NetMessageEventArgs(NetPlayer sender, byte[] data) : EventArgs
{
    /// <summary>The player that sent it.</summary>
    public NetPlayer Sender { get; } = sender;

    /// <summary>The bytes as they were sent.</summary>
    public byte[] Data { get; } = data;
}

/// <summary>
/// One mod's message channel (<see cref="Network.Channel"/>): bytes sent reliably and in order to players of the session
/// that run the same mod. A message carries at most 64 KB.
/// </summary>
public sealed class NetChannel
{
    private ModHandlers<EventHandler<NetMessageEventArgs>>? _received;

    internal NetChannel(string modId, string name, uint id)
    {
        ModId = modId;
        Name = name;
        Id = id;
    }

    /// <summary>The channel's name within its mod.</summary>
    public string Name { get; }

    /// <summary>The mod that opened it.</summary>
    public string ModId { get; }

    internal uint Id { get; }

    internal string FullName => $"{ModId}/{Name}";

    /// <summary>A message from another player arrived, on the main thread.</summary>
    public event EventHandler<NetMessageEventArgs>? Received
    {
        add => ModHandlers<EventHandler<NetMessageEventArgs>>.Of(ref _received, $"NetChannel {FullName}").Add(value);
        remove => _received?.Remove(value);
    }

    /// <summary>
    /// Sends <paramref name="data"/> to one player. False when it cannot reach it: not in a multiplayer session, the
    /// player is the local one or left. A message sent from another thread goes out on the next frame and answers true.
    /// </summary>
    /// <exception cref="ArgumentException">The data is larger than 64 KB.</exception>
    public bool Send(NetPlayer to, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(to);
        Check(data);
        byte[] copy = (byte[])data.Clone();
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Network.Session?.Send(to.Id, Id, copy));
            return Network.IsMultiplayer;
        }
        return Network.Session?.Send(to.Id, Id, copy) ?? false;
    }

    /// <summary>Sends <paramref name="data"/> to the host; false when the local player hosts or is not in a session.</summary>
    /// <exception cref="ArgumentException">The data is larger than 64 KB.</exception>
    public bool SendToHost(byte[] data)
    {
        var host = Network.Players.FirstOrDefault(p => p.IsHost);
        return host != null && Send(host, data);
    }

    /// <summary>Sends <paramref name="data"/> to every other player that passed the mod check; answers how many.</summary>
    /// <exception cref="ArgumentException">The data is larger than 64 KB.</exception>
    public int SendToAll(byte[] data)
    {
        Check(data);
        int sent = 0;
        foreach (var player in Network.Players.Where(p => !p.IsLocal))
            if (Send(player, data)) sent++;
        return sent;
    }

    /// <summary>The platform's listener of a platform channel: SDK code, called directly.</summary>
    internal Action<int, byte[]>? PlatformReceived { get; set; }

    internal void Deliver(NetPlayer sender, byte[] payload)
    {
        if (PlatformReceived is { } platform)
        {
            try
            {
                platform(sender.Id, payload);
            }
            catch (Exception e)
            {
                SdkLog.Error("SDK.Net", $"{this}: a message from {sender} failed: {e}");
            }
        }
        _received?.Raise(h => h(this, new NetMessageEventArgs(sender, payload)));
    }

    private static void Check(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > Packets.MaxPayload)
            throw new ArgumentException($"a message carries at most {Packets.MaxPayload} bytes, this one {data.Length}", nameof(data));
    }

    public override string ToString() => $"channel {FullName}";
}
