using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "animation" scenario (autopilot.once containing "animation"): Mjolnir.SDK.Animation in the world (the save, hosted).
/// In the main menu: the catalog of the game's clips. In the world: the skeletons written for the Mjolnir kit, the player's
/// animator (its rig, a bone), a wave on the whole body with the player held while walking, then through the upper-body mask
/// with the legs walking, a moment of the clip and its end, a dodge moving the player only with root motion, the player's
/// emote; then a villager spawned in front of the camera waving through its animator, its emote, and UiLab's job
/// lab_animation playing a looping wave for 4 s. Screenshots of each. Run it without -Examples (VillagersExample's greeting
/// would take the villager away). It writes nothing on purpose, but the game saves by itself: back the save up first.
/// </summary>
internal static partial class Autopilot
{
    /// <summary>The villager the lab_animation job asks for; its last step clears it.</summary>
    public static Resident? LabAnimated;

    /// <summary>When lab_animation's first step ran.</summary>
    public static float LabAnimationStarted;
    public static Resident? LabWalker;
    public static Vector3 LabWalkTo;

    private static IEnumerator<float> AnimationScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Animation: in the main menu {Animations.All.Count} animation(s), aska_wave {Animations.Find("aska_wave")?.Length.TotalSeconds:0.00} s, " +
            $"the player available {LocalPlayer.Animator.IsAvailable} (false expected)");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var drive = character?.GetComponent<SSSGame.Controllers.PlayerDrive>() ?? character?.GetComponentInParent<SSSGame.Controllers.PlayerDrive>();
        if (character == null || drive == null)
        {
            Log("Animation: no player");
            yield break;
        }
        UiLab.Instance.Harmony.CreateClassProcessor(typeof(ForcedWalk)).Patch();
        var body = character.transform;
        var start = body.position;
        var animator = LocalPlayer.Animator;
        string rigs = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ShotsDir)!)!)!, "ModKit", "rigs");
        Log($"Animation: the player's animator available {animator.IsAvailable}, rig '{animator.Rig}', the same object {ReferenceEquals(animator, LocalPlayer.Animator)}, " +
            $"right hand {animator.Bone("Bip001 R Hand")?.name}, a bone it lacks {animator.Bone("nothing") == null}; skeletons written: " +
            $"{(Directory.Exists(rigs) ? string.Join(", ", Directory.GetFiles(rigs).Select(f => $"{Path.GetFileName(f)} ({File.ReadAllLines(f).Length - 2} bones)")) : "none")}");

        var wave = Animations.Find("aska_wave")!;
        // The whole body: the player stands while it plays (walking is held), the moment and the end come.
        foreach (bool upper in new[] { false, true })
        {
            LocalPlayer.Teleport(start);
            yield return 1f;
            var from = body.position;
            float moment = -1, ended = -1, walkedWhilePlaying = -1;
            bool? stopped = null;
            float began = Time.realtimeSinceStartup;
            var playback = animator.Play(wave, new AnimationOptions { Mask = upper ? AnimationMask.UpperBody : null })
                                   .At(TimeSpan.FromSeconds(1), () => moment = Time.realtimeSinceStartup - began);
            playback.Ended += (_, e) =>
            {
                ended = Time.realtimeSinceStartup - began;
                stopped = e.WasStopped;
                walkedWhilePlaying = Vector3.Distance(Flat(from), Flat(body.position));
            };
            Log($"Animation: playing {wave} (upper body {upper}): current {ReferenceEquals(animator.Current, playback)}");
            bool shot = false;
            while (Time.realtimeSinceStartup - began < 4f)
            {
                ForcedWalk.Direction = new Vector2(0, 1);
                if (!shot && Time.realtimeSinceStartup - began > 1.5f)
                {
                    Shot($"anim-wave-{(upper ? "upper" : "full")}");
                    shot = true;
                }
                yield return 0f;
            }
            ForcedWalk.Direction = null;
            Log($"Animation: wave (upper body {upper}): the player walked {walkedWhilePlaying:0.00} m while it played " +
                $"({(upper ? "the legs free: some metres" : "held: within its fades, under a metre")}), {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m in 4 s, the moment at {moment:0.00} s, ended at {ended:0.00} s (stopped {stopped}), playing {playback.IsPlaying}, current {animator.Current?.ToString() ?? "none"}");
        }

        // Root motion: the game's dodge moves the player only when asked.
        var dodge = Animations.Find("aska_dodge_front");
        foreach (bool rootMotion in new[] { false, true })
        {
            if (dodge == null) break;
            LocalPlayer.Teleport(start);
            yield return 1f;
            var from = body.position;
            var playback = animator.Play(dodge, new AnimationOptions { RootMotion = rootMotion, FadeIn = TimeSpan.Zero });
            while (playback.IsPlaying) yield return 0.1f;
            yield return 0.3f;
            Log($"Animation: {dodge} with root motion {rootMotion}: moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m ({(rootMotion ? "about 3.8" : "0")} expected)");
        }

        // A stop while it plays, and the game's emote.
        var looped = animator.Play(wave, new AnimationOptions { Loop = true });
        yield return 1f;
        looped.Stop();
        yield return 0.5f;
        Log($"Animation: a loop stopped after 1 s: playing {looped.IsPlaying} (false expected)");
        Log($"Animation: the player's emote Cheer: {animator.PlayEmote(Emote.Cheer)}");
        yield return 1.5f;
        Shot("anim-player-emote");
        yield return 2f;

        // A villager in front of the camera.
        LocalPlayer.Teleport(start);
        yield return 1f;
        var camera = Camera.main!.transform;
        var at = body.position + Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized * 3.5f;
        int before = Residents.All.Count;
        Log($"Animation: spawn at {at} asked {Residents.Spawn(at)}");
        foreach (float wait in WaitFor("the new villager", () => Residents.All.Count > before, 15)) yield return wait;
        yield return 1f;
        var resident = Residents.All[^1];
        var villagerAnimator = resident.Animator;
        Log($"Animation: villager '{resident.Name}': available {villagerAnimator.IsAvailable}, rig '{villagerAnimator.Rig}', head {villagerAnimator.Bone("Bip001 Head")?.name}");
        var own = Animations.Find(villagerAnimator.Rig + "_wave") ?? wave;
        // A new villager walks off to the settlement: a whole-body wave holds it where it is, and it walks on after; a wave
        // through the upper-body mask leaves it walking.
        // UiLab's job lab_walk sends it to another villager 35 to 70 m away, so it is surely on its way.
        var goal = Residents.All.Where(r => r != resident && r.IsAlive)
                                .Select(r => r.Position)
                                .FirstOrDefault(p => Vector3.Distance(Flat(p), Flat(resident.Position)) is > 35 and < 70);
        LabWalkTo = goal == default ? resident.Position + Vector3.forward * 40 : goal;
        LabWalker = resident;
        float waitedWalking = 0;
        for (var last = resident.Position; waitedWalking < 15; waitedWalking += 0.5f)
        {
            yield return 0.5f;
            bool walking = Vector3.Distance(Flat(last), Flat(resident.Position)) > 0.5f;
            last = resident.Position;
            if (walking) break;
        }
        var walkedFrom = resident.Position;
        yield return 0.5f;
        Log($"Animation: the villager walked {Vector3.Distance(Flat(walkedFrom), Flat(resident.Position)):0.00} m in the half second before the wave, {waitedWalking:0.0} s after it was sent {Vector3.Distance(Flat(LabWalkTo), Flat(walkedFrom)):0.0} m away, doing '{resident.Activity}' (walking: some)");
        var waved = villagerAnimator.Play(own, new AnimationOptions { Loop = true, Duration = TimeSpan.FromSeconds(3) });
        yield return 0.4f;
        var heldFrom = resident.Position;
        yield return 0.8f;
        Shot("anim-villager-wave");
        yield return 1.4f;
        Log($"Animation: the villager's whole-body {own}: it moved {Vector3.Distance(Flat(heldFrom), Flat(resident.Position)):0.00} m in 2.2 s while it played (held: about 0)");
        yield return 1.1f;
        Log($"Animation: the villager's {own} for 3 s: playing {waved.IsPlaying} (false expected)");
        var after = resident.Position;
        yield return 2f;
        Log($"Animation: after the wave the villager walked {Vector3.Distance(Flat(after), Flat(resident.Position)):0.00} m in 2 s (on its way again: some, unless it arrived; doing '{resident.Activity}')");
        var maskedFrom = resident.Position;
        var masked = villagerAnimator.Play(own, new AnimationOptions { Loop = true, Duration = TimeSpan.FromSeconds(3), Mask = AnimationMask.UpperBody });
        yield return 1.5f;
        Shot("anim-villager-wave-walking");
        yield return 1.5f;
        Log($"Animation: the villager's {own} through the upper-body mask: it walked {Vector3.Distance(Flat(maskedFrom), Flat(resident.Position)):0.00} m in 3 s while it played (the legs free: some, unless it arrived)");
        LabWalker = null; // the walk is over for the scenario: lab_animation takes the villager next
        yield return 0.5f;
        Log($"Animation: the villager's emote Wave: {villagerAnimator.PlayEmote(Emote.Wave)}");
        yield return 1.5f;
        Shot("anim-villager-emote");
        yield return 2f;

        // UiLab's job: a looping wave for 4 s where the villager stands.
        LabAnimated = resident;
        float asked = Time.realtimeSinceStartup;
        foreach (float wait in WaitFor("lab_animation to start", () => LabAnimationStarted > 0, 20)) yield return wait;
        yield return 1.5f;
        Log($"Animation: lab_animation started {LabAnimationStarted - asked:0.0} s after it asked; the villager plays {villagerAnimator.Current?.Animation.ToString() ?? "nothing"}, doing '{resident.Activity}'");
        Shot("anim-villager-job");
        foreach (float wait in WaitFor("lab_animation to end", () => LabAnimated == null, 20)) yield return wait;
        Log($"Animation: lab_animation ended {Time.realtimeSinceStartup - LabAnimationStarted:0.0} s after it started (about 4); plays {villagerAnimator.Current?.Animation.ToString() ?? "nothing"}");

        // examples/Mjolnir.AnimationsExample (with -Examples): the game's clips by name, the villagers toasting back, the dodge.
        if (DevConsole.Commands.Any(c => c.Name == "toast") && Animations.Find("aska_idle_skal") is { } toast
            && Animations.Find("peasant_male_idle_skal") is { } toastBack && Animations.Find("aska_dodge_front") is { } exampleDodge)
        {
            // Someone to answer: a villager in front of the player when none is near.
            if (!Residents.All.Any(r => Vector3.Distance(r.Position, body.position) <= 12f))
            {
                int count = Residents.All.Count;
                var front = body.position + Vector3.ProjectOnPlane(Camera.main!.transform.forward, Vector3.up).normalized * 3f;
                Log($"Animation: a villager for the toast, spawn asked {Residents.Spawn(front)}");
                foreach (float wait in WaitFor("a villager for the toast", () => Residents.All.Count > count, 15)) yield return wait;
                yield return 1.5f;
            }
            var near = Residents.All.Where(r => Vector3.Distance(r.Position, body.position) <= 12f).ToList();
            DevConsole.Execute("toast");
            yield return 1.1f;
            Log($"Animation: the example's toast: the player plays {animator.Current?.Animation.ToString() ?? "nothing"} ({toast.Length.TotalSeconds:0.00} s), " +
                $"{near.Count} villager(s) within 12 m");
            Shot("anim-example-toast");
            var answering = new HashSet<string>();
            for (int i = 0; i < 16; i++)
            {
                foreach (var r in near.Where(r => r.Animator.Current?.Animation == toastBack)) answering.Add(r.Name);
                if (i == 6) Shot("anim-example-toast-back");
                yield return 0.5f;
            }
            Log($"Animation: villagers that toasted back: {answering.Count} ({string.Join(", ", answering.Take(8))})");
            yield return 1f;
            var from = body.position;
            DevConsole.Execute("dodge");
            yield return 0.3f;
            Shot("anim-example-dodge");
            yield return 1f;
            Log($"Animation: the example's dodge ({exampleDodge.Length.TotalSeconds:0.00} s, root motion): moved {Vector3.Distance(Flat(from), Flat(body.position)):0.00} m (3.76 for aska_dodge_front before)");
        }
        else Log("Animation: AnimationsExample is not here (deploy with -Examples)");
        string rigFiles = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ShotsDir)!)!)!, "ModKit", "rigs");
        Log($"Animation: skeletons written by now: {string.Join(", ", Directory.GetFiles(rigFiles).Select(Path.GetFileName))}");

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
