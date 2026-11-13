using Fusion;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SSSGame.Network;

namespace Mjolnir.SDK.Net;

internal static partial class GameNetwork
{
    /// <summary>The session's view of one Fusion runner.</summary>
    private sealed class RunnerTransport(NetworkRunner runner) : INetTransport
    {
        public int LocalId => runner.LocalPlayer.PlayerId;

        // The session's master is player 0 (the game keeps it in PlayerManager._players[0]; seen when hosting, 2026-11-07),
        // which is how clients address it too.
        public int HostId => 0;

        public bool IsHost => LocalId == HostId;

        public string? HostPlatform
        {
            get
            {
                if (IsHost) return Platform;
                var properties = runner.SessionInfo?.Properties;
                if (properties == null || !properties.ContainsKey(PropertyKey)) return null;
                return properties[PropertyKey]?.PropertyValue?.ToString();
            }
        }

        public void Send(int player, byte[] packet)
        {
            if (Find(player) is { } target) runner.SendReliableDataToPlayer(target, new Il2CppStructArray<byte>(packet));
            else SdkLog.Warn(NetTexts.Source, $"Player {player} is not in the session: a packet was not sent");
        }

        public void Kick(int player)
        {
            if (Find(player) is { } target)
            {
                SdkLog.Info(NetTexts.Source, $"Removing player {player} from the session");
                NetworkJoinHandler.Rpc_Kick(runner, target);
            }
        }

        public void Leave()
        {
            var session = UnityEngine.Object.FindObjectOfType<NetworkSession>();
            if (session == null)
            {
                SdkLog.Warn(NetTexts.Source, "No network session to leave");
                return;
            }
            session.Close(true, LeaveReason);
        }

        // The runner reported every player of the session as it joined; the host may also be addressed before its join
        // arrived here, by the game's own encoding of a player reference (its id plus one).
        private PlayerRef? Find(int id)
        {
            if (s_refs.TryGetValue(id, out var known)) return known;
            if (id < 0) return null;
            return new PlayerRef { _index = id + 1 };
        }
    }
}
