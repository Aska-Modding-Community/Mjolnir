using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Mjolnir.CodeMap;

/// <summary>
/// The ASKA install when --game is not given: MJOLNIR_GAME_DIR, else Steam's own records (the registry's Steam folder,
/// every library in its libraryfolders.vdf, and the library whose appmanifest_1898300.acf names the game's folder), as
/// tools/locations.ps1 finds it for the build scripts.
/// </summary>
internal static class GameLocator
{
    private const string AppId = "1898300";

    public static string Find()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("MJOLNIR_GAME_DIR");
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
        foreach (string library in Libraries())
        {
            string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            if (!File.Exists(manifest)) continue;
            var installDir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            string dir = Path.Combine(library, "steamapps", "common", installDir.Success ? installDir.Groups[1].Value : "ASKA");
            if (File.Exists(Path.Combine(dir, "Aska.exe"))) return dir;
        }
        throw new ArgumentException("ASKA not found in any Steam library: pass --game <folder with Aska.exe> or set MJOLNIR_GAME_DIR");
    }

    private static IEnumerable<string> Libraries()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var roots = new List<string>();
        foreach (var (hive, key, value) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
            if (hive.OpenSubKey(key)?.GetValue(value) is string root) roots.Add(root);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (seen.Add(Path.GetFullPath(root))) yield return root;
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            // Lines such as:  "path"  "D:\\SteamLibrary"  (backslashes doubled in the file)
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Cast<Match>())
            {
                string library = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(Path.GetFullPath(library))) yield return library;
            }
        }
    }
}
