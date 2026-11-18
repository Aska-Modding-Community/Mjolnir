using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe15" scenario: putting the world's objects down from code. It lists the item database's kinds whose
/// object the game puts in the world (what each one's object carries: a world item, a harvest, a gathering), then on a
/// free spot puts down one of a few kinds — a tree, a stone, a bush, a sapling — as the game drops loot, and reads what
/// stands there a moment later; it grows the sapling through its own growth process and harvests what it can. It changes
/// the save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe15()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe15: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();

        // A. What the database can put in the world.
        var all = db.CompleteItemInfoList.itemInfoList.ToArray().Where(i => i != null && i.spawnObject != null).ToList();
        string Parts(ItemInfo info)
        {
            var o = info.spawnObject;
            var flags = new List<string>();
            if (o.GetComponentInChildren<WorldItemObject>(true) != null) flags.Add("world item");
            if (o.GetComponentInChildren<HarvestInteraction>(true) != null) flags.Add("harvest");
            if (o.GetComponentInChildren<GatherInteraction>(true) != null) flags.Add("gather");
            if (o.GetComponentInChildren<BiomeObject>(true) != null) flags.Add("biome object");
            return string.Join("+", flags);
        }
        var kinds = all.Select(i => (Info: i, Type: i.GetIl2CppType().Name, Parts: Parts(i))).Where(k => k.Parts.Contains("harvest") || k.Parts.Contains("gather") || k.Type is "BiomeResourceInfo" or "VegetationResourceInfo" or "PlantableItemInfo").ToList();
        foreach (var group in kinds.GroupBy(k => $"{k.Type} [{k.Parts}]"))
            Log($"Probe15: {group.Key}: {group.Count()} — {string.Join(", ", group.Take(40).Select(k => $"{k.Info.name} ('{k.Info.spawnObject.name}')"))}");

        // B. A free spot.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 24, 12, blend: 0);
                var m = Landscape.Measure(square);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 3f || Landscape.Check(square, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                if (NaturalObjects.In(square).Any(o => o.Kind is NaturalObjectKind.Tree or NaturalObjectKind.LargeObject)) continue;
                spot = at;
                found = true;
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe15: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 10f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 14f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);

        // C. One of each kind put down as the game drops loot.
        ItemInfo? Pick(Func<(ItemInfo Info, string Type, string Parts), bool> where) => kinds.Where(where).Select(k => k.Info).FirstOrDefault();
        var picks = new (string What, ItemInfo? Info)[]
        {
            ("a tree resource", Pick(k => k.Type == "BiomeResourceInfo" && k.Info.name.Contains("Tree"))),
            ("a world item tree or trunk", Pick(k => k.Parts.Contains("world item") && k.Parts.Contains("harvest") && k.Info.name.StartsWith("Item_Wood_", StringComparison.Ordinal))),
            ("a world item stone", Pick(k => k.Parts.Contains("world item") && k.Parts.Contains("harvest") && k.Info.name.Contains("Stone"))),
            ("a gathered bush", Pick(k => k.Parts.Contains("gather") && k.Info.name.Contains("Berr"))),
            ("a sapling", Pick(k => k.Type == "PlantableItemInfo" && k.Info.name.Contains("Birch") && k.Info.name.Contains("Sapling"))),
        };
        var placed = new List<(string What, ItemInfo Info, Vector3 At)>();
        int n = 0;
        foreach (var (what, info) in picks)
        {
            if (info == null)
            {
                Log($"Probe15: {what}: none in the database");
                continue;
            }
            var at = spot + new Vector3(-10 + 5 * n++, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned = Try(() =>
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.identity;
                var scale = Vector3.one;
                var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? one.name : "null";
            });
            placed.Add((what, info, at));
            Log($"Probe15: {what} '{info.name}' put down at {P(at)}: {spawned}");
        }
        yield return 4f;
        foreach (var (what, info, at) in placed)
        {
            var items = Items(at, 1.2f);
            var biome = Biome(at, 1.2f);
            var sdk = NaturalObjects.Nearest(at, 1.2f);
            Log($"Probe15: {what} at {P(at)} 4 s later: world items [{string.Join(", ", items.Select(i => $"{i.Descriptor?.itemInfo?.name} scale {i.GetScale().y:0.##}"))}], objects [{string.Join(", ", biome.Select(b => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name))}]; SDK {sdk?.ToString() ?? "none"} (harvestable {sdk?.IsHarvestable})");
        }
        Shot("probe15-placed");
        yield return AfterShot;

        // D. The sapling grown through its own process.
        var sapling = placed.FirstOrDefault(p => p.What == "a sapling");
        if (sapling.Info != null && Items(sapling.At, 1.2f).FirstOrDefault() is { } young)
        {
            var item = young.GetItem();
            var growth = sapling.Info.processes?.ToArray().Select(p => p?.TryCast<SaplingGrowthProcess>()).FirstOrDefault(p => p != null);
            Log($"Probe15: the sapling's processes [{string.Join(", ", sapling.Info.processes?.ToArray().Select(p => p?.GetIl2CppType().Name) ?? [])}], item {item != null}, growth {growth != null}: scale range {growth?.saplingScaleRange}, mature flag {growth?.matureTreeFlag}, scale now {young.GetScale().y:0.##}");
            if (growth != null && item != null)
            {
                foreach (float dt in new[] { 60f, 600f, 6000f, 60000f, 600000f })
                {
                    string ran = Try(() =>
                    {
                        float d = dt;
                        growth.Run(item, ref d);
                        return "";
                    });
                    yield return 1f;
                    var now = Items(sapling.At, 1.5f).FirstOrDefault();
                    Log($"Probe15: the sapling run {dt} s: {ran} scale {now?.GetScale().y:0.###}, its name {now?.Descriptor?.itemInfo?.name}, SDK {NaturalObjects.Nearest(sapling.At, 1.5f)?.ToString() ?? "none"}");
                }
            }
        }
        Shot("probe15-grown");
        yield return AfterShot;

        // E. Harvest what was put down.
        foreach (var (what, info, at) in placed)
        {
            var sdk = NaturalObjects.Nearest(at, 1.5f);
            string before = string.Join(", ", NaturalObjects.Around(at, 6).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));
            bool done = sdk?.Harvest() ?? false;
            yield return 3f;
            Log($"Probe15: {what}: Harvest {done}; items round it before [{before}], after [{string.Join(", ", NaturalObjects.Around(at, 6).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"))}]");
        }
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
