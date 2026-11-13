using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SSSGame;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The player facade's runtime: its hidden Harmony patches (Game/PlayerPatches.cs), its tick, finding the local player in
/// the game, and its log. Nothing here throws into the game or into a mod: SDK faults are logged as [SDK.Player].
/// </summary>
internal static class GamePlayer
{
    public const string Source = "SDK.Player";
    private static readonly object s_lock = new();
    private static Harmony? s_harmony;
    private static bool s_staminaWanted;
    private static bool s_staminaPatched;

    /// <summary>
    /// The module's start (phase A: Harmony only, no Unity objects): follows the game setting and unsetting players, and
    /// checks every frame that the character still exists. Also patches the stamina method if a mod asked for it before.
    /// </summary>
    public static void Start()
    {
        try
        {
            Dispatcher.Hook(); // records the main thread at GameReady, which the stats rely on
            GameLoop.Update += Tick;
            lock (s_lock)
            {
                int patched = Install(typeof(SpawnPatches));
                SdkLog.Info(Source, $"Player facade started: {patched} game methods patched");
                if (s_staminaWanted) PatchStamina();
            }
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Player facade not started: {e}");
        }
    }

    /// <summary>A mod listens to <see cref="StaminaStat.Draining"/>: patch the game's stamina method once (now, or at the start).</summary>
    public static void WantStaminaPatch()
    {
        lock (s_lock)
        {
            if (s_staminaWanted) return;
            s_staminaWanted = true;
            if (s_harmony != null) PatchStamina();
        }
    }

    /// <summary>
    /// The game set or unset a player (main thread): look up which character the local player has now. Without a session
    /// the game's LocalPlayer throws; that is no character.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RefreshLocalPlayer()
    {
        PlayerManager.Player? local = null;
        try
        {
            local = GameManagers.Get<PlayerManager>()?.LocalPlayer;
        }
        catch (Exception)
        {
            // no session yet, or none any more
        }
        var character = local?.playerCharacter;
        LocalPlayer.Track(local != null && character != null ? new GameCharacter(local, character) : null);
    }

    /// <summary>Runs SDK code from a game callback (a Harmony patch, a game event): a fault is logged, never thrown into the game.</summary>
    public static void Guard(string what, Action body) => GameGuard.Run(Source, what, body);

    // Every frame: the first fault is logged, not one per frame.
    private static void Tick() => GameGuard.RunLogOnce(Source, "Player tick", LocalPlayer.Tick);

    private static void PatchStamina()
    {
        if (s_staminaPatched) return;
        s_staminaPatched = true;
        try
        {
            SdkLog.Info(Source, $"Stamina.Draining: {Install(typeof(StaminaPatches))} game method patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Stamina.Draining not installed: {e}");
        }
    }

    // Harmony and game types are touched only here, after Il2CppInterop is up. The count
    // catches a patch class Harmony skipped silently (it did once: no class-level [HarmonyPatch]).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Install(Type patches)
    {
        s_harmony ??= new Harmony("mjolnir.sdk.player");
        return s_harmony.CreateClassProcessor(patches).Patch()?.Count ?? 0;
    }
}
