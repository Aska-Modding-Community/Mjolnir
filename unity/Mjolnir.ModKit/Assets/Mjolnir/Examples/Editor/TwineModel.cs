using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit.Examples
{
    /// <summary>
    /// The model of Mjolnir.ItemsExample's twine, made in code so it can be made again anywhere, as its picture
    /// (items/twine.png) shows it: a flat coil of twine, four turns lying on the ground, the loose end running off at the outside;
    /// one HDRP/Lit material whose twisted strands are drawn here. It goes into Assets/Mods/Mjolnir.ItemsExample/models, the
    /// example's "models" bundle (BundleBuilder).
    /// </summary>
    public static class TwineModel
    {
        private const string Folder = "Assets/Mods/Mjolnir.ItemsExample/models";

        // Metres: turns touching one another from the hole in the middle out to the coil's rim.
        private const float CordRadius = 0.0065f, Inner = 0.018f, Outer = 0.07f, Turns = 4f, Tail = 0.08f;

        [MenuItem("Mjolnir/Examples/Make the Twine")]
        public static void Make()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var twine = ModelParts.Material(Folder, "Twine_Cord", ModelParts.Texture(Folder, "Twine_Cord", Cord), 0.05f);
            var path = Coil();
            var mesh = new ModelParts.Builder();
            ModelParts.Tube(mesh, 0, path, CordRadius, 10, 1f, Length(path) / 0.03f);
            // Both ends of the cord closed.
            ModelParts.Sphere(mesh, 0, path[0], CordRadius, 6, 10);
            ModelParts.Sphere(mesh, 0, path[^1], CordRadius, 6, 10);
            ModelParts.Prefab(Folder, "Twine", mesh.Build("Twine_Mesh"), new[] { twine });
            Debug.Log($"Mjolnir kit: the twine made in {Folder}");
        }

        // From the middle out, flat on the ground (the cord's centre at height 0), then the loose end along the last turn's way.
        private static List<Vector3> Coil()
        {
            var path = new List<Vector3>();
            float end = Turns * Mathf.PI * 2;
            const int steps = 200;
            for (int i = 0; i <= steps; i++)
            {
                float a = end * i / steps, r = Mathf.Lerp(Inner, Outer, (float)i / steps);
                path.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
            }
            var last = path[^1];
            var way = (path[^1] - path[^2]).normalized;
            var outward = new Vector3(last.x, 0, last.z).normalized;
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                path.Add(last + (way * 0.7f + outward * 0.3f).normalized * (Tail * t) + outward * (0.02f * t * t));
            }
            return path;
        }

        private static float Length(IReadOnlyList<Vector3> path)
        {
            float length = 0;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        // Two strands twisted round each other, tan with dark grooves between them and fibres along them, as in the picture.
        private static Color Cord(float u, float v)
        {
            float strand = Mathf.Repeat(u * 2f + v, 1f);
            float groove = Mathf.SmoothStep(0f, 1f, Mathf.Min(strand, 1f - strand) * 6f);
            float fibres = Mathf.PerlinNoise(u * 12f, v * 90f) * 0.25f;
            var light = new Color(0.66f, 0.46f, 0.24f);
            var dark = new Color(0.27f, 0.17f, 0.08f);
            return Color.Lerp(dark, light, Mathf.Clamp01(groove - fibres + 0.2f));
        }
    }
}
