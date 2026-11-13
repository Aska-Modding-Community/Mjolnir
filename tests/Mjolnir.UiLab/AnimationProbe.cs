using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Controllers;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "animprobe" research (autopilot.once containing "animprobe"): three questions an animation module has to answer
/// before it is built, on the player in the world (the save, hosted). (1) Root motion: a game clip that moves (a roll)
/// played through a graph of our own, once as it is and once with the character movement's root motion ratio held at 1:
/// how far the player went. (2) A mask: the player walks (PlayerDrive's direction held forward) while a wave plays on the
/// whole body and then through a layer mixer with the game's own upper-body mask: whether the legs keep walking (how much the
/// thigh turns) while the arm waves. (3) Locking: the player walks with the movement's input modifier at 1, 0.3 and 0: how
/// far it went. Screenshots of each. It writes nothing on purpose, but the game saves by itself: back the save up first.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> AnimationProbe()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var movement = character?.GetComponentInChildren<CharacterMovement>(true);
        var drive = character?.GetComponent<PlayerDrive>() ?? character?.GetComponentInParent<PlayerDrive>();
        var animator = character == null ? null : Geometry(character.gameObject)?.Animator;
        if (character == null || movement == null || drive == null || animator == null)
        {
            Log($"Probe: character {character != null}, movement {movement != null}, drive {drive != null}, animator {animator != null}");
            yield break;
        }
        var body = character.transform;
        var start = body.position;
        var thigh = Bone(animator.transform, "Bip001 Pelvis/Bip001 L Thigh");
        var arm = Bone(animator.transform, "Bip001 R Clavicle/Bip001 R UpperArm");
        Log($"Probe: forced walk patch: {UiLab.Instance.Harmony.CreateClassProcessor(typeof(ForcedWalk)).Patch()?.Count ?? 0} method(s) patched");
        Log($"Probe: avatar root '{animator.avatarRoot?.name}', thigh {thigh != null}, arm {arm != null}, ratio {movement.animationRootMotionRatio}, input modifier {movement.commandSmoothSpeedModifier}");

        var clips = Resources.FindObjectsOfTypeAll<AnimationClip>();
        var rolls = clips.Where(c => (c.name.StartsWith("ragnar_", StringComparison.Ordinal) || c.name.StartsWith("aska_", StringComparison.Ordinal))
                                     && (c.name.Contains("roll") || c.name.Contains("dodge"))).Select(c => c.name).OrderBy(n => n).ToList();
        Log($"Probe: roll clips: {string.Join(", ", rolls)}");
        var roll = clips.FirstOrDefault(c => c.name == rolls.FirstOrDefault(n => !n.Contains("back") && !n.Contains("left") && !n.Contains("right")) )
                   ?? clips.FirstOrDefault(c => rolls.Count > 0 && c.name == rolls[0]);
        var wave = clips.FirstOrDefault(c => c.name == "aska_wave");

        // The game's masks (none are loaded), and a kit's mask from UiLab's bundles/probe when one is there: the run of
        // 2026-11-08 had one, built for it; the animation module's own UpperBody has taken its place since (scenario "animation").
        string bundlePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ShotsDir)!, "bundles", "probe");
        var bundle = System.IO.File.Exists(bundlePath) ? AssetBundle.LoadFromFileAsync(bundlePath)?.assetBundle : null;
        var kitMask = bundle?.LoadAsset("UpperBody", Il2CppInterop.Runtime.Il2CppType.Of<AvatarMask>())?.TryCast<AvatarMask>();
        Log($"Probe: the kit's bundle {bundle != null}, its mask '{kitMask?.name}' with {kitMask?.transformCount} transform(s)");
        var masks = Resources.FindObjectsOfTypeAll<AvatarMask>();
        AvatarMask? upper = null;
        foreach (var mask in masks.OrderBy(m => m.name))
        {
            var active = new List<string>();
            var inactive = new List<string>();
            for (int i = 0; i < mask.transformCount; i++)
                (mask.GetTransformActive(i) ? active : inactive).Add(mask.GetTransformPath(i));
            bool legs = active.Any(p => p.EndsWith("Thigh", StringComparison.Ordinal));
            bool arms = active.Any(p => p.EndsWith("R UpperArm", StringComparison.Ordinal));
            Log($"Probe: mask '{mask.name}': {mask.transformCount} transform(s), {active.Count} active, legs {legs}, right arm {arms}; first active [{string.Join(", ", active.Take(4))}]");
            if (upper == null && arms && !legs) upper = mask;
        }
        upper ??= kitMask;
        Log($"Probe: the upper-body mask '{upper?.name}'");

        // (1) Root motion.
        if (roll != null)
        {
            foreach (bool held in new[] { false, true })
            {
                LocalPlayer.Teleport(start);
                yield return 1f;
                var from = body.position;
                var graph = Graph(animator, roll, null);
                float until = Time.realtimeSinceStartup + roll.length;
                float ratioSeen = movement.animationRootMotionRatio;
                while (Time.realtimeSinceStartup < until)
                {
                    if (held) movement.animationRootMotionRatio = 1f;
                    ratioSeen = movement.animationRootMotionRatio;
                    yield return 0f;
                }
                graph.Destroy();
                yield return 0.3f;
                Log($"Probe: root motion '{roll.name}' ({roll.length:0.00} s), ratio held {held} (last seen {ratioSeen:0.##}): moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m");
            }
            movement.animationRootMotionRatio = 0f;
        }

        // (2) Walking with a wave on the whole body and on the upper body only.
        if (wave != null && thigh != null && arm != null)
        {
            foreach (string mode in new[] { "none", "full", "upper" })
            {
                if (mode == "upper" && upper == null) continue;
                LocalPlayer.Teleport(start);
                yield return 1f;
                var from = body.position;
                PlayableGraph graph = mode == "none" ? default : Graph(animator, wave, mode == "upper" ? upper : null);
                var thighs = new List<Quaternion>();
                var arms = new List<Quaternion>();
                float until = Time.realtimeSinceStartup + 2.5f;
                bool shot = false;
                while (Time.realtimeSinceStartup < until)
                {
                    ForcedWalk.Direction = new Vector2(0, 1);
                    thighs.Add(thigh.localRotation);
                    arms.Add(arm.localRotation);
                    if (!shot && until - Time.realtimeSinceStartup < 1.2f)
                    {
                        Shot($"probe-walk-{mode}");
                        shot = true;
                    }
                    yield return 0f;
                }
                ForcedWalk.Direction = null;
                if (mode != "none") graph.Destroy();
                Log($"Probe: walking with wave '{mode}': moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m, thigh turned up to {Spread(thighs):0.#}°, arm {Spread(arms):0.#}° over {thighs.Count} frames");
                yield return 0.5f;
            }
        }

        // (3) Locking: the input modifier the game's InputLockerCurve writes.
        foreach (float modifier in new[] { 1f, 0.3f, 0f })
        {
            LocalPlayer.Teleport(start);
            yield return 1f;
            var from = body.position;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                ForcedWalk.Direction = new Vector2(0, 1);
                movement.commandSmoothSpeedModifier = modifier;
                yield return 0f;
            }
            ForcedWalk.Direction = null;
            float seen = movement.commandSmoothSpeedModifier;
            movement.commandSmoothSpeedModifier = 1f;
            Log($"Probe: walking 2 s with the input modifier {modifier}: moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m (it read {seen:0.##} at the end)");
            yield return 0.5f;
        }
        LocalPlayer.Teleport(start);
        yield return 1f;

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A clip on the animator through a graph of its own, through a layer mixer with the mask when one is given.
    private static PlayableGraph Graph(Animator animator, AnimationClip clip, AvatarMask? mask)
    {
        var graph = PlayableGraph.Create("MjolnirProbe");
        var playable = AnimationClipPlayable.Create(graph, clip);
        var output = AnimationPlayableOutput.Create(graph, "MjolnirProbe", animator);
        if (mask == null)
            output.GetHandle().SetSourcePlayable(playable.GetHandle(), 0);
        else
        {
            var mixer = AnimationLayerMixerPlayable.Create(graph, 1, false);
            graph.ConnectInternal(playable.GetHandle(), 0, mixer.GetHandle(), 0);
            mixer.GetHandle().SetInputWeight(0, 1f);
            mixer.SetLayerMaskFromAvatarMask(0, mask);
            output.GetHandle().SetSourcePlayable(mixer.GetHandle(), 0);
        }
        output.GetHandle().SetWeight(1f);
        graph.Play();
        return graph;
    }

    // The character's own bone whose path ends so: not one of its equipment's copies of the skeleton.
    private static Transform? Bone(Transform root, string end) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => BonePath(root, t) is var path && path.EndsWith(end, StringComparison.Ordinal) && path.Split("master/").Length == 2);

    private static float Spread(List<Quaternion> rotations) => rotations.Count == 0 ? 0 : rotations.Max(r => Quaternion.Angle(rotations[0], r));

    private static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);
}

/// <summary>Holds the player's move direction for the probe: set after the game read the input, before it is sent.</summary>
[HarmonyPatch]
internal static class ForcedWalk
{
    public static Vector2? Direction;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerDrive), nameof(PlayerDrive.SendMovementCommand))]
    private static void Prefix(PlayerDrive __instance)
    {
        if (Direction is { } direction) __instance._direction = direction;
    }
}
