using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mjolnir.Core.Mods;

internal sealed record LoadedModAssembly(string Path, Assembly Assembly);

internal static class ModScanner
{
    /// <summary>
    /// Creates Mods\ if missing and loads every managed .dll beneath it. Each first-level folder is one
    /// load context; a loose .dll in Mods\ gets its own. A failure skips that file, never the rest.
    /// This only loads the assemblies; ModLoader turns them into mods.
    /// </summary>
    public static IReadOnlyList<LoadedModAssembly> LoadAll(LoaderPaths paths)
    {
        string modsDir = paths.ModsDir;
        var loaded = new List<LoadedModAssembly>();

        // A folder the player cannot read is left out, not the whole scan; a scan that fails loads no mods but leaves the
        // platform's own start (the front ends) to go on.
        List<IGrouping<string, string>> groups;
        try
        {
            Directory.CreateDirectory(modsDir);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            groups = Directory.EnumerateFiles(modsDir, "*.dll", options)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .GroupBy(p => GroupKey(modsDir, p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Error("Mods", $"Could not list the mods in {modsDir}: {e.GetType().Name}: {e.Message}");
            return loaded;
        }

        foreach (var group in groups)
        {
            string folder = Path.Combine(modsDir, group.Key);
            var context = new ModLoadContext(group.Key, Directory.Exists(folder) ? folder : modsDir, paths);
            foreach (string path in group)
            {
                string rel = Path.GetRelativePath(modsDir, path);
                // A mod built without Private=false ships copies of Mjolnir.SDK, Tomlyn or interop assemblies;
                // loading them here would give it a second, incompatible copy of those types.
                if (ModLoadContext.IsShared(Path.GetFileNameWithoutExtension(path), paths))
                {
                    Log.Info("Mods", $"Skipping {rel}: shared assembly, taken from Mjolnir/core or Mjolnir/interop");
                    continue;
                }
                try
                {
                    AssemblyName.GetAssemblyName(path);  // throws BadImageFormatException for native DLLs
                }
                catch (BadImageFormatException)
                {
                    Log.Info("Mods", $"Skipping {rel}: not a .NET assembly");
                    continue;
                }
                catch (Exception e)
                {
                    // A file locked, gone or unreadable skips that file only.
                    Log.Error("Mods", $"Skipping {rel}: {e.GetType().Name}: {e.Message}");
                    continue;
                }

                try
                {
                    var asm = context.LoadFromAssemblyPath(Path.GetFullPath(path));
                    loaded.Add(new LoadedModAssembly(path, asm));
                    Log.Info("Mods", $"Loaded {asm.GetName().Name} {asm.GetName().Version} from {rel}");
                }
                catch (Exception e)
                {
                    Log.Error("Mods", $"Failed to load {rel}: {e.Message}");
                }
            }
        }
        return loaded;
    }

    private static string GroupKey(string modsDir, string path)
    {
        string rel = Path.GetRelativePath(modsDir, path);
        int sep = rel.IndexOfAny(['\\', '/']);
        return sep < 0 ? Path.GetFileNameWithoutExtension(rel) : rel[..sep];
    }
}
