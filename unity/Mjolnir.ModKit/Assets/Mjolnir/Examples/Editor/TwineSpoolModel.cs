using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit.Examples
{
    /// <summary>
    /// The model of Mjolnir.ItemsExample's twine spool, made in code so it can be made again anywhere, as its picture
    /// (items/twine_spool.png) shows it: twine wound round a stick between two dark wooden flanges, the stick's ends standing out
    /// of them, the loose end of the twine hanging down to the ground; it lies on its side. Two HDRP/Lit materials with
    /// textures drawn here, the twine's the same as the twine's own (TwineModel). It goes into
    /// Assets/Mods/Mjolnir.ItemsExample/models, the example's "models" bundle (BundleBuilder).
    /// </summary>
    public static class TwineSpoolModel
    {
        private const string Folder = "Assets/Mods/Mjolnir.ItemsExample/models";
        private const int Segments = 32;

        // Metres, about the size of the game's rope (0.14 × 0.38 × 0.09).
        private const float FlangeRadius = 0.105f, FlangeThickness = 0.02f, TwineRadius = 0.088f, Length = 0.24f;
        private const float StickRadius = 0.017f, StickOut = 0.028f, CordRadius = 0.0065f;

        [MenuItem("Mjolnir/Examples/Make the Twine Spool")]
        public static void Make()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var wood = ModelParts.Material(Folder, "TwineSpool_Wood", ModelParts.Texture(Folder, "TwineSpool_Wood", Wood), 0.2f);
            var wound = ModelParts.Material(Folder, "TwineSpool_Twine", ModelParts.Texture(Folder, "TwineSpool_Twine", Wound), 0.05f);
            var cord = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/Twine_Cord.mat"); // the twine's own (TwineModel.Make first)
            ModelParts.Prefab(Folder, "TwineSpool", Mesh(), cord == null ? new[] { wood, wound } : new[] { wood, wound, cord });
            Debug.Log($"Mjolnir kit: the twine spool made in {Folder}");
        }

        // The spool's axis is X, its middle the origin: it lies on its side as a dropped item does, on its flanges' rims.
        private static Mesh Mesh()
        {
            var mesh = new ModelParts.Builder();
            float half = Length / 2, inner = half - FlangeThickness, stick = half + StickOut;

            foreach (float side in new[] { -1f, 1f })
            {
                // A flange: its outer face round the stick, its inner face round the twine, its rim.
                Disc(mesh, side * half, side, FlangeRadius, StickRadius);
                Disc(mesh, side * inner, -side, FlangeRadius, TwineRadius);
                ModelParts.Tube(mesh, 0, Along(side * inner, side * half), FlangeRadius, Segments, 1f, 1f);
                // The stick's end standing out of it.
                ModelParts.Tube(mesh, 0, Along(side * half, side * stick), StickRadius, 12, 1f, 0.3f);
                Disc(mesh, side * stick, side, StickRadius, 0f);
            }
            // The twine wound between the flanges, and its loose end from the near flange down to the ground.
            ModelParts.Tube(mesh, 1, Along(-inner, inner), TwineRadius, Segments, 1f, 1f);
            var end = LooseEnd(inner);
            ModelParts.Tube(mesh, 2, end, CordRadius, 10, 1f, 8f);
            ModelParts.Sphere(mesh, 2, end[^1], CordRadius, 6, 10);
            return mesh.Build("TwineSpool_Mesh");
        }

        private static List<Vector3> Along(float x0, float x1) => new() { new Vector3(Mathf.Min(x0, x1), 0, 0), new Vector3(Mathf.Max(x0, x1), 0, 0) };

        // Off the front of the twine near the +X flange, down and out to the ground under the spool's rim.
        private static List<Vector3> LooseEnd(float inner)
        {
            var start = new Vector3(inner - 0.03f, -TwineRadius * 0.55f, TwineRadius * 0.8f);
            var bend = new Vector3(inner + 0.02f, -FlangeRadius * 0.9f, TwineRadius * 1.25f);
            var end = new Vector3(inner + 0.09f, -FlangeRadius + CordRadius, TwineRadius * 1.6f);
            var path = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                path.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * end); // a quadratic curve
            }
            return path;
        }

        // A ring (a full disc with hole 0) across the X axis at x, facing +X or -X: clockwise seen from the side it faces.
        private static void Disc(ModelParts.Builder mesh, float x, float facing, float outer, float hole)
        {
            var triangles = mesh.Part(0);
            int start = mesh.Vertices.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * Mathf.PI * 2 / Segments, c = Mathf.Cos(a), s = Mathf.Sin(a);
                mesh.Vertices.Add(new Vector3(x, c * outer, s * outer));
                mesh.Vertices.Add(new Vector3(x, c * hole, s * hole));
                mesh.Normals.Add(new Vector3(facing, 0, 0));
                mesh.Normals.Add(new Vector3(facing, 0, 0));
                mesh.Uvs.Add(new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f));
                float r = hole / outer;
                mesh.Uvs.Add(new Vector2(0.5f + c * 0.5f * r, 0.5f + s * 0.5f * r));
            }
            for (int i = 0; i < Segments; i++)
            {
                int a = start + i * 2, b = a + 2;
                if (facing > 0) triangles.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
                else triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        }

        // Dark wood, as the picture's flanges, with rings and grain.
        private static Color Wood(float u, float v)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float rings = Mathf.PerlinNoise(u * 6, v * 6) * 0.3f + Mathf.Sin(Mathf.Sqrt(dx * dx + dy * dy) * 90f) * 0.08f;
            var light = new Color(0.34f, 0.21f, 0.11f);
            var dark = new Color(0.20f, 0.12f, 0.06f);
            return Color.Lerp(light, dark, Mathf.Clamp01(0.3f + rings));
        }

        // Turns of twine side by side round the spool, each a twisted cord with a dark groove between turns, as in the picture.
        private static Color Wound(float u, float v)
        {
            float turn = Mathf.Repeat(v * 16f + u * 0.5f, 1f);
            float groove = Mathf.SmoothStep(0f, 1f, Mathf.Min(turn, 1f - turn) * 7f);
            float twist = Mathf.Repeat(u * 48f - v * 16f, 1f);
            float strands = Mathf.SmoothStep(0f, 1f, Mathf.Min(twist, 1f - twist) * 4f) * 0.25f;
            float fibres = Mathf.PerlinNoise(u * 80f, v * 40f) * 0.2f;
            var light = new Color(0.66f, 0.46f, 0.24f);
            var dark = new Color(0.27f, 0.17f, 0.08f);
            return Color.Lerp(dark, light, Mathf.Clamp01(groove * 0.85f + strands - fibres + 0.1f));
        }
    }
}
