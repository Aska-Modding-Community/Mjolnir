using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>The parts of the game's skeleton a mask takes or leaves (<see cref="Masks.PartOf"/>).</summary>
    [Flags]
    public enum BodyPart
    {
        None = 0,
        /// <summary>The skeleton's root, the pelvis and what hangs from it (pouches, side weapons).</summary>
        Hips = 1,
        /// <summary>The spine and what is on the back (back weapons, the backpack).</summary>
        Spine = 2,
        /// <summary>The neck, the head, the jaw, the eyelids, the hair.</summary>
        Head = 4,
        LeftArm = 8,
        RightArm = 16,
        LeftLeg = 32,
        RightLeg = 64,
        /// <summary>Above the waist: Mjolnir's <c>AnimationMask.UpperBody</c>.</summary>
        UpperBody = Spine | Head | LeftArm | RightArm,
    }

    /// <summary>
    /// Avatar masks on the game's skeleton: Mjolnir's own (<c>AnimationMask.UpperBody</c>, built into Mjolnir.SDK.Animation's
    /// bundle <c>masks</c>) and a mod's (New Mask: the parts of the body it takes, into a bundle folder of the mod, added in the
    /// mod with <c>AnimationMask.Add</c>). A mask lists every bone of the imported skeletons, each active or not: a bone left out
    /// of a mask would be the game's to animate or not as Unity sees fit, so none is left out.
    /// </summary>
    public static class Masks
    {
        /// <summary>Where Mjolnir's own masks are made (tools/build-bundles.ps1 copies the bundle into the module).</summary>
        public const string PlatformFolder = "Assets/Mods/Mjolnir.SDK.Animation/masks";

        [MenuItem("Mjolnir/Platform/Make Mjolnir's Masks", priority = 110)]
        public static void Make()
        {
            var paths = AllPaths();
            if (paths.Count == 0)
            {
                Debug.LogError("Mjolnir kit: no skeleton imported (Mjolnir > Rigs > Import from the Game)");
                return;
            }
            Save(Mask("UpperBody", paths, BodyPart.UpperBody), PlatformFolder);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Mjolnir/Animation/New Mask...", priority = 50)]
        public static void NewMask() => NewMaskWindow.Open();

        /// <summary>
        /// A mask of exactly the bones the selected clips move (each <c>&lt;clip&gt;Mask.mask</c> beside the clip's file): played
        /// through it, the clip moves only them and every other bone stays the game's, legs walking included.
        /// </summary>
        [MenuItem("Mjolnir/Animation/New Mask from Clip", priority = 51)]
        [MenuItem("Assets/Mjolnir/New Mask from Clip")]
        public static void NewMaskFromClip()
        {
            var paths = AllPaths();
            if (paths.Count == 0)
            {
                Debug.LogError("Mjolnir kit: no skeleton imported (Mjolnir > Rigs > Import from the Game)");
                return;
            }
            foreach (var clip in Selection.objects.OfType<AnimationClip>())
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip))!.Replace('\\', '/');
                string saved = Save(FromClip(clip, paths), folder);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<AvatarMask>(saved));
            }
        }

        [MenuItem("Mjolnir/Animation/New Mask from Clip", true)]
        [MenuItem("Assets/Mjolnir/New Mask from Clip", true)]
        private static bool CanMakeMaskFromClip() => Selection.objects.OfType<AnimationClip>().Any();

        /// <summary>A mask of every bone of every imported skeleton, active where the clip has curves (the clip's root left out).</summary>
        public static AvatarMask FromClip(AnimationClip clip, IReadOnlyList<string> paths)
        {
            var moved = new HashSet<string>(AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Where(p => p.Length > 0));
            var mask = new AvatarMask { name = clip.name + "Mask", transformCount = paths.Count };
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, moved.Contains(paths[i]));
            }
            int active = moved.Count(paths.Contains);
            Debug.Log($"Mjolnir kit: the mask {mask.name}: {active} bone(s) the clip {clip.name} moves" +
                      (moved.Count > active ? $", {moved.Count - active} path(s) no skeleton has" : ""));
            return mask;
        }

        /// <summary>The part of the body a bone belongs to, by the bones on its path (a clavicle, a thigh, the neck…).</summary>
        public static BodyPart PartOf(string path)
        {
            var parts = path.Split('/');
            bool Has(string bone) => parts.Contains(bone);
            if (Has("Bip001 L Clavicle")) return BodyPart.LeftArm;
            if (Has("Bip001 R Clavicle")) return BodyPart.RightArm;
            if (Has("Bip001 Neck")) return BodyPart.Head;
            if (Has("Bip001 L Thigh")) return BodyPart.LeftLeg;
            if (Has("Bip001 R Thigh")) return BodyPart.RightLeg;
            if (Has("Bip001 Spine")) return BodyPart.Spine;
            return BodyPart.Hips;
        }

        /// <summary>A mask of every bone of every imported skeleton, active where it belongs to <paramref name="parts"/>.</summary>
        public static AvatarMask Mask(string name, IReadOnlyList<string> paths, BodyPart parts)
        {
            var mask = new AvatarMask { name = name, transformCount = paths.Count };
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, (PartOf(paths[i]) & parts) != 0);
            }
            return mask;
        }

        /// <summary>Saves a mask as <c>&lt;folder&gt;/&lt;name&gt;.mask</c>, replacing one there.</summary>
        public static string Save(AvatarMask mask, string folder)
        {
            Directory.CreateDirectory(folder);
            string path = $"{folder}/{mask.name}.mask";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mask, path);
            Debug.Log($"Mjolnir kit: the mask {mask.name} ({mask.transformCount} bones) in {path}");
            return path;
        }

        /// <summary>Every bone's path of the imported skeletons, the shorter first (a parent before its children).</summary>
        public static List<string> AllPaths() =>
            RigImporter.Imported().SelectMany(RigImporter.Paths).Distinct().OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>The New Mask dialog: a name, the parts of the body, and the mod's bundle folder it goes into.</summary>
    public sealed class NewMaskWindow : EditorWindow
    {
        private static readonly BodyPart[] Parts = { BodyPart.Head, BodyPart.Spine, BodyPart.LeftArm, BodyPart.RightArm, BodyPart.Hips, BodyPart.LeftLeg, BodyPart.RightLeg };
        private string _name = "Arms";
        private BodyPart _parts = BodyPart.LeftArm | BodyPart.RightArm;
        private int _folder;

        public static void Open()
        {
            var window = GetWindow<NewMaskWindow>(true, "New Mask", true);
            window.minSize = window.maxSize = new Vector2(420, 330);
        }

        private void OnGUI()
        {
            var folders = KitCommon.BundleFolders();
            if (folders.Length == 0)
            {
                EditorGUILayout.HelpBox("No bundle folder yet: make one with Mjolnir > New Mod.", MessageType.Info);
                return;
            }
            if (Masks.AllPaths().Count == 0)
            {
                EditorGUILayout.HelpBox("No skeleton imported: Mjolnir > Rigs > Import from the Game first.", MessageType.Info);
                return;
            }
            _name = EditorGUILayout.TextField("Name", _name);
            _folder = Mathf.Clamp(EditorGUILayout.Popup("Bundle folder", _folder, folders), 0, folders.Length - 1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("The bones it moves (the others stay the game's)", EditorStyles.boldLabel);
            foreach (var part in Parts)
                if (EditorGUILayout.ToggleLeft(ObjectNames.NicifyVariableName(part.ToString()), (_parts & part) != 0) != ((_parts & part) != 0))
                    _parts ^= part;
            EditorGUILayout.Space();
            bool valid = ModFolders.IsName(_name) && _parts != BodyPart.None;
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Create"))
                {
                    string path = Masks.Save(Masks.Mask(_name, Masks.AllPaths(), _parts), folders[_folder]);
                    AssetDatabase.SaveAssets();
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<AvatarMask>(path));
                    Close();
                }
            }
            EditorGUILayout.HelpBox("In the mod: AnimationMask.Add(\"" + _name.ToLowerInvariant() + "\", \"bundles/<bundle>\", \"" + _name + "\").", MessageType.None);
        }
    }
}
