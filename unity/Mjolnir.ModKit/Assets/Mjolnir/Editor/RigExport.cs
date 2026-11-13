using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// An imported skeleton of the game as an FBX for Blender, Maya or 3ds Max: the bones, at rest, with a stick figure skinned to
    /// them (a thin box from each bone to each of its children), so the editor reads an armature, not loose empties. Animate it
    /// there, keep the bones' names, and bring the FBX back into the kit: an imported clip of it plays on the game's characters.
    /// Into <c>Build/Rigs/&lt;rig&gt;.fbx</c>. Only bone names and poses of the game: no mesh of it.
    /// </summary>
    public static class RigExport
    {
        public const string OutputFolder = "Build/Rigs";

        [MenuItem("Mjolnir/Rigs/Export as FBX", priority = 71)]
        public static void ExportAll()
        {
            var written = RigImporter.Imported().Select(Export).Where(p => p != null).ToArray();
            if (written.Length == 0)
            {
                EditorUtility.DisplayDialog("Mjolnir kit", "No skeleton imported: Mjolnir > Rigs > Import from the Game first.", "OK");
                return;
            }
            EditorUtility.RevealInFinder(written[0]);
        }

        /// <summary>Writes <c>Build/Rigs/&lt;rig&gt;.fbx</c>; its path, null without that skeleton.</summary>
        public static string Export(string rig)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RigImporter.RigsFolder}/{rig}.prefab");
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab);
            instance.name = rig;
            try
            {
                foreach (var gizmo in instance.GetComponents<RigGizmo>()) Object.DestroyImmediate(gizmo);
                // The bones alone: the prefab also carries the game's model on them (RigModels), whose meshes and materials
                // must not go into the file, nor its parts be taken for bones by the stick figure.
                KitCommon.StripToSkeleton(instance);
                AddStickFigure(instance.transform);
                Directory.CreateDirectory(OutputFolder);
                string path = Path.Combine(OutputFolder, rig + ".fbx");
                // The bones keep their names: the exporter's Maya-compatible names would turn "Bip001 L Thigh" into
                // "Bip001_L_Thigh", and a clip made on those would name no bone of the game.
                ModelExporter.ExportObject(path, instance, new ExportModelOptions { UseMayaCompatibleNames = false, ExportFormat = ExportFormat.Binary });
                Debug.Log($"Mjolnir kit: the skeleton {rig} as {path}");
                return path;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // A box from every bone to each child, skinned wholly to the bone, as one mesh over the whole skeleton.
        private static void AddStickFigure(Transform root)
        {
            var bones = root.GetComponentsInChildren<Transform>().Where(t => t != root).ToArray();
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++) index[bones[i]] = i;
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            foreach (var bone in bones)
            {
                if (bone.parent == null || !index.TryGetValue(bone.parent, out int owner)) continue;
                var from = root.InverseTransformPoint(bone.parent.position);
                var to = root.InverseTransformPoint(bone.position);
                float length = Vector3.Distance(from, to);
                if (length < 0.005f) continue;
                Box(from, to, Mathf.Clamp(length * 0.12f, 0.004f, 0.025f), owner, vertices, weights, triangles);
            }
            var mesh = new Mesh { name = root.name + " sticks" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var holder = new GameObject("Sticks");
            holder.transform.SetParent(root, false);
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
        }

        private static void Box(Vector3 from, Vector3 to, float width, int bone, List<Vector3> vertices, List<BoneWeight> weights, List<int> triangles)
        {
            var along = (to - from).normalized;
            var side = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right).normalized * width;
            var up = Vector3.Cross(along, side).normalized * width;
            int start = vertices.Count;
            foreach (var end in new[] { from, to })
                foreach (var (s, u) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    vertices.Add(end + side * s + up * u);
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 });
                }
            int[] faces = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
            triangles.AddRange(faces.Select(f => start + f));
        }
    }
}
