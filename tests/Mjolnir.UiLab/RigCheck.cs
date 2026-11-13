using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "rigcheck" scenario (autopilot.once containing "rigcheck"): whether the skeletons Mjolnir.SDK.Animation writes for the
/// Mjolnir kit are the game's exactly. In the main menu: the game's character definitions (the playable ones among them) and
/// their bodies, the skeleton files written there, and every loaded prefab of a person's body against the file of its skeleton,
/// bone by bone, positions and rotations; each body's bones in its root's space go to shots/rig-&lt;body&gt;.txt, which the
/// kit's skeletons are compared with. Then in the world (the save, hosted): the rig the player's and the villagers' animators
/// report, and how far the characters' bones stand from their file (animation moves some). It writes nothing; the game saves
/// by itself: back the save up first.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> RigCheck()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 20f; // the module writes the skeletons once the definitions are there
        string rigs = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ShotsDir)!)!)!, "ModKit", "rigs");
        Try("definitions", LogDefinitions);
        var files = Directory.GetFiles(rigs, "*.rig").Where(f => File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddMinutes(-10)).ToList();
        Log($"Rig check: in the main menu, skeletons written in this run: {string.Join(", ", files.Select(Path.GetFileNameWithoutExtension))}; " +
            $"in the folder: {string.Join(", ", Directory.GetFiles(rigs, "*.rig").Select(Path.GetFileNameWithoutExtension))}");
        foreach (string file in Directory.GetFiles(rigs, "*.rig"))
            Try("prefabs " + file, () => CheckPrefabs(file));

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 25f;
        Try("world", () =>
        {
            Log($"Rig check: the player's rig '{LocalPlayer.Animator.Rig}'; the villagers' rigs: " +
                string.Join(", ", Residents.All.GroupBy(r => r.Animator.Rig).Select(g => $"'{g.Key}' ×{g.Count()}")));
            foreach (string file in Directory.GetFiles(rigs, "*.rig")) CheckWorld(file);
        });
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void LogDefinitions()
    {
        foreach (var definition in Resources.FindObjectsOfTypeAll<CharacterDefinition>())
        {
            var prefab = definition.character;
            var animator = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
            var root = animator != null ? animator.avatarRoot ?? animator.transform : null;
            Log($"Rig check: definition '{definition.name}': character {prefab?.name}, female {definition.female}, avatar {animator?.avatar?.name}, " +
                $"body {(root != null ? Body(root) : "")}, {(root != null ? Pose(root).Count : 0)} bones");
        }
        foreach (var page in Resources.FindObjectsOfTypeAll<SSSGame.UI.CharacterSelectionTabPage>().Take(1))
            Log($"Rig check: playable characters: {string.Join(", ", page.playableCharacterDefinitions?.definitions?.ToArray().Select(d => d.name) ?? Array.Empty<string>())}");
    }

    // Every loaded prefab of the file's avatar against it: a body of this skeleton matches it exactly (every bone there, the
    // same pose), another body (the other player's character) does not.
    private static void CheckPrefabs(string file)
    {
        string rig = Path.GetFileNameWithoutExtension(file);
        var (avatar, written) = ReadRig(file);
        var lines = new List<string>();
        var dumped = new HashSet<string>();
        foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>()
                                          .Where(a => a != null && !a.gameObject.scene.IsValid() && a.avatar != null && a.avatar.name == avatar))
        {
            var root = animator.avatarRoot ?? animator.transform;
            var pose = Pose(root);
            float position = 0, angle = 0;
            string worstPosition = "", worstAngle = "";
            foreach (var (path, (p, q)) in pose)
            {
                if (!written.TryGetValue(path, out var w)) continue;
                float d = Vector3.Distance(p, w.Position), a = Quaternion.Angle(q, w.Rotation);
                if (d > position) { position = d; worstPosition = path.Split('/').Last(); }
                if (a > angle) { angle = a; worstAngle = path.Split('/').Last(); }
            }
            int lacking = written.Keys.Count(k => !pose.ContainsKey(k)), extra = pose.Keys.Count(k => !written.ContainsKey(k));
            bool exact = extra == 0 && position < 0.000001f && angle < 0.001f;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{(exact ? "SAME" : "OTHER")} '{Body(root)}' ({animator.name}): {pose.Count} bones, up to {position * 1000:0.######} mm ({worstPosition}) and {angle:0.####}° ({worstAngle}), {lacking} file bones it lacks, {extra} it has more"));
            string body = Body(root);
            if (!exact || !dumped.Add(body)) continue;
            var text = new System.Text.StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = BonePath(root, t);
                if (!written.ContainsKey(path)) continue;
                var p = root.InverseTransformPoint(t.position);
                var q = Quaternion.Inverse(root.rotation) * t.rotation;
                text.Append(CultureInfo.InvariantCulture, $"{path}|{p.x:R} {p.y:R} {p.z:R}|{q.x:R} {q.y:R} {q.z:R} {q.w:R}\n");
            }
            File.WriteAllText(Path.Combine(ShotsDir, $"rig-{rig}{(body == rig ? "" : "@" + body)}.txt"), text.ToString());
        }
        Log($"Rig check {rig}: {written.Count} bones in the file, avatar {avatar}; its prefabs: {string.Join("; ", lines.Distinct())}");
    }

    // The world's characters of this skeleton (by their body) against the file: which bones animation moves off it.
    private static void CheckWorld(string file)
    {
        string rig = Path.GetFileNameWithoutExtension(file);
        var (_, written) = ReadRig(file);
        var off = new Dictionary<string, float>();
        int characters = 0;
        foreach (var character in Object.FindObjectsOfType<Character>())
        {
            var animator = character.GetComponentInChildren<CharacterGeometry>(true)?.Animator;
            if (animator == null) continue;
            var root = animator.avatarRoot ?? animator.transform;
            var pose = Pose(root);
            if (!pose.Keys.All(written.ContainsKey) || pose.Count < 20) continue; // another skeleton
            if (Body(root) != rig && !(rig == "peasant_male" && Body(root).StartsWith("peasant_male", StringComparison.Ordinal))) continue;
            characters++;
            foreach (var (path, (position, _)) in pose)
                off[path] = Math.Max(off.GetValueOrDefault(path), Vector3.Distance(position, written[path].Position));
        }
        var moved = off.Where(o => o.Value > 0.0005f).OrderByDescending(o => o.Value).ToList();
        Log(string.Create(CultureInfo.InvariantCulture,
            $"Rig check {rig}: {characters} character(s) in the world, {off.Count - moved.Count} of {off.Count} bones at the file's place on every one (within 0.5 mm); " +
            $"moved on some: {string.Join(", ", moved.Take(12).Select(o => $"{o.Key.Split('/').Last()} {o.Value * 1000:0.#} mm"))}{(moved.Count > 12 ? ", …" : "")}"));
    }

    private static (string Avatar, Dictionary<string, (Vector3 Position, Quaternion Rotation)> Bones) ReadRig(string file)
    {
        var lines = File.ReadAllLines(file);
        var header = Regex.Match(lines.FirstOrDefault() ?? "", @"\(avatar (\S+?),");
        var bones = lines.Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('|'))
                         .ToDictionary(p => p[0], p => (V3(p[1]), Q(p[2])));
        return (header.Success ? header.Groups[1].Value : "", bones);
    }

    private static string Body(Transform root) =>
        Regex.Replace(root.name.Replace("(Clone)", "", StringComparison.Ordinal).Trim(), @" \(\d+\)$", "");

    private static Dictionary<string, (Vector3, Quaternion)> Pose(Transform root)
    {
        var pose = new Dictionary<string, (Vector3, Quaternion)>(StringComparer.Ordinal);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            string path = BonePath(root, t);
            var parts = path.Split('/');
            if (parts[0] == "master" && parts.Skip(1).All(p => p.StartsWith("Bip001", StringComparison.Ordinal) || p.StartsWith("Bone ", StringComparison.Ordinal)))
                pose.TryAdd(path, (t.localPosition, t.localRotation));
        }
        return pose;
    }

    private static Vector3 V3(string text)
    {
        var n = text.Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new Vector3(n[0], n[1], n[2]);
    }

    private static Quaternion Q(string text)
    {
        var n = text.Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new Quaternion(n[0], n[1], n[2], n[3]);
    }
}
