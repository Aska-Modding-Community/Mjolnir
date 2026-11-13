using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mjolnir.SDK.Config;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The characters' models for the Mjolnir kit, beside their skeletons: when the
/// platform's setting <c>[Kit] Models</c> is on, every body's skinned meshes go to <c>Mjolnir/ModKit/models/&lt;rig&gt;/</c>
/// (<c>model.txt</c>, a <c>.mesh</c> per part, a PNG per colour, normal and mask map at the game's own size), which the kit's Import Rigs
/// puts on its skeleton for previews. They stay on the player's computer: the kit keeps them out of git and out of mods' bundles.
/// What goes: the parts on the body's own skeleton (not the raven's form, which has its own), each with its bones, bind poses,
/// vertices, normals, tangents, UVs and sub-meshes as the game reads them (only readable meshes: a golem's body is not), each
/// material's colour, normal and mask maps (the game's shaders are its own, so the kit draws them with HDRP/Lit: a normal map
/// written as an ordinary one, X, Y and Z in red, green and blue, from the game's X in alpha and Y in green; a mask as it is,
/// metallic, occlusion, detail and smoothness in red, green, blue and alpha as HDRP's), and which parts the game's appearance
/// shows by default (hair style 0 and facial hair 0; the other styles written too, switched off).
/// </summary>
internal static class BodyModels
{
    /// <summary>The platform's setting, bound by the module's start.</summary>
    public static ConfigEntry<bool>? Setting;

    public static bool Enabled => Setting?.Value == true;

    private const int MaxTexture = 4096; // the game's people have 2048 at most

    // A material's maps, the first of these its shader has: HDRP/Lit, the game's workers and props, its skin, its hair cards
    // (colour and alpha in one); else the texture named as the colour map with the map's ending (…_nm, …_mm), as a shader graph
    // names its properties by hashes (the starting cloth's HDRPLitWet).
    private static readonly string[] ColorProperties = { "_BaseColorMap", "_BaseDiffuse", "_DiffuseMap", "_Alpha_Depth", "_MainTex", "_BaseMap" };
    private static readonly string[] NormalProperties = { "_NormalMap", "_BaseNormal", "_Normal_Map", "_Normal" };
    private static readonly string[] MaskProperties = { "_MaskMap", "_BaseMask", "_Masks" };

    private enum MapKind { Color, Normal, Mask }

    /// <summary>Writes one body's model when it changed; true when it wrote it. Main thread, in a running game.</summary>
    public static bool Write(string rig, Animator animator, GameObject prefab, string modelsFolder)
    {
        var root = animator.avatarRoot ?? animator.transform;
        var groups = Groups(prefab);
        var manifest = new StringBuilder();
        manifest.Append(CultureInfo.InvariantCulture, $"# Mjolnir model {rig} ({prefab.name}): the game's character, written by Mjolnir.SDK.Animation for the Mjolnir kit; only for this computer\n");
        manifest.Append("# part|index|name|mesh file|path from the avatar's root|position|rotation (x y z w)|scale|group|shown by default|root bone\n");
        manifest.Append("# bones|index|bone paths separated by ;\n# material|index|slot|name|shader|colour map file|cut out by alpha|normal map file|mask map file|the shader's numbers for the mask\n");
        var meshes = new List<(string File, Mesh Mesh)>();
        // A map by the texture and what it is (one texture may be two maps, two textures may share a name), each in a file of its own.
        var textures = new Dictionary<(IntPtr Texture, MapKind Kind), (string File, Texture2D Texture)>();
        var textureFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        int index = 0;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            var bones = renderer.bones;
            if (mesh == null || bones == null || bones.Length == 0) continue;
            string[] paths = bones.Select(b => b == null ? "" : KitFiles.PathOf(root, b)).ToArray();
            if (!paths.Any(p => p.StartsWith("master/", StringComparison.Ordinal))) continue; // another skeleton's (the raven's form)
            if (!mesh.isReadable)
            {
                skipped.Add(renderer.name);
                continue;
            }
            string file = $"{index:00}-{KitFiles.Safe(renderer.name)}.mesh";
            meshes.Add((file, mesh));
            var (group, shown) = groups.TryGetValue(renderer.Pointer, out var g) ? g : ("base", renderer.gameObject.activeSelf);
            var t = renderer.transform;
            var position = root.InverseTransformPoint(t.position);
            var rotation = Quaternion.Inverse(root.rotation) * t.rotation;
            var scale = t.lossyScale;
            manifest.Append(CultureInfo.InvariantCulture,
                $"part|{index}|{renderer.name}|{file}|{KitFiles.PathOf(root, t)}|{position.x:R} {position.y:R} {position.z:R}|{rotation.x:R} {rotation.y:R} {rotation.z:R} {rotation.w:R}|{scale.x:R} {scale.y:R} {scale.z:R}|{group}|{(shown ? 1 : 0)}|{(renderer.rootBone != null ? KitFiles.PathOf(root, renderer.rootBone) : "")}\n");
            manifest.Append(CultureInfo.InvariantCulture, $"bones|{index}|{string.Join(";", paths)}\n");
            var materials = renderer.sharedMaterials;
            for (int slot = 0; materials != null && slot < materials.Length; slot++)
            {
                var material = materials[slot];
                if (material == null) continue;
                string shader = material.shader != null ? material.shader.name : "";
                var (texture, property) = Map(material, ColorProperties, "_dm", null);
                var (normal, _) = Map(material, NormalProperties, "_nm", texture);
                var (mask, _) = Map(material, MaskProperties, "_mm", texture);
                string MapFile(Texture2D? map, MapKind kind)
                {
                    if (map == null) return "";
                    if (textures.TryGetValue((map.Pointer, kind), out var known)) return known.File;
                    string name = KitFiles.Safe(map.name);
                    string file = name + ".png";
                    for (int n = 2; !textureFiles.Add(file); n++) file = $"{name}_{n}.png";
                    textures[(map.Pointer, kind)] = (file, map);
                    return file;
                }
                bool cutout = property == "_Alpha_Depth" || shader.Contains("Hair", StringComparison.OrdinalIgnoreCase)
                              || (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f);
                manifest.Append(CultureInfo.InvariantCulture,
                    $"material|{index}|{slot}|{material.name}|{shader}|{MapFile(texture, MapKind.Color)}|{(cutout ? 1 : 0)}|{MapFile(normal, MapKind.Normal)}|{MapFile(mask, MapKind.Mask)}|{Numbers(material)}\n");
            }
            index++;
        }
        foreach (var ((_, kind), (file, texture)) in textures)
            manifest.Append(CultureInfo.InvariantCulture, $"# texture {file}: {kind}, {texture.width}x{texture.height} {texture.format}\n");

        string folder = Path.Combine(modelsFolder, rig);
        string manifestPath = Path.Combine(folder, "model.txt");
        string text = manifest.ToString();
        if (File.Exists(manifestPath) && File.ReadAllText(manifestPath) == text) return false;
        Directory.CreateDirectory(folder);
        foreach (string old in Directory.GetFiles(folder)) File.Delete(old);
        long bytes = 0;
        foreach (var (file, mesh) in meshes)
        {
            byte[] data = MeshBytes(mesh);
            File.WriteAllBytes(Path.Combine(folder, file), data);
            bytes += data.Length;
        }
        foreach (var ((_, kind), (file, texture)) in textures)
        {
            byte[]? png = Png(texture, kind);
            if (png == null) continue;
            File.WriteAllBytes(Path.Combine(folder, file), png);
            bytes += png.Length;
        }
        File.WriteAllText(manifestPath, text); // last: the kit reads a folder whose model.txt is there
        string left = skipped.Count > 0 ? $"; left out, not readable: {string.Join(", ", skipped)}" : "";
        SdkLog.Info(GameAnimations.Source, string.Create(CultureInfo.InvariantCulture,
            $"Animations: the model {rig} written to {folder} for the Mjolnir kit: {meshes.Count} parts, {textures.Count} textures, {bytes / 1048576.0:0.0} MB{left}"));
        return true;
    }

    // The parts the game's appearance switches: hair styles and facial hair, each style's renderers; the first one shown.
    private static Dictionary<IntPtr, (string Group, bool Shown)> Groups(GameObject prefab)
    {
        var groups = new Dictionary<IntPtr, (string, bool)>();
        var appearance = prefab.GetComponentInChildren<CharacterAppearance>(true);
        if (appearance == null) return groups;
        void Mark(Il2CppSystem.Collections.Generic.List<CharacterAppearance.HairConfiguration>? styles, string kind)
        {
            for (int i = 0; styles != null && i < styles.Count; i++)
            {
                var renderers = styles[i].renderers;
                for (int r = 0; renderers != null && r < renderers.Length; r++)
                    if (renderers[r] != null) groups.TryAdd(renderers[r].Pointer, ($"{kind}:{i}", i == 0));
            }
        }
        Mark(appearance.hairStyles, "hair");
        Mark(appearance.facialHairFeatures, "facial");
        return groups;
    }

    // A map: by the shader's property, else (a shader graph's properties are named by hashes: the starting cloth's HDRPLitWet)
    // the texture the game names with the map's ending ("…_dm", "…_nm", "…_mm"), a normal or mask map only beside the colour
    // map of the same name (a skin's detail normal map is not the skin's).
    private static (Texture2D? Texture, string Property) Map(Material material, string[] properties, string ending, Texture2D? color)
    {
        foreach (string property in properties)
        {
            if (!material.HasProperty(property)) continue;
            var texture = material.GetTexture(property)?.TryCast<Texture2D>();
            if (texture != null) return (texture, property);
        }
        string? stem = color == null ? null : StemOf(color.name);
        if (color != null && stem == null) return (null, "");
        var shader = material.shader;
        for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            string property = shader.GetPropertyName(i);
            var texture = material.GetTexture(property)?.TryCast<Texture2D>();
            if (texture == null || !texture.name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) continue;
            if (stem == null || string.Equals(StemOf(texture.name), stem, StringComparison.OrdinalIgnoreCase)) return (texture, property);
        }
        return (null, "");
    }

    // What the game's shaders do with a mask, by their own properties, for the
    // kit's HDRP/Lit: the smoothness range (hair's _Min_/_Max_Smoothness; skin's _Smoothness, the mask's alpha scaled to it),
    // the occlusion's strength (_AO, hair's _Ambient_Occlusion) and the metal (_Metallic, _Metal). "name=value" pairs by ";".
    private static string Numbers(Material material)
    {
        var parts = new List<string>();
        void Add(string key, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (!material.HasProperty(property)) continue;
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{key}={material.GetFloat(property):0.###}"));
                return;
            }
        }
        Add("smoothnessMin", "_Min_Smoothness");
        Add("smoothnessMax", "_Max_Smoothness", "_Smoothness");
        Add("occlusion", "_AO", "_Ambient_Occlusion");
        Add("metallic", "_Metallic", "_Metal");
        return string.Join(";", parts);
    }

    // A map's name without its ending: "ragnar_body_dm" → "ragnar_body"; null without one.
    private static string? StemOf(string name) =>
        name.Length > 3 && name[^3] == '_' ? name[..^3] : null;

    // The game's textures live on the GPU only: drawn into a render texture at their own size (at most MaxTexture), read back
    // and encoded as PNG. A colour map through sRGB, as it is drawn; a normal or a mask map linear, its numbers as they are,
    // a normal map rewritten from the game's packing (X in alpha, Y in green; red is 1) into an ordinary one.
    private static byte[]? Png(Texture2D source, MapKind kind)
    {
        int width = source.width, height = source.height;
        while (Math.Max(width, height) > MaxTexture)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        RenderTexture? target = null;
        var previous = RenderTexture.active;
        try
        {
            bool linear = kind != MapKind.Color;
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply();
            if (kind == MapKind.Normal) Unpack(copy);
            byte[] png = ImageConversion.EncodeToPNG(copy);
            Object.Destroy(copy);
            return png;
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameAnimations.Source, $"Animations: the texture {source.name} could not be read for the kit: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }

    // A normal map packed as the game packs it (red 1 everywhere, X in alpha, Y in green) rewritten as an ordinary one: X, Y and
    // the Z they make in red, green and blue, alpha 1. A map packed otherwise is left as it is.
    private static void Unpack(Texture2D map)
    {
        var pixels = map.GetPixels32();
        long red = 0;
        foreach (var p in pixels) red += p.r;
        if (red < pixels.Length * 250L) return;
        for (int i = 0; i < pixels.Length; i++)
        {
            float x = pixels[i].a / 255f * 2 - 1, y = pixels[i].g / 255f * 2 - 1;
            float z = MathF.Sqrt(Math.Max(0, 1 - x * x - y * y));
            pixels[i] = new Color32(pixels[i].a, pixels[i].g, (byte)Math.Round((z * 0.5f + 0.5f) * 255), 255);
        }
        map.SetPixels32(pixels);
        map.Apply();
    }

    // A mesh as the kit reads it: "MJM2", then vertices, normals, UVs, bone weights, bind poses, each sub-mesh's triangles and
    // the tangents, each with its count first (MJM1 had no tangents: the kit made its own, which a normal map does not match).
    private static byte[] MeshBytes(Mesh mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("MJM2"));
        var vertices = mesh.vertices;
        writer.Write(vertices.Length);
        foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
        var normals = mesh.normals;
        writer.Write(normals.Length);
        foreach (var n in normals) { writer.Write(n.x); writer.Write(n.y); writer.Write(n.z); }
        var uv = mesh.uv;
        writer.Write(uv.Length);
        foreach (var u in uv) { writer.Write(u.x); writer.Write(u.y); }
        var weights = mesh.boneWeights;
        writer.Write(weights.Length);
        foreach (var w in weights)
        {
            writer.Write(w.boneIndex0); writer.Write(w.weight0);
            writer.Write(w.boneIndex1); writer.Write(w.weight1);
            writer.Write(w.boneIndex2); writer.Write(w.weight2);
            writer.Write(w.boneIndex3); writer.Write(w.weight3);
        }
        var bindposes = mesh.bindposes;
        writer.Write(bindposes.Length);
        foreach (var m in bindposes)
            for (int i = 0; i < 16; i++) writer.Write(m[i]);
        writer.Write(mesh.subMeshCount);
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var triangles = mesh.GetTriangles(s);
            writer.Write(triangles.Length);
            foreach (int t in triangles) writer.Write(t);
        }
        var tangents = mesh.tangents;
        writer.Write(tangents.Length);
        foreach (var t in tangents) { writer.Write(t.x); writer.Write(t.y); writer.Write(t.z); writer.Write(t.w); }
        writer.Flush();
        return stream.ToArray();
    }

}
