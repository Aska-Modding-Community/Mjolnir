using System;
using System.Collections.Generic;
using SandSailorStudio;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Marks in the world: for each object a copy of its kind's model (its nearest level, no colliders or scripts) a little
/// larger than the object, wearing the game's own see-through red (<see cref="OverlayMaterials.Invalid"/>), on a root that
/// outlives the scenes. A copy stays while its object stays marked and moves with it.
/// </summary>
internal sealed class GameMarks : IMarksView
{
    /// <summary>A mark is this much larger than its object, so it shows over the object's own faces.</summary>
    private const float Grow = 1.04f;

    private readonly Dictionary<(string, int, int, int), GameObject> _shown = [];
    private readonly HashSet<(string, int, int, int)> _wanted = [];
    private GameObject? _root;

    public void Show(IReadOnlyList<MarkedObject> objects)
    {
        if (_root == null)
        {
            _root = new GameObject("Mjolnir_ObjectMarks");
            Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
        }
        if (!_root.activeSelf) _root.SetActive(true);
        _wanted.Clear();
        foreach (var marked in objects)
        {
            var key = Key(marked);
            _wanted.Add(key);
            if (!_shown.TryGetValue(key, out var mark) || mark == null)
            {
                mark = Make(marked);
                if (mark == null) continue;
                _shown[key] = mark;
            }
            var t = mark.transform;
            t.position = marked.Position;
            t.rotation = marked.Rotation;
            t.localScale = marked.Scale * Grow;
        }
        List<(string, int, int, int)>? gone = null;
        foreach (var key in _shown.Keys)
            if (!_wanted.Contains(key)) (gone ??= []).Add(key);
        if (gone == null) return;
        foreach (var key in gone)
        {
            if (_shown[key] != null) Object.Destroy(_shown[key]);
            _shown.Remove(key);
        }
    }

    // An object's mark by its kind and its place, to the centimetre.
    private static (string, int, int, int) Key(MarkedObject marked) =>
        (marked.AssetName, (int)MathF.Round(marked.Position.x * 100), (int)MathF.Round(marked.Position.y * 100), (int)MathF.Round(marked.Position.z * 100));

    private GameObject? Make(MarkedObject marked)
    {
        string name = marked.AssetName.StartsWith(GamePlacement.Prefix, StringComparison.Ordinal) ? marked.AssetName[GamePlacement.Prefix.Length..] : marked.AssetName;
        if (GamePlacement.ModelOf(name) is not { } model) return null;
        var mark = new GameObject("Mark");
        mark.hideFlags = HideFlags.HideAndDontSave;
        mark.transform.SetParent(_root!.transform, false);
        // The copy's parts stand where they stand about the model's root, at the root's own place.
        mark.transform.position = Vector3.zero;
        if (!KindPictures.Copy(model, mark.transform, 0, out _))
        {
            Object.Destroy(mark);
            return null;
        }
        var red = OverlayMaterials.Invalid();
        if (red != null)
            foreach (var renderer in mark.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = red;
                renderer.sharedMaterials = slots;
            }
        return mark;
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _shown.Clear();
    }
}

/// <summary>Materials drawn over the world: the game's red for what is in the way, lines that show through everything.</summary>
internal static class OverlayMaterials
{
    private static Material? s_invalid;
    private static bool s_looked;

    /// <summary>
    /// The game's see-through red (`construction_invalid`), which it lays over the resources that keep a building from its
    /// place (the preview manager's highlight); a plain red when the game has none.
    /// </summary>
    public static Material? Invalid()
    {
        if (s_invalid != null) return s_invalid;
        if (!s_looked)
        {
            s_looked = true;
            try
            {
                var highlight = GameManagers.Get<PreviewManager>()?._invalidResourceHighlight;
                var found = highlight != null ? highlight.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial : null;
                if (found != null)
                {
                    s_invalid = new Material(found) { name = "Mjolnir_Invalid" };
                    s_invalid.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    return s_invalid;
                }
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"The game's red for what is in the way was not found: {e.Message}");
            }
        }
        if (s_invalid == null && Shader.Find("HDRP/Unlit") is { } unlit)
        {
            s_invalid = new Material(unlit) { color = new Color(0.85f, 0.15f, 0.1f, 1f), name = "Mjolnir_InvalidPlain" };
            s_invalid.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }
        return s_invalid;
    }

    /// <summary>The game was not ready when the red was first asked for: look again next time.</summary>
    public static void Forget()
    {
        if (s_invalid != null && s_invalid.name == "Mjolnir_InvalidPlain") s_invalid = null;
        s_looked = false;
    }

    /// <summary>
    /// An unlit material that draws through whatever stands in front of it (depth test always passes): an outline on the
    /// ground stays whole over a rock or a tree in its way.
    /// </summary>
    public static Material? OnTop(Shader? unlit)
    {
        if (unlit == null) return null;
        var m = new Material(unlit);
        m.SetFloat("_ZWrite", 0);
        m.SetFloat("_TransparentZWrite", 0);
        m.SetFloat("_ZTestTransparent", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.SetFloat("_ZTestDepthEqualForOpaque", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.SetFloat("_SurfaceType", 1);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetShaderPassEnabled("DepthForwardOnly", false);
        m.SetShaderPassEnabled("MotionVectors", false);
        return m;
    }
}
