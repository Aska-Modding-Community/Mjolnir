using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using SandSailorStudio;
using SandSailorStudio.Render;
using SandSailorStudio.TerrainTools;
using SSSGame;
using UnityEngine;
using BiomeType = AwesomeTechnologies.VegetationSystem.VegetationType;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The world's natural objects for the model: found through the game's resource manager, told apart by their descriptor's
/// vegetation info, taken away as the game's ground tool takes them, and moved by their place in the vegetation data with
/// their look brought after them.
/// </summary>
internal static class GameObjects
{
    /// <summary>How tall a box is searched round a point, in metres: the whole height of the world.</summary>
    private const float SearchHeight = 400f;

    /// <summary>How long an object taken by the SDK counts as gone whatever the game says of it, in seconds.</summary>
    private const float TakenFor = 60f;

    // The objects the SDK took away lately, by their place in the vegetation data (an item of the world: by its own
    // object), with where they stood. The game marks an object destroyed only in its own copy of the data, so a handle
    // made for it by the next search still says it stands: a brush writing ten times a second took the same bush and the
    // same felled tree again and again, and the game, asked to remove an object no longer in its data, failed
    // ("RemoveInstanceData with index out of range") and soon hung (seen by the user, 2026-11-19).
    private static readonly Dictionary<(IntPtr Data, int Index), (Vector3 At, float When)> s_taken = [];

    private static (IntPtr Data, int Index) KeyOf(WorldItemInstance instance)
    {
        var biome = instance.TryCast<BiomeItemInstance>();
        return biome?.GetBuffer() is { } buffer ? (buffer.Pointer, biome.Index) : (instance.Pointer, -1);
    }

    /// <summary>Whether the SDK took this object away lately (the same place in the data, the same spot).</summary>
    private static bool WasTaken(WorldItemInstance instance)
    {
        if (s_taken.Count == 0 || !s_taken.TryGetValue(KeyOf(instance), out var taken)) return false;
        if (Time.realtimeSinceStartup - taken.When > TakenFor) return false;
        var at = instance.GetPosition();
        return (at - taken.At).sqrMagnitude < 0.01f; // a place in the data given to another object since is not it
    }

    // An object as it is before it is taken: its place in the data and where it stands. Read before the game takes it: the
    // game moves the data's last object into the place it frees, so read after, the place and the spot were another
    // object's, which then counted as taken for a minute — a shore's boulder beside one taken could be neither marked nor
    // taken by the eraser (seen in game, 2026-11-21).
    private static ((IntPtr Data, int Index) Key, Vector3 At) Before(WorldItemInstance instance) => (KeyOf(instance), instance.GetPosition());

    private static void MarkTaken(((IntPtr Data, int Index) Key, Vector3 At) gone)
    {
        float now = Time.realtimeSinceStartup;
        if (s_taken.Count > 2048)
        {
            var old = new List<(IntPtr, int)>();
            foreach (var (key, taken) in s_taken)
                if (now - taken.When > TakenFor) old.Add(key);
            foreach (var key in old) s_taken.Remove(key);
        }
        s_taken[gone.Key] = (gone.At, now);
    }

    /// <summary>
    /// The objects whose place lies within <paramref name="radius"/> metres of a point; with <paramref name="byFootprint"/>,
    /// also those whose footprint reaches into the circle (the vegetation kind's footprint, turned and sized as the object
    /// is: the game's own search tests it when asked to). A shore's large boulder stands some metres from its place.
    /// </summary>
    public static IReadOnlyList<IWorldObject> In(float centerX, float centerZ, float radius, bool byFootprint = false)
    {
        var resources = GameManagers.Get<ResourceManager>();
        if (resources == null) return [];
        var middle = At(centerX, 0, centerZ);
        TerrainSampler.TrySampleHeight(ref middle, out float ground);
        middle.y = ground;
        var found = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
        resources.GetAllInstancesList(found, middle, Quaternion.identity, At(radius * 2, SearchHeight, radius * 2), SDFShape.CIRCLE,
            null, InstanceActivationContext.ALL, byFootprint);
        var list = new List<IWorldObject>(found.Count);
        for (int i = 0; i < found.Count; i++)
        {
            var instance = found[i];
            if (instance != null && !instance.Destroyed) list.Add(new GameObjectHandle(instance));
        }
        return list;
    }

    /// <summary>
    /// Takes the objects a change of the ground takes, where <paramref name="where"/> says, as the game's tool does: its own
    /// filter and the level of a change of the ground, so trees and boulders stay.
    /// </summary>
    public static void Remove(float centerX, float centerZ, float radius, Func<float, float, bool> where)
    {
        var resources = GameManagers.Get<ResourceManager>();
        if (resources == null) return;
        var middle = At(centerX, 0, centerZ);
        TerrainSampler.TrySampleHeight(ref middle, out float ground);
        middle.y = ground;
        var found = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
        var filter = new HeightmapTool.RemovableInstanceFilter().BoxIl2CppObject().Cast<IWorldItemInstanceFilter>();
        resources.GetAllInstancesList(found, middle, Quaternion.identity, At(radius * 2, SearchHeight, radius * 2), SDFShape.BOX,
            filter, InstanceActivationContext.RESOURCE_FINDER, false);
        for (int i = found.Count - 1; i >= 0; i--)
        {
            var instance = found[i];
            if (instance == null || instance.Destroyed || WasTaken(instance)) continue;
            var position = instance.GetPosition();
            if (!where(position.x, position.z) || !instance.IsRemovable(InstanceDestructionLevel.TERRAFORMING)) continue;
            bool silent = false;
            var level = InstanceDestructionLevel.TERRAFORMING;
            var before = Before(instance);
            instance.Destroy(ref silent, ref level);
            MarkTaken(before);
        }
    }

    /// <summary>The world's items of one name lying within <paramref name="radius"/> metres of a point.</summary>
    public static List<InventoryItemInstance> Items(Vector3 center, float radius, string name)
    {
        var items = new List<InventoryItemInstance>();
        var resources = GameManagers.Get<ResourceManager>();
        if (resources == null) return items;
        var found = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
        resources.GetAllInstancesList(found, center, Quaternion.identity, At(radius * 2, SearchHeight, radius * 2), SDFShape.CIRCLE,
            null, InstanceActivationContext.ALL, false);
        for (int i = 0; i < found.Count; i++)
        {
            var item = found[i]?.TryCast<InventoryItemInstance>();
            if (item != null && !item.Destroyed && item.Descriptor?.itemInfo?.name == name) items.Add(item);
        }
        return items;
    }

    /// <summary>One object of the world as the model sees it.</summary>
    public static IWorldObject Wrap(WorldItemInstance instance) => new GameObjectHandle(instance);

    private static Vector3 At(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    /// <summary>One object of the world, read and changed through the game.</summary>
    private sealed class GameObjectHandle(WorldItemInstance instance) : IWorldObject
    {
        private readonly BiomeItemInstance? _biome = instance.TryCast<BiomeItemInstance>();
        private bool _taken;

        public string AssetName => Info()?.Name ?? instance.Descriptor?.itemInfo?.name ?? "";

        public NaturalObjectKind Kind => Info() is { } info ? KindOf(info.VegetationType)
            : instance.TryCast<InventoryItemInstance>() != null ? GamePlacement.KindOf(ItemName) ?? NaturalObjectKind.Item
            : NaturalObjectKind.Other;

        // A world item's kind name: a placed object's is the SDK's own (GamePlacement).
        private string ItemName => instance.Descriptor?.itemInfo?.name ?? "";

        public Vector3 Position => instance.GetPosition();

        public Quaternion Rotation => instance.GetRotation();

        public Vector3 Scale => instance.GetScale();

        public float Health => _biome != null ? _biome.GetHealth() : 0f;

        public int Quantity => _biome != null ? _biome.GetQuantity() : 0;

        public string ResourceName => (_biome != null ? _biome.GetResourceInfo()?.itemInfo?.name : GamePlacement.ResourceOf(ItemName) ?? instance.Descriptor?.itemInfo?.name) ?? "";

        public bool IsRemovable => instance.IsRemovable(InstanceDestructionLevel.TERRAFORMING);

        public bool IsHarvestable => instance.Descriptor?.TryCast<BiomeItemDescriptor>()?.IsHarvestable ?? GamePlacement.IsHarvestable(ItemName);

        // The game keeps the destroyed mark in the instance's own copy of the data, which a handle that was taken out of
        // the world no longer follows: what this mod took is remembered here (seen in game: right after a nest was taken
        // the game's own mark was still false, though the nest was gone).
        public bool IsGone => _taken || instance == null || instance.Destroyed || WasTaken(instance);

        public int GatherCount => Gathering() is { } gather ? gather.CheckAvailableItemCount() : 0;

        // A charge of what the player picks by hand: the items fall beside the object, and the game tells the other
        // players itself (the interaction is the game's own).
        public int Gather(int charges)
        {
            if (Gathering() is not { } gather) return 0;
            var agent = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.GetComponentInChildren<PlayerInteractionAgent>(true)?.TryCast<IInteractionAgent>();
            return gather.DropItemsCharge(agent, charges);
        }

        // The game's gathering interaction on the object, where it has one (a bush, a nest, driftwood).
        private GatherInteraction? Gathering() =>
            !IsGone && instance.gameObject != null ? instance.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;

        public bool IsClearable => instance.IsRemovable(InstanceDestructionLevel.SYSTEM);

        // The game's own level takes a tree and a boulder too, dropping nothing (seen in game: a willow and a shoreline
        // boulder gone at once, still gone after a save and a load).
        public bool Clear()
        {
            if (IsGone) return true;
            if (!IsClearable) return false;
            bool silent = false;
            var level = InstanceDestructionLevel.SYSTEM;
            var before = Before(instance);
            instance.Destroy(ref silent, ref level);
            _taken = true;
            MarkTaken(before);
            return true;
        }

        // The game's own harvest, blow by blow (GameHarvest); a felled tree's trunk is chopped once it comes up.
        public bool Harvest(HarvestOptions options)
        {
            if (IsGone) return true;
            var harvest = instance.gameObject != null ? instance.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
            if (harvest == null) return false;
            string resource = ResourceName;
            var at = instance.GetPosition();
            bool tree = Kind == NaturalObjectKind.Tree && options.ChopTrunk && resource.Length > 0;
            var lying = tree ? GameHarvest.TrunksAround(at, resource) : null;
            var before = Before(instance);
            bool done = GameHarvest.Strike(harvest, options);
            if (!done) return false;
            _taken = true; // used up: the game takes it (seen in game: a felled tree's place held only its loot)
            MarkTaken(before);
            if (lying != null) GameHarvest.ChopTrunk(at, resource, lying, options);
            return true;
        }

        public bool Remove()
        {
            if (IsGone) return true;
            if (!IsRemovable) return false;
            bool silent = false;
            var level = InstanceDestructionLevel.TERRAFORMING;
            var before = Before(instance);
            instance.Destroy(ref silent, ref level);
            _taken = true;
            MarkTaken(before);
            return true;
        }

        // The place lives in the vegetation data, which the look does not follow by itself: the object's activation contexts
        // are taken away and given back, which makes the game draw it anew where its data now says (its own refresh does not
        // move it). An object without one of its own (grass, drawn in its thousands) needs only the data.
        public bool MoveTo(Vector3 position)
        {
            if (IsGone) return false;
            if (_biome == null || _biome.GetBuffer()?.instances?.positions is not { } positions) return false;
            int index = _biome.Index;
            if (index < 0 || index >= positions.Length) return false;
            var place = positions[index];
            place.x = position.x;
            place.y = position.y;
            place.z = position.z;
            positions[index] = place;
            int contexts = _biome.GetContexts();
            var held = new List<InstanceActivationContext>();
            foreach (var context in Contexts)
                if ((contexts & (int)context) != 0) held.Add(context);
            foreach (var context in held) _biome.DeactivateContext(context);
            foreach (var context in held) _biome.ActivateContext(context);
            if (_biome.gameObject != null)
            {
                var transform = _biome.gameObject.transform;
                var shown = transform.position;
                if (Math.Abs(shown.y - position.y) > 0.01f || Math.Abs(shown.x - position.x) > 0.01f || Math.Abs(shown.z - position.z) > 0.01f)
                    transform.position = position;
            }
            return true;
        }

        private static readonly InstanceActivationContext[] Contexts =
        [
            InstanceActivationContext.AREA, InstanceActivationContext.REQUEST, InstanceActivationContext.NETWORK,
            InstanceActivationContext.PROCESS, InstanceActivationContext.PICKING, InstanceActivationContext.FORCE_ACTIVE,
            InstanceActivationContext.VFX, InstanceActivationContext.UPDATE_DATA, InstanceActivationContext.SPELL,
            InstanceActivationContext.PREDICTIVE_SPAWN, InstanceActivationContext.RESOURCE_FINDER,
        ];

        private AwesomeTechnologies.VegetationSystem.VegetationItemInfoPro? Info() =>
            instance.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo();

        private static NaturalObjectKind KindOf(BiomeType type) => type switch
        {
            BiomeType.Grass => NaturalObjectKind.Grass,
            BiomeType.Plant => NaturalObjectKind.Plant,
            BiomeType.Tree => NaturalObjectKind.Tree,
            BiomeType.Objects => NaturalObjectKind.Object,
            BiomeType.LargeObjects => NaturalObjectKind.LargeObject,
            _ => NaturalObjectKind.Other,
        };
    }
}
