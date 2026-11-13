using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "mixamo" scenario (autopilot.once containing "mixamo"): Mixamo's animations baked onto the game's skeleton by the
/// Mjolnir kit (Bake Humanoid Clips to Rig), played in the game. It needs UiLab's bundle <c>mixamo</c>, not kept in git (Mixamo's
/// files are not ours to share): put the FBX files (FBX, Without Skin, 30 fps) into the kit's
/// <c>Assets/Mods/Mjolnir.UiLab/mixamo</c> (the kit makes them humanoid), bake each onto <c>ragnar</c> into the same folder,
/// build the bundles, and copy <c>Build/Bundles/Mjolnir.UiLab/mixamo</c> into the game's <c>Mods/Mjolnir.UiLab/bundles/</c>
/// after deploying UiLab. In the world (the save, hosted): each clip on the player standing (how low the pelvis goes, where the
/// feet are against standing still, a screenshot), and the first on the nearest villager.
/// </summary>
internal static partial class Autopilot
{
    public static readonly List<AnimationInfo> MixamoClips = [];

    public static readonly string[] MixamoNames = ["Standing 1H Magic Attack 03", "Standing 2H Cast Spell 01", "Standing 2H Magic Attack 01"];

    private static IEnumerator<float> MixamoScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 3f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        Log($"Mixamo: {MixamoClips.Count} clip(s) registered; the player's rig '{LocalPlayer.Animator.Rig}'");
        if (character != null)
        {
            var body = character.transform;
            var animator = LocalPlayer.Animator;
            var still = Measure(animator, body, null);
            foreach (float wait in still.Run) yield return wait;
            Log($"Mixamo: standing still: {still}");
            foreach (var clip in MixamoClips)
                for (int round = 1; round <= Rounds; round++)
                {
                    Mjolnir.SDK.UI.Notify.Message($"Mixamo: {clip.Name} ({round}/{Rounds})");
                    yield return 1.5f;
                    var measure = Measure(animator, body, clip);
                    foreach (float wait in measure.Run) yield return wait;
                    Log($"Mixamo: {clip.Name} on the player, round {round}: {measure}");
                }
        }
        var villager = Residents.All.Where(r => r.IsAlive && r.Animator.IsAvailable)
                                    .OrderBy(r => Vector3.Distance(r.Position, LocalPlayer.Position)).FirstOrDefault();
        if (villager != null && MixamoClips.Count > 0)
        {
            var bodyOf = villager.Animator.Bone("master")?.parent;
            if (bodyOf != null)
            {
                Mjolnir.SDK.UI.Notify.Message($"Mixamo: {MixamoClips[0].Name} on {villager.Name}");
                var measure = Measure(villager.Animator, bodyOf, MixamoClips[0]);
                foreach (float wait in measure.Run) yield return wait;
                Log($"Mixamo: {MixamoClips[0].Name} on the villager '{villager.Name}' (rig {villager.Animator.Rig}): {measure}");
            }
        }
        yield return 3f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // Each clip played this many times on the player, whole, with a notification naming it: slow enough to watch.
    private const int Rounds = 3;

    private static Measured Measure(CharacterAnimator animator, Transform ground, AnimationInfo? clip) => new(animator, ground, clip);

    /// <summary>One clip played and watched: the pelvis's and the lower foot's height above the character's own place.</summary>
    private sealed class Measured(CharacterAnimator animator, Transform ground, AnimationInfo? clip)
    {
        private float _pelvisLow = float.MaxValue, _pelvisHigh = float.MinValue, _footLow = float.MaxValue, _footHigh = float.MinValue;
        private int _samples;

        public IEnumerable<float> Run
        {
            get
            {
                var playback = clip == null ? null : animator.Play(clip, new AnimationOptions { FadeIn = TimeSpan.Zero });
                float began = Time.realtimeSinceStartup;
                bool shot = false;
                // A mod's clip has its length once its bundle is loaded, at the first play: the clip plays to its end.
                while (playback == null ? Time.realtimeSinceStartup - began < 1.5f : playback.IsPlaying && Time.realtimeSinceStartup - began < 10f)
                {
                    float t = Time.realtimeSinceStartup - began;
                    if (t > 0.1f) Sample();
                    if (!shot && clip != null && ground == LocalPlayerBody() && t > clip.Length.TotalSeconds * 0.45)
                    {
                        Shot("mixamo-" + clip.Name.Replace(' ', '-'));
                        shot = true;
                    }
                    yield return 0f;
                }
                playback?.Stop();
                yield return 1f;
            }
        }

        private void Sample()
        {
            var pelvis = animator.Bone("Bip001 Pelvis");
            var left = animator.Bone("Bip001 L Foot");
            var right = animator.Bone("Bip001 R Foot");
            if (pelvis == null || left == null || right == null) return;
            float floor = ground.position.y;
            float p = pelvis.position.y - floor, f = Math.Min(left.position.y, right.position.y) - floor;
            _pelvisLow = Math.Min(_pelvisLow, p);
            _pelvisHigh = Math.Max(_pelvisHigh, p);
            _footLow = Math.Min(_footLow, f);
            _footHigh = Math.Max(_footHigh, f);
            _samples++;
        }

        public override string ToString() => _samples == 0 ? "no samples" : string.Create(CultureInfo.InvariantCulture,
            $"pelvis {_pelvisLow:0.00}..{_pelvisHigh:0.00} m, the lower foot {_footLow:0.00}..{_footHigh:0.00} m above the character's place ({_samples} frames)");
    }

    private static Transform? LocalPlayerBody() => GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.transform;
}
