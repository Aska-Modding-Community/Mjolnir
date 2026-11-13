using System;

namespace Mjolnir.SDK.Player;

/// <summary>The stats of the local player, in the order <see cref="LocalPlayer"/> lists them.</summary>
internal enum StatKind
{
    Health,
    Stamina,
    Food,
    Water,
    Warmth,
}

/// <summary>
/// The game side of the local player's character (Game/GameCharacter.cs), behind an interface so that the model
/// (<see cref="LocalPlayer"/>) is tested without the game; its stats are the character's sheet (<see cref="CharacterSheet"/>,
/// the stats module's). Called on the main thread only.
/// </summary>
internal interface IPlayerSource
{
    /// <summary>The same character has the same id (its IL2CPP object); a new character, a new id. Never 0.</summary>
    nint Id { get; }

    /// <summary>False once the game destroyed the character (its world was unloaded).</summary>
    bool IsAlive { get; }

    bool IsDead { get; }

    /// <summary>The player's name, or null while the game has none.</summary>
    string? Name { get; }

    /// <summary>Calls <paramref name="deadChanged"/> with true when the character dies and false when it comes back.</summary>
    IDisposable WatchDeath(Action<bool> deadChanged);

    /// <summary>The character's place in the world (y up), in metres.</summary>
    (float X, float Y, float Z) Position { get; }

    /// <summary>
    /// Moves the character there by the game's own teleport, at its next physics step (<see cref="Position"/> shows the
    /// old place until then); far away, the world there streams in.
    /// </summary>
    void Teleport(float x, float y, float z);

    /// <summary>The height of the ground at (x, z), or null while the world is streaming in or has nothing there to stand on.</summary>
    float? GroundHeight(float x, float z);
}

