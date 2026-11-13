using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// Builds mods' AssetBundles for the game: Windows, the game's own Unity and HDRP. Each folder Assets/Mods/&lt;mod&gt;/&lt;bundle&gt;
    /// is one bundle of everything in it (scripts left out: the game cannot run a mod's MonoBehaviour from a bundle). The
    /// bundles go to Build/Bundles/&lt;mod&gt;/&lt;bundle&gt;, and the mod ships them in its own folder, where
    /// <c>ModItem.WithModel(bundle, asset)</c> and <c>Animations.Add(name, bundle, asset)</c> load them. Each is built under a name of
    /// its mod's (<see cref="BuildName"/>), so two mods' bundles of one folder name load together.
    /// <para>
    /// A model file with animations (an FBX from Blender, Maya, 3ds Max, Cascadeur) goes in as its clips, each a clip of its
    /// own named after its action (<see cref="ClipsFolder"/>), and not as the file: the game finds an asset of a bundle only by
    /// the name it was put in under (it has no call that looks inside an asset), and a file's mesh is nothing the game wants.
    /// </para>
    /// </summary>
    public static class BundleBuilder
    {
        public const string ModsFolder = "Assets/Mods";
        public const string OutputFolder = "Build/Bundles";

        /// <summary>Where Unity builds a mod's bundles under their build names (<see cref="BuildName"/>), with its records: <c>&lt;mod&gt;.build</c>.</summary>
        public const string StagingFolder = "Build/BundleBuild";

        /// <summary>Where the clips taken out of model files wait for a build (made again by each build, not in git).</summary>
        public const string ClipsFolder = "Assets/Mjolnir/Clips";

        [MenuItem("Mjolnir/Build Bundles", priority = 20)]
        public static void BuildFromMenu() => Build();

        /// <summary>Batch mode: <c>-executeMethod Mjolnir.ModKit.BundleBuilder.BuildFromCommandLine</c>; the editor exits 1 on a failure.</summary>
        public static void BuildFromCommandLine()
        {
            KitSetup.Ensure(false);
            EditorApplication.Exit(Build() >= 0 ? 0 : 1);
        }

        /// <summary>Builds every mod's bundles; their count, or -1 when a build failed.</summary>
        public static int Build()
        {
            if (!Directory.Exists(ModsFolder))
            {
                Debug.LogWarning($"Mjolnir kit: no {ModsFolder} folder, nothing to build");
                return 0;
            }
            ModChecks.Run(); // warnings only: a bundle is built whatever they say
            int count = 0;
            foreach (string mod in Directory.GetDirectories(ModsFolder))
            {
                string modName = Path.GetFileName(mod);
                var builds = new List<AssetBundleBuild>();
                foreach (string bundle in Directory.GetDirectories(mod))
                {
                    string[] assets = Assets(modName, bundle.Replace('\\', '/'));
                    if (assets.Length > 0) builds.Add(new AssetBundleBuild { assetBundleName = BuildName(modName, Path.GetFileName(bundle)), assetNames = assets });
                }
                if (builds.Count == 0) continue;
                var game = GameClips.GameAssetsIn(builds.SelectMany(b => b.assetNames)).ToList();
                if (game.Count > 0)
                {
                    Debug.LogError($"Mjolnir kit: {Path.GetFileName(mod)}'s bundles would carry the game's own assets, which no mod may ship (play the game's clips by name, Animations.Find): {string.Join(", ", game.Take(6))}{(game.Count > 6 ? "…" : "")}");
                    return -1;
                }
                // Unity builds into a folder of its own (its records there make the next build incremental), and each bundle is
                // copied out under its folder's name, the name the mod loads it by. Unity writes a manifest bundle named after
                // that folder, and the build names make a folder <mod> in it: named <mod> too, the two were one path on Windows
                // and the build died ("Moving file failed … Access denied", 2026-11-12).
                string staging = Path.Combine(StagingFolder, modName + ".build");
                Directory.CreateDirectory(staging);
                var manifest = BuildPipeline.BuildAssetBundles(staging, builds.ToArray(),
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    Debug.LogError($"Mjolnir kit: building {modName}'s bundles failed");
                    return -1;
                }
                string output = Path.Combine(OutputFolder, modName);
                Directory.CreateDirectory(output);
                var shipped = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var build in builds)
                {
                    string file = FileName(build.assetBundleName);
                    File.Copy(Path.Combine(staging, build.assetBundleName), Path.Combine(output, file), overwrite: true);
                    shipped.Add(file);
                }
                // What an older kit left there (its manifest bundle, the .manifest texts) and bundles no longer built.
                foreach (string old in Directory.GetFiles(output).Where(f => !shipped.Contains(Path.GetFileName(f))))
                    File.Delete(old);
                count += builds.Count;
                Debug.Log($"Mjolnir kit: {modName}: {string.Join(", ", builds.Select(b => $"{FileName(b.assetBundleName)} as {b.assetBundleName} ({b.assetNames.Length} assets)"))} in {output}");
            }
            return count;
        }

        /// <summary>
        /// The name a bundle is built under, <c>&lt;mod&gt;/&lt;bundle&gt;</c> in lower case. Unity keys a loaded bundle on the
        /// files inside it, named from this name (<c>CAB-…</c>), and refuses a second bundle that holds a file of the same name:
        /// with a bundle named after its folder alone, two mods' <c>models</c> could not be loaded together. The file keeps its
        /// folder's name (<see cref="FileName"/>), which the mod loads it by.
        /// </summary>
        public static string BuildName(string mod, string bundle) => $"{mod}/{bundle}".ToLowerInvariant();

        /// <summary>The bundle's file name in the mod's <c>bundles/</c>: its folder's name in lower case.</summary>
        public static string FileName(string buildName) => buildName.Substring(buildName.LastIndexOf('/') + 1);

        /// <summary>
        /// What a bundle folder puts in its bundle: every file but scripts, a model file with clips as those clips (one asset each,
        /// named after its action).
        /// </summary>
        public static string[] Assets(string mod, string bundle)
        {
            var assets = new List<string>();
            var clipNames = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories)
                                             .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".cs"))
                                             .Select(f => f.Replace('\\', '/'))
                                             .OrderBy(f => f, System.StringComparer.Ordinal))
            {
                var importer = AssetImporter.GetAtPath(file) as ModelImporter;
                if (importer != null && importer.animationType == ModelImporterAnimationType.Human)
                {
                    // A humanoid source (Mixamo, motion capture): its clips move another skeleton's bones; what goes in is
                    // what Bake Humanoid Clips to Rig made of it.
                    Debug.Log($"Mjolnir kit: {file} is a humanoid source and stays out of the bundle (Mjolnir > Animation > Bake Humanoid Clips to Rig makes its clips for the game)");
                    continue;
                }
                var clips = importer != null ? KitCommon.ClipsIn(file).ToList() : new List<AnimationClip>();
                if (clips.Count == 0)
                {
                    assets.Add(file);
                    continue;
                }
                string folder = $"{ClipsFolder}/{mod}/{Path.GetFileName(bundle)}";
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                foreach (var clip in clips)
                {
                    if (clipNames.TryGetValue(clip.name, out string first))
                    {
                        Debug.LogWarning($"Mjolnir kit: {file}: a clip named {clip.name} is already in the bundle from {first}; this one is left out (rename its action)");
                        continue;
                    }
                    clipNames[clip.name] = file;
                    string path = $"{folder}/{clip.name}.anim";
                    var copy = Object.Instantiate(clip);
                    copy.name = clip.name;
                    KitCommon.WriteAsset(copy, path);
                    assets.Add(path);
                }
            }
            AssetDatabase.SaveAssets();
            return assets.ToArray();
        }
    }
}
