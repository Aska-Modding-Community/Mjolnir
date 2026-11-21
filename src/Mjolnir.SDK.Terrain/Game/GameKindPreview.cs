using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A preview of a kind in the world: plain copies of its model's nearest level (meshes and materials, no colliders, no
/// scripts, no shadows) under a root that outlives the scenes; a blocked one wears the game's see-through red instead (the
/// red it lays on what keeps a building from its place), and its own again once freed.
/// </summary>
internal sealed class GameKindPreview : IKindPreviewView
{
    private static Material? s_blocked;

    private readonly List<(MeshRenderer Renderer, Material[] Materials)> _parts = [];
    private GameObject? _root;
    private string? _kind;
    private bool _blocked;

    public void Show(string kind, Vector3 position, float rotation, float scale, bool blocked)
    {
        if (_root == null || kind != _kind)
        {
            Destroy();
            if (GamePlacement.ModelOf(kind) is not { } model) return;
            _root = new GameObject("Mjolnir_KindPreview");
            Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
            if (!KindPictures.Copy(model, _root.transform, 0, out _))
            {
                Destroy();
                return;
            }
            _kind = kind;
            _blocked = false;
            foreach (var renderer in _root.GetComponentsInChildren<MeshRenderer>(true)) _parts.Add((renderer, renderer.sharedMaterials));
            if (blocked) Paint(true);
        }
        else if (blocked != _blocked) Paint(blocked);
        var t = _root.transform;
        t.position = position;
        t.rotation = Quaternion.Euler(0, rotation, 0);
        t.localScale = Vector3.one * scale;
        if (!_root.activeSelf) _root.SetActive(true);
    }

    // Blocked, every slot of every part takes the red material; freed, each part takes its own back.
    private void Paint(bool blocked)
    {
        if (blocked && (s_blocked ??= OverlayMaterials.Invalid()) == null) return;
        _blocked = blocked;
        foreach (var (renderer, materials) in _parts)
        {
            if (renderer == null) continue;
            if (!blocked)
            {
                renderer.sharedMaterials = materials;
                continue;
            }
            var red = new Material[materials.Length];
            for (int i = 0; i < red.Length; i++) red[i] = s_blocked!;
            renderer.sharedMaterials = red;
        }
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _parts.Clear();
        _kind = null;
        _blocked = false;
    }
}
