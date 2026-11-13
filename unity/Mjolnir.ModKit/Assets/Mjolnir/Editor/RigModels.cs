using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// A character's model on its imported skeleton, for previews: the game writes it beside the skeleton
    /// (<c>Mjolnir/ModKit/models/&lt;rig&gt;/</c>, only when the platform's setting <c>[Kit] Models</c> is on: Mjolnir's settings in
    /// the game), and Import Rigs puts its parts on the skeleton's prefab as skinned meshes bound to its bones. Meshes, materials and
    /// textures go to <c>Assets/Mjolnir/Rigs/&lt;rig&gt;/</c>, which is not in git: they are the game's, for this computer only,
    /// and never go into a mod's bundle (nothing under <c>Assets/Mjolnir</c> does). The game's shaders are its own, so each material
    /// is HDRP/Lit with the game's maps at their own size, imported without loss that shows (BC7, a normal map as one): the colour
    /// map (hair cut out by its alpha and tinted, as the game's appearance tints it), the normal map, and the mask (metallic,
    /// occlusion, detail and smoothness as HDRP's; no metal on skin and hair, whose masks hold other things there); skin is
    /// subsurface scattering with HDRP's Skin profile, as the game's skin shaders are. The parts the game's default appearance
    /// shows are on, every other hair style and beard is there, switched off.
    /// </summary>
    public static class RigModels
    {
        // Hair textures are grey: the game colours them by the character's appearance. A plain brown in the kit.
        private static readonly Color HairTint = new Color(0.42f, 0.30f, 0.20f);

        /// <summary>
        /// Puts the model the game wrote into <paramref name="source"/> on the skeleton <paramref name="root"/> (its bones by
        /// path); the count of parts, 0 without a model.
        /// </summary>
        public static int Add(string rig, string source, GameObject root, IReadOnlyDictionary<string, Transform> bones)
        {
            string manifest = Path.Combine(source, "model.txt");
            if (!File.Exists(manifest)) return 0;
            string assets = $"{RigImporter.RigsFolder}/{rig}";
            // Assets are written over in place (their GUIDs kept, so a prefab or a scene that holds them keeps them: a folder
            // deleted and made again left the prefab's meshes missing), and what the model no longer has goes after.
            if (!AssetDatabase.IsValidFolder(assets)) AssetDatabase.CreateFolder(RigImporter.RigsFolder, rig);
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var parts = new Dictionary<int, Part>();
            foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('|');
                int index = int.Parse(f[1], CultureInfo.InvariantCulture);
                switch (f[0])
                {
                    case "part" when f.Length >= 11:
                        parts[index] = new Part
                        {
                            Name = f[2], MeshFile = f[3], Position = Vector(f[5]), Rotation = Rotation(f[6]), Scale = Vector(f[7]),
                            Group = f[8], Shown = f[9] == "1", RootBone = f[10],
                        };
                        break;
                    case "bones" when parts.TryGetValue(index, out var p):
                        p.Bones = f[2].Split(';');
                        break;
                    case "material" when parts.TryGetValue(index, out var p) && f.Length >= 7:
                        // A normal and a mask map since Mjolnir wrote them (an older model has the colour map only).
                        p.Materials.Add((int.Parse(f[2], CultureInfo.InvariantCulture), f[3], f[4],
                                         new Maps(f[5], f.Length > 7 ? f[7] : "", f.Length > 8 ? f[8] : "", f.Length > 9 ? f[9] : ""), f[6] == "1"));
                        break;
                }
            }

            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            int made = 0, missingBones = 0;
            foreach (var part in parts.Values)
            {
                string meshPath = Path.Combine(source, part.MeshFile);
                if (!File.Exists(meshPath)) continue;
                var mesh = ReadMesh(meshPath, part.Name);
                if (mesh == null)
                {
                    Debug.LogWarning($"Mjolnir kit: {meshPath} is not a mesh the kit reads");
                    continue;
                }
                mesh = Store(mesh, $"{assets}/{Path.GetFileNameWithoutExtension(part.MeshFile)}.asset", kept);

                var go = new GameObject(part.Name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = part.Position;
                go.transform.localRotation = part.Rotation;
                go.transform.localScale = part.Scale;
                go.SetActive(part.Shown);
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                var boneTransforms = new Transform[part.Bones.Length];
                for (int i = 0; i < part.Bones.Length; i++)
                {
                    if (bones.TryGetValue(part.Bones[i], out var bone)) boneTransforms[i] = bone;
                    else missingBones++;
                }
                renderer.bones = boneTransforms;
                renderer.rootBone = bones.TryGetValue(part.RootBone, out var rootBone) ? rootBone : boneTransforms.FirstOrDefault(b => b != null);
                renderer.sharedMesh = mesh;
                renderer.updateWhenOffscreen = true; // the bounds follow the animation in the preview
                var slots = new Material[Math.Max(mesh.subMeshCount, part.Materials.Count == 0 ? 1 : part.Materials.Max(m => m.Slot) + 1)];
                foreach (var (slot, name, shader, maps, cutout) in part.Materials)
                    slots[slot] = MaterialFor(name, shader, maps, cutout, source, assets, textures, materials, kept);
                renderer.sharedMaterials = slots;
                if (part.Group != "base") go.name = $"{part.Name} [{part.Group}]";
                made++;
            }
            foreach (string guid in AssetDatabase.FindAssets("", new[] { assets }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!kept.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            if (missingBones > 0)
                Debug.LogWarning($"Mjolnir kit: the model {rig}: {missingBones} bone(s) of its parts are not on the skeleton (an older skeleton file? start the game with this Mjolnir again)");
            var gizmo = root.GetComponent<RigGizmo>();
            if (gizmo != null && made > 0) gizmo.ShowSkeleton = false;
            return made;
        }

        private sealed class Part
        {
            public string Name, MeshFile, Group, RootBone;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool Shown;
            public string[] Bones = new string[0];
            public readonly List<(int Slot, string Name, string Shader, Maps Maps, bool Cutout)> Materials = new List<(int, string, string, Maps, bool)>();
        }

        // A material's map files beside the model ("" for none), and what the game's shader does with its mask ("name=value"
        // pairs: smoothnessMin, smoothnessMax, occlusion, metallic; those it has).
        private readonly struct Maps
        {
            public readonly string Color, Normal, Mask;
            public readonly Dictionary<string, float> Numbers;

            public Maps(string color, string normal, string mask, string numbers)
            {
                Color = color;
                Normal = normal;
                Mask = mask;
                Numbers = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (string pair in numbers.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] kv = pair.Split('=');
                    if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) Numbers[kv[0]] = value;
                }
            }

            public float Number(string name, float fallback) => Numbers.TryGetValue(name, out float value) ? value : fallback;
        }

        private enum MapKind { Color, Normal, Mask }

        private static Material MaterialFor(string name, string shader, Maps maps, bool cutout, string source, string assets,
                                            Dictionary<string, Texture2D> textures, Dictionary<string, Material> materials, HashSet<string> kept)
        {
            if (materials.TryGetValue(name, out var known)) return known;
            var material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            bool hair = shader.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool skin = shader.IndexOf("SSS", StringComparison.OrdinalIgnoreCase) >= 0; // HDRP SSS New Skin, HDRP_SSSMainCharacter
            var color = Texture(maps.Color, MapKind.Color, source, assets, textures, kept);
            if (color != null) material.SetTexture("_BaseColorMap", color);
            var normal = Texture(maps.Normal, MapKind.Normal, source, assets, textures, kept);
            if (normal != null)
            {
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalScale", 1f);
            }
            var mask = Texture(maps.Mask, MapKind.Mask, source, assets, textures, kept);
            if (mask != null)
            {
                material.SetTexture("_MaskMap", mask);
                // As the game's shader takes it: the smoothness in its range (skin's alpha scaled to 0.4: at 1 it shone wet),
                // the occlusion at its strength (the dark of the mask's green lifted by the rest), metal only where the shader
                // has it (the game's skin keeps 1 in the mask's red and its hair something else: neither is metal).
                material.SetFloat("_SmoothnessRemapMin", maps.Number("smoothnessMin", 0f));
                material.SetFloat("_SmoothnessRemapMax", maps.Number("smoothnessMax", 1f));
                material.SetFloat("_AORemapMin", 1f - Mathf.Clamp01(maps.Number("occlusion", 1f)));
                material.SetFloat("_AORemapMax", 1f);
                material.SetFloat("_MetallicRemapMin", 0f);
                material.SetFloat("_MetallicRemapMax", skin || hair ? 0f : Mathf.Clamp01(maps.Number("metallic", 1f)));
            }
            else material.SetFloat("_Smoothness", 0.25f);
            if (hair) material.SetColor("_BaseColor", HairTint);
            bool scatters = skin && SkinProfile != null;
            if (scatters)
            {
                material.SetFloat("_MaterialID", 0f); // subsurface scattering
                material.SetFloat("_SubsurfaceMask", 1f);
            }
            if (cutout)
            {
                material.SetFloat("_AlphaCutoffEnable", 1f);
                material.SetFloat("_AlphaCutoff", 0.5f);
                material.SetFloat("_DoubleSidedEnable", 1f);
            }
            material = Store(material, $"{assets}/{Safe(name)}.mat", kept);
            // The profile goes on the stored material: HDRP keeps its reference in an object inside the material's asset.
            if (scatters) UnityEngine.Rendering.HighDefinition.HDMaterial.SetDiffusionProfile(material, SkinProfile);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material); // HDRP's keywords and passes for what is set
            EditorUtility.SetDirty(material);
            materials[name] = material;
            return material;
        }

        // HDRP's own skin profile, which its default settings already list (so it needs no setting of the project's).
        private static UnityEngine.Rendering.HighDefinition.DiffusionProfileSettings SkinProfile =>
            AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.HighDefinition.DiffusionProfileSettings>(
                "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/SkinDiffusionProfile.asset");

        // A map beside the model, copied in and imported as what it is: a colour map in sRGB, a normal map as one, a mask linear;
        // each at its own size and in BC7 (the game's maps are DXT already, and a second DXT pass loses more of them).
        private static Texture2D Texture(string file, MapKind kind, string source, string assets, Dictionary<string, Texture2D> textures, HashSet<string> kept)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (textures.TryGetValue(file, out var known)) return known;
            string from = Path.Combine(source, file);
            if (!File.Exists(from)) return null;
            string to = $"{assets}/{file}";
            kept.Add(to);
            File.Copy(from, to, true);
            AssetDatabase.ImportAsset(to);
            if (AssetImporter.GetAtPath(to) is TextureImporter importer)
            {
                importer.textureType = kind == MapKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = kind == MapKind.Color;
                importer.alphaSource = kind == MapKind.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(to);
            MarkAsGame(texture);
            textures[file] = texture;
            return texture;
        }

        // An asset at its path: a new one, or the one there with the new content copied in (its GUID kept); labelled as the game's.
        private static T Store<T>(T fresh, string path, HashSet<string> kept) where T : Object
        {
            kept.Add(path);
            fresh.name = Path.GetFileNameWithoutExtension(path); // an asset's main object is named as its file
            var stored = KitCommon.WriteAsset(fresh, path);
            MarkAsGame(stored);
            return stored;
        }

        // The game's label on what the kit writes of its model, so the check that keeps the game's assets out of bundles knows
        // it wherever it is copied or moved, not only by its folder.
        private static void MarkAsGame(Object asset)
        {
            if (asset != null && !AssetDatabase.GetLabels(asset).Contains(GameClips.Label))
                AssetDatabase.SetLabels(asset, AssetDatabase.GetLabels(asset).Append(GameClips.Label).ToArray());
        }

        // The game's mesh file: "MJM2", then vertices, normals, UVs, bone weights, bind poses, each sub-mesh's triangles and the
        // tangents, each with its count first (Mjolnir.SDK.Animation's BodyModels); "MJM1", of an older Mjolnir, has no tangents,
        // and the kit makes them.
        private static Mesh ReadMesh(string file, string name)
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            string format = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (format != "MJM1" && format != "MJM2") return null;
            var vertices = new Vector3[reader.ReadInt32()];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var normals = new Vector3[reader.ReadInt32()];
            for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var uv = new Vector2[reader.ReadInt32()];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var weights = new BoneWeight[reader.ReadInt32()];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = new BoneWeight
                {
                    boneIndex0 = reader.ReadInt32(), weight0 = reader.ReadSingle(), boneIndex1 = reader.ReadInt32(), weight1 = reader.ReadSingle(),
                    boneIndex2 = reader.ReadInt32(), weight2 = reader.ReadSingle(), boneIndex3 = reader.ReadInt32(), weight3 = reader.ReadSingle(),
                };
            var bindposes = new Matrix4x4[reader.ReadInt32()];
            for (int i = 0; i < bindposes.Length; i++)
            {
                var m = new Matrix4x4();
                for (int k = 0; k < 16; k++) m[k] = reader.ReadSingle();
                bindposes[i] = m;
            }
            var mesh = new Mesh { name = name };
            if (vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (uv.Length == vertices.Length) mesh.uv = uv;
            int subMeshes = reader.ReadInt32();
            mesh.subMeshCount = subMeshes;
            for (int s = 0; s < subMeshes; s++)
            {
                var triangles = new int[reader.ReadInt32()];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
                mesh.SetTriangles(triangles, s);
            }
            var tangents = new Vector4[format == "MJM2" ? reader.ReadInt32() : 0];
            for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (weights.Length == vertices.Length) mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            if (normals.Length != vertices.Length) mesh.RecalculateNormals();
            // The game's own tangents, which its normal maps were baked against; made only when a file has none.
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            else mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Vector(string text)
        {
            var n = text.Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            return new Vector3(n[0], n[1], n[2]);
        }

        private static Quaternion Rotation(string text)
        {
            var n = text.Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            return new Quaternion(n[0], n[1], n[2], n[3]).normalized;
        }

        private static string Safe(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
    }
}
