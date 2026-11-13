using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// A clip on a character's animator through a playable graph of its own: the graph's output goes
/// over the controller's with its weight, a layer mixer with an avatar mask leaves the other bones to the controller. Only
/// non-generic handle methods are used: the game has them, generic instantiations it never made would not run.
/// </summary>
internal sealed class ClipPlayer : IClipPlayer
{
    private PlayableGraph _graph;
    private readonly PlayableHandle _clip;
    private readonly PlayableOutputHandle _output;

    private ClipPlayer(PlayableGraph graph, PlayableHandle clip, PlayableOutputHandle output)
    {
        _graph = graph;
        _clip = clip;
        _output = output;
    }

    public static ClipPlayer Start(Animator animator, AnimationClip clip, AvatarMask? mask, bool loop, double speed)
    {
        var graph = PlayableGraph.Create($"Mjolnir {clip.name}");
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetOverrideLoopTime(true); // the options say whether it loops, not the clip
        playable.SetLoopTime(loop);
        var clipHandle = playable.GetHandle();
        var source = clipHandle;
        if (mask != null)
        {
            var mixer = AnimationLayerMixerPlayable.Create(graph, 1, false);
            graph.ConnectInternal(clipHandle, 0, mixer.GetHandle(), 0);
            mixer.GetHandle().SetInputWeight(0, 1f);
            mixer.SetLayerMaskFromAvatarMask(0, mask);
            source = mixer.GetHandle();
        }
        var output = AnimationPlayableOutput.Create(graph, "Mjolnir", animator).GetHandle();
        output.SetSourcePlayable(source, 0);
        output.SetWeight(0f);
        clipHandle.SetSpeed(speed);
        graph.Play();
        return new ClipPlayer(graph, clipHandle, output);
    }

    public double Time => _graph.IsValid() ? _clip.GetTime() : 0;

    public double Speed
    {
        set
        {
            if (_graph.IsValid()) _clip.SetSpeed(value);
        }
    }

    public float Weight
    {
        set
        {
            if (_graph.IsValid()) _output.SetWeight(value);
        }
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
