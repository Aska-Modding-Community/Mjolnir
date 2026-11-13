using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The kit at a glance: whether it is ready (the game's Unity, HDRP, the game found, Mjolnir in it, the game's skeletons and
    /// models imported and up to date), each with the step that fixes it; the mods in <c>Assets/Mods</c> with their bundles and
    /// whether the game has them; and the kit's tools. Opens by itself the first time the project opens on a computer.
    /// </summary>
    [InitializeOnLoad]
    public sealed class KitWindow : EditorWindow
    {
        /// <summary>The Unity the game is made with: bundles built in another one may not load.</summary>
        public const string GameUnity = "6000.3.12f1";

        private static string StartupKey => "Mjolnir.KitWindow.AtStartup." + Application.dataPath;
        private Vector2 _scroll;
        private GUIStyle _title, _section, _ok, _warn, _bad;

        static KitWindow()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || SessionState.GetBool("Mjolnir.KitWindow.Shown", false)) return;
                SessionState.SetBool("Mjolnir.KitWindow.Shown", true);
                if (EditorPrefs.GetBool(StartupKey, true)) Open();
            };
        }

        [MenuItem("Mjolnir/Kit Window", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<KitWindow>("Mjolnir Kit");
            window.minSize = new Vector2(460, 420);
            window.Show();
        }

        // What the window shows of the disk (the game's folder, the files the game wrote, the mods' folders), read once and
        // again when the window takes the focus, the project changes or one of its buttons ran: it walks folders, and OnGUI runs
        // on every repaint.
        private sealed class Snapshot
        {
            public string Game;
            public bool MjolnirInstalled, GameWroteRigs, GameHasNewer, HasModels, HasClips;
            public int ClipCount;
            public List<string> Rigs;
            public List<(string Folder, string Name, string Installed, bool InGame, List<string> Bundles)> Mods;
        }

        private Snapshot _snapshot;

        private void OnFocus() => Refresh();

        private void OnProjectChange() => Refresh();

        private void Refresh()
        {
            _snapshot = null;
            Repaint();
        }

        private static Snapshot Read()
        {
            var s = new Snapshot { Game = GameFolder.Find(false), Rigs = RigImporter.Imported().ToList() };
            if (s.Game != null)
            {
                s.MjolnirInstalled = Directory.Exists(Path.Combine(s.Game, "Mjolnir", "core"));
                string written = RigImporter.GameRigsFolder(false);
                s.GameWroteRigs = written != null && Directory.Exists(written) && Directory.GetFiles(written, "*.rig").Length > 0;
                s.GameHasNewer = s.Rigs.Count > 0 && RigImporter.GameHasNewer();
            }
            if (s.Rigs.Count > 0)
            {
                s.HasModels = RigImporter.HasModels();
                s.HasClips = GameClips.Imported();
                s.ClipCount = GameClips.Entries().Count;
            }
            s.Mods = KitCommon.ModPaths().Select(folder =>
            {
                string mod = Path.GetFileName(folder);
                string installed = s.Game == null ? null : Path.Combine(s.Game, "Mjolnir", "Mods", mod);
                var bundles = Directory.GetDirectories(folder)
                                       .Select(b => $"{Path.GetFileName(b).ToLowerInvariant()} ({Directory.GetFiles(b, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta") && !f.EndsWith(".cs"))})")
                                       .ToList();
                return (folder, mod, installed, installed != null && Directory.Exists(installed), bundles);
            }).ToList();
            return s;
        }

        // A button of the window's own: what it did may change what the window shows.
        private void Run(Action act)
        {
            act();
            _snapshot = null;
        }

        private void OnGUI()
        {
            Styles();
            _snapshot ??= Read();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Mjolnir Kit", _title);
            GUILayout.Label("AssetBundles for ASKA mods: models, item pictures and animations for the game's characters.", EditorStyles.wordWrappedMiniLabel);

            Setup();
            Mods();
            Tools();

            EditorGUILayout.EndScrollView();
            var bar = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            bool atStartup = EditorPrefs.GetBool(StartupKey, true);
            bool now = EditorGUILayout.ToggleLeft("Show this window when the project opens", atStartup);
            if (now != atStartup) EditorPrefs.SetBool(StartupKey, now);
        }

        private void Setup()
        {
            Section("Ready to work");

            bool rightUnity = Application.unityVersion == GameUnity;
            Row(rightUnity ? Status.Ok : Status.Bad, rightUnity
                ? $"Unity {Application.unityVersion}, the game's own"
                : $"Unity {Application.unityVersion}: the game is made with {GameUnity}, and bundles built in another Unity may not load");

            var pipeline = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
            bool hdrp = pipeline != null && PlayerSettings.colorSpace == ColorSpace.Linear;
            Row(hdrp ? Status.Ok : Status.Bad, hdrp ? "HDRP in linear colour, as the game renders" : "Not set up as the game renders (HDRP, linear colour)",
                hdrp ? null : "Set Up", () => KitSetup.Ensure(true));

            var s = _snapshot;
            Row(s.Game != null ? Status.Ok : Status.Bad, s.Game != null ? $"ASKA: {s.Game}" : "ASKA not found: the kit takes the game's skeletons and installs bundles there",
                s.Game != null ? "Change…" : "Choose…", () =>
                {
                    if (GameFolder.Pick() == null) EditorUtility.DisplayDialog("Mjolnir kit", "That folder has no Aska.exe: the game's folder was not changed.", "OK");
                });
            if (s.Game == null) return;

            Row(s.MjolnirInstalled ? Status.Ok : Status.Bad, s.MjolnirInstalled ? "Mjolnir is installed in the game" : "Mjolnir is not installed in the game: unpack its archive into the game's folder");

            if (s.Rigs.Count == 0)
                Row(Status.Warn, s.GameWroteRigs ? "The game's skeletons are not imported yet" : "The game has written no skeleton yet: start ASKA with Mjolnir once (its main menu is enough)",
                    s.GameWroteRigs ? "Import" : null, RigImporter.ImportFromMenu);
            else if (s.GameHasNewer)
                Row(Status.Warn, $"The game wrote newer skeletons or models than the kit's ({string.Join(", ", s.Rigs)})", "Import", RigImporter.ImportFromMenu);
            else Row(Status.Ok, $"The game's skeletons: {string.Join(", ", s.Rigs)}", "Import Again", RigImporter.ImportFromMenu);

            if (s.Rigs.Count > 0)
            {
                Row(s.HasModels ? Status.Ok : Status.Info, s.HasModels
                    ? "The characters wear the game's models (on this computer only, never in a bundle)"
                    : "Bare skeletons: for the game's people on them, turn on \"Characters' models for the kit\" on Mjolnir's settings page in the game, start it once and import again");
                Row(s.HasClips ? Status.Ok : Status.Info, s.HasClips
                    ? $"The game's own clips: {s.ClipCount}, to preview and learn from (on this computer only, never in a bundle)"
                    : "No clip of the game: for them, turn on \"The game's animations for the kit\" on Mjolnir's settings page in the game, start it once and import again");
            }
        }

        private void Mods()
        {
            Section("Mods");
            var mods = _snapshot.Mods;
            if (mods.Count == 0) EditorGUILayout.HelpBox("No mod yet. New Mod makes its folder: what goes into it becomes the mod's AssetBundles.", MessageType.None);
            foreach (var (folder, mod, installed, inGame, bundles) in mods)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(mod, EditorStyles.boldLabel);
                        GUILayout.Label(bundles.Count == 0 ? "no bundle folder" : "bundles: " + string.Join(", ", bundles), EditorStyles.miniLabel);
                        GUILayout.Label(inGame ? "installed in the game" : "not installed in the game: Build and Copy leaves its bundles in Build/Bundles", inGame ? _ok : _warn);
                    }
                    if (GUILayout.Button("Show", GUILayout.Width(55)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                    using (new EditorGUI.DisabledScope(!inGame))
                        if (GUILayout.Button(new GUIContent("In Game", "The mod's folder in the game"), GUILayout.Width(65))) EditorUtility.RevealInFinder(installed);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Mod…")) Run(ModFolders.NewMod);
                if (GUILayout.Button("Check")) Run(ModChecks.CheckFromMenu);
                if (GUILayout.Button("Build")) Run(BundleBuilder.BuildFromMenu);
                if (GUILayout.Button("Build and Copy to Game")) Run(ModFolders.BuildAndCopy);
            }
        }

        private void Tools()
        {
            Section("Animation");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview Animation")) AnimationPreview.Open();
                if (GUILayout.Button("Preview Stage")) PreviewStage.Open();
                if (GUILayout.Button("New Mask…")) Masks.NewMask();
                if (GUILayout.Button("Bake Humanoid Clips…")) RigBake.Open();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Export Rigs as FBX", "Each skeleton with a stick figure, for Blender, Maya, 3ds Max"))) RigExport.ExportAll();
                GUILayout.FlexibleSpace();
            }
            Section("Items");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Selection.gameObjects.Any(g => PrefabUtility.IsPartOfPrefabAsset(g))))
                    if (GUILayout.Button(new GUIContent("Render Item Icons", "The selected prefabs drawn into item pictures, 256 × 256"), GUILayout.Width(140))) IconRenderer.RenderSelected();
                GUILayout.Label("the prefabs selected in Assets/Mods, into the mod's item pictures", EditorStyles.miniLabel);
            }
        }

        private void OnSelectionChange() => Repaint();

        private enum Status { Ok, Info, Warn, Bad }

        private void Row(Status status, string text, string action = null, Action act = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string mark = status == Status.Ok ? "✓" : status == Status.Info ? "i" : status == Status.Warn ? "!" : "✗";
                var style = status == Status.Ok ? _ok : status == Status.Bad ? _bad : _warn;
                GUILayout.Label(mark, style, GUILayout.Width(16));
                GUILayout.Label(text, EditorStyles.wordWrappedLabel);
                if (action != null && act != null && GUILayout.Button(action, GUILayout.Width(95)))
                {
                    Run(act);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void Section(string name)
        {
            EditorGUILayout.Space(10);
            GUILayout.Label(name, _section);
            var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            EditorGUILayout.Space(4);
        }

        private void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            bool dark = EditorGUIUtility.isProSkin;
            _ok = new GUIStyle(EditorStyles.label) { normal = { textColor = dark ? new Color(0.45f, 0.8f, 0.45f) : new Color(0.1f, 0.5f, 0.1f) } };
            _warn = new GUIStyle(EditorStyles.label) { normal = { textColor = dark ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.6f, 0.4f, 0f) } };
            _bad = new GUIStyle(EditorStyles.label) { normal = { textColor = dark ? new Color(0.95f, 0.4f, 0.35f) : new Color(0.7f, 0.1f, 0.1f) } };
        }
    }
}
