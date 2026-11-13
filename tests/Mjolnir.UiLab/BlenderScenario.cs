using System;
using System.Collections.Generic;
using Mjolnir.SDK;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "blender" scenario (autopilot.once containing "blender"): a clip made in Blender on a skeleton the Mjolnir kit exported,
/// played in the game. It needs UiLab's bundle <c>blender</c> (not in the repository: it is made from the game's skeleton):
/// export the skeletons from the kit (Mjolnir → Rigs → Export as FBX), run <c>tests/Mjolnir.UiLab/Blender/raise_arm.py</c> in
/// Blender on <c>Build/Rigs/ragnar.fbx</c> into <c>Assets/Mods/Mjolnir.UiLab/blender/raise.fbx</c> (the right upper arm raised
/// 70° and the armature moved 1 m forward over a second), build the kit's bundles, and copy
/// <c>Build/Bundles/Mjolnir.UiLab/blender</c> into the game's <c>Mods/Mjolnir.UiLab/bundles/</c> after deploying UiLab.
/// In the world (the save, hosted): the clip found under its action's name, played without and with root motion (how far the
/// player went), and how far the right upper arm turned between the clip's start and end (a screenshot at the end).
/// </summary>
internal static partial class Autopilot
{
    public static AnimationInfo? BlenderRaise;

    private static IEnumerator<float> BlenderScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var clip = BlenderRaise ?? Animations.Find("mjolnir.test.uilab.blender_raise");
        Log($"Blender: the clip {clip?.ToString() ?? "not found"}, {clip?.Length.TotalSeconds:0.00} s (1.00 expected); the player's rig '{LocalPlayer.Animator.Rig}'");
        if (character == null || clip == null)
        {
            Log($"Autopilot done; {CursorProbe.Describe()}");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        var body = character.transform;
        var start = body.position;
        var animator = LocalPlayer.Animator;
        foreach (bool rootMotion in new[] { false, true })
        {
            LocalPlayer.Teleport(start);
            yield return 1.5f;
            var from = body.position;
            var arm = animator.Bone("Bip001 R UpperArm");
            Quaternion? atStart = null, atEnd = null;
            var playback = animator.Play(clip, new AnimationOptions { RootMotion = rootMotion, FadeIn = TimeSpan.Zero, FadeOut = TimeSpan.FromSeconds(0.3) })
                                   .At(TimeSpan.FromSeconds(0.03), () => atStart = arm?.localRotation)
                                   .At(TimeSpan.FromSeconds(0.97), () => atEnd = arm?.localRotation);
            float began = Time.realtimeSinceStartup;
            bool shot = false;
            while (playback.IsPlaying && Time.realtimeSinceStartup - began < 3f)
            {
                if (!shot && rootMotion && Time.realtimeSinceStartup - began > 0.9f)
                {
                    Shot("blender-raise-end");
                    shot = true;
                }
                yield return 0f;
            }
            yield return 0.3f;
            float turned = atStart is { } a && atEnd is { } b ? Quaternion.Angle(a, b) : -1;
            Log($"Blender: root motion {rootMotion}: moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m ({(rootMotion ? "about 1" : "0")} expected); " +
                $"the right upper arm turned {turned:0.0}° from the clip's start to its end (70 expected)");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
