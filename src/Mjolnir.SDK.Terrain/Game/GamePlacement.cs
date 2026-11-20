using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SSSGame;
using SSSGame.Render;
using UnityEngine;
using Object = UnityEngine.Object;
using VegetationInfo = AwesomeTechnologies.VegetationSystem.VegetationItemInfoPro;
using VegetationPackage = AwesomeTechnologies.VegetationSystem.VegetationPackagePro;
using VegetationType = AwesomeTechnologies.VegetationSystem.VegetationType;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The kinds of the world a mod can place, as world items of the SDK's own. The world's trees, bushes and stones are the
/// vegetation system's, grown from the world's seed: the save keeps only their state, so a new one cannot be saved. A world
/// item is saved and sent to the other players, so each kind of the vegetation system (but its grass) gets an item kind
/// of the SDK's own, put in the game's item database as the game builds it (the main menu's and every world's), before a
/// save's world items come up:
/// <list type="bullet">
/// <item>a kind the game harvests or gathers (a tree, a bush, a stone clump) is a copy of its game object — its harvest or
/// gathering, its colliders, its data events, its spawners — with a plain resource object in the place of its biome object
/// (which would take it for vegetation), and the vegetation system's model added, since the world draws a tree with that
/// system and its object has no model of its own; a tree also takes its trunk kind's stump model, shown once the tree is
/// felled. Only the resource object takes the new kind: the object's other item components keep the game's data, which its
/// spawners lay as the game's do — a felled tree's trunk, lying, a clump's stone, a stump's firewood;</item>
/// <item>a kind of scenery (a boulder, a plant) is a raw stone's world item stripped of its look, pick-up and harvest, with
/// the kind's model and its colliders.</item>
/// </list>
/// The item kind is a copy of a world item kind of the game (a tree's own trunk kind, whose parts and points are the tree's,
/// else a gathered growth's or the raw stone's), given the world kind's harvest data, components (what it holds) and what
/// its gathering gives. Its id is worked out from its name, the same on every machine.
/// </summary>
internal static class GamePlacement
{
    /// <summary>The start of the item kinds' names: "Mjolnir_World_tree_willow".</summary>
    internal const string Prefix = "Mjolnir_World_";

    /// <summary>How many items a placed bush gives a charge, as the world's bushes do (their quantity is 3).</summary>
    private const int GatheredCount = 3;

    private static readonly Dictionary<string, Made> s_byName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Made> s_byAsset = new(StringComparer.Ordinal);
    private static readonly List<PlaceableKindData> s_kinds = [];
    // The game's item kind whose texts a placed kind shows, by the placed kind's object: the game builds a kind's keys from
    // its asset's name, which is the SDK's own.
    private static readonly Dictionary<IntPtr, ItemInfo> s_textsOf = [];
    private static GameObject? s_holder;
    private static bool s_made;

    /// <summary>The kinds made, in the order the vegetation system lists them.</summary>
    public static IReadOnlyList<PlaceableKindData> Kinds => s_kinds;

    /// <summary>The hidden prefix: the game is about to build its item database from its list.</summary>
    public static void BeforeDatabase(ItemInfoDatabase database)
    {
        var list = database.CompleteItemInfoList?.itemInfoList;
        if (list == null) return;
        if (!s_made)
        {
            s_made = true;
            Make(list);
        }
        GameRoads.BeforeDatabase(list);
        foreach (var made in s_byName.Values)
            if (IndexOf(list, made.Info) < 0) list.Add(made.Info);
    }

    /// <summary>The name the game gives the world's objects of a kind, in the player's language; null for none.</summary>
    public static string? DisplayName(string name)
    {
        if (!s_byName.TryGetValue(name, out var made) || made.Source == null) return null;
        string? text = made.Source.Name;
        // An item kind without a translation answers with its asset's name ("Item_…"): that is no name for a player.
        return string.IsNullOrWhiteSpace(text) || text.StartsWith("Item_", StringComparison.Ordinal) || text == made.Source.name ? null : text;
    }

    /// <summary>The vegetation system's model of a kind; null for an unknown kind.</summary>
    public static GameObject? ModelOf(string name) => s_byName.TryGetValue(name, out var made) ? made.Model : null;

    /// <summary>A picture of a kind's model; null for an unknown kind or one without a model.</summary>
    public static Texture2D? Picture(string name, int width, int height) =>
        s_byName.TryGetValue(name, out var made) && made.Model != null ? KindPictures.Of(name, made.Model, width, height) : null;

    /// <summary>Places a world item of a kind; the instance the game made of it, or null.</summary>
    public static IWorldObject? Place(string name, Vector3 position, float rotation, float scale)
    {
        if (!s_byName.TryGetValue(name, out var made) || made.Info == null) return null;
        var context = ItemObjectSpawnContext.WORLD;
        // A gathered kind comes up full: its stack's count is what its gathering gives (a sloe bush 5 berries).
        int quantity = made.Data.IsGathered && made.Info.TryCast<VegetationResourceInfo>()?.overridenGatherResource is { quantity: > 0 } gathered ? gathered.quantity : 1;
        var turn = Quaternion.Euler(0, rotation, 0);
        var size = Vector3.one * scale;
        var spawned = made.Info.SpawnObject(ref context, ref quantity, ref position, ref turn, ref size, null, false);
        if (spawned == null) return null;
        var instance = spawned.GetComponentInChildren<WorldItemObject>(true)?.WorldInstance;
        return instance != null ? GameObjects.Wrap(instance) : null;
    }

    /// <summary>The kind of a placed object by its item kind's name; null for anything else.</summary>
    public static NaturalObjectKind? KindOf(string assetName) => s_byAsset.TryGetValue(assetName, out var made) ? made.Data.Kind : null;

    /// <summary>What a placed object gives (a tree its trunk, a bush its berries); null for anything else.</summary>
    public static string? ResourceOf(string assetName) => s_byAsset.TryGetValue(assetName, out var made) ? made.Resource : null;

    /// <summary>Whether a placed object is felled or mined.</summary>
    public static bool IsHarvestable(string assetName) => s_byAsset.TryGetValue(assetName, out var made) && made.Data.IsHarvestable;

    /// <summary>A placed kind by its item kind's name; null for anything else.</summary>
    internal static Made? Find(string? assetName) => assetName != null && s_byAsset.TryGetValue(assetName, out var made) ? made : null;

    private static void Make(Il2CppSystem.Collections.Generic.List<ItemInfo> list)
    {
        var bySpawn = new Dictionary<IntPtr, ItemInfo>();
        var byName = new Dictionary<string, ItemInfo>(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        for (int i = 0; i < list.Count; i++)
        {
            var info = list[i];
            if (info == null) continue;
            ids.Add(info.id);
            byName.TryAdd(info.name, info);
            if (info.spawnObject != null) bySpawn.TryAdd(info.spawnObject.Pointer, info);
        }
        if (!byName.TryGetValue("Item_Food_CaveFingersGrowth", out var growth) || !byName.TryGetValue("Item_Stone_Raw", out var stone))
        {
            SdkLog.Warn(Landscape.Platform, "No kinds of the world to place: the game's base kinds are missing");
            return;
        }
        s_holder = new GameObject("Mjolnir_WorldKinds");
        s_holder.SetActive(false);
        Object.DontDestroyOnLoad(s_holder);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int failed = 0;
        foreach (var package in Resources.FindObjectsOfTypeAll<VegetationPackage>())
        {
            var infos = package?.VegetationInfoList;
            for (int i = 0; infos != null && i < infos.Count; i++)
            {
                var vegetation = infos[i];
                if (vegetation == null || vegetation.VegetationType == VegetationType.Grass || vegetation.VegetationPrefab == null) continue;
                if (string.IsNullOrEmpty(vegetation.Name) || !seen.Add(vegetation.Name)) continue;
                try
                {
                    int id = Id(vegetation.Name);
                    if (ids.Contains(id))
                    {
                        SdkLog.Warn(Landscape.Platform, $"The world's kind '{vegetation.Name}' is not placeable: its id {id} is taken");
                        continue;
                    }
                    var made = MakeKind(vegetation, bySpawn, growth, stone, id);
                    ids.Add(id);
                    s_byName[vegetation.Name] = made;
                    s_byAsset[made.Info.name] = made;
                    s_kinds.Add(made.Data);
                }
                catch (Exception e)
                {
                    failed++;
                    SdkLog.Warn(Landscape.Platform, $"The world's kind '{vegetation.Name}' is not placeable: {e.Message}");
                }
            }
        }
        SdkLog.Info(Landscape.Platform, $"{s_kinds.Count} kinds of the world placeable{(failed > 0 ? $", {failed} failed" : "")}");
    }

    // The id: worked out from the kind's name, the same on every machine, kept positive and clear of the game's.
    private static int Id(string name) => (int)(Fnv1a.OfUtf8("mjolnir.terrain/" + name) & 0x7FFFFFFF) | 0x40000000;

    private static Made MakeKind(VegetationInfo vegetation, Dictionary<IntPtr, ItemInfo> bySpawn, ItemInfo growth, ItemInfo stone, int id)
    {
        var function = vegetation.FunctionPrefab;
        bool harvest = function != null && function.GetComponentInChildren<HarvestInteraction>(true) != null;
        bool gather = function != null && function.GetComponentInChildren<GatherInteraction>(true) != null;
        var source = function != null && bySpawn.TryGetValue(function.Pointer, out var found) ? found : null;
        // What the kind gives is its item kind's first component, as the game reads a world object's resource: a tree's
        // trunk kind, a bush's berries, a clump's stone.
        var gives = source?.components != null && source.components.Length > 0 ? source.components[0] : null;
        string name = Prefix + vegetation.Name;
        GameObject prefab;
        ItemInfo baseInfo;
        ItemInfo? trunk = null;
        if (function != null && (harvest || gather) && function.GetComponentInChildren<BiomeObject>(true) != null)
        {
            // A tree's trunk: what it gives, when that is a world item of the game's that is felled in turn.
            trunk = harvest && gives?.itemInfo is { } wood && wood.TryCast<VegetationResourceInfo>() != null
                    && wood.spawnObject != null && wood.spawnObject.GetComponentInChildren<HarvestInteraction>(true) != null
                ? wood : null;
            prefab = FromFunction(function, name);
            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0) AddModel(prefab, vegetation.VegetationPrefab, "Model");
            if (trunk?.spawnObject != null) AddStump(prefab, trunk.spawnObject);
            baseInfo = trunk ?? growth;
        }
        else
        {
            prefab = Scenery(stone.spawnObject, vegetation, function, name);
            baseInfo = stone;
            harvest = gather = false;
        }
        var info = Object.Instantiate(baseInfo);
        info.name = name;
        info.id = id;
        info.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        info.spawnObject = prefab;
        // What the player sees of it is the world's own: its name, description and lore (the keys, below) and its icon.
        if (source != null)
        {
            s_textsOf[info.Pointer] = source;
            if (source.icon != null) info.icon = source.icon;
            info.previewImage = source.previewImage;
        }
        string? resource = trunk?.name ?? gives?.itemInfo?.name;
        // The tree's own trunk kind carries the tree's parts and points; any other base takes the world kind's.
        if (trunk == null && source?.TryCast<ResourceInfo>() is { } from && info.TryCast<ResourceInfo>() is { } to)
        {
            to.piecesHitpoints = from.piecesHitpoints;
            to.exhaustableComponents = from.exhaustableComponents;
            to.junk = from.junk;
            to.materialConfig = from.materialConfig;
            to.mainHarvestMoveset = from.mainHarvestMoveset;
            to.harvestChanceHasLootRequirements = from.harvestChanceHasLootRequirements;
            to.preferStacksWithSizeOneOnLoot = from.preferStacksWithSizeOneOnLoot;
            to.requiresDiscovery = from.requiresDiscovery;
            to.harvestDiscoversComponents = from.harvestDiscoversComponents;
            if (gather && gives?.itemInfo != null && info.TryCast<VegetationResourceInfo>() is { } vegetal)
            {
                var gathered = new ItemInfoQuantity { itemInfo = gives.itemInfo };
                gathered.quantity = gives.quantity > 0 ? gives.quantity : GatheredCount;
                vegetal.overridenGatherResource = gathered;
            }
        }
        // What the object holds, as its world kind does (a tree its trunk, a clump its stone): its loot spawners drop that.
        if (source?.components != null) info.components = source.components;
        // The world item's own component takes the new kind (without it the game's set-up fails); the object's other item
        // components keep the game's data, which its spawners lay: a felled tree's trunk, lying; a clump's stone. Given the
        // new kind, they laid copies of the object itself (a mined clump left 14 more clumps).
        foreach (var component in prefab.GetComponentsInChildren<WorldItemObject>(true))
        {
            var data = new ItemInfoQuantity { itemInfo = info };
            data.quantity = 1;
            component.data = data;
        }
        var kind = vegetation.VegetationType switch
        {
            VegetationType.Tree => NaturalObjectKind.Tree,
            VegetationType.Objects => NaturalObjectKind.Object,
            VegetationType.LargeObjects => NaturalObjectKind.LargeObject,
            VegetationType.Plant => NaturalObjectKind.Plant,
            _ => NaturalObjectKind.Other,
        };
        var data2 = new PlaceableKindData(vegetation.Name, kind, name, vegetation.MinScale, vegetation.MaxScale, harvest, gather);
        return new Made(info, data2, trunk, resource ?? "", source?.TryCast<BiomeResourceInfo>()) { Model = vegetation.VegetationPrefab, Source = source };
    }

    // A kind the game harvests or gathers: its own object, a world item's now.
    private static GameObject FromFunction(GameObject function, string name)
    {
        var prefab = Object.Instantiate(function, s_holder!.transform);
        prefab.name = name;
        var biome = prefab.GetComponentInChildren<BiomeObject>(true);
        if (biome != null)
        {
            var resource = biome.gameObject.AddComponent<ResourceObject>();
            resource.data = biome.data;
            resource.context = biome.context;
            resource.geometry = biome.geometry;
            resource.interactions = biome.interactions;
            resource.pickables = biome.pickables;
            resource.dataEvents = biome.dataEvents;
            // The spawners of the object's loot (a tree's trunk, a clump's stone) count what to drop from their master
            // item's manifest: the biome object's place goes to the resource object, or they drop nothing.
            foreach (var spawner in prefab.GetComponentsInChildren<SubcomponentSpawner>(true))
                if (spawner.masterItem == null || spawner.masterItem.Pointer == biome.Pointer) spawner.masterItem = resource;
            OutlineAsTheWorld(prefab, biome.geometry);
            Object.DestroyImmediate(biome);
        }
        // The spawners stay, as the game has them: the vegetation spawner lays a felled tree's trunk (lying, from the item
        // kind's first component), the stump's spawner its firewood.
        return prefab;
    }

    // The world's object of a kind is drawn by the vegetation system: its own model (the biome object's geometry) is
    // destroyed when it is set up, so the game outlines it by its picking volume alone, which follows the leaves. The copy
    // keeps the model to be seen, and a pickable outlines its objects' meshes whole: a bush came out as flat cards. So the
    // model is not the pickable's, as in the world.
    private static void OutlineAsTheWorld(GameObject prefab, GameObject? geometry)
    {
        if (geometry == null) return;
        foreach (var pickable in prefab.GetComponentsInChildren<Pickable>(true))
        {
            var objects = pickable.pickableObjects;
            for (int i = (objects?.Count ?? 0) - 1; i >= 0; i--)
            {
                var part = objects![i];
                if (part != null && (part.Pointer == geometry.Pointer || part.transform.IsChildOf(geometry.transform))) objects.RemoveAt(i);
            }
        }
    }

    // A kind of scenery: a raw stone's world item without its look, pick-up or harvest, with the kind's model and colliders.
    private static GameObject Scenery(GameObject stone, VegetationInfo vegetation, GameObject? function, string name)
    {
        var prefab = Object.Instantiate(stone, s_holder!.transform);
        prefab.name = name;
        foreach (var part in prefab.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<DynamicItemObject>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<PickupInteraction>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<HarvestInteraction>(true)) Object.DestroyImmediate(part);
        foreach (var part in prefab.GetComponentsInChildren<ItemSpawner>(true)) Object.DestroyImmediate(part);
        foreach (var body in prefab.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var item in prefab.GetComponentsInChildren<ItemObject>(true)) item.interactions = new Il2CppSystem.Collections.Generic.List<Interaction>();
        AddModel(prefab, vegetation.VegetationPrefab, "Model");
        int colliders = 0;
        if (function != null)
            foreach (var collider in function.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh == null) continue;
                var holder = new GameObject("Collision");
                holder.transform.SetParent(prefab.transform, false);
                holder.transform.localPosition = collider.transform.position - function.transform.position;
                holder.transform.localRotation = collider.transform.rotation;
                holder.transform.localScale = collider.transform.lossyScale;
                holder.AddComponent<MeshCollider>().sharedMesh = collider.sharedMesh;
                colliders++;
            }
        if (colliders == 0 && vegetation.ColliderMesh != null) prefab.AddComponent<MeshCollider>().sharedMesh = vegetation.ColliderMesh;
        return prefab;
    }

    private static void AddModel(GameObject prefab, GameObject model, string name)
    {
        var copy = Object.Instantiate(model, prefab.transform);
        copy.name = name;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;
    }

    // A tree's stump: the stump model its trunk kind's object carries, shown once the tree is felled.
    private static void AddStump(GameObject prefab, GameObject trunk)
    {
        foreach (var renderer in trunk.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.name.Contains("stump", StringComparison.OrdinalIgnoreCase)) continue;
            var copy = Object.Instantiate(renderer.gameObject, prefab.transform);
            copy.name = "Stump";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            copy.SetActive(false);
            return;
        }
    }

    /// <summary>Whether a harvest is a placed object's.</summary>
    public static bool IsOurs(HarvestInteraction harvest)
    {
        var item = harvest.GetComponentInParent<ResourceObject>();
        var name = item?.data?.itemInfo?.name;
        return name != null && s_byAsset.ContainsKey(name);
    }

    /// <summary>A placed tree shows its tree while it stands and its stump once it is felled.</summary>
    public static void Show(HarvestInteraction harvest)
    {
        var root = harvest.GetComponentInParent<ResourceObject>();
        if (root == null) return;
        var stump = root.transform.Find("Stump");
        if (stump == null) return;
        bool felled = harvest.GetCurrentPieceIndex() != 0;
        var model = root.transform.Find("Model");
        if (model != null && model.gameObject.activeSelf == felled) model.gameObject.SetActive(!felled);
        if (stump.gameObject.activeSelf != felled) stump.gameObject.SetActive(felled);
    }

    /// <summary>The game's item kind whose texts a placed kind shows; null for any other item kind.</summary>
    public static ItemInfo? TextsOf(IntPtr info) => s_textsOf.TryGetValue(info, out var source) ? source : null;

    /// <summary>A blow landed on a placed object: a tree just felled shows its stump (the game lays its trunk itself).</summary>
    public static void AfterDamage(HarvestInteraction harvest) => Show(harvest);

    private static int IndexOf(Il2CppSystem.Collections.Generic.List<ItemInfo> list, ItemInfo info)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] != null && list[i].Pointer == info.Pointer) return i;
        return -1;
    }

    /// <summary>A kind made: its item kind, what the SDK says of it, a tree's trunk kind, what it gives, the world's kind.</summary>
    internal sealed record Made(ItemInfo Info, PlaceableKindData Data, ItemInfo? Trunk, string Resource, BiomeResourceInfo? World)
    {
        /// <summary>The vegetation system's model of the kind: what its pictures are taken of.</summary>
        public GameObject? Model { get; init; }

        /// <summary>The game's item kind of the world's objects of this kind (their name in the player's language).</summary>
        public ItemInfo? Source { get; init; }
    }
}

/// <summary>
/// The placed kinds' hooks: the item database's build (the kinds go into its list first), a placed kind's texts' keys (the
/// world kind's), a placed tree's set-up (it shows
/// its tree or its stump, as its harvest part says) and a blow on it (a tree just felled drops the game's trunk). Each
/// method is the game's own; another prefix on the database's build (the content module's) runs beside this one.
/// </summary>
[HarmonyPatch]
internal static class PlacementPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemInfoDatabase), nameof(ItemInfoDatabase.Initialize))]
    private static void BeforeDatabase(ItemInfoDatabase __instance)
    {
        try
        {
            GamePlacement.BeforeDatabase(__instance);
        }
        catch (Exception e)
        {
            SdkLog.Error(Landscape.Platform, $"The kinds of the world to place could not be made: {e}");
        }
    }

    // A placed kind's texts are the world kind's: the game asks the keys of an item kind's name, description and lore here
    // (its name and description ask on every read), and a placed kind's own asset name has none.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemInfo), nameof(ItemInfo.GetKey))]
    private static bool GetKey(ItemInfo __instance, string token, ref string __result)
    {
        var source = GamePlacement.TextsOf(__instance.Pointer);
        if (source == null) return true;
        try
        {
            __result = source.GetKey(token);
            return false;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A placed kind's text '{token}' could not be read from its world kind: {e.Message}");
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HarvestInteraction), nameof(HarvestInteraction.SetWorldInstance))]
    private static void AfterWorldInstance(HarvestInteraction __instance)
    {
        try
        {
            if (!GamePlacement.IsOurs(__instance)) return;
            GamePlacement.Show(__instance);
            GameRegrowth.AfterTreeSetup(__instance);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A placed object could not show its part: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HarvestInteraction), nameof(HarvestInteraction._TakeDamageInternal))]
    private static void AfterDamage(HarvestInteraction __instance)
    {
        try
        {
            if (GamePlacement.IsOurs(__instance)) GamePlacement.AfterDamage(__instance);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A placed tree could not be felled as the game fells one: {e.Message}");
        }
    }
}
