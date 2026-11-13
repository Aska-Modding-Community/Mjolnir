using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The scene the game's characters stand in, at rest, for a mod's animations to be played on them: a ground of one-metre
    /// squares (root motion shows against it), a sun from the front above, a sky lighting the shadows as a clear day's does, a
    /// fixed exposure, a camera in front, and every imported skeleton (with its model, when the game wrote one) in a line, named
    /// in the scene view. Made in code into <c>Assets/Mjolnir/Preview</c> the first time (git-ignored: made again when missing);
    /// the characters follow every Import Rigs. The animation preview plays a clip on them without changing the scene.
    /// </summary>
    [InitializeOnLoad]
    public static class PreviewStage
    {
        public const string Folder = "Assets/Mjolnir/Preview";
        public const string ScenePath = Folder + "/Mjolnir Preview.unity";
        public const string CharactersName = "Characters";
        private const float GroundSize = 200;  // metres a side
        private const float SunLux = 100000;   // direct sunlight
        private const float SkyLux = 20000;    // a clear sky's light on the ground beside it
        private const float Exposure = 14.5f;  // EV100 for them
        private const float Gap = 0.6f;        // metres between two characters' arms held out
        private const float BareSpacing = 2f;  // skeletons without a model
        private const string GridKey = "Mjolnir.PreviewStage.Grid";

        // What each skeleton is in the game, for its name in the scene view.
        private static readonly Dictionary<string, string> s_roles = new Dictionary<string, string>
        {
            ["ragnar"] = "the player, male",
            ["aska"] = "the player, female",
            ["peasant_male"] = "villager, male",
            ["peasant_female"] = "villager, female",
        };

        static PreviewStage()
        {
            SceneView.duringSceneGui += DrawNames;
            EditorSceneManager.activeSceneChangedInEditMode += (_, scene) =>
            {
                if (scene.path != ScenePath) GiveGridBack();
            };
        }

        /// <summary>True while the stage is the open scene.</summary>
        public static bool IsOpen => SceneManager.GetActiveScene().path == ScenePath;

        [MenuItem("Mjolnir/Animation/Open Preview Stage", priority = 41)]
        public static void OpenFromMenu() => Open();

        /// <summary>
        /// Opens the stage (made first when missing) with its characters as the kit has them, asking to save the scene open
        /// before; false when the user kept that scene.
        /// </summary>
        public static bool Open()
        {
            if (IsOpen)
            {
                Sharpen();
                SyncCharacters();
                return true;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else Make();
            Sharpen();
            SyncCharacters();
            Frame();
            return true;
        }

        // No motion blur on the stage: HDRP's default profile has it on, and a pose that moves between two frames of the scene
        // view (few, while the editor is in the background) left ghosts of arms beside the characters. Also for a stage made
        // before (its profile is the stage's own asset).
        private static void Sharpen()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Folder}/Sky and Exposure.asset");
            if (profile == null || (profile.TryGet<MotionBlur>(out var existing) && existing.intensity.overrideState && existing.intensity.value == 0)) return;
            if (!profile.TryGet<MotionBlur>(out var blur))
            {
                blur = profile.Add<MotionBlur>(false);
                AssetDatabase.AddObjectToAsset(blur, profile);
            }
            blur.intensity.Override(0f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The characters standing on the open stage: each skeleton's object, none when the stage is not open.</summary>
        public static List<GameObject> Characters()
        {
            var root = IsOpen ? GameObject.Find("/" + CharactersName) : null;
            return root == null ? new List<GameObject>() : InstancesUnder(root.transform);
        }

        /// <summary>
        /// Puts every imported skeleton on the open stage, in a line, and takes away those no longer imported; the scene is
        /// saved when that changed it. Nothing when the stage is not open (it is brought up to date when it opens).
        /// </summary>
        public static void SyncCharacters()
        {
            if (!IsOpen) return;
            var root = GameObject.Find("/" + CharactersName);
            if (root == null) root = new GameObject(CharactersName);
            if (Populate(root.transform, HideFlags.None)) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// Every imported skeleton under <paramref name="parent"/>, each in a holder of its own name at its place in the line (the
        /// skeleton at the holder's origin, so a clip's root motion moves it from there): the player's Ragnar and Aska on the
        /// left of a camera in front, the villagers after. True when it added, removed or moved one.
        /// </summary>
        public static bool Populate(Transform parent, HideFlags flags)
        {
            bool changed = false;
            var rigs = KitCommon.RigsInOrder();
            foreach (Transform holder in parent.Cast<Transform>().ToList())
            {
                if (rigs.Contains(holder.name) && Instance(holder) != null) continue;
                Object.DestroyImmediate(holder.gameObject);
                changed = true;
            }
            var holders = new List<Transform>();
            foreach (string rig in rigs)
            {
                var holder = parent.Find(rig);
                if (holder == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RigImporter.RigsFolder}/{rig}.prefab");
                    if (prefab == null) continue;
                    holder = new GameObject(rig) { hideFlags = flags }.transform;
                    holder.SetParent(parent, false);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                    instance.hideFlags = flags;
                    changed = true;
                }
                holder.SetSiblingIndex(holders.Count);
                holders.Add(holder);
            }
            // One spacing for all: the tallest character's height, which is about its arms' span (the villagers stand with their
            // arms down at rest, and a clip may hold them out), and a gap. Measured at rest only (a clip moves the arms).
            if (AnimationMode.InAnimationMode() && !changed) return false;
            float size = holders.Select(h => Size(Instance(h))).DefaultIfEmpty(0).Max();
            float spacing = size > 0 ? size + Gap : BareSpacing;
            for (int i = 0; i < holders.Count; i++)
            {
                var place = new Vector3(spacing * ((holders.Count - 1) / 2f - i), 0, 0); // the characters face +Z: +X is the camera's left
                if ((holders[i].localPosition - place).sqrMagnitude < 0.0001f) continue;
                holders[i].localPosition = place;
                changed = true;
            }
            return changed;
        }

        /// <summary>The skeletons' objects under a parent made by <see cref="Populate"/>.</summary>
        public static List<GameObject> InstancesUnder(Transform parent) =>
            parent.Cast<Transform>().Select(Instance).Where(i => i != null).ToList();

        /// <summary>The scene view looking at the line of characters from the front.</summary>
        public static void Frame()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            var characters = Characters();
            float width = characters.Count == 0 ? 4 : characters.Max(c => c.transform.position.x) - characters.Min(c => c.transform.position.x) + 2;
            // The grid lies on the ground, flickering over its squares: off while the stage is open, given back after.
            if (!SessionState.GetBool(GridKey + ".Taken", false))
            {
                SessionState.SetBool(GridKey, view.showGrid);
                SessionState.SetBool(GridKey + ".Taken", true);
            }
            view.showGrid = false;
            view.LookAt(new Vector3(0, 1, 0), Quaternion.Euler(8, 180, 0), Mathf.Max(1.5f, width * 0.3f), false, true);
        }

        private static void GiveGridBack()
        {
            if (!SessionState.GetBool(GridKey + ".Taken", false)) return;
            SessionState.SetBool(GridKey + ".Taken", false);
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.showGrid = SessionState.GetBool(GridKey, true);
        }

        private static GameObject Instance(Transform holder) => holder.childCount > 0 ? holder.GetChild(0).gameObject : null;

        // The larger of a character's height and width, from its model; 0 for a skeleton without one.
        private static float Size(GameObject rig)
        {
            if (rig == null) return 0;
            var renderers = rig.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToList();
            if (renderers.Count == 0) return 0;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return Mathf.Max(bounds.size.x, bounds.size.y);
        }

        // Each character's name and what it is in the game, above its head, while the stage is open.
        private static void DrawNames(SceneView view)
        {
            if (!IsOpen || Event.current.type != EventType.Repaint) return;
            var style = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter };
            var small = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleCenter };
            foreach (var character in Characters())
            {
                var top = character.transform.position + Vector3.up * 2.25f; // over the character as a clip moves it
                Handles.Label(top, character.name, style);
                if (s_roles.TryGetValue(character.name, out string role)) Handles.Label(top - Vector3.up * 0.13f, role, small);
            }
        }

        private static void Make()
        {
            Directory.CreateDirectory(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Sky and exposure: HDRP's own sky picture, as bright as a clear day's sky (a physically based sky stayed black in
            // the kit), lighting the shadows as it is (Dynamic: the default profile's Static wants a baked sky, and the
            // shadowed side of a model was black), and a fixed exposure for the sun, so a model reads the same whatever the
            // camera looks at.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var environment = profile.Add<VisualEnvironment>(false);
            environment.skyType.Override((int)SkyType.HDRI);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = profile.Add<HDRISky>(false);
            sky.skyIntensityMode.Override(SkyIntensityMode.Lux);
            sky.desiredLuxValue.Override(SkyLux);
            var exposure = profile.Add<UnityEngine.Rendering.HighDefinition.Exposure>(false);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(Exposure);
            AssetDatabase.CreateAsset(profile, $"{Folder}/Sky and Exposure.asset");
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            var volume = new GameObject("Sky and Exposure").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // The sun from the front left, above: the characters face +Z, toward the camera.
            var sun = new GameObject("Sun");
            sun.transform.SetPositionAndRotation(new Vector3(0, 12, -6), Quaternion.Euler(40, 150, 0)); // its icon above the line
            var light = sun.AddComponent<Light>();
            if (sun.GetComponent<HDAdditionalLightData>() == null) sun.AddComponent<HDAdditionalLightData>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = SunLux;
            light.useColorTemperature = true;
            light.colorTemperature = 5500;
            light.shadows = LightShadows.Soft;

            // The ground: squares a metre wide in two close tones, at y 0 where the characters' roots stand.
            var checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "Ground Squares" };
            var pale = new Color(0.46f, 0.45f, 0.42f);
            var deep = new Color(0.40f, 0.39f, 0.36f);
            checker.SetPixels(new[] { pale, deep, deep, pale });
            checker.Apply();
            AssetDatabase.CreateAsset(checker, $"{Folder}/Ground Squares.asset");
            var material = new Material(Shader.Find("HDRP/Lit")) { name = "Ground" };
            material.SetTexture("_BaseColorMap", checker);
            material.SetTextureScale("_BaseColorMap", Vector2.one * (GroundSize / 2)); // a texture repeat is 2 m: two squares
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.1f);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, $"{Folder}/Ground.mat");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * (GroundSize / 10); // a plane is 10 m a side
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;

            // A camera in front, for the game view.
            var camera = new GameObject("Main Camera") { tag = "MainCamera" };
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.4f, 13), Quaternion.Euler(3, 180, 0));
            camera.AddComponent<Camera>().fieldOfView = 40;
            if (camera.GetComponent<HDAdditionalCameraData>() == null) camera.AddComponent<HDAdditionalCameraData>();

            new GameObject(CharactersName);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Mjolnir kit: the preview stage made at {ScenePath}");
        }
    }
}
