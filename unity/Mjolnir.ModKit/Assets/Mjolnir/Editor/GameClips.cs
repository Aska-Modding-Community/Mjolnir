using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The game's own animation clips in the kit, to preview on its characters and to learn from. The game writes them itself
    /// (<c>Mjolnir/ModKit/clips/</c>, only when the platform's setting <c>[Kit] Animations</c> is on: Mjolnir's settings in the
    /// game), each read back from the game frame by frame with only the curves it has, its root motion on <c>master</c> as the
    /// game's avatars take it.
    /// <para>
    /// Import Rigs copies those files into <c>Build/GameClips</c> (not in git: they are the game's, for this computer only), and
    /// a clip is made only when it is chosen, in memory (<see cref="Load"/>): as assets the 733 clips took 4.6 GB of text and the
    /// editor hung opening the list of them. One clip can be kept as an asset to open in the Animation window
    /// (<see cref="SaveAsAsset"/>), under <see cref="Folder"/> with the label <see cref="Label"/>; a bundle that would take one in,
    /// or a copy of one, is not built (<see cref="ModChecks"/>): a mod plays the game's clips by name (<c>Animations.Find</c>).
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class GameClips
    {
        static GameClips()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Unload;
        }

        /// <summary>Where a game clip kept as an asset goes (git-ignored).</summary>
        public const string Folder = "Assets/Mjolnir/GameClips";

        /// <summary>Where the game's clip files are kept, outside the assets (git-ignored with the rest of Build).</summary>
        public const string Store = "Build/GameClips";

        /// <summary>
        /// The label of the game's own assets in the kit (its clips kept as assets, its characters' skeletons and the meshes,
        /// materials and textures of their models), which no bundle may take.
        /// </summary>
        public const string Label = "MjolnirGameAsset";

        // A key the straight line between its neighbours gives within this is left out (0.05 mm, a tiny turn, a scale).
        private const float PositionTolerance = 0.00005f;
        private const float RotationTolerance = 0.00005f;
        private const int MaximumStretch = 60; // frames between two keys

        /// <summary>A clip of the game the kit has: its name, the skeleton the game named it for, its length and looping.</summary>
        public sealed class Entry
        {
            public string Name, File, Group;
            public float Length, Rate;
            public bool Loops;
            public int Curves;
        }

        private static List<Entry> s_entries;
        private static DateTime s_read;
        private static readonly Dictionary<string, AnimationClip> s_loaded = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Entry> s_ofClip = new Dictionary<int, Entry>();

        /// <summary>The game's clips the kit has, by name (read from the list the game wrote; nothing is loaded).</summary>
        public static IReadOnlyList<Entry> Entries()
        {
            string list = Path.Combine(Store, "clips.txt");
            if (!File.Exists(list)) return s_entries = new List<Entry>();
            var written = File.GetLastWriteTimeUtc(list);
            if (s_entries != null && written == s_read) return s_entries;
            s_read = written;
            s_entries = File.ReadAllLines(list, Encoding.UTF8)
                            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('|')).Where(f => f.Length >= 8)
                            .Select(f => new Entry
                            {
                                Name = f[0], File = f[1], Group = f[7].Length > 0 ? f[7] : "other",
                                Length = Number(f[2]), Rate = Number(f[3]), Loops = f[4] == "1", Curves = int.Parse(f[6], CultureInfo.InvariantCulture),
                            })
                            .OrderBy(e => e.Group, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
            return s_entries;
        }

        /// <summary>Whether the game's clips are in the kit.</summary>
        public static bool Imported() => Entries().Count > 0;

        /// <summary>The game's clip of that name, made from its file the first time (in memory, never saved); null without it.</summary>
        public static AnimationClip Load(Entry entry)
        {
            if (s_loaded.TryGetValue(entry.Name, out var known) && known != null) return known;
            string file = Path.Combine(Store, entry.File);
            if (!File.Exists(file)) return null;
            var clip = Read(file);
            if (clip == null) return null;
            clip.hideFlags = HideFlags.DontSave;
            s_loaded[entry.Name] = clip;
            s_ofClip[clip.GetInstanceID()] = entry;
            return clip;
        }

        /// <summary>The game's clip a loaded clip is, null for any other.</summary>
        public static Entry Of(AnimationClip clip) => clip != null && s_ofClip.TryGetValue(clip.GetInstanceID(), out var entry) ? entry : null;

        /// <summary>
        /// Keeps a loaded game clip as an asset under <see cref="Folder"/>/&lt;skeleton&gt;, labelled as the game's, to open in
        /// the Animation window; the asset, or null.
        /// </summary>
        public static AnimationClip SaveAsAsset(AnimationClip clip)
        {
            var entry = Of(clip);
            if (entry == null) return null;
            string folder = $"{Folder}/{entry.Group}";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string path = $"{folder}/{Path.GetFileNameWithoutExtension(entry.File)}.anim";
            var copy = Object.Instantiate(clip);
            copy.name = clip.name;
            copy.hideFlags = HideFlags.None;
            copy = KitCommon.WriteAsset(copy, path);
            AssetDatabase.SetLabels(copy, new[] { Label });
            AssetDatabase.SaveAssets();
            return copy;
        }

        /// <summary>
        /// Copies the clips the game wrote into <paramref name="kitFolder"/>/clips (Mjolnir/ModKit) to <see cref="Store"/>; their count, 0
        /// without any. The clips made as assets by an earlier kit (gigabytes of text) go.
        /// </summary>
        public static int Import(string kitFolder)
        {
            string source = Path.Combine(kitFolder, "clips");
            if (!File.Exists(Path.Combine(source, "clips.txt"))) return 0;
            if (Directory.Exists(Store)) Directory.Delete(Store, true);
            Directory.CreateDirectory(Store);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(Store, Path.GetFileName(file)), true);
            Unload();
            s_entries = null;
            // An earlier kit made every clip an asset there: only the ones kept on purpose stay (a folder of their own is
            // what this one keeps, by the label).
            if (AssetDatabase.IsValidFolder(Folder) && File.Exists($"{Folder}/imported.txt"))
            {
                AssetDatabase.DeleteAsset(Folder);
                Debug.Log($"Mjolnir kit: the game's clips made as assets by an earlier kit are gone from {Folder}; they are made when chosen now");
            }
            int count = Entries().Count;
            if (count > 0) EditorPrefs.SetBool(EverImportedKey, true);
            Debug.Log($"Mjolnir kit: {count} of the game's clips in {Store}");
            return count;
        }

        // The clips made from their files live outside any scene or asset (DontSave), so a domain reload, which empties the
        // static tables, would leave every one of them behind: they go first.
        private static void Unload()
        {
            foreach (var clip in s_loaded.Values) if (clip != null) Object.DestroyImmediate(clip);
            s_loaded.Clear();
            s_ofClip.Clear();
        }

        // Set once this project imported the game's clips: their list missing after that is worth a word in the checks.
        private static string EverImportedKey => "Mjolnir.GameClips.Imported." + Application.dataPath;

        /// <summary>
        /// Why a copy of one of the game's clips cannot be told by its name now (its list, <see cref="Store"/>, is gone though the
        /// kit had the game's clips), null when it can or when the kit never had them.
        /// </summary>
        public static string WhyNamesUnknown()
        {
            if (Imported()) return null;
            bool hadThem = EditorPrefs.GetBool(EverImportedKey, false) || AssetDatabase.IsValidFolder(Folder);
            return hadThem
                ? $"the list of the game's clips ({Store}) is gone, so a copy of one of them is not found by its name (Mjolnir > Rigs > Import from the Game brings it back)"
                : null;
        }

        // The game's clip file (Mjolnir.SDK.Animation's GameClips): "MJC1", the name, length, rate, whether it loops, the frame
        // count and the curves, each a bone's path, the kind (0 position, 1 rotation, 2 scale), whether it is one value, then
        // that value or one per frame.
        private static AnimationClip Read(string file)
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MJC1") return null;
            string name = reader.ReadString();
            float length = reader.ReadSingle();
            float rate = reader.ReadSingle();
            bool loops = reader.ReadBoolean();
            int frames = reader.ReadInt32();
            int count = reader.ReadInt32();
            var clip = new AnimationClip { name = name, frameRate = rate };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            for (int c = 0; c < count; c++)
            {
                string path = reader.ReadString();
                byte kind = reader.ReadByte();
                bool constant = reader.ReadBoolean();
                int size = kind == 1 ? 4 : 3;
                int n = constant ? 1 : frames;
                var values = new float[size][];
                for (int k = 0; k < size; k++) values[k] = new float[n];
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < size; k++) values[k][i] = reader.ReadSingle();
                string property = kind == 0 ? "m_LocalPosition" : kind == 1 ? "m_LocalRotation" : "m_LocalScale";
                float tolerance = kind == 1 ? RotationTolerance : PositionTolerance;
                for (int k = 0; k < size; k++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), $"{property}.{"xyzw"[k]}"));
                    curves.Add(Curve(values[k], rate, length, tolerance));
                }
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        // The frames as keys, straight between them (the game was read at the clip's own rate), without the keys the line
        // between the ones kept gives anyway: a still or steady stretch takes two keys, not one a frame.
        private static AnimationCurve Curve(float[] values, float rate, float length, float tolerance)
        {
            if (values.Length == 1) return new AnimationCurve(new Keyframe(0, values[0], 0, 0), new Keyframe(length, values[0], 0, 0));
            float Time(int i) => Mathf.Min(i / rate, length);
            var kept = new List<int> { 0 };
            int anchor = 0;
            for (int i = 2; i < values.Length; i++)
            {
                // Could the line from the anchor to i stand for every frame between them? At most a second of frames: the
                // check grows with the stretch, and a long clip's curves took seconds without a bound.
                bool fits = i - anchor <= MaximumStretch;
                for (int j = anchor + 1; j < i && fits; j++)
                {
                    float t = (Time(j) - Time(anchor)) / (Time(i) - Time(anchor));
                    fits = Mathf.Abs(Mathf.Lerp(values[anchor], values[i], t) - values[j]) <= tolerance;
                }
                if (fits) continue;
                anchor = i - 1;
                kept.Add(anchor);
            }
            kept.Add(values.Length - 1);
            var keys = new Keyframe[kept.Count];
            for (int k = 0; k < kept.Count; k++)
            {
                int i = kept[k];
                float before = k > 0 ? (values[i] - values[kept[k - 1]]) / Mathf.Max(1e-6f, Time(i) - Time(kept[k - 1])) : 0;
                float after = k < kept.Count - 1 ? (values[kept[k + 1]] - values[i]) / Mathf.Max(1e-6f, Time(kept[k + 1]) - Time(i)) : 0;
                keys[k] = new Keyframe(Time(i), values[i], k > 0 ? before : after, k < kept.Count - 1 ? after : before);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>
        /// The game's own assets among those a bundle would take in: its clips kept as assets, its characters' models and
        /// skeletons, what carries their label, and a clip named as one of the game's (a copy: a copied asset keeps no label),
        /// also under the name Unity gives a duplicate (<c>aska_wave 1</c>, <c>aska_wave (1)</c>). Without the list of the
        /// game's clips (<see cref="WhyNamesUnknown"/>) a copy is found by its label only.
        /// </summary>
        public static IEnumerable<string> GameAssetsIn(IEnumerable<string> assets)
        {
            var entries = Entries();
            var names = new HashSet<string>(entries.Select(e => Path.GetFileNameWithoutExtension(e.File)).Concat(entries.Select(e => e.Name)), StringComparer.Ordinal);
            bool GameName(string name) => names.Contains(name) || names.Contains(WithoutCopySuffix(name));
            return AssetDatabase.GetDependencies(assets.ToArray(), true)
                                .Where(p => p.StartsWith(Folder + "/", StringComparison.Ordinal) || p.StartsWith(RigImporter.RigsFolder + "/", StringComparison.Ordinal)
                                            || (p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) && GameName(Path.GetFileNameWithoutExtension(p)))
                                            || (AssetDatabase.LoadMainAssetAtPath(p) is { } a && AssetDatabase.GetLabels(a).Contains(Label)))
                                .Distinct();
        }

        // A name without the number Unity puts after a duplicate's ("aska_wave 1", "aska_wave (1)"), as often as it was copied.
        private static string WithoutCopySuffix(string name)
        {
            string stripped = name;
            while (true)
            {
                var match = CopySuffix.Match(stripped);
                if (!match.Success) return stripped;
                stripped = match.Groups[1].Value;
            }
        }

        private static readonly Regex CopySuffix = new Regex(@"^(.+?)(?: \(\d+\)| \d+)$", RegexOptions.CultureInvariant);

        private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    }
}
