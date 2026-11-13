using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.ModKit
{
    /// <summary>What several of the kit's tools share: the mods' folders, a file's clips, the skeletons' order, assets written in place.</summary>
    internal static class KitCommon
    {
        /// <summary>The name of the game's skeleton root under a rig's own root; what is beside it is a model's part.</summary>
        public const string Master = "master";

        // Unity's own clip in a model file, for its import preview: no clip of the file's.
        private const string PreviewClipPrefix = "__preview__";

        /// <summary>Whether a folder of Assets/Mods is the platform's own (Mjolnir.SDK.Animation's masks), not a mod's.</summary>
        public static bool IsPlatform(string modFolder) => Path.GetFileName(modFolder).StartsWith("Mjolnir.SDK", StringComparison.Ordinal);

        /// <summary>Every mod's folder under Assets/Mods, the platform's own left out, with forward slashes, in order.</summary>
        public static List<string> ModPaths() =>
            !Directory.Exists(BundleBuilder.ModsFolder) ? new List<string>()
                : Directory.GetDirectories(BundleBuilder.ModsFolder).Select(d => d.Replace('\\', '/')).Where(d => !IsPlatform(d)).OrderBy(d => d).ToList();

        /// <summary>Every bundle folder of the mods (<c>Assets/Mods/&lt;mod&gt;/&lt;bundle&gt;</c>), the platform's own left out, in order.</summary>
        public static string[] BundleFolders() =>
            ModPaths().SelectMany(Directory.GetDirectories).Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray();

        /// <summary>The clips of an asset file (a model file's animations), Unity's preview clip left out.</summary>
        public static IEnumerable<AnimationClip> ClipsIn(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal));

        /// <summary>The imported skeletons in the order the kit shows them: the player's Ragnar and Aska first, then the others by name.</summary>
        public static List<string> RigsInOrder() =>
            RigImporter.Imported().OrderBy(r => r == "ragnar" ? 0 : r == "aska" ? 1 : 2).ThenBy(r => r, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Leaves only the skeleton on a copy of a rig's prefab: its root and the <c>master</c> bones, without the model's parts
        /// beside them (the game's meshes and materials) or any renderer.
        /// </summary>
        public static void StripToSkeleton(GameObject instance)
        {
            foreach (var child in instance.transform.Cast<Transform>().ToList())
                if (child.name != Master) Object.DestroyImmediate(child.gameObject);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
        }

        /// <summary>
        /// An asset at its path: <paramref name="fresh"/> as a new one, or the one there with the new content copied in (its GUID
        /// kept, so what refers to it keeps it) and <paramref name="fresh"/> destroyed. The asset stored.
        /// </summary>
        public static T WriteAsset<T>(T fresh, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
