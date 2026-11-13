using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SSSGame;
using SSSGame.AI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The villager module's runtime: its hidden Harmony patches (villagers spawned and removed, the
/// priority of the SDK's job quests), the job step injected into the game once it is ready, a tick that keeps the residents in step with the game's population
/// and gives villagers the jobs every second, and spawning. Nothing here throws into the game or into a mod: SDK faults
/// are logged as [SDK.AI].
/// </summary>
internal static class GameVillagers
{
    public const string Source = "SDK.AI";
    private static bool s_injected;
    private static float s_nextCheck;

    /// <summary>The module's start (phase A: Harmony only, no Unity objects).</summary>
    public static void Start()
    {
        try
        {
            Dispatcher.Hook(); // records the main thread at GameReady
            GameLoop.GameReady += () => Guard("injecting the job step", Inject);
            // Every frame: a fault that repeats is logged once.
            GameLoop.Update += () => GameGuard.RunLogOnce(Source, "Villagers: following the villagers", Tick);
            GameLoop.Update += () => GameGuard.RunLogOnce(Source, "Villagers: following the interactions", GameInteractions.Tick);
            SdkLog.Info(Source, $"Villagers started: {Install()} game methods patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Villagers not started: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Install()
    {
        var harmony = new Harmony("mjolnir.sdk.ai");
        return (harmony.CreateClassProcessor(typeof(AiPatches)).Patch()?.Count ?? 0)
               + (harmony.CreateClassProcessor(typeof(InteractionPatches)).Patch()?.Count ?? 0);
    }

    /// <summary>Whether the job step type is in the game (phase B), so jobs can be given to villagers.</summary>
    public static bool Injected => s_injected;

    // A state machine step of the SDK's own, so the game runs the mods' code in its villagers' state machines (no fields:
    // the state lives in the SDK's tables, by pointer).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Inject()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<JobAction>()) ClassInjector.RegisterTypeInIl2Cpp<JobAction>();
        s_injected = true;
        SdkLog.Info(Source, "Villagers: job steps injected");
    }

    /// <summary>
    /// The game spawned a villager: a new one is a resident at once, with its jobs. A villager of a save is spawned before
    /// its quest runner can take quests (adding one then crashed the game) and before the save gives it its name (it was
    /// announced under another, seen 2026-10-28): the tick takes it in once the population has loaded.
    /// </summary>
    public static void Spawned(Villager villager)
    {
        if (Population() is not { IsLoading: false } || !JobQuests.Ready(villager)) return;
        ResidentRoster.Track(new GameVillager(villager));
        JobQuests.Give(villager);
    }

    /// <summary>The game removes a villager from its population.</summary>
    public static void Removing(Villager villager)
    {
        JobQuests.Forget(villager.Pointer);
        ResidentRoster.Forget(villager.Pointer);
    }

    // Every second: those whose world unloaded are let go, and every villager that is ready is taken in (one of a save, one
    // the patches missed), gets the jobs it lacks (jobs added later too), and its quest runner hears of new priorities.
    private static void Tick()
    {
        if (Time.realtimeSinceStartup < s_nextCheck) return;
        s_nextCheck = Time.realtimeSinceStartup + 1;
        foreach (nint gone in ResidentRoster.Sweep()) JobQuests.Forget(gone);
        if (Population() is not { IsLoading: false } population) return;
        var all = population.GetAllVillagers();
        for (int i = 0; all != null && i < all.Count; i++)
        {
            var villager = all[i];
            if (villager == null || !JobQuests.Ready(villager)) continue;
            if (ResidentRoster.Find(villager.Pointer) == null) ResidentRoster.Track(new GameVillager(villager));
            JobQuests.Give(villager);
            JobQuests.Reevaluate(villager);
        }
    }

    public static bool Spawn(Vector3 position)
    {
        if (!GameLoop.IsGameReady || !Dispatcher.IsMainThread) return false;
        var population = Population();
        if (population == null) return false;
        int kinds = population.villagerDefinitions?.Length ?? 0;
        try
        {
            population.TryAddVillager(position, 3f, kinds > 0 ? Random.Range(0, kinds) : 0, true);
            return true;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Source, $"Residents.Spawn failed: {e.Message}");
            return false;
        }
    }

    /// <summary>A place <paramref name="distance"/> metres in front of the local player, null without one (the console).</summary>
    public static Vector3? InFrontOfPlayer(float distance)
    {
        try
        {
            var body = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.transform;
            if (body == null) return null;
            var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            return body.position + forward * distance;
        }
        catch (Exception)
        {
            return null; // the game's LocalPlayer throws outside a session
        }
    }

    private static PopulationManager? Population()
    {
        try
        {
            return GameManagers.Get<PopulationManager>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Runs SDK code from a game callback: a fault is logged, never thrown into the game.</summary>
    public static void Guard(string what, Action body) => GameGuard.Run(Source, $"Villagers: {what}", body);
}

/// <summary>Hidden patches behind <see cref="Residents"/> (the hybrid Harmony rule: mods never see them).</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class AiPatches
{
    // Villager.Spawned tells the population of each villager: a new one and one loaded with a save.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PopulationManager), nameof(PopulationManager.OnVillagerSpawned))]
    private static void Spawned(Villager __0) => GameVillagers.Guard("a spawned villager", () => GameVillagers.Spawned(__0));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PopulationManager), nameof(PopulationManager.RemoveVillager))]
    private static void Removing(Villager __0) => GameVillagers.Guard("a removed villager", () => GameVillagers.Removing(__0));

    // A job's quest is a CaveCollapsingQuest of the SDK's: its priority is the job's. The game's own run as they do.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CaveCollapsingQuest), nameof(CaveCollapsingQuest.GetPriority))]
    private static bool Priority(CaveCollapsingQuest __instance, QuestData __0, ref float __result)
    {
        if (!JobQuests.Priority(__instance, __0, out float priority)) return true;
        __result = priority;
        return false;
    }
}
