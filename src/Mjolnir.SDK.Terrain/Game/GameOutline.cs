using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// An outline's two lines in the world: line renderers with an unlit material of the game's render pipeline (lit ones fade
/// in the shade) that draws over what stands in front of it, on an object that outlives the scenes, hidden until drawn.
/// </summary>
internal sealed class GameOutline : IOutlineView
{
    // The blend's line against the edge's.
    private const float BlendShare = 0.4f;

    private static Shader? s_shader;

    private readonly GameObject _root;
    private readonly LineRenderer _edge;
    private readonly LineRenderer _blend;

    public GameOutline()
    {
        _root = new GameObject("Mjolnir_LandscapeOutline");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _edge = NewLine("Edge", LandscapeOutline.EdgeWidth);
        _blend = NewLine("Blend", LandscapeOutline.EdgeWidth * BlendShare);
        _root.SetActive(false);
    }

    private LineRenderer NewLine(string name, float width)
    {
        var child = new GameObject(name);
        child.hideFlags = HideFlags.HideAndDontSave;
        child.transform.SetParent(_root.transform, false);
        var line = child.AddComponent(Il2CppType.Of<LineRenderer>()).Cast<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        // Drawn through whatever stands in front of it: a ring on the ground stays whole over a rock or a trunk in its way.
        if (OverlayMaterials.OnTop(Shader()) is { } material) line.material = material;
        return line;
    }

    private static Shader? Shader() => s_shader != null ? s_shader : s_shader = UnityEngine.Shader.Find("HDRP/Unlit");

    public void Draw(IReadOnlyList<Vector3> edge, IReadOnlyList<Vector3>? blend, Color color, float width)
    {
        if (_root == null) return;
        _edge.widthMultiplier = width;
        _blend.widthMultiplier = width * BlendShare;
        Set(_edge, edge, color);
        if (blend != null)
        {
            var dim = color;
            dim.r *= 0.7f;
            dim.g *= 0.7f;
            dim.b *= 0.7f;
            Set(_blend, blend, dim);
        }
        _blend.gameObject.SetActive(blend != null);
        _root.SetActive(true);
    }

    private static void Set(LineRenderer line, IReadOnlyList<Vector3> points, Color color)
    {
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        line.startColor = color;
        line.endColor = color;
        if (line.material is { } material) material.color = color;
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
    }
}
