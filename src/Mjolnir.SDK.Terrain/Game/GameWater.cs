using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Streaming;
using SandSailorStudio.WorldGen.Deterministic;
using SSSGame;
using SSSGame.Render;
using SSSGame.Water;
using UnityEngine;
using Object = UnityEngine.Object;
using TextureMask = AwesomeTechnologies.Vegetation.Masks.TextureMask;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's still water for the model.
/// <list type="bullet">
/// <item>A lake is a biome of the world grown from its seed (<c>LakeBiome_Big</c>): a <c>BiomeLakeStampMask</c> with a flat
/// level, a shore mask and a water tile of the lake's material, and a <c>LakeWorldContextSource</c> the character's world
/// context comes from while it stands in the lake's cells. Lakes are read from the scene.</item>
/// <item>A pond is a world item of the SDK's own (<c>Mjolnir_Water</c>), invisible: its place is the pond's point at the
/// height of its surface and its scale the side of its square ÷ 100, so the game saves it and sends it to the other players.
/// The SDK reads the ponds of the whole world from the item data's buffers (every tile and cell, a few milliseconds), and
/// draws each with a copy of the lake template's water tile, its shore mask made from the model's fill, and a lake component
/// of its own on an object with a <c>Biome</c>. The game finds a lake's water by the biome of the world's cells, which a pond
/// is not: the SDK gives the character the pond's context while it stands in the pond's water and takes it off after, and a
/// hidden postfix on <c>WaterSurfaceManager.GetEnvironment</c> answers with the pond (creatures, fishing, the ground's
/// "underwater" check).</item>
/// </list>
/// </summary>
internal sealed class GameWater : IWaterSource
{
    internal const string KindName = "Mjolnir_Water";

    // The scale a pond's item keeps for the side of its square: 0.32 for 32 m.
    private const float ScalePerMetre = 0.01f;

    // How often the ponds are read from the item data while nothing changed, in seconds.
    private const float ReadSeconds = 2f;

    // How long a pond placed this session may take to show in the item data before it is taken for gone, in seconds.
    private const float PlacingSeconds = 10f;

    // The side of the lake template's water tile, in metres (water_plane at 0.63 under a tile of scale 1).
    private const float TemplateTileSize = 160f;

    private static readonly GameWater s_instance = new();
    private static ItemInfo? s_info;
    private static GameObject? s_holder;

    private readonly Dictionary<object, PondView> _ponds = [];
    private readonly Dictionary<IntPtr, LakeView> _lakes = [];
    private readonly List<IWaterBodyView> _bodies = [];
    private readonly Dictionary<object, PondWater> _water = [];
    private float _readAt = -1;
    private bool _dirty = true;
    private BiomeLakeStampMask? _template;
    private PondWater? _context;

    internal static GameWater Instance => s_instance;

    /// <summary>The hidden prefix on the item database's build: the pond's item kind goes into its list.</summary>
    public static void BeforeDatabase(Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        if (s_info == null)
        {
            ItemInfo? stone = null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].name == "Item_Stone_Raw") { stone = list[i]; break; }
            if (stone?.spawnObject == null)
            {
                SdkLog.Warn(Landscape.Platform, "No ponds can be placed: the game's base kind is missing");
                return;
            }
            s_info = MakeKind(stone);
        }
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] != null && list[i].Pointer == s_info.Pointer) return;
        list.Add(s_info);
    }

    // The pond's kind: a raw stone's world item with nothing to see, touch, pick up or strike.
    private static ItemInfo MakeKind(ItemInfo stone)
    {
        s_holder = new GameObject("Mjolnir_WaterKind");
        s_holder.SetActive(false);
        Object.DontDestroyOnLoad(s_holder);
        var prefab = Object.Instantiate(stone.spawnObject, s_holder.transform);
        prefab.name = KindName;
        foreach (var part in prefab.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<DynamicItemObject>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<PickupInteraction>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<HarvestInteraction>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<ItemSpawner>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<Pickable>(true)) Object.DestroyImmediate(part);
        foreach (var body in prefab.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var item in prefab.GetComponentsInChildren<ItemObject>(true)) item.interactions = new Il2CppSystem.Collections.Generic.List<Interaction>();
        var info = Object.Instantiate(stone);
        info.name = KindName;
        info.id = (int)(Fnv1a.OfUtf8("mjolnir.terrain/" + KindName) & 0x7FFFFFFF) | 0x40000000;
        info.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        info.spawnObject = prefab;
        foreach (var component in prefab.GetComponentsInChildren<WorldItemObject>(true))
        {
            var data = new ItemInfoQuantity { itemInfo = info };
            data.quantity = 1;
            component.data = data;
        }
        return info;
    }

    public bool IsAvailable => s_info != null && Landscape.Source is { IsAvailable: true };

    public IReadOnlyList<IWaterBodyView> Bodies
    {
        get
        {
            float now = Time.unscaledTime;
            if (_dirty || now >= _readAt)
            {
                _dirty = false;
                _readAt = now + ReadSeconds;
                Read();
            }
            return _bodies;
        }
    }

    // The ponds of the whole world from the item data, and the lakes of the scene.
    private void Read()
    {
        _bodies.Clear();
        foreach (var pond in _ponds.Values) pond.Seen = false;
        if (s_info != null && GameManagers.Get<WorldDataManager>() is { } data && Items(data) is { } items)
        {
            var descriptor = items.GetItemDescriptor(s_info);
            var tiles = data.FetchAllData();
            int cells = data.CellResolution;
            for (int t = 0; descriptor != null && t < tiles.Count; t++)
            {
                var tile = tiles[t];
                if (tile == null) continue;
                var id = tile.Id;
                for (int cy = 0; cy < cells; cy++)
                    for (int cx = 0; cx < cells; cx++)
                    {
                        int x = cx, y = cy;
                        var buffer = items.GetExistingItemsBuffer(ref id, ref x, ref y, descriptor);
                        if (buffer == null) continue;
                        int size = buffer.GetSize();
                        for (int i = 0; i < size; i++)
                        {
                            int k = i;
                            if (!buffer.IsValidIndex(ref k)) continue;
                            var at = buffer.GetPosAt(ref k);
                            float side = Math.Clamp(buffer.GetScaleAt(ref k).x / ScalePerMetre, WaterBodies.MinimumSize, WaterBodies.MaximumSize);
                            var key = KeyOf(at);
                            if (!_ponds.TryGetValue(key, out var pond)) _ponds[key] = pond = new PondView(key, at, side);
                            pond.Seen = true;
                            pond.Placing = false;
                            pond.Cell = (at.x, at.z, k);
                        }
                    }
            }
        }
        foreach (var (key, pond) in _ponds)
            if (!pond.Seen && !(pond.Placing && Time.unscaledTime < pond.PlacedAt + PlacingSeconds)) pond.Gone = true;
        foreach (var pond in _ponds.Values)
        {
            if (pond.Gone) Hide(pond.Key);
            else _bodies.Add(pond);
        }
        RemoveGone();
        // The lakes the world has spawned.
        foreach (var lake in _lakes.Values) lake.Seen = false;
        foreach (var mask in Resources.FindObjectsOfTypeAll<BiomeLakeStampMask>())
        {
            if (mask == null || !mask.gameObject.scene.IsValid() || IsOurs(mask)) continue;
            if (!_lakes.TryGetValue(mask.Pointer, out var lake)) _lakes[mask.Pointer] = lake = new LakeView(mask);
            lake.Seen = true;
            _bodies.Add(lake);
        }
        var stale = new List<IntPtr>();
        foreach (var (key, lake) in _lakes) if (!lake.Seen) stale.Add(key);
        foreach (var key in stale) _lakes.Remove(key);
    }

    private void RemoveGone()
    {
        var gone = new List<object>();
        foreach (var (key, pond) in _ponds) if (pond.Gone) gone.Add(key);
        foreach (var key in gone) _ponds.Remove(key);
    }

    private static InventoryItemDataHandler? Items(WorldDataManager data)
    {
        var handlers = data._dataHandlers;
        for (int i = 0; handlers != null && i < handlers.Count; i++)
            if (handlers[i]?.TryCast<InventoryItemDataHandler>() is { } items) return items;
        return null;
    }

    // A pond's identity: its point to the centimetre (two ponds never share one: the second would stand in the first's water).
    private static object KeyOf(Vector3 at) => ((int)MathF.Round(at.x * 100), (int)MathF.Round(at.z * 100));

    private bool IsOurs(BiomeLakeStampMask mask)
    {
        foreach (var water in _water.Values) if (water.Lake != null && water.Lake.Pointer == mask.Pointer) return true;
        return false;
    }

    public IWaterBodyView? Place(Vector3 center, float size)
    {
        if (s_info == null) return null;
        var context = ItemObjectSpawnContext.WORLD;
        int quantity = 1;
        var position = center;
        var turn = Quaternion.identity;
        var scale = Vector3.one * (size * ScalePerMetre);
        var spawned = s_info.SpawnObject(ref context, ref quantity, ref position, ref turn, ref scale, null, false);
        if (spawned == null) return null;
        var instance = spawned.GetComponentInChildren<WorldItemObject>(true)?.WorldInstance?.TryCast<InventoryItemInstance>();
        var key = KeyOf(center);
        var pond = new PondView(key, center, size) { Instance = instance, Placing = true, Seen = true, PlacedAt = Time.unscaledTime };
        _ponds[key] = pond;
        _dirty = true;
        return pond;
    }

    public void Show(IWaterBodyView body, WaterFill? fill)
    {
        if (body is not PondView pond) return;
        if (fill == null || pond.Gone)
        {
            Hide(pond.Key);
            return;
        }
        if (!_water.TryGetValue(pond.Key, out var water) || !water.IsAlive)
        {
            if (Template() is not { } template) return;
            water = PondWater.Create(template, pond);
            _water[pond.Key] = water;
        }
        water.Fill(pond, fill);
    }

    private void Hide(object key)
    {
        if (!_water.TryGetValue(key, out var water)) return;
        if (ReferenceEquals(_context, water)) LeaveContext();
        water.Destroy();
        _water.Remove(key);
    }

    // The template every pond's water is copied from: the game's lake (an asset, loaded with the game's first scene).
    private BiomeLakeStampMask? Template()
    {
        if (_template != null && _template.waterTile != null) return _template;
        _template = null;
        foreach (var mask in Resources.FindObjectsOfTypeAll<BiomeLakeStampMask>())
            if (mask != null && !mask.gameObject.scene.IsValid() && mask.waterTile != null && mask.name == "LakeBiome_Big") { _template = mask; break; }
        if (_template == null) SdkLog.Warn(Landscape.Platform, "No pond's water can be drawn: the game's lake is not loaded");
        return _template;
    }

    /// <summary>Each frame: the character is given the pond's context while it stands in its water; all forgotten without a world.</summary>
    internal void Tick()
    {
        if (!IsAvailable)
        {
            if (_water.Count == 0 && _ponds.Count == 0) return;
            LeaveContext();
            foreach (var water in _water.Values) water.Destroy();
            _water.Clear();
            _ponds.Clear();
            _lakes.Clear();
            _bodies.Clear();
            _dirty = true;
            return;
        }
        if (_water.Count == 0)
        {
            LeaveContext();
            return;
        }
        Vector3 at;
        try
        {
            var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
            if (character == null) return;
            at = character.transform.position;
        }
        catch (Exception)
        {
            return; // no session
        }
        PondWater? inside = null;
        foreach (var water in _water.Values)
            if (water.Covers(at.x, at.z)) { inside = water; break; }
        if (inside == null)
        {
            LeaveContext();
            return;
        }
        var players = GameManagers.Get<PlayerManager>();
        if (players == null) return;
        var context = inside.Context;
        if (context == null) return;
        if (ReferenceEquals(_context, inside) && players._activeWorldContext?.Pointer == context.Pointer) return;
        LeaveContext();
        players.ApplyWorldContext(context);
        _context = inside;
    }

    private void LeaveContext()
    {
        if (_context == null) return;
        var water = _context;
        _context = null;
        try
        {
            if (water.Context is { } context) GameManagers.Get<PlayerManager>()?.RemoveWorldContext(context);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A pond's context could not be taken off the character: {e.Message}");
        }
    }

    /// <summary>The pond's water at a point, for the game's own question (the hidden postfix); null where there is none.</summary>
    internal IWaterEnvironment? EnvironmentAt(float x, float z)
    {
        foreach (var water in _water.Values)
            if (water.Covers(x, z)) return water.Environment;
        return null;
    }

    // Takes a pond's item away: the one placed this session directly, one read from the data through its cell.
    private bool Remove(PondView pond)
    {
        InventoryItemInstance? instance = pond.Instance != null && !pond.Instance.Destroyed ? pond.Instance : null;
        if (instance == null && s_info != null && pond.Cell is var (x, z, index) && GameManagers.Get<WorldDataManager>() is { } data && Items(data) is { } items)
        {
            var cell = data.OpenCell(x, z, DataAccessMode.FETCH);
            if (cell != null) instance = items.GetInstance(cell, s_info.id, index, false, true)?.TryCast<InventoryItemInstance>();
            if (instance != null && KeyOf(instance.GetPosition()) is var found && !Equals(found, pond.Key)) instance = null;
        }
        if (instance == null) return false;
        bool silent = false;
        var level = InstanceDestructionLevel.SYSTEM;
        instance.Destroy(ref silent, ref level);
        pond.Gone = true;
        Hide(pond.Key);
        _dirty = true;
        return true;
    }

    /// <summary>A pond of the SDK, from the item data.</summary>
    private sealed class PondView(object key, Vector3 center, float size) : IWaterBodyView
    {
        public object Key { get; } = key;

        public Vector3 Center { get; } = center;

        public float Size { get; } = size;

        public bool IsWorldLake => false;

        public bool IsGone => Gone;

        public Rect Bounds => LandscapeArea.MakeRect(Center.x - Size / 2, Center.z - Size / 2, Size, Size);

        public bool Covers(float x, float z) => false;

        public bool Remove() => s_instance.Remove(this);

        public bool Gone { get; set; }

        public bool Seen { get; set; }

        /// <summary>Placed this session and not read back from the data yet: kept, not taken for gone.</summary>
        public bool Placing { get; set; }

        public InventoryItemInstance? Instance { get; init; }

        public float PlacedAt { get; init; }

        /// <summary>Where the data keeps it: a point of its cell and its index in that cell's buffer.</summary>
        public (float X, float Z, int Index)? Cell { get; set; }
    }

    /// <summary>A lake of the world.</summary>
    private sealed class LakeView(BiomeLakeStampMask lake) : IWaterBodyView
    {
        private readonly BiomeLakeStampMask _lake = lake;

        public object Key => _lake.Pointer;

        public Vector3 Center
        {
            get
            {
                var at = _lake.transform.position;
                at.y = _lake.GetWaterLevel();
                return at;
            }
        }

        public float Size => Math.Max(_lake._bounds.z - _lake._bounds.x, _lake._bounds.w - _lake._bounds.y);

        public bool IsWorldLake => true;

        public bool IsGone => _lake == null || !_lake.gameObject.scene.IsValid();

        public Rect Bounds
        {
            get
            {
                var b = _lake._bounds;
                return LandscapeArea.MakeRect(b.x, b.y, b.z - b.x, b.w - b.y);
            }
        }

        public bool Covers(float x, float z)
        {
            var b = _lake._bounds;
            return x >= b.x && x <= b.z && z >= b.y && z <= b.w && _lake.IsInsideWaterArea(new Vector2(x, z));
        }

        public bool Remove() => false;

        public bool Seen { get; set; }
    }

    /// <summary>A pond's water: a copy of the lake's water tile, and a lake component of its own the character swims in.</summary>
    private sealed class PondWater
    {
        private GameObject? _root;
        private WaterTile? _tile;
        private Texture2D? _mask;
        private WaterFill? _fill;

        public BiomeLakeStampMask? Lake { get; private set; }

        public PlayerWorldContext? Context { get; private set; }

        public IWaterEnvironment? Environment { get; private set; }

        public bool IsAlive => _root != null && Lake != null;

        public static PondWater Create(BiomeLakeStampMask template, PondView pond)
        {
            var water = new PondWater();
            var root = new GameObject("Mjolnir_Pond");
            root.SetActive(false);
            root.transform.position = pond.Center;
            var tile = Object.Instantiate(template.waterTile.gameObject, root.transform);
            tile.name = "Water";
            tile.SetActive(true);
            root.AddComponent<Biome>().size = pond.Size;
            var lake = root.AddComponent<BiomeLakeStampMask>();
            lake.biomeType = template.biomeType;
            var operation = new MaskStampOperation();
            var parameters = operation.maskParams;
            // Water where the mask is at most 1 - 0.5 (14 bits of fixed point): the mask is 0 in the water, 1 on land.
            parameters.threshold = 8192;
            operation.maskParams = parameters;
            lake._stampOperation = operation;
            lake.waterTile = tile.GetComponent<WaterTile>();
            var source = root.AddComponent<LakeWorldContextSource>();
            water._root = root;
            water._tile = lake.waterTile;
            water.Lake = lake;
            water._root.SetActive(true);
            water.Context = source.GetContext();
            water.Environment = lake.TryCast<IWaterEnvironment>();
            return water;
        }

        /// <summary>The water drawn and swum in over a fill: the tile across the fill's square, its shore mask from the fill's cells.</summary>
        public void Fill(PondView pond, WaterFill fill)
        {
            if (_root == null || Lake == null || _tile == null) return;
            _fill = fill;
            int n = fill.Cells;
            float side = n * fill.Step;
            var bounds = new Vector4(fill.OriginX, fill.OriginZ, fill.OriginX + side, fill.OriginZ + side);
            if (_mask == null || _mask.width != n)
            {
                if (_mask != null) Object.Destroy(_mask);
                _mask = new Texture2D(n, n, TextureFormat.RGBA32, false, true)
                {
                    name = "Mjolnir_PondMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }
            var pixels = new Color32[n * n];
            var water = new Color32(0, 0, 0, 0);
            var land = new Color32(255, 0, 0, 255);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    pixels[j * n + i] = fill.IsWet(i, j) ? water : land;
            _mask.SetPixels32(new Il2CppStructArray<Color32>(pixels));
            _mask.Apply();
            var tile = _tile.transform;
            tile.position = new Vector3(fill.OriginX + side / 2, fill.Level, fill.OriginZ + side / 2);
            tile.localScale = Vector3.one * (side / TemplateTileSize);
            _tile.AssignLandMask(_mask, fill.Level / WorldHeightScale(), bounds);
            Lake._waterLevel = fill.Level;
            Lake._bounds = bounds;
            Lake._landmask = new TextureMask { MaskTexture = _mask, TextureRect = new Rect(fill.OriginX, fill.OriginZ, side, side) };
        }

        // What a normalized height of 1 is in metres of the world, as the game sets a lake's level from its normalized height.
        private static float WorldHeightScale()
        {
            float scale = GameManagers.Get<TerrainSurfaceManager>()?.GetWorldHeight(1f) ?? 0f;
            return scale > 1f ? scale : 80f;
        }

        public bool Covers(float x, float z) => _fill is { } fill && fill.Covers(x, z);

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mask != null) Object.Destroy(_mask);
            _root = null;
            _mask = null;
            Lake = null;
            Context = null;
            Environment = null;
            _fill = null;
        }
    }
}

/// <summary>
/// The ponds' hidden patch: the game asks <c>WaterSurfaceManager.GetEnvironment</c> (its own code, not folded with others',
/// Mjolnir.CodeMap) what water is at a point by the biome of the world's cell there, which a pond is not; the SDK answers with
/// the pond's where one covers the point.
/// </summary>
[HarmonyPatch]
internal static class WaterPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(WaterSurfaceManager), nameof(WaterSurfaceManager.GetEnvironment))]
    private static void AfterEnvironment(float worldX, float worldZ, ref IWaterEnvironment __result)
    {
        try
        {
            if (GameWater.Instance.EnvironmentAt(worldX, worldZ) is { } pond) __result = pond;
        }
        catch (Exception)
        {
            // the game's own answer stands
        }
    }
}
