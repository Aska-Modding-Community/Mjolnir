using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Mjolnir.Core.Mods;

/// <summary>
/// One context per mod folder, for dependency resolution only. Hot unload is unsupported by design,
/// so it is not collectible. Everything the loader ships in Mjolnir\core (Core, SDK, Il2CppInterop,
/// HarmonyX…) and every interop assembly (Mjolnir\interop) comes from the Default context, so all mods
/// see one copy of the platform and of each game type.
/// </summary>
internal sealed class ModLoadContext(string name, string directory, LoaderPaths paths)
    : AssemblyLoadContext("MjolnirMod:" + name, isCollectible: false)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName.Name, paths)) return null;
        string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    /// <summary>
    /// Platform and game assemblies, i.e. those shipped in Mjolnir\core (Core, SDK, Il2CppInterop, HarmonyX…) or
    /// Mjolnir\interop: always taken from the Default context, never from a mod folder. Decided by the files
    /// present, not by name: mods may be called Mjolnir.* too.
    /// </summary>
    internal static bool IsShared(string? name, LoaderPaths paths) =>
        name is not null && (File.Exists(Path.Combine(paths.CoreDir, name + ".dll"))
                          || File.Exists(Path.Combine(paths.InteropDir, name + ".dll")));
}
