using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// An item's picture from its model: the selected prefabs drawn by the kit's HDRP from above and in front, a little to the
    /// side, filling the picture, on a clear background, as <c>Build/Icons/&lt;mod&gt;/&lt;prefab&gt;.png</c> (256 × 256). A mod
    /// gives it to <c>Items.Add(…, icon: "items/&lt;name&gt;.png")</c>; Build and Copy to Game puts the mod's icons into its
    /// <c>items/</c> folder in the game. The background is found from two drawings, on black and on white: what differs between
    /// them is how much of the background shows through, so edges and glass come out right whatever the render's own format.
    /// </summary>
    public static class IconRenderer
    {
        public const string OutputFolder = "Build/Icons";
        public const int Size = 256;

        [MenuItem("Mjolnir/Items/Render Item Icons", priority = 30)]
        [MenuItem("Assets/Mjolnir/Render Item Icons", priority = 1110)]
        public static void RenderSelected()
        {
            var prefabs = Selection.gameObjects.Where(g => PrefabUtility.IsPartOfPrefabAsset(g)).ToArray();
            if (prefabs.Length == 0)
            {
                EditorUtility.DisplayDialog("Mjolnir kit", "Select one or more prefabs in Assets/Mods/<mod>/<bundle> first.", "OK");
                return;
            }
            var written = prefabs.Select(p => Render(p, Size)).Where(p => p != null).ToArray();
            EditorUtility.RevealInFinder(written.LastOrDefault() ?? OutputFolder);
        }

        [MenuItem("Mjolnir/Items/Render Item Icons", true)]
        [MenuItem("Assets/Mjolnir/Render Item Icons", true)]
        private static bool CanRender() => Selection.gameObjects.Any(g => PrefabUtility.IsPartOfPrefabAsset(g));

        /// <summary>Draws a prefab into <c>Build/Icons/&lt;mod&gt;/&lt;prefab&gt;.png</c>; the file's path, null when it has nothing to draw.</summary>
        public static string Render(GameObject prefab, int size)
        {
            // Each background in a preview of its own: a second drawing in one preview came out brighter (its exposure adapts).
            var onBlack = Draw(prefab, Color.black, size);
            var onWhite = onBlack == null ? null : Draw(prefab, Color.white, size);
            if (onBlack == null || onWhite == null)
            {
                Debug.LogWarning($"Mjolnir kit: {prefab.name} has no renderer to draw");
                return null;
            }
            var icon = Compose(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);

            string folder = Path.Combine(OutputFolder, ModOf(prefab));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, prefab.name + ".png");
            File.WriteAllBytes(path, icon.EncodeToPNG());
            Object.DestroyImmediate(icon);
            Debug.Log($"Mjolnir kit: the icon of {prefab.name} in {path}");
            return path;
        }

        // One drawing on a plain background, read back as a texture; null when the prefab has no renderer.
        private static Texture2D Draw(GameObject prefab, Color background, int size)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var instance = preview.InstantiatePrefabInScene(prefab);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);

                var camera = preview.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                Frame(camera, bounds, new Vector3(0.6f, 0.75f, -1f).normalized); // in front, above, a little to the right
                // A key light from above on the left and a fill from behind: in the kit's HDRP the preview's lights at 1 draw
                // a model at about a fifth of white (measured), these at about two thirds, as the game's pictures are lit.
                preview.lights[0].intensity = 6f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                preview.lights[1].intensity = 3f;
                preview.lights[1].transform.rotation = Quaternion.Euler(-20f, 160f, 0f);

                preview.BeginStaticPreview(new Rect(0, 0, size, size));
                preview.Render(true);
                var drawn = preview.EndStaticPreview();
                var readable = new Texture2D(drawn.width, drawn.height, TextureFormat.RGBA32, false);
                readable.SetPixels(drawn.GetPixels());
                readable.Apply();
                Object.DestroyImmediate(drawn);
                return readable;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        // An orthographic view along `view` that fits the model's box: centred, its widest side filling 90 % of the picture, so a
        // flat coil fills it as a tall spool does.
        private static void Frame(Camera camera, Bounds bounds, Vector3 view)
        {
            float radius = bounds.extents.magnitude;
            camera.orthographic = true;
            camera.transform.position = bounds.center + view * radius * 3f;
            camera.transform.LookAt(bounds.center);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = camera.transform.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y); maxY = Mathf.Max(maxY, local.y);
            }
            camera.transform.position += camera.transform.right * (minX + maxX) / 2 + camera.transform.up * (minY + maxY) / 2;
            camera.orthographicSize = Mathf.Max(maxX - minX, maxY - minY) / 2 / 0.9f;
            camera.nearClipPlane = radius * 0.5f;
            camera.farClipPlane = radius * 5.5f;
        }

        // The background shows through as much as white and black differ: alpha = 1 - (white - black); colour = black / alpha.
        private static Texture2D Compose(Texture2D onBlack, Texture2D onWhite)
        {
            var black = onBlack.GetPixels();
            var white = onWhite.GetPixels();
            var result = new Color[black.Length];
            for (int i = 0; i < black.Length; i++)
            {
                float through = Mathf.Clamp01(((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f);
                float alpha = 1f - through;
                result[i] = alpha <= 0.004f
                    ? new Color(0, 0, 0, 0)
                    : new Color(Mathf.Clamp01(black[i].r / alpha), Mathf.Clamp01(black[i].g / alpha), Mathf.Clamp01(black[i].b / alpha), alpha);
            }
            var icon = new Texture2D(onBlack.width, onBlack.height, TextureFormat.RGBA32, false);
            icon.SetPixels(result);
            icon.Apply();
            return icon;
        }

        // The mod a prefab belongs to: the folder under Assets/Mods.
        private static string ModOf(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            string prefix = BundleBuilder.ModsFolder + "/";
            return path.StartsWith(prefix) ? path.Substring(prefix.Length).Split('/')[0] : "Other";
        }
    }
}
