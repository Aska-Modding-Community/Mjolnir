using System;
using System.IO;

namespace Mjolnir.Core;

/// <summary>
/// Another mod loader installed in the game's folder beside Mjolnir, found at the start of phase A.
/// <para>Two loaders cannot share one game. Both proxies are loaded by the engine and both hook the same IL2CPP
/// entry point, so one of them wins and the other starts nothing — without an error of its own, because it never
/// runs far enough to write one. Mjolnir is the one running whenever this is logged, so the other loader's mods are
/// not loaded, and nothing but this line tells the player why.</para>
/// </summary>
/// <param name="Name">What it is, for the player: a product name, not a file.</param>
/// <param name="Marker">The file or folder it was recognised by, relative to the game's folder.</param>
internal sealed record OtherLoader(string Name, string Marker)
{
    private const string Source = "Core";

    /// <summary>Unity Doorstop's own files; BepInEx and others ride on it.</summary>
    private static readonly string[] DoorstopFiles = ["doorstop_config.ini", ".doorstop_version"];

    /// <summary>The loader found at this start, null for none. Set by <see cref="Detect"/>.</summary>
    internal static OtherLoader? Found { get; private set; }

    /// <summary>Finds another loader beside Mjolnir, remembers it for the Mods window and says in the log what it means.</summary>
    internal static OtherLoader? Detect(string gameDir)
    {
        Found = Find(gameDir);
        if (Found != null)
            Log.Warn(Source, $"{Found.Name} is installed beside Mjolnir ({Found.Marker}): two mod loaders cannot share one "
                           + $"game. Mjolnir is running, {Found.Name} is not, and its mods are not loaded. Keep one of the two.");
        return Found;
    }

    /// <summary>
    /// What is installed beside Mjolnir in <paramref name="gameDir"/>, null for nothing. A folder that cannot be read
    /// means nothing found: this is a courtesy line in the log, never a reason to fail a start.
    /// </summary>
    internal static OtherLoader? Find(string gameDir)
    {
        try
        {
            foreach (string marker in DoorstopFiles)
                if (File.Exists(Path.Combine(gameDir, marker)))
                    // Doorstop carries whatever was built on it; BepInEx is the one ASKA's players have.
                    return new OtherLoader(Directory.Exists(Path.Combine(gameDir, "BepInEx")) ? "BepInEx" : "Unity Doorstop", marker);

            // MelonLoader's own proxy is version.dll, the file Mjolnir ships: its folder is here, its loader is not.
            if (Directory.Exists(Path.Combine(gameDir, "MelonLoader"))) return new OtherLoader("MelonLoader", "MelonLoader");
            return null;
        }
        catch (Exception e)
        {
            Log.Warn(Source, $"Could not look for another loader beside Mjolnir: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }
}
