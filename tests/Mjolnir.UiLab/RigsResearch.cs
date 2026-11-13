using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "rigs" research (autopilot.once containing "rigs"), for the Mjolnir kit's skeletons and a second look at villagers.
/// In the world (the save, hosted, at a working hour) it writes the rig of the player and of a male and a female villager
/// (shots/rig-&lt;avatar&gt;.txt: every transform under the animator with its path, its local pose now and, for a skinned
/// bone, its pose at bind time from the mesh's bind poses) and logs which bone paths the rigs share. Then it spawns a villager
/// in front of the camera and plays on it each game emote, then a villager's clip and the player's clip through a graph of its
/// own, with screenshots. Run it without -Examples: VillagersExample's greeting would take the new villager away. It writes
/// nothing on purpose, but the game saves by itself: back the save up first, put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> RigsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null)
        {
            Log("Rigs: no local player");
            yield break;
        }

        // One character of each rig: the player, then villagers by their avatar.
        var rigs = new Dictionary<string, Animator>();
        void Take(GameObject go)
        {
            var animator = Geometry(go)?.Animator;
            string? avatar = animator?.avatar?.name;
            if (animator != null && avatar != null && !rigs.ContainsKey(avatar)) rigs[avatar] = animator;
        }
        Take(character.gameObject);
        foreach (var villager in Object.FindObjectsOfType<Villager>()) Take(villager.gameObject);
        var paths = new Dictionary<string, HashSet<string>>();
        foreach (var (avatar, animator) in rigs)
            Try("rig " + avatar, () => paths[avatar] = DumpRig(avatar, animator));
        Log($"Rigs: {string.Join("; ", paths.Select(p => $"{p.Key} {p.Value.Count} transform(s)"))}");
        var names = paths.Keys.ToList();
        for (int i = 0; i < names.Count; i++)
            for (int j = i + 1; j < names.Count; j++)
            {
                var a = paths[names[i]];
                var b = paths[names[j]];
                var shared = a.Intersect(b).ToList();
                Log($"Rigs: {names[i]} and {names[j]} share {shared.Count} path(s); only in the first {a.Count - shared.Count} " +
                    $"({string.Join(", ", a.Except(b).Take(12))}), only in the second {b.Count - shared.Count} ({string.Join(", ", b.Except(a).Take(12))})");
            }

        // A new villager in front of the camera, free of any work for a while.
        var camera = Camera.main!.transform;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        var at = character.transform.position + forward * 3.5f;
        int before = Residents.All.Count;
        Log($"Rigs: spawn at {at} asked {Residents.Spawn(at)}");
        foreach (float wait in WaitFor("the new villager", () => Residents.All.Count > before, 15)) yield return wait;
        yield return 2f;
        var resident = Residents.All[^1];
        var spawned = Object.FindObjectsOfType<Villager>()
                            .OrderBy(v => Vector3.Distance(v.transform.position, resident.Position)).First();
        var spawnedAnimator = Geometry(spawned.gameObject)!.Animator;
        Log($"Rigs: spawned '{resident.Name}', avatar '{spawnedAnimator.avatar?.name}', doing '{resident.Activity}', at {Vector3.Distance(spawned.transform.position, character.transform.position):0.0} m");

        var emotes = spawned.GetComponentInChildren<CharacterEmotes>(true);
        foreach (var emote in Resources.FindObjectsOfTypeAll<EmoteMoveset>().OrderBy(e => e.name))
        {
            var seen = new HashSet<string>();
            Try("emote " + emote.name, () => emotes.PlayEmote(emote, true));
            for (int i = 0; i < 6; i++)
            {
                yield return 0.5f;
                Playing(spawned.gameObject, seen);
                if (i == 2) Shot($"rigs-{emote.name}");
            }
            Log($"Rigs: villager emote {emote.name} (sub moveset {emote.animatorSubMovesetID}): clips {string.Join(", ", seen)}; activity '{resident.Activity}'");
        }

        // Clips through a graph: the villager's own rig's wave, then the player's.
        bool female = spawnedAnimator.avatar?.name?.Contains("female", StringComparison.OrdinalIgnoreCase) == true;
        var clips = Resources.FindObjectsOfTypeAll<AnimationClip>();
        foreach (string name in new[] { female ? "peasant_female_wave" : "peasant_male_wave", "aska_wave" })
        {
            var clip = clips.FirstOrDefault(c => c.name == name);
            if (clip == null)
            {
                Log($"Rigs: no clip {name}");
                continue;
            }
            PlayableGraph graph = default;
            bool made = false;
            Try("graph", () =>
            {
                graph = PlayableGraph.Create("MjolnirLab");
                var playable = AnimationClipPlayable.Create(graph, clip);
                var output = AnimationPlayableOutput.Create(graph, "MjolnirLab", spawnedAnimator);
                output.GetHandle().SetSourcePlayable(playable.GetHandle(), 0);
                output.GetHandle().SetWeight(1f);
                graph.Play();
                made = true;
            });
            Log($"Rigs: graph with {name} ({clip.length:0.00} s) on the villager: made {made}");
            yield return clip.length * 0.4f;
            Shot($"rigs-graph-{name}");
            yield return clip.length * 0.6f;
            if (made) Try("destroy", graph.Destroy);
            yield return 0.5f;
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // Every transform under the animator: path, local pose now, and a skinned bone's local pose at bind time.
    private static HashSet<string> DumpRig(string avatar, Animator animator)
    {
        var root = animator.transform;
        var all = root.GetComponentsInChildren<Transform>(true);
        var bind = new Dictionary<IntPtr, Matrix4x4>(); // a bone's world matrix at bind time, in the mesh's space
        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var meshes = new StringBuilder();
        foreach (var renderer in skinned)
        {
            var bones = renderer.bones;
            var poses = renderer.sharedMesh?.bindposes;
            meshes.Append($"# mesh {BonePath(root, renderer.transform)}: '{renderer.sharedMesh?.name}', root bone {(renderer.rootBone == null ? "none" : BonePath(root, renderer.rootBone))}, {bones?.Length} bone(s), {poses?.Length} bind pose(s)\n");
            for (int i = 0; bones != null && poses != null && i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null) bind[bones[i].Pointer] = poses[i].inverse;
        }
        var text = new StringBuilder();
        text.Append($"# rig {avatar}: animator on '{root.name}', {all.Length} transform(s), {bind.Count} skinned bone(s)\n");
        text.Append(meshes);
        text.Append("# path | local position | local rotation (x y z w) | local scale | bind local position | bind local rotation\n");
        var paths = new HashSet<string>();
        foreach (var t in all)
        {
            string path = BonePath(root, t);
            paths.Add(path);
            string bound = "-";
            if (bind.TryGetValue(t.Pointer, out var world))
            {
                var local = t.parent != null && t != root && bind.TryGetValue(t.parent.Pointer, out var parent) ? parent.inverse * world : world;
                var position = (Vector3)local.GetColumn(3);
                var rotation = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
                bound = $"{V(position)} | {Q(rotation)}";
            }
            text.Append($"{path} | {V(t.localPosition)} | {Q(t.localRotation)} | {V(t.localScale)} | {bound}\n");
        }
        File.WriteAllText(System.IO.Path.Combine(ShotsDir, $"rig-{avatar}.txt"), text.ToString());
        return paths;
    }

    private static string BonePath(Transform root, Transform t)
    {
        if (t == root) return "";
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static string V(Vector3 v) => $"{v.x:0.#####} {v.y:0.#####} {v.z:0.#####}".Replace(',', '.');
    private static string Q(Quaternion q) => $"{q.x:0.#####} {q.y:0.#####} {q.z:0.#####} {q.w:0.#####}".Replace(',', '.');
}
