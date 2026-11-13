using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit.Examples
{
    /// <summary>
    /// What the examples' models are made of, in code: meshes built face by face (clockwise seen from outside, as Unity draws
    /// a front face), textures drawn pixel by pixel into PNG files, HDRP/Lit materials, and the prefab a mod names.
    /// </summary>
    internal static class ModelParts
    {
        public delegate Color Pixel(float u, float v);

        /// <summary>A mesh's vertices and the triangles of each of its materials.</summary>
        public sealed class Builder
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<List<int>> Triangles = new();

            public List<int> Part(int index)
            {
                while (Triangles.Count <= index) Triangles.Add(new List<int>());
                return Triangles[index];
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.subMeshCount = Triangles.Count;
                for (int i = 0; i < Triangles.Count; i++) mesh.SetTriangles(Triangles[i], i);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>A tube of radius r along a path (at least two points), its texture uRepeat times round and vRepeat along.</summary>
        public static void Tube(Builder mesh, int part, IReadOnlyList<Vector3> path, float r, int segments, float uRepeat, float vRepeat)
        {
            var triangles = mesh.Part(part);
            int start = mesh.Vertices.Count;
            var normal = Vector3.Cross(path[1] - path[0], Vector3.up).sqrMagnitude > 1e-8f ? Vector3.up : Vector3.forward;
            for (int k = 0; k < path.Count; k++)
            {
                var tangent = (path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                normal = Vector3.Cross(Vector3.Cross(tangent, normal), tangent).normalized; // kept square to the tangent
                var binormal = Vector3.Cross(tangent, normal);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments;
                    var outward = Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal;
                    mesh.Vertices.Add(path[k] + outward * r);
                    mesh.Normals.Add(outward);
                    mesh.Uvs.Add(new Vector2((float)i / segments * uRepeat, (float)k / (path.Count - 1) * vRepeat));
                }
            }
            int ring = segments + 1;
            for (int k = 0; k < path.Count - 1; k++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = start + k * ring + i, b = a + 1, c = a + ring, d = c + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            }
        }

        /// <summary>A sphere of radius r round centre, its texture once round and once from pole to pole.</summary>
        public static void Sphere(Builder mesh, int part, Vector3 centre, float r, int rings, int segments)
        {
            var triangles = mesh.Part(part);
            int start = mesh.Vertices.Count;
            for (int i = 0; i <= rings; i++)
            {
                float theta = i * Mathf.PI / rings;
                for (int j = 0; j <= segments; j++)
                {
                    float phi = j * Mathf.PI * 2 / segments;
                    var outward = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    mesh.Vertices.Add(centre + outward * r);
                    mesh.Normals.Add(outward);
                    mesh.Uvs.Add(new Vector2((float)j / segments, 1f - (float)i / rings));
                }
            }
            int row = segments + 1;
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = start + i * row + j, b = a + 1, c = a + row, d = c + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            }
        }

        public static Material Material(string folder, string name, Texture2D texture, float smoothness)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Texture2D Texture(string folder, string name, Pixel pixel, int size = 256)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) texture.SetPixel(x, y, pixel((float)x / size, (float)y / size));
            }
            string path = $"{folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The prefab a mod names in <c>WithModel(bundle, asset)</c> or <c>Models.Spawn</c>: one mesh with its materials; a
        /// <paramref name="solid"/> one with a box round it that stops the player.
        /// </summary>
        public static void Prefab(string folder, string name, Mesh mesh, Material[] materials, bool solid = false)
        {
            // A mesh made again goes into the asset there already, so it keeps its id (and the prefab its reference).
            mesh = KitCommon.WriteAsset(mesh, $"{folder}/{name}_Mesh.asset");
            var root = new GameObject(name);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;
            if (solid) root.AddComponent<BoxCollider>(); // sized to the mesh's bounds
            PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{name}.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }
    }
}
