using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// Animations made in another program (Blender, Maya, 3ds Max, Cascadeur) on a skeleton the kit exported (Export Rigs as
    /// FBX), fitted to the game's skeleton as they are imported, for every model file under <c>Assets/Mods</c>:
    /// <list type="bullet">
    /// <item>the model is a generic one (a humanoid file is left alone: Bake Humanoid Clips to Rig takes it);</item>
    /// <item>a clip's paths start at the game's <c>master</c>: what the program puts above it goes (Blender writes the file's
    /// root as an empty, <c>ragnar/master/…</c>), and a path without it gets it (exporting only the armature makes
    /// <c>master</c> the file's root, <c>Bip001/…</c>);</item>
    /// <item>what moves <c>master</c> itself (in Blender the armature object) is the clip's root motion, on the clip's root
    /// as the game takes it;</item>
    /// <item>the clip is named after its action (Blender's <c>master|Raise</c> becomes <c>Raise</c>): the name a mod loads it
    /// by;</item>
    /// <item>a bone's position, rotation or scale that stays at the file's rest pose all through goes, left to the game's own
    /// animation (Blender writes every bone, those not animated at rest); Mjolnir → Animation → Keep Every Bone keeps them.</item>
    /// </list>
    /// Checked with Blender 5.2: a skeleton exported by the kit, a bone turned and the armature moved in Blender, the file
    /// written with Blender's own defaults and with the armature alone; both came back with every bone at the game's rest pose
    /// on the first frame (0°, 0 mm).
    /// </summary>
    public sealed class AnimationImport : AssetPostprocessor
    {
        private const string Master = "master";

        private bool Applies => assetPath.StartsWith(BundleBuilder.ModsFolder + "/", StringComparison.Ordinal)
                                && assetImporter is ModelImporter model && model.animationType != ModelImporterAnimationType.Human;

        private void OnPreprocessModel()
        {
            if (!Applies || !assetImporter.importSettingsMissing) return;
            // A new file: a generic model, without materials of its own (an animation's file carries none the game wants).
            var model = (ModelImporter)assetImporter;
            model.animationType = ModelImporterAnimationType.Generic;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        /// <summary>
        /// Marks a file the kit made humanoid itself (once: a file set back to generic by hand stays so). Such a file is a
        /// source for Bake Humanoid Clips to Rig and stays out of bundles.
        /// </summary>
        public const string HumanoidSourceSetting = "mjolnir:humanoid-source";

        // A file under Assets/Mods with a skeleton of another program's (Mixamo's mixamorig:Hips…, motion capture): it cannot
        // play on the game's bones as it is, so it becomes humanoid, ready to be baked onto a skeleton of the game. The switch
        // is a second import, which only OnPostprocessAllAssets may start.
        private static readonly List<string> s_toHumanoid = new List<string>();

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(BundleBuilder.ModsFolder + "/", StringComparison.Ordinal) || assetImporter is not ModelImporter model) return;
            if (model.animationType != ModelImporterAnimationType.Generic || (model.userData ?? "").Contains(HumanoidSourceSetting)) return;
            if (HasGameSkeleton(root) || !HasBody(root)) return;
            s_toHumanoid.Add(assetPath);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (s_toHumanoid.Count == 0) return;
            var paths = s_toHumanoid.Distinct().ToList();
            s_toHumanoid.Clear();
            foreach (string path in paths)
            {
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.userData = Marked(importer.userData, HumanoidSourceSetting, true);
                importer.SaveAndReimport();
                Debug.Log($"Mjolnir kit: {path} has a skeleton of its own, not the game's: it is humanoid now, a source to bake " +
                          "onto a skeleton of the game (Mjolnir > Animation > Bake Humanoid Clips to Rig), and stays out of the bundle");
            }
        }

        /// <summary>Marks a humanoid source whose clips the kit set up once (the modder may change them after).</summary>
        public const string ClipSettingsSetting = "mjolnir:clip-settings";

        // A humanoid source's clips keep the body's turn and height in the pose ("Bake Into Pose", based on the original
        // orientation and on the feet), and only the movement over the ground as root motion: the game moves a character by a
        // clip only when asked (RootMotion), and without it a turn or a crouch put into root motion was lost (a Mixamo attack
        // baked onto the game's skeleton stood 58° off and with its hips at standing height, its feet lifted 0.3 m).
        private void OnPreprocessAnimation()
        {
            if (assetImporter is not ModelImporter model || model.animationType != ModelImporterAnimationType.Human) return;
            string data = model.userData ?? "";
            if (!data.Contains(HumanoidSourceSetting) || data.Contains(ClipSettingsSetting)) return;
            var clips = model.clipAnimations.Length > 0 ? model.clipAnimations : model.defaultClipAnimations;
            if (clips.Length == 0) return;
            foreach (var clip in clips)
            {
                InPose(clip);
                if (PlainNames.Contains(clip.name, StringComparer.OrdinalIgnoreCase) && clips.Length == 1)
                    clip.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            }
            model.clipAnimations = clips;
            model.userData = Marked(data, ClipSettingsSetting, true);
        }

        /// <summary>A humanoid clip's settings for the game: the turn and height in the pose, the movement over the ground as root motion.</summary>
        public static void InPose(ModelImporterClipAnimation clip)
        {
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.heightFromFeet = true;
            clip.keepOriginalPositionY = false;
            clip.lockRootPositionXZ = false;
            clip.keepOriginalPositionXZ = true;
        }

        /// <summary>Whether a humanoid clip of a file keeps the turn and height in the pose; true when it is not in a file.</summary>
        public static bool KeepsTurnAndHeightInPose(AnimationClip clip, out string file)
        {
            file = AssetDatabase.GetAssetPath(clip);
            if (AssetImporter.GetAtPath(file) is not ModelImporter model) return true;
            var all = model.clipAnimations.Length > 0 ? model.clipAnimations : model.defaultClipAnimations;
            var settings = all.FirstOrDefault(c => c.name == clip.name) ?? (all.Length == 1 ? all[0] : null);
            return settings == null || (settings.lockRootRotation && settings.lockRootHeightY);
        }

        private static bool HasGameSkeleton(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Any(t => t.name == Master || t.name.StartsWith("Bip001", StringComparison.Ordinal));

        private static bool HasBody(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Any(t => t.name.EndsWith("Hips", StringComparison.OrdinalIgnoreCase) || t.name.EndsWith("Pelvis", StringComparison.OrdinalIgnoreCase));

        // Names programs give every clip alike (Mixamo's mixamo.com in every file): the file's name says which clip it is.
        private static readonly string[] PlainNames = { "mixamo.com", "Take 001", "Take 01", "Scene", "Default Take", "Animation" };

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (assetPath.StartsWith(BundleBuilder.ModsFolder + "/", StringComparison.Ordinal) && PlainNames.Contains(clip.name, StringComparer.OrdinalIgnoreCase))
                clip.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!Applies || !HasGameSkeleton(root)) return; // another skeleton's clip is a humanoid source, fitted by baking
            int bar = clip.name.LastIndexOf('|');
            if (bar >= 0 && bar < clip.name.Length - 1) clip.name = clip.name.Substring(bar + 1);
            if (!KeepsEveryBone(assetImporter)) LeaveRestToTheGame(root, clip);
            int moved = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                string path = GamePath(binding.path);
                if (path == binding.path) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                if (path == null) continue; // above master: the program's own nodes, nothing of the character
                var fitted = binding;
                fitted.path = path;
                AnimationUtility.SetEditorCurve(clip, fitted, curve);
                moved++;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                string path = GamePath(binding.path);
                if (path == binding.path) continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                if (path == null) continue;
                var fitted = binding;
                fitted.path = path;
                AnimationUtility.SetObjectReferenceCurve(clip, fitted, keys);
            }
            if (moved > 0)
                Debug.Log($"Mjolnir kit: {assetPath}: the clip {clip.name} fitted to the game's skeleton ({moved} curve(s) moved to the game's paths)");
        }

        // A bone's position, rotation or scale that stands at the file's rest pose all through the clip comes out of the clip,
        // so the game's own animation keeps it (seen in game: a bone a clip has no curves for walks and breathes as ever, one
        // held at rest stands still). Blender writes every bone, the ones not animated at rest: without this, a clip that
        // raises an arm stood the whole body in the skeleton's rest pose (a T-pose for the player).
        private void LeaveRestToTheGame(GameObject root, AnimationClip clip)
        {
            int left = 0, kept = 0;
            var bones = new List<string>();
            foreach (var group in AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).GroupBy(b => (b.path, Part(b.propertyName))))
            {
                if (group.Key.Item2 == null) continue;
                var bone = group.Key.path.Length == 0 ? root.transform : root.transform.Find(group.Key.path);
                if (bone == null || !AtRest(clip, group.ToList(), bone, group.Key.Item2))
                {
                    kept++;
                    continue;
                }
                foreach (var binding in group) AnimationUtility.SetEditorCurve(clip, binding, null);
                left++;
                if (group.Key.Item2 == "m_LocalRotation" && bones.Count < 200) bones.Add(group.Key.path.Split('/').Last());
            }
            if (left > 0)
                Debug.Log($"Mjolnir kit: {assetPath}: the clip {clip.name} keeps {kept} of the bones' positions, rotations and scales; " +
                          $"{left} stand at rest all through and are left to the game's own animation (rotations of {string.Join(", ", bones.Take(12))}{(bones.Count > 12 ? "…" : "")}). " +
                          "Mjolnir > Animation > Keep Every Bone keeps them in the clip.");
        }

        private static string Part(string property)
        {
            int dot = property.IndexOf('.');
            string part = dot < 0 ? property : property.Substring(0, dot);
            return part is "m_LocalRotation" or "m_LocalPosition" or "m_LocalScale" ? part : null;
        }

        private static bool AtRest(AnimationClip clip, List<EditorCurveBinding> bindings, Transform bone, string part)
        {
            // Each axis's curve once: the clip hands out a copy of a curve each time it is asked.
            var curves = new Dictionary<string, AnimationCurve>(StringComparer.Ordinal);
            foreach (var binding in bindings)
                if (binding.propertyName.StartsWith(part + ".", StringComparison.Ordinal) && !curves.ContainsKey(binding.propertyName.Substring(part.Length + 1)))
                    curves[binding.propertyName.Substring(part.Length + 1)] = AnimationUtility.GetEditorCurve(clip, binding);
            float Value(string axis, float time) => curves.TryGetValue(axis, out var curve) && curve != null ? curve.Evaluate(time) : float.NaN;
            int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * Mathf.Max(clip.frameRate, 1f)));
            for (int i = 0; i <= frames; i++)
            {
                float time = clip.length * i / frames;
                switch (part)
                {
                    case "m_LocalRotation":
                        var q = new Quaternion(Value("x", time), Value("y", time), Value("z", time), Value("w", time));
                        if (float.IsNaN(q.x + q.y + q.z + q.w) || Quaternion.Angle(q.normalized, bone.localRotation) > RestAngle) return false;
                        break;
                    case "m_LocalPosition":
                        var p = new Vector3(Value("x", time), Value("y", time), Value("z", time));
                        if (float.IsNaN(p.x + p.y + p.z) || Vector3.Distance(p, bone.localPosition) > RestDistance) return false;
                        break;
                    default:
                        var s = new Vector3(Value("x", time), Value("y", time), Value("z", time));
                        if (float.IsNaN(s.x + s.y + s.z) || Vector3.Distance(s, bone.localScale) > RestScale) return false;
                        break;
                }
            }
            return true;
        }

        private const float RestAngle = 0.01f;     // degrees
        private const float RestDistance = 0.00001f; // 0.01 mm
        private const float RestScale = 0.0001f;

        /// <summary>The import setting that keeps every bone in a file's clips, rest poses included (off by default).</summary>
        public const string KeepEveryBoneSetting = "mjolnir:keep-every-bone";

        public static bool KeepsEveryBone(AssetImporter importer) => importer.userData?.Contains(KeepEveryBoneSetting) == true;

        [MenuItem("Mjolnir/Animation/Keep Every Bone", priority = 61)]
        [MenuItem("Assets/Mjolnir/Keep Every Bone")]
        private static void ToggleKeepEveryBone()
        {
            foreach (string path in SelectedModelFiles())
            {
                var importer = AssetImporter.GetAtPath(path);
                bool keep = !KeepsEveryBone(importer);
                importer.userData = Marked(importer.userData, KeepEveryBoneSetting, keep);
                importer.SaveAndReimport();
                Debug.Log($"Mjolnir kit: {path}: {(keep ? "its clips keep every bone, those at rest too" : "its clips leave the bones at rest to the game")}");
            }
        }

        [MenuItem("Mjolnir/Animation/Keep Every Bone", true)]
        [MenuItem("Assets/Mjolnir/Keep Every Bone", true)]
        private static bool CanToggleKeepEveryBone()
        {
            var files = SelectedModelFiles();
            bool keeps = files.Count > 0 && files.All(f => KeepsEveryBone(AssetImporter.GetAtPath(f)));
            Menu.SetChecked("Mjolnir/Animation/Keep Every Bone", keeps);
            Menu.SetChecked("Assets/Mjolnir/Keep Every Bone", keeps);
            return files.Count > 0;
        }

        // An importer's user data with a marker of the kit's on or off, the other words in it (other markers) kept.
        private static string Marked(string data, string marker, bool on)
        {
            var words = (data ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w != marker).ToList();
            if (on) words.Add(marker);
            return string.Join(" ", words);
        }

        private static List<string> SelectedModelFiles() =>
            Selection.objects.Select(AssetDatabase.GetAssetPath)
                     .Where(p => p.StartsWith(BundleBuilder.ModsFolder + "/", StringComparison.Ordinal) && AssetImporter.GetAtPath(p) is ModelImporter)
                     .Distinct().ToList();

        /// <summary>
        /// A path of the file as the game's skeleton names it: from <c>master</c> on, <c>master</c> itself the clip's root
        /// (root motion), a skeleton without it put under it; null for the program's own nodes above it; others as they are.
        /// </summary>
        public static string GamePath(string path)
        {
            if (path.Length == 0) return path;
            string[] parts = path.Split('/');
            int master = Array.IndexOf(parts, Master);
            if (master >= 0)
                return master == parts.Length - 1 ? "" : string.Join("/", parts.Skip(master + 1).Prepend(Master));
            if (parts[0].StartsWith("Bip001", StringComparison.Ordinal) || parts[0].StartsWith("Bone ", StringComparison.Ordinal))
                return Master + "/" + path;
            return parts.Any(p => p.StartsWith("Bip001", StringComparison.Ordinal)) ? path : null;
        }
    }
}
