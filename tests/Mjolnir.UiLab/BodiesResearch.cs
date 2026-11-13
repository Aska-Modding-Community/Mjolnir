using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "bodies" scenario (autopilot.once containing "bodies"): what of the people's bodies can be read in the game, for the
/// Mjolnir kit's models. In the main menu, for every character definition's prefab: its renderers (skinned or not, active, the
/// mesh's name, whether it is readable, vertices, sub-meshes, bones), whether vertices, normals, UVs, triangles, bone weights and
/// bind poses come back from the mesh as it is and from a baked copy, its materials (shader, textures by the shader's
/// properties, their size, format and readability; every other property of the material: colours, numbers, vectors), one
/// texture copied through a render texture and encoded as PNG into shots/bodies-<texture>.png, and, for the texture quality of
/// the kit's models, each normal, mask and tint map's channels (least, mean and most of each, read linear at 256 a side), so
/// what each channel holds can be told. Nothing is changed in the game.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> BodiesResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var text = new System.Text.StringBuilder();
        bool textureTried = false;
        foreach (var definition in Resources.FindObjectsOfTypeAll<CharacterDefinition>())
        {
            var prefab = definition.character;
            if (prefab == null) continue;
            text.AppendLine($"== {definition.name}: {prefab.name}");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var skinned = renderer.TryCast<SkinnedMeshRenderer>();
                var mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                string line = $"  {(skinned != null ? "skinned" : "mesh")} '{renderer.name}' active {renderer.gameObject.activeSelf}/{renderer.enabled}";
                if (mesh != null)
                {
                    line += $", mesh '{mesh.name}' readable {mesh.isReadable}, {mesh.vertexCount} vertices, {mesh.subMeshCount} sub-meshes";
                    if (skinned != null) line += $", {skinned.bones?.Length ?? 0} bones, root {skinned.rootBone?.name}";
                    line += "; as it is: " + Probe(mesh);
                    if (skinned != null)
                    {
                        try
                        {
                            var baked = new Mesh();
                            skinned.BakeMesh(baked);
                            line += "; baked: " + Probe(baked);
                            Object.Destroy(baked);
                        }
                        catch (Exception e) { line += $"; bake failed: {e.GetType().Name} {e.Message}"; }
                    }
                }
                text.AppendLine(line);
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    var shader = material.shader;
                    var textures = new List<string>();
                    try
                    {
                        for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
                        {
                            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                            string property = shader.GetPropertyName(i);
                            var texture = material.GetTexture(property);
                            if (texture == null) continue;
                            var t2 = texture.TryCast<Texture2D>();
                            textures.Add($"{property}={texture.name} {texture.width}x{texture.height}{(t2 != null ? $" {t2.format} readable {t2.isReadable}" : "")}");
                            if (!textureTried && t2 != null && property.Contains("Base", StringComparison.OrdinalIgnoreCase))
                            {
                                textureTried = true;
                                text.AppendLine("    texture copy: " + CopyTexture(t2));
                            }
                        }
                    }
                    catch (Exception e) { textures.Add($"properties failed: {e.Message}"); }
                    text.AppendLine($"    material '{material.name}' shader '{shader?.name}': {string.Join("; ", textures)}");
                    text.AppendLine("      values: " + Values(material));
                    for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
                    {
                        if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                        var t2 = material.GetTexture(shader.GetPropertyName(i))?.TryCast<Texture2D>();
                        if (t2 == null || !s_channelsDone.Add(t2.name) || t2.name.EndsWith("_dm", StringComparison.OrdinalIgnoreCase)) continue;
                        text.AppendLine($"      channels of {t2.name} ({shader.GetPropertyName(i)}, {t2.format}): {Channels(t2)}");
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "bodies.txt"), text.ToString());
        Log($"Bodies: {text.Length} characters written to shots/bodies.txt");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static readonly HashSet<string> s_channelsDone = new(StringComparer.Ordinal);

    // Every colour, number and vector of a material, by its shader's property names.
    private static string Values(Material material)
    {
        var shader = material.shader;
        var parts = new List<string>();
        for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
        {
            string name = shader.GetPropertyName(i);
            try
            {
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        var c = material.GetColor(name);
                        parts.Add($"{name}=({c.r:0.###} {c.g:0.###} {c.b:0.###} {c.a:0.###})");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        parts.Add($"{name}={material.GetFloat(name):0.###}");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        var v = material.GetVector(name);
                        parts.Add($"{name}=({v.x:0.###} {v.y:0.###} {v.z:0.###} {v.w:0.###})");
                        break;
                }
            }
            catch (Exception e) { parts.Add($"{name} failed ({e.GetType().Name})"); }
        }
        return string.Join("; ", parts);
    }

    // A texture's channels as stored (read linear, no colour conversion): least, mean and most of each, at 256 a side.
    private static string Channels(Texture2D source)
    {
        try
        {
            const int size = 256;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels32();
            Object.Destroy(copy);
            string Stat(Func<Color32, byte> channel)
            {
                int least = 255, most = 0;
                long sum = 0;
                foreach (var p in pixels)
                {
                    int v = channel(p);
                    least = Math.Min(least, v);
                    most = Math.Max(most, v);
                    sum += v;
                }
                return $"{least}/{sum / pixels.Length}/{most}";
            }
            return $"R {Stat(p => p.r)}, G {Stat(p => p.g)}, B {Stat(p => p.b)}, A {Stat(p => p.a)} (least/mean/most of 255)";
        }
        catch (Exception e) { return $"failed: {e.GetType().Name} {e.Message}"; }
    }

    private static string Probe(Mesh mesh)
    {
        var parts = new List<string>();
        void Try(string name, Func<int> count)
        {
            try { parts.Add($"{name} {count()}"); }
            catch (Exception e) { parts.Add($"{name} failed ({e.GetType().Name})"); }
        }
        Try("vertices", () => mesh.vertices.Length);
        Try("normals", () => mesh.normals.Length);
        Try("uv", () => mesh.uv.Length);
        Try("triangles[0]", () => mesh.GetTriangles(0).Length);
        Try("weights", () => mesh.boneWeights.Length);
        Try("bindposes", () => mesh.bindposes.Length);
        return string.Join(", ", parts);
    }

    // A texture the GPU holds, copied through a render texture into a readable one and encoded as PNG.
    private static string CopyTexture(Texture2D source)
    {
        try
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            var read = typeof(Texture2D).GetMethod("ReadPixels", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Rect), typeof(int), typeof(int) }, null)
                       ?? typeof(Texture2D).GetMethod("ReadPixelsImpl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (read == null) return "no ReadPixels";
            object[] args = read.GetParameters().Length == 3
                ? new object[] { new Rect(0, 0, source.width, source.height), 0, 0 }
                : new object[] { new Rect(0, 0, source.width, source.height), 0, 0, false };
            read.Invoke(copy, args);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels32();
            var middle = pixels[pixels.Length / 2];
            byte[] png = ImageConversion.EncodeToPNG(copy);
            string path = Path.Combine(ShotsDir, $"bodies-{source.name}.png");
            File.WriteAllBytes(path, png);
            Object.Destroy(copy);
            return $"{source.name} through {read.Name}: {pixels.Length} pixels, middle {middle.r},{middle.g},{middle.b},{middle.a}, PNG {png.Length} bytes";
        }
        catch (Exception e) { return $"failed: {e.GetType().Name} {e.Message}"; }
    }
}
