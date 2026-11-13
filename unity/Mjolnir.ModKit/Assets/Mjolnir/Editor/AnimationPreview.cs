using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// A clip on every skeleton of the game at once. The characters stand on the preview stage (<see cref="PreviewStage"/>,
    /// opened with the window), at rest until a clip is chosen; outside the stage the window stands them in the open scene for
    /// as long as it is open, never saved with it. The clip is shown through the editor's animation mode, as the Animation
    /// window shows one: the scene is not changed, and the characters go back to rest when the clip is cleared or the window
    /// closes. The player's skeleton is taller than the villagers' (the pelvis at 1.05, 0.86 and 0.91 m), so a clip made on one
    /// shows here how it sits on the others.
    /// </summary>
    public sealed class AnimationPreview : EditorWindow
    {
        private const string LooseRootName = "Mjolnir Animation Preview";
        private static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f };
        private static readonly string[] SpeedNames = { "×0.1", "×0.25", "×0.5", "×1", "×2" };

        private AnimationClip _clip;
        private float _time;
        private bool _playing;
        private bool _loop = true;
        private bool _inPlace;
        private bool _followSelection = true;
        private int _speed = 3;
        private double _last;
        private GameObject _looseRoot; // the characters outside the stage
        private bool _sampling;
        private AnimationClip _masterClip; // the clip _movesMaster was asked of
        private bool _movesMaster;

        [MenuItem("Mjolnir/Animation/Preview Animation", priority = 40)]
        public static void Open()
        {
            PreviewStage.Open(); // the user may keep the open scene: the characters then stand there
            var window = GetWindow<AnimationPreview>("Mjolnir Preview");
            if (Selection.activeObject is AnimationClip clip) window._clip = clip;
            window.minSize = new Vector2(420, 150);
            window.Show();
        }

        [MenuItem("Assets/Mjolnir/Preview Animation", priority = 1100)]
        private static void OpenOnClip() => Open();

        [MenuItem("Assets/Mjolnir/Preview Animation", true)]
        private static bool IsClip() => Selection.activeObject is AnimationClip;

        private void OnEnable()
        {
            _last = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Selection.selectionChanged += FollowSelection;
            EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Selection.selectionChanged -= FollowSelection;
            EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
            Rest();
            if (_looseRoot != null) DestroyImmediate(_looseRoot);
        }

        private void SceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            Rest();
            _looseRoot = null;
            Repaint();
        }

        // A clip selected in the Project window becomes the preview's.
        private void FollowSelection()
        {
            if (!_followSelection || !(Selection.activeObject is AnimationClip clip) || clip == _clip) return;
            _clip = clip;
            _time = 0;
            Sample();
            Repaint();
        }

        private void OnGUI()
        {
            if (!RigImporter.Imported().Any())
            {
                EditorGUILayout.HelpBox("No skeleton of the game is imported yet. Start ASKA with Mjolnir once (the main menu is enough), then import its skeletons.", MessageType.Info);
                if (GUILayout.Button("Import Rigs from the Game")) RigImporter.ImportFromMenu();
                return;
            }
            var characters = Characters();

            using (new EditorGUILayout.HorizontalScope())
            {
                var clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", _clip, typeof(AnimationClip), false);
                if (clip != _clip) Choose(clip);
                var pick = GUILayoutUtility.GetRect(new GUIContent("Choose…"), EditorStyles.popup, GUILayout.Width(90));
                if (GUI.Button(pick, "Choose…", EditorStyles.popup)) new ClipPicker(clip =>
                {
                    Choose(clip);
                    _playing = true;
                    Repaint();
                }).Show(pick);
            }
            _followSelection = EditorGUILayout.ToggleLeft("Show the clip selected in the Project window", _followSelection);

            using (new EditorGUI.DisabledScope(_clip == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "Pause" : "Play", GUILayout.Width(60)))
                {
                    _playing = !_playing;
                    if (_playing && _clip != null && _time >= _clip.length) _time = 0;
                }
                if (GUILayout.Button("Stop", GUILayout.Width(45)))
                {
                    _playing = false;
                    _time = 0;
                }
                _loop = GUILayout.Toggle(_loop, "Loop", GUILayout.Width(48));
                _inPlace = GUILayout.Toggle(_inPlace, new GUIContent("In place", "Keeps each character over its place: a clip's root motion moves it no further"), GUILayout.Width(70));
                _speed = EditorGUILayout.Popup(_speed, SpeedNames, GUILayout.Width(55));
            }
            if (_clip != null)
            {
                float time = EditorGUILayout.Slider(_time, 0, _clip.length);
                if (!Mathf.Approximately(time, _time))
                {
                    _time = time;
                    _playing = false;
                }
                if (GameClips.Of(_clip) != null)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("The game's own clip, made from its file for this preview.", EditorStyles.miniLabel);
                        if (GUILayout.Button(new GUIContent("Save as Asset", "Keeps it under Assets/Mjolnir/GameClips to open in the Animation window; never in a bundle"), GUILayout.Width(100))
                            && GameClips.SaveAsAsset(_clip) is { } saved)
                        {
                            Selection.activeObject = saved;
                            EditorGUIUtility.PingObject(saved);
                        }
                    }
                float rate = _clip.frameRate > 0 ? _clip.frameRate : 30;
                EditorGUILayout.LabelField($"{_time:0.00} of {_clip.length:0.00} s · frame {Mathf.RoundToInt(_time * rate)} of {Mathf.RoundToInt(_clip.length * rate)} at {rate:0} fps"
                                           + (_clip.isLooping ? " · looping clip" : ""), EditorStyles.miniLabel);
            }
            else EditorGUILayout.HelpBox(GameClips.Imported()
                ? "Choose a clip, a mod's or one of the game's: the characters stand at rest until then."
                : "Choose a clip: the characters stand at rest until then. The game's own clips come with Import Rigs once the game wrote them (\"The game's animations for the kit\" on Mjolnir's settings page in the game).", MessageType.None);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(PreviewStage.IsOpen
                    ? $"On the preview stage: {string.Join(", ", characters.Select(c => c.name))}"
                    : $"In the open scene, while this window is open: {string.Join(", ", characters.Select(c => c.name))}", EditorStyles.miniLabel);
                if (!PreviewStage.IsOpen && GUILayout.Button("Open the Stage", GUILayout.Width(110))) PreviewStage.Open();
                if (PreviewStage.IsOpen && GUILayout.Button("Frame", GUILayout.Width(60))) PreviewStage.Frame();
            }
            Sample();
        }

        private void Choose(AnimationClip clip)
        {
            _clip = clip;
            _time = 0;
            if (clip == null)
            {
                _playing = false;
                Rest();
            }
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - _last);
            _last = now;
            if (!_playing || _clip == null) return;
            _time += delta * Speeds[_speed];
            if (_time > _clip.length)
            {
                if (_loop) _time %= Mathf.Max(0.0001f, _clip.length);
                else
                {
                    _time = _clip.length;
                    _playing = false;
                }
            }
            Sample();
            Repaint();
            SceneView.RepaintAll();
        }

        // The characters: the stage's, else the window's own in the open scene (made when missing).
        private List<GameObject> Characters()
        {
            if (PreviewStage.IsOpen)
            {
                if (_looseRoot != null) DestroyImmediate(_looseRoot);
                return PreviewStage.Characters();
            }
            if (_looseRoot == null)
            {
                Rest();
                _looseRoot = GameObject.Find("/" + LooseRootName) ?? new GameObject(LooseRootName);
                _looseRoot.hideFlags = HideFlags.DontSave;
                PreviewStage.Populate(_looseRoot.transform, HideFlags.DontSave);
            }
            return PreviewStage.InstancesUnder(_looseRoot.transform);
        }

        // The clip on every character at the time, through the animation mode (nothing in the scene changes); at rest without one.
        private bool Sample()
        {
            var characters = Characters();
            if (_clip == null)
            {
                Rest();
                return characters.Count > 0;
            }
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            _sampling = true;
            AnimationMode.BeginSampling();
            foreach (var character in characters) AnimationMode.SampleAnimationClip(character, _clip, _time);
            AnimationMode.EndSampling();
            // In place: a clip's root motion moves its character no further than over its place (its height kept). A clip
            // made in the kit or another program has it on the character's root; the game's own clips on master, as the game's
            // avatars read it, so master is held over its rest place too (only when the clip moves it: the animation mode gives
            // back only what the clip animates).
            if (_inPlace)
            {
                bool movesMaster = MovesMaster(_clip);
                foreach (var character in characters)
                {
                    character.transform.localPosition = new Vector3(0, character.transform.localPosition.y, 0);
                    var master = movesMaster ? character.transform.Find(KitCommon.Master) : null;
                    if (master == null) continue;
                    var rest = RestPosition(master, _clip);
                    master.localPosition = new Vector3(rest.x, master.localPosition.y, rest.z);
                }
            }
            SceneView.RepaintAll();
            return characters.Count > 0;
        }

        // Whether a clip moves master over the ground (its x or z), asked once per clip.
        private bool MovesMaster(AnimationClip clip)
        {
            if (clip != _masterClip)
            {
                _masterClip = clip;
                _movesMaster = AnimationUtility.GetCurveBindings(clip).Any(b => b.path == KitCommon.Master && b.type == typeof(Transform)
                                                                               && (b.propertyName == "m_LocalPosition.x" || b.propertyName == "m_LocalPosition.z"));
            }
            return _movesMaster;
        }

        // Where master stands at rest under its character's root: the skeleton prefab's own, else where the clip starts it.
        private static Vector3 RestPosition(Transform master, AnimationClip clip)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(master);
            if (source != null) return source.localPosition;
            float At(string axis) =>
                AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(KitCommon.Master, typeof(Transform), "m_LocalPosition." + axis)) is { } curve
                    ? curve.Evaluate(0) : 0;
            return new Vector3(At("x"), 0, At("z"));
        }

        // Every character back at rest: the animation mode gives back what it changed.
        private void Rest()
        {
            if (!_sampling) return;
            _sampling = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            SceneView.RepaintAll();
        }
    }
}
