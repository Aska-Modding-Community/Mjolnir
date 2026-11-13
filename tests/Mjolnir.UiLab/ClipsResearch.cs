using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SSSGame;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "clips" scenario (autopilot.once containing "clips"): how the game's own clips can be read back, for the Mjolnir kit. In
/// the main menu: every loaded clip by the start of its name, with its length, rate, looping and root curves; then a bare
/// copy of a body's skeleton (its bones only, no mesh, no controller) with a fresh Animator, driven by a playable graph
/// evaluated by hand, with every bone first set to a mark: which bones and properties a clip writes, what it writes on
/// `master`, and where root motion goes (the skeleton's root with and without applyRootMotion). Into shots/clips.txt. Then,
/// with the platform's [Kit] Animations on, it waits for Mjolnir.SDK.Animation to write the game's clips for the kit.
/// Nothing of the game is changed.
/// </summary>
internal static partial class Autopilot
{
    private static readonly Vector3 MarkPosition = new(1234f, 1234f, 1234f);
    private static readonly Quaternion MarkRotation = new(0.1f, 0.2f, 0.3f, 0.927362f);
    private static readonly Vector3 MarkScale = new(7f, 7f, 7f);

    private static IEnumerator<float> ClipsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var text = new StringBuilder();
        var clips = Resources.FindObjectsOfTypeAll<AnimationClip>().ToList();
        text.AppendLine($"clips loaded: {clips.Count}");
        foreach (var group in clips.GroupBy(c => Prefix(c.name)).OrderByDescending(g => g.Count()))
            text.AppendLine($"  {group.Key}: {group.Count()} (looping {group.Count(c => c.isLooping)}, root curves {group.Count(c => c.hasRootCurves)}, motion curves {group.Count(c => c.hasMotionCurves)}, generic root {group.Count(c => c.hasGenericRootTransform)}, root motion {group.Count(c => c.hasRootMotion)}, total {group.Sum(c => c.length):0} s, rates {string.Join("/", group.Select(c => c.frameRate).Distinct().OrderBy(r => r))})");
        var names = clips.Select(c => c.name).ToList();
        text.AppendLine($"  distinct names {names.Distinct().Count()} of {names.Count}; duplicated: {string.Join(", ", names.GroupBy(n => n).Where(g => g.Count() > 1).Take(20).Select(g => $"{g.Key} x{g.Count()}"))}");
        text.AppendLine("  all names: " + string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal)));

        var aska = Resources.FindObjectsOfTypeAll<CharacterDefinition>().FirstOrDefault(d => d.name == "Aska_CharDef");
        var animatorOfPrefab = aska?.character != null ? aska.character.GetComponentInChildren<Animator>(true) : null;
        if (animatorOfPrefab == null)
        {
            text.AppendLine("no Aska prefab");
        }
        else
        {
            var modelRoot = animatorOfPrefab.avatarRoot ?? animatorOfPrefab.transform;
            foreach (bool rootMotion in new[] { false, true })
            {
                var (root, bones) = BareSkeleton(modelRoot);
                var animator = root.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = rootMotion;
                text.AppendLine($"== bare skeleton {root.name}: {bones.Count} bones, applyRootMotion {rootMotion}, avatar {(animator.avatar == null ? "none" : animator.avatar.name)}");
                foreach (string name in new[] { "aska_dodge_front", "aska_wave", "peasant_male_walk", "aska_idle_cheer", "ragnar_sit_down" })
                {
                    var clip = clips.FirstOrDefault(c => c.name == name);
                    if (clip == null) { text.AppendLine($"  {name}: not loaded"); continue; }
                    try { text.Append(Probe(root, bones, animator, clip)); }
                    catch (Exception e) { text.AppendLine($"  {name}: failed {e.GetType().Name} {e.Message}"); }
                }
                Object.Destroy(root.gameObject);
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "clips.txt"), text.ToString());
        Log($"Clips: {text.Length} characters written to shots/clips.txt");
        // With the platform's [Kit] Animations on, Mjolnir.SDK.Animation writes the game's clips in the main menu over some
        // seconds: wait for its list.
        string list = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ShotsDir)!)!)!, "ModKit", "clips", "clips.txt");
        var since = DateTime.Now.AddMinutes(-5);
        foreach (float wait in WaitFor("the game's clips for the kit", () => File.Exists(list) && File.GetLastWriteTime(list) > since, 240)) yield return wait;
        Log($"Clips: the kit's list {(File.Exists(list) ? $"written at {File.GetLastWriteTime(list):HH:mm:ss}, {File.ReadAllLines(list).Count(l => !l.StartsWith('#'))} clips" : "not there")}");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Prefix(string name)
    {
        foreach (string p in new[] { "peasant_male_", "peasant_female_", "ragnar_", "aska_" })
            if (name.StartsWith(p, StringComparison.Ordinal)) return p;
        int underscore = name.IndexOf('_');
        return underscore > 0 ? name[..(underscore + 1)] : name;
    }

    // The bones of a model (Transforms named master, Bip001…, Bone…, under its root), copied as plain objects far below the world.
    private static (Transform Root, Dictionary<string, Transform> Bones) BareSkeleton(Transform modelRoot)
    {
        var root = new GameObject(modelRoot.name).transform;
        root.position = new Vector3(0, -500, 0);
        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        void Copy(Transform from, Transform to, string path)
        {
            for (int i = 0; i < from.childCount; i++)
            {
                var child = from.GetChild(i);
                if (!(child.name == "master" || child.name.StartsWith("Bip001", StringComparison.Ordinal) || child.name.StartsWith("Bone ", StringComparison.Ordinal))) continue;
                string childPath = path.Length == 0 ? child.name : path + "/" + child.name;
                if (path.Length == 0 && child.name != "master") continue;
                if (bones.ContainsKey(childPath)) continue;
                var copy = new GameObject(child.name).transform;
                copy.SetParent(to, false);
                copy.localPosition = child.localPosition;
                copy.localRotation = child.localRotation;
                copy.localScale = child.localScale;
                bones[childPath] = copy;
                Copy(child, copy, childPath);
            }
        }
        Copy(modelRoot, root, "");
        return (root, bones);
    }

    private static string Probe(Transform root, Dictionary<string, Transform> bones, Animator animator, AnimationClip clip)
    {
        var text = new StringBuilder();
        var watch = Stopwatch.StartNew();
        var graph = PlayableGraph.Create("UiLab clips");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, clip);
        var handle = playable.GetHandle();
        var output = AnimationPlayableOutput.Create(graph, "UiLab", animator).GetHandle();
        output.SetSourcePlayable(handle, 0);
        graph.Play();

        root.localPosition = new Vector3(0, -500, 0);
        root.localRotation = Quaternion.identity;
        foreach (var bone in bones.Values)
        {
            bone.localPosition = MarkPosition;
            bone.localRotation = MarkRotation;
            bone.localScale = MarkScale;
        }
        handle.SetTime(clip.length * 0.5);
        graph.Evaluate(0f);
        int positions = 0, rotations = 0, scales = 0;
        var untouched = new List<string>();
        foreach (var (path, bone) in bones)
        {
            bool p = (bone.localPosition - MarkPosition).sqrMagnitude > 1e-6f;
            bool r = Quaternion.Angle(bone.localRotation, MarkRotation) > 0.001f;
            bool s = (bone.localScale - MarkScale).sqrMagnitude > 1e-6f;
            positions += p ? 1 : 0;
            rotations += r ? 1 : 0;
            scales += s ? 1 : 0;
            if (!p && !r && !s) untouched.Add(path.Split('/').Last());
        }
        text.AppendLine($"  {clip.name} ({clip.length:0.00} s, {clip.frameRate} fps, loop {clip.isLooping}, root curves {clip.hasRootCurves}, motion {clip.hasMotionCurves}, generic root {clip.hasGenericRootTransform}): at the middle {positions} positions, {rotations} rotations, {scales} scales written; untouched {untouched.Count}: {string.Join(", ", untouched.Take(12))}");
        var master = bones["master"];
        var bip = bones.TryGetValue("master/Bip001", out var b) ? b : null;
        text.AppendLine($"    master pos {Fmt(master.localPosition)} rot {master.localRotation.eulerAngles} scale {Fmt(master.localScale)}; Bip001 pos {(bip != null ? Fmt(bip.localPosition) : "-")}; root {Fmt(root.localPosition)}");

        // Sampled through the clip: master, Bip001 and the root, from the start by Evaluate(step).
        root.localPosition = new Vector3(0, -500, 0);
        root.localRotation = Quaternion.identity;
        handle.SetTime(0);
        graph.Evaluate(0f);
        var line = new StringBuilder("    by steps:");
        float step = 1f / 30f;
        int frames = Mathf.CeilToInt(clip.length / step);
        for (int f = 0; f <= frames; f++)
        {
            if (f % Math.Max(1, frames / 6) == 0 || f == frames)
                line.Append($" [{f * step:0.00}s master {Fmt(master.localPosition)} {master.localRotation.eulerAngles.y:0} root {Fmt(root.localPosition - new Vector3(0, -500, 0))} {root.localRotation.eulerAngles.y:0} Δ {Fmt(animator.deltaPosition)}]");
            if (f < frames) graph.Evaluate(step);
        }
        text.AppendLine(line.ToString());
        // Absolute sampling: SetTime each frame then Evaluate(0).
        var line2 = new StringBuilder("    by SetTime:");
        for (int f = 0; f <= 6; f++)
        {
            double t = clip.length * f / 6.0;
            handle.SetTime(t);
            graph.Evaluate(0f);
            line2.Append($" [{t:0.00}s master {Fmt(master.localPosition)} Bip001 {(bip != null ? Fmt(bip.localPosition) : "-")}]");
        }
        text.AppendLine(line2.ToString());
        graph.Destroy();
        text.AppendLine($"    {watch.Elapsed.TotalMilliseconds:0.0} ms for {frames + 8} evaluations");
        return text.ToString();
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.###} {v.y:0.###} {v.z:0.###})";
}
