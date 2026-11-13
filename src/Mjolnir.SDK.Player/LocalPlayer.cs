using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The player playing on this computer: their character in the loaded world, its stats and its place, and when it
/// spawns, despawns, dies and comes back. Outside a world (the main menu) there is no character: <see cref="IsSpawned"/>
/// is false and every stat reads 0. Events are raised on the main thread; static events have a null sender.
/// </summary>
/// <example><code>
/// LocalPlayer.Spawned += (s, e) => Log.Info($"{LocalPlayer.Name} has {LocalPlayer.Health.Value} health");
/// LocalPlayer.Stamina.Draining += (s, e) => e.Amount *= 0.5f;
/// healthBar.Bind(UiProgressBar.ValueProperty, LocalPlayer.Health);
/// await LocalPlayer.TeleportToGroundAsync(LocalPlayer.Position.x + 300, LocalPlayer.Position.z);
/// </code></example>
public static class LocalPlayer
{
    private static readonly ModHandlers<EventHandler> s_spawnedHandlers = new("LocalPlayer.Spawned");
    private static readonly ModHandlers<EventHandler> s_despawnedHandlers = new("LocalPlayer.Despawned");
    private static readonly ModHandlers<EventHandler> s_diedHandlers = new("LocalPlayer.Died");
    private static readonly ModHandlers<EventHandler> s_revivedHandlers = new("LocalPlayer.Revived");
    // The character and its death subscription: main thread only. What other threads read is below.
    private static IPlayerSource? s_player;
    private static IDisposable? s_deathWatch;
    private static volatile bool s_isSpawned;
    private static volatile bool s_isDead;
    private static volatile string? s_name;
    private static nint s_characterId;
    // The place last seen on the main thread (once a frame), for other threads; and a ground teleport waiting for the
    // world to stream in (main thread only).
    private static readonly object s_positionLock = new();
    private static (float X, float Y, float Z) s_position;
    private static GroundTeleport? s_ground;

    // Above the ground found, so the character is not put into it; how near the place the character must be before the
    // ground there counts (the game moves it at its next physics step); and how long a ground teleport waits.
    private const float GroundClearance = 0.1f;
    private const float Arrived = 2f;
    private const int GroundTimeoutSeconds = 10;

    /// <summary>Health.</summary>
    public static PlayerStat Health { get; } = new(StatKind.Health);

    /// <summary>Stamina, spent by sprinting, harvesting and combat; <see cref="StaminaStat.Draining"/> sees each cost first.</summary>
    public static StaminaStat Stamina { get; } = new();

    /// <summary>Food, dropping all the time.</summary>
    public static PlayerStat Food { get; } = new(StatKind.Food);

    /// <summary>Water, dropping all the time.</summary>
    public static PlayerStat Water { get; } = new(StatKind.Water);

    /// <summary>Warmth.</summary>
    public static PlayerStat Warmth { get; } = new(StatKind.Warmth);

    /// <summary>
    /// What the character is made of: characteristics, skills, status effects. It belongs to the character of now, so take it
    /// again after <see cref="Spawned"/>; without a character it is empty.
    /// </summary>
    public static CharacterSheet Sheet => CharacterSheet.Of(s_player?.Id ?? 0);

    /// <summary>
    /// The player as a combatant: health, damage, healing, death (<see cref="Combatants"/>); an empty one while there is no
    /// character. The same object as in <see cref="Combatants.All"/>.
    /// </summary>
    public static Combatant Combatant => SDK.Combatant.Of(s_player?.Id ?? 0);

    /// <summary>
    /// The character's animations: the game's and mods' animations played over its own, the game's emotes, its bones. It
    /// belongs to the character of now, so take it again after <see cref="Spawned"/>; without a character nothing plays.
    /// </summary>
    public static CharacterAnimator Animator => CharacterAnimator.Of(s_player?.Id ?? 0);

    // After the stats: static fields are initialised in the order they are written.
    private static readonly PlayerStat[] s_stats = [Health, Stamina, Food, Water, Warmth];

    /// <summary>Whether the local player's character is in a loaded world.</summary>
    public static bool IsSpawned => s_isSpawned;

    /// <summary>Whether the character is dead (between <see cref="Died"/> and <see cref="Revived"/>); false without one.</summary>
    public static bool IsDead => s_isDead;

    /// <summary>The player's name as the game shows it (their platform name), or null without a character.</summary>
    public static string? Name
    {
        get
        {
            // The game may learn the name after the character spawned: ask again while it has none.
            if (s_name == null && s_player != null && Dispatcher.IsMainThread) s_name = s_player.Name;
            return s_name;
        }
    }

    /// <summary>
    /// The character's place in the world (y up, in metres); zero without a character. On another thread, the place last
    /// seen on the main thread (once a frame).
    /// </summary>
    public static Vector3 Position
    {
        get
        {
            var (x, y, z) = PositionNow;
            return new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// Moves the character to <paramref name="position"/> by the game's own teleport, at its next physics step
    /// (<see cref="Position"/> shows the old place until then). Far away the world there streams in within a second or
    /// so, and a character put above the ground falls onto it. False without a character. From another thread it moves
    /// on the next frame.
    /// </summary>
    public static bool Teleport(Vector3 position) => TeleportTo(position.x, position.y, position.z);

    /// <summary>
    /// Moves the character onto the ground at (<paramref name="x"/>, <paramref name="z"/>): there at once, and onto the
    /// ground (the terrain, else the highest thing to stand on) as soon as the world there has streamed in. In the sea the
    /// ground is its bottom, and the character swims up. The task gives
    /// true once the character is sent there (it stands there from the next physics step); false without a character, if
    /// it despawns first, if another teleport comes first, or if no ground turns up within ten seconds.
    /// </summary>
    public static Task<bool> TeleportToGroundAsync(float x, float z) =>
        LandAsync(x, z).ContinueWith(static landed => landed.Result != null, TaskContinuationOptions.ExecuteSynchronously);

    /// <summary><see cref="TeleportToGroundAsync"/> giving the place the character was sent to, or null.</summary>
    internal static Task<(float X, float Y, float Z)?> LandAsync(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z)) throw new ArgumentException($"Not a place in the world: ({x}, {z})");
        var done = new TaskCompletionSource<(float X, float Y, float Z)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!s_isSpawned) done.SetResult(null);
        else Dispatcher.Run(() => StartGroundTeleport(x, z, done));
        return done.Task;
    }

    /// <summary>
    /// Raised when the local player's character is in a world (a save was loaded, a session joined); the HUD comes up a
    /// frame later. The stats read the character already, but the game fills in the saved values during this frame and
    /// the next few (stamina's limit two frames later), and <see cref="PlayerStat.Changed"/> reports each: follow a stat
    /// with Changed or a binding rather than reading it once here.
    /// </summary>
    public static event EventHandler? Spawned
    {
        add => s_spawnedHandlers.Add(value);
        remove => s_spawnedHandlers.Remove(value);
    }

    /// <summary>Raised when the character left the world (its world was unloaded, or the session ended); the stats read 0 again.</summary>
    public static event EventHandler? Despawned
    {
        add => s_despawnedHandlers.Add(value);
        remove => s_despawnedHandlers.Remove(value);
    }

    /// <summary>Raised when the character dies.</summary>
    public static event EventHandler? Died
    {
        add => s_diedHandlers.Add(value);
        remove => s_diedHandlers.Remove(value);
    }

    /// <summary>Raised when the dead character comes back to life.</summary>
    public static event EventHandler? Revived
    {
        add => s_revivedHandlers.Add(value);
        remove => s_revivedHandlers.Remove(value);
    }

    /// <summary>The character's id (<see cref="IPlayerSource.Id"/>), 0 without one: what the stamina patch compares.</summary>
    internal static nint CharacterId => Volatile.Read(ref s_characterId);

    /// <summary>
    /// <see cref="Position"/> as numbers (unit tests cannot make a Vector3). A character the game destroyed (its world
    /// unloaded, not yet despawned by the tick) gives the place last seen.
    /// </summary>
    internal static (float X, float Y, float Z) PositionNow
    {
        get
        {
            if (Dispatcher.IsMainThread && Live() is { } player) return player.Position;
            lock (s_positionLock) return s_position;
        }
    }

    /// <summary><see cref="Teleport"/> as numbers.</summary>
    internal static bool TeleportTo(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new ArgumentException($"Not a place in the world: ({x}, {y}, {z})");
        if (!s_isSpawned) return false;
        if (Dispatcher.IsMainThread) return TeleportNow(x, y, z);
        Dispatcher.Run(() => TeleportNow(x, y, z));
        return true;
    }

    /// <summary>The time a ground teleport waits by (<see cref="Stopwatch"/> ticks; unit tests set their own).</summary>
    internal static Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>
    /// The game's local character now (the facade calls it on the main thread whenever the game sets or unsets a player):
    /// another character despawns the old one and spawns the new one; the same one changes nothing.
    /// </summary>
    internal static void Track(IPlayerSource? player)
    {
        var current = s_player;
        if (current == null ? player == null : player != null && player.Id == current.Id) return;
        if (current != null)
        {
            Detach();
            s_despawnedHandlers.Raise(static h => h(null, EventArgs.Empty));
        }
        if (player != null)
        {
            Attach(player);
            s_spawnedHandlers.Raise(static h => h(null, EventArgs.Empty));
        }
    }

    /// <summary>
    /// Every frame (main thread): a character the game destroyed despawns (the game does not always unset it when a world
    /// unloads), and stats the character did not have yet are looked up again.
    /// </summary>
    internal static void Tick()
    {
        var player = s_player;
        if (player == null) return;
        if (!player.IsAlive)
        {
            Track(null);
            return;
        }
        foreach (var stat in s_stats) stat.Resolve();
        var position = player.Position;
        lock (s_positionLock) s_position = position;
        if (s_ground != null) LandOnGround(player, s_ground);
    }

    /// <summary>Unit tests: forget the character without raising anything.</summary>
    internal static void ResetForTests()
    {
        if (s_player != null) Detach();
        Clock = Stopwatch.GetTimestamp;
    }

    private static void Attach(IPlayerSource player)
    {
        s_player = player;
        Volatile.Write(ref s_characterId, player.Id);
        s_isSpawned = true;
        s_isDead = player.IsDead;
        s_name = player.Name;
        s_deathWatch = player.WatchDeath(DeadChanged);
        var sheet = Sheet;
        foreach (var stat in s_stats) stat.Attach(sheet);
    }

    private static void Detach()
    {
        CancelGroundTeleport();
        lock (s_positionLock) s_position = default;
        s_deathWatch?.Dispose();
        s_deathWatch = null;
        s_player = null;
        Volatile.Write(ref s_characterId, 0);
        s_isSpawned = false;
        s_isDead = false;
        s_name = null;
        foreach (var stat in s_stats) stat.Detach();
    }

    // The character while the game still has it (main thread): between a world's unload and the next tick it is
    // destroyed but not yet despawned, and its transform cannot be read.
    private static IPlayerSource? Live() => s_player is { IsAlive: true } player ? player : null;

    private static bool TeleportNow(float x, float y, float z)
    {
        if (Live() is not { } player) return false;
        CancelGroundTeleport();
        player.Teleport(x, y, z);
        return true;
    }

    // On the ground at once where the world is there already; else there at the same height, so the world streams in,
    // and onto the ground when it has (Tick).
    private static void StartGroundTeleport(float x, float z, TaskCompletionSource<(float X, float Y, float Z)?> done)
    {
        var player = Live();
        if (player == null)
        {
            done.TrySetResult(null);
            return;
        }
        CancelGroundTeleport();
        if (player.GroundHeight(x, z) is { } height)
        {
            player.Teleport(x, height + GroundClearance, z);
            done.TrySetResult((x, height + GroundClearance, z));
            return;
        }
        player.Teleport(x, player.Position.Y, z);
        s_ground = new GroundTeleport(x, z, Clock() + GroundTimeoutSeconds * Stopwatch.Frequency, done);
    }

    private static void LandOnGround(IPlayerSource player, GroundTeleport ground)
    {
        var (x, _, z) = player.Position;
        bool there = Math.Abs(x - ground.X) < Arrived && Math.Abs(z - ground.Z) < Arrived;
        if (there && player.GroundHeight(ground.X, ground.Z) is { } height)
        {
            s_ground = null;
            player.Teleport(ground.X, height + GroundClearance, ground.Z);
            ground.Done.TrySetResult((ground.X, height + GroundClearance, ground.Z));
        }
        else if (Clock() > ground.Deadline)
        {
            s_ground = null;
            ground.Done.TrySetResult(null);
        }
    }

    private static void CancelGroundTeleport()
    {
        var ground = s_ground;
        s_ground = null;
        ground?.Done.TrySetResult(null);
    }

    private sealed record GroundTeleport(float X, float Z, long Deadline, TaskCompletionSource<(float X, float Y, float Z)?> Done);

    private static void DeadChanged(bool dead)
    {
        if (s_isDead == dead) return;
        s_isDead = dead;
        (dead ? s_diedHandlers : s_revivedHandlers).Raise(static h => h(null, EventArgs.Empty));
    }
}
