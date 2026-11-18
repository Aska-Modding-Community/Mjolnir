using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "assettools" scenario (autopilot.once containing "assettools"): two questions about building assets for the game
/// outside Unity, both answered in the main menu, with nothing of the game changed.
/// <list type="number">
/// <item>Can an <see cref="AnimationClip"/> be built while the game runs? The curves are set through <c>SetCurve</c>,
/// whose native side takes its strings as spans, which is where a method the game never calls itself fails under
/// Il2CppInterop. It is tried twice, as a plain clip played by a playable graph and as a legacy clip played by an
/// <c>Animation</c>, and each is read back off a transform, so a clip the engine accepted but left empty is told apart
/// from one that works.</item>
/// <item>Which containers does the game's bundle reader accept? Several bundles are tried that each differ from the
/// platform's own in one property of the UnityFS container: the padding flag (0x200) and whether the block listing is
/// compressed. The serialized file inside is the same bytes in all of them.</item>
/// </list>
/// </summary>
/// <remarks>
/// It needs UiLab's bundles <c>atsa</c>…<c>atse</c> in the game's <c>Mods/Mjolnir.UiLab/bundles/</c>. They are repacked
/// from the animation module's <c>masks</c> bundle, which holds no resource stream and no <c>archive:/</c> path inside,
/// so the archive entry can be renamed while the data blocks stay byte for byte as Unity wrote them: <c>atsa</c> is the
/// control (renamed only), <c>atsb</c> drops the padding flag, <c>atsc</c> stores the block listing uncompressed,
/// <c>atsd</c> does both, and <c>atse</c> is a byte-exact copy that keeps its own archive name, so it is expected to be
/// refused for clashing with the loaded masks bundle — which is itself proof that the file is a readable archive.
/// Without them the scenario says so and still answers the first question.
/// </remarks>
internal static partial class Autopilot
{
    private const float ProbeMark = -99f;

    private static IEnumerator<float> AssetToolsProbe()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var text = new StringBuilder();

        text.AppendLine("== 1. An AnimationClip built while the game runs (SetCurve through Il2CppInterop)");
        text.AppendLine("     the curve is localPosition.x of a child: 0 m at 0 s, 2 m at 1 s; the mark is -99.000");

        // (a) A plain clip for an Animator, evaluated by hand as the "clips" scenario does.
        text.AppendLine("  -- a plain clip (Animator, playable graph)");
        text.Append(PlainClipProbe());

        // (b) A legacy clip for an Animation component. SetCurve is documented as working at run time only for these.
        text.AppendLine("  -- a legacy clip (Animation component)");
        Transform? legacyChild = null;
        GameObject? legacyRoot = null;
        text.Append(LegacyClipProbe(ref legacyRoot, ref legacyChild));
        if (legacyChild != null)
        {
            yield return 0.5f; // let the engine tick the clip on its own
            text.AppendLine($"     after half a second of the engine's own ticking: x {legacyChild.localPosition.x:0.000}");
        }
        if (legacyRoot != null) Object.Destroy(legacyRoot);

        // (c) The curves go in while the clip is legacy, then it is turned back into a Mecanim clip: if that keeps them,
        // a mod's clip can be built while the game runs and still be played the way the animation module plays clips.
        text.AppendLine("  -- a legacy clip turned back into a Mecanim clip");
        text.Append(FlippedClipProbe());

        text.AppendLine("== 2. Containers the game's bundle reader accepts (the same serialized file in each)");
        text.Append(ContainerProbe());

        string file = Path.Combine(ShotsDir, "assettools.txt");
        File.WriteAllText(file, text.ToString());
        foreach (string line in text.ToString().Split('\n')) if (line.Trim().Length > 0) Log("AssetTools: " + line.TrimEnd());
        Log($"AssetTools: written to shots/assettools.txt ({text.Length} characters)");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static AnimationClip? NewProbeClip(StringBuilder text, bool legacy)
    {
        try
        {
            var clip = new AnimationClip { frameRate = 30f, legacy = legacy };
            text.AppendLine($"     new AnimationClip(): ok, empty {clip.empty}, length {clip.length:0.000}, legacy {clip.legacy}");
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0f);
            curve.AddKey(1f, 2f);
            clip.SetCurve("child", Il2CppType.Of<Transform>(), "localPosition.x", curve);
            text.AppendLine($"     SetCurve(localPosition.x): no throw; empty {clip.empty}, length {clip.length:0.000}, " +
                            $"root curves {clip.hasRootCurves}, generic root {clip.hasGenericRootTransform}");
            return clip;
        }
        catch (Exception e)
        {
            text.AppendLine($"     building the clip FAILED: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static (GameObject Root, Transform Child) ProbeObjects()
    {
        var root = new GameObject("Mjolnir_AssetToolsProbe");
        root.transform.position = new Vector3(0f, -500f, 0f);
        var child = new GameObject("child").transform;
        child.SetParent(root.transform, false);
        child.localPosition = new Vector3(ProbeMark, ProbeMark, ProbeMark);
        return (root, child);
    }

    private static string PlainClipProbe()
    {
        var text = new StringBuilder();
        var clip = NewProbeClip(text, legacy: false);
        if (clip == null) return text.ToString();
        try
        {
            var (root, child) = ProbeObjects();
            var animator = root.AddComponent<Animator>();
            var graph = PlayableGraph.Create("UiLab assettools");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var handle = AnimationClipPlayable.Create(graph, clip).GetHandle();
            AnimationPlayableOutput.Create(graph, "UiLab", animator).GetHandle().SetSourcePlayable(handle, 0);
            graph.Play();
            var line = new StringBuilder("     played by hand:");
            foreach (double t in new[] { 0.0, 0.5, 1.0 })
            {
                child.localPosition = new Vector3(ProbeMark, ProbeMark, ProbeMark);
                handle.SetTime(t);
                graph.Evaluate(0f);
                line.Append($" [{t:0.00}s x {child.localPosition.x:0.000}]");
            }
            graph.Destroy();
            Object.Destroy(root);
            text.AppendLine(line.ToString());
        }
        catch (Exception e)
        {
            text.AppendLine($"     playing the clip FAILED: {e.GetType().Name}: {e.Message}");
        }
        return text.ToString();
    }

    private static string LegacyClipProbe(ref GameObject? keptRoot, ref Transform? keptChild)
    {
        var text = new StringBuilder();
        var clip = NewProbeClip(text, legacy: true);
        if (clip == null) return text.ToString();
        try
        {
            var (root, child) = ProbeObjects();
            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, "probe");
            bool played = animation.Play("probe");
            animation.Sample();
            text.AppendLine($"     Animation.Play: {played}; after Sample() at the start: x {child.localPosition.x:0.000}");
            keptRoot = root;
            keptChild = child;
        }
        catch (Exception e)
        {
            text.AppendLine($"     playing the legacy clip FAILED: {e.GetType().Name}: {e.Message}");
        }
        return text.ToString();
    }

    private static string FlippedClipProbe()
    {
        var text = new StringBuilder();
        var clip = NewProbeClip(text, legacy: true);
        if (clip == null) return text.ToString();
        try
        {
            clip.legacy = false;
            text.AppendLine($"     legacy set back to false: empty {clip.empty}, length {clip.length:0.000}, " +
                            $"root curves {clip.hasRootCurves}, generic root {clip.hasGenericRootTransform}");
        }
        catch (Exception e)
        {
            text.AppendLine($"     clearing legacy FAILED: {e.GetType().Name}: {e.Message}");
            return text.ToString();
        }
        try
        {
            var (root, child) = ProbeObjects();
            var animator = root.AddComponent<Animator>();
            var graph = PlayableGraph.Create("UiLab assettools flipped");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var handle = AnimationClipPlayable.Create(graph, clip).GetHandle();
            AnimationPlayableOutput.Create(graph, "UiLab", animator).GetHandle().SetSourcePlayable(handle, 0);
            graph.Play();
            var line = new StringBuilder("     played by hand:");
            foreach (double t in new[] { 0.0, 0.5, 1.0 })
            {
                child.localPosition = new Vector3(ProbeMark, ProbeMark, ProbeMark);
                handle.SetTime(t);
                graph.Evaluate(0f);
                line.Append($" [{t:0.00}s x {child.localPosition.x:0.000}]");
            }
            graph.Destroy();
            Object.Destroy(root);
            text.AppendLine(line.ToString());
        }
        catch (Exception e)
        {
            text.AppendLine($"     playing the flipped clip FAILED: {e.GetType().Name}: {e.Message}");
        }
        return text.ToString();
    }

    private static string ContainerProbe()
    {
        var text = new StringBuilder();
        var what = new (string Name, string Note)[]
        {
            ("atsa", "control: as Unity wrote it, only the archive renamed (flags 0x243)"),
            ("atsb", "no padding flag (flags 0x043)"),
            ("atsc", "block listing uncompressed (flags 0x240)"),
            ("atsd", "both (flags 0x040)"),
            ("atse", "byte-exact copy, its own archive name: a clash is expected, not a bad header"),
        };
        string bundles = Path.Combine(Path.GetDirectoryName(ShotsDir)!, "bundles");
        foreach (var (name, note) in what)
        {
            string path = Path.Combine(bundles, name);
            if (!File.Exists(path))
            {
                text.AppendLine($"  {name}: not there ({path})");
                continue;
            }
            long size = new FileInfo(path).Length;
            AssetBundle? bundle;
            try
            {
                bundle = AssetBundle.LoadFromFileAsync(path)?.assetBundle;
            }
            catch (Exception e)
            {
                text.AppendLine($"  {name} ({size} B, {note}): LoadFromFileAsync threw {e.GetType().Name}: {e.Message}");
                continue;
            }
            if (bundle == null)
            {
                text.AppendLine($"  {name} ({size} B, {note}): NOT LOADED");
                continue;
            }
            var mask = bundle.LoadAsset("UpperBody", Il2CppType.Of<AvatarMask>())?.TryCast<AvatarMask>();
            text.AppendLine($"  {name} ({size} B, {note}): LOADED, mask '{mask?.name}' with {mask?.transformCount} transform(s)");
        }
        return text.ToString();
    }
}
