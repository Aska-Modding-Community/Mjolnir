using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>The Bake dialog: humanoid clips, the skeleton, the bundle folder, in place or not.</summary>
    public sealed class RigBakeWindow : EditorWindow
    {
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();
        private int _rig;
        private int _folder;
        private bool _inPlace;

        public static void Open()
        {
            var window = GetWindow<RigBakeWindow>(true, "Bake Humanoid Clips to Rig", true);
            window.minSize = new Vector2(460, 300);
            window._clips.Clear();
            window._clips.AddRange(Selection.objects.OfType<AnimationClip>().Where(c => c.humanMotion));
            foreach (var path in Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)))
                window._clips.AddRange(KitCommon.ClipsIn(path).Where(c => c.humanMotion));
        }

        private void OnGUI()
        {
            var rigs = KitCommon.RigsInOrder().ToArray();
            var folders = KitCommon.BundleFolders();
            if (rigs.Length == 0 || folders.Length == 0)
            {
                EditorGUILayout.HelpBox(rigs.Length == 0 ? "No skeleton imported: Mjolnir > Rigs > Import from the Game first." : "No bundle folder yet: Mjolnir > New Mod.", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("Humanoid clips: an FBX of Mixamo or motion capture imported with Rig > Animation Type: Humanoid. " +
                                    "Keep such FBX files outside Assets/Mods, so they do not go into a bundle themselves.", MessageType.None);
            for (int i = 0; i < _clips.Count; i++) _clips[i] = (AnimationClip)EditorGUILayout.ObjectField($"Clip {i + 1}", _clips[i], typeof(AnimationClip), false);
            var added = (AnimationClip)EditorGUILayout.ObjectField("Add a clip", null, typeof(AnimationClip), false);
            if (added != null) _clips.Add(added);
            _clips.RemoveAll(c => c == null);
            _rig = EditorGUILayout.Popup("Made on the skeleton", Mathf.Clamp(_rig, 0, rigs.Length - 1), rigs);
            _folder = EditorGUILayout.Popup("Into the bundle folder", Mathf.Clamp(_folder, 0, folders.Length - 1), folders);
            _inPlace = EditorGUILayout.ToggleLeft("In place (leave out the clip's movement forward)", _inPlace);
            using (new EditorGUI.DisabledScope(_clips.Count == 0))
            {
                if (GUILayout.Button($"Bake {_clips.Count} clip(s)"))
                {
                    var made = _clips.Select(c => RigBake.Bake(c, rigs[_rig], folders[_folder], _inPlace)).Where(p => p != null).ToArray();
                    if (made.Length > 0) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<AnimationClip>(made[0]));
                }
            }
        }
    }
}
