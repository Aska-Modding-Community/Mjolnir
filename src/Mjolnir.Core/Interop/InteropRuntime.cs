using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime.Startup;

namespace Mjolnir.Core.Interop;

/// <summary>
/// Starts Il2CppInterop (step 4) in phase A, right after il2cpp_init: native
/// detours go through MinHook, HarmonyX patches on IL2CPP methods are enabled. The interop assemblies
/// in Mjolnir\interop are generated for the game's build by <see cref="InteropCache"/> just before.
/// </summary>
internal static unsafe class InteropRuntime
{
    public static bool Started { get; private set; }

    /// <summary>Never throws: without the runtime the game still runs, only mods that use game types fail.</summary>
    public static void TryStart(NativeContext* ctx, LoaderPaths paths)
    {
        try
        {
            if (!File.Exists(Path.Combine(paths.InteropDir, "Il2Cppmscorlib.dll")))
            {
                Log.Warn("Interop", $"No interop assemblies in {paths.InteropDir}; Il2CppInterop not started");
                return;
            }

            // Il2CppInterop reads MethodXrefScanCache.db / MethodAddressToToken.db from here.
            Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", paths.InteropDir);
            var unity = UnityVersion(paths.UnityPlayerPath);

            var watch = Stopwatch.StartNew();
            Start(ctx, unity);
            Started = true;
            Log.Info("Interop", $"Il2CppInterop started for Unity {unity} ({watch.ElapsedMilliseconds} ms)");
        }
        catch (Exception e)
        {
            Log.Error("Interop", "Il2CppInterop failed to start; mods that use game types will not work: " + e);
        }
    }

    // Separate, non-inlined method: Il2CppInterop assemblies are only loaded once the checks above pass.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start(NativeContext* ctx, Version unity)
    {
        var detours = new MinHookDetourProvider(ctx, new NativeImage(ctx->GameAssembly));
        Il2CppInteropRuntime.Create(new RuntimeConfiguration { UnityVersion = unity, DetourProvider = detours })
            .AddLogger(new InteropLogger())
            .AddHarmonySupport()
            .Start();

        // Harmony's static constructor installs MonoMod's runtime detours. A MonoMod that does not support
        // this CoreCLR dies there with a fatal, uncatchable runtime error; trigger it here, right after a log
        // line, so LogOutput.log blames HarmonyX instead of whichever mod happens to patch first.
        Log.Info("Interop", $"Initialising HarmonyX {typeof(Harmony).Assembly.GetName().Version}");
        _ = new Harmony("mjolnir.core");
    }

    // UnityPlayer.dll FileVersion is "6000.3.12.16556090"; Il2CppInterop wants Major.Minor.Build.
    private static Version UnityVersion(string unityPlayer)
    {
        string? fileVersion = FileVersionInfo.GetVersionInfo(unityPlayer).FileVersion;
        if (fileVersion == null || !Version.TryParse(fileVersion, out var v))
            throw new InvalidOperationException($"cannot read the Unity version from {unityPlayer} ('{fileVersion}')");
        return new Version(v.Major, v.Minor, v.Build);
    }
}
