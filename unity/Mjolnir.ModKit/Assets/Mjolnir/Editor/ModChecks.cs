using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// What would go wrong in the game, found before a bundle is built (each a warning; nothing stops the build):
    /// <list type="bullet">
    /// <item>a material whose shader is not one of the game's HDRP shaders (the game takes its own shader of the same name, and has
    /// no other, so the material draws pink or black);</item>
    /// <item>a prefab whose pivot lies away from its model (an item's pivot is where the game holds and drops it);</item>
    /// <item>a script on a prefab (the game runs no script from a bundle; the prefab keeps only its Unity components);</item>
    /// <item>a clip or a mask naming bones that no imported skeleton of the game has (a typo, or a skeleton of another rig);</item>
    /// <item>the game's own assets in a mod (its clips, its characters' models or skeletons, or what uses them), which no bundle may
    /// carry (the bundle build refuses them), and a clip named as one of the game's (a copy of it, likely);</item>
    /// <item>bundles the game would not load together: two built under one name (<see cref="BundleBuilder.BuildName"/>), in the kit or
    /// among the mods installed in the game (an older kit's bundles named after their folder alone).</item>
    /// </list>
    /// </summary>
    public static class ModChecks
    {
        [MenuItem("Mjolnir/Check Mods", priority = 22)]
        public static void CheckFromMenu()
        {
            var problems = Run();
            EditorUtility.DisplayDialog("Mjolnir kit", problems.Count == 0
                ? "Nothing to fix in Assets/Mods."
                : $"{problems.Count} thing(s) to look at (the Console has them all):\n\n" + string.Join("\n", problems.Take(12)), "OK");
        }

        /// <summary>Checks everything under Assets/Mods; the problems, each also logged as a warning.</summary>
        public static List<string> Run()
        {
            var problems = new List<string>();
            if (!Directory.Exists(BundleBuilder.ModsFolder)) return problems;
            var bones = new HashSet<string>(Masks.AllPaths()) { "" };
            var modAssets = AssetDatabase.FindAssets("", new[] { BundleBuilder.ModsFolder }).Select(AssetDatabase.GUIDToAssetPath)
                                         .Where(p => !AssetDatabase.IsValidFolder(p)).ToList();
            foreach (string game in GameClips.GameAssetsIn(modAssets))
                problems.Add($"{game}: the game's own asset (or a copy of one of its clips), taken in by a mod's bundle; no mod may ship it, and the bundle is not built (play the game's clips by name, Animations.Find)");
            // A copy of a game clip is told by its name, from the list of the game's clips: said when that list is gone, rather
            // than passing the mods' clips as checked.
            if (GameClips.WhyNamesUnknown() is { } unknown && modAssets.Any(IsClipFile))
                problems.Add($"{BundleBuilder.ModsFolder}: the mods' clips were not checked for copies of the game's: {unknown}");
            foreach (string guid in AssetDatabase.FindAssets("", new[] { BundleBuilder.ModsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab")) CheckPrefab(path, problems);
                else if (path.EndsWith(".mat")) CheckMaterial(AssetDatabase.LoadAssetAtPath<Material>(path), path, problems);
                else if (path.EndsWith(".anim") && bones.Count > 1) CheckClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(path), path, bones, problems);
                else if (AssetImporter.GetAtPath(path) is ModelImporter model && model.animationType != ModelImporterAnimationType.Human && bones.Count > 1)
                    foreach (var clip in KitCommon.ClipsIn(path))
                        CheckClip(clip, $"{path} ({clip.name})", bones, problems);
                else if (path.EndsWith(".mask") && bones.Count > 1) CheckMask(AssetDatabase.LoadAssetAtPath<AvatarMask>(path), path, bones, problems);
            }
            CheckBundleNames(problems);
            // Each with its asset, so a click on the line in the Console shows it in the Project window.
            foreach (string problem in problems)
            {
                int colon = problem.IndexOf(": ", System.StringComparison.Ordinal);
                string path = colon > 0 ? problem.Substring(0, colon).Split(' ')[0] : null;
                Debug.LogWarning("Mjolnir kit: " + problem, path != null ? AssetDatabase.LoadMainAssetAtPath(path) : null);
            }
            return problems;
        }

        /// <summary>
        /// Bundles the game would refuse to load together: Unity loads one bundle of a build name (the files inside it are named
        /// from it). Two bundle folders of the kit with one build name, and, in the game, bundles of different mods (or of a mod
        /// and the platform) holding files of the same name: built by an older kit, which named a bundle after its folder alone,
        /// or by another kit with a mod folder of the same name. The kit's own built bundles stand in for their mods' copies in
        /// the game.
        /// </summary>
        private static void CheckBundleNames(List<string> problems)
        {
            var names = new Dictionary<string, string>();
            foreach (string mod in Directory.GetDirectories(BundleBuilder.ModsFolder))
                foreach (string bundle in Directory.GetDirectories(mod))
                {
                    string folder = bundle.Replace('\\', '/');
                    string name = BundleBuilder.BuildName(Path.GetFileName(mod), Path.GetFileName(bundle));
                    if (names.TryGetValue(name, out string first))
                        problems.Add($"{folder}: built under the same name as {first} ({name}); the game would load only one of them (rename the folder)");
                    else names[name] = folder;
                }

            string game = GameFolder.Find(false);
            if (game == null) return;
            // Every bundle file and the mod it belongs to: the game's mods, the platform's own, the kit's builds over the mods'.
            var files = new Dictionary<string, (string Owner, string File)>(System.StringComparer.OrdinalIgnoreCase);
            void Add(string owner, string folder)
            {
                if (!Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder).Where(f => Path.GetExtension(f).Length == 0))
                    files[$"{owner}/{Path.GetFileName(file)}"] = (owner, file.Replace('\\', '/'));
            }
            string mods = Path.Combine(game, "Mjolnir", "Mods");
            if (Directory.Exists(mods))
                foreach (string mod in Directory.GetDirectories(mods)) Add(Path.GetFileName(mod), Path.Combine(mod, "bundles"));
            Add("Mjolnir", Path.Combine(game, "Mjolnir", "core", "bundles"));
            if (Directory.Exists(BundleBuilder.OutputFolder))
                foreach (string built in Directory.GetDirectories(BundleBuilder.OutputFolder))
                {
                    string mod = Path.GetFileName(built);
                    if (Directory.Exists(Path.Combine(mods, mod))) Add(mod, built);
                }

            var holders = new Dictionary<string, (string Owner, string File)>();
            var reported = new HashSet<string>();
            foreach (var (owner, file) in files.Values.OrderBy(f => f.File, System.StringComparer.Ordinal))
            {
                var inside = BundleArchive.FileNames(file);
                if (inside == null) continue;
                foreach (string name in inside)
                {
                    if (!holders.TryGetValue(name, out var other))
                    {
                        holders[name] = (owner, file);
                        continue;
                    }
                    if (other.Owner == owner || !reported.Add($"{other.File}|{file}")) continue;
                    problems.Add($"{file}: built under the same name as {other.File} ({owner} and {other.Owner}); the game loads only one of them and the other mod's assets are missing. Build them again with this kit, which builds each bundle under a name of its mod's");
                }
            }
        }

        private static void CheckPrefab(string path, List<string> problems)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;
            foreach (var script in prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && IsUsers(m)))
                problems.Add($"{path}: {script.GetType().Name} on {script.name} is a script; the game will not run it");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !AssetDatabase.GetAssetPath(material).StartsWith(BundleBuilder.ModsFolder))
                        CheckMaterial(material, $"{path} ({renderer.name})", problems);
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            var around = bounds;
            around.Expand(Mathf.Max(0.05f, bounds.size.magnitude * 0.25f));
            if (!around.Contains(prefab.transform.position))
                problems.Add($"{path}: its pivot is {Vector3.Distance(bounds.ClosestPoint(prefab.transform.position), prefab.transform.position):0.00} m off its model; the game holds and drops an item by its pivot");
        }

        private static void CheckMaterial(Material material, string where, List<string> problems)
        {
            string shader = material.shader != null ? material.shader.name : "none";
            if (!shader.StartsWith("HDRP/"))
                problems.Add($"{where}: the material {material.name} uses '{shader}'; the game has only its HDRP shaders (HDRP/Lit…)");
        }

        private static void CheckClip(AnimationClip clip, string path, HashSet<string> bones, List<string> problems)
        {
            if (clip == null) return;
            var unknown = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Where(p => !bones.Contains(p)).Distinct().ToList();
            if (unknown.Count > 0)
                problems.Add($"{path}: {unknown.Count} path(s) no skeleton of the game has: {string.Join(", ", unknown.Take(4))}{(unknown.Count > 4 ? "…" : "")}");
        }

        private static void CheckMask(AvatarMask mask, string path, HashSet<string> bones, List<string> problems)
        {
            if (mask == null) return;
            var unknown = Enumerable.Range(0, mask.transformCount).Select(mask.GetTransformPath).Where(p => !bones.Contains(p)).ToList();
            if (unknown.Count > 0)
                problems.Add($"{path}: {unknown.Count} bone(s) no skeleton of the game has: {string.Join(", ", unknown.Take(4))}{(unknown.Count > 4 ? "…" : "")}");
        }

        // A clip of its own or a model file, which may hold clips.
        private static bool IsClipFile(string path) =>
            path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase) || AssetImporter.GetAtPath(path) is ModelImporter;

        // A script of the project's own (the game knows Unity's and HDRP's components, not a mod's).
        private static bool IsUsers(MonoBehaviour behaviour)
        {
            string assembly = behaviour.GetType().Assembly.GetName().Name;
            return !assembly.StartsWith("Unity") && !assembly.StartsWith("UnityEngine");
        }
    }
}
