using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// What the mods' models in the game share: where a model's meshes lie (read from the meshes: the renderers of an object
/// that never woke have no bounds), a box collider fitted round them, a layer for a whole model, and the inactive holder its
/// copies live under for the whole game, so they never wake.
/// </summary>
internal static class ModelGeometry
{
    /// <summary>The corners of the meshes under an object, in the world: its mesh filters', and its skinned meshes' too when asked.</summary>
    public static List<Vector3> Corners(GameObject root, bool skinned = false)
    {
        var points = new List<Vector3>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) AddCorners(points, filter.sharedMesh, filter.transform);
        if (skinned)
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) AddCorners(points, renderer.sharedMesh, renderer.transform);
        }
        return points;
    }

    /// <summary>The world bounds of the meshes under an object (its skinned meshes too); null without a mesh.</summary>
    public static Bounds? Bounds(GameObject root)
    {
        Bounds? all = null;
        foreach (var corner in Corners(root, skinned: true))
        {
            if (all is { } b)
            {
                b.Encapsulate(corner);
                all = b;
            }
            else all = new Bounds(corner, default);
        }
        return all;
    }

    /// <summary>Both bounds in one; either may be none.</summary>
    public static Bounds? Union(Bounds? all, Bounds? more)
    {
        if (more is not { } m) return all;
        if (all is not { } a) return m;
        a.Encapsulate(m);
        return a;
    }

    /// <summary>A box collider round world bounds with <paramref name="room"/> on every side, in its own space.</summary>
    public static void Fit(BoxCollider box, Bounds bounds, float room)
    {
        var t = box.transform;
        var scale = t.lossyScale;
        box.center = t.InverseTransformPoint(bounds.center);
        var size = bounds.size + new Vector3(room, room, room) * 2;
        box.size = new Vector3(size.x / Safe(scale.x), size.y / Safe(scale.y), size.z / Safe(scale.z));
    }

    /// <summary>An object and everything under it on one layer.</summary>
    public static void SetLayer(Transform transform, int layer)
    {
        transform.gameObject.layer = layer;
        for (int i = 0; i < transform.childCount; i++) SetLayer(transform.GetChild(i), layer);
    }

    /// <summary>
    /// The inactive object kept in <paramref name="holder"/>, made at the first call with <paramref name="flags"/>: what lies
    /// under it never wakes, and it outlives every scene.
    /// </summary>
    public static GameObject Holder(ref GameObject? holder, string name, HideFlags flags)
    {
        if (holder != null) return holder;
        holder = new GameObject(name) { hideFlags = flags };
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        return holder;
    }

    private static void AddCorners(List<Vector3> points, Mesh? mesh, Transform transform)
    {
        if (mesh == null) return;
        var bounds = mesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            points.Add(transform.TransformPoint(corner));
        }
    }

    private static float Safe(float value) => Mathf.Abs(value) < 0.0001f ? 1f : Mathf.Abs(value);
}
