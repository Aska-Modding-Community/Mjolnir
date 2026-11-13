using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// A humanoid clip (Mixamo, motion capture, the Asset Store: an FBX imported with its Rig set to Humanoid) made into a clip
    /// of the game's skeleton. The imported skeleton gets a humanoid avatar of its own (its Biped bones named to Unity's human
    /// bones, from a T-pose of it), the clip plays on it through that avatar, and every bone's pose is recorded frame by frame
    /// into an ordinary clip whose curves name the game's bones, which the game plays as any clip. Its movement forward stays
    /// on the skeleton's root, which the game takes as root motion; In Place leaves it out. The body's turn and height belong
    /// in the pose (the source clip's Bake Into Pose, which the kit sets on the files it makes humanoid, and warns about
    /// otherwise), the pelvis keeps its position (the crouch), and the feet follow the clip's foot goals where it has them.
    /// </summary>
    public static class RigBake
    {
        private const float FrameRate = 30f;

        // Unity's human bones and the game's Biped bones that are them (a finger the skeleton lacks is left out).
        private static readonly (string Human, string Bone)[] HumanBones =
        {
            ("Hips", "Bip001 Pelvis"), ("Spine", "Bip001 Spine"), ("Chest", "Bip001 Spine1"), ("Neck", "Bip001 Neck"), ("Head", "Bip001 Head"),
            ("LeftShoulder", "Bip001 L Clavicle"), ("LeftUpperArm", "Bip001 L UpperArm"), ("LeftLowerArm", "Bip001 L Forearm"), ("LeftHand", "Bip001 L Hand"),
            ("RightShoulder", "Bip001 R Clavicle"), ("RightUpperArm", "Bip001 R UpperArm"), ("RightLowerArm", "Bip001 R Forearm"), ("RightHand", "Bip001 R Hand"),
            ("LeftUpperLeg", "Bip001 L Thigh"), ("LeftLowerLeg", "Bip001 L Calf"), ("LeftFoot", "Bip001 L Foot"), ("LeftToes", "Bip001 L Toe0"),
            ("RightUpperLeg", "Bip001 R Thigh"), ("RightLowerLeg", "Bip001 R Calf"), ("RightFoot", "Bip001 R Foot"), ("RightToes", "Bip001 R Toe0"),
            ("Left Thumb Proximal", "Bip001 L Finger0"), ("Left Thumb Intermediate", "Bip001 L Finger01"), ("Left Thumb Distal", "Bip001 L Finger02"),
            ("Left Index Proximal", "Bip001 L Finger1"), ("Left Index Intermediate", "Bip001 L Finger11"), ("Left Index Distal", "Bip001 L Finger12"),
            ("Left Middle Proximal", "Bip001 L Finger2"), ("Left Middle Intermediate", "Bip001 L Finger21"), ("Left Middle Distal", "Bip001 L Finger22"),
            ("Left Ring Proximal", "Bip001 L Finger3"), ("Left Ring Intermediate", "Bip001 L Finger31"), ("Left Ring Distal", "Bip001 L Finger32"),
            ("Right Thumb Proximal", "Bip001 R Finger0"), ("Right Thumb Intermediate", "Bip001 R Finger01"), ("Right Thumb Distal", "Bip001 R Finger02"),
            ("Right Index Proximal", "Bip001 R Finger1"), ("Right Index Intermediate", "Bip001 R Finger11"), ("Right Index Distal", "Bip001 R Finger12"),
            ("Right Middle Proximal", "Bip001 R Finger2"), ("Right Middle Intermediate", "Bip001 R Finger21"), ("Right Middle Distal", "Bip001 R Finger22"),
            ("Right Ring Proximal", "Bip001 R Finger3"), ("Right Ring Intermediate", "Bip001 R Finger31"), ("Right Ring Distal", "Bip001 R Finger32"),
        };

        [MenuItem("Mjolnir/Animation/Bake Humanoid Clips to Rig...", priority = 60)]
        public static void Open() => RigBakeWindow.Open();

        /// <summary>
        /// Bakes a humanoid clip onto an imported skeleton (<c>ragnar</c>, the male player's, or another) and saves it as
        /// <c>&lt;folder&gt;/&lt;clip&gt;.anim</c>; its path, null when the clip is not humanoid or the skeleton is missing.
        /// </summary>
        public static string Bake(AnimationClip source, string rig, string folder, bool inPlace)
        {
            if (source == null || !source.humanMotion)
            {
                Debug.LogError($"Mjolnir kit: {source?.name} is not a humanoid clip (set its FBX's Rig to Humanoid)");
                return null;
            }
            if (!AnimationImport.KeepsTurnAndHeightInPose(source, out string file))
                Debug.LogWarning($"Mjolnir kit: {file} ({source.name}): Root Transform Rotation or Position (Y) is not baked into the pose; " +
                                 "the game takes a clip's root motion only when asked (RootMotion), so without it the body's turn and height are lost " +
                                 "(the clip's Animation settings: Bake Into Pose for both, based upon Original and Feet)");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RigImporter.RigsFolder}/{rig}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"Mjolnir kit: the skeleton {rig} is not imported (Mjolnir > Rigs > Import from the Game)");
                return null;
            }
            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            PlayableGraph graph = default;
            try
            {
                // The bones alone: the model's parts beside master are no bones of the game, and the recorder would write a
                // curve for each of them.
                KitCommon.StripToSkeleton(instance);
                var avatar = HumanAvatar(instance);
                if (avatar == null) return null;
                var animator = instance.GetComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = true; // the clip's movement moves the root, where the game takes root motion from

                graph = PlayableGraph.Create("Mjolnir bake");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, source);
                // The feet where the clip's IK goals put them when it has them (an FBX's humanoid clip: Mixamo's, motion
                // capture), so they stay on the ground on legs of other lengths; a clip without goals keeps its muscles' legs (foot
                // IK would pull both feet to the origin).
                playable.SetApplyFootIK(HasFootGoals(source));
                var output = AnimationPlayableOutput.Create(graph, "bake", animator);
                output.SetSourcePlayable(playable);

                var recorder = new GameObjectRecorder(instance);
                recorder.BindComponentsOfType<Transform>(instance, true);
                int frames = Mathf.Max(1, Mathf.CeilToInt(source.length * FrameRate));
                for (int frame = 0; frame <= frames; frame++)
                {
                    float time = Mathf.Min(source.length, frame / FrameRate);
                    playable.SetTime(time);
                    graph.Evaluate(frame == 0 ? 0 : 1 / FrameRate);
                    recorder.TakeSnapshot(frame == 0 ? 0 : 1 / FrameRate);
                }
                var baked = new AnimationClip { name = source.name, frameRate = FrameRate };
                recorder.SaveToClip(baked, FrameRate);
                recorder.ResetRecording();
                Tidy(baked, inPlace);

                Directory.CreateDirectory(folder);
                string path = $"{folder}/{source.name}.anim";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(baked, path);
                AssetDatabase.SaveAssets();
                Debug.Log($"Mjolnir kit: {source.name} baked onto {rig} ({baked.length:0.00} s{(inPlace ? ", in place" : "")}) in {path}");
                Object.DestroyImmediate(avatar);
                return path;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Whether a humanoid clip carries its feet's IK goals (the LeftFootT/RightFootT curves).</summary>
        public static bool HasFootGoals(AnimationClip clip) =>
            AnimationUtility.GetCurveBindings(clip).Any(b => b.propertyName.StartsWith("LeftFootT.") || b.propertyName.StartsWith("RightFootT."));

        // A humanoid avatar of the skeleton, from a T-pose of it (arms out to the sides, legs straight down): Unity's human space
        // starts from one, and the game's rest pose is not one. Null (logged) when Unity finds it no human.
        private static Avatar HumanAvatar(GameObject instance)
        {
            var root = instance.transform;
            var byName = root.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            TPose(byName);
            var human = HumanBones.Where(h => byName.ContainsKey(h.Bone))
                                  .Select(h => new HumanBone { humanName = h.Human, boneName = h.Bone, limit = new HumanLimit { useDefaultValues = true } })
                                  .ToArray();
            var skeleton = root.GetComponentsInChildren<Transform>()
                               .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                               .ToArray();
            var description = new HumanDescription
            {
                human = human, skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
            if (avatar.isValid && avatar.isHuman) return avatar;
            Debug.LogError("Mjolnir kit: Unity found the skeleton no human (its bones or their T-pose)");
            Object.DestroyImmediate(avatar);
            return null;
        }

        // Arms along the sides out (left to -X, right to +X: the character faces +Z), legs straight down.
        private static void TPose(Dictionary<string, Transform> bones)
        {
            void Point(string bone, string next, Vector3 direction)
            {
                if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(next, out var n)) return;
                var current = n.position - b.position;
                if (current.sqrMagnitude < 1e-8f) return;
                b.rotation = Quaternion.FromToRotation(current, direction) * b.rotation;
            }
            foreach (var (side, x) in new[] { ("L", -1f), ("R", 1f) })
            {
                Point($"Bip001 {side} UpperArm", $"Bip001 {side} Forearm", new Vector3(x, 0, 0));
                Point($"Bip001 {side} Forearm", $"Bip001 {side} Hand", new Vector3(x, 0, 0));
                Point($"Bip001 {side} Hand", $"Bip001 {side} Finger2", new Vector3(x, 0, 0));
                Point($"Bip001 {side} Thigh", $"Bip001 {side} Calf", Vector3.down);
                Point($"Bip001 {side} Calf", $"Bip001 {side} Foot", Vector3.down);
            }
        }

        // Only what moves stays: a bone's position only on the root, Bip001 and the pelvis (Unity's Hips, which carries the
        // body's height: a crouch dropped with it lifted the feet 0.3 m off the ground), so each skeleton keeps its own bone
        // lengths; the scale nowhere; In Place drops the root's movement.
        private static void Tidy(AnimationClip clip, bool inPlace)
        {
            string pelvis = "master/Bip001/" + HumanBones.First(h => h.Human == "Hips").Bone;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                bool position = binding.propertyName.StartsWith("m_LocalPosition");
                bool scale = binding.propertyName.StartsWith("m_LocalScale");
                bool rootPath = binding.path.Length == 0;
                bool hips = binding.path == "master/Bip001" || binding.path == pelvis;
                if (scale || (position && !rootPath && !hips) || (position && rootPath && inPlace) || (rootPath && binding.propertyName.StartsWith("m_LocalRotation") && inPlace))
                    AnimationUtility.SetEditorCurve(clip, binding, null);
            }
        }
    }
}
