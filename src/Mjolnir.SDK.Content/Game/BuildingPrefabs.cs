using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Every mod building's own network prefab. The game spawns a
/// structure from its template's <c>nodeStructure</c> through Fusion, by the prefab's id in Fusion's table; a copy Fusion
/// does not know cannot be spawned. So each building gets, once per game, a copy of its base's structure prefab (the
/// carrier of its function and stages: <see cref="ModBuilding.BasedOn"/>) under an inactive object living the whole game, so
/// it never wakes; the copy takes the mod's look once (<see cref="GameBuildingModels.DressPrefab"/>), a network guid of its
/// own from the mod id and the name (<see cref="ContentIds.BuildingGuid"/>), and goes into Fusion's table.
/// <para>
/// Fusion gives prefabs ids in the order they are added and sends the id, not the guid, so every player registers the
/// buildings in the same order: by id, all of those added in <c>OnLoad</c> at once when the game first builds its item
/// database (the main menu, before any session), a building added later after them. Players of a session have the same
/// mods (the network module's check). A save keeps a structure by its template's id, not by its prefab.
/// </para>
/// </summary>
internal static class BuildingPrefabs
{
    private const string Source = GameLocalization.Source;

    private static GameObject? s_holder;
    private static readonly Dictionary<int, Made> s_prefabs = [];

    /// <summary>The building's template takes its own prefab, made (and its look given) once per game.</summary>
    public static void Use(ModBuilding building, NodeTemplate made)
    {
        if (!s_prefabs.TryGetValue(building.Id, out var known) || known.Prefab == null)
        {
            var source = made.nodeStructure;
            if (source == null)
            {
                SdkLog.Warn(Source, $"Buildings: {building}'s base has no structure prefab: it stands on the base's");
                return;
            }
            var copy = Object.Instantiate(source.gameObject, ModelGeometry.Holder(ref s_holder, "Mjolnir building prefabs", HideFlags.DontUnloadUnusedAsset).transform, false);
            copy.name = building.AssetName;
            copy.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            var network = copy.GetComponent<NetworkObject>();
            var structure = copy.GetComponent<Structure>();
            if (network == null || structure == null)
            {
                Object.Destroy(copy);
                SdkLog.Warn(Source, $"Buildings: {building}'s base '{source.name}' is no network structure: it stands on the base's prefab");
                return;
            }
            network.NetworkGuid = new NetworkObjectGuid(ContentIds.BuildingGuid(building.ModId, building.Name));
            s_prefabs[building.Id] = known = new Made(structure, network);
        }
        int version = building.Version;
        if (known.Dressed != version)
        {
            known.Dressed = version;
            GameBuildingModels.DressPrefab(known.Prefab, building);
        }
        if (made.nodeStructure == null || made.nodeStructure.Pointer != known.Prefab.Pointer) made.nodeStructure = known.Prefab;
    }

    /// <summary>The prefabs not yet in Fusion's table go in, by id.</summary>
    public static void Register()
    {
        if (s_prefabs.Values.All(m => m.Registered)) return;
        NetworkPrefabTable? table;
        try
        {
            table = NetworkProjectConfig.Global?.PrefabTable;
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Buildings: no network prefab table, mod buildings cannot be built: {e.Message}");
            return;
        }
        if (table == null) return;
        foreach (var (id, made) in s_prefabs.OrderBy(p => p.Key))
        {
            if (made.Registered || made.Prefab == null) continue;
            made.Registered = true;
            var guid = made.Network.NetworkGuid;
            var name = made.Prefab.name;
            try
            {
                if (table.TryGetId(guid, out var taken))
                {
                    SdkLog.Error(Source, $"Buildings: the network guid of {name} ({guid}) is taken (prefab {taken.Value}): it cannot be built");
                    continue;
                }
                var source = new NetworkPrefabSourceStatic { PrefabReference = made.Network };
                if (table.TryAdd(guid, source.Cast<INetworkPrefabSource>(), out var prefabId))
                    SdkLog.Info(Source, $"Buildings: {name} has its own network prefab {prefabId.Value}");
                else SdkLog.Error(Source, $"Buildings: Fusion's table refused {name}'s prefab: it cannot be built");
            }
            catch (Exception e)
            {
                SdkLog.Error(Source, $"Buildings: {name}'s prefab could not be registered: {e.Message}");
            }
        }
    }

    private sealed class Made(Structure prefab, NetworkObject network)
    {
        public Structure Prefab { get; } = prefab;
        public NetworkObject Network { get; } = network;
        public int Dressed { get; set; } = int.MinValue;
        public bool Registered { get; set; }
    }
}
