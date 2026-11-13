using System;
using System.Threading;
using HarmonyLib;
using Mjolnir.SDK;
using SSSGame;
using SSSGame.Controllers;
using UnityEngine.SceneManagement;

namespace Mjolnir.SmokeMod;

/// <summary>
/// In-game smoke test for the loader, deployed with deploy.ps1 -SmokeMod.
/// <list type="bullet">
/// <item>Step 4: HarmonyX patches on IL2CPP methods fire. The menu patches only log; the stamina patches skip
/// the original, so stamina never drains (sprint in a loaded world to see it).</item>
/// <item>Step 5: the phase B callbacks arrive, OnFixedUpdate included.</item>
/// <item>Step 6: it is a Mod with a config (Mjolnir/Config/mjolnir.test.smokemod.toml). Mjolnir.FaultMod
/// depends on it, so it must load first although its id sorts later.</item>
/// </list>
/// </summary>
[ModManifest("mjolnir.test.smokemod", "SmokeMod", "0.1.0")]
public sealed class SmokeMod : Mod
{
    private int _update;
    private int _lateUpdate;
    private int _fixedUpdate;

    public SmokeMod() => Smoke.Logger = Log;

    public override void OnLoad()
    {
        Patch(typeof(MenuStateActivatePatch), typeof(MainMenuStartPatch),
              typeof(TryDrainStaminaPatch), typeof(MovementDrainStaminaPatch), typeof(CharacterDrainStaminaPatch));

        if (Config.Bind("Probes", "Enabled", true, "Logging-only probes on the methods listed in Probes.cs.").Value)
        {
            Probes.Apply(Harmony);
            Patch(typeof(StartSessionProbe), typeof(PlayerJoinedProbe));
        }
    }

    // One class at a time, so a failing patch is reported without hiding the others.
    private void Patch(params Type[] patches)
    {
        foreach (var patch in patches)
        {
            try
            {
                Harmony.CreateClassProcessor(patch).Patch();
                Log.Info($"{patch.Name} applied");
            }
            catch (Exception e)
            {
                Log.Error($"{patch.Name} failed: {e}");
            }
        }
    }

    public override void OnGameReady() => Log.Info($"OnGameReady in scene '{SceneManager.GetActiveScene().name}'");

    public override void OnUpdate() => Smoke.Hit("OnUpdate", ref _update);

    public override void OnLateUpdate() => Smoke.Hit("OnLateUpdate", ref _lateUpdate);

    public override void OnFixedUpdate() => Smoke.Hit("OnFixedUpdate", ref _fixedUpdate);

    public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
        Log.Info($"OnSceneLoaded '{scene.name}' (build index {scene.buildIndex}, {mode})");
}

/// <summary>Static helpers for the Harmony patch classes, which have no access to the mod instance.</summary>
internal static class Smoke
{
    public static ModLogger Logger = null!;

    /// <summary>Logs the first three calls, then the 100th, 1000th, 10 000th and 100 000th.</summary>
    public static void Hit(string what, ref int counter)
    {
        int n = Interlocked.Increment(ref counter);
        if (n <= 3 || n is 100 or 1000 or 10_000 or 100_000) Logger.Info($"{what} hit #{n}");
    }
}

[HarmonyPatch(typeof(GameState_Menu), nameof(GameState_Menu.OnActivate))]
internal static class MenuStateActivatePatch
{
    private static int s_calls;
    private static void Postfix() => Smoke.Hit("GameState_Menu.OnActivate", ref s_calls);
}

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
internal static class MainMenuStartPatch
{
    private static int s_calls;
    private static void Postfix() => Smoke.Hit("MainMenu.Start", ref s_calls);
}

[HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.TryDrainStamina))]
internal static class TryDrainStaminaPatch
{
    private static int s_calls;

    private static bool Prefix(float stamina, ref bool __result)
    {
        Smoke.Hit($"CharacterMovement.TryDrainStamina({stamina})", ref s_calls);
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.DrainStamina))]
internal static class MovementDrainStaminaPatch
{
    private static int s_calls;

    private static bool Prefix(float stamina, DrainStaminaUsage usage)
    {
        Smoke.Hit($"CharacterMovement.DrainStamina({stamina}, {usage})", ref s_calls);
        return false;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.DrainStamina))]
internal static class CharacterDrainStaminaPatch
{
    private static int s_calls;

    private static bool Prefix(float value, DrainStaminaUsage usage)
    {
        Smoke.Hit($"Character.DrainStamina({value}, {usage})", ref s_calls);
        return false;
    }
}
