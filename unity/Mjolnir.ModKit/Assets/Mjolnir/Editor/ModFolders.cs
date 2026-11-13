using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// A mod's place in the kit and in the game. A mod's assets live in <c>Assets/Mods/&lt;mod&gt;/&lt;bundle&gt;/</c>, where
    /// <c>&lt;mod&gt;</c> is the name of the mod's folder in the game's <c>Mjolnir/Mods</c> (the one its .dll is in) and each
    /// folder under it is one AssetBundle, which the mod loads as <c>bundles/&lt;bundle&gt;</c>. New Mod makes those folders;
    /// Build and Copy to Game builds every bundle and puts each into its mod's folder in the game, with the item pictures Render
    /// Item Icons made into its <c>items/</c>, ready for the next start.
    /// </summary>
    public static class ModFolders
    {
        [MenuItem("Mjolnir/New Mod...", priority = 1)]
        public static void NewMod() => NewModWindow.Open();

        [MenuItem("Mjolnir/Build and Copy to Game", priority = 21)]
        public static void BuildAndCopy()
        {
            if (BundleBuilder.Build() < 0) return;
            string game = GameFolder.Find(ask: true);
            if (game == null)
            {
                Debug.LogError("Mjolnir kit: the ASKA folder was not found (set MJOLNIR_GAME_DIR, or pick it when asked)");
                return;
            }
            var copied = CopyToGame(game, out var failed);
            string text = copied.Length == 0 && failed.Length == 0
                ? "No bundle went into the game: install each mod into Mjolnir/Mods first (its .dll in a folder named as its folder in Assets/Mods)."
                : (copied.Length > 0 ? "Copied into the game, for its next start:\n" + string.Join("\n", copied) : "");
            if (failed.Length > 0)
                text += (text.Length > 0 ? "\n\n" : "") + "Not copied, the files are in use (close ASKA and copy again):\n" + string.Join("\n", failed);
            EditorUtility.DisplayDialog("Mjolnir kit", text, "OK");
        }

        /// <summary>
        /// Copies every built bundle into the game: <c>Build/Bundles/&lt;mod&gt;/&lt;bundle&gt;</c> to
        /// <c>Mjolnir/Mods/&lt;mod&gt;/bundles/&lt;bundle&gt;</c> for a mod installed there. What was copied, as lines, and in
        /// <paramref name="failed"/> the files that could not be written: the running game keeps its mods' bundles open.
        /// </summary>
        public static string[] CopyToGame(string game, out string[] failed)
        {
            var notCopied = new System.Collections.Generic.List<string>();
            failed = new string[0];
            string mods = Path.Combine(game, "Mjolnir", "Mods");
            if (!Directory.Exists(BundleBuilder.OutputFolder)) return new string[0];
            var lines = new System.Collections.Generic.List<string>();
            foreach (string built in Directory.GetDirectories(BundleBuilder.OutputFolder))
            {
                string mod = Path.GetFileName(built);
                string target = Path.Combine(mods, mod);
                // The platform's own bundles (Mjolnir.SDK.Animation's masks) ship with Mjolnir, not as a mod's.
                if (!Directory.Exists(target))
                {
                    if (!KitCommon.IsPlatform(mod))
                        Debug.LogWarning($"Mjolnir kit: {mod} is not installed in {mods}; its bundles stay in {built}");
                    continue;
                }
                Directory.CreateDirectory(Path.Combine(target, "bundles"));
                // The bundles only: the folder's own manifest bundle and the .manifest texts are the build's records.
                foreach (string file in Directory.GetFiles(built).Where(f => Path.GetExtension(f).Length == 0 && Path.GetFileName(f) != mod))
                {
                    if (Copy(file, Path.Combine(target, "bundles", Path.GetFileName(file)), notCopied)) lines.Add($"{mod}/bundles/{Path.GetFileName(file)}");
                }
            }
            // The item pictures rendered from the mods' models (Render Item Icons), into each mod's items/.
            if (Directory.Exists(IconRenderer.OutputFolder))
                foreach (string icons in Directory.GetDirectories(IconRenderer.OutputFolder))
                {
                    string mod = Path.GetFileName(icons);
                    string target = Path.Combine(mods, mod);
                    if (!Directory.Exists(target)) continue;
                    Directory.CreateDirectory(Path.Combine(target, "items"));
                    foreach (string file in Directory.GetFiles(icons, "*.png"))
                    {
                        if (Copy(file, Path.Combine(target, "items", Path.GetFileName(file)), notCopied)) lines.Add($"{mod}/items/{Path.GetFileName(file)}");
                    }
                }
            foreach (string line in lines) Debug.Log($"Mjolnir kit: copied {line} into the game");
            failed = notCopied.ToArray();
            return lines.ToArray();
        }

        // One file into the game; false, with the reason logged, when it is in use or cannot be written.
        private static bool Copy(string from, string to, System.Collections.Generic.List<string> failed)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return true;
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogWarning($"Mjolnir kit: {to} was not written ({e.Message}); is ASKA running?");
                failed.Add(to);
                return false;
            }
        }

        /// <summary>Whether a name can be a folder of the kit and the game: letters, digits, '.', '_' and '-'.</summary>
        public static bool IsName(string name) => !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "^[A-Za-z0-9._-]+$");

        /// <summary>Makes <c>Assets/Mods/&lt;mod&gt;/&lt;bundle&gt;</c>; false when the names are not folder names.</summary>
        public static bool Create(string mod, string bundle)
        {
            if (!IsName(mod) || !IsName(bundle)) return false;
            string folder = $"{BundleBuilder.ModsFolder}/{mod}/{bundle.ToLowerInvariant()}";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<Object>(folder);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"Mjolnir kit: {folder} made: what goes there becomes the bundle bundles/{bundle.ToLowerInvariant()} of {mod}");
            return true;
        }
    }

    /// <summary>The New Mod dialog: the mod's folder name and its first bundle's.</summary>
    public sealed class NewModWindow : EditorWindow
    {
        private string _mod = "MyMod";
        private string _bundle = "assets";

        public static void Open()
        {
            var window = GetWindow<NewModWindow>(true, "New Mod", true);
            window.minSize = window.maxSize = new Vector2(420, 170);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("The mod's folder: the name of its folder in the game's Mjolnir/Mods, where its .dll is. " +
                                    "A bundle: a folder of assets the mod loads as bundles/<bundle>.", MessageType.None);
            _mod = EditorGUILayout.TextField("Mod folder", _mod);
            _bundle = EditorGUILayout.TextField("Bundle", _bundle);
            bool valid = ModFolders.IsName(_mod) && ModFolders.IsName(_bundle);
            if (!valid) EditorGUILayout.HelpBox("Letters, digits, '.', '_' and '-' only.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Create") && ModFolders.Create(_mod, _bundle)) Close();
            }
        }
    }
}
