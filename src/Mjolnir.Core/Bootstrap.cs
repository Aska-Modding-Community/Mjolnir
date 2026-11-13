using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using Mjolnir.Core.Interop;
using Mjolnir.Core.Lifecycle;
using Mjolnir.Core.Mods;

namespace Mjolnir.Core;

public static unsafe class Bootstrap
{
    private static int s_started;

    internal static LoaderPaths Paths { get; private set; } = null!;

    /// <summary>Hashes of the installed game binaries, or null if they could not be computed.</summary>
    internal static GameIdentity? Game { get; private set; }

    /// <summary>
    /// Called by Mjolnir.Native from the il2cpp_init detour, after IL2CPP has initialised and
    /// before the Unity engine is up (phase A): no Unity objects may be touched here.
    /// Returns 0 on success. No exception may escape into native code.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int Start(NativeContext* ctx)
    {
        if (Interlocked.Exchange(ref s_started, 1) != 0) return 1;
        try
        {
            if (ctx == null || ctx->Version != NativeContext.ExpectedVersion || ctx->Size < (uint)sizeof(NativeContext))
                return -2;

            Log.Init(ctx->Log);
            Paths = new LoaderPaths(new string(ctx->GameDir), new string(ctx->LoaderDir));

            // Core's dependencies (Il2CppInterop, HarmonyX, MonoMod…) live in Mjolnir\core, the generated
            // interop assemblies (Il2Cppmscorlib, Assembly-CSharp…) in Mjolnir\interop.
            AssemblyLoadContext.Default.Resolving += ResolveFromLoaderDirs;

            Log.Info("Core", $"Mjolnir.Core {typeof(Bootstrap).Assembly.GetName().Version} on .NET {Environment.Version}");
            Directory.CreateDirectory(Paths.ConfigDir);
            Game = IdentifyGame();

            // Mjolnir\interop for this build, generated now if it is not (the first start, a game update): the game
            // waits here, the generator shows its own window. Without it nothing below can work.
            if (!InteropCache.EnsureReady(Paths, Game))
            {
                Log.Warn("Core", "No interop assemblies for this game build: continuing without mods");
                return 0;
            }

            InteropRuntime.TryStart(ctx, Paths);
            PhaseB.TryArm();

            var loaded = ModScanner.LoadAll(Paths);
            Log.Info("Core", $"Mod assemblies loaded: {loaded.Count}");
            ModLoader.LoadAll(loaded, Paths);
            FrontEnds.Start(Paths); // the SDK's own UI exists even when no mod loaded
            return 0;
        }
        catch (Exception e)
        {
            try { Log.Error("Core", "Bootstrap failed, continuing without mods: " + e); } catch { }
            return -1;
        }
    }

    // Non-fatal: a failure only loses the build check, the game and mods still load.
    private static GameIdentity? IdentifyGame()
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var game = GameIdentity.Compute(Paths);
            Log.Info("Core", $"Game build: GameAssembly.dll {game.GameAssemblySha256[..16]}, " +
                             $"global-metadata.dat {game.MetadataSha256[..16]}, cache key {game.CacheKey} ({watch.ElapsedMilliseconds} ms)");
            if (game.IsAnalysedBuild)
                Log.Info("Core", "Game build matches the analysed build");
            else
                Log.Warn("Core", "Game build differs from the analysed build: " +
                                 "recorded RVAs and Il2CppInterop signature findings may be stale");
            return game;
        }
        catch (Exception e)
        {
            Log.Warn("Core", "Could not identify the game build: " + e.Message);
            return null;
        }
    }

    private static Assembly? ResolveFromLoaderDirs(AssemblyLoadContext alc, AssemblyName name)
    {
        foreach (string dir in new[] { Paths.CoreDir, Paths.InteropDir })
        {
            string candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate)) return alc.LoadFromAssemblyPath(candidate);
        }
        return null;
    }
}
