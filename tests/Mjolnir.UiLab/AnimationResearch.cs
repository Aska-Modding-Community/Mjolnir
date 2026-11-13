using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Network;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "animations" research (autopilot.once containing "animations"): how characters are animated, for an animation
/// module. In the main menu it lists every animation clip loaded (shots/clips.txt) and every emote and interaction moveset
/// (shots/movesets-anim.txt). In the world (the save, hosted, at a working hour) it dumps the player's and the nearest
/// villager's animator (shots/animator-player.txt, animator-villager.txt: avatar, controller, layers with what they play,
/// parameters, the character geometry's actions, the network animator), plays the player's first emote and the same emote on
/// the villager (the clips each layer plays, sampled), then plays a game clip on each through a playable graph of its own at
/// weight 1 and 0.5, logging a bone's rotation against the controller's, and takes screenshots. It writes nothing on purpose,
/// but the game saves by itself: back the save up first, put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> AnimationResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Try("clips", () => DumpClips("menu"));
        Try("movesets", DumpAnimMovesets);

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 5f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null)
        {
            Log("Animations: no local player");
            yield break;
        }
        var villager = Object.FindObjectsOfType<Villager>()
                             .OrderBy(v => Vector3.Distance(v.transform.position, character.transform.position))
                             .FirstOrDefault();
        Log($"Animations: player '{character.name}', nearest villager '{villager?.GetName()}' at {(villager == null ? 0 : Vector3.Distance(villager.transform.position, character.transform.position)):0.0} m");
        Try("clips", () => DumpClips("world"));
        Try("player", () => File.WriteAllText(Path.Combine(ShotsDir, "animator-player.txt"), DescribeCharacter(character.gameObject)));
        if (villager != null)
            Try("villager", () => File.WriteAllText(Path.Combine(ShotsDir, "animator-villager.txt"), DescribeCharacter(villager.gameObject)));

        // Bring the villager in front of the camera: the player stands 3 m from it, looking at it is the camera's business.
        if (villager != null)
        {
            var to = villager.transform.position + villager.transform.forward * 3f;
            Log($"Animations: player teleported in front of the villager: {LocalPlayer.Teleport(to)}");
            yield return 2f;
        }
        Shot("anim-start");
        yield return AfterShot;

        // The game's emote on the player, then the same one on the villager.
        var emotes = character.GetComponentInChildren<CharacterEmotes>(true);
        var emote = emotes?.EmoteList != null && emotes.EmoteList.Count > 0 ? emotes.EmoteList[0] : null;
        Log($"Animations: player emotes component {emotes != null}, {emotes?.EmoteList?.Count ?? 0} emote(s), the first '{emote?.name}'");
        var seen = new HashSet<string>();
        if (emotes != null && emote != null)
        {
            Try("player emote", () => emotes.PlayEmote(emote, true));
            for (int i = 0; i < 8; i++)
            {
                yield return 0.4f;
                Log($"Animations: player emote +{(i + 1) * 0.4f:0.0} s {Playing(character.gameObject, seen)}");
                if (i == 2) Shot("anim-player-emote");
            }
        }
        var villagerEmotes = villager?.GetComponentInChildren<CharacterEmotes>(true);
        Log($"Animations: villager emotes component {villagerEmotes != null}");
        if (villager != null && emote != null)
        {
            if (villagerEmotes != null) Try("villager emote", () => villagerEmotes.PlayEmote(emote, true));
            else Try("villager action", () =>
            {
                var geometry = villager.GetComponentInChildren<CharacterGeometry>(true);
                string action = emote.attackActionName;
                geometry.RunAction(ref action, emote.actionId);
            });
            for (int i = 0; i < 8; i++)
            {
                yield return 0.4f;
                Log($"Animations: villager emote +{(i + 1) * 0.4f:0.0} s {Playing(villager.gameObject, seen)}");
                if (i == 2) Shot("anim-villager-emote");
            }
        }
        Log($"Animations: clips seen while emoting: {string.Join(", ", seen)}");

        // A game clip through a graph of our own: an emote's clip if one was seen, else the first clip named like one.
        var clips = Resources.FindObjectsOfTypeAll<AnimationClip>();
        var clip = clips.FirstOrDefault(c => seen.Contains(c.name) && !c.name.Contains("Idle", StringComparison.OrdinalIgnoreCase))
                   ?? clips.FirstOrDefault(c => c.name.Contains("Emote", StringComparison.OrdinalIgnoreCase) || c.name.Contains("Wave", StringComparison.OrdinalIgnoreCase));
        Log($"Animations: the clip for the graph '{clip?.name}', {clip?.length:0.00} s, legacy {clip?.legacy}, root curves {clip?.hasRootCurves}, motion curves {clip?.hasMotionCurves}");
        if (clip != null)
        {
            foreach (var target in new[] { villager?.gameObject, character.gameObject })
            {
                if (target == null) continue;
                var animator = Geometry(target)?.Animator ?? target.GetComponentInChildren<Animator>();
                if (animator == null) continue;
                foreach (float weight in new[] { 1f, 0.5f })
                {
                    string before = Bone(animator);
                    PlayableGraph graph = default;
                    bool made = false;
                    Try("graph", () =>
                    {
                        graph = PlayableGraph.Create("MjolnirLab");
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        var output = AnimationPlayableOutput.Create(graph, "MjolnirLab", animator);
                        output.GetHandle().SetSourcePlayable(playable.GetHandle(), 0);
                        output.GetHandle().SetWeight(weight);
                        graph.Play();
                        made = true;
                    });
                    Log($"Animations: graph on '{target.name}' at weight {weight}: made {made}, valid {made && graph.IsValid()}, playing {made && graph.IsPlaying()}, bound {animator.hasBoundPlayables}; bone before {before}");
                    for (int i = 0; i < 5; i++)
                    {
                        yield return Math.Min(clip.length, 3f) / 5f;
                        Log($"Animations: graph +{i + 1}/5 bone {Bone(animator)}; controller {Playing(target, null)}");
                        if (i == 2) Shot($"anim-graph-{(target == character.gameObject ? "player" : "villager")}-{weight:0.0}");
                    }
                    if (made) Try("destroy", graph.Destroy);
                    yield return 0.5f;
                    Log($"Animations: graph destroyed, bound {animator.hasBoundPlayables}, bone {Bone(animator)}");
                }
            }
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static CharacterGeometry? Geometry(GameObject go) => go.GetComponentInChildren<CharacterGeometry>(true);

    // The right upper arm's local rotation: what a clip changes and the controller would not.
    private static string Bone(Animator animator)
    {
        try
        {
            var bone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            return bone == null ? "none" : bone.localEulerAngles.ToString("0");
        }
        catch (Exception e)
        {
            return $"failed: {e.Message}";
        }
    }

    // What each layer of the character's animator plays now: state hash, normalized time, weight and its clips.
    private static string Playing(GameObject go, HashSet<string>? seen)
    {
        var animator = Geometry(go)?.Animator ?? go.GetComponentInChildren<Animator>();
        if (animator == null) return "no animator";
        var parts = new List<string>();
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            float weight = animator.GetLayerWeight(layer);
            if (layer > 0 && weight <= 0.01f) continue;
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            string clips = Clips(animator, layer, seen);
            parts.Add($"L{layer} w{weight:0.##} {state.shortNameHash}@{state.normalizedTime:0.00} [{clips}]");
        }
        return string.Join("; ", parts);
    }

    private static string Clips(Animator animator, int layer, HashSet<string>? seen)
    {
        try
        {
            var list = new Il2CppSystem.Collections.Generic.List<AnimatorClipInfo>();
            animator.GetCurrentAnimatorClipInfo(layer, list);
            var names = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var info = list[i];
                string name = info.clip?.name ?? "?";
                seen?.Add(name);
                names.Add($"{name} {info.weight:0.##}");
            }
            return string.Join(", ", names);
        }
        catch (Exception e)
        {
            return $"failed: {e.Message}";
        }
    }

    private static string DescribeCharacter(GameObject go)
    {
        var text = new StringBuilder();
        var geometry = Geometry(go);
        var animators = go.GetComponentsInChildren<Animator>(true);
        text.Append($"{go.name}: {animators.Length} animator(s), geometry {geometry?.name} [{geometry?.GetIl2CppType().FullName}]\n");
        foreach (var animator in animators)
        {
            text.Append($"\nAnimator on '{animator.name}' (the geometry's {geometry != null && geometry.Animator != null && geometry.Animator.Pointer == animator.Pointer}): enabled {animator.enabled}, human {animator.isHuman}, " +
                        $"avatar '{animator.avatar?.name}' (human {animator.avatar?.isHuman}, valid {animator.avatar?.isValid}), controller '{animator.runtimeAnimatorController?.name}' " +
                        $"[{animator.runtimeAnimatorController?.GetIl2CppType().Name}], root motion {animator.applyRootMotion}, update {animator.updateMode}, culling {animator.cullingMode}, " +
                        $"speed {animator.speed}, bound playables {animator.hasBoundPlayables}, layers {animator.layerCount}, parameters {animator.parameterCount}\n");
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                var state = animator.GetCurrentAnimatorStateInfo(layer);
                text.Append($"  layer {layer}: weight {animator.GetLayerWeight(layer):0.##}, state {state.shortNameHash} full {state.fullPathHash} tag {state.tagHash} length {state.length:0.00} loop {state.loop}, clips [{Clips(animator, layer, null)}]\n");
            }
            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                text.Append($"  parameter {p.name} ({p.nameHash}): {p.type}, default {p.defaultFloat}/{p.defaultInt}/{p.defaultBool}\n");
            }
            try
            {
                var behaviours = animator.GetBehaviours<StateMachineBehaviour>();
                var names = new List<string>();
                for (int i = 0; behaviours != null && i < behaviours.Length; i++) names.Add(behaviours[i].GetIl2CppType().Name);
                text.Append($"  state machine behaviours: {names.Count}: {string.Join(", ", names.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"))}\n");
            }
            catch (Exception e)
            {
                text.Append($"  state machine behaviours failed: {e.Message}\n");
            }
        }
        if (geometry != null)
        {
            text.Append($"\nGeometry: last action '{geometry._lastStartedAction}', action SM active {geometry._isActionSMActive}, hit reactions layer {geometry._hitReactionsLayer}\n");
            var remote = geometry.allowedRemoteActions;
            text.Append($"  allowed remote actions: {(remote == null ? "none" : string.Join(", ", Enumerable.Range(0, remote.Count).Select(i => remote[i])))}\n");
            try
            {
                var triggers = geometry.ActionTriggerInfoList;
                text.Append($"  action triggers: {triggers?.Length}\n");
                for (int i = 0; triggers != null && i < triggers.Length; i++)
                {
                    var t = triggers[i];
                    var keys = new List<int>();
                    if (t.ActionDir != null)
                        foreach (var pair in t.ActionDir) keys.Add(pair.Key);
                    text.Append($"    {t.StateName} idx {t.Idx}: action ids {string.Join(",", keys)}\n");
                }
            }
            catch (Exception e)
            {
                text.Append($"  action triggers failed: {e.Message}\n");
            }
        }
        var emotes = go.GetComponentInChildren<CharacterEmotes>(true);
        text.Append($"\nEmotes: {(emotes == null ? "none" : $"{emotes.EmoteList?.Count} on '{emotes.name}'")}\n");
        for (int i = 0; emotes?.EmoteList != null && i < emotes.EmoteList.Count; i++)
        {
            var e = emotes.EmoteList[i];
            text.Append($"  {i} {e.name}: '{e.localizedName}', action '{e.attackActionName}' id {e.actionId}, sub moveset {e.animatorSubMovesetID}, display {e.displayDuration}, handle {e.equipHandleDisplay}\n");
        }
        foreach (var net in go.GetComponentsInChildren<NetworkAnimator>(true))
        {
            var floats = net.floatParametersToNetwork;
            var layers = net._networkedLayers;
            text.Append($"\nNetwork animator [{net.GetIl2CppType().Name}] on '{net.name}': owner {net.IsOwner}, ignore tag '{net._ignoreSyncTag}', sync layer weights {net._synchronizeLayerWeights}, " +
                        $"floats [{(floats == null ? "" : string.Join(",", Enumerable.Range(0, floats.Count).Select(i => floats[i])))}], " +
                        $"layers [{(layers == null ? "" : string.Join(",", Enumerable.Range(0, layers.Count).Select(i => layers[i])))}]\n");
        }
        return text.ToString();
    }

    private static void DumpClips(string where)
    {
        var clips = Resources.FindObjectsOfTypeAll<AnimationClip>().OrderBy(c => c.name).ToList();
        var text = new StringBuilder();
        foreach (var clip in clips)
            text.Append($"{clip.name}: {clip.length:0.00} s, {clip.frameRate} fps, legacy {clip.legacy}, root curves {clip.hasRootCurves}, motion curves {clip.hasMotionCurves}, generic root {clip.hasGenericRootTransform}\n");
        File.WriteAllText(Path.Combine(ShotsDir, $"clips-{where}.txt"), text.ToString());
        Log($"Animations: {clips.Count} clip(s) loaded in the {where}");
    }

    private static void DumpAnimMovesets()
    {
        var text = new StringBuilder();
        var movesets = Resources.FindObjectsOfTypeAll<InteractionMoveset>().OrderBy(m => m.name).ToList();
        foreach (var m in movesets)
        {
            var emote = m.TryCast<EmoteMoveset>();
            text.Append($"{m.name} [{m.GetIl2CppType().Name}]: action '{m.attackActionName}' id {m.actionId}, sub moveset {m.animatorSubMovesetID}, display {m.displayDuration}, " +
                        $"damage event '{m.animatorDamageEvent}', fallback {m.fallbackTriggerInterval}, handle {m.equipHandleDisplay}" +
                        (emote != null ? $", emote '{emote.localizedName}'" : "") + "\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "movesets-anim.txt"), text.ToString());
        Log($"Animations: {movesets.Count} moveset(s), {movesets.Count(m => m.TryCast<EmoteMoveset>() != null)} emote(s)");
    }
}
