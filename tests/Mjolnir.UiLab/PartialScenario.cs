using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "partial" scenario (autopilot.once containing "partial"): what the game shows of the bones a mod's clip has no curves
/// for. It needs UiLab's bundle <c>partial</c> (made from the game's skeleton, not kept in git): two clips the kit makes, ArmOnly
/// (the right upper arm raised 70° over a second, nothing else) and ArmAndRest (the same, every other bone held at its rest pose,
/// as Blender writes a clip), copied into the game's <c>Mods/Mjolnir.UiLab/bundles/</c> after deploying UiLab. In the world
/// (the save, hosted) the player walks forward while each plays on the whole body, walking let through: how far its left thigh
/// and its spine turn meanwhile (the game's walk swings them; held at rest, they stay), against the walk without a clip and
/// ArmAndRest through the upper-body mask; and, when UiLab's bundle <c>blender</c> is there too (see BlenderScenario), the
/// gesture BlenderWave made in Blender (raise_arm.py with "still") on the whole body and through the kit's mask from it.
/// </summary>
internal static partial class Autopilot
{
    public static AnimationInfo? ArmOnly, ArmAndRest, BlenderWave;
    public static AnimationMask? BlenderWaveMask;

    private const string Thigh = "master/Bip001/Bip001 Pelvis/Bip001 L Thigh";
    private const string Spine = "master/Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1";
    private const string Arm = "master/Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 R Clavicle/Bip001 R UpperArm";

    private static IEnumerator<float> PartialScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || ArmOnly == null || ArmAndRest == null)
        {
            Log($"Partial: no player or no clips ({ArmOnly}, {ArmAndRest})");
            Log($"Autopilot done; {CursorProbe.Describe()}");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        UiLab.Instance.Harmony.CreateClassProcessor(typeof(ForcedWalk)).Patch();
        var rest = RestPose(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ShotsDir)!)!)!, "ModKit", "rigs", "ragnar.rig"));
        var body = character.transform;
        var start = body.position;
        var animator = LocalPlayer.Animator;
        var cases = new (string Name, AnimationInfo? Clip, AnimationMask? Mask)[]
        {
            ("the walk alone", null, null), ("ArmOnly, whole body", ArmOnly, null), ("ArmAndRest, whole body", ArmAndRest, null),
            ("ArmAndRest, upper body", ArmAndRest, AnimationMask.UpperBody),
            ("BlenderWave, whole body", BlenderWave, null), ("BlenderWave, its own mask", BlenderWave, BlenderWaveMask),
        };
        foreach (var (name, clip, mask) in cases)
        {
            if (name.StartsWith("Blender", StringComparison.Ordinal) && (clip == null || mask == null && name.EndsWith("mask", StringComparison.Ordinal)))
            {
                Log($"Partial: {name}: not here (UiLab's bundle blender)");
                continue;
            }
            LocalPlayer.Teleport(start);
            yield return 1.5f;
            var from = body.position;
            var playback = clip == null ? null : animator.Play(clip, new AnimationOptions { Loop = true, Mask = mask, MoveSpeed = 1, Duration = TimeSpan.FromSeconds(3) });
            float began = Time.realtimeSinceStartup;
            var samples = new Dictionary<string, List<Quaternion>> { [Thigh] = [], [Spine] = [], [Arm] = [] };
            bool shot = false;
            while (Time.realtimeSinceStartup - began < 3f)
            {
                ForcedWalk.Direction = new Vector2(0, 1);
                float t = Time.realtimeSinceStartup - began;
                if (t > 0.5f && t < 2.8f)
                    foreach (var (path, list) in samples)
                        if (animator.Bone(path.Split('/').Last()) is { } bone) list.Add(bone.localRotation);
                if (!shot && t > 1.4f)
                {
                    Shot($"partial-{name.Replace(", ", "-").Replace(' ', '-')}");
                    shot = true;
                }
                yield return 0f;
            }
            ForcedWalk.Direction = null;
            playback?.Stop();
            string Describe(string path)
            {
                var list = samples[path];
                if (list.Count == 0) return "no samples";
                float swing = list.Max(a => list.Max(b => Quaternion.Angle(a, b)));
                float fromRest = rest.TryGetValue(path, out var r) ? list.Average(q => Quaternion.Angle(q, r)) : -1;
                return string.Create(CultureInfo.InvariantCulture, $"swings {swing:0.0}°, {fromRest:0.0}° from rest on average");
            }
            Log(string.Create(CultureInfo.InvariantCulture,
                $"Partial: {name}: walked {Vector3.Distance(Flat(from), Flat(body.position)):0.0} m; left thigh {Describe(Thigh)}; spine {Describe(Spine)}; right upper arm {Describe(Arm)}"));
            yield return 1f;
        }
        ForcedWalk.Direction = null;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static Dictionary<string, Quaternion> RestPose(string file)
    {
        var pose = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        if (!File.Exists(file)) return pose;
        foreach (string line in File.ReadAllLines(file).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var parts = line.Split('|');
            var n = parts[2].Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            pose[parts[0]] = new Quaternion(n[0], n[1], n[2], n[3]);
        }
        return pose;
    }
}
