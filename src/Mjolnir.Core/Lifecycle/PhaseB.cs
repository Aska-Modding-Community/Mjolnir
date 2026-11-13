extern alias unity;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Mjolnir.Core.Interop;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using unity::UnityEngine;
using unity::UnityEngine.Events;
using unity::UnityEngine.SceneManagement;
using Object = unity::UnityEngine.Object;

namespace Mjolnir.Core.Lifecycle;

/// <summary>
/// Phase B: starts when the engine reports its first scene, i.e. once Unity is up.
/// Injects <see cref="MjolnirBehaviour"/>, parks it on a hidden DontDestroyOnLoad GameObject, hooks
/// SceneManager.sceneLoaded and raises <see cref="GameEvents.GameReady"/>.
/// </summary>
internal static class PhaseB
{
    private static int s_started;

    /// <summary>Phase A: arms the trigger. Never throws; without Il2CppInterop there is no phase B.</summary>
    public static void TryArm()
    {
        if (!InteropRuntime.Started) return;
        try
        {
            Arm();
        }
        catch (Exception e)
        {
            Log.Error("PhaseB", "Could not arm phase B; mods get no GameReady/Update: " + e);
        }
    }

    // Separate, non-inlined method: HarmonyLib and the interop UnityEngine assembly load only once
    // Il2CppInterop is up.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Arm()
    {
        // The engine calls both through il2cpp_runtime_invoke on every scene change (so they cannot be
        // inlined away); whichever comes first starts phase B, later calls are no-ops.
        var harmony = new Harmony("mjolnir.core");
        var trigger = new HarmonyMethod(typeof(PhaseB), nameof(OnEngineSceneCallback));
        harmony.Patch(AccessTools.Method(typeof(SceneManager), nameof(SceneManager.Internal_ActiveSceneChanged)), postfix: trigger);
        harmony.Patch(AccessTools.Method(typeof(SceneManager), nameof(SceneManager.Internal_SceneLoaded)), postfix: trigger);
        Log.Info("PhaseB", "Armed: phase B starts at the engine's first scene callback");
    }

    // Harmony postfix, called from native code: must never throw.
    private static void OnEngineSceneCallback(MethodBase __originalMethod)
    {
        if (Interlocked.Exchange(ref s_started, 1) != 0) return;
        try
        {
            Start(__originalMethod.Name);
        }
        catch (Exception e)
        {
            Log.Error("PhaseB", "Phase B failed to start; mods get no GameReady/Update: " + e);
            return;
        }
        GameEvents.RaiseGameReady();
    }

    private static void Start(string trigger)
    {
        var watch = Stopwatch.StartNew();
        ClassInjector.RegisterTypeInIl2Cpp<MjolnirBehaviour>();

        var host = new GameObject("Mjolnir");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent(Il2CppType.Of<MjolnirBehaviour>());

        SceneManager.add_sceneLoaded(DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(
            new Action<Scene, LoadSceneMode>(GameEvents.RaiseSceneLoaded)));

        Log.Info("PhaseB", $"Phase B started by {trigger}, active scene '{SceneManager.GetActiveScene().name}', " +
                           $"frame {Time.frameCount} ({watch.ElapsedMilliseconds} ms)");
    }
}
