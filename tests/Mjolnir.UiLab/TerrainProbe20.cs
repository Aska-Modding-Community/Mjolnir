using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe20" scenario: the willow kind of probe19 made from the willow's own trunk kind, its data events read,
/// and one of its own added that hides the tree's model once its tree part is harvested; two willows put down, one felled
/// (the trunk and the stump looked for), then a save and a load with each willow's place read exactly.
/// </summary>
internal static partial class Autopilot
{
    private const int Probe20Id = 0x4D4A5704;
    private const int Probe20BareId = 0x4D4A5705;

    private static IEnumerator<float> TerrainProbe20()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe20: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        string Parts(GameObject o) => string.Join(", ", o.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(24));

        // The willow's vegetation info, from a willow of the world.
        var willowSpot = new Vector3(-200.9f, 34.4f, 394.5f);
        var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(willowSpot.x - 8, willowSpot.z - 8);
        foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var willow = Biome(willowSpot, 2f).FirstOrDefault(b => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name == "tree_willow");
        var vegetation = willow?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo();
        Log($"Probe20: the willow of the world {(willow != null ? P(willow.GetPosition()) : "none")}: vegetation prefab '{(vegetation?.VegetationPrefab != null ? vegetation.VegetationPrefab.name : "none")}' ({vegetation?.PrefabType}), function prefab '{(vegetation?.FunctionPrefab != null ? vegetation.FunctionPrefab.name : "none")}', scale {vegetation?.MinScale}–{vegetation?.MaxScale}, collider {vegetation?.ColliderType}");
        if (vegetation?.VegetationPrefab == null)
        {
            Log("Probe20: no willow model");
            yield break;
        }
        Log($"Probe20: the willow model's parts [{Parts(vegetation.VegetationPrefab)}], renderers {vegetation.VegetationPrefab.GetComponentsInChildren<Renderer>(true).Length}");

        // The kind: a trunk's item copied, with the willow's object and model.
        var baseInfo = db.GetItemInfo("Item_Wood_Willow");
        var willowInfo = db.GetItemInfo("Item_Wood_TreeWillow");
        var list = db.CompleteItemInfoList.itemInfoList;
        var willowBiome = willowInfo.spawnObject.GetComponentInChildren<BiomeObject>(true);
        if (willowBiome?.dataEvents?.dataEvents is { } events)
        {
            Log($"Probe20: the willow's data events: setup listeners {willowBiome.dataEvents.onSetupVegetationObject?.GetPersistentEventCount()}, set-data listeners {willowBiome.dataEvents.onSetData?.GetPersistentEventCount()}, {events.Count} by data");
            for (int e = 0; e < events.Count; e++)
            {
                var ev = events[e];
                int count = ev.onDataEvent?.GetPersistentEventCount() ?? 0;
                string targets = string.Join("; ", Enumerable.Range(0, count).Select(k => $"{ev.onDataEvent.GetPersistentTarget(k)?.name}.{ev.onDataEvent.GetPersistentMethodName(k)}"));
                Log($"Probe20:   {ev.data} {ev.invokeRule} negate {ev.negateOutputValue}: [{targets}]");
            }
        }
        Log($"Probe20: the base kind {baseInfo?.name} ({baseInfo?.GetIl2CppType().Name}), its object '{(baseInfo?.spawnObject != null ? baseInfo.spawnObject.name : "none")}', its harvest pieces {baseInfo?.spawnObject?.GetComponentInChildren<HarvestInteraction>(true)?.harvestPieces?.Count}; the willow's {willowInfo.spawnObject.GetComponentInChildren<HarvestInteraction>(true)?.harvestPieces?.Count}");
        ItemInfo? Make(string name, int id, bool spawners, out string report)
        {
            ItemInfo? made = null;
            report = Try(() =>
            {
                var holder = new GameObject("Mjolnir_Probe20_Prefabs_" + name);
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                var prefab = Object.Instantiate(willowInfo.spawnObject, holder.transform);
                prefab.name = name;
                var biome = prefab.GetComponentInChildren<BiomeObject>(true);
                var holderOf = biome != null ? biome.gameObject : prefab;
                var resource = holderOf.AddComponent<ResourceObject>();
                if (biome != null)
                {
                    resource.data = biome.data;
                    resource.context = biome.context;
                    resource.geometry = biome.geometry;
                    resource.interactions = biome.interactions;
                    resource.pickables = biome.pickables;
                    resource.dataEvents = biome.dataEvents;
                    Object.DestroyImmediate(biome);
                }
                if (!spawners)
                {
                    foreach (var sp in prefab.GetComponentsInChildren<SubcomponentSpawner>(true)) Object.DestroyImmediate(sp);
                    foreach (var sp in prefab.GetComponentsInChildren<VegetationItemSpawner>(true)) Object.DestroyImmediate(sp);
                }
                var model = Object.Instantiate(vegetation.VegetationPrefab, prefab.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                var info = Object.Instantiate(baseInfo);
                info.name = name;
                info.id = id;
                info.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                info.spawnObject = prefab;
                foreach (var component in prefab.GetComponentsInChildren<ItemComponent>(true))
                {
                    var quantity = new ItemInfoQuantity { itemInfo = info };
                    quantity.quantity = 1;
                    component.data = quantity;
                }
                list.Add(info);
                info.databaseIndex = (ushort)(list.Count - 1);
                db._itemsMap[info.id] = info;
                info.Configure();
                made = info;
                return $"made: index {info.databaseIndex}, by id {db.GetItemInfoFromID(id)?.name}; prefab [{Parts(prefab)}]";
            });
            return made;
        }
        var kind = Make("Mjolnir_Tree_Willow", Probe20Id, true, out string made);
        var bare = Make("Mjolnir_Tree_WillowBare", Probe20BareId, false, out string madeBare);
        Log($"Probe20: the bare kind {madeBare}");
        Log($"Probe20: the kind {made}");
        if (kind == null) yield break;

        // A free spot.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 24, 14, blend: 0);
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
            Log("Probe20: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 18f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 12f, -24f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.up * 5 - camera.Position, Vector3.up);

        var placed = new List<(float Scale, Vector3 At)>();
        foreach (float size in new[] { 1f, 0.5f, 0.8f })
        {
            var at = spot + new Vector3(size == 1f ? -7f : size == 0.5f ? 0f : 7f, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned;
            try
            {
                spawned = Spawn();
            }
            catch (Exception e)
            {
                spawned = string.Join(" | ", e.ToString().Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries));
            }
            string Spawn()
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.identity;
                var scale = Vector3.one * size;
                var which = size == 1f ? kind : bare;
                var one = which!.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? $"{one.name}, renderers {one.GetComponentsInChildren<Renderer>(true).Length}, scale {one.transform.localScale.y}" : "null";
            }
            placed.Add((size, at));
            Log($"Probe20: a willow at scale {size} put down at {P(at)}: {spawned}");
        }
        yield return 5f;
        foreach (var (size, at) in placed)
        {
            var one = Items(at, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_Tree_Willow", StringComparison.Ordinal) == true);
            var harvest = one?.gameObject != null ? one.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
            Log($"Probe20: the willow at scale {size} 5 s later: world item {one != null}, its scale {one?.GetScale().y:0.##}, object '{(one?.gameObject != null ? one.gameObject.name : "none")}', active {one?.gameObject?.activeInHierarchy}, renderers {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Renderer>().Length : 0)}, harvest pieces {harvest?.harvestPieces?.Count}, piece {Try(() => harvest?.GetCurrentPieceIndex())}, points {Try(() => harvest?.GetHarvestHitPoints())}; SDK {NaturalObjects.Nearest(at, 1.5f)?.ToString() ?? "none"}; others round it [{string.Join(", ", Items(at, 6).Select(i => i.Descriptor?.itemInfo?.name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"))}]");
        }
        Shot("probe20-placed");
        yield return AfterShot;

        // The small one harvested.
        foreach (var cut in placed.Where(p => p.Scale != 0.8f))
        {
            string before = string.Join(", ", NaturalObjects.Around(cut.At, 10).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.StartsWith("Mjolnir_", StringComparison.Ordinal)).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));
            if (NaturalObjects.Nearest(cut.At, 1.5f) is { } sdk && sdk.AssetName.StartsWith("Mjolnir_Tree_Willow", StringComparison.Ordinal))
            {
                var mine = Items(cut.At, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_Tree_Willow", StringComparison.Ordinal) == true);
                string Data() => mine == null ? "none" : $"model active {mine.gameObject?.transform.Find("Model")?.gameObject.activeSelf}, piece {Try(() => mine.gameObject?.GetComponentInChildren<HarvestInteraction>(true)?.GetCurrentPieceIndex())}";
                Log($"Probe20: the willow at scale {cut.Scale} before: {Data()}");
                bool done = sdk.Harvest();
                Log($"Probe20: the willow at scale {cut.Scale} right after: {Data()}");
                yield return 8f;
                Log($"Probe20: the willow at scale {cut.Scale} 8 s after: {Data()}, destroyed {mine?.Destroyed}");
                Log($"Probe20: the willow at scale {cut.Scale} ({sdk.AssetName}) harvested: {done}; items round it before [{before}], after [{string.Join(", ", NaturalObjects.Around(cut.At, 10).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.StartsWith("Mjolnir_", StringComparison.Ordinal)).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"))}]");
            }
            else Log($"Probe20: the willow at scale {cut.Scale}: not found to harvest");
        }
        Shot("probe20-harvested");
        yield return AfterShot;

        // Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Probe20: saved, {storage.LastSaveOperationCode}");
            camera.Dispose();
            MainMenuReady = false;
            HudReady = false;
            Menu.Show<GamePauseMenu>(null);
            yield return 1.5f;
            Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
            yield return 1.5f;
            if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
            yield return 3f;
            foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
            yield return 10f;
            var again = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
            foreach (float wait in WaitFor("the teleport", () => again.IsCompleted, 30)) yield return wait;
            yield return 4f;
            var db2 = GameManagers.Get<ItemDatabaseManager>()?.Database;
            Log($"Probe20: after loading again the database knows the kinds: {db2?.GetItemInfoFromID(Probe20Id)?.name ?? "no"}, {db2?.GetItemInfoFromID(Probe20BareId)?.name ?? "no"}");
            var big = placed.First(p => p.Scale == 0.8f);
            foreach (var p in placed)
            {
                var it = Items(p.At, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_", StringComparison.Ordinal) == true);
                Log($"Probe20: after loading again at the willow of scale {p.Scale}: {it?.Descriptor?.itemInfo?.name ?? "nothing of ours"}, scale {it?.GetScale().y:0.##}, piece {Try(() => it?.gameObject?.GetComponentInChildren<HarvestInteraction>(true)?.GetCurrentPieceIndex())}, object '{(it?.gameObject != null ? it.gameObject.name : "none")}', model active {it?.gameObject?.transform.Find("Model")?.gameObject.activeSelf}");
            }
            var one = Items(big.At, 1.5f).FirstOrDefault();
            Log($"Probe20: after loading again at the big willow's place: {one?.Descriptor?.itemInfo?.name ?? "nothing"}, scale {one?.GetScale().y:0.##}, object '{(one?.gameObject != null ? one.gameObject.name : "none")}', renderers {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Renderer>().Length : 0)}");
            var back = Overhead(spot);
            back.Position = spot + new Vector3(0, 12f, -24f);
            back.Rotation = Quaternion.LookRotation(spot + Vector3.up * 5 - back.Position, Vector3.up);
            yield return 1f;
            Shot("probe20-reloaded");
            yield return AfterShot;
            back.Dispose();
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
