using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The game's skeletons in the kit. The game writes them itself: Mjolnir.SDK.Animation puts the skeleton of every body of
    /// the game's characters (bone names and their poses as the body's prefab defines them, nothing else of the game) into
    /// <c>Mjolnir/ModKit/rigs/&lt;rig&gt;.rig</c> once the main menu is up, so start the game with Mjolnir once. Import Rigs reads them into
    /// <c>Assets/Mjolnir/Rigs/&lt;rig&gt;.prefab</c> (ragnar and aska: the player's male and female characters; peasant_male, peasant_female: villagers), each with an
    /// Animator and a generic avatar of its own, to animate in the Animation window or to give imported animations as their
    /// avatar; then Mjolnir's masks are made again (<see cref="Masks"/>). The folder is not in git: it is the game's.
    /// </summary>
    public static class RigImporter
    {
        public const string RigsFolder = "Assets/Mjolnir/Rigs";

        [MenuItem("Mjolnir/Rigs/Import from the Game", priority = 70)]
        public static void ImportFromMenu()
        {
            int count = Import(ask: true);
            if (count < 0) return;
            if (count == 0)
            {
                EditorUtility.DisplayDialog("Mjolnir kit", "The game has written no skeleton yet. Start ASKA with Mjolnir once and wait for its main menu, then import again.", "OK");
                return;
            }
            string models = HasModels() ? "" : "\n\nThey have no models: to see the game's people on them, turn on \"Characters' models for the kit\" on Mjolnir's settings page in the game (the Mjolnir window, Settings), start it once and import again.";
            string clips = LastClips > 0
                ? $"\n\n{LastClips} of the game's clips, to choose in Preview Animation."
                : "\n\nNo clip of the game: to have them, turn on \"The game's animations for the kit\" on Mjolnir's settings page in the game, start it once and import again.";
            if (EditorUtility.DisplayDialog("Mjolnir kit", $"{count} skeleton(s) imported into {RigsFolder}.{models}{clips}", "Open the Preview Stage", "Close"))
                PreviewStage.Open();
        }

        /// <summary>Batch mode: <c>-executeMethod Mjolnir.ModKit.RigImporter.ImportFromCommandLine</c>.</summary>
        public static void ImportFromCommandLine() => EditorApplication.Exit(Import(ask: false) > 0 ? 0 : 1);

        /// <summary>Imports every skeleton the game wrote; their count, -1 without the game's folder.</summary>
        public static int Import(bool ask)
        {
            string kit = GameKitFolder(ask);
            if (kit == null)
            {
                Debug.LogError("Mjolnir kit: the ASKA folder was not found (set MJOLNIR_GAME_DIR, or pick it from Mjolnir > Rigs > Import from the Game)");
                return -1;
            }
            string source = Path.Combine(kit, "rigs");
            var files = Directory.Exists(source) ? Directory.GetFiles(source, "*.rig") : new string[0];
            Directory.CreateDirectory(RigsFolder);
            var rigs = files.ToDictionary(f => Path.GetFileNameWithoutExtension(f), Read);
            // The game writes every bone as its character's prefab has it (asset): the skeleton's own definition, the same in
            // every character of the rig. Only a bone it found nowhere but on a character (live: an older Mjolnir, or a prefab
            // the game had not loaded) takes its rotation from the skeleton with the most defined bones, as the people share one
            // Biped skeleton and differ in lengths, and keeps its own position; the import says so.
            var reference = rigs.OrderByDescending(r => r.Value.Count(b => b.Source == "asset" || b.Source == "rest")).FirstOrDefault();
            var restRotations = reference.Value?.Where(b => b.Source != "live").ToDictionary(b => b.Path, b => b.Rotation)
                                ?? new Dictionary<string, Quaternion>();
            foreach (var (rig, read) in rigs)
            {
                int filled = 0;
                var bones = read.Select(b =>
                {
                    if (b.Source != "live" || !restRotations.TryGetValue(b.Path, out var rotation)) return (b.Path, b.Position, b.Rotation);
                    filled++;
                    return (b.Path, b.Position, rotation);
                }).ToList();
                int parts = Build(rig, bones, Path.Combine(kit, "models", rig));
                if (parts > 0) Debug.Log($"Mjolnir kit: the model {rig}, {parts} part(s), on its skeleton");
                string from = string.Join(", ", read.GroupBy(b => b.Source).Select(g => $"{g.Count()} {g.Key}"));
                if (filled > 0) Debug.LogWarning($"Mjolnir kit: the skeleton {rig}, {bones.Count} bones ({from}): {filled} turned to {reference.Key}'s pose; start the game with this Mjolnir again for its exact skeleton");
                else Debug.Log($"Mjolnir kit: the skeleton {rig}, {bones.Count} bones ({from})");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // A skeleton whose model's assets were written over in place: the prefab loaded in the editor kept the replaced
            // objects (its parts showed no mesh until it was imported again).
            foreach (string rig in rigs.Keys)
                AssetDatabase.ImportAsset($"{RigsFolder}/{rig}.prefab", ImportAssetOptions.ForceUpdate);
            if (files.Length > 0) Masks.Make();
            LastClips = GameClips.Import(kit);
            PreviewStage.SyncCharacters();
            return files.Length;
        }

        /// <summary>How many of the game's clips the last import brought (0: the game wrote none).</summary>
        public static int LastClips { get; private set; }

        /// <summary>Whether the imported skeletons wear the game's models (the game wrote them: its setting for the kit).</summary>
        public static bool HasModels() =>
            Imported().Any(rig => AssetDatabase.LoadAssetAtPath<GameObject>($"{RigsFolder}/{rig}.prefab")?.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);

        /// <summary>The folder the game writes its files for the kit to (Mjolnir/ModKit), null without the game.</summary>
        public static string GameKitFolder(bool ask)
        {
            string game = GameFolder.Find(ask);
            return game == null ? null : Path.Combine(game, "Mjolnir", "ModKit");
        }

        /// <summary>The folder the game writes its skeletons to, null without the game.</summary>
        public static string GameRigsFolder(bool ask) => GameKitFolder(ask) is { } kit ? Path.Combine(kit, "rigs") : null;

        /// <summary>
        /// Whether the game wrote skeletons or models after the kit imported them (a new Mjolnir, a game update, the models'
        /// setting turned on): the kit's are then older than the game's.
        /// </summary>
        public static bool GameHasNewer()
        {
            string folder = GameKitFolder(false);
            if (folder == null || !Directory.Exists(folder)) return false;
            var written = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            var imported = Imported().Select(r => File.GetLastWriteTimeUtc($"{RigsFolder}/{r}.prefab")).DefaultIfEmpty(DateTime.MinValue).Min();
            return written > imported;
        }

        /// <summary>
        /// A rig file's bones in order (a parent before its children): path, local position and rotation, and where the game took
        /// the pose from: <c>rest</c> (the model's), <c>bind</c> (a mesh's) or <c>live</c> (as the bone stood; also a file of an
        /// older Mjolnir, which did not say).
        /// </summary>
        public static List<(string Path, Vector3 Position, Quaternion Rotation, string Source)> Read(string file)
        {
            var bones = new List<(string, Vector3, Quaternion, string)>();
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split('|');
                if (parts.Length < 3) continue;
                float[] p = Numbers(parts[1]), q = Numbers(parts[2]);
                if (p.Length != 3 || q.Length != 4) continue;
                bones.Add((parts[0], new Vector3(p[0], p[1], p[2]), new Quaternion(q[0], q[1], q[2], q[3]).normalized, parts.Length > 3 ? parts[3].Trim() : "live"));
            }
            return bones;
        }

        /// <summary>The skeletons imported: their names (ragnar, aska, peasant_male…).</summary>
        public static IEnumerable<string> Imported() =>
            Directory.Exists(RigsFolder) ? Directory.GetFiles(RigsFolder, "*.prefab").Select(Path.GetFileNameWithoutExtension) : Enumerable.Empty<string>();

        /// <summary>The bones' paths of an imported skeleton, from its root (what clips and masks name).</summary>
        public static IEnumerable<string> Paths(string rig)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RigsFolder}/{rig}.prefab");
            if (prefab == null) yield break;
            // Only the skeleton's bones: the model's parts beside master (RigModels) are no bones of the game.
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t == prefab.transform) continue;
                string path = AnimationUtility.CalculateTransformPath(t, prefab.transform);
                if (path == "master" || path.StartsWith("master/", StringComparison.Ordinal)) yield return path;
            }
        }

        private static int Build(string rig, List<(string Path, Vector3 Position, Quaternion Rotation)> bones, string modelFolder)
        {
            // Built in the open scene and saved as the prefab; the object goes whatever happens on the way.
            var root = new GameObject(rig);
            try
            {
                var byPath = new Dictionary<string, Transform> { [""] = root.transform };
                foreach (var (path, position, rotation) in bones)
                {
                    int slash = path.LastIndexOf('/');
                    string parentPath = slash < 0 ? "" : path.Substring(0, slash);
                    if (!byPath.TryGetValue(parentPath, out var parent)) continue;
                    var bone = new GameObject(path.Substring(slash + 1)).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = position;
                    bone.localRotation = rotation;
                    byPath[path] = bone;
                }
                root.AddComponent<RigGizmo>();
                int parts = RigModels.Add(rig, modelFolder, root, byPath);
                var animator = root.AddComponent<Animator>();
                var avatar = AvatarBuilder.BuildGenericAvatar(root, "");
                avatar.name = rig + "Avatar";
                string avatarPath = $"{RigsFolder}/{rig}Avatar.asset";
                AssetDatabase.DeleteAsset(avatarPath);
                AssetDatabase.CreateAsset(avatar, avatarPath);
                AssetDatabase.SetLabels(avatar, new[] { GameClips.Label });
                animator.avatar = avatar;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{RigsFolder}/{rig}.prefab");
                if (prefab != null) AssetDatabase.SetLabels(prefab, new[] { GameClips.Label });
                return parts;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static float[] Numbers(string text) =>
            text.Split(' ').Where(s => s.Length > 0).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    }
}
