using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using SandSailorStudio.Inventory;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The game side of <see cref="Buildings"/>: a building is a copy of a game structure template with the mod's settings, and
/// its cost, when the mod gives one, an item config of the mod's own per construction stage on its build site's parts and in
/// the build menu (hidden patches, <see cref="ItemPatches"/>).
/// </summary>
internal static class GameBuildings
{
    // A mod building's own supplies, by its id: a config per construction stage (null for a stage that costs nothing) and
    // what dismantling it gives back, made for one version of the building.
    private static readonly Dictionary<int, Cost> s_costs = [];
    private static ItemManifestConfig? s_noCost;
    private static readonly Dictionary<string, ItemCategoryInfo?> s_categories = [];

    // A mod's building: a copy of its base structure template (its model, placement, stages and function), or of the copy
    // made for the mod's building it stands on, made once per game, with the mod's id, asset name, picture, texts and
    // settings: a cost of its own per stage, its category and whether the ground is levelled first. Its id is its own: the
    // registry leaves out content whose id another took.
    public static ItemInfo? Make(ItemIndex index, ModBuilding building, int depth = 0)
    {
        if (!GameItems.Made.TryGetValue(building.Id, out var made) || made == null)
        {
            var template = building.BaseBuilding != null && depth < 16
                ? Make(index, building.BaseBuilding, depth + 1)?.TryCast<NodeTemplate>()
                : index.Find(building.BasedOn)?.TryCast<NodeTemplate>();
            if (template == null)
            {
                ContentLog.WarnOnce($"{building}: no structure '{building.BasedOn}' to make it from");
                return null;
            }
            var copy = Object.Instantiate(template);
            copy.name = building.AssetName;
            copy.id = building.Id;
            copy.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (building.IconPath != null && ContentPictures.Picture(building.IconPath) is { } sprite)
            {
                copy.icon = sprite;
                copy.previewImage = sprite;
            }
            GameItems.Made[building.Id] = made = copy;
            ContentTexts.Add(ModStringsRegistry.ForModId(building.ModId), "building", building.Name, copy);
        }
        var node = made.TryCast<NodeTemplate>();
        if (node == null) return null;
        SetCost(index, building, node);
        SetCategory(building, node);
        SetPlacement(index.List, building, node);
        BuildingPrefabs.Use(building, node);
        return made;
    }

    // Its build menu category: the game's of that asset name, else the one it was made with.
    private static void SetCategory(ModBuilding building, NodeTemplate made)
    {
        if (building.Category is not { } name || made.category?.name == name) return;
        if (!s_categories.TryGetValue(name, out var category) || category == null)
        {
            category = null;
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ItemCategoryInfo>());
            for (int i = 0; i < all.Length && category == null; i++)
            {
                if (all[i]?.name == name) category = all[i].TryCast<ItemCategoryInfo>();
            }
            s_categories[name] = category;
        }
        if (category != null) made.category = category;
        else ContentLog.WarnOnce($"{building}: the game has no category '{name}', it keeps its base's");
    }

    // Whether the ground is levelled first is the placement tool's: a terraforming one places a plan dug with a hoe, which
    // creates the structure once the ground is level; the others create it at once. The tool is a shared asset of the game's,
    // so a building takes one of a game structure that places as wanted.
    private static void SetPlacement(Il2CppSystem.Collections.Generic.List<ItemInfo> list, ModBuilding building, NodeTemplate made)
    {
        if (building.LevelsGround is not { } levels) return;
        bool now = made.placementTool != null && Levels(made.placementTool);
        if (now == levels) return;
        string wanted = levels ? "TerraformingPointPlacementTool" : "AnchorPointPlacementTool";
        for (int i = 0; i < list.Count; i++)
        {
            var tool = list[i]?.TryCast<NodeTemplate>()?.placementTool;
            if (tool == null || tool.GetIl2CppType().Name != wanted) continue;
            made.placementTool = tool;
            return;
        }
        ContentLog.WarnOnce($"{building}: no game structure places with a {wanted}, it places as its base");
    }

    private static bool Levels(PlacementTool tool) => tool.GetIl2CppType().Name.StartsWith("Terraforming", StringComparison.Ordinal);

    // A building's own cost: an item config of the mod's per construction stage (the kind of asset the game's build parts
    // take theirs from), or none to cost what its base costs; the same configs are what dismantling it gives back of. They
    // are made once for each version of the building: parts added later make them again. The configs they replace are left
    // as they are, since a build site started before the change holds them on its parts.
    private static void SetCost(ItemIndex index, ModBuilding building, NodeTemplate made)
    {
        int stages = building.StagesWithParts;
        if (stages == 0)
        {
            s_costs.Remove(building.Id);
            return;
        }
        int version = building.Version;
        if (s_costs.TryGetValue(building.Id, out var known) && known.Version == version && known.Refund != null)
        {
            made.dismantlePartsConfig = known.Refund;
            return;
        }
        var configs = new ItemManifestConfig?[stages];
        for (int stage = 0; stage < stages; stage++)
        {
            var parts = building.PartsAt(stage);
            if (parts.Count == 0) continue;
            var items = new Il2CppSystem.Collections.Generic.List<ItemInfoQuantity>();
            foreach (var part in parts)
            {
                if (GameItems.Quantity(index, part, building, "cost") is not { } quantity)
                {
                    s_costs.Remove(building.Id);
                    return;
                }
                items.Add(quantity);
            }
            configs[stage] = Config(items);
        }
        var refund = ScriptableObject.CreateInstance(Il2CppType.Of<BuildPartConfig>()).Cast<BuildPartConfig>();
        refund.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        refund.name = building.AssetName + "_Parts";
        refund.subparts = new Il2CppSystem.Collections.Generic.List<ItemManifestConfig>();
        foreach (var config in configs)
        {
            if (config != null) refund.subparts.Add(config);
        }
        s_costs[building.Id] = new Cost(version, configs, refund);
        made.dismantlePartsConfig = refund;
    }

    private static ItemManifestConfig Config(Il2CppSystem.Collections.Generic.List<ItemInfoQuantity> items)
    {
        var config = ScriptableObject.CreateInstance(Il2CppType.Of<ItemManifestConfig>()).Cast<ItemManifestConfig>();
        config.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        config.items = items;
        config._ResetAsset(); // the config keeps a manifest made from its items
        return config;
    }

    /// <summary>
    /// The hidden prefix on a build site's start, before its parts take their costs: a mod building with a cost of its own
    /// gets each stage's on the first part of that stage (a stage's other parts and a stage without parts cost nothing),
    /// and its parts the work it asks for.
    /// </summary>
    public static void BeforeBuildSite(BuildSite site, Structure? structure)
    {
        if (structure == null) return;
        int id = structure.TemplateID;
        if (id >= 0) return; // the game's own: a mod's ids are negative
        var building = ContentRegistry.Shared.BuildingById(id);
        var layers = site.layers;
        if (layers == null || building == null) return;
        var costs = s_costs.GetValueOrDefault(id)?.Stages;
        if (costs != null) s_noCost ??= Config(new Il2CppSystem.Collections.Generic.List<ItemInfoQuantity>());
        if (costs != null && costs.Length > layers.Count)
            ContentLog.WarnOnce($"{building}: supplies for {costs.Length} stages, its base has {layers.Count}: the stages after it are left out");
        float? effort = building.BuildEffort;
        for (int i = 0; i < layers.Count; i++)
        {
            var parts = layers[i]?.parts;
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var part = parts[k];
                if (part == null) continue;
                if (costs != null && part.container != null) part.container.itemConfig = k == 0 && i < costs.Length && costs[i] != null ? costs[i] : s_noCost;
                if (effort is { } work && (part.buildInteraction ?? part.GetComponent<BuildInteraction>()) is { } build) build.buildVolume = work;
            }
        }
    }

    /// <summary>
    /// The hidden prefix on a structure template's cost, as the build menu shows it: a mod building's own cost instead of
    /// its base's; false when the game's own is to be filled.
    /// </summary>
    public static bool FillCost(NodeTemplate template, ItemManifest manifest)
    {
        if (!s_costs.TryGetValue(template.id, out var cost)) return false;
        foreach (var stage in cost.Stages)
        {
            for (int i = 0; stage != null && i < stage.items.Count; i++)
            {
                var part = stage.items[i];
                if (part?.itemInfo != null) manifest.AddItem(part.itemInfo, part.quantity);
            }
        }
        return true;
    }

    private sealed record Cost(int Version, ItemManifestConfig?[] Stages, BuildPartConfig Refund);
}
