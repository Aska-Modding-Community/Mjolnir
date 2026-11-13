using System;
using Il2CppInterop.Runtime;
using SandSailorStudio;
using SandSailorStudio.Streaming;
using SSSGame;
using SSSGame.Controllers;
using UnityEngine;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The local player's character in the game: the PlayerManager's record of the player and its
/// <see cref="PlayerCharacter"/> (its stats are its sheet, which the stats module reads). It moves through its
/// <see cref="PlayerDrive"/>, the game's own teleport.
/// </summary>
internal sealed class GameCharacter(PlayerManager.Player player, PlayerCharacter character) : IPlayerSource
{
    private PlayerDrive? _drive;

    public nint Id => character.Pointer;

    // Unity's null check: false once the game destroyed the object, though our wrapper still holds it.
    public bool IsAlive => character != null;

    public bool IsDead => character.IsDead;

    public string? Name => player.user?.Moniker is { Length: > 0 } name ? name : null;

    public IDisposable WatchDeath(Action<bool> deadChanged)
    {
        var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
            new Action<bool>(dead => GamePlayer.Guard("Character.OnDead", () => deadChanged(dead))))!;
        character.add_OnDead(handler);
        return new GameSubscription(GamePlayer.Source, () => character.remove_OnDead(handler));
    }

    public (float X, float Y, float Z) Position
    {
        get
        {
            var position = character.transform.position;
            return (position.x, position.y, position.z);
        }
    }

    // As the game's deeds move the player: PlayerDrive moves the character, the cameras and its network state in its
    // next FixedUpdate, and the world there streams in.
    public void Teleport(float x, float y, float z)
    {
        _drive ??= character.GetComponent<PlayerDrive>();
        if (_drive == null) SdkLog.Warn(GamePlayer.Source, "Teleport: the character has no PlayerDrive");
        else _drive.Teleport(new Vector3(x, y, z));
    }

    // Not while the world streams in after a teleport: a terrain chunk can answer before its heights are there.
    public float? GroundHeight(float x, float z)
    {
        var streaming = GameManagers.Get<WorldStreamingManager>();
        return streaming != null && streaming.IsTeleporting ? null : Ground.HeightAt(x, z);
    }
}
