using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The ASKA install the kit reads the game's skeletons from: MJOLNIR_GAME_DIR, else the folder chosen before (remembered by
    /// the editor), else Steam's own records (the registry's Steam folder, its libraryfolders.vdf, the library whose
    /// appmanifest_1898300.acf names the game's folder), else a folder the user picks, as tools/locations.ps1 finds it.
    /// </summary>
    public static class GameFolder
    {
        private const string AppId = "1898300";
        private const string PrefKey = "Mjolnir.GameFolder";

        /// <summary>The game's folder (with Aska.exe), null when none was found or picked.</summary>
        public static string Find(bool ask)
        {
            string fromEnv = Environment.GetEnvironmentVariable("MJOLNIR_GAME_DIR");
            if (IsGame(fromEnv)) return fromEnv;
            string remembered = EditorPrefs.GetString(PrefKey, "");
            if (IsGame(remembered)) return remembered;
            foreach (string library in Libraries())
            {
                string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(manifest)) continue;
                var installDir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                string dir = Path.Combine(library, "steamapps", "common", installDir.Success ? installDir.Groups[1].Value : "ASKA");
                if (IsGame(dir)) return dir;
            }
            if (!ask) return null;
            string picked = EditorUtility.OpenFolderPanel("The ASKA folder (with Aska.exe)", "", "");
            if (!IsGame(picked)) return null;
            EditorPrefs.SetString(PrefKey, picked);
            return picked;
        }

        /// <summary>
        /// Asks for the game's folder and remembers it (over Steam's records; MJOLNIR_GAME_DIR still comes first); null when the
        /// folder picked has no Aska.exe or none was picked.
        /// </summary>
        public static string Pick()
        {
            string picked = EditorUtility.OpenFolderPanel("The ASKA folder (with Aska.exe)", Find(false) ?? "", "");
            if (!IsGame(picked)) return null;
            EditorPrefs.SetString(PrefKey, picked);
            return picked;
        }

        private static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Aska.exe"));

        private static IEnumerable<string> Libraries()
        {
            var roots = new List<string>();
#if UNITY_EDITOR_WIN
            try
            {
                foreach (var (hive, key, value) in new[]
                         {
                             (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                             (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                             (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                         })
                    if (hive.OpenSubKey(key)?.GetValue(value) is string root) roots.Add(root);
            }
            catch (Exception)
            {
                // no registry access: the user picks the folder
            }
#endif
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
}
