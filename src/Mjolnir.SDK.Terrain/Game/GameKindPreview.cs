using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A preview of a kind in the world: plain copies of its model's nearest level (meshes and materials, no colliders, no
/// scripts, no shadows) under a root that outlives the scenes; a blocked one wears one unlit red material instead.
/// </summary>
internal sealed class GameKindPreview : IKindPreviewView
{
    private static readonly Color BlockedColor = new(0.85f, 0.15f, 0.1f, 1f);
    private static Material? s_blocked;

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
            if (blocked) Paint(true);
        }
        else if (blocked != _blocked) Paint(blocked);
        var t = _root.transform;
        t.position = position;
        t.rotation = Quaternion.Euler(0, rotation, 0);
        t.localScale = Vector3.one * scale;
        if (!_root.activeSelf) _root.SetActive(true);
    }

    // Blocked, every slot of every part takes the red material; freed, the parts are copied from the model again.
    private void Paint(bool blocked)
    {
        if (_root == null) return;
        _blocked = blocked;
        if (blocked)
        {
            s_blocked ??= Blocked();
            if (s_blocked == null) return;
            foreach (var renderer in _root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = renderer.sharedMaterials;
                var red = new Material[slots.Length];
                for (int i = 0; i < red.Length; i++) red[i] = s_blocked!;
                renderer.sharedMaterials = red;
            }
            return;
        }
        // Freed: made again from the model, its own look back.
        var kind = _kind;
        var transform = _root.transform;
        var (position, rotation, scale) = (transform.position, transform.eulerAngles.y, transform.localScale.x);
        Destroy();
        if (kind != null) Show(kind, position, rotation, scale, false);
    }

    private static Material? Blocked()
    {
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null) return null;
        var material = new Material(shader) { color = BlockedColor };
        material.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return material;
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _kind = null;
        _blocked = false;
    }
}
