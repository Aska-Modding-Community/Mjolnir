using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SSSGame;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A grid's cells in the world: a decal projected down onto the ground with a copy of the game's levelling-field material
/// and a cell map of its own, on an object that outlives the scenes, hidden until placed.
/// </summary>
/// <remarks>
/// The field's shader reads one pixel per cell (a linear texture): red is the cell's state — 26 marks it in the shader's
/// own red (the material's _CellInvalidColor is not read: set to cyan in game, the cells stayed red), 77 leaves it empty
/// (only its frame), 51 shades it — and green its height, coloured from the reference colour at the reference height (the
/// vector's z) towards the limit colour away from it, on either side alike.
/// </remarks>
internal sealed class GameGrid : IGridView
{
    private const byte Marked = 26;
    private const byte Shaded = 51;
    private const byte Empty = 77;

    // How far from the reference height, as a share of the range, the shader reaches the limit colour: in game it blends
    // from about 0.3 and is whole by about 0.6, so a shade's 0…1 is laid over 0…0.6.
    private const float ShadeReach = 0.6f;

    // How far below and above the middle's ground the decal reaches, at the least, in metres.
    private const float MinimumReach = 20f;

    private static Material? s_template;
    private static Color s_near, s_far;
    private static bool s_warned;

    private readonly int _columns;
    private readonly int _rows;
    private readonly GameObject? _root;
    private readonly DecalProjector? _decal;
    private readonly Material? _material;
    private readonly Texture2D? _map;
    private readonly Il2CppStructArray<Color32>? _pixels;

    public GameGrid(int columns, int rows)
    {
        _columns = columns;
        _rows = rows;
        var template = Template();
        if (template == null) return;
        _root = new GameObject("Mjolnir_LandscapeGrid");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.SetActive(false);
        _decal = _root.AddComponent(Il2CppType.Of<DecalProjector>()).Cast<DecalProjector>();
        _material = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
        _map = new Texture2D(columns, rows, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        _pixels = new Il2CppStructArray<Color32>((long)columns * rows);
        _material.SetTexture("_GridMap", _map);
        _material.SetVector("_Size_width_height", new Vector4(columns, rows, 0, 0));
        _material.SetVector("_ReferenceHeight", new Vector4(0, 1, 0, 0));
        _decal.material = _material;
        _decal.pivot = Vector3.zero;
        if (s_layers is { } layers) _decal.decalLayerMask = layers;
    }

    private static UnityEngine.Rendering.HighDefinition.RenderingLayerMask? s_layers;

    public void Place(Vector3 center, float rotation, float cellSize)
    {
        if (_root == null || _decal == null) return;
        float width = _columns * cellSize, depth = _rows * cellSize;
        _decal.size = new Vector3(width, depth, 2 * Math.Max(MinimumReach, Math.Max(width, depth)));
        _root.transform.position = center;
        // Pitched down so the decal projects onto the ground, its x and y the grid's columns and rows; Unity turns a
        // positive yaw the other way round from LandscapeArea.Turn.
        _root.transform.rotation = Quaternion.Euler(90, -rotation, 0);
        if (!_root.activeSelf) _root.SetActive(true);
    }

    public void Paint(GridCellKind[] kinds, float[] fractions)
    {
        if (_map == null || _pixels == null) return;
        for (int i = 0; i < kinds.Length; i++)
            _pixels[i] = kinds[i] switch
            {
                GridCellKind.Shade => new Color32(Shaded, (byte)MathF.Round(fractions[i] * ShadeReach * 255), 0, 0),
                GridCellKind.Mark => new Color32(Marked, 0, 0, 0),
                _ => new Color32(Empty, 0, 0, 0),
            };
        _map.SetPixels32(_pixels);
        _map.Apply(false);
    }

    public void Colors(Color? near, Color? far)
    {
        if (_material == null) return;
        _material.SetColor("_ReferenceHeightColor", near ?? s_near);
        _material.SetColor("_LimitHeightColor", far ?? s_far);
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        if (_material != null) Object.Destroy(_material);
        if (_map != null) Object.Destroy(_map);
    }

    // The levelling field's decal material, from any of the game's cell renderers that carries it (the field's prefab is
    // loaded with the game's buildings): known by what it has, a cell map and the height colours.
    private static Material? Template()
    {
        if (s_template != null) return s_template;
        foreach (var renderer in Resources.FindObjectsOfTypeAll<FarmCropGridRenderer>())
        {
            var decal = renderer != null ? renderer.decalProjector : null;
            var material = decal != null ? decal.material : null;
            if (material == null || !material.HasProperty("_GridMap") || !material.HasProperty("_ReferenceHeightColor")) continue;
            s_template = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            s_near = material.GetColor("_ReferenceHeightColor");
            s_far = material.GetColor("_LimitHeightColor");
            s_layers = decal!.decalLayerMask;
            return s_template;
        }
        if (!s_warned)
        {
            s_warned = true;
            SdkLog.Warn(Landscape.Platform, "Grids are not drawn: the game's levelling field material was not found");
        }
        return null;
    }
}
