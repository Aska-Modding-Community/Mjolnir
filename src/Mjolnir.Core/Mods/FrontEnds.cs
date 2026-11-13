using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Mjolnir.Core.Interop;

namespace Mjolnir.Core.Mods;

/// <summary>
/// The mod API's front ends: assemblies in Mjolnir/core that reference Core and carry <see cref="FrontEndAttribute"/>
/// (Mjolnir.SDK). Core starts each once after loading the mods, whether or not any mod loaded. Before, the SDK started
/// only from the first mod's attach, so with every mod turned off its Mods window was gone and no mod could be turned
/// back on (found by the user, 2026-10-23).
/// </summary>
internal static class FrontEnds
{
    /// <summary>Starts the front ends in Mjolnir/core when Il2CppInterop runs (their modules patch the game). Never throws.</summary>
    public static void Start(LoaderPaths paths)
    {
        if (!InteropRuntime.Started) return;
        try
        {
            foreach (var entry in Find(paths.CoreDir))
            {
                try
                {
                    ((IFrontEnd)Activator.CreateInstance(entry)!).Start();
                    Log.Info("Core", $"Front end {entry.Assembly.GetName().Name} started");
                }
                catch (Exception e)
                {
                    Log.Error("Core", $"Front end {entry.Assembly.GetName().Name} failed to start: {e}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("Core", $"Front ends not started: {e}");
        }
    }

    /// <summary>
    /// The front-end entries in <paramref name="folder"/>, in file name order. Only assemblies whose metadata references
    /// Core are loaded (into the default context, where mods and the platform live): the rest of Mjolnir/core (Il2CppInterop,
    /// HarmonyX, MonoMod…) is only read.
    /// </summary>
    internal static IReadOnlyList<Type> Find(string folder)
    {
        string core = typeof(FrontEnds).Assembly.GetName().Name!;
        var entries = new List<Type>();
        foreach (string path in Directory.EnumerateFiles(folder, "*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!References(path, core)) continue;
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(AssemblyName.GetAssemblyName(path));
                if (assembly.GetCustomAttribute<FrontEndAttribute>() is not { } front) continue;
                if (!typeof(IFrontEnd).IsAssignableFrom(front.Entry) || front.Entry.GetConstructor(Type.EmptyTypes) == null)
                {
                    Log.Error("Core", $"{Path.GetFileName(path)}: front end {front.Entry.FullName} is not an IFrontEnd with a parameterless constructor");
                    continue;
                }
                entries.Add(front.Entry);
            }
            catch (Exception e)
            {
                Log.Warn("Core", $"{Path.GetFileName(path)}: not checked for a front end: {e.Message}");
            }
        }
        return entries;
    }

    // Whether a .NET assembly's metadata lists Core among its references (a native DLL has no metadata).
    private static bool References(string path, string assemblyName)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return false;
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences.Any(handle =>
            metadata.StringComparer.Equals(metadata.GetAssemblyReference(handle).Name, assemblyName));
    }
}
