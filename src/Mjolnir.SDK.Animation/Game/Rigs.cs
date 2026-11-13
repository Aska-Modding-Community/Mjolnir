using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SSSGame;
using UnityEngine;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The characters' skeletons for the Mjolnir kit: one per
/// body of the game's character definitions (the playable Ragnar and Aska, the villagers), every bone of its own skeleton under
/// the avatar's root (<c>master</c>, <c>Bip001 …</c>, <c>Bone …</c>; not the copies its equipment carries, nor colliders and
/// slots) as the body's prefab defines it. Written to <c>Mjolnir/ModKit/rigs/&lt;rig&gt;.rig</c> once the definitions are loaded,
/// which the kit's Import Rigs reads. Only names and poses: no mesh or texture of the game leaves it.
/// </summary>
internal static class Rigs
{
    private const int MinimumBones = 20;
    private const float SamePosition = 0.00001f; // 0.01 mm
    private const float SameAngle = 0.01f;       // degrees

    /// <summary>A skeleton written for the kit: its rig's name and its bones (path from the avatar's root, local pose).</summary>
    public sealed record Skeleton(string Name, IReadOnlyList<(string Path, Vector3 Position, Quaternion Rotation)> Bones);

    /// <summary>The skeletons written, once they are (the game's clips are read on them).</summary>
    public static IReadOnlyList<Skeleton> Skeletons { get; private set; } = [];

    // A body's name → the rig it is written as: itself, or the body with the same skeleton (a golem's on a man's).
    private static readonly Dictionary<string, string> s_rigOf = new(StringComparer.Ordinal);
    private static bool s_written;

    /// <summary>
    /// The rig of a character's skeleton: its body's name (<c>ragnar</c>, <c>aska</c>, <c>peasant_male</c>,
    /// <c>peasant_female</c>), or the body with the same skeleton.
    /// </summary>
    public static string NameOf(Animator animator)
    {
        ExportAll(); // names a golem's body after the villager's skeleton it shares
        string body = BodyOf(Root(animator));
        return s_rigOf.TryGetValue(body, out string? rig) ? rig : body;
    }

    /// <summary>
    /// Writes every body's skeleton once the game's character definitions are loaded: true once done (or failed), false while
    /// there are none yet. Each file is written only when it changed; logged; a fault is logged too.
    /// </summary>
    public static bool ExportAll()
    {
        if (s_written) return true;
        if (PlatformPaths.Loader is not { } loader) return s_written = true;
        try
        {
            var bodies = new List<Body>();
            foreach (var definition in Resources.FindObjectsOfTypeAll<CharacterDefinition>())
            {
                var animator = definition.character != null ? definition.character.GetComponentInChildren<Animator>(true) : null;
                if (animator == null || animator.avatar == null) continue;
                // A path twice (Aska carries two `Bone side_weapon` under the pelvis): a clip moves the first of them, as Unity
                // finds a child by its name, so the skeleton keeps the first.
                var all = Bones(animator).ToList();
                var bones = all.DistinctBy(b => b.Path, StringComparer.Ordinal).ToList();
                if (bones.Count < MinimumBones) continue; // only the people's Biped skeleton
                string name = BodyOf(Root(animator));
                if (all.Count > bones.Count)
                    SdkLog.Info(GameAnimations.Source, $"Animations: the body {name} has {all.Count - bones.Count} bone(s) on a path taken already ({string.Join(", ", all.GroupBy(b => b.Path).Where(g => g.Count() > 1).Select(g => g.Key.Split('/').Last()))}): the first one is the skeleton's");
                if (bodies.FirstOrDefault(b => b.Name == name) is { } known)
                {
                    known.Definitions.Add(Describe(definition));
                    continue;
                }
                bodies.Add(new Body(name, animator, bones.Select(b => (b.Path, b.Bone.localPosition, b.Bone.localRotation)).ToList(),
                                    [Describe(definition)], definition.character!));
            }
            if (bodies.Count == 0) return false;

            // A body whose skeleton another body already has (every bone of it there, in the same pose) is written as that one:
            // the fuller skeleton and the plainer name first.
            var rigs = new List<Body>();
            foreach (var body in bodies.OrderByDescending(b => b.Bones.Count).ThenBy(b => b.Name.Length).ThenBy(b => b.Name, StringComparer.Ordinal))
            {
                var same = rigs.FirstOrDefault(r => SameSkeleton(body, r));
                s_rigOf[body.Name] = same?.Name ?? body.Name;
                if (same != null) same.Definitions.AddRange(body.Definitions.Select(d => $"{d} (body {body.Name})"));
                else rigs.Add(body);
            }

            KitExport.Prepare();
            string folder = KitExport.Rigs!;
            Directory.CreateDirectory(folder);
            foreach (var rig in rigs)
            {
                string text = Write(rig);
                string path = Path.Combine(folder, rig.Name + ".rig");
                if (File.Exists(path) && File.ReadAllText(path) == text) continue;
                File.WriteAllText(path, text);
                SdkLog.Info(GameAnimations.Source, $"Animations: the skeleton {rig.Name} written to {path} for the Mjolnir kit");
            }
            Skeletons = rigs.Select(r => new Skeleton(r.Name, r.Bones)).ToList();
            SdkLog.Info(GameAnimations.Source,
                $"Animations: the skeletons for the Mjolnir kit: {string.Join("; ", rigs.Select(r => $"{r.Name}, {r.Bones.Count} bones, of {string.Join(", ", r.Definitions)}"))}");
            if (BodyModels.Enabled)
                foreach (var rig in rigs)
                {
                    try
                    {
                        BodyModels.Write(rig.Name, rig.Animator, rig.Prefab, KitExport.Models!);
                    }
                    catch (Exception e)
                    {
                        SdkLog.Error(GameAnimations.Source, $"Animations: writing the model {rig.Name} failed: {e}");
                    }
                }
        }
        catch (Exception e)
        {
            SdkLog.Error(GameAnimations.Source, $"Animations: writing the skeletons failed: {e}");
        }
        return s_written = true;
    }

    /// <summary>The character's own bones under the avatar's root, by their path from it: what clips and masks name.</summary>
    public static IEnumerable<(string Path, Transform Bone)> Bones(Animator animator)
    {
        var root = Root(animator);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            string path = KitFiles.PathOf(root, t);
            if (IsBone(path)) yield return (path, t);
        }
    }

    /// <summary>A bone of the character's own skeleton by its name, null for none.</summary>
    public static Transform? Bone(Animator animator, string name) =>
        Bones(animator).Where(b => b.Bone.name == name).Select(b => b.Bone).FirstOrDefault();

    private sealed record Body(string Name, Animator Animator, List<(string Path, Vector3 Position, Quaternion Rotation)> Bones, List<string> Definitions, GameObject Prefab);

    /// <summary>Writes the skeletons (and the models, when they are asked for) again at the next look.</summary>
    public static void ExportAgain() => s_written = false;

    private static string Describe(CharacterDefinition definition) =>
        $"{definition.name}{(definition.female ? " (female)" : "")}";

    private static bool SameSkeleton(Body body, Body rig)
    {
        var bones = rig.Bones.ToDictionary(b => b.Path, StringComparer.Ordinal);
        return body.Bones.All(b => bones.TryGetValue(b.Path, out var r)
                                   && Vector3.Distance(b.Position, r.Position) <= SamePosition
                                   && Quaternion.Angle(b.Rotation, r.Rotation) <= SameAngle);
    }

    private static string Write(Body rig)
    {
        var animator = rig.Animator;
        var root = Root(animator);
        string rootMotion = "";
        try
        {
            rootMotion = animator.avatar.humanDescription.m_RootMotionBoneName ?? "";
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameAnimations.Source, $"Animations: the skeleton {rig.Name}'s root motion bone could not be read from its avatar ({e.Message})");
        }

        // How far the meshes' bind pose stands from the prefab (read only against a parent bound in the same mesh: two meshes'
        // spaces differ), for the log: the prefab is what a character is made from and what its animator starts from.
        var meshes = new List<Dictionary<IntPtr, Matrix4x4>>();
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.rootBone == null || !IsBone(KitFiles.PathOf(root, renderer.rootBone))) continue; // equipment has its own skeleton
            var bones = renderer.bones;
            var poses = renderer.sharedMesh?.bindposes;
            var bind = new Dictionary<IntPtr, Matrix4x4>();
            for (int i = 0; bones != null && poses != null && i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null) bind.TryAdd(bones[i].Pointer, poses[i].inverse);
            meshes.Add(bind);
        }
        float bindAngle = 0, bindDistance = 0;
        foreach (var (path, bone) in Bones(animator))
        {
            if (bone.parent == null || meshes.FirstOrDefault(m => m.ContainsKey(bone.Pointer) && m.ContainsKey(bone.parent.Pointer)) is not { } mesh) continue;
            var m = mesh[bone.parent.Pointer].inverse * mesh[bone.Pointer];
            bindDistance = Math.Max(bindDistance, Vector3.Distance(m.GetColumn(3), bone.localPosition));
            bindAngle = Math.Max(bindAngle, Quaternion.Angle(Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1)), bone.localRotation));
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"# Mjolnir rig {rig.Name} (avatar {animator.avatar?.name}, root motion from '{rootMotion}'; characters: {string.Join(", ", rig.Definitions)}): the game's skeleton, written by Mjolnir.SDK.Animation for the Mjolnir kit\n");
        text.Append("# path from the avatar's root | local position | local rotation (x y z w) | where the pose comes from: asset (the character's prefab)\n");
        foreach (var (path, position, rotation) in rig.Bones)
            text.Append(CultureInfo.InvariantCulture,
                $"{path}|{position.x:R} {position.y:R} {position.z:R}|{rotation.x:R} {rotation.y:R} {rotation.z:R} {rotation.w:R}|asset\n");
        SdkLog.Info(GameAnimations.Source, string.Create(CultureInfo.InvariantCulture,
            $"Animations: the skeleton {rig.Name}: {rig.Bones.Count} bones from its prefab; the meshes' bind pose differs from it by up to {bindAngle:0.###}° and {bindDistance * 1000:0.##} mm; root motion from '{rootMotion}'"));
        return text.ToString();
    }

    // The body's name: its model's (the avatar's root), without the copy's and the duplicate's marks: `aska (1)` → `aska`.
    private static string BodyOf(Transform root) =>
        Regex.Replace(root.name.Replace("(Clone)", "", StringComparison.Ordinal).Trim(), @" \(\d+\)$", "");

    // The root clips' paths start from: the avatar's (the model's root, `ragnar` on the player), else the animator's object.
    private static Transform Root(Animator animator) => animator.avatarRoot ?? animator.transform;

    // A bone of the character's own skeleton: master and, under it, only bones named Bip001… or Bone… on the way (not an
    // equipment's copy of the skeleton, under its own object, nor colliders, slots and effects).
    private static bool IsBone(string path)
    {
        var parts = path.Split('/');
        return parts[0] == "master" && parts.Skip(1).All(p => p.StartsWith("Bip001", StringComparison.Ordinal) || p.StartsWith("Bone ", StringComparison.Ordinal));
    }
}
