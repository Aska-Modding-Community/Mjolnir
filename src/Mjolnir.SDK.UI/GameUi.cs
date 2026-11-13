using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The UI facade's runtime: its hidden Harmony patches (installed on the first use of any UI API, so mods that never
/// touch the UI cost nothing), the "may Unity be used now?" check (the SDK's <see cref="GameLoop.CanUseUnity"/>), and its
/// log. Nothing here throws into the game or into a mod: SDK faults are logged as [SDK.UI].
/// </summary>
internal static class GameUi
{
    public const string Source = "SDK.UI";
    private static readonly object s_lock = new();
    private static bool s_installed;

    /// <summary>Installs the hidden patches once. Safe in phase A (OnLoad): Harmony only, no Unity objects.</summary>
    public static void EnsureInstalled()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;
        }
        // Each part on its own: a patch the game no longer allows leaves the rest of the facade working.
        Guard("UI tick", LiveTree.EnsureTicking); // hooks the Dispatcher too, which records the main thread at GameReady
        Guard("UI patches", Install);
        Guard("interface scale", ScaleService.EnsureTicking);
        Guard("headless check", GameLoop.WatchForHeadless);
    }

    /// <summary>Whether Unity UI may be touched now (otherwise logs why as [SDK.UI] and returns false).</summary>
    public static bool CanUseUnity(string what) => GameLoop.CanUseUnity(what, Source);

    /// <summary>Runs SDK code from a game callback (a Harmony patch): a fault is logged, never thrown into the game.</summary>
    public static void Guard(string what, Action body) => GameGuard.Run(Source, what, body);

    // Harmony and game types are touched only here, after Il2CppInterop is up.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Install()
    {
        var harmony = new Harmony("mjolnir.sdk.ui");
        foreach (var patches in new[] { typeof(MenuPatches), typeof(MenuTabPatches), typeof(HudPatches), typeof(ScalePatches), typeof(TextFieldPatches) })
            Guard($"UI patches {patches.Name}", () => harmony.CreateClassProcessor(patches).Patch());
        // The count catches a patch class Harmony skipped silently (it did once: no class-level [HarmonyPatch]).
        SdkLog.Info(Source, $"UI facade installed: {harmony.GetPatchedMethods().Count()} game methods patched");
    }
}
