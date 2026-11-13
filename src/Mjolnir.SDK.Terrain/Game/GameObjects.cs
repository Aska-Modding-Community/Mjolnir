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

    /// <summary>The objects whose place lies within <paramref name="radius"/> metres of a point.</summary>
    public static IReadOnlyList<IWorldObject> In(float centerX, float centerZ, float radius)
    {
        var resources = GameManagers.Get<ResourceManager>();
        if (resources == null) return [];
        var middle = At(centerX, 0, centerZ);
        TerrainSampler.TrySampleHeight(ref middle, out float ground);
        middle.y = ground;
        var found = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
        resources.GetAllInstancesList(found, middle, Quaternion.identity, At(radius * 2, SearchHeight, radius * 2), SDFShape.CIRCLE,
            null, InstanceActivationContext.ALL, false);
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
            if (instance == null || instance.Destroyed) continue;
            var position = instance.GetPosition();
            if (!where(position.x, position.z) || !instance.IsRemovable(InstanceDestructionLevel.TERRAFORMING)) continue;
            bool silent = false;
            var level = InstanceDestructionLevel.TERRAFORMING;
            instance.Destroy(ref silent, ref level);
        }
    }

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
            : instance.TryCast<InventoryItemInstance>() != null ? NaturalObjectKind.Item
            : NaturalObjectKind.Other;

        public Vector3 Position => instance.GetPosition();

        public float Health => _biome != null ? _biome.GetHealth() : 0f;

        public int Quantity => _biome != null ? _biome.GetQuantity() : 0;

        public string ResourceName => (_biome != null ? _biome.GetResourceInfo()?.itemInfo?.name : instance.Descriptor?.itemInfo?.name) ?? "";

        public bool IsRemovable => instance.IsRemovable(InstanceDestructionLevel.TERRAFORMING);

        public bool IsHarvestable => instance.Descriptor?.TryCast<BiomeItemDescriptor>()?.IsHarvestable ?? false;

        // The game keeps the destroyed mark in the instance's own copy of the data, which a handle that was taken out of
        // the world no longer follows: what this mod took is remembered here (seen in game: right after a nest was taken
        // the game's own mark was still false, though the nest was gone).
        public bool IsGone => _taken || instance == null || instance.Destroyed;

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

        public bool Remove()
        {
            if (IsGone) return true;
            if (!IsRemovable) return false;
            bool silent = false;
            var level = InstanceDestructionLevel.TERRAFORMING;
            instance.Destroy(ref silent, ref level);
            _taken = true;
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
