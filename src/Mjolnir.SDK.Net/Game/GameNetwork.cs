using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mjolnir.SDK.Threading;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Network;
using SSSGame.UI;
using UnityEngine;

namespace Mjolnir.SDK.Net;

/// <summary>
/// The network module in the game: the game's session runs Photon Fusion in shared mode, the host
/// being its master, player 0. Hidden patches:
/// <list type="bullet">
/// <item><c>NetworkSession._SetUpSessionProperties</c>: the host announces its Mjolnir version in the session's properties;</item>
/// <item>the runner's <c>PlayerJoined</c>/<c>PlayerLeft</c>: the session starts at the local player's join, and follows the others;</item>
/// <item>the runner's <c>OnReliableData</c>: Mjolnir's packets are taken before the game's world streaming sees them, the
/// game's own go on untouched;</item>
/// <item><c>NetworkSession.OnShutdown</c>: the session ends;</item>
/// <item><c>MainMenu.DisplayDisconnectionMessage</c>, <c>MainMenu.OnActivate</c>, <c>MainMenu.Start</c>: a refused client sees why
/// in the main menu, and not the game's own message about leaving.</item>
/// </list>
/// </summary>
internal static partial class GameNetwork
{
    private const string PropertyKey = "mjolnir";
    // A refused client leaves as a player leaving of their own accord: with the game's "kicked" (101) the main menu shows
    // its own message too, each time a menu over it closes (MainMenu.OnActivate), and it stayed on screen (seen 2026-11-07).
    private const ShutdownReason LeaveReason = ShutdownReason.Ok;
    private static IntPtr s_runner;
    // The players of the current session as the runner reported them: a packet needs the game's own player reference.
    private static readonly System.Collections.Generic.Dictionary<int, PlayerRef> s_refs = [];
    private static CompatibilityReport? s_pendingRefusal;
    private static bool s_installed;

    /// <summary>The platform's version as players write it (0.3.0).</summary>
    internal static string Platform
    {
        get
        {
            var v = typeof(Mod).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    /// <summary>The module's start (phase A): the patches, the per-frame tick.</summary>
    public static void Install()
    {
        if (s_installed) return;
        s_installed = true;
        try
        {
            var harmony = new Harmony("mjolnir.sdk.net");
            harmony.CreateClassProcessor(typeof(NetPatches)).Patch();
            GameLoop.Update += Tick;
            SdkLog.Info(NetTexts.Source, $"Network patches installed: {harmony.GetPatchedMethods().Count()} method(s)");
        }
        catch (Exception e)
        {
            SdkLog.Error(NetTexts.Source, $"Network patches not installed, sessions are not checked: {e}");
        }
    }

    // The mods this player runs, as the handshake compares them.
    private static ModStamp[] LocalMods() =>
        [.. ModRegistry.Loaded.Select(m => new ModStamp(m.Id, m.Name, m.VersionText, m.Network)).OrderBy(m => m.Id, StringComparer.Ordinal)];

    private static void Tick() => GameGuard.Run(NetTexts.Source, "Session tick", () => Network.Session?.Tick());

    internal static void AnnounceSession(Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty> properties)
    {
        properties[PropertyKey] = Il2CppStringProperty(Platform);
        SdkLog.Info(NetTexts.Source, $"The session announces Mjolnir {Platform}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SessionProperty Il2CppStringProperty(string value) => value;

    internal static void PlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.GameMode != GameMode.Shared) return; // single player: no one to check
        if (player.PlayerId == runner.LocalPlayer.PlayerId)
        {
            if (s_runner == runner.Pointer && Network.Session != null) return;
            if (s_runner != runner.Pointer) s_refs.Clear();
            s_runner = runner.Pointer;
            s_refs[player.PlayerId] = player;
            s_pendingRefusal = null;
            var transport = new RunnerTransport(runner);
            var session = new NetSession(transport, Platform, LocalMods(), () => Time.realtimeSinceStartupAsDouble);
            session.Log += line => SdkLog.Info(NetTexts.Source, line);
            Network.SetSession(session);
            SdkLog.Info(NetTexts.Source, $"In a session as player {transport.LocalId} (reference {player._index}, " +
                                         $"{(transport.IsHost ? "host" : "client")}, the host announces {transport.HostPlatform ?? "no Mjolnir"})");
            if (transport.IsHost)
            {
                var properties = runner.SessionInfo?.Properties;
                bool announced = properties != null && properties.ContainsKey(PropertyKey);
                SdkLog.Info(NetTexts.Source, announced
                    ? $"The session's properties carry Mjolnir {properties![PropertyKey]?.PropertyValue?.ToString()}"
                    : "The session's properties do not carry Mjolnir: clients will take this host for one without it");
            }
            session.Start();
            return;
        }
        if (s_runner != runner.Pointer && s_runner != IntPtr.Zero) return;
        s_refs[player.PlayerId] = player;
        Network.Session?.PeerJoined(player.PlayerId);
    }

    internal static void PlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (s_runner != runner.Pointer || Network.Session is not { } session) return;
        s_refs.Remove(player.PlayerId);
        if (player.PlayerId == runner.LocalPlayer.PlayerId) End("the local player left");
        else session.PeerLeft(player.PlayerId);
    }

    /// <summary>
    /// Whether the data starts with Mjolnir's header: such a packet is Mjolnir's, whatever its format or body, and the game
    /// must never see it. Asked of every packet the game receives, so it makes nothing and never throws.
    /// </summary>
    internal static bool HasMagic(Il2CppStructArray<byte> data)
    {
        try
        {
            return data != null && data.Length >= 4 && data[0] == (byte)'A' && data[1] == (byte)'K' && data[2] == (byte)'L' && data[3] == (byte)'N';
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A packet with Mjolnir's header (<see cref="HasMagic"/>), read by the session.</summary>
    internal static void Received(NetworkRunner runner, Il2CppStructArray<byte> data)
    {
        if (s_runner != runner.Pointer || Network.Session is not { } session) return; // ours, but no session to read it
        session.Receive(data.ToArray());
    }

    internal static void End(string why)
    {
        var session = Network.Session;
        if (session == null) return;
        if (session.Refusal != null) s_pendingRefusal = session.Refusal;
        s_runner = IntPtr.Zero;
        s_refs.Clear();
        Network.SetSession(null);
        SdkLog.Info(NetTexts.Source, $"The session ended: {why}");
    }

    /// <summary>Whether a refusal waits to be shown (the game's own disconnection message then stays away).</summary>
    internal static bool HasPendingRefusal => s_pendingRefusal != null || Network.Session?.Refusal != null;

    /// <summary>Shows a waiting refusal once the main menu is up; tries again on later frames until the menus are idle.</summary>
    internal static void ShowPendingRefusal()
    {
        var report = s_pendingRefusal ?? Network.Session?.Refusal;
        if (report == null) return;
        Dispatcher.Run(() => TryShow(report, attempts: 600));
    }

    private static void TryShow(CompatibilityReport report, int attempts)
    {
        if (s_pendingRefusal != report && Network.Session?.Refusal != report) return; // shown already
        var menus = MenuManager.Instance;
        if ((menus == null || menus.IsWorking) && attempts > 0)
        {
            Dispatcher.Enqueue(() => TryShow(report, attempts - 1), "Net refusal");
            return;
        }
        s_pendingRefusal = null;
        try
        {
            ForgetDisconnectReason();
            ShowRefusal(report);
        }
        catch (Exception e)
        {
            SdkLog.Error(NetTexts.Source, $"The refusal could not be shown ({report.Summary}): {e}");
        }
    }

    // The main menu shows the reason of the last disconnection (MainMenu._disconnectReason) whenever it becomes the top
    // menu again: a refusal is the module's to show, so the game's reason is cleared (a host's removal may have set it).
    internal static void ForgetDisconnectReason()
    {
        var menu = Menu.GetInstance<MainMenu>();
        if (menu != null && menu._disconnectReason != ShutdownReason.Ok)
        {
            SdkLog.Info(NetTexts.Source, $"The main menu's own disconnection message ({menu._disconnectReason}) is replaced by the refusal");
            menu._disconnectReason = ShutdownReason.Ok;
        }
    }
}
