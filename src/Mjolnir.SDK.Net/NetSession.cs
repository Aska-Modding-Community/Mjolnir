using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.SDK.Net;

/// <summary>The local side of a session.</summary>
internal enum SessionRole
{
    /// <summary>Not started yet.</summary>
    None,

    /// <summary>The host: it checks each client's mods.</summary>
    Host,

    /// <summary>A client waiting for the host's answer.</summary>
    Waiting,

    /// <summary>A client the host accepted.</summary>
    Accepted,

    /// <summary>A client refused, leaving the session.</summary>
    Refused,

    /// <summary>A client of a host without Mjolnir, with no mod every player needs: it plays on, without messages.</summary>
    Vanilla,
}

/// <summary>
/// The mod-compatibility handshake and the routing of mods' messages, for one session. Pure: the game is behind
/// <see cref="INetTransport"/> and the time behind a clock, so the unit tests play host and clients.
/// <list type="bullet">
/// <item>The host announces its Mjolnir version in the session's properties. A client sends it its mods; the host compares
/// them with its own and accepts the client or refuses it with its own list, and removes a refused client after a moment
/// (the client leaves by itself, the removal is for one that does not).</item>
/// <item>A client that sends nothing (it has no Mjolnir) is removed when the host has mods every player needs, and
/// plays on without messages otherwise.</item>
/// <item>A client of a host without Mjolnir leaves when it has mods every player needs.</item>
/// <item>Messages go only between players that passed the handshake: a player without Mjolnir would take them for the
/// game's own data.</item>
/// </list>
/// </summary>
internal sealed class NetSession(INetTransport net, string platform, IReadOnlyList<ModStamp> mods, Func<double> now)
{
    /// <summary>How long the host waits for a new client's mods.</summary>
    public const double HelloTimeout = 20;

    /// <summary>How long a client waits for the host's answer.</summary>
    public const double AnswerTimeout = 30;

    /// <summary>How long the host gives a refused client to leave by itself (it leaves at once; the removal is for one that does not).</summary>
    public const double KickDelay = 10;

    private readonly Dictionary<int, double> _pending = [];
    private readonly Dictionary<int, double> _kicks = [];
    private readonly SortedSet<int> _verified = [];
    private double _waitingSince;
    private bool _foreignFormatLogged;

    public SessionRole Role { get; private set; }

    public int LocalId => net.LocalId;

    public int HostId => net.HostId;

    /// <summary>Why this client was refused or left; null while it plays.</summary>
    public CompatibilityReport? Refusal { get; private set; }

    /// <summary>The players that passed the handshake, the local one included.</summary>
    public IReadOnlyCollection<int> Verified => _verified;

    /// <summary>A player passed the handshake (the local one too, once it is accepted or hosts).</summary>
    public event Action<int>? PeerVerified;

    /// <summary>A player that had passed the handshake left.</summary>
    public event Action<int>? PeerGone;

    /// <summary>A mod's message from a verified player: sender, channel, payload.</summary>
    public event Action<int, uint, byte[]>? Message;

    /// <summary>A line for the log.</summary>
    public event Action<string>? Log;

    private bool HasSharedMods => mods.Any(m => m.IsShared);

    /// <summary>The local player is in the session.</summary>
    public void Start()
    {
        if (Role != SessionRole.None) return;
        if (net.IsHost)
        {
            Role = SessionRole.Host;
            Verify(net.LocalId);
            Log?.Invoke($"hosting with {Describe(mods)}");
            return;
        }
        if (net.HostPlatform == null)
        {
            if (HasSharedMods) Refuse(CompatibilityReport.HostHasNoMjolnir(platform, mods));
            else
            {
                Role = SessionRole.Vanilla;
                Log?.Invoke("the host has no Mjolnir; no mod needs it, so the session goes on without mod messages");
            }
            return;
        }
        Role = SessionRole.Waiting;
        _waitingSince = now();
        net.Send(net.HostId, Packets.Hello(net.LocalId, platform, mods));
        Log?.Invoke($"sent the host (Mjolnir {net.HostPlatform}) {Describe(mods)}");
    }

    /// <summary>A player joined the session (the host checks clients; a client waits for the host's list).</summary>
    public void PeerJoined(int player)
    {
        if (Role != SessionRole.Host || player == net.LocalId || _verified.Contains(player)) return;
        _pending[player] = now();
    }

    public void PeerLeft(int player)
    {
        _pending.Remove(player);
        _kicks.Remove(player);
        if (!_verified.Remove(player)) return;
        PeerGone?.Invoke(player);
        if (Role == SessionRole.Host) SendPeers();
    }

    /// <summary>
    /// Data that arrived. True when it was Mjolnir's (the game must not see it), false when it is the game's own. The
    /// sender is the one the packet names: the game does not tell it.
    /// </summary>
    /// <remarks>
    /// A packet's sender cannot be checked against the network: in shared mode the runner reported a client's packet as the
    /// host's own, so its player reference proves nothing. So the host takes a client's mods only from a player that has
    /// not passed yet and is not the host, and a client takes answers only from the host's id; a player that writes
    /// another's id into its packets can still speak for it (to the host as a client not checked yet, to a client as the
    /// host), which no check of the header can tell.
    /// </remarks>
    public bool Receive(byte[] data)
    {
        if (!Packets.HasMagic(data)) return false;
        if (!Packets.IsOurs(data))
        {
            if (!_foreignFormatLogged) Log?.Invoke($"a Mjolnir packet of another format (version {(data.Length > 4 ? data[4] : 0)}) ignored: the other player runs another Mjolnir");
            _foreignFormatLogged = true;
            return true;
        }
        var packet = Packets.Read(data);
        if (packet == null)
        {
            Log?.Invoke("a broken Mjolnir packet ignored");
            return true;
        }
        int from = packet.Sender;
        switch (packet.Kind)
        {
            case PacketKind.Hello when Role == SessionRole.Host && from != net.LocalId && !_verified.Contains(from):
                Check(from, packet);
                break;
            case PacketKind.Accept when Role == SessionRole.Waiting && from == net.HostId:
                Role = SessionRole.Accepted;
                Log?.Invoke("the host accepted the mods");
                Verify(net.HostId);
                Verify(net.LocalId);
                break;
            case PacketKind.Refuse when Role == SessionRole.Waiting && from == net.HostId:
                Refuse(CompatibilityReport.Compare(packet.Platform, packet.Mods, platform, mods));
                break;
            case PacketKind.Peers when Role == SessionRole.Accepted && from == net.HostId:
                foreach (int gone in _verified.Where(p => !packet.Players.Contains(p) && p != net.LocalId && p != net.HostId).ToList())
                {
                    _verified.Remove(gone);
                    PeerGone?.Invoke(gone);
                }
                foreach (int player in packet.Players) Verify(player);
                break;
            case PacketKind.Channel when _verified.Contains(from) && from != net.LocalId:
                Message?.Invoke(from, packet.Channel, packet.Payload);
                break;
        }
        return true;
    }

    /// <summary>Time-outs and delayed removals; called every frame.</summary>
    public void Tick()
    {
        double t = now();
        if (Role == SessionRole.Host)
        {
            if (_pending.Count > 0) TimeOutHellos(t);
            if (_kicks.Count > 0) KickDue(t);
        }
        else if (Role == SessionRole.Waiting && t - _waitingSince >= AnswerTimeout)
        {
            Refuse(CompatibilityReport.NoAnswer(net.HostPlatform ?? "", platform));
        }
    }

    private void TimeOutHellos(double t)
    {
        foreach (var (player, since) in _pending.Where(p => t - p.Value >= HelloTimeout).ToList())
        {
            _pending.Remove(player);
            if (HasSharedMods)
            {
                Log?.Invoke($"player {player} sent no mods (no Mjolnir): removed, since the session needs {Describe(mods.Where(m => m.IsShared).ToList())}");
                net.Kick(player);
            }
            else
            {
                Log?.Invoke($"player {player} has no Mjolnir; no mod needs it, so it plays on without mod messages");
            }
        }
    }

    private void KickDue(double t)
    {
        foreach (var (player, at) in _kicks.Where(k => t >= k.Value).ToList())
        {
            _kicks.Remove(player);
            net.Kick(player);
        }
    }

    /// <summary>Sends a mod's message to a verified player; false when the player cannot take it.</summary>
    public bool Send(int to, uint channel, byte[] payload)
    {
        if (to == net.LocalId || !_verified.Contains(to)) return false;
        net.Send(to, Packets.ChannelMessage(net.LocalId, channel, payload));
        return true;
    }

    private void Check(int from, Packet hello)
    {
        _pending.Remove(from);
        var report = CompatibilityReport.Compare(platform, mods, hello.Platform, hello.Mods);
        if (report.IsCompatible)
        {
            _kicks.Remove(from);
            net.Send(from, Packets.Accept(net.LocalId));
            Log?.Invoke($"player {from} accepted: {Describe(hello.Mods)}");
            Verify(from);
            SendPeers();
            return;
        }
        Log?.Invoke($"player {from} refused: {report.Summary}");
        net.Send(from, Packets.Refuse(net.LocalId, platform, mods));
        _kicks[from] = now() + KickDelay;
    }

    private void Refuse(CompatibilityReport report)
    {
        Role = SessionRole.Refused;
        Refusal = report;
        Log?.Invoke($"leaving the session: {report.Summary}");
        net.Leave();
    }

    private void Verify(int player)
    {
        if (_verified.Add(player)) PeerVerified?.Invoke(player);
    }

    // Every accepted client learns who else passed, so it may message them too.
    private void SendPeers()
    {
        byte[] packet = Packets.Peers(net.LocalId, _verified.ToList());
        foreach (int player in _verified.Where(p => p != net.LocalId)) net.Send(player, packet);
    }

    private static string Describe(IReadOnlyCollection<ModStamp> list) =>
        list.Count == 0 ? "no mods" : $"{list.Count} mod(s): {string.Join(", ", list)}";
}
